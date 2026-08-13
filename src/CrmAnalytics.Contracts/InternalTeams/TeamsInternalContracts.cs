namespace CrmAnalytics.Contracts.InternalTeams;

using CrmAnalytics.Contracts.Notifications;

public sealed record RegisterTeamsTargetRequest
{
    public required string ConversationId { get; init; }
}

public sealed record TeamsTargetResponse
{
    public required string RequestId { get; init; }
    public required string ConversationId { get; init; }
}

public sealed record ClaimTeamsActionRequest
{
    public required string RequestId { get; init; }
    public required string ActionType { get; init; }
    public required string LockOwner { get; init; }
}

public sealed record ClaimTeamsActionResponse
{
    public required string Result { get; init; }
    public string? ResultRequestId { get; init; }
}

public sealed record CompleteTeamsActionRequest
{
    public required string LockOwner { get; init; }
    public string? ResultRequestId { get; init; }
}

public sealed record ReleaseTeamsActionRequest
{
    public required string LockOwner { get; init; }
}

public static class TeamsInternalHttpConstants
{
    public const string ApiKeyHeaderName = "X-CrmAnalytics-Notification-Key";
}

public sealed record InternalReportStatusNotificationRequest
{
    public string? DeliveryId { get; init; }
    public string? ConversationId { get; init; }
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
