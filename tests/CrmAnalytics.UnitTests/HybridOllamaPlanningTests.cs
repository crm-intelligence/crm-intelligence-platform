using Crm.Analytics.Sql.Contracts;
using Crm.Analytics.Sql.Service;
using CrmAnalytics.Application.SqlProduction;
using CrmAnalytics.Contracts.Integrations;
using CrmAnalytics.Infrastructure.Integrations;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ExternalDecision = Crm.Analytics.Sql.Contracts.GuardrailDecision;
using InternalScope = CrmAnalytics.Contracts.Integrations.UserDataScope;

namespace CrmAnalytics.UnitTests;

public sealed class HybridOllamaPlanningTests
{
    private const string RawPrompt = "RAW_USER_PROMPT_DO_NOT_LOG";
    private const string RawModelValue = "RAW_MODEL_RESPONSE_DO_NOT_LOG";

    [Fact]
    public async Task DeterministicAccepted_DoesNotCallOllama()
    {
        var ollama = new CountingOllama(Result());
        var client = CreateClient(new PlannerService(Accepted()), ollama, enabled: true);
        var result = await client.ProduceAsync(Request(), CancellationToken.None);
        Assert.Equal(SqlProductionClientDecision.Accepted, result.Decision);
        Assert.Equal(0, ollama.CallCount);
    }

    [Fact]
    public async Task DeterministicAccepted_DoesNotCallEmbeddingResolver()
    {
        var resolver = new CountingResolver(AvailableResolved());
        var client = CreateClient(new PlannerService(Accepted()),
            new CountingOllama(Result()), enabled: true,
            semanticResolver: resolver, embeddingEnabled: true);

        await client.ProduceAsync(Request(), CancellationToken.None);

        Assert.Equal(0, resolver.CallCount);
    }

    [Fact]
    public async Task DeterministicRejected_DoesNotCallOllama()
    {
        var ollama = new CountingOllama(Result());
        var client = CreateClient(new PlannerService(new SqlProductionResponse
        {
            RequestId = "request0000000000000000000000001",
            Decision = ExternalDecision.Rejected,
            ReasonCode = ReasonCode.GR003
        }), ollama, enabled: true);
        var result = await client.ProduceAsync(Request(), CancellationToken.None);
        Assert.Equal(SqlProductionClientDecision.Rejected, result.Decision);
        Assert.Equal(0, ollama.CallCount);
    }

    [Fact]
    public async Task Cl001_CallsOllamaAndValidCanonicalCanBeAccepted()
    {
        var ollama = new CountingOllama(Result(Canonical()));
        var service = new PlannerService(Clarification(), Accepted());
        var result = await CreateClient(service, ollama, enabled: true)
            .ProduceAsync(Request(), CancellationToken.None);
        Assert.Equal(1, ollama.CallCount);
        Assert.Equal(1, service.CanonicalCallCount);
        Assert.Equal(SqlProductionClientDecision.Accepted, result.Decision);
    }

    [Fact]
    public async Task InvalidOllamaResult_PreservesDeterministicClarification()
    {
        var ollama = new CountingOllama(Result());
        var result = await CreateClient(new PlannerService(Clarification()), ollama,
                enabled: true)
            .ProduceAsync(Request(), CancellationToken.None);
        Assert.Equal(1, ollama.CallCount);
        Assert.Equal(SqlProductionClientDecision.NeedsClarification, result.Decision);
    }

    [Theory]
    [InlineData(PlanningOutcome.NeedsClarification)]
    [InlineData(PlanningOutcome.Unsupported)]
    public async Task NonAcceptedPlanningOutcome_DoesNotCallCanonicalQueryPath(
        PlanningOutcome outcome)
    {
        var unresolved = new[] { new UnresolvedConcept(
            outcome == PlanningOutcome.Unsupported
                ? UnresolvedConceptKind.Metric
                : UnresolvedConceptKind.Date) };
        var clarification = outcome == PlanningOutcome.NeedsClarification
            ? new PlanningClarification(UnresolvedConceptKind.Date)
            : null;
        var ollama = new CountingOllama(Result(new PlanningResult(
            outcome, null, unresolved, clarification)));
        var service = new PlannerService(Clarification(), Accepted());

        var result = await CreateClient(service, ollama, enabled: true)
            .ProduceAsync(Request(), CancellationToken.None);

        Assert.Equal(SqlProductionClientDecision.NeedsClarification, result.Decision);
        Assert.Equal(0, service.CanonicalCallCount);
        Assert.Null(result.ExecutionPlan);
    }

