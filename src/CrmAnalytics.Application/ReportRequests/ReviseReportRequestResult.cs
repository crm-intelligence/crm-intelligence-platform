using CrmAnalytics.Domain.ReportRequests;

namespace CrmAnalytics.Application.ReportRequests;

public sealed record ReviseReportRequestResult(
    string RequestId,
    string PreviousRequestId,
    string ConversationId,
    ReportRequestStatus Status,
    DateTimeOffset CreatedAt);
