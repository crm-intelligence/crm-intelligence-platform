using System.Net;
using System.Net.Http.Json;
using Crm.Analytics.Sql.Audit;
using Crm.Analytics.Sql.Service;
using CrmAnalytics.Application.Abstractions.Integrations;
using CrmAnalytics.Application.SqlProduction;
using CrmAnalytics.Contracts.CopilotStudio;
using CrmAnalytics.Contracts.ReportRequests;
using CrmAnalytics.Infrastructure.Integrations;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CrmAnalytics.IntegrationTests;

public sealed class CopilotPlannedReportEndpointTests
    : IClassFixture<IntegrationTestWebApplicationFactory>
{
    private readonly IntegrationTestWebApplicationFactory _baseFactory;

    public CopilotPlannedReportEndpointTests(
        IntegrationTestWebApplicationFactory baseFactory) =>
        _baseFactory = baseFactory;

    [Fact]
    public async Task AcceptedPlan_BypassesQwenAndCannotBypassDataScope()
    {
        var execution = new RecordingExecutionClient();
        var ollama = new RecordingOllamaClient();
        var sqlClient = new RecordingSqlProductionClient(
            CreateSqlProductionClient(ollama));
        await using var factory = CreateFactory(
            execution, ollama, sqlClient);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/report-requests/planned",
            AcceptedRequest());
        var responseBody = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.Accepted,
            $"Unexpected response {response.StatusCode}: {responseBody}");
        var created = await response.Content
            .ReadFromJsonAsync<CreateReportRequestResponse>();
        Assert.NotNull(created);
        Assert.Equal("Received", created.Status);
        var sqlResult = await sqlClient.Result.Task.WaitAsync(
            TimeSpan.FromSeconds(10));
        Assert.True(sqlResult.Decision == SqlProductionClientDecision.Accepted,
            $"SQL production decision was {sqlResult.Decision}; "
            + $"scope regions={string.Join(',', sqlClient.LastRequest!.UserDataScope.AllowedRegions)}, "
            + $"allowAllRegions={sqlClient.LastRequest.UserDataScope.AllowAllRegions}, "
            + $"plan={sqlClient.LastRequest.SemanticPlan?.Outcome}.");
        var completed = await Task.WhenAny(
            execution.Plan.Task,
            Task.Delay(TimeSpan.FromSeconds(10)));
        if (completed != execution.Plan.Task)
        {
            var status = await client.GetStringAsync(
                $"/api/report-requests/{created.RequestId}");
            Assert.Fail($"Execution was not reached. Status: {status}");
        }
        var plan = await execution.Plan.Task;
        Assert.Equal(0, ollama.CallCount);
        Assert.False(string.IsNullOrWhiteSpace(plan.AppliedScopeFilter));
        Assert.Contains(plan.Parameters, parameter =>
            string.Equals(
                parameter.Value?.ToString(),
                "SP",
                StringComparison.Ordinal));
    }

    [Fact]
    public async Task InvalidMetric_IsRejectedWithoutExecutionOrQwen()
    {
        var execution = new RecordingExecutionClient();
        var ollama = new RecordingOllamaClient();
        var sqlClient = new RecordingSqlProductionClient(
            CreateSqlProductionClient(ollama));
        await using var factory = CreateFactory(
            execution, ollama, sqlClient);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/report-requests/planned",
            AcceptedRequest("unknown_metric"));
        var created = await response.Content
            .ReadFromJsonAsync<CreateReportRequestResponse>();

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.NotNull(created);
        var terminal = await WaitForTerminalAsync(
            client, created.RequestId, TimeSpan.FromSeconds(10));
        Assert.Equal("Rejected", terminal.Status);
        Assert.False(execution.Plan.Task.IsCompleted);
        Assert.Equal(0, ollama.CallCount);
        Assert.Equal(
            SqlProductionClientDecision.Rejected,
            (await sqlClient.Result.Task).Decision);
    }

    [Theory]
    [InlineData("2018 mayıd ayında sipariş sayısını göster.")]
    [InlineData("2018 mayiz ayında sipariş sayısını göster.")]
    [InlineData("2018 ağustso ayında sipariş sayısını göster.")]
    public async Task TypoMonthAcceptedPlan_RemainsPrimaryAndExecutesScalarKpi(
        string prompt)
    {
        var execution = new RecordingExecutionClient();
        var ollama = new RecordingOllamaClient();
        var sqlClient = new RecordingSqlProductionClient(
            CreateSqlProductionClient(ollama));
        await using var factory = CreateFactory(
            execution, ollama, sqlClient);
        using var client = factory.CreateClient();
        var month = prompt.Contains("ağust", StringComparison.Ordinal) ? 8 : 5;

        var response = await client.PostAsJsonAsync(
            "/api/report-requests/planned",
            AcceptedRequest(
                prompt: prompt,
                groupBy: [],
                from: $"2018-{month:00}-01",
                to: month == 5 ? "2018-05-31" : "2018-08-31"));
        var created = await response.Content
            .ReadFromJsonAsync<CreateReportRequestResponse>();

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.NotNull(created);
        var sqlResult = await sqlClient.Result.Task.WaitAsync(
            TimeSpan.FromSeconds(10));
        Assert.Equal(SqlProductionClientDecision.Accepted, sqlResult.Decision);
        Assert.Equal("KpiCard", sqlResult.ResultShape!.SuggestedVisual);
        Assert.Equal(0, ollama.CallCount);
        Assert.NotNull(await execution.Plan.Task.WaitAsync(
            TimeSpan.FromSeconds(10)));
    }

    [Theory]
    [InlineData("2018 yili")]
    [InlineData("2018 mayis ayinda siparis sayisini goster")]
    public async Task PlannedClarification_UsesFullPlanAndDoesNotLoop(
        string answer)
    {
        var execution = new RecordingExecutionClient();
        var ollama = new RecordingOllamaClient();
        var sqlClient = new RecordingSqlProductionClient(
            CreateSqlProductionClient(ollama));
        await using var factory = CreateFactory(execution, ollama, sqlClient);
        using var client = factory.CreateClient();

        var initialResponse = await client.PostAsJsonAsync(
            "/api/report-requests/planned",
            NeedsYearRequest());
        var initial = await initialResponse.Content
            .ReadFromJsonAsync<CreateReportRequestResponse>();
        Assert.Equal(HttpStatusCode.Accepted, initialResponse.StatusCode);
        Assert.NotNull(initial);
        var waiting = await WaitForTerminalAsync(
            client, initial.RequestId, TimeSpan.FromSeconds(10));
        Assert.Equal("WaitingForClarification", waiting.Status);

        var context = await client.GetFromJsonAsync<
            ReportPlanningContextResponse>(
            $"/api/report-requests/{initial.RequestId}/planning-context");
        Assert.NotNull(context);
        Assert.Equal("order_count",
            context.CurrentSemanticPlan?.SemanticIntent?.Metric);
        Assert.False(string.IsNullOrWhiteSpace(
            context.ClarificationQuestion));

        var clarification = await client.PostAsJsonAsync(
            $"/api/report-requests/{initial.RequestId}/planned-clarification",
            new CopilotPlannedClarificationRequest
            {
                Answer = answer,
                Plan = FullPlan()
            });
        Assert.Equal(HttpStatusCode.Accepted, clarification.StatusCode);

        var terminal = await WaitForStatusAsync(
            client, initial.RequestId, "Completed",
            TimeSpan.FromSeconds(10));
        Assert.Equal("Completed", terminal.Status);
        Assert.Equal("accepted", sqlClient.LastRequest?.SemanticPlan?.Outcome);
        Assert.Equal("2018-05-01",
            sqlClient.LastRequest?.SemanticPlan?.SemanticIntent?.Date.From);
        Assert.Equal("2018-05-31",
            sqlClient.LastRequest?.SemanticPlan?.SemanticIntent?.Date.To);
        Assert.Equal(0, ollama.CallCount);
    }

    [Theory]
    [InlineData("kategori bazinda siparis sayisini goster", "2018-05-01", "2018-05-31", false, false)]
    [InlineData("2018 agustos olarak degistir", "2018-08-01", "2018-08-31", true, false)]
    [InlineData("sadece Istanbul icin", "2018-05-01", "2018-05-31", true, true)]
    public async Task PlannedRevision_PreservesBaselineAndOverridesOnlyRequestedFields(
        string instruction,
        string from,
        string to,
        bool sourceHasCategory,
        bool addCityFilter)
    {
        var execution = new RecordingExecutionClient();
        var ollama = new RecordingOllamaClient();
        var sqlClient = new RecordingSqlProductionClient(
            CreateSqlProductionClient(ollama));
        await using var factory = CreateFactory(execution, ollama, sqlClient);
        using var client = factory.CreateClient();

        var sourceResponse = await client.PostAsJsonAsync(
            "/api/report-requests/planned",
            AcceptedRequest(
                prompt: "2018 mayis ayinda siparis sayisini goster",
                groupBy: sourceHasCategory ? ["product_category"] : [],
                from: "2018-05-01",
                to: "2018-05-31"));
        var source = await sourceResponse.Content
            .ReadFromJsonAsync<CreateReportRequestResponse>();
        Assert.NotNull(source);
        Assert.Equal("Completed", (await WaitForTerminalAsync(
            client, source.RequestId, TimeSpan.FromSeconds(10))).Status);

        var revisedPlan = FullPlan(
            groupBy: ["product_category"],
            from: from,
            to: to,
            filters: addCityFilter
                ? [new CopilotSemanticFilter
                {
                    Dimension = "customer_city",
                    Operator = "eq",
                    Values = ["Istanbul"]
                }]
                : []);
        var revisionResponse = await client.PostAsJsonAsync(
            $"/api/report-requests/{source.RequestId}/planned-revision",
            new CopilotPlannedRevisionRequest
            {
                RevisionInstruction = instruction,
                Plan = revisedPlan
            });
        var revision = await revisionResponse.Content
            .ReadFromJsonAsync<ReviseReportRequestResponse>();
        Assert.Equal(HttpStatusCode.Accepted, revisionResponse.StatusCode);
        Assert.NotNull(revision);
        Assert.Equal(source.RequestId, revision.PreviousRequestId);
        Assert.Equal("Completed", (await WaitForTerminalAsync(
            client, revision.RequestId, TimeSpan.FromSeconds(10))).Status);

        var intent = sqlClient.LastRequest?.SemanticPlan?.SemanticIntent;
        Assert.NotNull(intent);
        Assert.Equal("order_count", intent.Metric);
        Assert.Equal(from, intent.Date.From);
        Assert.Equal(to, intent.Date.To);
        Assert.Equal(["product_category"], intent.GroupBy);
        Assert.Equal(addCityFilter ? 1 : 0, intent.Filters.Count);
        Assert.Equal(0, ollama.CallCount);
    }

    private WebApplicationFactory<Program> CreateFactory(
        RecordingExecutionClient execution,
        RecordingOllamaClient ollama,
        RecordingSqlProductionClient sqlClient) =>
        _baseFactory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["SqlProduction:Provider"] = "CrmAnalyticsSql",
                        ["Ollama:Enabled"] = "false",
                        ["ReportProcessing:Queue:Enabled"] = "true",
                        ["ReportDataAccess:Assignments:0:AllowAllRegions"] =
                            "false",
                        ["ReportDataAccess:Assignments:0:AllowAllStores"] =
                            "true",
                        ["ReportDataAccess:Assignments:0:AllowedRegions:0"] =
                            "SP"
                    });
            });
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ISqlProductionClient>();
                services.RemoveAll<IQueryExecutionClient>();
                services.RemoveAll<IOllamaStructuredPlanningClient>();
                services.AddSingleton<ISqlProductionClient>(sqlClient);
                services.AddSingleton<IQueryExecutionClient>(execution);
                services.AddSingleton<IOllamaStructuredPlanningClient>(ollama);
            });
        });

    private static CrmAnalyticsSqlProductionClient CreateSqlProductionClient(
        IOllamaStructuredPlanningClient ollama) => new(
            SqlProductionFactory.CreateForOlist(
                new NullDecisionAuditWriter()),
            new SqlProductionScopeCompatibilityMapper(),
            ollama,
            Options.Create(new OllamaOptions
            {
                Enabled = true,
                PlanningMode = OllamaPlanningMode.LlmFirst
            }),
            Options.Create(new SqlProductionProviderOptions()),
            NullLogger<CrmAnalyticsSqlProductionClient>.Instance);

    private static async Task<GetReportRequestResponse> WaitForTerminalAsync(
        HttpClient client,
        string requestId,
        TimeSpan timeout)
    {
        using var timeoutSource = new CancellationTokenSource(timeout);
        while (true)
        {
            var report = await client.GetFromJsonAsync<GetReportRequestResponse>(
                $"/api/report-requests/{requestId}",
                timeoutSource.Token);
            Assert.NotNull(report);
            if (report.Status is "Completed" or "Failed" or "Rejected"
                or "WaitingForClarification")
            {
                return report;
            }
            await Task.Delay(25, timeoutSource.Token);
        }
    }

    private static async Task<GetReportRequestResponse> WaitForStatusAsync(
        HttpClient client,
        string requestId,
        string expectedStatus,
        TimeSpan timeout)
    {
        using var timeoutSource = new CancellationTokenSource(timeout);
        while (true)
        {
            var report = await client.GetFromJsonAsync<GetReportRequestResponse>(
                $"/api/report-requests/{requestId}",
                timeoutSource.Token);
            Assert.NotNull(report);
            if (report.Status == expectedStatus) return report;
            if (report.Status is "Failed" or "Rejected")
            {
                Assert.Fail(
                    $"Expected {expectedStatus}, received {report.Status}: "
                    + $"{report.ErrorCode} {report.RejectionMessage}");
            }
            await Task.Delay(25, timeoutSource.Token);
        }
    }

    private static CopilotPlannedReportRequest AcceptedRequest(
        string metric = "order_count",
        string prompt = "2018 siparis sayisini eyalete gore goster",
        IReadOnlyList<string>? groupBy = null,
        string from = "2018-01-01",
        string to = "2018-12-31") => new()
    {
        Prompt = prompt,
        ConversationId = $"copilot-planned-{Guid.NewGuid():N}",
        PreviousRequestId = null,
        Outcome = "accepted",
        SemanticIntent = new CopilotSemanticIntent
        {
            Metric = metric,
            GroupBy = groupBy ?? ["customer_state"],
            Filters = [],
            Date = new CopilotDateIntent
            {
                Kind = "absolute",
                RelativeExpression = "",
                Count = 0,
                From = from,
                To = to,
                Grain = "none"
            },
            Ranking = new CopilotRankingIntent
            {
                TopN = 0,
                OrderBy = "",
                Direction = ""
            }
        },
        UnresolvedConcepts = [],
        Clarification = new CopilotClarification
        {
            Kind = ""
        }
    };

    private static CopilotPlannedReportRequest NeedsYearRequest() => new()
    {
        Prompt = "mayis ayi siparis sayisini getir",
        ConversationId = $"copilot-clarify-{Guid.NewGuid():N}",
        Outcome = "needs_clarification",
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
        UnresolvedConcepts =
            [new CopilotUnresolvedConcept { Kind = "date" }],
        Clarification = new CopilotClarification { Kind = "date" }
    };

    private static CopilotSemanticPlan FullPlan(
        IReadOnlyList<string>? groupBy = null,
        string from = "2018-05-01",
        string to = "2018-05-31",
        IReadOnlyList<CopilotSemanticFilter>? filters = null) => new()
    {
        Outcome = "accepted",
        SemanticIntent = new CopilotSemanticIntent
        {
            Metric = "order_count",
            GroupBy = groupBy ?? [],
            Filters = filters ?? [],
            Date = new CopilotDateIntent
            {
                Kind = "absolute",
                From = from,
                To = to,
                Grain = "none"
            },
            Ranking = new CopilotRankingIntent()
        },
        UnresolvedConcepts = [],
        Clarification = new CopilotClarification()
    };

    private sealed class RecordingExecutionClient : IQueryExecutionClient
    {
        public TaskCompletionSource<SqlExecutionPlan> Plan { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<QueryExecutionResult> ExecuteAsync(
            SqlExecutionPlan executionPlan,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Plan.TrySetResult(executionPlan);
            return Task.FromResult(new QueryExecutionResult(
                "copilot-result",
                executionPlan.Source,
                [],
                [],
                false,
                0,
                DateTimeOffset.UtcNow,
                TimeSpan.Zero));
        }
    }

    private sealed class RecordingSqlProductionClient(
        ISqlProductionClient inner) : ISqlProductionClient
    {
        public TaskCompletionSource<SqlProductionClientResult> Result { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public SqlProductionClientRequest? LastRequest { get; private set; }

        public async Task<SqlProductionClientResult> ProduceAsync(
            SqlProductionClientRequest request,
            CancellationToken cancellationToken)
        {
            LastRequest = request;
            var result = await inner.ProduceAsync(request, cancellationToken);
            Result.TrySetResult(result);
            return result;
        }
    }

    private sealed class RecordingOllamaClient
        : IOllamaStructuredPlanningClient
    {
        public int CallCount { get; private set; }

        public Task<OllamaStructuredPlanningResult> PlanAsync(
            OllamaStructuredPlanningRequest request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            throw new InvalidOperationException("Ollama must not be called.");
        }

        public Task<OllamaSemanticPlanningResult> PlanSemanticAsync(
            OllamaSemanticPlanningRequest request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            throw new InvalidOperationException("Qwen must not be called.");
        }
    }

    private sealed class NullDecisionAuditWriter : IDecisionAuditWriter
    {
        public void Write(DecisionAuditRecord record) { }
    }
}