    [Fact]
    public async Task AcceptedWithMultipleSupportedMetricsAndUnresolvedMetric_IsRejectedBeforeCanonicalQueryPath()
    {
        var plan = new PlanningResult(
            PlanningOutcome.Accepted,
            Canonical() with
            {
                Metrics = ["item_sales", "order_count"],
                Confidence = 0.99
            },
            [new UnresolvedConcept(UnresolvedConceptKind.Metric)],
            null);
        var service = new PlannerService(Clarification(), Accepted());

        var result = await CreateClient(service,
                new CountingOllama(Result(plan)), enabled: true)
            .ProduceAsync(Request(), CancellationToken.None);

        Assert.Equal(SqlProductionClientDecision.NeedsClarification, result.Decision);
        Assert.Equal(0, service.CanonicalCallCount);
        Assert.Null(result.ExecutionPlan);
    }

    [Fact]
    public async Task UnsupportedWithCanonical_IsRejectedBeforeCanonicalQueryPath()
    {
        var plan = new PlanningResult(
            PlanningOutcome.Unsupported,
            Canonical(),
            [new UnresolvedConcept(UnresolvedConceptKind.Metric)],
            null);
        var service = new PlannerService(Clarification(), Accepted());

        var result = await CreateClient(service,
                new CountingOllama(Result(plan)), enabled: true)
            .ProduceAsync(Request(), CancellationToken.None);

        Assert.Equal(SqlProductionClientDecision.NeedsClarification, result.Decision);
        Assert.Equal(0, service.CanonicalCallCount);
        Assert.Null(result.ExecutionPlan);
    }

    [Fact]
    public async Task DisabledOllama_PreservesExistingBehaviorWithoutCall()
    {
        var ollama = new CountingOllama(Result(Canonical()));
        var result = await CreateClient(new PlannerService(Clarification()), ollama,
                enabled: false)
            .ProduceAsync(Request(), CancellationToken.None);
        Assert.Equal(0, ollama.CallCount);
        Assert.Equal(SqlProductionClientDecision.NeedsClarification, result.Decision);
    }

    [Fact]
    public async Task UnsupportedMetricCannotCallQwenOrCanonicalQueryPath()
    {
        var ollama = new CountingOllama(Result(Canonical()));
        var service = new PlannerService(Clarification(), Accepted());
        var resolver = new CountingResolver(new SemanticResolverResult(
            true,
            new SemanticResolutionResult(SemanticResolutionKind.Unsupported,
                SemanticSlotKind.Metric, null, .30, .29, .01, ["item_sales"]),
            null,
            MetricRequested: true,
            DimensionRequested: false,
            3,
            1));

        var result = await CreateClient(service, ollama, enabled: true,
                semanticResolver: resolver, embeddingEnabled: true)
            .ProduceAsync(Request(), CancellationToken.None);

        Assert.Equal(SqlProductionClientDecision.Rejected, result.Decision);
        Assert.Equal(0, ollama.CallCount);
        Assert.Equal(0, service.CanonicalCallCount);
        Assert.Null(result.ExecutionPlan);
    }

    [Fact]
    public async Task ResolvedSlotsAssembleCanonicalAndBypassQwen()
    {
        var ollama = new CountingOllama(Result(Canonical()));
        var service = new PlannerService(Clarification(), Accepted());

        var result = await CreateClient(service, ollama, enabled: true,
                semanticResolver: new CountingResolver(AvailableResolved()),
                embeddingEnabled: true)
            .ProduceAsync(Request(), CancellationToken.None);

        Assert.Equal(SqlProductionClientDecision.Accepted, result.Decision);
        Assert.Equal(0, ollama.CallCount);
        Assert.Equal(1, service.CanonicalCallCount);
        Assert.Equal(["item_sales"], service.LastCanonical!.Metrics);
        Assert.Equal(["product_category"], service.LastCanonical.Dimensions);
        Assert.Equal(Canonical().DateRange, service.LastCanonical.DateRange);
    }

