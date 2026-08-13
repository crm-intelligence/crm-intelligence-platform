using Crm.Analytics.Sql.Catalog;
using Crm.Analytics.Sql.Contracts;
using Crm.Analytics.Sql.Service;
using CrmAnalytics.Application.SqlProduction;
using CrmAnalytics.Contracts.Integrations;
using CrmAnalytics.Infrastructure.Integrations;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ExternalDecision = Crm.Analytics.Sql.Contracts.GuardrailDecision;
using InternalScope = CrmAnalytics.Contracts.Integrations.UserDataScope;

namespace CrmAnalytics.UnitTests;

public sealed class LlmFirstStructuredPlanningTests
{
    private const string RequestId = "request0000000000000000000000001";
    private const string ConversationId = "conversation-1";
    private static readonly DateOnly Today = new(2026, 8, 8);
    private static readonly LlmFirstCanonicalRequestAssembler Assembler = new(
        SemanticCatalogRegistry.CreateDefault());

    [Fact]
    public void NormalMetric_ProducesBackendOwnedCanonicalIntent()
    {
        var result = Assemble(Intent("item_sales"));

        Assert.Equal(LlmFirstAssemblyOutcome.Assembled, result.Outcome);
        Assert.Equal(["item_sales"], result.CanonicalRequest!.Metrics);
        Assert.Equal(DataSource.Dwh, result.CanonicalRequest.Source);
        Assert.Equal(RequestIntent.SingleValue, result.CanonicalRequest.Intent);
    }

    [Fact]
    public void MetricAndDimension_ProducesBreakdown()
    {
        var result = Assemble(Intent("order_count") with
        {
            GroupBy = ["product_category"]
        });

        Assert.Equal(LlmFirstAssemblyOutcome.Assembled, result.Outcome);
        Assert.Equal(["product_category"], result.CanonicalRequest!.Dimensions);
        Assert.Equal(RequestIntent.Breakdown, result.CanonicalRequest.Intent);
    }

    [Fact]
    public void FilterLiteral_IsTypedFromCatalogAndKeptOutOfSqlText()
    {
        var result = Assemble(Intent("order_count") with
        {
            Filters =
            [
                new ExtractedSemanticFilter(
                    "customer_state", FilterOperator.Eq, ["SP"])
            ]
        });

        var filter = Assert.Single(result.CanonicalRequest!.Filters);
        Assert.Equal("customer_state", filter.Field);
        Assert.Equal(FilterValueKind.Text, Assert.Single(filter.Values).Kind);
        Assert.Equal("SP", Assert.Single(filter.Values).Raw);
    }

    [Fact]
    public void RelativeDate_IsCalculatedDeterministicallyByBackend()
    {
        var result = Assemble(Intent("item_sales") with
        {
            Date = new ExtractedDateIntent(
                ExtractedDateKind.Relative, "last_n_days", 30,
                null, null, TimeGrain.None)
        });

        Assert.Equal(DateRangeKind.Relative,
            result.CanonicalRequest!.DateRange.Kind);
        Assert.Equal("last_30_days",
            result.CanonicalRequest.DateRange.RelativeExpression);
        Assert.Equal(new DateOnly(2026, 7, 10),
            result.CanonicalRequest.DateRange.From);
        Assert.Equal(Today, result.CanonicalRequest.DateRange.To);
    }

    [Fact]
    public void RelativePromptWithValidAbsolutePlan_IsAcceptedWithAdvisoryDivergence()
    {
        var result = Assemble(Intent("item_sales") with
        {
            Date = new ExtractedDateIntent(
                ExtractedDateKind.Absolute, null, null,
                new DateOnly(2026, 7, 10), Today, TimeGrain.None)
        });

        Assert.Equal(LlmFirstAssemblyOutcome.Assembled, result.Outcome);
        Assert.NotNull(result.CanonicalRequest);
        Assert.False(result.Diagnostics!.DeterministicResolverAgreement);
        Assert.Equal("DATE_KIND", result.Diagnostics.DivergenceCategory);
        Assert.False(result.Diagnostics.ExplicitAnchorConflict);
    }

