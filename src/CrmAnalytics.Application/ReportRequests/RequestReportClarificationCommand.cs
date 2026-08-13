namespace CrmAnalytics.Application.ReportRequests;

public sealed record RequestReportClarificationCommand(
    string RequestId,
    string ClarificationQuestion,
    DateTimeOffset RequestedAt);
