using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CrmAnalytics.Application.Abstractions.Integrations;
using CrmAnalytics.Application.Auditing;
using CrmAnalytics.Application.ReportProcessing;
using CrmAnalytics.Application.SqlProduction;
using CrmAnalytics.Contracts.Integrations;
using CrmAnalytics.Contracts.ReportRequests;
using CrmAnalytics.Domain.ReportRequests;
using CrmAnalytics.Infrastructure.Auditing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CrmAnalytics.IntegrationTests;

public sealed class SqlProductionProviderIntegrationTests
    : IClassFixture<IntegrationTestWebApplicationFactory>
{
    private readonly IntegrationTestWebApplicationFactory _baseFactory;

    public SqlProductionProviderIntegrationTests(
        IntegrationTestWebApplicationFactory baseFactory)
    {
        _baseFactory = baseFactory;
    }

    [Fact]
    public async Task Rejected_IsSafeInGetResponse()
    {
        await using var factory = CreateFactory(
            new StaticSqlProductionClient(
                new SqlProductionClientResult(
                    SqlProductionClientDecision.Rejected,
                    "{\"decision\":\"rejected\"}",
                    "Bu veri kapsamı desteklenmiyor.",
                    "GR007",
                    null,
                    null)),
            new RecordingExecutionClient());
        var client = factory.CreateClient();
        var conversationId = $"sql-rejected-{Guid.NewGuid():N}";
        var created = await CreateAsync(client, conversationId);

        using (var scope = factory.Services.CreateScope())
        {
            var processing = scope.ServiceProvider
                .GetRequiredService<IReportProcessingService>();
            var result = await processing.ProcessAsync(
                new ProcessReportRequestCommand(
                    created.RequestId,
                    TestDataScopeFactory.Create()),
                CancellationToken.None);
            Assert.Equal(ReportRequestStatus.Rejected, result.Status);
        }

        var response = await client.GetAsync(
            $"/api/report-requests/{created.RequestId}");
        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            "Rejected",
            document.RootElement.GetProperty("status").GetString());
        Assert.Equal(
            "Bu veri kapsamı desteklenmiyor.",
            document.RootElement
                .GetProperty("rejectionMessage")
                .GetString());
        Assert.Equal(
            JsonValueKind.Null,
            document.RootElement.GetProperty("errorCode").ValueKind);
        Assert.Equal(
            JsonValueKind.Null,
            document.RootElement.GetProperty("errorMessage").ValueKind);
        Assert.False(
            document.RootElement.TryGetProperty(
                "rejectionCode",
                out _));
        Assert.False(
            document.RootElement.TryGetProperty(
                "canonicalRequestJson",
                out _));
        Assert.False(document.RootElement.TryGetProperty("sql", out _));
        Assert.False(document.RootElement.TryGetProperty("prompt", out _));

        var historyJson = await client.GetStringAsync(
            $"/api/conversations/{conversationId}/report-requests");
        using var historyDocument = JsonDocument.Parse(historyJson);
        var historyItem = historyDocument.RootElement
            .GetProperty("reportRequests")[0];
        Assert.Equal(
            "Rejected",
            historyItem.GetProperty("status").GetString());
        Assert.Equal(
            "Bu veri kapsamı desteklenmiyor.",
            historyItem.GetProperty("rejectionMessage").GetString());
        Assert.False(
            historyItem.TryGetProperty("rejectionCode", out _));
        Assert.False(
            historyItem.TryGetProperty("canonicalRequestJson", out _));
        Assert.False(historyItem.TryGetProperty("sql", out _));
    }

    [Fact]
    public async Task Accepted_FakeExecutionContinuesToCompleted()
    {
        var execution = new RecordingExecutionClient();
        var sqlClient = new StaticSqlProductionClient(
            new SqlProductionClientResult(
                SqlProductionClientDecision.Accepted,
                "{\"decision\":\"accepted\"}",
                null,
                null,
                CreatePlan(),
                null));
        await using var factory = CreateFactory(sqlClient, execution);
        var client = factory.CreateClient();
        var created = await CreateAsync(client);

        using (var scope = factory.Services.CreateScope())
        {
            var processing = scope.ServiceProvider
                .GetRequiredService<IReportProcessingService>();
            var result = await processing.ProcessAsync(
                new ProcessReportRequestCommand(
                    created.RequestId,
                    TestDataScopeFactory.Create()),
                CancellationToken.None);

            Assert.Equal(ReportRequestStatus.Completed, result.Status);
            var audit = scope.ServiceProvider
                .GetRequiredService<InMemoryApplicationAuditWriter>()
                .Snapshot.Single(value => value.EventType
                    == ApplicationAuditEventType.QueryExecutionSucceeded);
            Assert.Equal("Dwh", audit.DataSource);
            Assert.Equal(2, audit.RowCount);
            Assert.False(audit.ResultTruncated);
        }

        var response = await client.GetFromJsonAsync<
            GetReportRequestResponse>(
                $"/api/report-requests/{created.RequestId}");

        Assert.NotNull(response);
        Assert.Equal("Completed", response.Status);
        Assert.Equal(1, execution.CallCount);
        Assert.Equal(
            sqlClient.LastRequest?.Today,
            DateOnly.FromDateTime(
                response.CreatedAt.UtcDateTime));
    }

    [Fact]
    public async Task Accepted_QueryFailureWritesSafeAuditAndDoesNotFallback()
    {
        var sqlClient = new StaticSqlProductionClient(
            new SqlProductionClientResult(
                SqlProductionClientDecision.Accepted,
                "{\"decision\":\"accepted\"}",
                null,
                null,
                CreatePlan(),
                null));
        var execution = new ThrowingExecutionClient();
        await using var factory = CreateFactory(sqlClient, execution);
        var client = factory.CreateClient();
        var created = await CreateAsync(client);

        using var scope = factory.Services.CreateScope();
        var processing = scope.ServiceProvider
            .GetRequiredService<IReportProcessingService>();
        var result = await processing.ProcessAsync(
            new ProcessReportRequestCommand(
                created.RequestId,
                TestDataScopeFactory.Create()),
            CancellationToken.None);

        Assert.Equal(ReportRequestStatus.Failed, result.Status);
        Assert.Equal(QueryExecutionErrorCodes.Timeout, result.ErrorCode);
        Assert.Equal(1, execution.CallCount);
        Assert.Equal([SqlDataSource.Dwh], execution.Sources);
        var audit = scope.ServiceProvider
            .GetRequiredService<InMemoryApplicationAuditWriter>()
            .Snapshot.Single(value => value.EventType
                == ApplicationAuditEventType.QueryExecutionFailed);
        Assert.Equal("Dwh", audit.DataSource);
        Assert.Equal(QueryExecutionErrorCodes.Timeout, audit.ReasonCode);
        Assert.Null(audit.RowCount);
    }

    private WebApplicationFactory<Program> CreateFactory(
        ISqlProductionClient sqlClient,
        IQueryExecutionClient executionClient)
    {
        return _baseFactory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ISqlProductionClient>();
                services.RemoveAll<IQueryExecutionClient>();
                services.AddSingleton(sqlClient);
                services.AddSingleton(executionClient);
            });
        });
    }

    private static async Task<CreateReportRequestResponse> CreateAsync(
        HttpClient client,
        string? conversationId = null)
    {
        var response = await client.PostAsJsonAsync(
            "/api/report-requests",
            new CreateReportRequestRequest
            {
                Prompt = "2018 satış raporu",
                ConversationId = conversationId
                    ?? $"sql-production-{Guid.NewGuid():N}"
            });
        var created = await response.Content
            .ReadFromJsonAsync<CreateReportRequestResponse>();

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        return Assert.IsType<CreateReportRequestResponse>(created);
    }

    private static SqlExecutionPlan CreatePlan() =>
        new(
            SqlDataSource.Dwh,
            "SELECT * FROM vw_sales WHERE customer_state = @region",
            [
                new SqlExecutionParameter(
                    "@region",
                    SqlExecutionParameterKind.Text,
                    "SP",
                    true)
            ],
            "customer_state = @region",
            30,
            null,
            "mart.vw_sales",
            5000);

    private sealed class StaticSqlProductionClient
        : ISqlProductionClient
    {
        private readonly SqlProductionClientResult _result;

        public StaticSqlProductionClient(
            SqlProductionClientResult result)
        {
            _result = result;
        }

        public SqlProductionClientRequest? LastRequest { get; private set; }

        public Task<SqlProductionClientResult> ProduceAsync(
            SqlProductionClientRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastRequest = request;
            return Task.FromResult(_result);
        }
    }

    private sealed class RecordingExecutionClient
        : IQueryExecutionClient
    {
        public int CallCount { get; private set; }

        public Task<QueryExecutionResult> ExecuteAsync(
            SqlExecutionPlan executionPlan,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            return Task.FromResult(
                new QueryExecutionResult(
                    "fake-query-result",
                    executionPlan.Source,
                    Array.Empty<QueryResultColumn>(),
                    [
                        new QueryResultRow(Array.Empty<QueryResultValue>()),
                        new QueryResultRow(Array.Empty<QueryResultValue>())
                    ],
                    false,
                    0,
                    DateTimeOffset.UtcNow,
                    TimeSpan.Zero));
        }
    }

    private sealed class ThrowingExecutionClient : IQueryExecutionClient
    {
        public int CallCount { get; private set; }
        public List<SqlDataSource> Sources { get; } = [];

        public Task<QueryExecutionResult> ExecuteAsync(
            SqlExecutionPlan executionPlan,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            Sources.Add(executionPlan.Source);
            throw new QueryExecutionTimeoutException(
                executionPlan.Source,
                TimeSpan.FromSeconds(1));
        }
    }
}
