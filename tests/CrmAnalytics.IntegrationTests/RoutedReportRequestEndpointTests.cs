using System.Net;
using System.Net.Http.Json;
using Crm.Analytics.Sql.Audit;
using Crm.Analytics.Sql.Contracts;
using Crm.Analytics.Sql.Service;
using CrmAnalytics.Application.Abstractions.Integrations;
using CrmAnalytics.Application.Abstractions.Persistence;
using CrmAnalytics.Application.Outbox;
using CrmAnalytics.Application.SqlAgent;
using CrmAnalytics.Contracts.CopilotStudio;
using CrmAnalytics.Contracts.SqlAgent;
using CrmAnalytics.Domain.ReportRequests;
using CrmAnalytics.Infrastructure.Outbox;
using CrmAnalytics.Infrastructure.Integrations;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CrmAnalytics.IntegrationTests;

public sealed class RoutedReportRequestEndpointTests
{
    private const string OwnerUser =
        "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa";
    private const string OwnerTenant =
        "22222222-2222-4222-8222-222222222222";
    private const string OtherUser =
        "11111111-1111-4111-8111-111111111111";
    private const string OtherTenant =
        "bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb";

    [Fact]
    public async Task DeterministicInitial_RoutesThenWritesExactlyOneLegacyPayload()
    {
        await using var baseFactory = new SecuredTestWebApplicationFactory();
        await using var factory = RoutedFactory(baseFactory);
        using var client = OwnerClient(factory);
        var request = InitialRequest(agentic: false);

        var response = await client.PostAsJsonAsync(
            "/api/report-requests/planned-routed", request);
        var routed = await response.Content
            .ReadFromJsonAsync<CopilotRoutedReportResponse>();

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.NotNull(routed);
        Assert.Equal("Received", routed.RequestStatus);
        Assert.Equal("succeeded", routed.Capability.Status);
        Assert.Equal("deterministic", routed.Capability.Outcome);
        Assert.Single(ProcessingMessages(factory.Services, routed.RequestId));
        var message = Assert.Single(
            ProcessingMessages(factory.Services, routed.RequestId));
        var serializer = factory.Services
            .GetRequiredService<IOutboxMessageSerializer>();
        var payload = serializer.DeserializeReportProcessingRequested(
            message.Message.PayloadJson);
        Assert.Equal(routed.RequestId, payload.RequestId);
        Assert.Equal("accepted", payload.SemanticPlan?.Outcome);
        Assert.Equal("order_count",
            payload.SemanticPlan?.SemanticIntent?.Metric);

        await Task.Delay(100);
        Assert.Single(ProcessingMessages(factory.Services, routed.RequestId));
    }

    [Fact]
    public async Task AgenticInitial_BindsToolsAndWritesNoProcessingMessage()
    {
        await using var baseFactory = new SecuredTestWebApplicationFactory();
        await using var factory = RoutedFactory(baseFactory);
        using var owner = OwnerClient(factory);
        var request = InitialRequest(agentic: true);

        var response = await owner.PostAsJsonAsync(
            "/api/report-requests/planned-routed", request);
        var routed = await response.Content
            .ReadFromJsonAsync<CopilotRoutedReportResponse>();

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.NotNull(routed);
        Assert.Equal("Processing", routed.RequestStatus);
        Assert.Equal("agentic_required", routed.Capability.Outcome);
        Assert.Equal(64, routed.Capability.ContextFingerprint?.Length);
        Assert.Empty(ProcessingMessages(factory.Services, routed.RequestId));
        var report = await GetReportAsync(factory.Services, routed.RequestId);
        Assert.NotNull(report.CanonicalRequestJson);
        Assert.NotNull(report.SemanticPlanJson);

        var contextResponse = await owner.PostAsJsonAsync(
            "/api/sql-agent/tools/query-context",
            new SqlAgentIntentRequest
            {
                RequestId = routed.RequestId,
                Intent = request.Intent
            });
        var context = await contextResponse.Content
            .ReadFromJsonAsync<SqlAgentQueryContextResponse>();
        Assert.Equal(HttpStatusCode.OK, contextResponse.StatusCode);
        Assert.Equal(
            routed.Capability.ContextFingerprint,
            context?.ContextFingerprint);

        using var other = AuthenticatedClient(
            factory, OtherUser, OtherTenant);
        var hidden = await other.PostAsJsonAsync(
            "/api/sql-agent/tools/query-context",
            new SqlAgentIntentRequest
            {
                RequestId = routed.RequestId,
                Intent = request.Intent
            });
        Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
    }

