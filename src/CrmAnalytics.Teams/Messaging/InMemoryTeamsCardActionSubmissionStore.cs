using System.Collections.Concurrent;

namespace CrmAnalytics.Teams.Messaging;

public sealed class InMemoryTeamsCardActionSubmissionStore
    : ITeamsCardActionSubmissionStore
{
    private readonly ConcurrentDictionary<string, byte> _processed =
        new(StringComparer.Ordinal);

    public Task<bool> IsProcessedAsync(
        string actionToken,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actionToken);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(
            _processed.ContainsKey(actionToken.Trim()));
    }

    public Task MarkProcessedAsync(
        string actionToken,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actionToken);
        cancellationToken.ThrowIfCancellationRequested();

        _processed.TryAdd(actionToken.Trim(), 0);
        return Task.CompletedTask;
    }
}
