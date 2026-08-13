using CrmAnalytics.Contracts.ReportRequests;
using CrmAnalytics.Teams.Authentication;
using CrmAnalytics.Contracts.InternalTeams;
using CrmAnalytics.Contracts.CopilotStudio;

namespace CrmAnalytics.Teams.Backend;

public interface IReportRequestsApiClient
{
    Task<CreateReportRequestResponse> CreateAsync(
        CreateReportRequestRequest request,
        BackendApiAuthorization authorization,
        CancellationToken cancellationToken);

    Task<CreateReportRequestResponse> CreatePlannedAsync(
        CopilotPlannedReportRequest request,
        BackendApiAuthorization authorization,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    Task<ReviseReportRequestResponse> ReviseAsync(
        string sourceRequestId,
        ReviseReportRequestRequest request,
        BackendApiAuthorization authorization,
        CancellationToken cancellationToken);

    Task<SubmitReportClarificationResponse> SubmitClarificationAsync(
        string requestId,
        SubmitReportClarificationRequest request,
        BackendApiAuthorization authorization,
        CancellationToken cancellationToken);

    Task<ReportPlanningContextResponse?> GetPlanningContextAsync(
        string requestId,
        BackendApiAuthorization authorization,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    Task<ReviseReportRequestResponse> RevisePlannedAsync(
        string sourceRequestId,
        CopilotPlannedRevisionRequest request,
        BackendApiAuthorization authorization,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    Task<SubmitReportClarificationResponse> SubmitPlannedClarificationAsync(
        string requestId,
        CopilotPlannedClarificationRequest request,
        BackendApiAuthorization authorization,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    Task RegisterTargetAsync(string requestId, string conversationId,
        CancellationToken cancellationToken) => Task.CompletedTask;

    Task<TeamsTargetResponse?> GetTargetAsync(string requestId,
        CancellationToken cancellationToken) => Task.FromResult<TeamsTargetResponse?>(null);

    Task<ClaimTeamsActionResponse?> ClaimActionAsync(string actionToken,
        ClaimTeamsActionRequest request, CancellationToken cancellationToken) =>
        Task.FromResult<ClaimTeamsActionResponse?>(null);

    Task CompleteActionAsync(string actionToken,
        CompleteTeamsActionRequest request, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    Task ReleaseActionAsync(string actionToken,
        ReleaseTeamsActionRequest request, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}
