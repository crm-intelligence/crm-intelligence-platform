using Crm.Analytics.Sql.Agentic;
using Crm.Analytics.Sql.Audit;
using Crm.Analytics.Sql.Catalog;
using Crm.Analytics.Sql.Contracts;
using Crm.Analytics.Sql.Contracts.V2;
using Crm.Analytics.Sql.Nlu;
using Crm.Analytics.Sql.Parsing;
using Crm.Analytics.Sql.QueryBuilder;
using Crm.Analytics.Sql.Routing;
using Crm.Analytics.Sql.SemanticContext;

namespace Crm.Analytics.Sql.Service;

internal sealed class SqlAgentBackend : ISqlAgentBackend
{
    private readonly IReadOnlyDictionary<DataSource, Runtime> runtimes;

    public SqlAgentBackend(
        SemanticCatalogRegistry registry,
        TSqlParserFactory parserFactory,
        IDecisionAuditWriter auditWriter,
        SqlProductionOptions options)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(parserFactory);
        ArgumentNullException.ThrowIfNull(auditWriter);
        ArgumentNullException.ThrowIfNull(options);

        var semanticTools = new SemanticContextService(registry, parserFactory);
        runtimes = registry.Sources.ToDictionary(pair => pair.Key, pair =>
        {
            var builder = new DeterministicQueryBuilder(
                parserFactory, pair.Value.Catalog, pair.Value.AllowList);
            var router = new SqlProductionRouter(
                new QueryStrategyRouter(
                    [new DeterministicSqlQueryStrategy(builder)],
                    builder),
                pair.Value.AllowList,
                parserFactory,
                auditWriter,
                new AmbiguityGate(options.ConfidenceThreshold),
                pair.Key);
            return new Runtime(
                pair.Value.Catalog,
                semanticTools,
                new QueryCapabilityAnalyzer(
                    pair.Value.Catalog, builder.Capabilities),
                parserFactory,
                router);
        });
    }

    public QueryStrategyDecision Analyze(DataSource runtime, CanonicalQuery query) =>
        GetRuntime(runtime).Analyzer.Analyze(query);

    public SemanticContextResult<SemanticQueryContext> GetQuerySemanticContext(
        DataSource runtime,
        CanonicalQuery query) =>
        GetRuntime(runtime).SemanticTools.GetQuerySemanticContext(runtime, query);

    public SemanticContextResult<SemanticMetricContext> GetMetricContext(
        DataSource runtime,
        string metricKey) =>
        GetRuntime(runtime).SemanticTools.GetMetricContext(runtime, metricKey);

    public SemanticContextResult<SemanticDimensionContext> GetDimensionContext(
        DataSource runtime,
        string dimensionKey) =>
        GetRuntime(runtime).SemanticTools.GetDimensionContext(runtime, dimensionKey);

    public SemanticContextResult<SemanticSourceContext> GetSourceContext(
        DataSource runtime,
        string logicalSource) =>
        GetRuntime(runtime).SemanticTools.GetSourceContext(runtime, logicalSource);

    public SemanticContextResult<ImmutableRelationshipSet> GetRelationships(
        DataSource runtime,
        IReadOnlyCollection<string> sourceSet) =>
        GetRuntime(runtime).SemanticTools.GetRelationships(runtime, sourceSet);

    public JoinPathResolutionResult FindApprovedJoinPaths(
        DataSource runtime,
        string fromSource,
        string toSource,
        int maxHops) =>
        GetRuntime(runtime).SemanticTools.FindApprovedJoinPaths(
            runtime, fromSource, toSource, maxHops);

    public JoinPathValidationResult ValidateApprovedJoinPath(
        DataSource runtime,
        JoinPathProposal proposal) =>
        GetRuntime(runtime).SemanticTools.ValidateApprovedJoinPath(runtime, proposal);

    public SqlProductionResponse ValidateCandidate(
        SqlProductionRequest request,
        CanonicalRequest canonical,
        CanonicalQuery query,
        SqlReasoningResponse candidate)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(canonical);
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(candidate);

        var selectedSource = request.Source ?? canonical.Source;
        if (selectedSource is null
            || canonical.Source != selectedSource
            || !runtimes.TryGetValue(selectedSource.Value, out var runtime))
        {
            return Rejected(request, canonical, ReasonCode.CL001);
        }

        var decision = runtime.Analyzer.Analyze(query);
        if (decision.Outcome != QueryCapabilityOutcome.AgenticRequired)
        {
            return Rejected(
                request,
                canonical,
                decision.Outcome == QueryCapabilityOutcome.Unsupported
                    ? decision.FailureReasonCode
                    : ReasonCode.GR014);
        }

        if (selectedSource != DataSource.Dwh)
        {
            return Rejected(request, canonical, ReasonCode.GR014);
        }

        var semantic = runtime.SemanticTools.GetQuerySemanticContext(
            selectedSource.Value, query);
        if (!semantic.IsSuccessful || semantic.Value!.Sources.Length != 1)
        {
            return Rejected(request, canonical, ReasonCode.GR006);
        }

        var converted = SqlReasoningCandidateValidator.Validate(
            candidate, query, semantic.Value, runtime.ParserFactory);
        var routing = runtime.Router.ProduceCandidate(
            canonical, request.Scope, converted, request.UserId);
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
            UserMessage = guardrail.ReasonMessage,
            ResultShape = guardrail.Decision == GuardrailDecision.Accepted
                ? ResultShapeClassifier.Classify(canonical, runtime.Catalog)
                : null,
            CanonicalRequest = canonical,
            Path = routing.Path,
            Checks = guardrail.Checks,
            UnresolvedTerms = canonical.UnresolvedTerms
        };
    }

    private Runtime GetRuntime(DataSource runtime) =>
        runtimes.TryGetValue(runtime, out var value)
            ? value
            : throw new KeyNotFoundException("SQL agent runtime catalog bulunamadi.");

    private static SqlProductionResponse Rejected(
        SqlProductionRequest request,
        CanonicalRequest canonical,
        ReasonCode reasonCode) => new()
        {
            RequestId = request.RequestId,
            Decision = GuardrailDecision.Rejected,
            ReasonCode = reasonCode,
            CanonicalRequest = canonical,
            Checks = [],
            UnresolvedTerms = canonical.UnresolvedTerms
        };

    private sealed record Runtime(
        MetricCatalogDocument Catalog,
        ISqlSemanticTools SemanticTools,
        QueryCapabilityAnalyzer Analyzer,
        TSqlParserFactory ParserFactory,
        SqlProductionRouter Router);
}