    [Fact]
    public async Task UnsupportedInitial_IsRejectedWithoutProcessing()
    {
        await using var baseFactory = new SecuredTestWebApplicationFactory();
        await using var factory = RoutedFactory(baseFactory);
        using var client = OwnerClient(factory);
        var request = InitialRequest(
            agentic: false,
            metric: "unknown_metric");

        var response = await client.PostAsJsonAsync(
            "/api/report-requests/planned-routed", request);
        var routed = await response.Content
            .ReadFromJsonAsync<CopilotRoutedReportResponse>();

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.NotNull(routed);
        Assert.Equal("Rejected", routed.RequestStatus);
        Assert.Equal("unsupported", routed.Capability.Status);
        Assert.Equal("unsupported", routed.Capability.Outcome);
        Assert.Contains("unknown_metric", routed.Capability.Reasons);
        Assert.Null(routed.Capability.ContextFingerprint);
        Assert.Empty(ProcessingMessages(factory.Services, routed.RequestId));
        Assert.Equal(
            ReportRequestStatus.Rejected,
            (await GetReportAsync(factory.Services, routed.RequestId)).Status);
    }

    [Theory]
    [InlineData("item_sales")]
    [InlineData("unknown_metric")]
    public async Task SemanticMismatch_FailsClosedBeforePersistenceOrDispatch(
        string v2Metric)
    {
        await using var baseFactory = new SecuredTestWebApplicationFactory();
        await using var factory = RoutedFactory(baseFactory);
        using var client = OwnerClient(factory);
        var baseline = InitialRequest(agentic: false);
        var request = new CopilotRoutedPlannedReportRequest
        {
            Prompt = baseline.Prompt,
            ConversationId = baseline.ConversationId,
            Plan = baseline.Plan,
            Intent = Intent(agentic: false, metric: v2Metric)
        };

        var response = await client.PostAsJsonAsync(
            "/api/report-requests/planned-routed", request);
        var error = await response.Content
            .ReadFromJsonAsync<SqlAgentErrorResponse>();

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("rejected", error?.Status);
        Assert.Equal("INTENT_REQUEST_MISMATCH", error?.ReasonCode);
        Assert.Empty(ProcessingMessages(factory.Services));
        using var scope = factory.Services.CreateScope();
        var reports = await scope.ServiceProvider
            .GetRequiredService<IReportRequestRepository>()
            .GetByConversationIdAsync(
                request.ConversationId, CancellationToken.None);
        Assert.Empty(reports);
    }

    [Theory]
    [InlineData(false, "deterministic", 1)]
    [InlineData(true, "agentic_required", 0)]
    public async Task RoutedRevision_PreservesLineageAndDispatchesByCapability(
        bool agentic,
        string outcome,
        int expectedProcessingMessages)
    {
        await using var baseFactory = new SecuredTestWebApplicationFactory();
        await using var factory = RoutedFactory(baseFactory);
        var source = await SeedCompletedAsync(factory.Services);
        using var client = OwnerClient(factory);

        var response = await client.PostAsJsonAsync(
            $"/api/report-requests/{source.RequestId}/planned-revision-routed",
            new CopilotRoutedPlannedRevisionRequest
            {
                RevisionInstruction = "Use the newly planned report.",
                Plan = Plan(),
                Intent = Intent(agentic)
            });
        var routed = await response.Content
            .ReadFromJsonAsync<CopilotRoutedReportResponse>();

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.NotNull(routed);
        Assert.NotEqual(source.RequestId, routed.RequestId);
        Assert.Equal(source.RequestId, routed.PreviousRequestId);
        Assert.Equal(source.ConversationId, routed.ConversationId);
        Assert.Equal(outcome, routed.Capability.Outcome);
        Assert.Equal(
            expectedProcessingMessages,
            ProcessingMessages(factory.Services, routed.RequestId).Count);
        var revision = await GetReportAsync(
            factory.Services, routed.RequestId);
        Assert.Equal(source.RequestId, revision.PreviousRequestId);
        Assert.Equal(source.ConversationId, revision.ConversationId);
        Assert.Null(revision.ReportId);
        Assert.Null(revision.Summary);
        Assert.Null(revision.PowerBiUrl);
        if (agentic)
        {
            Assert.Equal(ReportRequestStatus.Processing, revision.Status);
            Assert.Equal(64, routed.Capability.ContextFingerprint?.Length);
        }
    }

