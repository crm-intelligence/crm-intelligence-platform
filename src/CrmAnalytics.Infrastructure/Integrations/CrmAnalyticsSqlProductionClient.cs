using System.Globalization;
using Crm.Analytics.Sql.Contracts;
using Crm.Analytics.Sql.Catalog;
using Crm.Analytics.Sql.Nlu;
using Crm.Analytics.Sql.Routing;
using Crm.Analytics.Sql.Service;
using CrmAnalytics.Application.Abstractions.Integrations;
using CrmAnalytics.Application.SqlProduction;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ExternalDataSource = Crm.Analytics.Sql.Contracts.DataSource;
using ExternalDecision = Crm.Analytics.Sql.Contracts.GuardrailDecision;
using ExternalScope = Crm.Analytics.Sql.Contracts.UserDataScope;

namespace CrmAnalytics.Infrastructure.Integrations;

public enum PlanningPath
{
    DeterministicParser,
    EmbeddingDeterministic,
    EmbeddingPlusQwen,
    LlmFirst,
    SubmittedPlan,
    Clarification,
    Unsupported
}

public sealed class CrmAnalyticsSqlProductionClient(
    ISqlProductionService service,
    SqlProductionScopeCompatibilityMapper scopeMapper,
    IOllamaStructuredPlanningClient ollamaClient,
    IOptions<OllamaOptions> ollamaOptions,
    IOptions<SqlProductionProviderOptions> sqlProductionOptions,
    ILogger<CrmAnalyticsSqlProductionClient> logger,
    ISemanticEmbeddingResolver? semanticResolver = null,
    IOptions<SemanticEmbeddingOptions>? semanticOptions = null,
    ISemanticCanonicalRequestAssembler? semanticAssembler = null,
    ILlmFirstCanonicalRequestAssembler? llmFirstAssembler = null)
    : ISqlProductionClient
{
    private readonly ISemanticCanonicalRequestAssembler effectiveSemanticAssembler =
        semanticAssembler ?? new SemanticCanonicalRequestAssembler(
            Crm.Analytics.Sql.Catalog.SemanticCatalogRegistry.CreateDefault());
    private readonly ILlmFirstCanonicalRequestAssembler effectiveLlmFirstAssembler =
        llmFirstAssembler ?? new LlmFirstCanonicalRequestAssembler(
            Crm.Analytics.Sql.Catalog.SemanticCatalogRegistry.CreateDefault());

    public CrmAnalyticsSqlProductionClient(
        ISqlProductionService service,
        SqlProductionScopeCompatibilityMapper scopeMapper)
        : this(service, scopeMapper, DisabledOllamaClient.Instance,
            Options.Create(new OllamaOptions()),
            Options.Create(new SqlProductionProviderOptions()),
            NullLogger<CrmAnalyticsSqlProductionClient>.Instance,
            DisabledSemanticResolver.Instance,
            Options.Create(new SemanticEmbeddingOptions()),
            new SemanticCanonicalRequestAssembler(
                Crm.Analytics.Sql.Catalog.SemanticCatalogRegistry.CreateDefault()))
    {
    }

    public async Task<SqlProductionClientResult> ProduceAsync(
        SqlProductionClientRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var previous = request.PreviousCanonicalRequestJson is null
                ? null
                : CanonicalRequestSerializer.Deserialize(
                    request.PreviousCanonicalRequestJson);
            var productionRequest = new SqlProductionRequest
            {
                RequestId = request.RequestId,
                ConversationId = request.ConversationId,
                Prompt = request.Prompt,
                Today = request.Today,
                UserId = request.UserId,
                Source = MapSource(request.Source),
                Scope = MapScope(scopeMapper.Map(request.UserDataScope)),
                PreviousRequest = previous
            };
            if (request.SemanticPlan is not null)
            {
                var submitted = TrySubmittedSemanticPlan(
                    request, productionRequest, previous);
                return MapResponse(submitted);
            }
            if (ollamaOptions.Value.PlanningMode == OllamaPlanningMode.LlmFirst)
            {
                var llmFirst = await TryLlmFirstAsync(
                    request, productionRequest, previous, cancellationToken);
                return MapResponse(llmFirst);
            }

            var response = service.Produce(productionRequest);

            if (response.Decision == ExternalDecision.NeedsClarification
                && response.ReasonCode is ReasonCode.CL001 or ReasonCode.CL002)
            {
                response = await TryOllamaAsync(
                    request, productionRequest, response, cancellationToken);
            }
            else
            {
                logger.LogInformation("Planning path {PlanningPath}.",
                    PlanningPath.DeterministicParser);
            }

            return MapResponse(response);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            // Technical library, serialization and contract failures are Failed,
            // never policy rejections.
            logger.LogInformation(
                "SQL production failed closed with {FailureKind}.",
                exception.GetType().Name);
            return new SqlProductionClientResult(
                SqlProductionClientDecision.Failed,
                null,
                null,
                null,
                null,
                null);
        }
    }

    private SqlProductionResponse TrySubmittedSemanticPlan(
        SqlProductionClientRequest request,
        SqlProductionRequest productionRequest,
        CanonicalRequest? previous)
    {
        if (!SubmittedSemanticPlanningResultMapper.TryMap(
                request.SemanticPlan!, out var plan)
            || plan is null
            || !HasValidSemanticPlanningInvariant(plan))
        {
            logger.LogInformation(
                "Planning path {PlanningPath} rejected an invalid envelope.",
                PlanningPath.SubmittedPlan);
            LogSemanticTrust(
                null, "INVALID_ENVELOPE", false, "Rejected");
            return InvalidPlanResponse(request.RequestId);
        }

        if (plan.Outcome == PlanningOutcome.NeedsClarification)
        {
            logger.LogInformation("Planning path {PlanningPath} requires clarification.",
                PlanningPath.SubmittedPlan);
            LogSemanticTrust(
                null, "PLAN_NEEDS_CLARIFICATION", false,
                "NeedsClarification");
            return SafeClarificationResponse(
                request.RequestId, plan.Clarification);
        }
        if (plan.Outcome == PlanningOutcome.Unsupported)
        {
            logger.LogInformation("Planning path {PlanningPath} is unsupported.",
                PlanningPath.SubmittedPlan);
            LogSemanticTrust(
                null, "PLAN_UNSUPPORTED", false, "Rejected");
            return UnsupportedResponse(request.RequestId);
        }

        var assembly = effectiveLlmFirstAssembler.Assemble(
            request.RequestId,
            request.ConversationId,
            request.Prompt,
            request.Today,
            productionRequest.Source,
            previous,
            plan.Intent!);
        if (assembly.Outcome == LlmFirstAssemblyOutcome.NeedsClarification)
        {
            LogSemanticTrust(
                assembly.Diagnostics?.DeterministicResolverAgreement,
                assembly.Diagnostics?.DivergenceCategory ?? "NOT_EVALUATED",
                assembly.Diagnostics?.ExplicitAnchorConflict ?? false,
                "NeedsClarification");
            return SafeClarificationResponse(
                request.RequestId, assembly.Clarification);
        }
        if (assembly.Outcome == LlmFirstAssemblyOutcome.Unsupported)
        {
            logger.LogInformation(
                "Planning path {PlanningPath} failed backend validation with {ReasonCode}.",
                PlanningPath.SubmittedPlan,
                assembly.ReasonCode);
            LogSemanticTrust(
                assembly.Diagnostics?.DeterministicResolverAgreement,
                assembly.Diagnostics?.DivergenceCategory ?? "NOT_EVALUATED",
                assembly.Diagnostics?.ExplicitAnchorConflict ?? false,
                "RejectedUnsupported");
            return UnsupportedResponse(request.RequestId);
        }
        if (assembly.Outcome != LlmFirstAssemblyOutcome.Assembled
            || assembly.CanonicalRequest is null)
        {
            logger.LogInformation(
                "Planning path {PlanningPath} failed backend validation with {ReasonCode}.",
                PlanningPath.SubmittedPlan,
                assembly.ReasonCode);
            LogSemanticTrust(
                assembly.Diagnostics?.DeterministicResolverAgreement,
                assembly.Diagnostics?.DivergenceCategory ?? "NOT_EVALUATED",
                assembly.Diagnostics?.ExplicitAnchorConflict ?? false,
                "RejectedInvalid");
            return InvalidPlanResponse(request.RequestId);
        }

        var produced = TryProduceCanonical(
            productionRequest, assembly.CanonicalRequest);
        logger.LogInformation(
            "Planning path {PlanningPath} canonical validation outcome is {Outcome}.",
            PlanningPath.SubmittedPlan,
            produced?.Decision.ToString() ?? "Failed");
        LogSemanticTrust(
            assembly.Diagnostics?.DeterministicResolverAgreement,
            assembly.Diagnostics?.DivergenceCategory ?? "NOT_EVALUATED",
            assembly.Diagnostics?.ExplicitAnchorConflict ?? false,
            produced?.Decision.ToString() ?? "Failed");
        return produced ?? InvalidPlanResponse(request.RequestId);
    }

    private async Task<SqlProductionResponse> TryLlmFirstAsync(
        SqlProductionClientRequest request,
        SqlProductionRequest productionRequest,
        CanonicalRequest? previous,
        CancellationToken cancellationToken)
    {
        if (!ollamaOptions.Value.Enabled)
        {
            LogLlmFirst("Disabled", "OLLAMA_DISABLED", 0, false, false,
                null, null, null, []);
            return SafeClarificationResponse(request.RequestId);
        }

        var result = await ollamaClient.PlanSemanticAsync(
            new OllamaSemanticPlanningRequest(
                request.RequestId,
                request.ConversationId,
                request.Today,
                request.Prompt,
                request.OriginalPrompt,
                request.ClarificationQuestion,
                request.ClarificationAnswer),
            cancellationToken);
        var plan = result.Plan;
        if (plan is null || !HasValidSemanticPlanningInvariant(plan))
        {
            LogLlmFirst(result.Outcome, result.ReasonCode,
                result.DurationMilliseconds, result.SchemaValidationSucceeded,
                false, result.DoneReason, result.PromptTokenCount,
                result.CompletionTokenCount, plan?.UnresolvedConcepts ?? []);
            return SafeClarificationResponse(request.RequestId);
        }

        if (plan.Outcome == PlanningOutcome.NeedsClarification)
        {
            LogLlmFirst("NeedsClarification", "NONE",
                result.DurationMilliseconds, result.SchemaValidationSucceeded,
                false, result.DoneReason, result.PromptTokenCount,
                result.CompletionTokenCount, plan.UnresolvedConcepts);
            return SafeClarificationResponse(request.RequestId, plan.Clarification);
        }
        if (plan.Outcome == PlanningOutcome.Unsupported)
        {
            LogLlmFirst("Unsupported", "NONE",
                result.DurationMilliseconds, result.SchemaValidationSucceeded,
                false, result.DoneReason, result.PromptTokenCount,
                result.CompletionTokenCount, plan.UnresolvedConcepts);
            return UnsupportedResponse(request.RequestId);
        }

        var assembly = effectiveLlmFirstAssembler.Assemble(
            request.RequestId,
            request.ConversationId,
            request.Prompt,
            request.Today,
            productionRequest.Source,
            previous,
            plan.Intent!);
        if (assembly.Outcome == LlmFirstAssemblyOutcome.NeedsClarification)
        {
            LogLlmFirst("NeedsClarification", assembly.ReasonCode,
                result.DurationMilliseconds, result.SchemaValidationSucceeded,
                false, result.DoneReason, result.PromptTokenCount,
                result.CompletionTokenCount, []);
            return SafeClarificationResponse(
                request.RequestId, assembly.Clarification);
        }
        if (assembly.Outcome == LlmFirstAssemblyOutcome.Unsupported)
        {
            LogLlmFirst("Unsupported", assembly.ReasonCode,
                result.DurationMilliseconds, result.SchemaValidationSucceeded,
                false, result.DoneReason, result.PromptTokenCount,
                result.CompletionTokenCount, []);
            return UnsupportedResponse(request.RequestId);
        }
        if (assembly.Outcome != LlmFirstAssemblyOutcome.Assembled
            || assembly.CanonicalRequest is null)
        {
            LogLlmFirst("BackendValidationRejected", assembly.ReasonCode,
                result.DurationMilliseconds, result.SchemaValidationSucceeded,
                false, result.DoneReason, result.PromptTokenCount,
                result.CompletionTokenCount, []);
            return SafeClarificationResponse(request.RequestId);
        }

        var planned = TryProduceCanonical(
            productionRequest, assembly.CanonicalRequest);
        var canonicalAccepted = planned?.Decision == ExternalDecision.Accepted;
        LogLlmFirst(canonicalAccepted ? "Accepted" : "CanonicalRejected",
            canonicalAccepted ? "NONE" : planned?.ReasonCode.ToString()
                ?? "CANONICAL_VALIDATION_FAILURE",
            result.DurationMilliseconds, result.SchemaValidationSucceeded,
            canonicalAccepted, result.DoneReason, result.PromptTokenCount,
            result.CompletionTokenCount, []);
        return planned ?? SafeClarificationResponse(request.RequestId);
    }

    private async Task<SqlProductionResponse> TryOllamaAsync(
        SqlProductionClientRequest request,
        SqlProductionRequest productionRequest,
        SqlProductionResponse deterministicFallback,
        CancellationToken cancellationToken)
    {
        SemanticCandidateConstraints? constraints = null;
        var dateMatch = RelativeDateResolver.Resolve(
            TurkishTextNormalizer.Tokenize(request.Prompt), request.Today);
        var deterministicIntent = new SemanticSlotIntentDetector().Detect(
            request.Prompt, deterministicFallback.CanonicalRequest,
            productionRequest.Source);
        var embeddingEnabled = semanticOptions?.Value.Enabled == true
            && semanticResolver is not null;
        if (embeddingEnabled)
        {
            var semantic = await semanticResolver!.ResolveAsync(
                request.Prompt,
                productionRequest.Source,
                deterministicFallback.CanonicalRequest,
                request.Today,
                cancellationToken);
            LogSemanticResolution(semantic);
            if (semantic.Available)
            {
                var assembly = effectiveSemanticAssembler.Assemble(
                    request.RequestId,
                    request.ConversationId,
                    request.Prompt,
                    productionRequest.Source,
                    deterministicFallback.CanonicalRequest,
                    semantic);
                if (assembly.Outcome == SemanticCanonicalAssemblyOutcome.Assembled)
                {
                    var deterministicValidation = TryProduceCanonical(
                        productionRequest, assembly.CanonicalRequest!);
                    var deterministicAccepted = deterministicValidation?.Decision
                        == ExternalDecision.Accepted;
                    LogPlanningPath(PlanningPath.EmbeddingDeterministic, semantic,
                        assembly.State.Completeness.DurationMilliseconds,
                        assembly.DurationMilliseconds, 0, deterministicAccepted);
                    return deterministicAccepted
                        ? deterministicValidation! : deterministicFallback;
                }

                if (assembly.Outcome == SemanticCanonicalAssemblyOutcome.Unsupported)
                {
                    LogPlanningPath(PlanningPath.Unsupported, semantic,
                        assembly.State.Completeness.DurationMilliseconds,
                        assembly.DurationMilliseconds, 0, false);
                    return UnsupportedResponse(request.RequestId);
                }

                if (assembly.Outcome == SemanticCanonicalAssemblyOutcome.Invalid
                    || !CanResolveWithPartialQwen(assembly.State))
                {
                    LogPlanningPath(PlanningPath.Clarification, semantic,
                        assembly.State.Completeness.DurationMilliseconds,
                        assembly.DurationMilliseconds, 0, false);
                    return deterministicFallback;
                }

                if (!ollamaOptions.Value.Enabled)
                {
                    LogPlanningPath(PlanningPath.Clarification, semantic,
                        assembly.State.Completeness.DurationMilliseconds,
                        assembly.DurationMilliseconds, 0, false);
                    return deterministicFallback;
                }

                constraints = CreateGapConstraints(assembly.State);
                var gapResult = await ollamaClient.ResolveGapsAsync(
                    new SemanticGapPlanningRequest(
                        request.RequestId,
                        request.ConversationId,
                        request.Today,
                        request.Prompt,
                        assembly.State,
                        constraints),
                    cancellationToken);
                if (gapResult.Selection is not
                    { Outcome: SemanticGapOutcome.Resolved } selection
                    || !GapSelectionAllowed(selection, assembly.State, constraints))
                {
                    LogPlanningPath(PlanningPath.Clarification, semantic,
                        assembly.State.Completeness.DurationMilliseconds,
                        assembly.DurationMilliseconds,
                        gapResult.DurationMilliseconds, false);
                    return deterministicFallback;
                }

                var completedSemantic = CompleteSemanticResult(
                    semantic, assembly.State, selection);
                var completed = effectiveSemanticAssembler.Assemble(
                    request.RequestId,
                    request.ConversationId,
                    request.Prompt,
                    productionRequest.Source,
                    deterministicFallback.CanonicalRequest,
                    completedSemantic);
                if (completed.Outcome != SemanticCanonicalAssemblyOutcome.Assembled)
                {
                    LogPlanningPath(PlanningPath.Clarification, semantic,
                        completed.State.Completeness.DurationMilliseconds,
                        completed.DurationMilliseconds,
                        gapResult.DurationMilliseconds, false);
                    return deterministicFallback;
                }

                var gapValidation = TryProduceCanonical(
                    productionRequest, completed.CanonicalRequest!);
                var gapAccepted = gapValidation?.Decision == ExternalDecision.Accepted;
                LogPlanningPath(PlanningPath.EmbeddingPlusQwen, semantic,
                    completed.State.Completeness.DurationMilliseconds,
                    completed.DurationMilliseconds,
                    gapResult.DurationMilliseconds, gapAccepted);
                return gapAccepted ? gapValidation! : deterministicFallback;
            }

            // The configured semantic flow is candidate-constrained. An embedding
            // outage must not reopen full-catalog planning, and an unresolved requested
            // date must never be guessed by the model.
            LogPlanningPath(PlanningPath.Clarification, semantic,
                0, 0, 0, false);
            return deterministicFallback;
        }

        if (!ollamaOptions.Value.Enabled)
        {
            LogOllama("Disabled", fallbackUsed: true,
                deterministicFallback.ReasonCode.ToString(), 0, false, false,
                null, null, null, []);
            return deterministicFallback;
        }

        if (deterministicIntent.DateRequested && dateMatch is null)
        {
            logger.LogInformation(
                "Requested date was not resolved deterministically; model planning was skipped.");
            return deterministicFallback;
        }

        var result = await ollamaClient.PlanAsync(
            new OllamaStructuredPlanningRequest(
                request.RequestId,
                request.ConversationId,
                request.Today,
                request.Prompt,
                request.OriginalPrompt,
                request.ClarificationQuestion,
                request.ClarificationAnswer,
                constraints),
            cancellationToken);

        var plan = result.Plan;
        if (plan is null)
        {
            LogOllama(result.Outcome, fallbackUsed: true, result.ReasonCode,
                result.DurationMilliseconds, result.SchemaValidationSucceeded,
                canonicalValidationSucceeded: false, result.DoneReason,
                result.PromptTokenCount, result.CompletionTokenCount, []);
            return deterministicFallback;
        }

        if (!HasValidPlanningInvariant(plan)
            || constraints is not null && plan.CanonicalRequest is not null
                && !constraints.Allows(plan.CanonicalRequest)
            || dateMatch is not null && plan.CanonicalRequest is not null
                && plan.CanonicalRequest.DateRange != dateMatch.Range)
        {
            LogOllama("PlanningInvariantRejected", fallbackUsed: true,
                "OLLAMA_PLANNING_INVARIANT_REJECTED",
                result.DurationMilliseconds, result.SchemaValidationSucceeded,
                canonicalValidationSucceeded: false, result.DoneReason,
                result.PromptTokenCount, result.CompletionTokenCount,
                plan.UnresolvedConcepts);
            return deterministicFallback;
        }

        if (plan.Outcome is PlanningOutcome.NeedsClarification
            or PlanningOutcome.Unsupported)
        {
            LogOllama(plan.Outcome.ToString(), fallbackUsed: true, "NONE",
                result.DurationMilliseconds, result.SchemaValidationSucceeded,
                canonicalValidationSucceeded: false, result.DoneReason,
                result.PromptTokenCount, result.CompletionTokenCount,
                plan.UnresolvedConcepts);
            return deterministicFallback;
        }

        var canonical = plan.CanonicalRequest!;
        var belowConfidenceThreshold = canonical.Confidence
            < sqlProductionOptions.Value.ConfidenceThreshold;
        if (belowConfidenceThreshold)
        {
            LogOllama(
                "LowConfidence",
                fallbackUsed: true,
                "OLLAMA_LOW_CONFIDENCE",
                result.DurationMilliseconds, result.SchemaValidationSucceeded,
                canonicalValidationSucceeded: false, result.DoneReason,
                result.PromptTokenCount, result.CompletionTokenCount,
                plan.UnresolvedConcepts);
            return deterministicFallback;
        }

        try
        {
            var planned = service.ProduceCanonical(
                productionRequest, canonical);
            var accepted = planned.Decision == ExternalDecision.Accepted;
            var clarification = planned.Decision
                == ExternalDecision.NeedsClarification;
            LogOllama(accepted ? "Accepted" : "CanonicalRejected",
                fallbackUsed: !accepted,
                accepted ? "NONE" : planned.ReasonCode.ToString(),
                result.DurationMilliseconds,
                result.SchemaValidationSucceeded,
                canonicalValidationSucceeded: accepted,
                result.DoneReason,
                result.PromptTokenCount,
                result.CompletionTokenCount,
                plan.UnresolvedConcepts);
            return accepted || clarification ? planned : deterministicFallback;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogOllama("CanonicalValidationFailure", fallbackUsed: true,
                exception.GetType().Name, result.DurationMilliseconds,
                result.SchemaValidationSucceeded,
                canonicalValidationSucceeded: false, result.DoneReason,
                result.PromptTokenCount, result.CompletionTokenCount,
                plan.UnresolvedConcepts);
            return deterministicFallback;
        }
    }

    private SqlProductionResponse? TryProduceCanonical(
        SqlProductionRequest productionRequest,
        CanonicalRequest canonical)
    {
        try
        {
            return service.ProduceCanonical(productionRequest, canonical);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogInformation(
                "Semantic canonical validation failed closed with {FailureKind}.",
                exception.GetType().Name);
            return null;
        }
    }

    private static SqlProductionResponse UnsupportedResponse(string requestId) => new()
    {
        RequestId = requestId,
        Decision = ExternalDecision.Rejected,
        ReasonCode = ReasonCode.CL001,
        UserMessage = "Istenen is kavrami semantic catalog tarafindan desteklenmiyor.",
        CanonicalRequest = null,
        Sql = null,
        Parameters = [],
        Checks = []
    };

    private static SqlProductionResponse InvalidPlanResponse(string requestId) => new()
    {
        RequestId = requestId,
        Decision = ExternalDecision.Rejected,
        ReasonCode = ReasonCode.CL001,
        UserMessage = "Semantic plan dogrulama kurallarini gecemedi.",
        CanonicalRequest = null,
        Sql = null,
        Parameters = [],
        Checks = []
    };

    private static SqlProductionResponse SafeClarificationResponse(
        string requestId,
        PlanningClarification? clarification = null) => new()
    {
        RequestId = requestId,
        Decision = ExternalDecision.NeedsClarification,
        ReasonCode = ReasonCode.CL001,
        UserMessage = clarification?.Kind switch
        {
            UnresolvedConceptKind.Metric =>
                "Hangi metrigi raporlamami istediginizi netlestirir misiniz?",
            UnresolvedConceptKind.Dimension =>
                "Raporu hangi kirilima gore gostermemi istediginizi netlestirir misiniz?",
            UnresolvedConceptKind.Filter =>
                "Uygulanacak filtreyi netlestirir misiniz?",
            UnresolvedConceptKind.Date =>
                "Hangi tarih araligini kullanmami istediginizi netlestirir misiniz?",
            UnresolvedConceptKind.Source =>
                "Raporun veri kaynagini netlestirir misiniz?",
            _ => "Talebi guvenli bicimde planlamak icin biraz daha ayrinti verir misiniz?"
        },
        CanonicalRequest = null,
        Sql = null,
        Parameters = [],
        Checks = []
    };

    private static SemanticResolverResult CompleteSemanticResult(
        SemanticResolverResult original,
        SemanticPlanningState state,
        SemanticGapSelection selection)
    {
        var metric = original.Metric;
        if (selection.MetricKey is not null)
        {
            metric = new SemanticResolutionResult(
                SemanticResolutionKind.Resolved,
                SemanticSlotKind.Metric,
                selection.MetricKey,
                original.Metric?.Similarity ?? state.Confidence,
                original.Metric?.SecondBestSimilarity ?? -1,
                original.Metric?.Margin ?? 0,
                [selection.MetricKey],
                original.Metric?.SupportingRepresentationType
                    ?? SemanticRepresentationType.FullNormalized);
        }

        var dimension = original.Dimension;
        var resolvesDimension = state.QwenResolvableSlots.Any(slot =>
            slot.Kind == UnresolvedConceptKind.Dimension);
        if (selection.DimensionKeys.Count > 0 && resolvesDimension)
        {
            dimension = new SemanticResolutionResult(
                SemanticResolutionKind.Resolved,
                SemanticSlotKind.Dimension,
                selection.DimensionKeys[0],
                original.Dimension?.Similarity ?? state.Confidence,
                original.Dimension?.SecondBestSimilarity ?? -1,
                original.Dimension?.Margin ?? 0,
                selection.DimensionKeys,
                original.Dimension?.SupportingRepresentationType
                    ?? SemanticRepresentationType.FullNormalized);
        }

        var filterLiteral = original.FilterLiteral;
        if (selection.DimensionKeys.Count > 0
            && state.QwenResolvableSlots.Any(slot =>
                slot.Kind == UnresolvedConceptKind.Filter)
            && filterLiteral is not null)
        {
            filterLiteral = filterLiteral with
            {
                DimensionCandidate = selection.DimensionKeys[0],
                ResolutionKind = FilterLiteralResolutionKind.Resolved,
                DimensionCandidateKeys = selection.DimensionKeys
            };
        }

        return original with
        {
            Metric = metric,
            Dimension = dimension,
            FilterLiteral = filterLiteral
        };
    }

    private static bool GapSelectionAllowed(
        SemanticGapSelection selection,
        SemanticPlanningState state,
        SemanticCandidateConstraints constraints)
    {
        var metricAmbiguous = state.QwenResolvableSlots.Any(slot =>
            slot.Kind == UnresolvedConceptKind.Metric);
        var dimensionAmbiguous = state.QwenResolvableSlots.Any(slot =>
            slot.Kind is UnresolvedConceptKind.Dimension
                or UnresolvedConceptKind.Filter);
        return metricAmbiguous == (selection.MetricKey is not null)
            && (selection.MetricKey is null || constraints.MetricKeys.Contains(
                selection.MetricKey, StringComparer.Ordinal))
            && dimensionAmbiguous == (selection.DimensionKeys.Count > 0)
            && selection.DimensionKeys.All(key => constraints.DimensionKeys.Contains(
                key, StringComparer.Ordinal));
    }

    private static bool HasValidPlanningInvariant(PlanningResult plan)
    {
        if (!Enum.IsDefined(plan.Outcome)
            || plan.UnresolvedConcepts.Any(concept => !Enum.IsDefined(concept.Kind))
            || plan.Clarification is not null
                && !Enum.IsDefined(plan.Clarification.Kind))
        {
            return false;
        }

        return plan.Outcome switch
        {
            PlanningOutcome.Accepted => plan.CanonicalRequest is not null
                && plan.UnresolvedConcepts.Count == 0
                && plan.Clarification is null
                && plan.CanonicalRequest.UnresolvedTerms.Count == 0,
            PlanningOutcome.NeedsClarification => plan.CanonicalRequest is null
                && plan.Clarification is not null,
            PlanningOutcome.Unsupported => plan.CanonicalRequest is null
                && plan.UnresolvedConcepts.Count > 0
                && plan.Clarification is null,
            _ => false
        };
    }

    private static bool HasValidSemanticPlanningInvariant(
        ExtractedSemanticPlanningResult plan)
    {
        if (!Enum.IsDefined(plan.Outcome)
            || plan.UnresolvedConcepts.Any(concept => !Enum.IsDefined(concept.Kind))
            || plan.Clarification is not null
                && !Enum.IsDefined(plan.Clarification.Kind))
        {
            return false;
        }

        return plan.Outcome switch
        {
            PlanningOutcome.Accepted => plan.Intent is not null
                && plan.UnresolvedConcepts.Count == 0
                && plan.Clarification is null,
            PlanningOutcome.NeedsClarification => plan.Intent is null
                && plan.Clarification is not null,
            PlanningOutcome.Unsupported => plan.Intent is null
                && plan.UnresolvedConcepts.Count > 0
                && plan.Clarification is null,
            _ => false
        };
    }

    private static SemanticCandidateConstraints CreateConstraints(
        SemanticResolverResult result,
        string prompt,
        DateOnly today) => new(
        CandidateKeys(result.Metric, result.MetricRequested),
        CandidateKeys(result.Dimension, result.DimensionRequested),
        RelativeDateResolver.Resolve(
            TurkishTextNormalizer.Tokenize(prompt), today)?.Range,
        result.Metric?.Kind == SemanticResolutionKind.Resolved
            && result.Metric.CandidateKey is not null
                ? [result.Metric.CandidateKey] : [],
        result.Dimension?.Kind == SemanticResolutionKind.Resolved
            && result.Dimension.CandidateKey is not null
                ? [result.Dimension.CandidateKey] : []);

    private static SemanticCandidateConstraints CreateGapConstraints(
        SemanticPlanningState state) => new(
        state.QwenResolvableSlots
            .Where(slot => slot.Kind == UnresolvedConceptKind.Metric)
            .SelectMany(slot => slot.CandidateKeys)
            .Concat(state.ResolvedMetric is null ? [] : [state.ResolvedMetric])
            .Distinct(StringComparer.Ordinal).ToArray(),
        state.QwenResolvableSlots
            .Where(slot => slot.Kind is UnresolvedConceptKind.Dimension
                or UnresolvedConceptKind.Filter)
            .SelectMany(slot => slot.CandidateKeys)
            .Concat(state.ResolvedDimensions)
            .Distinct(StringComparer.Ordinal).ToArray(),
        state.ResolvedDate,
        state.ResolvedMetric is null ? [] : [state.ResolvedMetric],
        state.ResolvedDimensions,
        state.QwenResolvableSlots.Select(slot => slot.Kind).ToArray(),
        state.ResolvedSource);

    private static bool CanResolveWithPartialQwen(SemanticPlanningState state)
    {
        if (state.UnsupportedSlots.Count > 0)
        {
            return false;
        }

        var unresolved = state.AmbiguousSlots.Select(slot => slot.Kind)
            .Concat(state.MissingSlots)
            .Distinct()
            .ToArray();
        return unresolved.Length > 0
            && unresolved.All(kind => kind is UnresolvedConceptKind.Metric
                or UnresolvedConceptKind.Dimension
                or UnresolvedConceptKind.Filter)
            && unresolved.All(kind => state.QwenResolvableSlots.Any(slot =>
                slot.Kind == kind && slot.CandidateKeys.Count > 0));
    }

    private static IReadOnlyList<string> CandidateKeys(
        SemanticResolutionResult? result,
        bool requested)
    {
        if (!requested || result is null)
        {
            return [];
        }

        return result.Kind == SemanticResolutionKind.Resolved
            && result.CandidateKey is not null
                ? [result.CandidateKey]
                : result.CandidateKeys;
    }

    private void LogSemanticResolution(SemanticResolverResult result)
    {
        logger.LogInformation(
            "Semantic embedding resolution available {Available}; metric decision {MetricDecision}; metric support {MetricSupportingRepresentation}; dimension decision {DimensionDecision}; dimension support {DimensionSupportingRepresentation}; intent detection {IntentDetectionMilliseconds} ms; preprocessing {PreprocessingMilliseconds} ms; query embedding {QueryEmbeddingMilliseconds} ms; similarity search {SimilaritySearchMilliseconds} ms; representation count {RepresentationCount}; failure {FailureKind}.",
            result.Available,
            result.Metric?.Kind.ToString() ?? "None",
            result.Metric?.SupportingRepresentationType.ToString() ?? "None",
            result.Dimension?.Kind.ToString() ?? "None",
            result.Dimension?.SupportingRepresentationType.ToString() ?? "None",
            result.IntentDetectionMilliseconds,
            result.PreprocessingMilliseconds,
            result.QueryEmbeddingMilliseconds,
            result.SimilaritySearchMilliseconds,
            result.RepresentationCount,
            result.FailureKind);
    }

    private void LogPlanningPath(
        PlanningPath path,
        SemanticResolverResult semantic,
        long completenessMilliseconds,
        long assemblyMilliseconds,
        long qwenMilliseconds,
        bool canonicalValidationSucceeded) => logger.LogInformation(
        "Planning path {PlanningPath}; intent detection {IntentDetectionMilliseconds} ms; preprocessing {PreprocessingMilliseconds} ms; query embedding {QueryEmbeddingMilliseconds} ms; similarity search {SimilaritySearchMilliseconds} ms; completeness gate {CompletenessMilliseconds} ms; deterministic canonical assembly {AssemblyMilliseconds} ms; Qwen {QwenMilliseconds} ms; canonical validation {CanonicalValidationSucceeded}.",
        path,
        semantic.IntentDetectionMilliseconds,
        semantic.PreprocessingMilliseconds,
        semantic.QueryEmbeddingMilliseconds,
        semantic.SimilaritySearchMilliseconds,
        completenessMilliseconds,
        assemblyMilliseconds,
        qwenMilliseconds,
        canonicalValidationSucceeded);

    private void LogOllama(
        string outcome,
        bool fallbackUsed,
        string reasonCode,
        long durationMilliseconds,
        bool schemaValidationSucceeded,
        bool canonicalValidationSucceeded,
        string? doneReason,
        int? promptTokenCount,
        int? completionTokenCount,
        IReadOnlyList<UnresolvedConcept> unresolvedConcepts)
    {
        var unresolvedKinds = string.Join(",",
            unresolvedConcepts.Select(concept => concept.Kind.ToString()));
        logger.LogInformation(
            "Structured planning {Provider} {Model} completed in {DurationMilliseconds} ms with {Outcome}; fallback {FallbackUsed}; reason {ReasonCode}; schema valid {SchemaValidationSucceeded}; canonical valid {CanonicalValidationSucceeded}; unresolved kinds {UnresolvedConceptKinds}; done reason {OllamaDoneReason}; prompt tokens {PromptTokenCount}; completion tokens {CompletionTokenCount}.",
            "Ollama",
            ollamaOptions.Value.Model,
            durationMilliseconds,
            outcome,
            fallbackUsed,
            reasonCode,
            schemaValidationSucceeded,
            canonicalValidationSucceeded,
            unresolvedKinds,
            doneReason,
            promptTokenCount,
            completionTokenCount);
    }

    private void LogLlmFirst(
        string outcome,
        string reasonCode,
        long durationMilliseconds,
        bool schemaValidationSucceeded,
        bool canonicalValidationSucceeded,
        string? doneReason,
        int? promptTokenCount,
        int? completionTokenCount,
        IReadOnlyList<UnresolvedConcept> unresolvedConcepts)
    {
        var unresolvedKinds = string.Join(",",
            unresolvedConcepts.Select(concept => concept.Kind.ToString()));
        logger.LogInformation(
            "LLM-first semantic planning {Provider} {Model} completed in {DurationMilliseconds} ms with {Outcome}; reason {ReasonCode}; schema valid {SchemaValidationSucceeded}; backend canonical valid {CanonicalValidationSucceeded}; unresolved kinds {UnresolvedConceptKinds}; done reason {OllamaDoneReason}; prompt tokens {PromptTokenCount}; completion tokens {CompletionTokenCount}.",
            "Ollama",
            ollamaOptions.Value.Model,
            durationMilliseconds,
            outcome,
            reasonCode,
            schemaValidationSucceeded,
            canonicalValidationSucceeded,
            unresolvedKinds,
            doneReason,
            promptTokenCount,
            completionTokenCount);
    }

    private void LogSemanticTrust(
        bool? deterministicResolverAgreement,
        string divergenceCategory,
        bool explicitAnchorConflict,
        string finalValidationDecision)
    {
        if (deterministicResolverAgreement == false || explicitAnchorConflict)
        {
            logger.LogWarning(
                "SEMANTIC_INTERPRETATION_DIVERGENCE semantic source {SemanticSource}; deterministic resolver agreement {DeterministicResolverAgreement}; divergence category {DivergenceCategory}; explicit-anchor conflict {ExplicitAnchorConflict}; final validation decision {FinalValidationDecision}.",
                "copilot",
                deterministicResolverAgreement,
                divergenceCategory,
                explicitAnchorConflict,
                finalValidationDecision);
            return;
        }

        logger.LogInformation(
            "Semantic trust validation semantic source {SemanticSource}; deterministic resolver agreement {DeterministicResolverAgreement}; divergence category {DivergenceCategory}; explicit-anchor conflict {ExplicitAnchorConflict}; final validation decision {FinalValidationDecision}.",
            "copilot",
            deterministicResolverAgreement,
            divergenceCategory,
            explicitAnchorConflict,
            finalValidationDecision);
    }

    internal static SqlProductionClientResult MapResponse(
        SqlProductionResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);
        var canonicalJson = response.CanonicalRequest is null
            ? null
            : CanonicalRequestSerializer.Serialize(response.CanonicalRequest);

        return response.Decision switch
        {
            ExternalDecision.Accepted => new SqlProductionClientResult(
                SqlProductionClientDecision.Accepted,
                canonicalJson,
                null,
                null,
                CreateExecutionPlan(response),
                MapShape(response.ResultShape, response.CanonicalRequest)),
            ExternalDecision.NeedsClarification =>
                new SqlProductionClientResult(
                    SqlProductionClientDecision.NeedsClarification,
                    canonicalJson,
                    response.UserMessage,
                    null,
                    null,
                    null),
            ExternalDecision.Rejected => new SqlProductionClientResult(
                SqlProductionClientDecision.Rejected,
                canonicalJson,
                response.UserMessage,
                response.ReasonCode.ToString(),
                null,
                null),
            _ => throw new InvalidDataException(
                "Unknown SQL production decision.")
        };
    }

    private static SqlExecutionPlan CreateExecutionPlan(
        SqlProductionResponse response)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(response.Sql);
        ArgumentException.ThrowIfNullOrWhiteSpace(
            response.AppliedScopeFilter);

        if (response.CommandTimeoutSeconds is not > 0)
        {
            throw new InvalidDataException(
                "SQL production returned an invalid timeout.");
        }

        if (response.Source is null)
        {
            throw new InvalidDataException(
                "SQL production returned no source.");
        }

        if (string.IsNullOrWhiteSpace(response.PhysicalObject))
        {
            throw new InvalidDataException(
                "SQL production returned no verified physical object.");
        }

        if (response.RowLimit is not > 0)
        {
            throw new InvalidDataException(
                "SQL production returned an invalid row limit.");
        }

        var parameters = response.Parameters
            ?? throw new InvalidDataException(
                "SQL production returned null parameters.");
        var shape = MapShape(response.ResultShape, response.CanonicalRequest);
        return new SqlExecutionPlan(
            MapSource(response.Source.Value),
            response.Sql,
            parameters.Select(MapParameter).ToArray(),
            response.AppliedScopeFilter,
            response.CommandTimeoutSeconds.Value,
            shape,
            response.PhysicalObject,
            response.RowLimit.Value);
    }

    private static SqlExecutionParameter MapParameter(
        SqlParameterSpec parameter)
    {
        ArgumentNullException.ThrowIfNull(parameter);
        var (kind, value) = parameter.Kind switch
        {
            FilterValueKind.Text =>
                (SqlExecutionParameterKind.Text, (object)parameter.Raw),
            FilterValueKind.Integer =>
                (SqlExecutionParameterKind.Integer,
                    int.Parse(parameter.Raw, NumberStyles.Integer,
                        CultureInfo.InvariantCulture)),
            FilterValueKind.Decimal =>
                (SqlExecutionParameterKind.Decimal,
                    decimal.Parse(parameter.Raw, NumberStyles.Number,
                        CultureInfo.InvariantCulture)),
            FilterValueKind.Boolean =>
                (SqlExecutionParameterKind.Boolean,
                    bool.Parse(parameter.Raw)),
            FilterValueKind.Date =>
                (SqlExecutionParameterKind.Date,
                    DateOnly.Parse(parameter.Raw, CultureInfo.InvariantCulture)),
            _ => throw new NotSupportedException(
                "Unknown SQL parameter kind.")
        };

        return new SqlExecutionParameter(
            parameter.Name,
            kind,
            value,
            parameter.IsUnicode,
            parameter.IsLargeObject ? -1 : parameter.Kind == FilterValueKind.Text
                ? Math.Max(parameter.Raw.Length, 1)
                : null);
    }

    private static SqlResultShapeMetadata? MapShape(
        ResultShape? shape,
        CanonicalRequest? canonical)
    {
        if (shape is null)
        {
            return null;
        }

        var dimensions = canonical?.Dimensions.ToArray() ?? [];
        var metrics = canonical?.Metrics.ToArray() ?? [];
        var columns = new List<SqlResultColumnMetadata>();
        if (canonical?.Source is { } source)
        {
            var catalog = SemanticCatalogRegistry.CreateDefault()
                .GetRequired(source).Catalog;
            columns.AddRange(dimensions.Select(name =>
            {
                var definition = catalog.FindDimension(name);
                return new SqlResultColumnMetadata(
                    name,
                    null,
                    null,
                    definition?.Label,
                    definition?.IsTimeDimension == true);
            }));
            columns.AddRange(metrics.Select(name =>
            {
                var definition = catalog.FindMetric(name);
                return new SqlResultColumnMetadata(
                    name,
                    definition?.Unit,
                    definition?.Format,
                    definition?.Label);
            }));
        }
        return new SqlResultShapeMetadata(
            shape.SuggestedVisual.ToString(),
            shape.Rationale,
            columns,
            dimensions,
            metrics,
            dimensions.Concat(metrics).ToArray());
    }

    private static ExternalScope MapScope(SqlProductionScopeMapping mapping) =>
        mapping.Kind switch
        {
            SqlProductionScopeMappingKind.Unrestricted =>
                ExternalScope.Unrestricted,
            SqlProductionScopeMappingKind.Regions =>
                ExternalScope.ForRegions(mapping.Regions.ToArray()),
            SqlProductionScopeMappingKind.Unresolved =>
                ExternalScope.Unresolved,
            _ => ExternalScope.Unresolved
        };

    private static ExternalDataSource? MapSource(SqlDataSource source) =>
        source switch
        {
            SqlDataSource.Unknown => null,
            SqlDataSource.Dwh => ExternalDataSource.Dwh,
            SqlDataSource.Oltp => ExternalDataSource.Oltp,
            _ => throw new NotSupportedException("Unknown SQL source.")
        };

    private static SqlDataSource MapSource(ExternalDataSource source) =>
        source switch
        {
            ExternalDataSource.Dwh => SqlDataSource.Dwh,
            ExternalDataSource.Oltp => SqlDataSource.Oltp,
            _ => throw new NotSupportedException("Unknown SQL source.")
        };

    private sealed class DisabledOllamaClient : IOllamaStructuredPlanningClient
    {
        public static readonly DisabledOllamaClient Instance = new();

        public Task<OllamaStructuredPlanningResult> PlanAsync(
            OllamaStructuredPlanningRequest request,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Ollama is disabled.");
    }

    private sealed class DisabledSemanticResolver : ISemanticEmbeddingResolver
    {
        public static readonly DisabledSemanticResolver Instance = new();

        public Task<SemanticResolverResult> ResolveAsync(
            string prompt,
            ExternalDataSource? source,
            CanonicalRequest? partiallyResolved,
            CancellationToken cancellationToken) => Task.FromResult(new SemanticResolverResult(
                false, null, null, false, false, 0, 0, "Disabled"));
    }
}
