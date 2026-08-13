namespace CrmAnalytics.Contracts.ReportRequests;

public sealed record CreateReportRequestResponse(
    string RequestId,
    string Status,
    string Message);