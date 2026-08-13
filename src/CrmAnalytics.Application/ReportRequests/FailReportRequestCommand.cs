namespace CrmAnalytics.Application.ReportRequests;

public sealed record FailReportRequestCommand(
    string RequestId,
    string ErrorCode,
    string ErrorMessage,
    DateTimeOffset FailedAt);
