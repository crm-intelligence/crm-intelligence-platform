namespace CrmAnalytics.Application.ReportRequests;

public sealed record ReviseReportRequestCommand(
    string SourceRequestId,
    string Prompt,
    string CorrelationId);