    [Fact]
    public async Task AmbiguousDimensionQwenSeesOnlyDimensionGapAndCannotChangeResolvedFields()
    {
        var ollama = new CountingOllama(Result())
        {
            GapResult = new SemanticGapPlanningResult(
                new SemanticGapSelection(SemanticGapOutcome.Resolved,
                    null, ["product_category"], null),
                "Resolved", "NONE", 12, true)
        };
        var semantic = AvailableResolved() with
        {
            Dimension = new SemanticResolutionResult(
                SemanticResolutionKind.Ambiguous,
                SemanticSlotKind.Dimension,
                null, .82, .80, .02,
                ["product_category", "product_id"])
        };
        var service = new PlannerService(Clarification(), Accepted());

        var result = await CreateClient(service, ollama, enabled: true,
                semanticResolver: new CountingResolver(semantic),
                embeddingEnabled: true)
            .ProduceAsync(Request(), CancellationToken.None);

        Assert.Equal(SqlProductionClientDecision.Accepted, result.Decision);
        var gap = Assert.IsType<SemanticGapPlanningRequest>(ollama.LastGapRequest);
        Assert.Equal("item_sales", gap.State.ResolvedMetric);
        Assert.Equal(Canonical().DateRange, gap.State.ResolvedDate);
        Assert.Equal([UnresolvedConceptKind.Dimension],
            gap.State.AmbiguousSlots.Select(slot => slot.Kind));
        Assert.Equal(["item_sales"], gap.CandidateConstraints.FixedMetricKeys);
        Assert.Equal(Canonical().DateRange,
            service.LastCanonical!.DateRange);
        Assert.Equal(["item_sales"], service.LastCanonical.Metrics);
    }

    [Fact]
    public async Task MissingRequiredDimensionUsesPartialQwenAndKeepsResolvedSlotsImmutable()
    {
        var ollama = new CountingOllama(Result())
        {
            GapResult = new SemanticGapPlanningResult(
                new SemanticGapSelection(SemanticGapOutcome.Resolved,
                    null, ["product_category"], null),
                "Resolved", "NONE", 12, true)
        };
        var semantic = AvailableResolved() with
        {
            Dimension = AvailableResolved().Dimension! with
            {
                Kind = SemanticResolutionKind.Missing,
                CandidateKey = null,
                CandidateKeys = ["product_category", "product_id"]
            }
        };
        var service = new PlannerService(Clarification(), Accepted());

        var result = await CreateClient(service, ollama, enabled: true,
                semanticResolver: new CountingResolver(semantic),
                embeddingEnabled: true)
            .ProduceAsync(Request(), CancellationToken.None);

        Assert.Equal(SqlProductionClientDecision.Accepted, result.Decision);
        Assert.Equal("item_sales", ollama.LastGapRequest!.State.ResolvedMetric);
        Assert.Equal(Canonical().DateRange, ollama.LastGapRequest.State.ResolvedDate);
        Assert.Equal(["item_sales"], service.LastCanonical!.Metrics);
        Assert.Equal(Canonical().DateRange, service.LastCanonical.DateRange);
    }

    [Fact]
    public async Task PartialQwenAttemptToModifyResolvedMetricIsRejected()
    {
        var ollama = new CountingOllama(Result())
        {
            GapResult = new SemanticGapPlanningResult(
                new SemanticGapSelection(SemanticGapOutcome.Resolved,
                    "payment_total", ["product_category"], null),
                "Resolved", "NONE", 12, true)
        };
        var semantic = AvailableResolved() with
        {
            Dimension = AvailableResolved().Dimension! with
            {
                Kind = SemanticResolutionKind.Ambiguous,
                CandidateKey = null,
                CandidateKeys = ["product_category", "product_id"]
            }
        };
        var service = new PlannerService(Clarification(), Accepted());

        var result = await CreateClient(service, ollama, enabled: true,
                semanticResolver: new CountingResolver(semantic),
                embeddingEnabled: true)
            .ProduceAsync(Request(), CancellationToken.None);

        Assert.Equal(SqlProductionClientDecision.NeedsClarification, result.Decision);
        Assert.Equal(0, service.CanonicalCallCount);
    }

