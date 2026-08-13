using System.Collections.Concurrent;

namespace CrmAnalytics.Teams.Notifications;

public sealed class InMemoryTeamsNotificationTargetStore
    : ITeamsNotificationTargetStore
{
    private readonly ConcurrentDictionary<string, TeamsNotificationTarget>
        _targets = new(StringComparer.OrdinalIgnoreCase);

    public Task SaveAsync(
        TeamsNotificationTarget target,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        cancellationToken.ThrowIfCancellationRequested();

        var stored = _targets.GetOrAdd(target.RequestId, target);
        if (!string.Equals(
            stored.ConversationId,
            target.ConversationId,
            StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "A notification target cannot be changed after "
                + "registration.");
        }

        return Task.CompletedTask;
    }

    public Task<TeamsNotificationTarget?> GetByRequestIdAsync(
        string requestId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestId);
        cancellationToken.ThrowIfCancellationRequested();

        _targets.TryGetValue(requestId.Trim(), out var target);
        return Task.FromResult(target);
    }
}
