using CrmAnalytics.Application.Abstractions.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CrmAnalytics.Infrastructure.Persistence.SqlServer;

public sealed class EfCoreApplicationTransactionRunner
    : IApplicationTransactionRunner
{
    private readonly CrmAnalyticsDbContext _dbContext;

    public EfCoreApplicationTransactionRunner(
        CrmAnalyticsDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task ExecuteAsync(
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        return ExecuteAsync<object?>(async token =>
        {
            await operation(token);
            return null;
        }, cancellationToken);
    }

    public async Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);

        if (_dbContext.Database.CurrentTransaction is not null)
        {
            return await operation(cancellationToken);
        }

        var strategy = _dbContext.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction =
                await _dbContext.Database.BeginTransactionAsync(
                    cancellationToken);
            try
            {
                var result = await operation(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return result;
            }
            catch
            {
                try
                {
                    await transaction.RollbackAsync(CancellationToken.None);
                }
                catch
                {
                    // Preserve the operation/cancellation exception. Disposal
                    // still makes a best effort to release the transaction.
                }
                throw;
            }
        });
    }
}