    [Fact]
    public async Task GroupingRequestedWithoutDimensionCannotBypassQwen()
    {
        var semantic = AvailableResolved() with
        {
            Dimension = AvailableResolved().Dimension! with
            {
                Kind = SemanticResolutionKind.Missing,
                CandidateKey = null,
                CandidateKeys = []
            },
            SlotIntent = new SemanticSlotIntent(
                true, true, true, true, false, false, false, false)
        };
        var ollama = new CountingOllama(Result(Canonical()));
        var service = new PlannerService(Clarification(), Accepted());

        var result = await CreateClient(service, ollama, enabled: true,
                semanticResolver: new CountingResolver(semantic),
                embeddingEnabled: true)
            .ProduceAsync(Request() with
            {
                Prompt = "urun grubuna gore toplam satis bu yil"
            }, CancellationToken.None);

        Assert.Equal(SqlProductionClientDecision.NeedsClarification, result.Decision);
        Assert.Equal(0, ollama.CallCount);
        Assert.Equal(0, service.CanonicalCallCount);
    }

    [Fact]
    public async Task MissingMetricReturnsClarificationWithoutQwen()
    {
        var ollama = new CountingOllama(Result(Canonical()));
        var missing = AvailableResolved() with
        {
            Metric = AvailableResolved().Metric! with
            {
                Kind = SemanticResolutionKind.Missing,
                CandidateKey = null,
                CandidateKeys = []
            },
            MetricRequested = false
        };

        var result = await CreateClient(new PlannerService(Clarification()),
                ollama, enabled: true,
                semanticResolver: new CountingResolver(missing),
                embeddingEnabled: true)
            .ProduceAsync(Request(), CancellationToken.None);

        Assert.Equal(SqlProductionClientDecision.NeedsClarification, result.Decision);
        Assert.Equal(0, ollama.CallCount);
        Assert.Null(result.ExecutionPlan);
    }

    [Fact]
    public async Task InvalidDeterministicCanonicalFailsClosedWithoutQwen()
    {
        var ollama = new CountingOllama(Result(Canonical()));
        var service = new PlannerService(Clarification(), Clarification());

        var result = await CreateClient(service, ollama, enabled: true,
                semanticResolver: new CountingResolver(AvailableResolved()),
                embeddingEnabled: true)
            .ProduceAsync(Request(), CancellationToken.None);

        Assert.Equal(SqlProductionClientDecision.NeedsClarification, result.Decision);
        Assert.Equal(0, ollama.CallCount);
        Assert.Equal(1, service.CanonicalCallCount);
        Assert.Null(result.ExecutionPlan);
    }

    [Fact]
    public async Task AmbiguousMetricSendsOnlyTopCandidatesToQwen()
    {
        var ollama = new CountingOllama(Result());
        var semantic = AvailableResolved() with
        {
            Metric = new SemanticResolutionResult(SemanticResolutionKind.Ambiguous,
                SemanticSlotKind.Metric, null, .75, .73, .02,
                ["payment_total", "item_sales"])
        };

        await CreateClient(new PlannerService(Clarification()), ollama, enabled: true,
                semanticResolver: new CountingResolver(semantic), embeddingEnabled: true)
            .ProduceAsync(Request(), CancellationToken.None);

        Assert.Equal(["payment_total", "item_sales"],
            ollama.LastGapRequest!.CandidateConstraints.MetricKeys);
    }

