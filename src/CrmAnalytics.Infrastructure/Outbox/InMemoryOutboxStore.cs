using CrmAnalytics.Application.Exceptions;
using CrmAnalytics.Application.Outbox;

namespace CrmAnalytics.Infrastructure.Outbox;

public sealed record OutboxStateSnapshot(
    OutboxMessage Message,
    string Status,
    int AttemptCount,
    DateTimeOffset NextAttemptAt,
    DateTimeOffset? PublishedAt,
    DateTimeOffset? DeadLetteredAt,
    string? LockOwner,
    DateTimeOffset? LockedUntil,
    string? LastFailureCode);

/// <summary>
/// Process-local development store. Its contents are lost on restart.
/// </summary>
public sealed class InMemoryOutboxStore : IOutboxWriter, IOutboxStore
{
    private readonly object _gate = new();
    private readonly Dictionary<string, State> _messages =
        new(StringComparer.Ordinal);

    public IReadOnlyList<OutboxStateSnapshot> Snapshot
    {
        get
        {
            lock (_gate)
            {
                return _messages.Values
                    .OrderBy(item => item.Message.OccurredAt)
                    .ThenBy(item => item.Message.MessageId, StringComparer.Ordinal)
                    .Select(item => item.ToSnapshot())
                    .ToArray();
            }
        }
    }

    public Task AppendAsync(OutboxMessage message,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (_messages.TryGetValue(message.MessageId, out var existing))
            {
                if (existing.Message != message)
                    throw new OutboxMessageConflictException();
                return Task.CompletedTask;
            }

            _messages.Add(message.MessageId, new State(message));
        }
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ClaimedOutboxMessage>> ClaimPendingBatchAsync(
        int batchSize, string lockOwner, DateTimeOffset now,
        TimeSpan lockDuration, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = new List<ClaimedOutboxMessage>();
        lock (_gate)
        {
            foreach (var state in _messages.Values
                .Where(item =>
                    (item.Status == OutboxStatus.Pending
                        && item.NextAttemptAt <= now)
                    || (item.Status == OutboxStatus.Processing
                        && item.LockedUntil <= now))
                .OrderBy(item => item.NextAttemptAt)
                .ThenBy(item => item.Message.OccurredAt)
                .ThenBy(item => item.Message.MessageId, StringComparer.Ordinal)
                .Take(batchSize))
            {
                state.Status = OutboxStatus.Processing;
                state.LockOwner = lockOwner;
                state.LockedUntil = now.Add(lockDuration);
                state.AttemptCount++;
                result.Add(new ClaimedOutboxMessage(
                    state.Message.MessageId,
                    state.Message.MessageType,
                    state.Message.AggregateId,
                    state.Message.OccurredAt,
                    state.Message.PayloadJson,
                    state.AttemptCount,
                    lockOwner,
                    state.LockedUntil.Value));
            }
        }
        return Task.FromResult<IReadOnlyList<ClaimedOutboxMessage>>(
            result.AsReadOnly());
    }

    public Task MarkPublishedAsync(string messageId, string lockOwner,
        DateTimeOffset publishedAt, CancellationToken cancellationToken) =>
        UpdateClaimed(messageId, lockOwner, cancellationToken, state =>
        {
            state.Status = OutboxStatus.Published;
            state.PublishedAt = publishedAt;
            state.LastFailureCode = null;
        });

    public Task ReleaseForRetryAsync(string messageId, string lockOwner,
        DateTimeOffset nextAttemptAt, string failureCode,
        CancellationToken cancellationToken) =>
        UpdateClaimed(messageId, lockOwner, cancellationToken, state =>
        {
            state.Status = OutboxStatus.Pending;
            state.NextAttemptAt = nextAttemptAt;
            state.LastFailureCode = failureCode;
        });

    public Task MarkDeadLetteredAsync(string messageId, string lockOwner,
        DateTimeOffset deadLetteredAt, string failureCode,
        CancellationToken cancellationToken) =>
        UpdateClaimed(messageId, lockOwner, cancellationToken, state =>
        {
            state.Status = OutboxStatus.DeadLettered;
            state.DeadLetteredAt = deadLetteredAt;
            state.LastFailureCode = failureCode;
        });

    public Task<int> DeletePublishedBeforeAsync(DateTimeOffset cutoff,
        int batchSize, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var ids = _messages.Values
                .Where(item => item.Status == OutboxStatus.Published
                    && item.PublishedAt < cutoff)
                .OrderBy(item => item.PublishedAt)
                .Take(batchSize)
                .Select(item => item.Message.MessageId)
                .ToArray();
            foreach (var id in ids) _messages.Remove(id);
            return Task.FromResult(ids.Length);
        }
    }

    private Task UpdateClaimed(string messageId, string lockOwner,
        CancellationToken cancellationToken, Action<State> update)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (!_messages.TryGetValue(messageId, out var state)
                || state.Status != OutboxStatus.Processing
                || !string.Equals(state.LockOwner, lockOwner,
                    StringComparison.Ordinal))
                throw new PersistenceConcurrencyException();
            update(state);
            state.LockOwner = null;
            state.LockedUntil = null;
        }
        return Task.CompletedTask;
    }

    private sealed class State
    {
        public State(OutboxMessage message)
        {
            Message = message;
            NextAttemptAt = message.OccurredAt;
        }
        public OutboxMessage Message { get; }
        public string Status { get; set; } = OutboxStatus.Pending;
        public int AttemptCount { get; set; }
        public DateTimeOffset NextAttemptAt { get; set; }
        public DateTimeOffset? PublishedAt { get; set; }
        public DateTimeOffset? DeadLetteredAt { get; set; }
        public string? LockOwner { get; set; }
        public DateTimeOffset? LockedUntil { get; set; }
        public string? LastFailureCode { get; set; }
        public OutboxStateSnapshot ToSnapshot() => new(
            Message, Status, AttemptCount, NextAttemptAt, PublishedAt,
            DeadLetteredAt, LockOwner, LockedUntil, LastFailureCode);
    }
}
