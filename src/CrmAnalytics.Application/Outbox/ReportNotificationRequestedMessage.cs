using CrmAnalytics.Contracts.Notifications;

namespace CrmAnalytics.Application.Outbox;

public sealed record ReportNotificationRequestedMessage
{
    public const int LegacySchemaVersion = 1;
    public const int CurrentSchemaVersion = 2;

    public ReportNotificationRequestedMessage(
        string requestId,
        ReportNotificationStatus status,
        DateTimeOffset reportUpdatedAt,
        int schemaVersion = CurrentSchemaVersion,
        ReportVisualizationPreview? visualizationPreview = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestId);
        if (!Enum.IsDefined(status)) throw new ArgumentOutOfRangeException(nameof(status));
        if (reportUpdatedAt.Offset != TimeSpan.Zero)
            throw new ArgumentException("Report timestamp must be UTC.",
                nameof(reportUpdatedAt));
        if (schemaVersion is not LegacySchemaVersion
            and not CurrentSchemaVersion)
            throw new NotSupportedException("Unsupported notification schema version.");
        if (schemaVersion == LegacySchemaVersion
            && visualizationPreview is not null)
            throw new ArgumentException(
                "Legacy notification envelopes cannot carry a visualization preview.",
                nameof(visualizationPreview));
        RequestId = requestId.Trim();
        Status = status;
        ReportUpdatedAt = reportUpdatedAt;
        SchemaVersion = schemaVersion;
        VisualizationPreview = visualizationPreview;
    }

    public string RequestId { get; }
    public ReportNotificationStatus Status { get; }
    public DateTimeOffset ReportUpdatedAt { get; }
    public int SchemaVersion { get; }
    public ReportVisualizationPreview? VisualizationPreview { get; }
}
