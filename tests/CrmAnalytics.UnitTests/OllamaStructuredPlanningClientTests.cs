using System.Net;
using System.Text;
using System.Text.Json;
using Crm.Analytics.Sql.Contracts;
using CrmAnalytics.Infrastructure.Integrations;
using Microsoft.Extensions.Options;

namespace CrmAnalytics.UnitTests;

public sealed class OllamaStructuredPlanningClientTests
{
    private const string RequestId = "request0000000000000000000000001";
    private const string ConversationId = "conversation-1";
    private static readonly DateOnly Today = new(2026, 8, 6);

    [Fact]
    public async Task TurkishSalesRequest_ReturnsStrictCanonical()
    {
        var handler = HandlerForContent(ValidCanonicalJson());
        var result = await CreateClient(handler).PlanAsync(Request(
            "Son 30 gundeki satislari urun kategorisine gore grupla."),
            CancellationToken.None);

        Assert.NotNull(result.CanonicalRequest);
        Assert.Equal(["item_sales"], result.CanonicalRequest.Metrics);
        Assert.Equal(["product_category"], result.CanonicalRequest.Dimensions);
        Assert.True(result.SchemaValidationSucceeded);
    }

    [Fact]
    public async Task Request_UsesNonStreamingNonThinkingZeroTemperatureAndSchemaObject()
    {
        var handler = HandlerForContent(ValidCanonicalJson());
        await CreateClient(handler).PlanAsync(Request("guvenli talep"),
            CancellationToken.None);

        using var body = JsonDocument.Parse(handler.LastRequestBody!);
        Assert.False(body.RootElement.GetProperty("stream").GetBoolean());
        Assert.False(body.RootElement.GetProperty("think").GetBoolean());
        Assert.Equal(0, body.RootElement.GetProperty("options")
            .GetProperty("temperature").GetDouble());
        var format = body.RootElement.GetProperty("format");
        Assert.Equal(JsonValueKind.Object, format.ValueKind);
        Assert.False(format.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal(JsonValueKind.Array, format.GetProperty("oneOf").ValueKind);
        Assert.False(format.GetProperty("$defs").GetProperty("canonicalRequest")
            .GetProperty("additionalProperties").GetBoolean());
        Assert.False(format.GetProperty("$defs").GetProperty("unresolvedConcept")
            .GetProperty("additionalProperties").GetBoolean());
        Assert.False(format.GetProperty("$defs").GetProperty("filter")
            .GetProperty("additionalProperties").GetBoolean());
        Assert.Equal("qwen3:4b", body.RootElement.GetProperty("model").GetString());
    }

    [Fact]
    public async Task SystemMessage_UsesSemanticProjectionWithoutPhysicalSchemaOrExamples()
    {
        var handler = HandlerForContent(ValidCanonicalJson());
        await CreateClient(handler).PlanAsync(Request(
            "USER_SENTENCE_MUST_NOT_BECOME_SYSTEM_TEMPLATE"), CancellationToken.None);

        using var body = JsonDocument.Parse(handler.LastRequestBody!);
        var system = body.RootElement.GetProperty("messages")[0]
            .GetProperty("content").GetString()!;
        Assert.Contains("compatibleDimensions", system, StringComparison.Ordinal);
        Assert.Contains("Satilan urun kalemlerinin", system, StringComparison.Ordinal);
        Assert.DoesNotContain("USER_SENTENCE_MUST_NOT_BECOME_SYSTEM_TEMPLATE", system,
            StringComparison.Ordinal);
        Assert.DoesNotContain("vw_", system, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("mart.", system, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SUM(", system, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ValidMetric_IsAccepted()
    {
        var result = await PlanContent(ValidCanonicalJson());
        Assert.NotNull(result.CanonicalRequest);
        Assert.Equal(PlanningOutcome.Accepted, result.SemanticOutcome);
    }

    [Fact]
    public async Task AcceptedCanonicalWithUnresolvedMetric_IsRejected()
    {
        var json = ValidCanonicalJson().Replace(
            "\"unresolvedConcepts\":[]",
            "\"unresolvedConcepts\":[{\"kind\":\"metric\"}]",
            StringComparison.Ordinal);

        var result = await PlanContent(json);

        Assert.Null(result.Plan);
        Assert.Equal("SchemaRejected", result.Outcome);
        Assert.Contains("OUTCOMEINVARIANT", result.ReasonCode,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnsupportedWithNullCanonical_IsValidPlanningResult()
    {
        var result = await PlanContent(UnsupportedJson());

        Assert.Equal(PlanningOutcome.Unsupported, result.SemanticOutcome);
        Assert.Null(result.CanonicalRequest);
        Assert.Equal(UnresolvedConceptKind.Metric,
            Assert.Single(result.UnresolvedConcepts).Kind);
        Assert.True(result.SchemaValidationSucceeded);
    }

    [Fact]
    public async Task UnsupportedWithCanonicalPresent_IsRejected()
    {
        var json = ValidCanonicalJson()
            .Replace("\"outcome\":\"accepted\"", "\"outcome\":\"unsupported\"",
                StringComparison.Ordinal)
            .Replace("\"unresolvedConcepts\":[]",
                "\"unresolvedConcepts\":[{\"kind\":\"metric\"}]",
                StringComparison.Ordinal);

        var result = await PlanContent(json);

        Assert.Null(result.Plan);
        Assert.Equal("SchemaRejected", result.Outcome);
    }

    [Fact]
    public async Task NeedsClarificationWithNullCanonical_IsValidPlanningResult()
    {
        var result = await PlanContent(NeedsClarificationJson());

        Assert.Equal(PlanningOutcome.NeedsClarification, result.SemanticOutcome);
        Assert.Null(result.CanonicalRequest);
        Assert.Equal(UnresolvedConceptKind.Date, result.Plan!.Clarification!.Kind);
    }

    [Fact]
    public async Task HighConfidenceAndSupportedMetrics_CannotOverrideUnresolvedMetric()
    {
        var json = ValidCanonicalJson()
            .Replace("\"metrics\":[\"item_sales\"]",
                "\"metrics\":[\"item_sales\",\"order_count\"]",
                StringComparison.Ordinal)
            .Replace("\"unresolvedConcepts\":[]",
                "\"unresolvedConcepts\":[{\"kind\":\"metric\"}]",
                StringComparison.Ordinal);

        var result = await PlanContent(json);

        Assert.Null(result.Plan);
        Assert.Equal("SchemaRejected", result.Outcome);
    }

    [Fact]
    public async Task UnknownMetric_IsRejected()
    {
        var json = ValidCanonicalJson().Replace("item_sales", "unknown_metric",
            StringComparison.Ordinal);
        var result = await PlanContent(json);
        Assert.Null(result.CanonicalRequest);
        Assert.Equal("SchemaRejected", result.Outcome);
    }

    [Fact]
    public async Task UnknownDimension_IsRejected()
    {
        var json = ValidCanonicalJson().Replace("product_category", "secret_column",
            StringComparison.Ordinal);
        var result = await PlanContent(json);
        Assert.Null(result.CanonicalRequest);
        Assert.Equal("SchemaRejected", result.Outcome);
    }

    [Fact]
    public async Task CandidateConstrainedRequestRejectsMetricOutsideCandidates()
    {
        var handler = HandlerForContent(ValidCanonicalJson());
        var request = Request("semantic request") with
        {
            CandidateConstraints = new SemanticCandidateConstraints(
                ["payment_total"], ["product_category"])
        };

        var result = await CreateClient(handler).PlanAsync(request,
            CancellationToken.None);

        Assert.Null(result.Plan);
        Assert.Equal("SchemaRejected", result.Outcome);
        using var body = JsonDocument.Parse(handler.LastRequestBody!);
        var system = body.RootElement.GetProperty("messages")[0]
            .GetProperty("content").GetString()!;
        Assert.Contains("payment_total", system, StringComparison.Ordinal);
        Assert.DoesNotContain("item_sales", system, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GapPlannerReturnsOnlyAmbiguousDimensionSelection()
    {
        var handler = HandlerForContent("""
            {"outcome":"resolved","metricKey":null,"dimensionKeys":["product_category"],"clarification":null}
            """);
        var result = await CreateClient(handler).ResolveGapsAsync(
            DimensionGapRequest(), CancellationToken.None);

        Assert.True(result.SchemaValidationSucceeded);
        Assert.Equal(SemanticGapOutcome.Resolved, result.Selection!.Outcome);
        Assert.Null(result.Selection.MetricKey);
        Assert.Equal(["product_category"], result.Selection.DimensionKeys);
        using var body = JsonDocument.Parse(handler.LastRequestBody!);
        var format = body.RootElement.GetProperty("format");
        Assert.False(format.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal(JsonValueKind.Null,
            format.GetProperty("properties").GetProperty("metricKey")
                .GetProperty("const").ValueKind);
    }

    [Fact]
    public async Task GapPlannerRejectsCandidateOutsideConstraintAndResolvedFieldMutation()
    {
        var outside = HandlerForContent("""
            {"outcome":"resolved","metricKey":"payment_total","dimensionKeys":["product_category"],"clarification":null}
            """);
        var outsideResult = await CreateClient(outside).ResolveGapsAsync(
            DimensionGapRequest(), CancellationToken.None);
        Assert.Null(outsideResult.Selection);
        Assert.Equal("SchemaRejected", outsideResult.Outcome);

        var dateMutation = HandlerForContent("""
            {"outcome":"resolved","metricKey":null,"dimensionKeys":["product_category"],"dateRange":null,"clarification":null}
            """);
        var dateResult = await CreateClient(dateMutation).ResolveGapsAsync(
            DimensionGapRequest(), CancellationToken.None);
        Assert.Null(dateResult.Selection);
        Assert.Equal("SchemaRejected", dateResult.Outcome);
    }

    [Fact]
    public async Task MetricFromDifferentSelectedSource_IsRejected()
    {
        var json = ValidCanonicalJson().Replace(
            "\"source\":\"dwh\"", "\"source\":\"oltp\"", StringComparison.Ordinal);
        var result = await PlanContent(json);

        Assert.Null(result.CanonicalRequest);
        Assert.Equal("SchemaRejected", result.Outcome);
    }

    [Theory]
    [InlineData("{\"sql\":\"SELECT * FROM hidden\"}", "UnsafeOutput")]
    [InlineData("not-json", "InvalidJson")]
    [InlineData("", "EmptyResponse")]
    public async Task UnsafeOrMalformedContent_FallsBack(string content,
        string outcome)
    {
        var result = await PlanContent(content);
        Assert.Null(result.CanonicalRequest);
        Assert.Equal(outcome, result.Outcome);
    }

    [Fact]
    public async Task UnknownProperty_IsNotSilentlyIgnored()
    {
        var json = ValidCanonicalJson().Replace(
            "\"confidence\":0.95",
            "\"sql\":null,\"confidence\":0.95",
            StringComparison.Ordinal);
        var result = await PlanContent(json);
        Assert.Null(result.CanonicalRequest);
        Assert.Equal("SchemaRejected", result.Outcome);
    }

    [Fact]
    public async Task Http500_FallsBack()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(
            HttpStatusCode.InternalServerError));
        var result = await CreateClient(handler).PlanAsync(Request("prompt"),
            CancellationToken.None);
        Assert.Equal("HttpFailure", result.Outcome);
    }

    [Fact]
    public async Task Timeout_FallsBack()
    {
        var handler = new RecordingHandler(_ => throw new TaskCanceledException());
        var result = await CreateClient(handler).PlanAsync(Request("prompt"),
            CancellationToken.None);
        Assert.Equal("Timeout", result.Outcome);
    }

    [Fact]
    public async Task ClarificationContext_IsSentAsSeparateMessages()
    {
        var handler = HandlerForContent(ValidCanonicalJson());
        var request = Request("2018 yili") with
        {
            OriginalPrompt = "Satislari goster",
            ClarificationQuestion = "Hangi donem?",
            ClarificationAnswer = "2018 yili"
        };
        await CreateClient(handler).PlanAsync(request, CancellationToken.None);

        using var body = JsonDocument.Parse(handler.LastRequestBody!);
        var messages = body.RootElement.GetProperty("messages").EnumerateArray()
            .ToArray();
        Assert.Equal(4, messages.Length);
        Assert.StartsWith("ORIGINAL_USER_REQUEST", messages[1]
            .GetProperty("content").GetString(), StringComparison.Ordinal);
        Assert.StartsWith("CLARIFICATION_QUESTION_MEANING", messages[2]
            .GetProperty("content").GetString(), StringComparison.Ordinal);
        Assert.StartsWith("SINGLE_CLARIFICATION_ANSWER", messages[3]
            .GetProperty("content").GetString(), StringComparison.Ordinal);
    }

    private static async Task<OllamaStructuredPlanningResult> PlanContent(
        string content) => await CreateClient(HandlerForContent(content))
        .PlanAsync(Request("prompt"), CancellationToken.None);

    private static OllamaStructuredPlanningClient CreateClient(
        RecordingHandler handler) => new(
            new HttpClient(handler) { BaseAddress = new Uri("http://localhost:11434") },
            Options.Create(new OllamaOptions { Enabled = true }),
            new OllamaCanonicalContract());

    private static RecordingHandler HandlerForContent(string content) => new(_ =>
        new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(new
            {
                message = new { content },
                done_reason = "stop",
                prompt_eval_count = 10,
                eval_count = 20
            }), Encoding.UTF8, "application/json")
        });

    private static OllamaStructuredPlanningRequest Request(string prompt) => new(
        RequestId, ConversationId, Today, prompt);

    private static SemanticGapPlanningRequest DimensionGapRequest()
    {
        var date = new DateRangeSpec
        {
            Kind = DateRangeKind.Relative,
            RelativeExpression = "last_30_days",
            From = new DateOnly(2026, 7, 8),
            To = Today
        };
        var state = new SemanticPlanningState(
            "item_sales", [], date, [], DataSource.Dwh,
            [new SemanticSlotGap(UnresolvedConceptKind.Dimension,
                ["product_category", "product_id"])],
            [], [], .82,
            new SemanticSlotIntent(true, true, true, true, false,
                false, false, false),
            new SemanticCompletenessResult(false,
                [SemanticCompletenessSlot.Metric,
                    SemanticCompletenessSlot.Dimension,
                    SemanticCompletenessSlot.Date,
                    SemanticCompletenessSlot.Source],
                [SemanticCompletenessSlot.Metric,
                    SemanticCompletenessSlot.Date,
                    SemanticCompletenessSlot.Source],
                [], [SemanticCompletenessSlot.Dimension], [],
                [SemanticCompletenessReasonCode.AmbiguousRequiredSlot], 0),
            [new SemanticSlotGap(UnresolvedConceptKind.Dimension,
                ["product_category", "product_id"])]);
        return new SemanticGapPlanningRequest(
            RequestId, ConversationId, Today,
            "kategori kiriliminda urun satis tutari", state,
            new SemanticCandidateConstraints(
                ["item_sales"],
                ["product_category", "product_id"],
                date,
                ["item_sales"],
                [],
                [UnresolvedConceptKind.Dimension]));
    }

    private static string ValidCanonicalJson() => """
        {
          "outcome":"accepted",
          "canonicalRequest":{
          "requestId":"request0000000000000000000000001",
          "conversationId":"conversation-1",
          "previousRequestId":null,
          "source":"dwh",
          "intent":"breakdown",
          "metrics":["item_sales"],
          "dimensions":["product_category"],
          "filters":[],
          "dateRange":{"kind":"relative","relativeExpression":"last_30_days","from":"2026-07-08","to":"2026-08-06"},
          "grain":"none",
          "limit":null,
          "orderBy":null,
          "orderDirection":"asc",
          "scenarioKey":null,
          "confidence":0.95,
          "unresolvedTerms":[]
          },
          "unresolvedConcepts":[],
          "clarification":null
        }
        """;

    private static string UnsupportedJson() => """
        {
          "outcome":"unsupported",
          "canonicalRequest":null,
          "unresolvedConcepts":[{"kind":"metric"}],
          "clarification":null
        }
        """;

    private static string NeedsClarificationJson() => """
        {
          "outcome":"needs_clarification",
          "canonicalRequest":null,
          "unresolvedConcepts":[{"kind":"date"}],
          "clarification":{"kind":"date"}
        }
        """;

    private sealed class RecordingHandler(
        Func<HttpRequestMessage, HttpResponseMessage> responder)
        : HttpMessageHandler
    {
        public string? LastRequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequestBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return responder(request);
        }
    }
}