    [Theory]
    [InlineData("2018 mayıd ayında sipariş sayısını göster.", 5)]
    [InlineData("2018 mayiz ayında sipariş sayısını göster.", 5)]
    [InlineData("2018 ağustso ayında sipariş sayısını göster.", 8)]
    public void TypoMonthPrompt_ValidCopilotPlan_IsPrimaryAndProducesScalarKpi(
        string prompt,
        int month)
    {
        var from = new DateOnly(2018, month, 1);
        var result = Assemble(
            Intent("order_count") with
            {
                Date = new ExtractedDateIntent(
                    ExtractedDateKind.Absolute, null, null,
                    from, from.AddMonths(1).AddDays(-1), TimeGrain.None)
            },
            prompt);

        Assert.Equal(LlmFirstAssemblyOutcome.Assembled, result.Outcome);
        Assert.Equal(RequestIntent.SingleValue, result.CanonicalRequest!.Intent);
        Assert.Empty(result.CanonicalRequest.Dimensions);
        Assert.Equal(from, result.CanonicalRequest.DateRange.From);
        Assert.Equal(from.AddMonths(1).AddDays(-1),
            result.CanonicalRequest.DateRange.To);
        Assert.True(result.Diagnostics!.DeterministicResolverAgreement);
        Assert.False(result.Diagnostics.ExplicitAnchorConflict);
    }

    [Fact]
    public void ExplicitYearConflict_RequiresDateClarificationWithoutCanonicalRequest()
    {
        var result = Assemble(
            Intent("order_count") with
            {
                Date = new ExtractedDateIntent(
                    ExtractedDateKind.Absolute, null, null,
                    new DateOnly(2025, 5, 1), new DateOnly(2025, 5, 31),
                    TimeGrain.None)
            },
            "2018 Mayıs ayında sipariş sayısını göster.");

        Assert.Equal(LlmFirstAssemblyOutcome.NeedsClarification, result.Outcome);
        Assert.Null(result.CanonicalRequest);
        Assert.Equal("EXPLICIT_DATE_ANCHOR_CONFLICT", result.ReasonCode);
        Assert.Equal(UnresolvedConceptKind.Date, result.Clarification!.Kind);
        Assert.True(result.Diagnostics!.ExplicitAnchorConflict);
    }

    [Theory]
    [InlineData("2018'de Mayıs sipariş sayısını göster.")]
    [InlineData("2018 yılında Mayıs sipariş sayısını göster.")]
    public void SuffixedExplicitYearConflict_AlsoRequiresClarification(string prompt)
    {
        var result = Assemble(
            Intent("order_count") with
            {
                Date = new ExtractedDateIntent(
                    ExtractedDateKind.Absolute, null, null,
                    new DateOnly(2025, 5, 1), new DateOnly(2025, 5, 31),
                    TimeGrain.None)
            },
            prompt);

        Assert.Equal(LlmFirstAssemblyOutcome.NeedsClarification, result.Outcome);
        Assert.True(result.Diagnostics!.ExplicitAnchorConflict);
    }

    [Fact]
    public void UnknownGroupBy_IsRejectedBySemanticCatalog()
    {
        var result = Assemble(Intent("order_count") with
        {
            GroupBy = ["unknown_physical_field"]
        });

        Assert.Equal(LlmFirstAssemblyOutcome.Unsupported, result.Outcome);
        Assert.Null(result.CanonicalRequest);
        Assert.Equal("UNKNOWN_GROUP_BY_DIMENSION", result.ReasonCode);
    }

    [Fact]
    public void ReversedAbsoluteDate_IsRejectedByBackendValidation()
    {
        var result = Assemble(
            Intent("order_count") with
            {
                Date = new ExtractedDateIntent(
                    ExtractedDateKind.Absolute, null, null,
                    new DateOnly(2018, 5, 31), new DateOnly(2018, 5, 1),
                    TimeGrain.None)
            },
            "2018 sipariş sayısını göster.");

        Assert.Equal(LlmFirstAssemblyOutcome.Invalid, result.Outcome);
        Assert.Null(result.CanonicalRequest);
        Assert.Equal("INVALID_DATE_INTENT", result.ReasonCode);
    }

