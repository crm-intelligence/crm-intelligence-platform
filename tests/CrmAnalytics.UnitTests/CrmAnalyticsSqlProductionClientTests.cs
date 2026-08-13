using Crm.Analytics.Sql.Audit;
using Crm.Analytics.Sql.Contracts;
using Crm.Analytics.Sql.Routing;
using Crm.Analytics.Sql.Service;
using CrmAnalytics.Application.SqlProduction;
using CrmAnalytics.Contracts.Integrations;
using CrmAnalytics.Infrastructure.Integrations;
using ExternalDataSource = Crm.Analytics.Sql.Contracts.DataSource;
using ExternalDecision = Crm.Analytics.Sql.Contracts.GuardrailDecision;
using InternalScope = CrmAnalytics.Contracts.Integrations.UserDataScope;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CrmAnalytics.UnitTests;

public sealed class CrmAnalyticsSqlProductionClientTests
{
    private static readonly DateOnly Today = new(2026, 7, 31);

    [Fact]
    public async Task RealLibrary_Accepted_MapsExecutablePlanAndSerializer()
    {
        var client = CreateRealClient();
        var result = await client.ProduceAsync(Request(
            "2018 satış tutarını eyalete göre göster"),
            CancellationToken.None);

        Assert.Equal(SqlProductionClientDecision.Accepted, result.Decision);
        Assert.NotNull(result.ExecutionPlan);
        Assert.False(string.IsNullOrWhiteSpace(result.ExecutionPlan.Sql));
        Assert.False(string.IsNullOrWhiteSpace(
            result.ExecutionPlan.AppliedScopeFilter));
        Assert.Equal(SqlDataSource.Dwh, result.ExecutionPlan.Source);
        Assert.Equal("mart.vw_sales",
            result.ExecutionPlan.VerifiedPhysicalObject);
        Assert.Equal(5000, result.ExecutionPlan.RowLimit);
        var canonical = CanonicalRequestSerializer.Deserialize(
            Assert.IsType<string>(result.CanonicalRequestJson));
        Assert.Equal("request0000000000000000000000001",
            canonical.RequestId);
        Assert.Equal(result.CanonicalRequestJson,
            CanonicalRequestSerializer.Serialize(canonical));
    }

    [Fact]
    public async Task RealLibrary_AutomaticOltpSelection_ProducesOltpPlan()
    {
        var result = await CreateRealClient().ProduceAsync(
            Request("Bugunku siparisleri getir", source: SqlDataSource.Unknown),
            CancellationToken.None);

        Assert.Equal(SqlProductionClientDecision.Accepted, result.Decision);
        var plan = Assert.IsType<SqlExecutionPlan>(result.ExecutionPlan);
        Assert.Equal(SqlDataSource.Oltp, plan.Source);
        Assert.Equal(15, plan.CommandTimeoutSeconds);
        Assert.Equal(1000, plan.RowLimit);
        Assert.Equal("dbo.vw_operational_orders",
            plan.VerifiedPhysicalObject);
        Assert.Contains("dbo.vw_operational_orders", plan.Sql, StringComparison.Ordinal);
        Assert.DoesNotContain("mart.", plan.Sql, StringComparison.OrdinalIgnoreCase);
        var canonical = CanonicalRequestSerializer.Deserialize(result.CanonicalRequestJson!);
        Assert.Equal(ExternalDataSource.Oltp, canonical.Source);
    }