    [Fact]
    public async Task RoutedRevisionMismatch_WritesNoRevisionOrProcessingMessage()
    {
        await using var baseFactory = new SecuredTestWebApplicationFactory();
        await using var factory = RoutedFactory(baseFactory);
        var source = await SeedCompletedAsync(factory.Services);
        using var client = OwnerClient(factory);

        var response = await client.PostAsJsonAsync(
            $"/api/report-requests/{source.RequestId}/planned-revision-routed",
            new CopilotRoutedPlannedRevisionRequest
            {
                RevisionInstruction = "Mismatch the request.",
                Plan = Plan(),
                Intent = Intent(false, "item_sales")
            });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Empty(ProcessingMessages(factory.Services));
        using var scope = factory.Services.CreateScope();
        var history = await scope.ServiceProvider
            .GetRequiredService<IReportRequestRepository>()
            .GetByConversationIdAsync(
                source.ConversationId, CancellationToken.None);
        Assert.Single(history);
    }

    [Fact]
    public async Task RoutedRevisionUnsupported_PersistsRejectedWithoutDispatch()
    {
        await using var baseFactory = new SecuredTestWebApplicationFactory();
        await using var factory = RoutedFactory(baseFactory);
        var source = await SeedCompletedAsync(factory.Services);
        using var client = OwnerClient(factory);

        var response = await client.PostAsJsonAsync(
            $"/api/report-requests/{source.RequestId}/planned-revision-routed",
            new CopilotRoutedPlannedRevisionRequest
            {
                RevisionInstruction = "Use an unsupported metric.",
                Plan = Plan("unknown_metric"),
                Intent = Intent(false, "unknown_metric")
            });
        var routed = await response.Content
            .ReadFromJsonAsync<CopilotRoutedReportResponse>();

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal("unsupported", routed?.Capability.Outcome);
        Assert.Equal("Rejected", routed?.RequestStatus);
        Assert.Equal(source.RequestId, routed?.PreviousRequestId);
        Assert.Empty(ProcessingMessages(factory.Services));
    }

    [Theory]
    [InlineData(false, "deterministic", 1)]
    [InlineData(true, "agentic_required", 0)]
    public async Task RoutedClarification_DispatchesOnlyDeterministic(
        bool agentic,
        string outcome,
        int expectedProcessingMessages)
    {
        await using var baseFactory = new SecuredTestWebApplicationFactory();
        await using var factory = RoutedFactory(baseFactory);
        var waiting = await SeedWaitingAsync(factory.Services);
        using var client = OwnerClient(factory);

        var response = await client.PostAsJsonAsync(
            $"/api/report-requests/{waiting.RequestId}/planned-clarification-routed",
            new CopilotRoutedPlannedClarificationRequest
            {
                Answer = "Use the 2018 calendar year.",
                Plan = Plan(),
                Intent = Intent(agentic)
            });
        var routed = await response.Content
            .ReadFromJsonAsync<CopilotRoutedReportResponse>();

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.NotNull(routed);
        Assert.Equal(waiting.RequestId, routed.RequestId);
        Assert.Equal(outcome, routed.Capability.Outcome);
        Assert.Equal(
            expectedProcessingMessages,
            ProcessingMessages(factory.Services, waiting.RequestId).Count);
        var persisted = await GetReportAsync(
            factory.Services, waiting.RequestId);
        Assert.Equal("Use the 2018 calendar year.",
            persisted.ClarificationResponse);
        if (agentic)
        {
            Assert.Equal(ReportRequestStatus.Processing, persisted.Status);
            Assert.NotNull(persisted.CanonicalRequestJson);
        }
    }

