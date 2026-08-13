namespace CrmAnalytics.Infrastructure.Teams;

internal sealed class TeamsNotificationTargetEntity
{
    public string RequestId { get; set; } = string.Empty;
    public string ConversationId { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

internal sealed class TeamsNotificationDeliveryEntity
{
    public string DeliveryId { get; set; } = string.Empty;
    public string RequestId { get; set; } = string.Empty;
    public string NotificationStatus { get; set; } = string.Empty;
    public DateTimeOffset ReportUpdatedAt { get; set; }
    public string State { get; set; } = string.Empty;
    public int AttemptCount { get; set; }
    public string? LockOwner { get; set; }
    public DateTimeOffset? LockedUntil { get; set; }
    public DateTimeOffset? DeliveredAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

internal sealed class TeamsCardActionSubmissionEntity
{
    public string ActionToken { get; set; } = string.Empty;
    public string RequestId { get; set; } = string.Empty;
    public string ActionType { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string? ResultRequestId { get; set; }
    public string? LockOwner { get; set; }
    public DateTimeOffset? LockedUntil { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public byte[] RowVersion { get; set; } = [];
}
