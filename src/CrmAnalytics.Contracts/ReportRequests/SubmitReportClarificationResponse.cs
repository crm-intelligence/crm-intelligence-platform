namespace CrmAnalytics.Contracts.ReportRequests;

public sealed record SubmitReportClarificationResponse(
    string RequestId,
    string Status,
    string Message);
