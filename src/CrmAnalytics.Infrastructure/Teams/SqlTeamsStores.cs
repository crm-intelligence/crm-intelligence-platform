using System.Data;
using CrmAnalytics.Application.Teams;
using CrmAnalytics.Infrastructure.Persistence.SqlServer;
using Microsoft.EntityFrameworkCore;

namespace CrmAnalytics.Infrastructure.Teams;

public sealed class SqlTeamsNotificationTargetStore(CrmAnalyticsDbContext db)
    : ITeamsNotificationTargetStore
{
    public async Task UpsertAsync(string requestId, string conversationId,
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        Validate(requestId, conversationId, now);
        requestId = requestId.Trim();
        conversationId = conversationId.Trim();
        var strategy = db.Database.CreateExecutionStrategy();
        try
        {
            await strategy.ExecuteAsync(async () =>
            {
                await using var tx = await db.Database.BeginTransactionAsync(
                    IsolationLevel.Serializable, cancellationToken);
                var entity = await db.Set<TeamsNotificationTargetEntity>()
                    .SingleOrDefaultAsync(x => x.RequestId == requestId,
                        cancellationToken);
                if (entity is null)
                {
                    db.Add(new TeamsNotificationTargetEntity
                    {
                        RequestId = requestId,
                        ConversationId = conversationId,
                        CreatedAt = now,
                        UpdatedAt = now
                    });
                }
                else if (!string.Equals(entity.ConversationId,
                    conversationId, StringComparison.Ordinal))
                {
                    throw new TeamsNotificationTargetConflictException();
                }
                else
                {
                    entity.UpdatedAt = now;
                }
                await db.SaveChangesAsync(cancellationToken);
                await tx.CommitAsync(cancellationToken);
            });
        }
        catch (DbUpdateException)
        {
            throw new TeamsNotificationTargetConflictException();
        }
    }

    public async Task<TeamsNotificationTargetRecord?> FindAsync(
        string requestId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestId);
        var entity = await db.Set<TeamsNotificationTargetEntity>()
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.RequestId == requestId.Trim(),
                cancellationToken);
        return entity is null ? null : new(entity.RequestId,
            entity.ConversationId, entity.CreatedAt, entity.UpdatedAt);
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

public sealed class SqlTeamsNotificationDeliveryStore(CrmAnalyticsDbContext db)
    : ITeamsNotificationDeliveryStore
{
    public async Task<TeamsDeliveryClaimResult> ClaimAsync(
        string deliveryId, string requestId, string notificationStatus,
        DateTimeOffset reportUpdatedAt, string lockOwner,
        DateTimeOffset now, TimeSpan lockDuration,
        CancellationToken cancellationToken)
    {
        Validate(deliveryId, requestId, notificationStatus, lockOwner);
        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(
                IsolationLevel.Serializable, cancellationToken);
            var set = db.Set<TeamsNotificationDeliveryEntity>();
            var item = await set.SingleOrDefaultAsync(
                x => x.DeliveryId == deliveryId, cancellationToken);
            if (item is not null)
            {
                if (!string.Equals(item.RequestId, requestId,
                        StringComparison.Ordinal)
                    || !string.Equals(item.NotificationStatus,
                        notificationStatus, StringComparison.Ordinal)
                    || item.ReportUpdatedAt != reportUpdatedAt)
                    throw new InvalidOperationException(
                        "Delivery identity conflict.");
                if (item.State == TeamsNotificationDeliveryState.Delivered
                    .ToString()) return TeamsDeliveryClaimResult.Delivered;
                if (item.State == TeamsNotificationDeliveryState.Processing
                        .ToString() && item.LockedUntil > now)
                    return TeamsDeliveryClaimResult.Busy;
                item.State = TeamsNotificationDeliveryState.Processing
                    .ToString();
                item.LockOwner = lockOwner;
                item.LockedUntil = now.Add(lockDuration);
                item.AttemptCount++;
                item.UpdatedAt = now;
            }
            else
            {
                set.Add(new TeamsNotificationDeliveryEntity
                {
                    DeliveryId = deliveryId,
                    RequestId = requestId,
                    NotificationStatus = notificationStatus,
                    ReportUpdatedAt = reportUpdatedAt,
                    State = TeamsNotificationDeliveryState.Processing
                        .ToString(),
                    AttemptCount = 1,
                    LockOwner = lockOwner,
                    LockedUntil = now.Add(lockDuration),
                    CreatedAt = now,
                    UpdatedAt = now
                });
            }
            await db.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);
            return TeamsDeliveryClaimResult.Claimed;
        });
    }

    public Task MarkDeliveredAsync(string deliveryId, string lockOwner,
        DateTimeOffset deliveredAt, CancellationToken cancellationToken) =>
        UpdateClaimAsync(deliveryId, lockOwner, item =>
        {
            item.State = TeamsNotificationDeliveryState.Delivered.ToString();
            item.DeliveredAt = deliveredAt;
            item.UpdatedAt = deliveredAt;
            item.LockOwner = null;
            item.LockedUntil = null;
        }, cancellationToken);

    public Task ReleaseAsync(string deliveryId, string lockOwner,
        DateTimeOffset now, CancellationToken cancellationToken) =>
        UpdateClaimAsync(deliveryId, lockOwner, item =>
        {
            item.State = TeamsNotificationDeliveryState.Pending.ToString();
            item.UpdatedAt = now;
            item.LockOwner = null;
            item.LockedUntil = null;
        }, cancellationToken);

    private async Task UpdateClaimAsync(string deliveryId, string lockOwner,
        Action<TeamsNotificationDeliveryEntity> update,
        CancellationToken cancellationToken)
    {
        var item = await db.Set<TeamsNotificationDeliveryEntity>()
            .SingleOrDefaultAsync(x => x.DeliveryId == deliveryId,
                cancellationToken);
        if (item is null
            || item.State != TeamsNotificationDeliveryState.Processing.ToString()
            || !string.Equals(item.LockOwner, lockOwner,
                StringComparison.Ordinal)) throw new InvalidOperationException();
        update(item);
        await db.SaveChangesAsync(cancellationToken);
    }

    private static void Validate(string deliveryId, string requestId,
        string status, string lockOwner)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deliveryId);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestId);
        ArgumentException.ThrowIfNullOrWhiteSpace(status);
        ArgumentException.ThrowIfNullOrWhiteSpace(lockOwner);
        if (deliveryId.Length > 64 || requestId.Length > 32
            || status.Length > 64) throw new ArgumentException();
    }
}

