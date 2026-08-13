using CrmAnalytics.Domain.ReportRequests;

namespace CrmAnalytics.Application.Conversations;

public sealed record ConversationReportRequestHistoryItem(
    string RequestId,
    string? PreviousRequestId,
    ReportRequestStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string? Summary,
    string? PowerBiUrl,
    string? ClarificationQuestion,
    string? RejectionMessage = null);
