using CrmAnalytics.Contracts.Integrations;

namespace CrmAnalytics.Application.ReportProcessing;

public sealed record ProcessReportRequestCommand(
    string RequestId,
    UserDataScope UserDataScope,
    int AttemptNumber = 1,
    string? DeliveryId = null,
    SubmittedSemanticPlanningResult? SemanticPlan = null);
