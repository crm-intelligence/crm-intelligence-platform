using CrmAnalytics.Domain.ReportRequests;

namespace CrmAnalytics.Application.ReportRequests;

public sealed record CreateReportRequestResult(
    string RequestId,
    ReportRequestStatus Status);