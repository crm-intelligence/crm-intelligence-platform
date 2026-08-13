namespace CrmAnalytics.Application.Teams;

public sealed record TeamsNotificationTargetRecord(
    string RequestId,
    string ConversationId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed class TeamsNotificationTargetConflictException : Exception
{
    public TeamsNotificationTargetConflictException()
        : base("The notification target conflicts with its existing mapping.") { }
}

public interface ITeamsNotificationTargetStore
{
    Task UpsertAsync(string requestId, string conversationId,
        DateTimeOffset now, CancellationToken cancellationToken);
    Task<TeamsNotificationTargetRecord?> FindAsync(string requestId,
        CancellationToken cancellationToken);
}

public enum TeamsNotificationDeliveryState
{
    Pending = 1,
    Processing = 2,
    Delivered = 3
}

public enum TeamsDeliveryClaimResult
{
    Claimed = 1,
    Busy = 2,
    Delivered = 3
}

public interface ITeamsNotificationDeliveryStore
{
    Task<TeamsDeliveryClaimResult> ClaimAsync(
        string deliveryId, string requestId, string notificationStatus,
        DateTimeOffset reportUpdatedAt, string lockOwner,
        DateTimeOffset now, TimeSpan lockDuration,
        CancellationToken cancellationToken);
    Task MarkDeliveredAsync(string deliveryId, string lockOwner,
        DateTimeOffset deliveredAt, CancellationToken cancellationToken);
    Task ReleaseAsync(string deliveryId, string lockOwner,
        DateTimeOffset now, CancellationToken cancellationToken);
}

public enum TeamsCardActionState
{
    Processing = 1,
    Completed = 2
}

public enum TeamsActionClaimResult
{
    Claimed = 1,
    Busy = 2,
    Completed = 3
}

public sealed record TeamsActionClaim(
    TeamsActionClaimResult Result,
    string? ResultRequestId);

public interface ITeamsCardActionSubmissionStore
{
    Task<TeamsActionClaim> ClaimAsync(
        string actionToken, string requestId, string actionType,
        string lockOwner, DateTimeOffset now, TimeSpan lockDuration,
        CancellationToken cancellationToken);
    Task CompleteAsync(string actionToken, string lockOwner,
        string? resultRequestId, DateTimeOffset completedAt,
        CancellationToken cancellationToken);
    Task ReleaseAsync(string actionToken, string lockOwner,
        DateTimeOffset now, CancellationToken cancellationToken);
}
