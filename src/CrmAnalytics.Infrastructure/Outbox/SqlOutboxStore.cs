using CrmAnalytics.Application.Exceptions;
using CrmAnalytics.Application.Outbox;
using CrmAnalytics.Infrastructure.Persistence.SqlServer;
using Microsoft.EntityFrameworkCore;

namespace CrmAnalytics.Infrastructure.Outbox;

public sealed class SqlOutboxStore : IOutboxStore
{
    private readonly CrmAnalyticsDbContext _dbContext;

    public SqlOutboxStore(CrmAnalyticsDbContext dbContext) =>
        _dbContext = dbContext;

    public async Task<IReadOnlyList<ClaimedOutboxMessage>>
        ClaimPendingBatchAsync(
            int batchSize,
            string lockOwner,
            DateTimeOffset now,
            TimeSpan lockDuration,
            CancellationToken cancellationToken)
    {
        ValidateClaimArguments(batchSize, lockOwner, now, lockDuration);
        var candidateIds = await _dbContext.OutboxMessages
            .AsNoTracking()
            .Where(item =>
                (item.Status == OutboxStatus.Pending
                    && item.NextAttemptAt <= now)
                || (item.Status == OutboxStatus.Processing
                    && item.LockedUntil <= now))
            .OrderBy(item => item.NextAttemptAt)
            .ThenBy(item => item.OccurredAt)
            .ThenBy(item => item.MessageId)
            .Select(item => item.MessageId)
            .Take(batchSize * 4)
            .ToArrayAsync(cancellationToken);

        var claimed = new List<ClaimedOutboxMessage>(batchSize);
        foreach (var candidateId in candidateIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (claimed.Count == batchSize) break;

            var entity = await _dbContext.OutboxMessages
                .SingleOrDefaultAsync(item => item.MessageId == candidateId,
                    cancellationToken);
            if (entity is null
                || !IsClaimable(entity, now))
            {
                continue;
            }

            entity.Status = OutboxStatus.Processing;
            entity.LockOwner = lockOwner;
            entity.LockedUntil = now.Add(lockDuration);
            entity.AttemptCount++;
            entity.UpdatedAt = now;

            try
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                _dbContext.Entry(entity).State = EntityState.Detached;
                continue;
            }

            if (!Enum.TryParse<OutboxMessageType>(
                    entity.MessageType, ignoreCase: false, out var messageType)
                || !Enum.IsDefined(messageType))
            {
                messageType = (OutboxMessageType)0;
            }

            claimed.Add(new ClaimedOutboxMessage(
                entity.MessageId,
                messageType,
                entity.AggregateId,
                entity.OccurredAt,
                entity.PayloadJson,
                entity.AttemptCount,
                lockOwner,
                entity.LockedUntil.Value));
        }

        return claimed.AsReadOnly();
    }

    public Task MarkPublishedAsync(string messageId, string lockOwner,
        DateTimeOffset publishedAt, CancellationToken cancellationToken) =>
        UpdateClaimedAsync(messageId, lockOwner, publishedAt,
            entity =>
            {
                entity.Status = OutboxStatus.Published;
                entity.PublishedAt = publishedAt;
                entity.LastFailureCode = null;
            }, cancellationToken);

    public Task ReleaseForRetryAsync(string messageId, string lockOwner,
        DateTimeOffset nextAttemptAt, string failureCode,
        CancellationToken cancellationToken) =>
        UpdateClaimedAsync(messageId, lockOwner, nextAttemptAt,
            entity =>
            {
                entity.Status = OutboxStatus.Pending;
                entity.NextAttemptAt = nextAttemptAt;
                entity.LastFailureCode = NormalizeFailureCode(failureCode);
            }, cancellationToken);

    public Task MarkDeadLetteredAsync(string messageId, string lockOwner,
        DateTimeOffset deadLetteredAt, string failureCode,
        CancellationToken cancellationToken) =>
        UpdateClaimedAsync(messageId, lockOwner, deadLetteredAt,
            entity =>
            {
                entity.Status = OutboxStatus.DeadLettered;
                entity.DeadLetteredAt = deadLetteredAt;
                entity.LastFailureCode = NormalizeFailureCode(failureCode);
            }, cancellationToken);

    public async Task<int> DeletePublishedBeforeAsync(
        DateTimeOffset cutoff,
        int batchSize,
        CancellationToken cancellationToken)
    {
        var ids = await _dbContext.OutboxMessages.AsNoTracking()
            .Where(item => item.Status == OutboxStatus.Published
                && item.PublishedAt < cutoff)
            .OrderBy(item => item.PublishedAt)
            .Select(item => item.MessageId)
            .Take(batchSize)
            .ToArrayAsync(cancellationToken);
        if (ids.Length == 0) return 0;
        return await _dbContext.OutboxMessages
            .Where(item => ids.Contains(item.MessageId)
                && item.Status == OutboxStatus.Published)
            .ExecuteDeleteAsync(cancellationToken);
    }

    private async Task UpdateClaimedAsync(
        string messageId,
        string lockOwner,
        DateTimeOffset now,
        Action<OutboxMessageEntity> update,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(messageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(lockOwner);
        var entity = await _dbContext.OutboxMessages.SingleOrDefaultAsync(
            item => item.MessageId == messageId,
            cancellationToken);
        if (entity is null
            || entity.Status != OutboxStatus.Processing
            || !string.Equals(entity.LockOwner, lockOwner,
                StringComparison.Ordinal))
        {
            throw new PersistenceConcurrencyException();
        }

        update(entity);
        entity.LockOwner = null;
        entity.LockedUntil = null;
        entity.UpdatedAt = now;
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new PersistenceConcurrencyException(exception);
        }
    }

    private static bool IsClaimable(
        OutboxMessageEntity entity,
        DateTimeOffset now) =>
        (entity.Status == OutboxStatus.Pending
            && entity.NextAttemptAt <= now)
        || (entity.Status == OutboxStatus.Processing
            && entity.LockedUntil <= now);

    private static void ValidateClaimArguments(
        int batchSize,
        string lockOwner,
        DateTimeOffset now,
        TimeSpan lockDuration)
    {
        if (batchSize < 1) throw new ArgumentOutOfRangeException(nameof(batchSize));
        ArgumentException.ThrowIfNullOrWhiteSpace(lockOwner);
        if (lockOwner.Trim().Length > 128)
            throw new ArgumentException("Lock owner is too long.", nameof(lockOwner));
        if (now.Offset != TimeSpan.Zero)
            throw new ArgumentException("Claim time must be UTC.", nameof(now));
        if (lockDuration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(lockDuration));
    }

    private static string NormalizeFailureCode(string failureCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(failureCode);
        return failureCode.Trim().Length <= 128
            ? failureCode.Trim()
            : throw new ArgumentException("Failure code is too long.", nameof(failureCode));
    }
}
