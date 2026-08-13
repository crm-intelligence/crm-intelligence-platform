using CrmAnalytics.Application.ReportProcessing;
using CrmAnalytics.Application.Identity;
using CrmAnalytics.Contracts.Integrations;

namespace CrmAnalytics.Application.ReportRequests;

public sealed class ReportRequestSubmissionService
    : IReportRequestSubmissionService
{
    private readonly IReportRequestService _reportRequestService;

    public ReportRequestSubmissionService(
        IReportRequestService reportRequestService)
    {
        ArgumentNullException.ThrowIfNull(reportRequestService);
        _reportRequestService = reportRequestService;
    }

    public ReportRequestSubmissionService(
        IReportRequestService reportRequestService,
        IReportProcessingQueue processingQueue,
        ReportProcessingQueueOptions queueOptions)
        : this(reportRequestService)
    {
        ArgumentNullException.ThrowIfNull(processingQueue);
        ArgumentNullException.ThrowIfNull(queueOptions);
    }

    public async Task<CreateReportRequestResult> SubmitAsync(
        CreateReportRequestCommand command,
        UserDataScope userDataScope,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(userDataScope);

        _ = UserDataScopeValidator.CreateSnapshot(userDataScope);
        return await _reportRequestService.CreateAsync(
            command,
            cancellationToken);
    }

    public async Task<CreateReportRequestResult> SubmitAsync(
        CreateReportRequestCommand command,
        AuthenticatedUserContext user,
        UserDataScope userDataScope,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(userDataScope);
        _ = UserDataScopeValidator.CreateRequiredSnapshot(userDataScope, user);

        return await _reportRequestService.CreateAsync(
            command,
            user,
            cancellationToken);
    }

    public async Task<CreateReportRequestResult> SubmitPlannedAsync(
        CreateReportRequestCommand command,
        AuthenticatedUserContext user,
        UserDataScope userDataScope,
        SubmittedSemanticPlanningResult semanticPlan,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(userDataScope);
        ArgumentNullException.ThrowIfNull(semanticPlan);
        _ = UserDataScopeValidator.CreateRequiredSnapshot(userDataScope, user);

        return await _reportRequestService.CreatePlannedAsync(
            command,
            user,
            semanticPlan,
            cancellationToken);
    }

    public async Task<ReviseReportRequestResult> ReviseAndSubmitAsync(
        ReviseReportRequestCommand command,
        UserDataScope userDataScope,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(userDataScope);

        _ = UserDataScopeValidator.CreateSnapshot(userDataScope);
        return await _reportRequestService.ReviseAsync(
            command,
            cancellationToken);
    }

    public async Task<ReviseReportRequestResult> ReviseAndSubmitAsync(
        ReviseReportRequestCommand command,
        AuthenticatedUserContext user,
        UserDataScope userDataScope,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(userDataScope);
        _ = UserDataScopeValidator.CreateRequiredSnapshot(userDataScope, user);

        return await _reportRequestService.ReviseAsync(
            command,
            user,
            cancellationToken);
    }

    public async Task<ReviseReportRequestResult> RevisePlannedAndSubmitAsync(
        ReviseReportRequestCommand command,
        AuthenticatedUserContext user,
        UserDataScope userDataScope,
        SubmittedSemanticPlanningResult semanticPlan,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(userDataScope);
        ArgumentNullException.ThrowIfNull(semanticPlan);
        _ = UserDataScopeValidator.CreateRequiredSnapshot(userDataScope, user);

        return await _reportRequestService.RevisePlannedAsync(
            command, user, semanticPlan, cancellationToken);
    }

    public async Task<UpdateReportRequestResult>
        SubmitClarificationAndResumeAsync(
            SubmitReportClarificationCommand command,
            string correlationId,
            UserDataScope userDataScope,
            CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);
        ArgumentNullException.ThrowIfNull(userDataScope);

        _ = UserDataScopeValidator.CreateSnapshot(userDataScope);
        return await _reportRequestService
            .SubmitClarificationAsync(
                command, correlationId, cancellationToken);
    }

    public async Task<UpdateReportRequestResult>
        SubmitClarificationAndResumeAsync(
            SubmitReportClarificationCommand command,
            string correlationId,
            AuthenticatedUserContext user,
            UserDataScope userDataScope,
            CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(userDataScope);
        _ = UserDataScopeValidator.CreateRequiredSnapshot(userDataScope, user);

        return await _reportRequestService
            .SubmitClarificationAsync(
                command, correlationId, user, cancellationToken);
    }

    public async Task<UpdateReportRequestResult>
        SubmitPlannedClarificationAndResumeAsync(
            SubmitReportClarificationCommand command,
            string correlationId,
            AuthenticatedUserContext user,
            UserDataScope userDataScope,
            SubmittedSemanticPlanningResult semanticPlan,
            CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(userDataScope);
        ArgumentNullException.ThrowIfNull(semanticPlan);
        _ = UserDataScopeValidator.CreateRequiredSnapshot(userDataScope, user);

        return await _reportRequestService.SubmitPlannedClarificationAsync(
            command, correlationId, user, semanticPlan, cancellationToken);
    }
}
