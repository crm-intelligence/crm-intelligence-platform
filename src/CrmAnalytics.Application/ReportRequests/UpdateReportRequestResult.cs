using CrmAnalytics.Domain.ReportRequests;

namespace CrmAnalytics.Application.ReportRequests;

public sealed record UpdateReportRequestResult(
    string RequestId,
    ReportRequestStatus Status,
    DateTimeOffset UpdatedAt,
    string? RejectionCode = null,
    string? RejectionMessage = null);
