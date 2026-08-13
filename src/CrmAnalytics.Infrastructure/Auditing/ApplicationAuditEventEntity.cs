namespace CrmAnalytics.Infrastructure.Auditing;

internal sealed class ApplicationAuditEventEntity
{
    public string EventId { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public string Outcome { get; set; } = string.Empty;
    public DateTimeOffset OccurredAt { get; set; }
    public string? RequestId { get; set; }
    public string? PreviousRequestId { get; set; }
    public string? CorrelationId { get; set; }
    public string? ActorUserId { get; set; }
    public string? TenantId { get; set; }
    public string? ReportStatus { get; set; }
    public string? ReasonCode { get; set; }
    public string? DataSource { get; set; }
    public long? DurationMilliseconds { get; set; }
    public int? RowCount { get; set; }
    public bool? ResultTruncated { get; set; }
    public string? AuditMetadataJson { get; set; }
}
