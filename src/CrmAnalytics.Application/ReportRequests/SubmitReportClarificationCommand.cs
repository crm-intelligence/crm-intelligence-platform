namespace CrmAnalytics.Application.ReportRequests;

public sealed record SubmitReportClarificationCommand(
    string RequestId,
    string Response,
    DateTimeOffset SubmittedAt);
