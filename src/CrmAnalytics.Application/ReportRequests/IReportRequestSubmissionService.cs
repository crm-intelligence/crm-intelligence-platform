using CrmAnalytics.Contracts.Integrations;
using CrmAnalytics.Application.Identity;

namespace CrmAnalytics.Application.ReportRequests;

public interface IReportRequestSubmissionService
{
    Task<CreateReportRequestResult> SubmitAsync(
        CreateReportRequestCommand command,
        UserDataScope userDataScope,
        CancellationToken cancellationToken);

    Task<CreateReportRequestResult> SubmitAsync(
        CreateReportRequestCommand command,
        AuthenticatedUserContext user,
        UserDataScope userDataScope,
        CancellationToken cancellationToken);

    Task<CreateReportRequestResult> SubmitPlannedAsync(
        CreateReportRequestCommand command,
        AuthenticatedUserContext user,
        UserDataScope userDataScope,
        SubmittedSemanticPlanningResult semanticPlan,
        CancellationToken cancellationToken);

    Task<ReviseReportRequestResult> ReviseAndSubmitAsync(
        ReviseReportRequestCommand command,
        UserDataScope userDataScope,
        CancellationToken cancellationToken);

    Task<ReviseReportRequestResult> RevisePlannedAndSubmitAsync(
        ReviseReportRequestCommand command,
        AuthenticatedUserContext user,
        UserDataScope userDataScope,
        SubmittedSemanticPlanningResult semanticPlan,
        CancellationToken cancellationToken);

    Task<ReviseReportRequestResult> ReviseAndSubmitAsync(
        ReviseReportRequestCommand command,
        AuthenticatedUserContext user,
        UserDataScope userDataScope,
        CancellationToken cancellationToken);

    Task<UpdateReportRequestResult> SubmitClarificationAndResumeAsync(
        SubmitReportClarificationCommand command,
        string correlationId,
        UserDataScope userDataScope,
        CancellationToken cancellationToken);

    Task<UpdateReportRequestResult> SubmitClarificationAndResumeAsync(
        SubmitReportClarificationCommand command,
        string correlationId,
        AuthenticatedUserContext user,
        UserDataScope userDataScope,
        CancellationToken cancellationToken);

    Task<UpdateReportRequestResult>
        SubmitPlannedClarificationAndResumeAsync(
            SubmitReportClarificationCommand command,
            string correlationId,
            AuthenticatedUserContext user,
            UserDataScope userDataScope,
            SubmittedSemanticPlanningResult semanticPlan,
            CancellationToken cancellationToken);
}
