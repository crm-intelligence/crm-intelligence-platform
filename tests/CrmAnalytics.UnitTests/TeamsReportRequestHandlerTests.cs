using System.Collections.Concurrent;
using System.Net;
using CrmAnalytics.Contracts.CopilotStudio;
using CrmAnalytics.Contracts.ReportRequests;
using CrmAnalytics.Teams.Authentication;
using CrmAnalytics.Teams.Backend;
using CrmAnalytics.Teams.Messaging;
using CrmAnalytics.Teams.Notifications;
using CrmAnalytics.Teams.Planning;
using Microsoft.Extensions.Logging;

namespace CrmAnalytics.UnitTests;

public sealed class TeamsReportRequestHandlerTests
{
    [Fact]
    public async Task EmptyPrompt_DoesNotCallBackend()
    {
        var apiClient = new RecordingApiClient();
        var handler = CreateHandler(apiClient);

        var result = await handler.HandleAsync(
            "   ",
            "conversation-1",
            BackendApiAuthorization.Development(),
            CancellationToken.None);

        Assert.Equal(
            TeamsReportRequestHandler.EmptyPromptMessage,
            result.Message);
        Assert.Empty(apiClient.Requests);
    }

    [Fact]
    public async Task PromptOverMaximum_DoesNotCallBackend()
    {
        var apiClient = new RecordingApiClient();
        var handler = CreateHandler(apiClient);

        var result = await handler.HandleAsync(
            new string('a', 2001),
            "conversation-1",
            BackendApiAuthorization.Development(),
            CancellationToken.None);

        Assert.Equal(
            TeamsReportRequestHandler.PromptTooLongMessage,
            result.Message);
        Assert.Empty(apiClient.Requests);
    }

