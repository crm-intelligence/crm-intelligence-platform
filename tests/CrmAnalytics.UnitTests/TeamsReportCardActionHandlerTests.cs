using System.Net;
using CrmAnalytics.Contracts.ReportRequests;
using CrmAnalytics.Contracts.CopilotStudio;
using CrmAnalytics.Contracts.Integrations;
using CrmAnalytics.Contracts.InternalTeams;
using CrmAnalytics.Teams.Authentication;
using CrmAnalytics.Teams.Backend;
using CrmAnalytics.Teams.Messaging;
using CrmAnalytics.Teams.Notifications;
using CrmAnalytics.Teams.Planning;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Teams.Cards;

namespace CrmAnalytics.UnitTests;

public sealed class TeamsReportCardActionHandlerTests
{
    [Fact]
    public async Task Revision_MapsNewRequestAndDuplicateCallsBackendOnce()
    {
        var api = new RecordingApiClient();
        var targets = new InMemoryTeamsNotificationTargetStore();
        var submissions = new InMemoryTeamsCardActionSubmissionStore();
        var revisionQueue = new RecordingRevisionActionQueue();
        await targets.SaveAsync(
            new TeamsNotificationTarget(
                "source-1",
                "conversation-1",
                DateTimeOffset.UtcNow),
            CancellationToken.None);
        var planner = new RecordingPlanningClient();
        var handler = CreateHandler(
            api,
            targets,
            planner,
            revisionQueue: revisionQueue,
            submissionStore: submissions);
        var data = new Dictionary<string, object?>
        {
            ["requestId"] = "source-1",
            ["actionToken"] = "token-1",
            ["revisionPrompt"] = "  Net kârı göster  "
        };

        var first = await handler.HandleAsync(
            TeamsReportCardActionHandler.ReviseVerb,
            data,
            "conversation-1",
            BackendApiAuthorization.Development(),
            CancellationToken.None);

        Assert.Equal(0, api.ReviseCalls);
        Assert.Equal(string.Empty, planner.LastPrompt);
        Assert.NotNull(revisionQueue.WorkItem);
        Assert.Equal("Net kârı göster", revisionQueue.WorkItem.RevisionPrompt);
        Assert.Contains("Revizyon talebiniz alındı", VisibleText(first));
        Assert.Contains("source-1", VisibleText(first));

        var processor = new TeamsRevisionActionProcessor(
            api,
            targets,
            submissions,
            planner,
            NullLogger<TeamsRevisionActionProcessor>.Instance);
        await processor.ProcessAsync(
            revisionQueue.WorkItem,
            CancellationToken.None);

        await handler.HandleAsync(
            TeamsReportCardActionHandler.ReviseVerb,
            data,
            "conversation-1",
            BackendApiAuthorization.Development(),
            CancellationToken.None);

        Assert.Equal(1, api.ReviseCalls);
        Assert.False(
            api.LastAuthorization!.RequiresBearerToken);
        Assert.Equal("Net kârı göster", api.RevisionPrompt);
        Assert.Contains("Mode: revision", planner.LastPrompt);
        Assert.Contains("Original user request:", planner.LastPrompt);
        Assert.Contains("Current semantic plan (PRIMARY baseline):",
            planner.LastPrompt);
        Assert.Contains("Revision instruction:", planner.LastPrompt);
        Assert.Contains("never a delta", planner.LastPrompt);
        Assert.NotNull(await targets.GetByRequestIdAsync(
            "revision-1",
            CancellationToken.None));
    }

