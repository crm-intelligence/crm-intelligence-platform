using CrmAnalytics.Application.SqlProduction;

namespace CrmAnalytics.Application.Abstractions.Integrations;

public interface IQueryExecutionClient
{
    Task<QueryExecutionResult> ExecuteAsync(
        SqlExecutionPlan executionPlan,
        CancellationToken cancellationToken);
}
