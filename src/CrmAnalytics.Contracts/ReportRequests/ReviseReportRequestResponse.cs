namespace CrmAnalytics.Contracts.ReportRequests;

public sealed record ReviseReportRequestResponse(
    string RequestId,
    string PreviousRequestId,
    string ConversationId,
    string Status,
    string Message);
