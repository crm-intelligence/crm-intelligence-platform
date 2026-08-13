using System.Net;
using System.Text;
using System.Text.Json;
using CrmAnalytics.Infrastructure.Integrations;
using Microsoft.Extensions.Options;

namespace CrmAnalytics.UnitTests;

public sealed class OllamaSemanticExtractionClientTests
{
    [Fact]
    public async Task AcceptedSemanticIntent_UsesStrictSchemaWithoutSourceField()
    {
        var handler = HandlerForContent(ValidSemanticJson());
        var result = await CreateClient(handler).PlanSemanticAsync(Request(),
            CancellationToken.None);

        Assert.True(result.SchemaValidationSucceeded);
        Assert.Equal(PlanningOutcome.Accepted, result.Plan!.Outcome);
        Assert.Equal("order_count", result.Plan.Intent!.Metric);

        using var body = JsonDocument.Parse(handler.LastRequestBody!);
        var schema = body.RootElement.GetProperty("format");
        var semanticProperties = schema.GetProperty("properties")
            .GetProperty("semanticIntent").GetProperty("anyOf")[0]
            .GetProperty("properties");
        Assert.False(semanticProperties.TryGetProperty("source", out _));
        Assert.False(semanticProperties.TryGetProperty("sql", out _));
        var dateKind = semanticProperties.GetProperty("date")
            .GetProperty("properties").GetProperty("kind")
            .GetProperty("enum");
        Assert.Equal("relative", Assert.Single(dateKind.EnumerateArray()).GetString());
    }

    [Fact]
    public void ModelCatalogProjection_HasOnlyApprovedSemanticFields()
    {
        var projection = new OllamaCanonicalContract().CreateSemanticCatalogPrompt();
        using var document = JsonDocument.Parse(projection);
        var approved = new HashSet<string>(StringComparer.Ordinal)
        {
            "semanticKey", "label", "description", "conceptualAliases",
            "allowedSemanticOperations"
        };

        Assert.NotEmpty(document.RootElement.EnumerateArray());
        foreach (var entry in document.RootElement.EnumerateArray())
        {
            Assert.Equal(approved,
                entry.EnumerateObject().Select(property => property.Name).ToHashSet(
                    StringComparer.Ordinal));
        }
        Assert.DoesNotContain("vw_", projection, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("mart.", projection, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SUM(", projection, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task InvalidJson_ReturnsSafeSemanticFailure()
    {
        var result = await CreateClient(HandlerForContent("not-json"))
            .PlanSemanticAsync(Request(), CancellationToken.None);

        Assert.Null(result.Plan);
        Assert.Equal("InvalidJson", result.Outcome);
        Assert.Equal("OLLAMA_SEMANTIC_INVALID_JSON", result.ReasonCode);
        Assert.False(result.SchemaValidationSucceeded);
    }

    [Fact]
    public async Task UnknownMetric_IsRejectedByStrictSemanticSchema()
    {
        var result = await CreateClient(HandlerForContent(
                ValidSemanticJson().Replace(
                    "order_count", "hallucinated_metric", StringComparison.Ordinal)))
            .PlanSemanticAsync(Request(), CancellationToken.None);

        Assert.Null(result.Plan);
        Assert.Equal("SchemaRejected", result.Outcome);
    }

    private static OllamaStructuredPlanningClient CreateClient(
        RecordingHandler handler) => new(
            new HttpClient(handler) { BaseAddress = new Uri("http://localhost:11434") },
            Options.Create(new OllamaOptions { Enabled = true }),
            new OllamaCanonicalContract());

    private static OllamaSemanticPlanningRequest Request() => new(
        "request0000000000000000000000001",
        "conversation-1",
        new DateOnly(2026, 8, 8),
        "gecen ay kategori bazinda siparis sayisi");

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

    private static string ValidSemanticJson() => """
        {
          "outcome":"accepted",
          "semanticIntent":{
            "metric":"order_count",
            "groupBy":["product_category"],
            "filters":[],
            "date":{
              "kind":"relative",
              "relativeExpression":"previous_month",
              "count":null,
              "from":null,
              "to":null,
              "grain":"none"
            },
            "ranking":null
          },
          "unresolvedConcepts":[],
          "clarification":null
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