    [Fact]
    public async Task ValidPrompt_CallsPlannedBackendOnceWithActivityConversationId()
    {
        var apiClient = new RecordingApiClient();
        var handler = CreateHandler(apiClient);

        var result = await handler.HandleAsync(
            "  bölgesel satış raporu  ",
            "teams-conversation-42",
            BackendApiAuthorization.Development(),
            CancellationToken.None);

        var request = Assert.Single(apiClient.Requests);
        Assert.Equal("bölgesel satış raporu", request.Prompt);
        Assert.Equal(
            "teams-conversation-42",
            request.ConversationId);
        Assert.Null(request.PreviousRequestId);
        Assert.Equal("accepted", request.Outcome);
        Assert.False(
            apiClient.LastAuthorization!.RequiresBearerToken);
        Assert.Contains(
            "request-123",
            result.Message,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task AuthorizationFailure_ReturnsSafeMessage(
        HttpStatusCode statusCode)
    {
        const string token = "opaque-handler-token";
        var apiClient = new RecordingApiClient
        {
            Exception = new BackendApiException(statusCode)
        };
        var logger = new RecordingLogger<TeamsReportRequestHandler>();
        var handler = new TeamsReportRequestHandler(
            apiClient,
            new RecordingPlanningClient(),
            new InMemoryTeamsNotificationTargetStore(),
            logger);

        var result = await handler.HandleAsync(
            "confidential prompt",
            "conversation-1",
            BackendApiAuthorization.Bearer(token),
            CancellationToken.None);

        Assert.Equal(
            statusCode == HttpStatusCode.Forbidden
                ? TeamsReportRequestHandler.BackendForbiddenMessage
                : TeamsReportRequestHandler
                    .BackendAuthorizationFailureMessage,
            result.Message);
        Assert.Same(
            apiClient.LastAuthorization!.AccessToken,
            token);
        Assert.DoesNotContain(token, result.Message);
        Assert.DoesNotContain(
            logger.Entries,
            entry => entry.Contains(token, StringComparison.Ordinal));
    }

    [Fact]
    public async Task SuccessfulCreate_SavesRequestConversationMapping()
    {
        var apiClient = new RecordingApiClient();
        var store = new InMemoryTeamsNotificationTargetStore();
        var handler = new TeamsReportRequestHandler(
            apiClient,
            new RecordingPlanningClient(),
            store,
            new RecordingLogger<TeamsReportRequestHandler>());

        await handler.HandleAsync(
            "sales report",
            "  teams-conversation-42  ",
            BackendApiAuthorization.Development(),
            CancellationToken.None);

        var target = await store.GetByRequestIdAsync(
            "request-123",
            CancellationToken.None);
        Assert.NotNull(target);
        Assert.Equal(
            "teams-conversation-42",
            target.ConversationId);
        Assert.Equal(TimeSpan.Zero, target.RegisteredAt.Offset);
        Assert.Equal(
            ("request-123", "teams-conversation-42"),
            Assert.Single(apiClient.RegisteredTargets));
    }

    [Fact]
    public async Task StoreFailure_StillReturnsAcceptedReplyWithRequestId()
    {
        var logger = new RecordingLogger<TeamsReportRequestHandler>();
        var handler = new TeamsReportRequestHandler(
            new RecordingApiClient(),
            new RecordingPlanningClient(),
            new ThrowingTargetStore(),
            logger);

        var result = await handler.HandleAsync(
            "confidential prompt",
            "conversation-1",
            BackendApiAuthorization.Development(),
            CancellationToken.None);

        Assert.Contains("request-123", result.Message);
        Assert.Contains(
            logger.Entries,
            entry => entry.Contains(
                "request-123",
                StringComparison.Ordinal));
        Assert.DoesNotContain(
            logger.Entries,
            entry => entry.Contains(
                "confidential prompt",
                StringComparison.Ordinal));
    }

    [Fact]
    public async Task MissingConversationId_DoesNotCallBackend()
    {
        var apiClient = new RecordingApiClient();
        var handler = CreateHandler(apiClient);

        var result = await handler.HandleAsync(
            "sales report",
            null,
            BackendApiAuthorization.Development(),
            CancellationToken.None);

        Assert.Equal(
            TeamsReportRequestHandler.InvalidConversationMessage,
            result.Message);
        Assert.Empty(apiClient.Requests);
    }

    [Fact]
    public async Task BackendFailure_ReturnsSafeMessageAndDoesNotLogPrompt()
    {
        const string prompt = "highly confidential sales prompt";
        var apiClient = new RecordingApiClient
        {
            Exception = new BackendApiException(
                HttpStatusCode.InternalServerError)
        };
        var logger = new RecordingLogger<TeamsReportRequestHandler>();
        var handler = new TeamsReportRequestHandler(
            apiClient,
            new RecordingPlanningClient(),
            new InMemoryTeamsNotificationTargetStore(),
            logger);

        var result = await handler.HandleAsync(
            prompt,
            "conversation-1",
            BackendApiAuthorization.Development(),
            CancellationToken.None);

        Assert.Equal(
            TeamsReportRequestHandler.BackendFailureMessage,
            result.Message);
        Assert.DoesNotContain(
            logger.Entries,
            entry => entry.Contains(
                prompt,
                StringComparison.Ordinal));
        Assert.Contains(
            logger.Entries,
            entry =>
                entry.Contains("500", StringComparison.Ordinal));
        Assert.DoesNotContain(
            logger.Entries,
            entry => entry.Contains(
                "conversation-1",
                StringComparison.Ordinal));
    }

    [Fact]
    public async Task HttpTimeout_ReturnsBackendUnavailableMessage()
    {
        var apiClient = new RecordingApiClient
        {
            Exception = new TaskCanceledException("HTTP timeout")
        };
        var handler = CreateHandler(apiClient);

        var result = await handler.HandleAsync(
            "sales report",
            "conversation-1",
            BackendApiAuthorization.Development(),
            CancellationToken.None);

        Assert.Equal(
            TeamsReportRequestHandler.BackendUnavailableMessage,
            result.Message);
    }

    [Fact]
    public async Task MalformedCopilotPayload_DoesNotCallBackend()
    {
        var apiClient = new RecordingApiClient();
        var planningClient = new RecordingPlanningClient
        {
            Exception = new CopilotStudioPlanningException(
                "malformed semantic payload")
        };
        var handler = CreateHandler(apiClient, planningClient);

        var result = await handler.HandleAsync(
            "sales report",
            "conversation-1",
            BackendApiAuthorization.Bearer("teams-oauth-token"),
            CancellationToken.None);

        Assert.Equal(
            TeamsReportRequestHandler.PlanningFailureMessage,
            result.Message);
        Assert.Empty(apiClient.Requests);
    }

    [Fact]
    public async Task CopilotTimeout_ReturnsControlledTechnicalMessage()
    {
        var apiClient = new RecordingApiClient();
        var planningClient = new RecordingPlanningClient
        {
            Exception = new CopilotStudioPlanningTimeoutException(
                "timeout",
                new TaskCanceledException())
        };
        var handler = CreateHandler(apiClient, planningClient);

        var result = await handler.HandleAsync(
            "sales report",
            "conversation-1",
            BackendApiAuthorization.Development(),
            CancellationToken.None);

        Assert.Equal(
            TeamsReportRequestHandler.PlanningUnavailableMessage,
            result.Message);
        Assert.Empty(apiClient.Requests);
    }

    [Fact]
    public async Task UnsupportedCopilotPlan_IsForwardedToPlannedEndpoint()
    {
        var apiClient = new RecordingApiClient();
        var planningClient = new RecordingPlanningClient
        {
            Plan = CreatePlan("unsupported")
        };
        var handler = CreateHandler(apiClient, planningClient);

        await handler.HandleAsync(
            "unsupported report",
            "conversation-1",
            BackendApiAuthorization.Development(),
            CancellationToken.None);

        var request = Assert.Single(apiClient.Requests);
        Assert.Equal("unsupported", request.Outcome);
    }

    [Fact]
    public async Task TeamsBearerToken_IsPreservedOnlyForPlannedBackendCall()
    {
        const string token = "opaque-teams-oauth-token";
        var apiClient = new RecordingApiClient();
        var planningClient = new RecordingPlanningClient();
        var handler = CreateHandler(apiClient, planningClient);

        await handler.HandleAsync(
            "sales report",
            "conversation-1",
            BackendApiAuthorization.Bearer(token),
            CancellationToken.None);

        Assert.Equal(token, apiClient.LastAuthorization?.AccessToken);
        Assert.Equal("sales report", planningClient.LastPrompt);
        Assert.Equal("conversation-1", planningClient.LastConversationId);
    }

    [Fact]
    public async Task CallerCancellation_IsPropagated()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var apiClient = new RecordingApiClient
        {
            Exception = new TaskCanceledException(
                "shutdown",
                null,
                cancellation.Token)
        };
        var handler = CreateHandler(apiClient);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => handler.HandleAsync(
                "sales report",
                "conversation-1",
                BackendApiAuthorization.Development(),
                cancellation.Token));
    }