    [Fact]
    public async Task RoutedClarificationUnsupported_RejectsWithoutDispatch()
    {
        await using var baseFactory = new SecuredTestWebApplicationFactory();
        await using var factory = RoutedFactory(baseFactory);
        var waiting = await SeedWaitingAsync(factory.Services);
        using var client = OwnerClient(factory);

        var response = await client.PostAsJsonAsync(
            $"/api/report-requests/{waiting.RequestId}/planned-clarification-routed",
            new CopilotRoutedPlannedClarificationRequest
            {
                Answer = "Use the unsupported measure.",
                Plan = Plan("unknown_metric"),
                Intent = Intent(false, "unknown_metric")
            });
        var routed = await response.Content
            .ReadFromJsonAsync<CopilotRoutedReportResponse>();

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal("unsupported", routed?.Capability.Outcome);
        Assert.Equal("Rejected", routed?.RequestStatus);
        Assert.Empty(ProcessingMessages(factory.Services));
    }

    [Fact]
    public async Task ProcessingOutbox_IsInvisibleUntilCapabilityDecisionCompletes()
    {
        var blocker = new BlockingPreparedCapabilityService();
        await using var baseFactory = new SecuredTestWebApplicationFactory();
        await using var factory = RoutedFactory(baseFactory, services =>
        {
            services.RemoveAll<IPreparedSqlAgentCapabilityService>();
            services.AddSingleton<IPreparedSqlAgentCapabilityService>(blocker);
        });
        using var client = OwnerClient(factory);

        var responseTask = client.PostAsJsonAsync(
            "/api/report-requests/planned-routed",
            InitialRequest(agentic: false));
        await blocker.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Empty(ProcessingMessages(factory.Services));
        Assert.False(responseTask.IsCompleted);

        blocker.Release.TrySetResult();
        var response = await responseTask.WaitAsync(TimeSpan.FromSeconds(10));
        var routed = await response.Content
            .ReadFromJsonAsync<CopilotRoutedReportResponse>();
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Single(ProcessingMessages(factory.Services, routed!.RequestId));
    }

    [Fact]
    public async Task RoutedOwnership_HidesRevisionAndClarificationSources()
    {
        await using var baseFactory = new SecuredTestWebApplicationFactory();
        await using var factory = RoutedFactory(baseFactory);
        var completed = await SeedCompletedAsync(factory.Services);
        var waiting = await SeedWaitingAsync(factory.Services);
        using var other = AuthenticatedClient(
            factory, OtherUser, OtherTenant);

        var revision = await other.PostAsJsonAsync(
            $"/api/report-requests/{completed.RequestId}/planned-revision-routed",
            new CopilotRoutedPlannedRevisionRequest
            {
                RevisionInstruction = "Try another owner's request.",
                Plan = Plan(),
                Intent = Intent(false)
            });
        var clarification = await other.PostAsJsonAsync(
            $"/api/report-requests/{waiting.RequestId}/planned-clarification-routed",
            new CopilotRoutedPlannedClarificationRequest
            {
                Answer = "Try another owner's request.",
                Plan = Plan(),
                Intent = Intent(false)
            });

        Assert.Equal(HttpStatusCode.NotFound, revision.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, clarification.StatusCode);
        Assert.Empty(ProcessingMessages(factory.Services));
    }

