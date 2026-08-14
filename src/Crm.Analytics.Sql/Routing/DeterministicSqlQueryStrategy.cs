using Crm.Analytics.Sql.Contracts;
using Crm.Analytics.Sql.QueryBuilder;

namespace Crm.Analytics.Sql.Routing;

/// <summary>
/// Mevcut deterministik Query Builder'i production Fast Path olarak sunar.
/// </summary>
internal sealed class DeterministicSqlQueryStrategy(
    DeterministicQueryBuilder queryBuilder) : ISqlQueryStrategy
{
    public SqlQueryStrategyKind Kind => SqlQueryStrategyKind.Deterministic;

    public Task<QueryBuildResult> ProduceAsync(
        SqlQueryStrategyContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(queryBuilder.Build(context.V1Request));
    }
}