    [Fact]
    public async Task ModelMetricOutsideCandidateSetIsRejectedBeforeCanonicalQueryPath()
    {
        var service = new PlannerService(Clarification(), Accepted());
        var ollama = new CountingOllama(Result(Canonical()))
        {
            GapResult = new SemanticGapPlanningResult(
                new SemanticGapSelection(SemanticGapOutcome.Resolved,
                    "item_sales", [], null),
                "Resolved", "NONE", 12, true)
        };
        var result = await CreateClient(service,
                ollama, enabled: true,
                semanticResolver: new CountingResolver(AvailableResolved() with
                {
                    Metric = new SemanticResolutionResult(SemanticResolutionKind.Ambiguous,
                        SemanticSlotKind.Metric, null, .9, .85, .05,
                        ["payment_total"])
                }),
                embeddingEnabled: true)
            .ProduceAsync(Request(), CancellationToken.None);

        Assert.Equal(SqlProductionClientDecision.NeedsClarification, result.Decision);
        Assert.Equal(0, service.CanonicalCallCount);
    }

    [Fact]
    public async Task EmbeddingUnavailableFailsClosedWithoutFullCatalogQwen()
    {
        var ollama = new CountingOllama(Result(Canonical()));
        var service = new PlannerService(Clarification(), Accepted());

        var result = await CreateClient(service, ollama, enabled: true,
                semanticResolver: new CountingResolver(new SemanticResolverResult(
                    false, null, null, false, false, 0, 0, "Timeout")),
                embeddingEnabled: true)
            .ProduceAsync(Request(), CancellationToken.None);

        Assert.Equal(SqlProductionClientDecision.NeedsClarification, result.Decision);
        Assert.Equal(0, ollama.CallCount);
        Assert.Null(ollama.LastRequest);
        Assert.Equal(0, service.CanonicalCallCount);
    }

    [Fact]
    public async Task Telemetry_DoesNotContainRawPromptOrModelResponse()
    {
        var logger = new RecordingLogger<CrmAnalyticsSqlProductionClient>();
        var canonical = Canonical() with
        {
            Filters =
            [
                new RequestFilter
                {
                    Field = "customer_state",
                    Op = FilterOperator.Eq,
                    Values = [new FilterLiteral(FilterValueKind.Text, RawModelValue)]
                }
            ]
        };
        var client = CreateClient(new PlannerService(Clarification(), Accepted()),
            new CountingOllama(Result(canonical)), enabled: true, logger);
        await client.ProduceAsync(Request(), CancellationToken.None);

        var logs = string.Join('\n', logger.Messages);
        Assert.DoesNotContain(RawPrompt, logs, StringComparison.Ordinal);
        Assert.DoesNotContain(RawModelValue, logs, StringComparison.Ordinal);
        Assert.Contains("Ollama", logs, StringComparison.Ordinal);
        Assert.Contains("qwen3:4b", logs, StringComparison.Ordinal);
    }

    private static CrmAnalyticsSqlProductionClient CreateClient(
        ISqlProductionService service,
        IOllamaStructuredPlanningClient ollama,
        bool enabled,
        ILogger<CrmAnalyticsSqlProductionClient>? logger = null,
        ISemanticEmbeddingResolver? semanticResolver = null,
        bool embeddingEnabled = false) => new(
            service,
            new SqlProductionScopeCompatibilityMapper(),
            ollama,
            Options.Create(new OllamaOptions { Enabled = enabled }),
            Options.Create(new SqlProductionProviderOptions
            {
                ConfidenceThreshold = 0.60
            }),
            logger ?? new RecordingLogger<CrmAnalyticsSqlProductionClient>(),
            semanticResolver,
            Options.Create(new SemanticEmbeddingOptions
            {
                Enabled = embeddingEnabled
            }));

    private static SemanticResolverResult AvailableResolved() => new(
        true,
        new SemanticResolutionResult(SemanticResolutionKind.Resolved,
            SemanticSlotKind.Metric, "item_sales", .9, .4, .5, ["item_sales"]),
        new SemanticResolutionResult(SemanticResolutionKind.Resolved,
            SemanticSlotKind.Dimension, "product_category", .9, .4, .5,
            ["product_category"]),
        MetricRequested: true,
        DimensionRequested: true,
        3,
        1,
        ResolvedDate: Canonical().DateRange,
        SlotIntent: new SemanticSlotIntent(
            true, true, true, true, false, false, false, false));

