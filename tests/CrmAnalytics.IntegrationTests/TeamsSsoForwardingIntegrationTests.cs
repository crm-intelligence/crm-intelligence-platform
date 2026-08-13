extern alias TeamsHost;

using CrmAnalytics.Contracts.ReportRequests;
using CrmAnalytics.Contracts.CopilotStudio;
using CrmAnalytics.Contracts.Integrations;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using BackendApiAuthorization =
    TeamsHost::CrmAnalytics.Teams.Authentication.BackendApiAuthorization;
using ITeamsBackendAuthorizationCoordinator =
    TeamsHost::CrmAnalytics.Teams.Authentication.ITeamsBackendAuthorizationCoordinator;
using IReportRequestsApiClient =
    TeamsHost::CrmAnalytics.Teams.Backend.IReportRequestsApiClient;
using ITeamsReportCardActionHandler =
    TeamsHost::CrmAnalytics.Teams.Messaging.ITeamsReportCardActionHandler;
using TeamsReportCardActionHandler =
    TeamsHost::CrmAnalytics.Teams.Messaging.TeamsReportCardActionHandler;
using TeamsReportRequestHandler =
    TeamsHost::CrmAnalytics.Teams.Messaging.TeamsReportRequestHandler;
using ITeamsNotificationTargetStore =
    TeamsHost::CrmAnalytics.Teams.Notifications.ITeamsNotificationTargetStore;
using TeamsNotificationTarget =
    TeamsHost::CrmAnalytics.Teams.Notifications.TeamsNotificationTarget;
using TeamsHostMarker =
    TeamsHost::CrmAnalytics.Teams.TeamsHostMarker;
using ICopilotStudioPlanningClient =
    TeamsHost::CrmAnalytics.Teams.Planning.ICopilotStudioPlanningClient;

namespace CrmAnalytics.IntegrationTests;

public sealed class TeamsSsoForwardingIntegrationTests
{
    [Fact]
    public async Task EntraHost_CreateForwardsFakeOpaqueToken()
    {
        const string accessToken = "integration-opaque-token";
        var api = new RecordingApiClient();
        using var factory = new EntraTeamsFactory(api);
        using var scope = factory.Services.CreateScope();
        var coordinator = scope.ServiceProvider.GetRequiredService<
            ITeamsBackendAuthorizationCoordinator>();
        var authorization = await coordinator.AuthorizeAsync(
            _ => Task.FromResult<string?>(accessToken),
            CancellationToken.None);
        var handler = scope.ServiceProvider
            .GetRequiredService<TeamsReportRequestHandler>();

        await handler.HandleAsync(
            "Quarterly sales",
            "conversation-1",
            authorization.Authorization!,
            CancellationToken.None);

        Assert.Equal(0, api.CreateCalls);
        Assert.Equal(1, api.PlannedCreateCalls);
        Assert.Equal(
            accessToken,
            api.LastAuthorization!.AccessToken);
    }

    [Fact]
    public async Task EntraHost_NullTokenDoesNotCallBackend()
    {
        var api = new RecordingApiClient();
        using var factory = new EntraTeamsFactory(api);
        using var scope = factory.Services.CreateScope();
        var coordinator = scope.ServiceProvider.GetRequiredService<
            ITeamsBackendAuthorizationCoordinator>();

        var result = await coordinator.AuthorizeAsync(
            _ => Task.FromResult<string?>(null),
            CancellationToken.None);

        Assert.False(result.IsAuthorized);
        Assert.Equal(0, api.CreateCalls);
        Assert.Equal(0, api.PlannedCreateCalls);
        Assert.Equal(0, api.ReviseCalls);
        Assert.Equal(0, api.ClarificationCalls);
    }

