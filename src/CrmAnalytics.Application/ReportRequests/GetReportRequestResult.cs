using CrmAnalytics.Domain.ReportRequests;

namespace CrmAnalytics.Application.ReportRequests;

public sealed record GetReportRequestResult(
    string RequestId,
    string ConversationId,
    string? PreviousRequestId,
    ReportRequestStatus Status,
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
