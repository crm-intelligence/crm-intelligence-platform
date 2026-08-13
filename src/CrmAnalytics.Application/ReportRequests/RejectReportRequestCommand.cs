namespace CrmAnalytics.Application.ReportRequests;

public sealed record RejectReportRequestCommand(
    string RequestId,
    string RejectionCode,
    string RejectionMessage,
    DateTimeOffset RejectedAt);
