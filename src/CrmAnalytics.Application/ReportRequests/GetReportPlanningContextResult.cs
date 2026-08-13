using CrmAnalytics.Contracts.Integrations;
using CrmAnalytics.Domain.ReportRequests;

namespace CrmAnalytics.Application.ReportRequests;

public sealed record GetReportPlanningContextResult(
    string RequestId,
    string ConversationId,
    string OriginalRequest,
    ReportRequestStatus Status,
    string? ClarificationQuestion,
    SubmittedSemanticPlanningResult? CurrentSemanticPlan,
    string? CanonicalRequestJson);