    [Fact]
    public async Task RoutedJson_AcceptsAndReturnsExistingSnakeCaseValues()
    {
        await using var baseFactory = new SecuredTestWebApplicationFactory();
        await using var factory = RoutedFactory(baseFactory);
        using var client = OwnerClient(factory);
        var request = InitialRequest(agentic: true);

        var response = await client.PostAsJsonAsync(
            "/api/report-requests/planned-routed", request);
        var json = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Contains("\"agentic_required\"", json,
            StringComparison.Ordinal);
        Assert.Contains("\"period_comparison\"", json,
            StringComparison.Ordinal);
        Assert.DoesNotContain("AgenticRequired", json,
            StringComparison.Ordinal);
        Assert.DoesNotContain("sql", json,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("parameter", json,
            StringComparison.OrdinalIgnoreCase);
    }

    private static WebApplicationFactory<Program> RoutedFactory(
        SecuredTestWebApplicationFactory factory,
        Action<IServiceCollection>? configureServices = null) =>
        factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["SqlProduction:Provider"] = "CrmAnalyticsSql",
                        ["Ollama:Enabled"] = "false",
                        ["OutboxDispatcher:Enabled"] = "false"
                    }));
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ISqlProductionClient>();
                services.AddSingleton<ISqlProductionClient>(
                    new CrmAnalyticsSqlProductionClient(
                        SqlProductionFactory.CreateForOlist(
                            new NullDecisionAuditWriter()),
                        new SqlProductionScopeCompatibilityMapper()));
                configureServices?.Invoke(services);
            });
        });

    private static CopilotRoutedPlannedReportRequest InitialRequest(
        bool agentic,
        string metric = "order_count") => new()
    {
        Prompt = "Show 2018 orders by customer state.",
        ConversationId = "routed-" + Guid.NewGuid().ToString("N"),
        Plan = Plan(metric),
        Intent = Intent(agentic, metric)
    };

    private static CopilotSemanticPlan Plan(
        string metric = "order_count") => new()
    {
        Outcome = "accepted",
        SemanticIntent = new CopilotSemanticIntent
        {
            Metric = metric,
            GroupBy = ["customer_state"],
            Filters = [],
            Date = new CopilotDateIntent
            {
                Kind = "absolute",
                From = "2018-01-01",
                To = "2018-12-31",
                Grain = "none"
            },
            Ranking = new CopilotRankingIntent()
        },
        UnresolvedConcepts = [],
        Clarification = new CopilotClarification()
    };

    private static CopilotSqlAgentIntent Intent(
        bool agentic,
        string metric = "order_count") => new()
    {
        Metrics = [metric],
        Dimensions = ["customer_state"],
        Filters = [],
        Time = new SqlAgentTimeIntent
        {
            Range = new SqlAgentDateRange
            {
                Kind = SqlAgentDateRangeKind.Absolute,
                From = new DateOnly(2018, 1, 1),
                To = new DateOnly(2018, 12, 31)
            }
        },
        Comparisons = agentic
            ? [new SqlAgentPeriodComparison
            {
                Kind = SqlAgentPeriodComparisonKind.PreviousPeriod
            }]
            : []
    };

    private static IReadOnlyList<OutboxStateSnapshot> ProcessingMessages(
        IServiceProvider services,
        string? requestId = null) => services
        .GetRequiredService<InMemoryOutboxStore>()
        .Snapshot
        .Where(item =>
            item.Message.MessageType
                == OutboxMessageType.ReportProcessingRequested
            && (requestId is null
                || item.Message.AggregateId == requestId))
        .ToArray();

    private static async Task<ReportRequest> GetReportAsync(
        IServiceProvider services,
        string requestId)
    {
        using var scope = services.CreateScope();
        return (await scope.ServiceProvider
            .GetRequiredService<IReportRequestRepository>()
            .GetByIdAsync(requestId, CancellationToken.None))!;
    }

    private static async Task<ReportRequest> SeedCompletedAsync(
        IServiceProvider services)
    {
        var now = DateTimeOffset.UtcNow.AddMinutes(-10);
        var report = OwnedReport("completed", now);
        report.TransitionTo(ReportRequestStatus.Validating,
            now.AddMinutes(1));
        report.TransitionTo(ReportRequestStatus.Processing,
            now.AddMinutes(2));
        report.RecordCanonicalRequest(
            CanonicalRequestSerializer.Serialize(Canonical(report)),
            now.AddMinutes(3));
        report.Complete(
            "report-source",
            "Source summary.",
            "https://app.powerbi.com/reports/source",
            now.AddMinutes(4));
        await AddReportAsync(services, report);
        return report;
    }

    private static async Task<ReportRequest> SeedWaitingAsync(
        IServiceProvider services)
    {
        var now = DateTimeOffset.UtcNow.AddMinutes(-5);
        var report = OwnedReport("waiting", now);
        report.TransitionTo(ReportRequestStatus.Validating,
            now.AddMinutes(1));
        report.RequestClarification(
            "Which year should be used?", now.AddMinutes(2));
        await AddReportAsync(services, report);
        return report;
    }

    private static ReportRequest OwnedReport(
        string prefix,
        DateTimeOffset createdAt) => ReportRequest.Create(
        prefix + "-" + Guid.NewGuid().ToString("N"),
        prefix + "-conversation-" + Guid.NewGuid().ToString("N"),
        null,
        "Show orders by state.",
        prefix + "-correlation",
        OwnerUser,
        OwnerTenant,
        createdAt);

    private static async Task AddReportAsync(
        IServiceProvider services,
        ReportRequest report)
    {
        using var scope = services.CreateScope();
        await scope.ServiceProvider
            .GetRequiredService<IReportRequestRepository>()
            .AddAsync(report, CancellationToken.None);
    }

    private static CanonicalRequest Canonical(ReportRequest report) => new()
    {
        RequestId = report.RequestId,
        ConversationId = report.ConversationId,
        Source = DataSource.Dwh,
        Intent = RequestIntent.Breakdown,
        Metrics = ["order_count"],
        Dimensions = ["customer_state"],
        Filters = [],
        DateRange = new DateRangeSpec
        {
            Kind = DateRangeKind.Absolute,
            From = new DateOnly(2018, 1, 1),
            To = new DateOnly(2018, 12, 31)
        },
        Confidence = 1
    };

    private static HttpClient OwnerClient(
        WebApplicationFactory<Program> factory) =>
        AuthenticatedClient(factory, OwnerUser, OwnerTenant);

    private static HttpClient AuthenticatedClient(
        WebApplicationFactory<Program> factory,
        string userId,
        string tenantId)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(
            TestAuthenticationDefaults.AuthenticatedHeader, "true");
        client.DefaultRequestHeaders.Add(
            TestAuthenticationDefaults.UserIdHeader, userId);
        client.DefaultRequestHeaders.Add(
            TestAuthenticationDefaults.TenantIdHeader, tenantId);
        client.DefaultRequestHeaders.Add(
            TestAuthenticationDefaults.ScopesHeader, "access_as_user");
        client.DefaultRequestHeaders.Add(
            TestAuthenticationDefaults.RolesHeader, "Report.User");
        return client;
    }

    private sealed class NullDecisionAuditWriter : IDecisionAuditWriter
    {
        public void Write(DecisionAuditRecord record) { }
    }

    private sealed class BlockingPreparedCapabilityService
        : IPreparedSqlAgentCapabilityService
    {
        public TaskCompletionSource Entered { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<SqlAgentServiceResult<SqlAgentCapabilityResponse>>
            AnalyzePreparedCapabilityAsync(
                string requestId,
                string conversationId,
                string? canonicalRequestJson,
                CopilotSqlAgentIntent intent,
                CancellationToken cancellationToken)
        {
            Entered.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            return new SqlAgentServiceResult<SqlAgentCapabilityResponse>(
                new SqlAgentCapabilityResponse(
                    "succeeded",
                    "deterministic",
                    "moderate",
                    ["time_range"],
                    ["deterministic_capabilities_satisfied"],
                    new string('0', 64)),
                null,
                200);
        }
    }
}
