namespace CrmAnalytics.Contracts.ReportRequests;

public sealed record GetReportRequestResponse(
    string RequestId,
    string ConversationId,
    string? PreviousRequestId,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string CorrelationId,
    string? ReportId,
    string? Summary,
    string? PowerBiUrl,
    string? ClarificationQuestion,
    string? ErrorCode,
    string? ErrorMessage,
    string? RejectionMessage = null);
