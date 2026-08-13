using CrmAnalytics.Application.Abstractions.Integrations;
using CrmAnalytics.Application.SqlProduction;

namespace CrmAnalytics.Infrastructure.QueryExecution;

public sealed class MockQueryExecutionClient : IQueryExecutionClient
{
    private readonly TimeProvider _timeProvider;
    private readonly IQueryResultReferenceFactory _referenceFactory;

    public MockQueryExecutionClient(
        TimeProvider timeProvider,
        IQueryResultReferenceFactory referenceFactory)
    {
        _timeProvider = timeProvider;
        _referenceFactory = referenceFactory;
    }

    public Task<QueryExecutionResult> ExecuteAsync(
        SqlExecutionPlan executionPlan,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(executionPlan);
        cancellationToken.ThrowIfCancellationRequested();
        ValidatePlan(executionPlan);
        return Task.FromResult(new QueryExecutionResult(
            _referenceFactory.Create(),
            executionPlan.Source,
            Array.Empty<QueryResultColumn>(),
            Array.Empty<QueryResultRow>(),
            false,
            0,
            _timeProvider.GetUtcNow(),
            TimeSpan.Zero));
    }

    internal static void ValidatePlan(SqlExecutionPlan plan)
    {
        if (string.IsNullOrWhiteSpace(plan.Sql)
            || string.IsNullOrWhiteSpace(plan.AppliedScopeFilter)
            || plan.Parameters is null
            || plan.CommandTimeoutSeconds <= 0
            || !Enum.IsDefined(plan.Source)
            || plan.Source == SqlDataSource.Unknown)
        {
            throw new QueryExecutionPermanentException(
                QueryExecutionErrorCodes.Failed,
                plan.Source,
                TimeSpan.Zero);
        }
    }
}
