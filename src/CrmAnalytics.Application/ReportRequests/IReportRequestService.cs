using CrmAnalytics.Application.Identity;
using CrmAnalytics.Contracts.Integrations;

namespace CrmAnalytics.Application.ReportRequests;

public interface IReportRequestService
{
    Task<CreateReportRequestResult> CreateAsync(
        CreateReportRequestCommand command,
        CancellationToken cancellationToken);

    Task<CreateReportRequestResult> CreateAsync(
        CreateReportRequestCommand command,
        AuthenticatedUserContext user,
        CancellationToken cancellationToken) =>
        CreateAsync(command, cancellationToken);

    Task<CreateReportRequestResult> CreatePlannedAsync(
        CreateReportRequestCommand command,
        AuthenticatedUserContext user,
        SubmittedSemanticPlanningResult semanticPlan,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException(
            "Submitted semantic plans are not supported by this service.");

    Task<CreateReportRequestResult> CreatePlannedAwaitingAgenticAsync(
        CreateReportRequestCommand command,
        AuthenticatedUserContext user,
        SubmittedSemanticPlanningResult semanticPlan,
        string canonicalRequestJson,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException(
            "Agentic report creation is not supported by this service.");

    Task<CreateReportRequestResult> CreateRejectedPlannedAsync(
        CreateReportRequestCommand command,
        AuthenticatedUserContext user,
        SubmittedSemanticPlanningResult semanticPlan,
        string rejectionCode,
        string rejectionMessage,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException(
            "Routed report rejection is not supported by this service.");

    Task<ReviseReportRequestResult> ReviseAsync(
        ReviseReportRequestCommand command,
        CancellationToken cancellationToken);

    Task<ReviseReportRequestResult> ReviseAsync(
        ReviseReportRequestCommand command,
        AuthenticatedUserContext user,
        CancellationToken cancellationToken) =>
        ReviseAsync(command, cancellationToken);

    Task<ReviseReportRequestResult> RevisePlannedAsync(
        ReviseReportRequestCommand command,
        AuthenticatedUserContext user,
        SubmittedSemanticPlanningResult semanticPlan,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException(
            "Submitted revision plans are not supported by this service.");

    Task<ReviseReportRequestResult> RevisePlannedAwaitingAgenticAsync(
        ReviseReportRequestCommand command,
        AuthenticatedUserContext user,
        SubmittedSemanticPlanningResult semanticPlan,
        string canonicalRequestJson,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException(
            "Agentic report revision is not supported by this service.");

    Task<ReviseReportRequestResult> ReviseRejectedPlannedAsync(
        ReviseReportRequestCommand command,
        AuthenticatedUserContext user,
        SubmittedSemanticPlanningResult semanticPlan,
        string rejectionCode,
        string rejectionMessage,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException(
            "Routed revision rejection is not supported by this service.");

    Task<GetReportRequestResult?> GetByIdAsync(
        string requestId,
        CancellationToken cancellationToken);

    Task<GetReportRequestResult?> GetByIdAsync(
        string requestId,
        AuthenticatedUserContext user,
        CancellationToken cancellationToken) =>
        GetByIdAsync(requestId, cancellationToken);

    Task<GetReportPlanningContextResult?> GetPlanningContextAsync(
        string requestId,
        AuthenticatedUserContext user,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException(
            "Planning context is not supported by this service.");

    Task<UpdateReportRequestResult> TransitionStatusAsync(
        TransitionReportRequestStatusCommand command,
        CancellationToken cancellationToken);

    Task<UpdateReportRequestResult> CompleteAsync(
        CompleteReportRequestCommand command,
        CancellationToken cancellationToken);

    Task<UpdateReportRequestResult> RequestClarificationAsync(
        RequestReportClarificationCommand command,
        CancellationToken cancellationToken);

    Task<UpdateReportRequestResult> SubmitClarificationAsync(
        SubmitReportClarificationCommand command,
        CancellationToken cancellationToken);

    Task<UpdateReportRequestResult> SubmitClarificationAsync(
        SubmitReportClarificationCommand command,
        AuthenticatedUserContext user,
        CancellationToken cancellationToken) =>
        SubmitClarificationAsync(command, cancellationToken);

    Task<UpdateReportRequestResult> SubmitClarificationAsync(
        SubmitReportClarificationCommand command,
        string correlationId,
        CancellationToken cancellationToken) =>
        SubmitClarificationAsync(command, cancellationToken);

    Task<UpdateReportRequestResult> SubmitClarificationAsync(
        SubmitReportClarificationCommand command,
        string correlationId,
        AuthenticatedUserContext user,
        CancellationToken cancellationToken) =>
        SubmitClarificationAsync(command, user, cancellationToken);

    Task<UpdateReportRequestResult> SubmitPlannedClarificationAsync(
        SubmitReportClarificationCommand command,
        string correlationId,
        AuthenticatedUserContext user,
        SubmittedSemanticPlanningResult semanticPlan,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException(
            "Submitted clarification plans are not supported by this service.");

    Task<UpdateReportRequestResult>
        SubmitPlannedClarificationAwaitingAgenticAsync(
            SubmitReportClarificationCommand command,
            string correlationId,
            AuthenticatedUserContext user,
            SubmittedSemanticPlanningResult semanticPlan,
            string canonicalRequestJson,
            CancellationToken cancellationToken) =>
        throw new NotSupportedException(
            "Agentic clarification routing is not supported by this service.");

    Task<UpdateReportRequestResult> RejectPlannedClarificationAsync(
        SubmitReportClarificationCommand command,
        string correlationId,
        AuthenticatedUserContext user,
        SubmittedSemanticPlanningResult semanticPlan,
        string rejectionCode,
        string rejectionMessage,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException(
            "Routed clarification rejection is not supported by this service.");

    Task<UpdateReportRequestResult> FailAsync(
        FailReportRequestCommand command,
        CancellationToken cancellationToken);

    Task<UpdateReportRequestResult> RecordCanonicalRequestAsync(
        RecordCanonicalRequestCommand command,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException(
            "Canonical request recording is not supported by this service.");

    Task<UpdateReportRequestResult> RejectAsync(
        RejectReportRequestCommand command,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException(
            "Report request rejection is not supported by this service.");
}
