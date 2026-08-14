using Crm.Analytics.Sql.Catalog;
using Crm.Analytics.Sql.Contracts;
using Crm.Analytics.Sql.Contracts.V2;

namespace Crm.Analytics.Sql.SemanticContext;

/// <summary>
/// Backend-owned semantic tool boundary available to agentic orchestration. Implementations
/// expose only reviewed catalog and relationship contracts; they do not discover runtime
/// schema, authorize users, mutate data scope or execute SQL.
/// </summary>
internal interface ISqlSemanticTools
{
    SemanticContextResult<SemanticMetricContext> GetMetricContext(
        DataSource runtime,
        string metricKey);

    SemanticContextResult<SemanticDimensionContext> GetDimensionContext(
        DataSource runtime,
        string dimensionKey);

    SemanticContextResult<SemanticSourceContext> GetSourceContext(
        DataSource runtime,
        string logicalSource);

    SemanticContextResult<SemanticQueryContext> GetQuerySemanticContext(
        DataSource runtime,
        CanonicalQuery query,
        int relationshipHopBudget = 3);

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
}