    [Fact]
    public async Task RealLibrary_AutomaticDwhSelection_ProducesDwhPlan()
    {
        var result = await CreateRealClient().ProduceAsync(
            Request("2018 satis tutarini eyalete gore goster", source: SqlDataSource.Unknown),
            CancellationToken.None);

        Assert.Equal(SqlProductionClientDecision.Accepted, result.Decision);
        Assert.Equal(SqlDataSource.Dwh, result.ExecutionPlan!.Source);
        Assert.DoesNotContain("dbo.vw_operational_orders", result.ExecutionPlan.Sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RealLibrary_UnresolvedStoreRestrictedScope_IsGr007Rejected()
    {
        var client = CreateRealClient();
        var result = await client.ProduceAsync(Request(
            "2018 satış tutarını eyalete göre göster",
            new InternalScope
            {
                AllowAllRegions = true,
                AllowAllStores = false,
                AllowedStoreIds = ["store-1"]
            }), CancellationToken.None);

        Assert.Equal(SqlProductionClientDecision.Rejected, result.Decision);
        Assert.Equal("GR007", result.RejectionCode);
        Assert.Null(result.ExecutionPlan);
    }

    [Fact]
    public async Task RealLibrary_NeedsClarification_DoesNotCreatePlan()
    {
        var result = await CreateRealClient().ProduceAsync(
            Request("bana bir şeyler göster"), CancellationToken.None);

        Assert.Equal(SqlProductionClientDecision.NeedsClarification,
            result.Decision);
        Assert.Null(result.ExecutionPlan);
        Assert.False(string.IsNullOrWhiteSpace(result.UserMessage));
    }

    [Fact]
    public async Task RealLibrary_OriginalPromptAndClarificationAnswer_AreReparsed()
    {
        var report = CrmAnalytics.Domain.ReportRequests.ReportRequest.Create(
            "request0000000000000000000000001",
            "conversation-1",
            null,
            "satis tutarini eyalete gore goster",
            "correlation-1",
            "11111111-1111-1111-1111-111111111111",
            "22222222-2222-2222-2222-222222222222");
        report.TransitionTo(
            CrmAnalytics.Domain.ReportRequests.ReportRequestStatus.Validating,
            report.UpdatedAt.AddMinutes(1));
        report.RequestClarification(
            "Hangi donem?",
            report.UpdatedAt.AddMinutes(1));
        report.SubmitClarificationResponse(
            "2018 yili",
            report.UpdatedAt.AddMinutes(1));

        var request = SqlProductionClientRequestFactory.Create(
            report,
            null,
            new InternalScope
            {
                AllowAllRegions = false,
                AllowAllStores = true,
                AllowedRegions = ["SP", "RJ"]
            },
            SqlDataSource.Dwh);
        var result = await CreateRealClient().ProduceAsync(
            request,
            CancellationToken.None);

        Assert.Equal(SqlProductionClientDecision.Accepted, result.Decision);
        Assert.NotNull(result.CanonicalRequestJson);
        Assert.NotNull(result.ExecutionPlan);
        Assert.Equal(SqlDataSource.Dwh, result.ExecutionPlan.Source);
    }

    [Fact]
    public async Task PreviousCanonical_IsDeserializedAndUsedAsRevision()
    {
        var client = CreateRealClient();
        var first = await client.ProduceAsync(Request(
            "2018 satış tutarını eyalete göre göster"),
            CancellationToken.None);
        var second = await client.ProduceAsync(Request(
            "sipariş sayısı", previous: first.CanonicalRequestJson),
            CancellationToken.None);

        Assert.Equal(SqlProductionClientDecision.Accepted, second.Decision);
        var revised = CanonicalRequestSerializer.Deserialize(
            second.CanonicalRequestJson!);
        Assert.Equal(["order_count"], revised.Metrics);
        Assert.Equal(["customer_state"], revised.Dimensions);
        Assert.Equal(new DateOnly(2018, 1, 1), revised.DateRange.From);
    }

    [Theory]
    [InlineData(ReasonCode.GR003)]
    [InlineData(ReasonCode.GR008)]
    [InlineData(ReasonCode.GR012)]
    [InlineData(ReasonCode.GR014)]
    public async Task RejectedGuardrailCode_RemainsRejectedNotTechnicalFailure(
        ReasonCode reasonCode)
    {
        var client = CreateFakeClient(new SqlProductionResponse
        {
            RequestId = "request0000000000000000000000001",
            Decision = ExternalDecision.Rejected,
            ReasonCode = reasonCode,
            UserMessage = ReasonCodeMessages.For(reasonCode)
        });
        var result = await client.ProduceAsync(Request("safe prompt"),
            CancellationToken.None);

        Assert.Equal(SqlProductionClientDecision.Rejected, result.Decision);
        Assert.Equal(reasonCode.ToString(), result.RejectionCode);
        Assert.Null(result.ExecutionPlan);
    }

    [Fact]
    public async Task RequestAndConversationIdentifiers_ArePreserved()
    {
        var service = new CountingService(new SqlProductionResponse
        {
            RequestId = "request0000000000000000000000001",
            Decision = ExternalDecision.NeedsClarification,
            ReasonCode = ReasonCode.CL001
        });
        var client = new CrmAnalyticsSqlProductionClient(service,
            new SqlProductionScopeCompatibilityMapper());

        await client.ProduceAsync(Request("safe prompt"),
            CancellationToken.None);

        Assert.Equal("request0000000000000000000000001",
            service.LastRequest!.RequestId);
        Assert.Equal("conversation-1", service.LastRequest.ConversationId);
    }

    [Fact]
    public async Task UnknownAcceptedSource_FailsClosed()
    {
        var client = CreateFakeClient(new SqlProductionResponse
        {
            RequestId = "request0000000000000000000000001",
            Decision = ExternalDecision.Accepted,
            Sql = "SELECT 1",
            AppliedScopeFilter = "scope-applied",
            CommandTimeoutSeconds = 30,
            RowLimit = 5000,
            PhysicalObject = "mart.vw_sales",
            Source = (ExternalDataSource)999
        });

        var result = await client.ProduceAsync(Request("safe prompt"),
            CancellationToken.None);

        Assert.Equal(SqlProductionClientDecision.Failed, result.Decision);
        Assert.Null(result.ExecutionPlan);
    }

    [Theory]
    [InlineData(ExternalDataSource.Dwh, SqlDataSource.Dwh)]
    [InlineData(ExternalDataSource.Oltp, SqlDataSource.Oltp)]
    public async Task Accepted_MapsSourcesParametersAndResultShape(
        ExternalDataSource externalSource, SqlDataSource expectedSource)
    {
        var client = CreateFakeClient(new SqlProductionResponse
        {
            RequestId = "request0000000000000000000000001",
            Decision = ExternalDecision.Accepted,
            Sql = "SELECT 1",
            AppliedScopeFilter = "scope-applied",
            CommandTimeoutSeconds = 30,
            RowLimit = externalSource == ExternalDataSource.Oltp
                ? 1000
                : 5000,
            PhysicalObject = externalSource == ExternalDataSource.Oltp
                ? "dbo.vw_operational_orders"
                : "mart.vw_sales",
            Source = externalSource,
            Parameters =
            [
                new("@text", FilterValueKind.Text, "İstanbul"),
                new("@integer", FilterValueKind.Integer, "42"),
                new("@decimal", FilterValueKind.Decimal, "1.25"),
                new("@boolean", FilterValueKind.Boolean, "true"),
                new("@date", FilterValueKind.Date, "2026-07-31")
            ],
            ResultShape = new ResultShape(1, 1, false,
                VisualType.BarChart, "safe rationale")
        });
        var result = await client.ProduceAsync(Request("safe prompt"),
            CancellationToken.None);

        var plan = Assert.IsType<SqlExecutionPlan>(result.ExecutionPlan);
        Assert.Equal(expectedSource, plan.Source);
        Assert.Equal(Enum.GetValues<SqlExecutionParameterKind>(),
            plan.Parameters.Select(x => x.Kind));
        Assert.Equal("BarChart", plan.ResultShape!.SuggestedVisual);
        Assert.Empty(plan.ResultShape.Columns);
    }

    [Fact]
    public async Task InvalidAcceptedResponse_FailsClosed()
    {
        var client = CreateFakeClient(new SqlProductionResponse
        {
            RequestId = "request0000000000000000000000001",
            Decision = ExternalDecision.Accepted,
            Sql = "SELECT 1",
            AppliedScopeFilter = " ",
            CommandTimeoutSeconds = 30,
            Source = ExternalDataSource.Dwh
        });
        var result = await client.ProduceAsync(Request("safe prompt"),
            CancellationToken.None);
        Assert.Equal(SqlProductionClientDecision.Failed, result.Decision);
        Assert.Null(result.ExecutionPlan);
    }

    [Fact]
    public async Task SubmittedAcceptedPlan_BypassesOllamaAndUsesCanonicalPipeline()
    {
        var ollama = new RecordingOllamaClient();
        var service = new TrackingSqlProductionService(
            SqlProductionFactory.CreateForOlist(new NullAuditWriter()));
        var client = CreateSubmittedPlanClient(service, ollama);

        var result = await client.ProduceAsync(
            Request(
                "2018 siparis sayisini eyalete gore goster",
                source: SqlDataSource.Unknown,
                semanticPlan: AcceptedPlan("order_count")),
            CancellationToken.None);

        Assert.Equal(SqlProductionClientDecision.Accepted, result.Decision);
        Assert.NotNull(result.ExecutionPlan);
        Assert.Equal(0, ollama.CallCount);
        Assert.Equal(0, service.NaturalLanguageCallCount);
        Assert.Equal(1, service.CanonicalCallCount);
    }

    [Theory]
    [InlineData("2018 mayıd ayında sipariş sayısını göster.")]
    [InlineData("2018 mayiz ayında sipariş sayısını göster.")]
    public async Task SubmittedTypoMonthPlan_ProducesScalarKpiThroughQueryBuilder(
        string prompt)
    {
        var ollama = new RecordingOllamaClient();
        var service = new TrackingSqlProductionService(
            SqlProductionFactory.CreateForOlist(new NullAuditWriter()));
        var client = CreateSubmittedPlanClient(service, ollama);

        var result = await client.ProduceAsync(
            Request(
                prompt,
                source: SqlDataSource.Unknown,
                semanticPlan: AcceptedPlan(
                    "order_count", [], "2018-05-01", "2018-05-31")),
            CancellationToken.None);

        Assert.Equal(SqlProductionClientDecision.Accepted, result.Decision);
        Assert.NotNull(result.ExecutionPlan);
        Assert.Equal("KpiCard", result.ResultShape!.SuggestedVisual);
        Assert.Equal(0, ollama.CallCount);
        Assert.Equal(0, service.NaturalLanguageCallCount);
        Assert.Equal(1, service.CanonicalCallCount);
    }

    [Fact]
    public async Task SubmittedExplicitYearConflict_RequiresClarificationWithoutSql()
    {
        var ollama = new RecordingOllamaClient();
        var service = new TrackingSqlProductionService(
            SqlProductionFactory.CreateForOlist(new NullAuditWriter()));
        var client = CreateSubmittedPlanClient(service, ollama);

        var result = await client.ProduceAsync(
            Request(
                "2018 Mayıs ayında sipariş sayısını göster.",
                semanticPlan: AcceptedPlan(
                    "order_count", [], "2025-05-01", "2025-05-31")),
            CancellationToken.None);

        Assert.Equal(
            SqlProductionClientDecision.NeedsClarification,
            result.Decision);
        Assert.Contains("tarih", result.UserMessage!,
            StringComparison.OrdinalIgnoreCase);
        Assert.Null(result.ExecutionPlan);
        Assert.Equal(0, ollama.CallCount);
        Assert.Equal(0, service.NaturalLanguageCallCount);
        Assert.Equal(0, service.CanonicalCallCount);
    }

    [Fact]
    public async Task SubmittedUnknownGroupBy_IsRejectedByCatalogWithoutSql()
    {
        var ollama = new RecordingOllamaClient();
        var service = new TrackingSqlProductionService(
            SqlProductionFactory.CreateForOlist(new NullAuditWriter()));
        var client = CreateSubmittedPlanClient(service, ollama);

        var result = await client.ProduceAsync(
            Request(
                "2018 sipariş sayısını göster.",
                semanticPlan: AcceptedPlan(
                    "order_count", ["unknown_physical_field"])),
            CancellationToken.None);

        Assert.Equal(SqlProductionClientDecision.Rejected, result.Decision);
        Assert.Null(result.ExecutionPlan);
        Assert.Equal(0, service.CanonicalCallCount);
    }

    [Fact]
    public async Task SubmittedReversedDate_IsRejectedAsInvalidNotCatalogUnsupported()
    {
        var ollama = new RecordingOllamaClient();
        var service = new TrackingSqlProductionService(
            SqlProductionFactory.CreateForOlist(new NullAuditWriter()));
        var client = CreateSubmittedPlanClient(service, ollama);

        var result = await client.ProduceAsync(
            Request(
                "2018 sipariş sayısını göster.",
                semanticPlan: AcceptedPlan(
                    "order_count", [], "2018-05-31", "2018-05-01")),
            CancellationToken.None);

        Assert.Equal(SqlProductionClientDecision.Rejected, result.Decision);
        Assert.DoesNotContain("semantic catalog", result.UserMessage!,
            StringComparison.OrdinalIgnoreCase);
        Assert.Null(result.ExecutionPlan);
        Assert.Equal(0, service.CanonicalCallCount);
    }

    [Fact]
    public async Task SubmittedInvalidMetric_IsRejectedWithoutSqlOrOllama()
    {
        var ollama = new RecordingOllamaClient();
        var service = new TrackingSqlProductionService(
            SqlProductionFactory.CreateForOlist(new NullAuditWriter()));
        var client = CreateSubmittedPlanClient(service, ollama);

        var result = await client.ProduceAsync(
            Request(
                "2018 siparis sayisini eyalete gore goster",
                semanticPlan: AcceptedPlan("unknown_metric")),
            CancellationToken.None);

        Assert.Equal(SqlProductionClientDecision.Rejected, result.Decision);
        Assert.Null(result.ExecutionPlan);
        Assert.Equal(0, ollama.CallCount);
        Assert.Equal(0, service.NaturalLanguageCallCount);
        Assert.Equal(0, service.CanonicalCallCount);
    }

    [Fact]
    public async Task SubmittedUnsupportedPlan_DoesNotGenerateSql()
    {
        var ollama = new RecordingOllamaClient();
        var service = new TrackingSqlProductionService(
            SqlProductionFactory.CreateForOlist(new NullAuditWriter()));
        var client = CreateSubmittedPlanClient(service, ollama);
        var plan = new SubmittedSemanticPlanningResult(
            "unsupported",
            null,
            [new SubmittedUnresolvedConcept("metric")],
            null);

        var result = await client.ProduceAsync(
            Request("desteklenmeyen rapor", semanticPlan: plan),
            CancellationToken.None);

        Assert.Equal(SqlProductionClientDecision.Rejected, result.Decision);
        Assert.Null(result.ExecutionPlan);
        Assert.Equal(0, ollama.CallCount);
        Assert.Equal(0, service.NaturalLanguageCallCount);
        Assert.Equal(0, service.CanonicalCallCount);
    }

    [Fact]
    public async Task Cancellation_IsCheckedBeforeSynchronousLibraryCall()
    {
        var service = new CountingService(new SqlProductionResponse
        {
            RequestId = "request0000000000000000000000001",
            Decision = ExternalDecision.NeedsClarification
        });
        var client = new CrmAnalyticsSqlProductionClient(service,
            new SqlProductionScopeCompatibilityMapper());
        using var source = new CancellationTokenSource();
        source.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            client.ProduceAsync(Request("safe prompt"), source.Token));
        Assert.Equal(0, service.CallCount);
    }

    [Fact]
    public void Production_RejectsMockAndUnknownVersion()
    {
        var validator = new SqlProductionProviderOptionsValidator(
            new TestEnvironment(Environments.Production));
        Assert.True(validator.Validate(null,
            new SqlProductionProviderOptions
            {
                Provider = "Mock",
                SqlVersionName = "Sql150"
            }).Failed);
        var invalid = validator.Validate(null,
            new SqlProductionProviderOptions
            {
                Provider = "CrmAnalyticsSql",
                SqlVersionName = "Sql999"
            });
        Assert.True(invalid.Failed);
        Assert.Single(invalid.Failures);
    }

    private static CrmAnalyticsSqlProductionClient CreateRealClient() =>
        new(SqlProductionFactory.CreateForOlist(new NullAuditWriter()),
            new SqlProductionScopeCompatibilityMapper());

    private static CrmAnalyticsSqlProductionClient CreateFakeClient(
        SqlProductionResponse response) => new(new CountingService(response),
            new SqlProductionScopeCompatibilityMapper());

    private static CrmAnalyticsSqlProductionClient CreateSubmittedPlanClient(
        ISqlProductionService service,
        IOllamaStructuredPlanningClient ollama) => new(
            service,
            new SqlProductionScopeCompatibilityMapper(),
            ollama,
            Options.Create(new OllamaOptions
            {
                Enabled = true,
                PlanningMode = OllamaPlanningMode.LlmFirst
            }),
            Options.Create(new SqlProductionProviderOptions()),
            NullLogger<CrmAnalyticsSqlProductionClient>.Instance);

    private static SqlProductionClientRequest Request(string prompt,
        InternalScope? scope = null, string? previous = null,
        SqlDataSource source = SqlDataSource.Dwh,
        SubmittedSemanticPlanningResult? semanticPlan = null) => new(
            "request0000000000000000000000001", "conversation-1", prompt,
            Today, scope ?? new InternalScope
            {
                AllowAllRegions = false,
                AllowAllStores = true,
                AllowedRegions = ["SP", "RJ"]
            }, previous, "user-1", source,
            SemanticPlan: semanticPlan);

    private static SubmittedSemanticPlanningResult AcceptedPlan(
        string metric,
        IReadOnlyList<string>? groupBy = null,
        string from = "2018-01-01",
        string to = "2018-12-31") => new(
            "accepted",
            new SubmittedSemanticIntent(
                metric,
                groupBy ?? ["customer_state"],
                [],
                new SubmittedDateIntent(
                    "absolute", null, null, from, to,
                    "none"),
                null),
            [],
            null);

    private sealed class NullAuditWriter : IDecisionAuditWriter
    {
        public void Write(DecisionAuditRecord record) { }
    }

    private sealed class CountingService(SqlProductionResponse response)
        : ISqlProductionService
    {
        public int CallCount { get; private set; }
        public SqlProductionRequest? LastRequest { get; private set; }
        public SqlProductionResponse Produce(SqlProductionRequest request)
        {
            CallCount++;
            LastRequest = request;
            return response;
        }
    }

    private sealed class TrackingSqlProductionService(
        ISqlProductionService inner) : ISqlProductionService
    {
        public int NaturalLanguageCallCount { get; private set; }
        public int CanonicalCallCount { get; private set; }

        public SqlProductionResponse Produce(SqlProductionRequest request)
        {
            NaturalLanguageCallCount++;
            return inner.Produce(request);
        }

        public SqlProductionResponse ProduceCanonical(
            SqlProductionRequest request,
            CanonicalRequest canonical)
        {
            CanonicalCallCount++;
            return inner.ProduceCanonical(request, canonical);
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

    private sealed class TestEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } =
            new NullFileProvider();
    }
}
