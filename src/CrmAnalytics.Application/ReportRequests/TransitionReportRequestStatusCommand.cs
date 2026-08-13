using CrmAnalytics.Domain.ReportRequests;

namespace CrmAnalytics.Application.ReportRequests;

public sealed record TransitionReportRequestStatusCommand(
    string RequestId,
    ReportRequestStatus TargetStatus,
    DateTimeOffset TransitionedAt);
