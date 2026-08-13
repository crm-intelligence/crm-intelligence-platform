using System.Collections.Concurrent;
using CrmAnalytics.Application.Teams;

namespace CrmAnalytics.Infrastructure.Teams;

public sealed class InMemoryTeamsNotificationTargetStore
    : ITeamsNotificationTargetStore
{
    private readonly ConcurrentDictionary<string, TeamsNotificationTargetRecord>
        _items = new(StringComparer.Ordinal);

    public Task UpsertAsync(string requestId, string conversationId,
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        Validate(requestId, conversationId, now);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_items)
        {
            if (_items.TryGetValue(requestId.Trim(), out var existing))
            {
                if (!string.Equals(existing.ConversationId,
                    conversationId.Trim(), StringComparison.Ordinal))
                    throw new TeamsNotificationTargetConflictException();
                _items[existing.RequestId] = existing with { UpdatedAt = now };
            }
            else
            {
                _items[requestId.Trim()] = new(requestId.Trim(),
                    conversationId.Trim(), now, now);
            }
        }
        return Task.CompletedTask;
    }

    public Task<TeamsNotificationTargetRecord?> FindAsync(string requestId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestId);
        cancellationToken.ThrowIfCancellationRequested();
        _items.TryGetValue(requestId.Trim(), out var value);
        return Task.FromResult(value);
    }

    private static void Validate(string requestId, string conversationId,
        DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestId);
        ArgumentException.ThrowIfNullOrWhiteSpace(conversationId);
        if (requestId.Trim().Length > 32 || conversationId.Trim().Length > 512
            || now.Offset != TimeSpan.Zero) throw new ArgumentException();
    }
}

public sealed class InMemoryTeamsNotificationDeliveryStore
    : ITeamsNotificationDeliveryStore
{
    private readonly Dictionary<string, Delivery> _items =
        new(StringComparer.Ordinal);

    public Task<TeamsDeliveryClaimResult> ClaimAsync(string deliveryId,
        string requestId, string notificationStatus,
        DateTimeOffset reportUpdatedAt, string lockOwner,
        DateTimeOffset now, TimeSpan lockDuration,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_items)
        {
            if (_items.TryGetValue(deliveryId, out var item))
            {
                if (item.State == TeamsNotificationDeliveryState.Delivered)
                    return Task.FromResult(TeamsDeliveryClaimResult.Delivered);
                if (item.LockedUntil > now)
                    return Task.FromResult(TeamsDeliveryClaimResult.Busy);
                item.State = TeamsNotificationDeliveryState.Processing;
                item.LockOwner = lockOwner;
                item.LockedUntil = now.Add(lockDuration);
                item.AttemptCount++;
            }
            else
            {
                _items.Add(deliveryId, new Delivery
                {
                    State = TeamsNotificationDeliveryState.Processing,
                    LockOwner = lockOwner,
                    LockedUntil = now.Add(lockDuration),
                    AttemptCount = 1
                });
            }
            return Task.FromResult(TeamsDeliveryClaimResult.Claimed);
        }
    }

    public Task MarkDeliveredAsync(string deliveryId, string lockOwner,
        DateTimeOffset deliveredAt, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_items)
        {
            if (!_items.TryGetValue(deliveryId, out var item)
                || item.State != TeamsNotificationDeliveryState.Processing
                || !string.Equals(item.LockOwner, lockOwner,
                    StringComparison.Ordinal)) throw new InvalidOperationException();
            item.State = TeamsNotificationDeliveryState.Delivered;
            item.LockOwner = null;
            item.LockedUntil = null;
        }
        return Task.CompletedTask;
    }

    public Task ReleaseAsync(string deliveryId, string lockOwner,
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_items)
        {
            if (_items.TryGetValue(deliveryId, out var item)
                && item.State == TeamsNotificationDeliveryState.Processing
                && string.Equals(item.LockOwner, lockOwner,
                    StringComparison.Ordinal))
            {
                item.State = TeamsNotificationDeliveryState.Pending;
                item.LockOwner = null;
                item.LockedUntil = null;
            }
        }
        return Task.CompletedTask;
    }

    private sealed class Delivery
    {
        public TeamsNotificationDeliveryState State { get; set; }
        public int AttemptCount { get; set; }
        public string? LockOwner { get; set; }
        public DateTimeOffset? LockedUntil { get; set; }
    }
}

public sealed class InMemoryTeamsCardActionSubmissionStore
    : ITeamsCardActionSubmissionStore
{
    private readonly Dictionary<string, ActionItem> _items =
        new(StringComparer.Ordinal);

    public Task<TeamsActionClaim> ClaimAsync(string actionToken,
        string requestId, string actionType, string lockOwner,
        DateTimeOffset now, TimeSpan lockDuration,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_items)
        {
            if (_items.TryGetValue(actionToken, out var item))
            {
                if (!string.Equals(item.RequestId, requestId,
                    StringComparison.Ordinal)
                    || !string.Equals(item.ActionType, actionType,
                        StringComparison.Ordinal))
                    throw new InvalidOperationException();
                if (item.State == TeamsCardActionState.Completed)
                    return Task.FromResult(new TeamsActionClaim(
                        TeamsActionClaimResult.Completed,
                        item.ResultRequestId));
                if (item.LockedUntil > now)
                    return Task.FromResult(new TeamsActionClaim(
                        TeamsActionClaimResult.Busy, null));
                item.LockOwner = lockOwner;
                item.LockedUntil = now.Add(lockDuration);
            }
            else
            {
                _items.Add(actionToken, new ActionItem
                {
                    RequestId = requestId, ActionType = actionType,
                    State = TeamsCardActionState.Processing,
                    LockOwner = lockOwner,
                    LockedUntil = now.Add(lockDuration)
                });
            }
            return Task.FromResult(new TeamsActionClaim(
                TeamsActionClaimResult.Claimed, null));
        }
    }

    public Task CompleteAsync(string actionToken, string lockOwner,
        string? resultRequestId, DateTimeOffset completedAt,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_items)
        {
            if (!_items.TryGetValue(actionToken, out var item)
                || !string.Equals(item.LockOwner, lockOwner,
                    StringComparison.Ordinal)) throw new InvalidOperationException();
            item.State = TeamsCardActionState.Completed;
            item.ResultRequestId = resultRequestId;
            item.LockOwner = null;
            item.LockedUntil = null;
        }
        return Task.CompletedTask;
    }

    public Task ReleaseAsync(string actionToken, string lockOwner,
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_items)
        {
            if (_items.TryGetValue(actionToken, out var item)
                && item.State == TeamsCardActionState.Processing
                && string.Equals(item.LockOwner, lockOwner,
                    StringComparison.Ordinal))
            {
                item.LockOwner = null;
                item.LockedUntil = now;
            }
        }
        return Task.CompletedTask;
    }

    private sealed class ActionItem
    {
        public string RequestId { get; set; } = string.Empty;
        public string ActionType { get; set; } = string.Empty;
        public TeamsCardActionState State { get; set; }
        public string? ResultRequestId { get; set; }
        public string? LockOwner { get; set; }
        public DateTimeOffset? LockedUntil { get; set; }
    }
}
