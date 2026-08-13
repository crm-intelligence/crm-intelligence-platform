using CrmAnalytics.Application.Abstractions.Integrations;
using CrmAnalytics.Application.SqlProduction;

namespace CrmAnalytics.Infrastructure.Integrations;

public sealed class UnconfiguredQueryExecutionClient
    : IQueryExecutionClient
{
    public Task<QueryExecutionResult> ExecuteAsync(
        SqlExecutionPlan executionPlan,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(executionPlan);
        cancellationToken.ThrowIfCancellationRequested();

        throw new InvalidOperationException(
            "Gerçek sorgu çalıştırma servisi yapılandırılmamış.");
    }
}
