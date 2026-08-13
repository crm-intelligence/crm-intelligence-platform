namespace CrmAnalytics.Application.Auditing;

public sealed record ApplicationAuditEvent
{
    public ApplicationAuditEvent(
        string eventId,
        ApplicationAuditEventType eventType,
        ApplicationAuditOutcome outcome,
        DateTimeOffset occurredAt,
        string? requestId,
        string? previousRequestId,
        string? correlationId,
        string? actorUserId,
        string? tenantId,
        string? reportStatus,
        string? reasonCode,
        string? dataSource = null,
        long? durationMilliseconds = null,
        int? rowCount = null,
        bool? resultTruncated = null,
        ApplicationAuditMetadata? auditMetadata = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventId);
        if (eventId.Trim().Length > 64)
        {
            throw new ArgumentException(
                "The audit event ID cannot exceed 64 characters.",
                nameof(eventId));
        }

        if (!Enum.IsDefined(eventType))
        {
            throw new ArgumentOutOfRangeException(nameof(eventType));
        }

        if (!Enum.IsDefined(outcome))
        {
            throw new ArgumentOutOfRangeException(nameof(outcome));
        }

        if (occurredAt.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException(
                "The audit occurrence time must be in UTC.",
                nameof(occurredAt));
        }

        if (string.IsNullOrWhiteSpace(requestId))
        {
            throw new ArgumentException(
                "The request ID is required for report audit events.",
                nameof(requestId));
        }

        if (reasonCode?.Trim().Length > 128)
        {
            throw new ArgumentException(
                "The audit reason code cannot exceed 128 characters.",
                nameof(reasonCode));
        }

        if (dataSource?.Trim().Length > 32)
        {
            throw new ArgumentException(
                "The audit data source cannot exceed 32 characters.",
                nameof(dataSource));
        }

        if (durationMilliseconds < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(durationMilliseconds));
        }

        if (rowCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(rowCount));
        }

        EventId = eventId.Trim();
        EventType = eventType;
        Outcome = outcome;
        OccurredAt = occurredAt;
        RequestId = Normalize(requestId);
        PreviousRequestId = Normalize(previousRequestId);
        CorrelationId = Normalize(correlationId);
        ActorUserId = Normalize(actorUserId);
        TenantId = Normalize(tenantId);
        ReportStatus = Normalize(reportStatus);
        ReasonCode = Normalize(reasonCode);
        DataSource = Normalize(dataSource);
        DurationMilliseconds = durationMilliseconds;
        RowCount = rowCount;
        ResultTruncated = resultTruncated;
        AuditMetadata = auditMetadata;
    }

    public string EventId { get; }
    public ApplicationAuditEventType EventType { get; }
    public ApplicationAuditOutcome Outcome { get; }
    public DateTimeOffset OccurredAt { get; }
    public string? RequestId { get; }
    public string? PreviousRequestId { get; }
    public string? CorrelationId { get; }
    public string? ActorUserId { get; }
    public string? TenantId { get; }
    public string? ReportStatus { get; }
    public string? ReasonCode { get; }
    public string? DataSource { get; }
    public long? DurationMilliseconds { get; }
    public int? RowCount { get; }
    public bool? ResultTruncated { get; }
    public ApplicationAuditMetadata? AuditMetadata { get; }

    public override string ToString() =>
        $"ApplicationAuditEvent {{ EventType = {EventType}, "
        + $"Outcome = {Outcome}, OccurredAt = {OccurredAt:O} }}";

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
