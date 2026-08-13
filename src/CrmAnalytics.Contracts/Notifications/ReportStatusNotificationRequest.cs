namespace CrmAnalytics.Contracts.Notifications;

public sealed record ReportStatusNotificationRequest
{
    public required string RequestId { get; init; }

    public required ReportNotificationStatus Status { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }

    public required string CorrelationId { get; init; }

    public string? Summary { get; init; }

    public string? PowerBiUrl { get; init; }

    public ReportVisualizationPreview? VisualizationPreview { get; init; }

    public string? ClarificationQuestion { get; init; }

    public string? ErrorCode { get; init; }

    public string? RejectionMessage { get; init; }
}
