using Crm.Analytics.Sql.Agentic;
using Crm.Analytics.Sql.Catalog;
using Crm.Analytics.Sql.Contracts;
using Crm.Analytics.Sql.Contracts.V2;
using Crm.Analytics.Sql.Routing;
using Crm.Analytics.Sql.SemanticContext;

namespace Crm.Analytics.Sql.Service;

/// <summary>
/// Internal backend facade used by the authenticated HTTP integration adapter. It exposes
/// reviewed semantic tools and untrusted-candidate validation, never database execution.
/// </summary>
internal interface ISqlAgentBackend
{
    QueryStrategyDecision Analyze(DataSource runtime, CanonicalQuery query);

    SemanticContextResult<SemanticQueryContext> GetQuerySemanticContext(
        DataSource runtime,
        CanonicalQuery query);

    SemanticContextResult<SemanticMetricContext> GetMetricContext(
        DataSource runtime,
        string metricKey);

    SemanticContextResult<SemanticDimensionContext> GetDimensionContext(
        DataSource runtime,
        string dimensionKey);

    SemanticContextResult<SemanticSourceContext> GetSourceContext(
        DataSource runtime,
        string logicalSource);

    SemanticContextResult<ImmutableRelationshipSet> GetRelationships(
        DataSource runtime,
        IReadOnlyCollection<string> sourceSet);

    JoinPathResolutionResult FindApprovedJoinPaths(
        DataSource runtime,
        string fromSource,
        string toSource,
        int maxHops);

    JoinPathValidationResult ValidateApprovedJoinPath(
        DataSource runtime,
        JoinPathProposal proposal);

    SqlProductionResponse ValidateCandidate(
        SqlProductionRequest request,
        CanonicalRequest canonical,
        CanonicalQuery query,
        SqlReasoningResponse candidate);
}
