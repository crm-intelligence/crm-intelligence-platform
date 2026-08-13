namespace CrmAnalytics.Infrastructure.Outbox;

internal sealed class OutboxMessageEntity
{
    public string MessageId { get; set; } = string.Empty;
    public string MessageType { get; set; } = string.Empty;
    public string AggregateId { get; set; } = string.Empty;
    public DateTimeOffset OccurredAt { get; set; }
    public string PayloadJson { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int AttemptCount { get; set; }
    public DateTimeOffset NextAttemptAt { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
    public DateTimeOffset? DeadLetteredAt { get; set; }
    public string? LockOwner { get; set; }
    public DateTimeOffset? LockedUntil { get; set; }
    public string? LastFailureCode { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public byte[] RowVersion { get; set; } = [];
}