    [Fact]
    public async Task EntraHost_CardActionsForwardFakeOpaqueToken()
    {
        const string accessToken = "card-integration-token";
        var api = new RecordingApiClient();
        using var factory = new EntraTeamsFactory(api);
        using var scope = factory.Services.CreateScope();
        var targetStore = scope.ServiceProvider
            .GetRequiredService<ITeamsNotificationTargetStore>();
        await targetStore.SaveAsync(
            new TeamsNotificationTarget(
                "source-1",
                "conversation-1",
                DateTimeOffset.UtcNow),
            CancellationToken.None);
        var coordinator = scope.ServiceProvider.GetRequiredService<
            ITeamsBackendAuthorizationCoordinator>();
        var result = await coordinator.AuthorizeAsync(
            _ => Task.FromResult<string?>(accessToken),
            CancellationToken.None);
        var handler = scope.ServiceProvider
            .GetRequiredService<ITeamsReportCardActionHandler>();

        await handler.HandleAsync(
            TeamsReportCardActionHandler.ReviseVerb,
            new Dictionary<string, object?>
            {
                ["requestId"] = "source-1",
                ["actionToken"] = "revision-action",
                ["revisionPrompt"] = "Show net profit"
            },
            "conversation-1",
            result.Authorization!,
            CancellationToken.None);
        var revisionAuthorization = await api.PlannedRevisionCalled
            .WaitAsync(TimeSpan.FromSeconds(10));
        await targetStore.SaveAsync(
            new TeamsNotificationTarget(
                "clarification-1",
                "conversation-1",
                DateTimeOffset.UtcNow),
            CancellationToken.None);
        await handler.HandleAsync(
            TeamsReportCardActionHandler.ClarificationVerb,
            new Dictionary<string, object?>
            {
                ["requestId"] = "clarification-1",
                ["actionToken"] = "clarification-action",
                ["clarificationResponse"] = "First quarter"
            },
            "conversation-1",
            result.Authorization!,
            CancellationToken.None);
        var clarificationAuthorization = await api.PlannedClarificationCalled
            .WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(1, api.ReviseCalls);
        Assert.Equal(1, api.ClarificationCalls);
        Assert.Equal(accessToken, revisionAuthorization.AccessToken);
        Assert.Equal(accessToken, clarificationAuthorization.AccessToken);
    }

