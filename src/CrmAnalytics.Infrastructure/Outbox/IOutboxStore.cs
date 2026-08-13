using CrmAnalytics.Application.Outbox;

namespace CrmAnalytics.Infrastructure.Outbox;

public sealed record ClaimedOutboxMessage(
    string MessageId,
    OutboxMessageType MessageType,
    string AggregateId,
    DateTimeOffset OccurredAt,
    string PayloadJson,
    int AttemptCount,
    string LockOwner,
    DateTimeOffset LockedUntil);

public interface IOutboxStore
{
    Task<IReadOnlyList<ClaimedOutboxMessage>> ClaimPendingBatchAsync(
        int batchSize,
        string lockOwner,
        DateTimeOffset now,
        TimeSpan lockDuration,
        CancellationToken cancellationToken);

    Task MarkPublishedAsync(string messageId, string lockOwner,
        DateTimeOffset publishedAt, CancellationToken cancellationToken);

    Task ReleaseForRetryAsync(string messageId, string lockOwner,
        DateTimeOffset nextAttemptAt, string failureCode,
        CancellationToken cancellationToken);

    Task MarkDeadLetteredAsync(string messageId, string lockOwner,
        DateTimeOffset deadLetteredAt, string failureCode,
        CancellationToken cancellationToken);

    Task<int> DeletePublishedBeforeAsync(
        DateTimeOffset cutoff,
        int batchSize,
        CancellationToken cancellationToken);
}