    [Fact]
    public async Task Clarification_UsesSameRequestAndDoesNotCreateTarget()
    {
        var api = new RecordingApiClient();
        var targets = new InMemoryTeamsNotificationTargetStore();
        var submissions = new InMemoryTeamsCardActionSubmissionStore();
        var queue = new RecordingClarificationActionQueue();
        await targets.SaveAsync(
            new TeamsNotificationTarget(
                "request-1",
                "conversation-1",
                DateTimeOffset.UtcNow),
            CancellationToken.None);
        var planner = new RecordingPlanningClient();
        var handler = CreateHandler(
            api,
            targets,
            planner,
            queue,
            submissionStore: submissions);

        var card = await handler.HandleAsync(
            TeamsReportCardActionHandler.ClarificationVerb,
            new Dictionary<string, object?>
            {
                ["requestId"] = "request-1",
                ["actionToken"] = "token-2",
                ["clarificationResponse"] = "  2018 yılı  "
            },
            "conversation-1",
            BackendApiAuthorization.Development(),
            CancellationToken.None);

        Assert.Equal(0, api.ClarificationCalls);
        Assert.Equal(string.Empty, planner.LastPrompt);
        Assert.NotNull(queue.WorkItem);
        Assert.Equal("2018 yılı", queue.WorkItem.ClarificationResponse);
        Assert.Contains("request-1", VisibleText(card));

        var processor = new TeamsClarificationActionProcessor(
            api,
            submissions,
            planner,
            NullLogger<TeamsClarificationActionProcessor>.Instance);
        await processor.ProcessAsync(
            queue.WorkItem,
            CancellationToken.None);

        Assert.Equal(1, api.ClarificationCalls);
        Assert.False(
            api.LastAuthorization!.RequiresBearerToken);
        Assert.Equal("request-1", api.ClarificationRequestId);
        Assert.Contains("Mode: clarification", planner.LastPrompt);
        Assert.Contains("Clarification question:", planner.LastPrompt);
        Assert.Contains("User answer:", planner.LastPrompt);
        Assert.Contains(
            "Do not interpret the answer as a standalone request",
            planner.LastPrompt);
        Assert.Contains("mayis ayi siparis sayisini getir", planner.LastPrompt);
        Assert.Equal("2018 yılı", api.ClarificationResponse);
    }