    private sealed class EntraTeamsFactory
        : WebApplicationFactory<TeamsHostMarker>
    {
        private readonly RecordingApiClient _api;

        public EntraTeamsFactory(RecordingApiClient api)
        {
            _api = api;
        }

        protected override void ConfigureWebHost(
            IWebHostBuilder builder)
        {
            builder.UseEnvironment(Environments.Production);
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["BackendApi:BaseUrl"] =
                            "https://backend.example",
                        ["BackendApi:TimeoutSeconds"] = "15",
                        ["ReportNotifications:Enabled"] = "false",
                        ["Teams:SkipAuth"] = "false",
                        ["Teams:ClientId"] =
                            "11111111-1111-4111-8111-111111111111",
                        ["Teams:ClientSecret"] = "test-only-secret",
                        ["Teams:TenantId"] =
                            "22222222-2222-4222-8222-222222222222",
                        ["TeamsUserAuthentication:Mode"] = "Entra",
                        ["TeamsUserAuthentication:"
                            + "OAuthConnectionName"] = "test-oauth"
                    });
            });
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IReportRequestsApiClient>();
                services.AddSingleton<IReportRequestsApiClient>(_api);
                services.RemoveAll<ICopilotStudioPlanningClient>();
                services.AddSingleton<ICopilotStudioPlanningClient>(
                    new SuccessfulPlanningClient());
            });
        }
    }

    private sealed class RecordingApiClient
        : IReportRequestsApiClient
    {
        private readonly TaskCompletionSource<BackendApiAuthorization>
            _plannedRevisionCalled = new(
                TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<BackendApiAuthorization>
            _plannedClarificationCalled = new(
                TaskCreationOptions.RunContinuationsAsynchronously);

        public int CreateCalls { get; private set; }
        public int PlannedCreateCalls { get; private set; }
        public int ReviseCalls { get; private set; }
        public int ClarificationCalls { get; private set; }
        public Task<BackendApiAuthorization> PlannedRevisionCalled =>
            _plannedRevisionCalled.Task;
        public Task<BackendApiAuthorization> PlannedClarificationCalled =>
            _plannedClarificationCalled.Task;
        public BackendApiAuthorization? LastAuthorization
        {
            get;
            private set;
        }

        public Task<CreateReportRequestResponse> CreateAsync(
            CreateReportRequestRequest request,
            BackendApiAuthorization authorization,
            CancellationToken cancellationToken)
        {
            CreateCalls++;
            LastAuthorization = authorization;
            return Task.FromResult(
                new CreateReportRequestResponse(
                    "request-1",
                    "Received",
                    "accepted"));
        }

        public Task<ReviseReportRequestResponse> ReviseAsync(
            string sourceRequestId,
            ReviseReportRequestRequest request,
            BackendApiAuthorization authorization,
            CancellationToken cancellationToken)
        {
            ReviseCalls++;
            LastAuthorization = authorization;
            return Task.FromResult(
                new ReviseReportRequestResponse(
                    "revision-1",
                    sourceRequestId,
                    "conversation-1",
                    "Received",
                    "accepted"));
        }

        public Task<ReportPlanningContextResponse?> GetPlanningContextAsync(
            string requestId,
            BackendApiAuthorization authorization,
            CancellationToken cancellationToken)
        {
            LastAuthorization = authorization;
            var clarification = requestId == "clarification-1";
            return Task.FromResult<ReportPlanningContextResponse?>(new(
                requestId,
                "conversation-1",
                "2018 mayis siparis sayisi",
                clarification ? "WaitingForClarification" : "Completed",
                clarification ? "Hangi yil?" : null,
                new SubmittedSemanticPlanningResult(
                    "accepted",
                    new SubmittedSemanticIntent(
                        "order_count", [], [],
                        new SubmittedDateIntent(
                            "absolute", null, null,
                            "2018-05-01", "2018-05-31", "none"),
                        null),
                    [], null)));
        }

        public Task<ReviseReportRequestResponse> RevisePlannedAsync(
            string sourceRequestId,
            CopilotPlannedRevisionRequest request,
            BackendApiAuthorization authorization,
            CancellationToken cancellationToken)
        {
            ReviseCalls++;
            LastAuthorization = authorization;
            _plannedRevisionCalled.TrySetResult(authorization);
            return Task.FromResult(new ReviseReportRequestResponse(
                "revision-1", sourceRequestId, "conversation-1",
                "Received", "accepted"));
        }

        public Task<CreateReportRequestResponse> CreatePlannedAsync(
            CopilotPlannedReportRequest request,
            BackendApiAuthorization authorization,
            CancellationToken cancellationToken)
        {
            PlannedCreateCalls++;
            LastAuthorization = authorization;
            return Task.FromResult(
                new CreateReportRequestResponse(
                    "request-1",
                    "Received",
                    "accepted"));
        }

        public Task<SubmitReportClarificationResponse>
            SubmitClarificationAsync(
                string requestId,
                SubmitReportClarificationRequest request,
                BackendApiAuthorization authorization,
                CancellationToken cancellationToken)
        {
            ClarificationCalls++;
            LastAuthorization = authorization;
            return Task.FromResult(
                new SubmitReportClarificationResponse(
                    requestId,
                    "WaitingForClarification",
                    "accepted"));
        }

        public Task<SubmitReportClarificationResponse>
            SubmitPlannedClarificationAsync(
                string requestId,
                CopilotPlannedClarificationRequest request,
                BackendApiAuthorization authorization,
                CancellationToken cancellationToken)
        {
            ClarificationCalls++;
            LastAuthorization = authorization;
            _plannedClarificationCalled.TrySetResult(authorization);
            return Task.FromResult(new SubmitReportClarificationResponse(
                requestId, "WaitingForClarification", "accepted"));
        }
    }

    private sealed class SuccessfulPlanningClient
        : ICopilotStudioPlanningClient
    {
        public Task<CopilotPlannedReportRequest> PlanAsync(
            string prompt,
            string conversationId,
            CancellationToken cancellationToken) =>
            Task.FromResult(new CopilotPlannedReportRequest
            {
                Prompt = prompt,
                ConversationId = conversationId,
                Outcome = "accepted",
                SemanticIntent = new CopilotSemanticIntent
                {
                    Metric = "order_count",
                    GroupBy = [],
                    Filters = [],
                    Date = new CopilotDateIntent
                    {
                        Kind = "unspecified",
                        Grain = "none"
                    },
                    Ranking = new CopilotRankingIntent()
                },
                UnresolvedConcepts = [],
                Clarification = new CopilotClarification()
            });
    }
}
