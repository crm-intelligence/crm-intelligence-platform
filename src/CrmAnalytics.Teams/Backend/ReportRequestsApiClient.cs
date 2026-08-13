using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using CrmAnalytics.Contracts.ReportRequests;
using CrmAnalytics.Teams.Authentication;
using CrmAnalytics.Contracts.InternalTeams;
using CrmAnalytics.Contracts.CopilotStudio;

namespace CrmAnalytics.Teams.Backend;

public sealed class ReportRequestsApiClient : IReportRequestsApiClient
{
    public const string HttpClientName =
        "CrmAnalytics.Teams.ReportRequestsApi";

    private const string CreateRequestPath = "/api/report-requests";
    private const string CreatePlannedRequestPath =
        "/api/report-requests/planned";

    private readonly HttpClient _httpClient;

    public ReportRequestsApiClient(HttpClient httpClient)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        _httpClient = httpClient;
    }

    public async Task<CreateReportRequestResponse> CreateAsync(
        CreateReportRequestRequest request,
        BackendApiAuthorization authorization,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return await PostAcceptedAsync<
            CreateReportRequestRequest,
            CreateReportRequestResponse>(
            CreateRequestPath,
            request,
            authorization,
            cancellationToken);
    }

    public async Task<CreateReportRequestResponse> CreatePlannedAsync(
        CopilotPlannedReportRequest request,
        BackendApiAuthorization authorization,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return await PostAcceptedAsync<
            CopilotPlannedReportRequest,
            CreateReportRequestResponse>(
            CreatePlannedRequestPath,
            request,
            authorization,
            cancellationToken);
    }

    public Task<ReviseReportRequestResponse> ReviseAsync(
        string sourceRequestId,
        ReviseReportRequestRequest request,
        BackendApiAuthorization authorization,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceRequestId);
        ArgumentNullException.ThrowIfNull(request);

        return PostAcceptedAsync<
            ReviseReportRequestRequest,
            ReviseReportRequestResponse>(
                $"/api/report-requests/"
                    + $"{Uri.EscapeDataString(sourceRequestId.Trim())}"
                    + "/revise",
                request,
                authorization,
                cancellationToken);
    }

    public Task<SubmitReportClarificationResponse>
        SubmitClarificationAsync(
            string requestId,
            SubmitReportClarificationRequest request,
            BackendApiAuthorization authorization,
            CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestId);
        ArgumentNullException.ThrowIfNull(request);

        return PostAcceptedAsync<
            SubmitReportClarificationRequest,
            SubmitReportClarificationResponse>(
                $"/api/report-requests/"
                    + $"{Uri.EscapeDataString(requestId.Trim())}"
                    + "/clarifications",
                request,
                authorization,
                cancellationToken);
    }

    public async Task<ReportPlanningContextResponse?> GetPlanningContextAsync(
        string requestId,
        BackendApiAuthorization authorization,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestId);
        using var requestMessage = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/report-requests/{Uri.EscapeDataString(requestId.Trim())}/planning-context");
        AddAuthorization(requestMessage, authorization);
        using var response = await _httpClient.SendAsync(
            requestMessage, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        if (response.StatusCode != HttpStatusCode.OK)
            throw new BackendApiException(response.StatusCode);
        return await response.Content
            .ReadFromJsonAsync<ReportPlanningContextResponse>(
                cancellationToken: cancellationToken);
    }

    public Task<ReviseReportRequestResponse> RevisePlannedAsync(
        string sourceRequestId,
        CopilotPlannedRevisionRequest request,
        BackendApiAuthorization authorization,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceRequestId);
        ArgumentNullException.ThrowIfNull(request);
        return PostAcceptedAsync<
            CopilotPlannedRevisionRequest,
            ReviseReportRequestResponse>(
                $"/api/report-requests/{Uri.EscapeDataString(sourceRequestId.Trim())}/planned-revision",
                request,
                authorization,
                cancellationToken);
    }

    public Task<SubmitReportClarificationResponse>
        SubmitPlannedClarificationAsync(
            string requestId,
            CopilotPlannedClarificationRequest request,
            BackendApiAuthorization authorization,
            CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestId);
        ArgumentNullException.ThrowIfNull(request);
        return PostAcceptedAsync<
            CopilotPlannedClarificationRequest,
            SubmitReportClarificationResponse>(
                $"/api/report-requests/{Uri.EscapeDataString(requestId.Trim())}/planned-clarification",
                request,
                authorization,
                cancellationToken);
    }

    public async Task RegisterTargetAsync(string requestId,
        string conversationId, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.PutAsJsonAsync(
            $"/api/internal/teams-targets/{Uri.EscapeDataString(requestId)}",
            new RegisterTeamsTargetRequest { ConversationId = conversationId },
            cancellationToken);
        if (response.StatusCode != HttpStatusCode.NoContent)
            throw new BackendApiException(response.StatusCode);
    }

    public async Task<TeamsTargetResponse?> GetTargetAsync(string requestId,
        CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(
            $"/api/internal/teams-targets/{Uri.EscapeDataString(requestId)}",
            cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        if (response.StatusCode != HttpStatusCode.OK)
            throw new BackendApiException(response.StatusCode);
        return await response.Content.ReadFromJsonAsync<TeamsTargetResponse>(
            cancellationToken: cancellationToken);
    }

    public Task<ClaimTeamsActionResponse?> ClaimActionAsync(
        string actionToken, ClaimTeamsActionRequest request,
        CancellationToken cancellationToken) => PostInternalAsync<
            ClaimTeamsActionRequest, ClaimTeamsActionResponse>(
            actionToken, "claim", request, cancellationToken);

    public async Task CompleteActionAsync(string actionToken,
        CompleteTeamsActionRequest request,
        CancellationToken cancellationToken) => await PostInternalNoContentAsync(
            actionToken, "complete", request, cancellationToken);

    public async Task ReleaseActionAsync(string actionToken,
        ReleaseTeamsActionRequest request,
        CancellationToken cancellationToken) => await PostInternalNoContentAsync(
            actionToken, "release", request, cancellationToken);

    private async Task<TResponse?> PostInternalAsync<TRequest, TResponse>(
        string token, string operation, TRequest request,
        CancellationToken cancellationToken)
    {
        using var response = await _httpClient.PostAsJsonAsync(
            $"/api/internal/teams-actions/{Uri.EscapeDataString(token)}/{operation}",
            request, cancellationToken);
        if (response.StatusCode != HttpStatusCode.OK)
            throw new BackendApiException(response.StatusCode);
        return await response.Content.ReadFromJsonAsync<TResponse>(
            cancellationToken: cancellationToken);
    }

    private async Task PostInternalNoContentAsync<TRequest>(string token,
        string operation, TRequest request,
        CancellationToken cancellationToken)
    {
        using var response = await _httpClient.PostAsJsonAsync(
            $"/api/internal/teams-actions/{Uri.EscapeDataString(token)}/{operation}",
            request, cancellationToken);
        if (response.StatusCode != HttpStatusCode.NoContent)
            throw new BackendApiException(response.StatusCode);
    }

    private async Task<TResponse> PostAcceptedAsync<TRequest, TResponse>(
        string path,
        TRequest request,
        BackendApiAuthorization authorization,
        CancellationToken cancellationToken)
    {
        using var requestMessage = new HttpRequestMessage(
            HttpMethod.Post,
            path)
        {
            Content = JsonContent.Create(request)
        };

        AddAuthorization(requestMessage, authorization);

        using var response = await _httpClient.SendAsync(
            requestMessage,
            cancellationToken);

        if (response.StatusCode != HttpStatusCode.Accepted)
        {
            throw new BackendApiException(response.StatusCode);
        }

        var result = await response.Content.ReadFromJsonAsync<TResponse>(
            cancellationToken: cancellationToken);

        return result
            ?? throw new BackendApiException(response.StatusCode);
    }

    private static void AddAuthorization(
        HttpRequestMessage requestMessage,
        BackendApiAuthorization authorization)
    {
        ArgumentNullException.ThrowIfNull(requestMessage);
        ArgumentNullException.ThrowIfNull(authorization);
        if (authorization.RequiresBearerToken)
        {
            if (string.IsNullOrWhiteSpace(authorization.AccessToken))
            {
                throw new InvalidOperationException(
                    "Bearer authorization requires an access token.");
            }

            requestMessage.Headers.Authorization =
                new AuthenticationHeaderValue(
                    "Bearer",
                    authorization.AccessToken);
        }
    }
}