public sealed class SqlTeamsCardActionSubmissionStore(CrmAnalyticsDbContext db)
    : ITeamsCardActionSubmissionStore
{
    public async Task<TeamsActionClaim> ClaimAsync(string actionToken,
        string requestId, string actionType, string lockOwner,
        DateTimeOffset now, TimeSpan lockDuration,
        CancellationToken cancellationToken)
    {
        Validate(actionToken, requestId, actionType, lockOwner);
        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(
                IsolationLevel.Serializable, cancellationToken);
            var set = db.Set<TeamsCardActionSubmissionEntity>();
            var item = await set.SingleOrDefaultAsync(
                x => x.ActionToken == actionToken, cancellationToken);
            if (item is not null)
            {
                if (!string.Equals(item.RequestId, requestId,
                        StringComparison.Ordinal)
                    || !string.Equals(item.ActionType, actionType,
                        StringComparison.Ordinal))
                    throw new InvalidOperationException(
                        "Action identity conflict.");
                if (item.State == TeamsCardActionState.Completed.ToString())
                    return new TeamsActionClaim(
                        TeamsActionClaimResult.Completed,
                        item.ResultRequestId);
                if (item.LockedUntil > now)
                    return new TeamsActionClaim(
                        TeamsActionClaimResult.Busy, null);
                item.LockOwner = lockOwner;
                item.LockedUntil = now.Add(lockDuration);
                item.UpdatedAt = now;
            }
            else
            {
                set.Add(new TeamsCardActionSubmissionEntity
                {
                    ActionToken = actionToken,
                    RequestId = requestId,
                    ActionType = actionType,
                    State = TeamsCardActionState.Processing.ToString(),
                    LockOwner = lockOwner,
                    LockedUntil = now.Add(lockDuration),
                    CreatedAt = now,
                    UpdatedAt = now
                });
            }
            await db.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);
            return new TeamsActionClaim(
                TeamsActionClaimResult.Claimed, null);
        });
    }

    public Task CompleteAsync(string actionToken, string lockOwner,
        string? resultRequestId, DateTimeOffset completedAt,
        CancellationToken cancellationToken) => UpdateClaimAsync(
            actionToken, lockOwner, item =>
            {
                item.State = TeamsCardActionState.Completed.ToString();
                item.ResultRequestId = resultRequestId;
                item.CompletedAt = completedAt;
                item.UpdatedAt = completedAt;
                item.LockOwner = null;
                item.LockedUntil = null;
            }, cancellationToken);

    public Task ReleaseAsync(string actionToken, string lockOwner,
        DateTimeOffset now, CancellationToken cancellationToken) =>
        UpdateClaimAsync(actionToken, lockOwner, item =>
        {
            item.UpdatedAt = now;
            item.LockOwner = null;
            item.LockedUntil = now;
        }, cancellationToken);

    private async Task UpdateClaimAsync(string actionToken, string lockOwner,
        Action<TeamsCardActionSubmissionEntity> update,
        CancellationToken cancellationToken)
    {
        var item = await db.Set<TeamsCardActionSubmissionEntity>()
            .SingleOrDefaultAsync(x => x.ActionToken == actionToken,
                cancellationToken);
        if (item is null
            || item.State != TeamsCardActionState.Processing.ToString()
            || !string.Equals(item.LockOwner, lockOwner,
                StringComparison.Ordinal)) throw new InvalidOperationException();
        update(item);
        await db.SaveChangesAsync(cancellationToken);
    }

    private static void Validate(string token, string requestId,
        string actionType, string lockOwner)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestId);
        ArgumentException.ThrowIfNullOrWhiteSpace(actionType);
        ArgumentException.ThrowIfNullOrWhiteSpace(lockOwner);
        if (token.Length > 64 || requestId.Length > 32
            || actionType.Length > 64) throw new ArgumentException();
    }
}