    [Fact]
    public void UnknownMetric_FailsClosedBeforeCanonicalRequest()
    {
        var result = Assemble(Intent("hallucinated_metric"));

        Assert.Equal(LlmFirstAssemblyOutcome.Unsupported, result.Outcome);
        Assert.Null(result.CanonicalRequest);
        Assert.Equal("UNKNOWN_METRIC", result.ReasonCode);
    }

    [Fact]
    public void IncompatibleMetricAndDimension_FailsClosed()
    {
        var result = Assemble(Intent("payment_total") with
        {
            GroupBy = ["product_category"]
        });

        Assert.Equal(LlmFirstAssemblyOutcome.Unsupported, result.Outcome);
        Assert.Null(result.CanonicalRequest);
        Assert.Equal("INCOMPATIBLE_SEMANTIC_COMBINATION", result.ReasonCode);
    }

    [Fact]
    public async Task AmbiguousRequest_DoesNotCallDeterministicOrCanonicalQueryPath()
    {
        var plan = new ExtractedSemanticPlanningResult(
            PlanningOutcome.NeedsClarification,
            null,
            [new UnresolvedConcept(UnresolvedConceptKind.Metric)],
            new PlanningClarification(UnresolvedConceptKind.Metric));
        var service = new CountingProductionService();

        var result = await CreateClient(service, SemanticResult(plan))
            .ProduceAsync(ClientRequest(), CancellationToken.None);

        Assert.Equal(SqlProductionClientDecision.NeedsClarification, result.Decision);
        Assert.Equal(0, service.DeterministicCallCount);
        Assert.Equal(0, service.CanonicalCallCount);
        Assert.Null(result.ExecutionPlan);
    }

    [Fact]
    public async Task UnsupportedConcept_DoesNotCallDeterministicOrCanonicalQueryPath()
    {
        var plan = new ExtractedSemanticPlanningResult(
            PlanningOutcome.Unsupported,
            null,
            [new UnresolvedConcept(UnresolvedConceptKind.Metric)],
            null);
        var service = new CountingProductionService();

        var result = await CreateClient(service, SemanticResult(plan))
            .ProduceAsync(ClientRequest(), CancellationToken.None);

        Assert.Equal(SqlProductionClientDecision.Rejected, result.Decision);
        Assert.Equal(0, service.DeterministicCallCount);
        Assert.Equal(0, service.CanonicalCallCount);
        Assert.Null(result.ExecutionPlan);
    }

    [Fact]
    public async Task InvalidJsonResult_DoesNotCallQueryBuilderOrSqlPath()
    {
        var service = new CountingProductionService();
        var failure = new OllamaSemanticPlanningResult(
            null, "InvalidJson", "OLLAMA_SEMANTIC_INVALID_JSON", 5,
            false, "stop", 10, 4);

        var result = await CreateClient(service, failure)
            .ProduceAsync(ClientRequest(), CancellationToken.None);

        Assert.Equal(SqlProductionClientDecision.NeedsClarification, result.Decision);
        Assert.Equal(0, service.DeterministicCallCount);
        Assert.Equal(0, service.CanonicalCallCount);
        Assert.Null(result.ExecutionPlan);
    }

    [Fact]
    public async Task AcceptedIntent_UsesStructuredQwenThenBackendCanonicalPath()
    {
        var service = new CountingProductionService();
        var plan = new ExtractedSemanticPlanningResult(
            PlanningOutcome.Accepted,
            Intent("order_count") with { GroupBy = ["product_category"] },
            [],
            null);

        var result = await CreateClient(service, SemanticResult(plan))
            .ProduceAsync(ClientRequest(), CancellationToken.None);

        Assert.Equal(SqlProductionClientDecision.Accepted, result.Decision);
        Assert.Equal(0, service.DeterministicCallCount);
        Assert.Equal(1, service.CanonicalCallCount);
        Assert.Equal(["order_count"], service.LastCanonical!.Metrics);
        Assert.Equal(["product_category"], service.LastCanonical.Dimensions);
    }

