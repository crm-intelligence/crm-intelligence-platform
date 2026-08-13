using CrmAnalytics.Application.Abstractions.Persistence;

namespace CrmAnalytics.Infrastructure.Persistence;

public sealed class InMemoryApplicationTransactionRunner
    : IApplicationTransactionRunner
{
    public async Task ExecuteAsync(
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        await operation(cancellationToken);
    }

    public async Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        return await operation(cancellationToken);
    }
}