    [Fact]
    public async Task TeamsBearer_IsNotIncludedInCopilotPlanningContext()
    {
        const string bearer = "opaque-teams-bearer";
        var api = new RecordingApiClient();
        var targets = new InMemoryTeamsNotificationTargetStore();
        await targets.SaveAsync(
            new TeamsNotificationTarget(
                "source-1", "conversation-1", DateTimeOffset.UtcNow),
            CancellationToken.None);
        var planner = new RecordingPlanningClient();
        var revisionQueue = new RecordingRevisionActionQueue();
        var handler = CreateHandler(
            api,
            targets,
            planner,
            revisionQueue: revisionQueue);

        await handler.HandleAsync(
            TeamsReportCardActionHandler.ReviseVerb,
            new Dictionary<string, object?>
            {
                ["requestId"] = "source-1",
                ["actionToken"] = "bearer-action",
                ["revisionPrompt"] = "kategori bazinda goster"
            },
            "conversation-1",
            BackendApiAuthorization.Bearer(bearer),
            CancellationToken.None);

        var processor = new TeamsRevisionActionProcessor(
            api,
            targets,
            new InMemoryTeamsCardActionSubmissionStore(),
            planner,
            NullLogger<TeamsRevisionActionProcessor>.Instance);
        await processor.ProcessAsync(
            Assert.IsType<TeamsRevisionActionWorkItem>(
                revisionQueue.WorkItem),
            CancellationToken.None);

        Assert.Equal(bearer, api.LastAuthorization?.AccessToken);
        Assert.DoesNotContain(bearer, planner.LastPrompt);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task AuthorizationFailure_InBackgroundIsRetryable(
        HttpStatusCode statusCode)
    {
        const string accessToken = "opaque-card-token";
        var api = new RecordingApiClient
        {
            Failure = new BackendApiException(statusCode)
        };
        var targets = new InMemoryTeamsNotificationTargetStore();
        await targets.SaveAsync(
            new TeamsNotificationTarget(
                "request-1",
                "conversation-1",
                DateTimeOffset.UtcNow),
            CancellationToken.None);
        var submissions =
            new InMemoryTeamsCardActionSubmissionStore();
        var revisionQueue = new RecordingRevisionActionQueue();
        var handler = new TeamsReportCardActionHandler(
            api,
            targets,
            submissions,
            new RecordingClarificationActionQueue(),
            revisionQueue,
            new RecordingPlanningClient(),
            NullLogger<TeamsReportCardActionHandler>.Instance);
        var data = new Dictionary<string, object?>
        {
            ["requestId"] = "request-1",
            ["actionToken"] = "retry-authorization",
            ["revisionPrompt"] = "Geçerli revizyon"
        };

        await handler.HandleAsync(
            TeamsReportCardActionHandler.ReviseVerb,
            data,
            "conversation-1",
            BackendApiAuthorization.Bearer(accessToken),
            CancellationToken.None);

        var processor = new TeamsRevisionActionProcessor(
            api,
            targets,
            submissions,
            new RecordingPlanningClient(),
            NullLogger<TeamsRevisionActionProcessor>.Instance);
        await processor.ProcessAsync(
            Assert.IsType<TeamsRevisionActionWorkItem>(
                revisionQueue.WorkItem),
            CancellationToken.None);

        Assert.False(await submissions.IsProcessedAsync(
            "retry-authorization",
            CancellationToken.None));
        Assert.Equal(1, api.ReleaseCalls);
        Assert.Equal(accessToken, api.LastAuthorization!.AccessToken);
    }

    [Theory]
    [InlineData(null, "conversation-1")]
    [InlineData("request-1", "other-conversation")]
    public async Task MissingMappingOrConversationMismatch_DoesNotCallBackend(
        string? mappedRequest,
        string conversationId)
    {
        var api = new RecordingApiClient();
        var targets = new InMemoryTeamsNotificationTargetStore();
        if (mappedRequest is not null)
        {
            await targets.SaveAsync(
                new TeamsNotificationTarget(
                    mappedRequest,
                    "conversation-1",
                    DateTimeOffset.UtcNow),
                CancellationToken.None);
        }
        var handler = CreateHandler(api, targets);

        await handler.HandleAsync(
            TeamsReportCardActionHandler.ReviseVerb,
            new Dictionary<string, object?>
            {
                ["requestId"] = "request-1",
                ["actionToken"] = "token-3",
                ["revisionPrompt"] = "Geçerli revizyon"
            },
            conversationId,
            BackendApiAuthorization.Development(),
            CancellationToken.None);

        Assert.Equal(0, api.ReviseCalls);
        Assert.Equal(0, api.ClarificationCalls);
    }

    [Fact]
    public async Task InvalidInputAndUnknownVerb_DoNotCallBackend()
    {
        var api = new RecordingApiClient();
        var targets = new InMemoryTeamsNotificationTargetStore();
        await targets.SaveAsync(
            new TeamsNotificationTarget(
                "request-1",
                "conversation-1",
                DateTimeOffset.UtcNow),
            CancellationToken.None);
        var handler = CreateHandler(api, targets);
        var data = new Dictionary<string, object?>
        {
            ["requestId"] = "request-1",
            ["actionToken"] = "token-4",
            ["revisionPrompt"] = "  "
        };

        await handler.HandleAsync(
            TeamsReportCardActionHandler.ReviseVerb,
            data,
            "conversation-1",
            BackendApiAuthorization.Development(),
            CancellationToken.None);
        await handler.HandleAsync(
            "unknown",
            data,
            "conversation-1",
            BackendApiAuthorization.Development(),
            CancellationToken.None);

        Assert.Equal(0, api.ReviseCalls);
    }

    [Fact]
    public async Task Revision_QueueFull_ReleasesActionAndReturnsFailure()
    {
        var api = new RecordingApiClient();
        var targets = new InMemoryTeamsNotificationTargetStore();
        await targets.SaveAsync(
            new TeamsNotificationTarget(
                "request-1",
                "conversation-1",
                DateTimeOffset.UtcNow),
            CancellationToken.None);
        var revisionQueue = new RecordingRevisionActionQueue
        {
            Accept = false
        };
        var handler = CreateHandler(
            api,
            targets,
            revisionQueue: revisionQueue);

        var card = await handler.HandleAsync(
            TeamsReportCardActionHandler.ReviseVerb,
            new Dictionary<string, object?>
            {
                ["requestId"] = "request-1",
                ["actionToken"] = "queue-full-token",
                ["revisionPrompt"] = "Geçerli revizyon"
            },
            "conversation-1",
            BackendApiAuthorization.Development(),
            CancellationToken.None);

        Assert.Equal(1, api.ReleaseCalls);
        Assert.DoesNotContain("Revizyon talebiniz alındı", VisibleText(card));
    }

    [Fact]
    public async Task BackendFailure_DoesNotMarkTokenProcessed()
    {
        var api = new RecordingApiClient
        {
            Failure = new BackendApiException(
                HttpStatusCode.ServiceUnavailable)
        };
        var targets = new InMemoryTeamsNotificationTargetStore();
        await targets.SaveAsync(
            new TeamsNotificationTarget(
                "request-1",
                "conversation-1",
                DateTimeOffset.UtcNow),
            CancellationToken.None);
        var store = new InMemoryTeamsCardActionSubmissionStore();
        var queue = new RecordingClarificationActionQueue();
        var handler = new TeamsReportCardActionHandler(
            api,
            targets,
            store,
            queue,
            new RecordingRevisionActionQueue(),
            new RecordingPlanningClient(),
            NullLogger<TeamsReportCardActionHandler>.Instance);

        await handler.HandleAsync(
            TeamsReportCardActionHandler.ClarificationVerb,
            new Dictionary<string, object?>
            {
                ["requestId"] = "request-1",
                ["actionToken"] = "retry-token",
                ["clarificationResponse"] = "Geçerli açıklama"
            },
            "conversation-1",
            BackendApiAuthorization.Development(),
            CancellationToken.None);

        var processor = new TeamsClarificationActionProcessor(
            api,
            store,
            new RecordingPlanningClient(),
            NullLogger<TeamsClarificationActionProcessor>.Instance);
        await processor.ProcessAsync(
            Assert.IsType<TeamsClarificationActionWorkItem>(queue.WorkItem),
            CancellationToken.None);

        Assert.False(await store.IsProcessedAsync(
            "retry-token",
            CancellationToken.None));
    }

    private static TeamsReportCardActionHandler CreateHandler(
        RecordingApiClient api,
        ITeamsNotificationTargetStore targets,
        ICopilotStudioPlanningClient? planningClient = null,
        ITeamsClarificationActionQueue? clarificationQueue = null,
        ITeamsRevisionActionQueue? revisionQueue = null,
        ITeamsCardActionSubmissionStore? submissionStore = null) =>
        new(
            api,
            targets,
            submissionStore ?? new InMemoryTeamsCardActionSubmissionStore(),
            clarificationQueue ?? new RecordingClarificationActionQueue(),
            revisionQueue ?? new RecordingRevisionActionQueue(),
            planningClient ?? new RecordingPlanningClient(),
            NullLogger<TeamsReportCardActionHandler>.Instance);

    private static string VisibleText(AdaptiveCard card) =>
        string.Join(
            "\n",
            card.Body!.Select(element => element switch
            {
                TextBlock text => text.Text,
                FactSet facts => string.Join(
                    " ",
                    facts.Facts!.Select(fact => fact.Value)),
                _ => string.Empty
            }));

    private sealed class RecordingApiClient : IReportRequestsApiClient
    {
        public int ReviseCalls { get; private set; }
        public int ClarificationCalls { get; private set; }
        public int ReleaseCalls { get; private set; }
        public string? RevisionPrompt { get; private set; }
        public string? ClarificationRequestId { get; private set; }
        public string? ClarificationResponse { get; private set; }
        public Exception? Failure { get; init; }
        public BackendApiAuthorization? LastAuthorization
        {
            get;
            private set;
        }

        public Task<CreateReportRequestResponse> CreateAsync(
            CreateReportRequestRequest request,
            BackendApiAuthorization authorization,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

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

        public Task<ReportPlanningContextResponse?> GetPlanningContextAsync(
            string requestId,
            BackendApiAuthorization authorization,
            CancellationToken cancellationToken)
        {
            LastAuthorization = authorization;
            if (Failure is not null)
            {
                return Task.FromException<ReportPlanningContextResponse?>(
                    Failure);
            }

            var clarification = requestId != "source-1";
            return Task.FromResult<ReportPlanningContextResponse?>(new(
                requestId,
                "conversation-1",
                "mayis ayi siparis sayisini getir",
                clarification ? "WaitingForClarification" : "Completed",
                clarification ? "Hangi yil?" : null,
                CurrentPlan()));
        }

        public Task<ReviseReportRequestResponse> RevisePlannedAsync(
            string sourceRequestId,
            CopilotPlannedRevisionRequest request,
            BackendApiAuthorization authorization,
            CancellationToken cancellationToken)
        {
            ReviseCalls++;
            LastAuthorization = authorization;
            RevisionPrompt = request.RevisionInstruction;
            if (Failure is not null)
            {
                return Task.FromException<ReviseReportRequestResponse>(
                    Failure);
            }

            return Task.FromResult(new ReviseReportRequestResponse(
                "revision-1",
                sourceRequestId,
                "conversation-1",
                "Received",
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
            ClarificationRequestId = requestId;
            ClarificationResponse = request.Answer;
            if (Failure is not null)
            {
                return Task.FromException<
                    SubmitReportClarificationResponse>(Failure);
            }

            return Task.FromResult(
                new SubmitReportClarificationResponse(
                    requestId,
                    "WaitingForClarification",
                    "accepted"));
        }

        public Task ReleaseActionAsync(
            string actionToken,
            ReleaseTeamsActionRequest request,
            CancellationToken cancellationToken)
        {
            ReleaseCalls++;
            return Task.CompletedTask;
        }

        private static SubmittedSemanticPlanningResult CurrentPlan() => new(
            "accepted",
            new SubmittedSemanticIntent(
                "order_count",
                [],
                [],
                new SubmittedDateIntent(
                    "absolute", null, null,
                    "2018-05-01", "2018-05-31", "none"),
                null),
            [],
            null);
    }

    private sealed class RecordingPlanningClient
        : ICopilotStudioPlanningClient
    {
        public string LastPrompt { get; private set; } = string.Empty;

        public Task<CopilotPlannedReportRequest> PlanAsync(
            string prompt,
            string conversationId,
            CancellationToken cancellationToken)
        {
            LastPrompt = prompt;
            return Task.FromResult(new CopilotPlannedReportRequest
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
                        Kind = "absolute",
                        From = "2018-05-01",
                        To = "2018-05-31",
                        Grain = "none"
                    },
                    Ranking = new CopilotRankingIntent()
                },
                UnresolvedConcepts = [],
                Clarification = new CopilotClarification()
            });
        }
    }

    private sealed class RecordingClarificationActionQueue
        : ITeamsClarificationActionQueue
    {
        public TeamsClarificationActionWorkItem? WorkItem { get; private set; }

        public bool TryEnqueue(TeamsClarificationActionWorkItem workItem)
        {
            WorkItem = workItem;
            return true;
        }

        public async IAsyncEnumerable<TeamsClarificationActionWorkItem>
            ReadAllAsync(
                [System.Runtime.CompilerServices.EnumeratorCancellation]
                CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            yield break;
        }
    }

    private sealed class RecordingRevisionActionQueue
        : ITeamsRevisionActionQueue
    {
        public bool Accept { get; init; } = true;

        public TeamsRevisionActionWorkItem? WorkItem { get; private set; }

        public bool TryEnqueue(TeamsRevisionActionWorkItem workItem)
        {
            if (Accept)
            {
                WorkItem = workItem;
            }

            return Accept;
        }

        public async IAsyncEnumerable<TeamsRevisionActionWorkItem>
            ReadAllAsync(
                [System.Runtime.CompilerServices.EnumeratorCancellation]
                CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            yield break;
        }
    }
}