    private static LlmFirstCanonicalAssemblyResult Assemble(
        ExtractedSemanticIntent intent,
        string prompt = "son 30 gunde raporla") => Assembler.Assemble(
        RequestId, ConversationId, prompt, Today,
        null, null, intent);

    private static ExtractedSemanticIntent Intent(string metric) => new(
        metric,
        [],
        [],
        new ExtractedDateIntent(
            ExtractedDateKind.Relative, "last_n_days", 30,
            null, null, TimeGrain.None),
        null);

    private static OllamaSemanticPlanningResult SemanticResult(
        ExtractedSemanticPlanningResult plan) => new(
        plan, plan.Outcome.ToString(), "NONE", 5, true, "stop", 10, 4);

    private static CrmAnalyticsSqlProductionClient CreateClient(
        ISqlProductionService service,
        OllamaSemanticPlanningResult result) => new(
        service,
        new SqlProductionScopeCompatibilityMapper(),
        new SemanticOllama(result),
        Options.Create(new OllamaOptions
        {
            Enabled = true,
            PlanningMode = OllamaPlanningMode.LlmFirst
        }),
        Options.Create(new SqlProductionProviderOptions
        {
            ConfidenceThreshold = .60
        }),
        NullLogger<CrmAnalyticsSqlProductionClient>.Instance,
        new ThrowingEmbeddingResolver(),
        Options.Create(new SemanticEmbeddingOptions { Enabled = true }),
        null,
        Assembler);

    private static SqlProductionClientRequest ClientRequest() => new(
        RequestId,
        ConversationId,
        "son 30 gunde siparis sayisini kategoriye gore goster",
        Today,
        new InternalScope
        {
            AllowAllRegions = true,
            AllowAllStores = true
        },
        null,
        "user-redacted",
        SqlDataSource.Unknown);

    private sealed class SemanticOllama(OllamaSemanticPlanningResult result)
        : IOllamaStructuredPlanningClient
    {
        public Task<OllamaStructuredPlanningResult> PlanAsync(
            OllamaStructuredPlanningRequest request,
            CancellationToken cancellationToken) => throw new InvalidOperationException(
                "Legacy planner must not run in LLM-first mode.");

        public Task<OllamaSemanticPlanningResult> PlanSemanticAsync(
            OllamaSemanticPlanningRequest request,
            CancellationToken cancellationToken) => Task.FromResult(result);
    }

    private sealed class ThrowingEmbeddingResolver : ISemanticEmbeddingResolver
    {
        public Task<SemanticResolverResult> ResolveAsync(
            string prompt,
            DataSource? source,
            CanonicalRequest? partiallyResolved,
            CancellationToken cancellationToken) => throw new InvalidOperationException(
                "Embedding resolver must not run in LLM-first mode.");
    }

    private sealed class CountingProductionService : ISqlProductionService
    {
        public int DeterministicCallCount { get; private set; }
        public int CanonicalCallCount { get; private set; }
        public CanonicalRequest? LastCanonical { get; private set; }

        public SqlProductionResponse Produce(SqlProductionRequest request)
        {
            DeterministicCallCount++;
            throw new InvalidOperationException(
                "Deterministic parser must not run in LLM-first mode.");
        }

        public SqlProductionResponse ProduceCanonical(
            SqlProductionRequest request,
            CanonicalRequest canonical)
        {
            CanonicalCallCount++;
            LastCanonical = canonical;
            return new SqlProductionResponse
            {
                RequestId = request.RequestId,
                Decision = ExternalDecision.Accepted,
                Sql = "SELECT 1",
                AppliedScopeFilter = "scope-applied",
                CommandTimeoutSeconds = 30,
                RowLimit = 5000,
                PhysicalObject = "MART.vw_olist_sales",
                Source = canonical.Source,
                CanonicalRequest = canonical
            };
        }
    }
}