    private static SqlProductionClientRequest Request() => new(
        "request0000000000000000000000001",
        "conversation-1",
        RawPrompt,
        new DateOnly(2026, 8, 6),
        new InternalScope
        {
            AllowAllRegions = true,
            AllowAllStores = true
        },
        null,
        "user-redacted",
        SqlDataSource.Dwh);

    private static CanonicalRequest Canonical() => new()
    {
        RequestId = "request0000000000000000000000001",
        ConversationId = "conversation-1",
        Source = DataSource.Dwh,
        Intent = RequestIntent.Breakdown,
        Metrics = ["item_sales"],
        Dimensions = ["product_category"],
        DateRange = new DateRangeSpec
        {
            Kind = DateRangeKind.Relative,
            RelativeExpression = "last_30_days",
            From = new DateOnly(2026, 7, 8),
            To = new DateOnly(2026, 8, 6)
        },
        Confidence = 0.95
    };

    private static OllamaStructuredPlanningResult Result(
        CanonicalRequest? canonical = null) => Result(canonical is null
            ? null
            : new PlanningResult(
                PlanningOutcome.Accepted, canonical, [], null));

    private static OllamaStructuredPlanningResult Result(
        PlanningResult? plan) => new(
            plan,
            plan is null ? "InvalidJson" : plan.Outcome.ToString(),
            plan is null ? "OLLAMA_INVALID_JSON" : "NONE",
            12,
            plan is not null,
            "stop",
            10,
            20);

    private static SqlProductionResponse Clarification() => new()
    {
        RequestId = "request0000000000000000000000001",
        Decision = ExternalDecision.NeedsClarification,
        ReasonCode = ReasonCode.CL001,
        UserMessage = ReasonCodeMessages.For(ReasonCode.CL001)
    };

    private static SqlProductionResponse Accepted() => new()
    {
        RequestId = "request0000000000000000000000001",
        Decision = ExternalDecision.Accepted,
        Sql = "SELECT 1",
        AppliedScopeFilter = "scope-applied",
        CommandTimeoutSeconds = 30,
        RowLimit = 5000,
        PhysicalObject = "mart.vw_sales",
        Source = DataSource.Dwh,
        CanonicalRequest = Canonical()
    };

    private sealed class CountingOllama(OllamaStructuredPlanningResult result)
        : IOllamaStructuredPlanningClient
    {
        public int CallCount { get; private set; }
        public OllamaStructuredPlanningRequest? LastRequest { get; private set; }
        public SemanticGapPlanningRequest? LastGapRequest { get; private set; }
        public SemanticGapPlanningResult? GapResult { get; init; }

        public Task<OllamaStructuredPlanningResult> PlanAsync(
            OllamaStructuredPlanningRequest request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            LastRequest = request;
            return Task.FromResult(result);
        }

        public Task<SemanticGapPlanningResult> ResolveGapsAsync(
            SemanticGapPlanningRequest request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            LastGapRequest = request;
            return Task.FromResult(GapResult
                ?? SemanticGapPlanningResult.Failure(
                    "InvalidJson", "OLLAMA_GAP_INVALID_JSON"));
        }
    }

    private sealed class CountingResolver(SemanticResolverResult result)
        : ISemanticEmbeddingResolver
    {
        public int CallCount { get; private set; }

        public Task<SemanticResolverResult> ResolveAsync(
            string prompt,
            DataSource? source,
            CanonicalRequest? partiallyResolved,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(result);
        }
    }

    private sealed class PlannerService(
        SqlProductionResponse deterministic,
        SqlProductionResponse? canonical = null) : ISqlProductionService
    {
        public int CanonicalCallCount { get; private set; }
        public CanonicalRequest? LastCanonical { get; private set; }
        public SqlProductionResponse Produce(SqlProductionRequest request) => deterministic;

        public SqlProductionResponse ProduceCanonical(
            SqlProductionRequest request,
            CanonicalRequest planned)
        {
            CanonicalCallCount++;
            LastCanonical = planned;
            return canonical ?? deterministic;
        }
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter) =>
            Messages.Add(formatter(state, exception));
    }
}
