using Crm.Analytics.Sql.Agentic;
using Crm.Analytics.Sql.Contracts;
using Crm.Analytics.Sql.QueryBuilder;

namespace Crm.Analytics.Sql.Routing;

/// <summary>
/// Produces one untrusted agentic candidate only for an explicit AgenticRequired decision.
/// Successful output must still enter SqlProductionRouter's common guardrail pipeline.
/// </summary>
internal sealed class AgenticSqlQueryStrategy(
    DataSource runtime,
    SqlReasoningOrchestrator orchestrator) : ISqlQueryStrategy
{
    public SqlQueryStrategyKind Kind => SqlQueryStrategyKind.Agentic;

    public Task<QueryBuildResult> ProduceAsync(
        SqlQueryStrategyContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        if (context.Decision.Outcome != QueryCapabilityOutcome.AgenticRequired)
        {
            return Task.FromResult(QueryBuildResult.Failure(
                ReasonCode.GR014,
                "Agentic strategy yalnizca AgenticRequired capability karariyla cagrilabilir."));
        }

        if (context.V1Request.Source != runtime)
        {
            return Task.FromResult(QueryBuildResult.Failure(
                ReasonCode.CL001,
                "Canonical source ile agentic runtime source uyusmuyor."));
        }

        return orchestrator.GenerateAsync(context.Query, context.Decision, cancellationToken);
    }
}
