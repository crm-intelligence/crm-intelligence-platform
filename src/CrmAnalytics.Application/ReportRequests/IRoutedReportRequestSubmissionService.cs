using CrmAnalytics.Application.Identity;
using CrmAnalytics.Contracts.CopilotStudio;
using CrmAnalytics.Contracts.Integrations;
using CrmAnalytics.Contracts.SqlAgent;

namespace CrmAnalytics.Application.ReportRequests;

public interface IRoutedReportRequestSubmissionService
{
    Task<RoutedReportRequestSubmissionResult> SubmitPlannedAsync(
        CreateReportRequestCommand command,
        AuthenticatedUserContext user,
        UserDataScope userDataScope,
        SubmittedSemanticPlanningResult semanticPlan,
        CopilotSqlAgentIntent intent,
        CancellationToken cancellationToken);

    Task<RoutedReportRequestSubmissionResult> RevisePlannedAsync(
        ReviseReportRequestCommand command,
        AuthenticatedUserContext user,
        UserDataScope userDataScope,
        SubmittedSemanticPlanningResult semanticPlan,
        CopilotSqlAgentIntent intent,
        CancellationToken cancellationToken);

    Task<RoutedReportRequestSubmissionResult> SubmitPlannedClarificationAsync(
        SubmitReportClarificationCommand command,
        string correlationId,
        AuthenticatedUserContext user,
        UserDataScope userDataScope,
        SubmittedSemanticPlanningResult semanticPlan,
        CopilotSqlAgentIntent intent,
        CancellationToken cancellationToken);
}

public sealed record RoutedReportRequestSubmissionResult(
    CopilotRoutedReportResponse? Response,
    SqlAgentErrorResponse? Error,
    int StatusCode)
{
    public bool IsSuccessful => Response is not null && Error is null;
}
