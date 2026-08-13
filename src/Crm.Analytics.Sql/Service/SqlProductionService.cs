using Crm.Analytics.Sql.Audit;
using Crm.Analytics.Sql.Contracts;
using Crm.Analytics.Sql.Nlu;
using Crm.Analytics.Sql.Routing;

namespace Crm.Analytics.Sql.Service;

public interface ISqlProductionService
{
    SqlProductionResponse Produce(SqlProductionRequest request);

    /// <summary>
    /// Produces SQL from an externally planned canonical request. The request still enters
    /// the same catalog, ambiguity, Query Builder, scope and guardrail pipeline as a
    /// deterministically parsed request.
    /// </summary>
    SqlProductionResponse ProduceCanonical(
        SqlProductionRequest request,
        CanonicalRequest canonical) =>
        throw new NotSupportedException(
            "This SQL production service does not accept external canonical requests.");
}

/// <summary>
/// Source determination happens before SQL production. Each parser, builder, prompt and
/// guardrail receives exactly one immutable source catalog.
/// </summary>
internal sealed class SqlProductionService(
    IReadOnlyDictionary<DataSource, SqlSourceRuntime> runtimes,
    IDecisionAuditWriter auditWriter) : ISqlProductionService
{
    public SqlProductionResponse Produce(SqlProductionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var (canonical, runtime, failure) = Resolve(request);
        if (failure is not null)
        {
            return failure;
        }

        var routing = runtime!.Router.Produce(
            canonical!, request.Scope, request.Prompt, request.UserId);
        var guardrail = routing.Guardrail;

        return new SqlProductionResponse
        {
            RequestId = request.RequestId,
            Decision = guardrail.Decision,
            Sql = guardrail.Sql,
            Parameters = guardrail.Parameters,
            AppliedScopeFilter = guardrail.AppliedScopeFilter,
            CommandTimeoutSeconds = guardrail.QueryTimeoutSeconds,
            RowLimit = guardrail.RowLimit,
            PhysicalObject = guardrail.PhysicalObject,
            Source = guardrail.Source,
            ReasonCode = guardrail.ReasonCode,
            UserMessage = guardrail.Decision == GuardrailDecision.NeedsClarification
                ? SemanticClarificationQuestionBuilder.Build(
                    guardrail.ReasonCode, canonical, canonical!.UnresolvedTerms)
                : guardrail.ReasonMessage,
            ResultShape = guardrail.Decision == GuardrailDecision.Accepted
                ? ResultShapeClassifier.Classify(canonical!, runtime.Catalog)
                : null,
            CanonicalRequest = canonical,
            Path = routing.Path,
            Checks = guardrail.Checks,
            UnresolvedTerms = canonical!.UnresolvedTerms
        };
    }

    public SqlProductionResponse ProduceCanonical(
        SqlProductionRequest request,
        CanonicalRequest canonical)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(canonical);

        if (!string.Equals(canonical.RequestId, request.RequestId, StringComparison.Ordinal)
            || !string.Equals(canonical.ConversationId, request.ConversationId,
                StringComparison.Ordinal))
        {
            return Clarify(request, ReasonCode.CL001, [], canonical);
        }

        var selectedSource = request.Source ?? canonical.Source;
        if (selectedSource is null
            || canonical.Source != selectedSource
            || !runtimes.TryGetValue(selectedSource.Value, out var runtime)
            || !runtime.Supports(canonical))
        {
            return Clarify(request, ReasonCode.CL001, canonical.UnresolvedTerms, canonical);
        }

        var routing = runtime.Router.Produce(
            canonical, request.Scope, rawPrompt: null, request.UserId);
        var guardrail = routing.Guardrail;
        return new SqlProductionResponse
        {
            RequestId = request.RequestId,
            Decision = guardrail.Decision,
            Sql = guardrail.Sql,
            Parameters = guardrail.Parameters,
            AppliedScopeFilter = guardrail.AppliedScopeFilter,
            CommandTimeoutSeconds = guardrail.QueryTimeoutSeconds,
            RowLimit = guardrail.RowLimit,
            PhysicalObject = guardrail.PhysicalObject,
            Source = guardrail.Source,
            ReasonCode = guardrail.ReasonCode,
            UserMessage = guardrail.Decision == GuardrailDecision.NeedsClarification
                ? SemanticClarificationQuestionBuilder.Build(
                    guardrail.ReasonCode, canonical, canonical.UnresolvedTerms)
                : guardrail.ReasonMessage,
            ResultShape = guardrail.Decision == GuardrailDecision.Accepted
                ? ResultShapeClassifier.Classify(canonical, runtime.Catalog)
                : null,
            CanonicalRequest = canonical,
            Path = routing.Path,
            Checks = guardrail.Checks,
            UnresolvedTerms = canonical.UnresolvedTerms
        };
    }

    private (CanonicalRequest? Canonical, SqlSourceRuntime? Runtime, SqlProductionResponse? Failure)
        Resolve(SqlProductionRequest request)
    {
        var input = new RequestParseInput(
            request.Prompt,
            request.RequestId,
            request.ConversationId,
            request.Today,
            request.PreviousRequest?.RequestId);

        if (request.PreviousRequest is null)
        {
            return ResolveNew(request, input);
        }

        var previous = request.PreviousRequest;
        var previousSource = previous.Source ?? InferSource(previous);
        if (request.Source is { } explicitSource
            && previousSource is { } knownPrevious
            && explicitSource != knownPrevious)
        {
            if (!runtimes.TryGetValue(explicitSource, out var newRuntime))
            {
                return (null, null, Clarify(request, ReasonCode.CL001, []));
            }

            // Cross-source revision is treated as a full request. No object/metric from the
            // previous source is copied into the new source context.
            var reparsed = newRuntime.Parser.Parse(input);
            if (!reparsed.IsSuccessful)
            {
                return (null, null, Clarify(
                    request, reparsed.ReasonCode, reparsed.UnresolvedTerms));
            }

            var crossSource = reparsed.Request! with
            {
                PreviousRequestId = previous.RequestId,
                Source = explicitSource
            };
            return (crossSource, newRuntime, null);
        }

        var selectedSource = request.Source ?? previousSource;
        if (selectedSource is null || !runtimes.TryGetValue(selectedSource.Value, out var runtime))
        {
            return (null, null, Clarify(request, ReasonCode.CL001, []));
        }

        var revision = runtime.Parser.ParseRevision(input);
        if (!revision.IsSuccessful)
        {
            return (null, null, Clarify(
                request, revision.ReasonCode, revision.UnresolvedTerms));
        }

        var revised = CanonicalRequestReviser.Apply(
            previous,
            revision.Delta! with { Source = selectedSource },
            request.RequestId) with
        {
            Source = selectedSource,
            Confidence = revision.Confidence,
            UnresolvedTerms = revision.UnresolvedTerms
        };

        if (!runtime.Supports(revised))
        {
            return (null, null, Clarify(request, ReasonCode.CL001, revised.UnresolvedTerms));
        }

        return (revised, runtime, null);
    }

    private (CanonicalRequest? Canonical, SqlSourceRuntime? Runtime, SqlProductionResponse? Failure)
        ResolveNew(SqlProductionRequest request, RequestParseInput input)
    {
        if (request.Source is { } explicitSource)
        {
            if (!runtimes.TryGetValue(explicitSource, out var explicitRuntime))
            {
                return (null, null, Clarify(request, ReasonCode.CL001, []));
            }

            var explicitOutcome = explicitRuntime.Parser.Parse(input);
            return explicitOutcome.IsSuccessful
                ? (explicitOutcome.Request! with { Source = explicitSource }, explicitRuntime, null)
                : (null, null, Clarify(
                    request, explicitOutcome.ReasonCode, explicitOutcome.UnresolvedTerms));
        }

        var parsed = runtimes.Values
            .Select(runtime => (Runtime: runtime, Outcome: runtime.Parser.Parse(input)))
            .ToArray();
        var fullyResolvedSources = parsed
            .Where(candidate => candidate.Outcome.IsSuccessful
                && candidate.Outcome.UnresolvedTerms.Count == 0)
            .ToArray();
        if (fullyResolvedSources.Length > 1)
        {
            return (null, null, Clarify(request, ReasonCode.CL001, []));
        }

        var outcomes = parsed
            .Where(candidate => candidate.Runtime.CanResolve(candidate.Outcome))
            .ToArray();

        if (outcomes.Length != 1)
        {
            var unresolved = outcomes.Length == 0
                ? parsed
                    .SelectMany(candidate => candidate.Outcome.UnresolvedTerms)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray()
                : Array.Empty<string>();
            return (null, null, Clarify(request, ReasonCode.CL001, unresolved));
        }

        var selected = outcomes[0];
        return (
            selected.Outcome.Request! with { Source = selected.Runtime.Source },
            selected.Runtime,
            null);
    }

    private DataSource? InferSource(CanonicalRequest request)
    {
        var matches = runtimes.Values.Where(runtime => runtime.Supports(request)).ToArray();
        return matches.Length == 1 ? matches[0].Source : null;
    }

    private SqlProductionResponse Clarify(
        SqlProductionRequest request,
        ReasonCode reasonCode,
        IReadOnlyList<string> unresolvedTerms,
        CanonicalRequest? canonical = null)
    {
        var decision = reasonCode is ReasonCode.CL001 or ReasonCode.CL002
            ? GuardrailDecision.NeedsClarification
            : GuardrailDecision.Rejected;

        auditWriter.Write(new DecisionAuditRecord
        {
            RequestId = request.RequestId,
            ConversationId = request.ConversationId,
            PreviousRequestId = request.PreviousRequest?.RequestId,
            EffectiveScope = DecisionAuditRecordFactory.DescribeScope(request.Scope),
            Path = ProductionPath.QueryBuilder,
            Decision = decision,
            ReasonCode = reasonCode,
            Checks = [],
            Source = request.Source
        });

        return new SqlProductionResponse
        {
            RequestId = request.RequestId,
            Decision = decision,
            ReasonCode = reasonCode,
            UserMessage = SemanticClarificationQuestionBuilder.Build(
                reasonCode, canonical, unresolvedTerms),
            CanonicalRequest = canonical,
            UnresolvedTerms = unresolvedTerms,
            Checks = []
        };
    }
}