    private static TeamsReportRequestHandler CreateHandler(
        IReportRequestsApiClient apiClient,
        ICopilotStudioPlanningClient? planningClient = null)
    {
        return new TeamsReportRequestHandler(
            apiClient,
            planningClient ?? new RecordingPlanningClient(),
            new InMemoryTeamsNotificationTargetStore(),
            new RecordingLogger<TeamsReportRequestHandler>());
    }

    private sealed class RecordingApiClient
        : IReportRequestsApiClient
    {
        public ConcurrentQueue<CopilotPlannedReportRequest> Requests
        {
            get;
        } = new();

        public Exception? Exception { get; init; }
        public BackendApiAuthorization? LastAuthorization
        {
            get;
            private set;
        }

        public ConcurrentQueue<(string RequestId, string ConversationId)>
            RegisteredTargets { get; } = new();

        public Task<CreateReportRequestResponse> CreateAsync(
            CreateReportRequestRequest request,
            BackendApiAuthorization authorization,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException(
                "Initial Teams requests must use the planned endpoint.");

        public Task<CreateReportRequestResponse> CreatePlannedAsync(
            CopilotPlannedReportRequest request,
            BackendApiAuthorization authorization,
            CancellationToken cancellationToken)
        {
            Requests.Enqueue(request);
            LastAuthorization = authorization;

            if (Exception is not null)
            {
                return Task.FromException<CreateReportRequestResponse>(
                    Exception);
            }

            return Task.FromResult(
                new CreateReportRequestResponse(
                    "request-123",
                    "Received",
                    "accepted"));
        }

        public Task<ReviseReportRequestResponse> ReviseAsync(
            string sourceRequestId,
            ReviseReportRequestRequest request,
            BackendApiAuthorization authorization,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<SubmitReportClarificationResponse>
            SubmitClarificationAsync(
                string requestId,
                SubmitReportClarificationRequest request,
                BackendApiAuthorization authorization,
                CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task RegisterTargetAsync(
            string requestId,
            string conversationId,
            CancellationToken cancellationToken)
        {
            RegisteredTargets.Enqueue((requestId, conversationId));
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingPlanningClient
        : ICopilotStudioPlanningClient
    {
        public CopilotPlannedReportRequest Plan { get; init; } =
            CreatePlan("accepted");

        public Exception? Exception { get; init; }

        public string? LastPrompt { get; private set; }

        public string? LastConversationId { get; private set; }

        public Task<CopilotPlannedReportRequest> PlanAsync(
            string prompt,
            string conversationId,
            CancellationToken cancellationToken)
        {
            LastPrompt = prompt;
            LastConversationId = conversationId;
            return Exception is null
                ? Task.FromResult(Plan)
                : Task.FromException<CopilotPlannedReportRequest>(Exception);
        }
    }

    private static CopilotPlannedReportRequest CreatePlan(string outcome) =>
        new()
        {
            Prompt = "planner prompt must be replaced",
            ConversationId = "planner conversation must be replaced",
            Outcome = outcome,
            SemanticIntent = new CopilotSemanticIntent
            {
                Metric = outcome == "accepted" ? "order_count" : null,
                GroupBy = [],
                Filters = [],
                Date = new CopilotDateIntent
                {
                    Kind = "unspecified",
                    Grain = "none"
                },
                Ranking = new CopilotRankingIntent()
            },
            UnresolvedConcepts = outcome == "unsupported"
                ? [new CopilotUnresolvedConcept { Kind = "metric" }]
                : [],
            Clarification = new CopilotClarification { Kind = null }
        };

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public ConcurrentQueue<string> Entries { get; } = new();

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull =>
            null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Entries.Enqueue(formatter(state, exception));
        }
    }

    private sealed class ThrowingTargetStore
        : ITeamsNotificationTargetStore
    {
        public Task SaveAsync(
            TeamsNotificationTarget target,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("store unavailable");

        public Task<TeamsNotificationTarget?> GetByRequestIdAsync(
            string requestId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
