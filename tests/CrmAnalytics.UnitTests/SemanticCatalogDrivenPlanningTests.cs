using System.Text.Json;
using Crm.Analytics.Sql.Catalog;
using Crm.Analytics.Sql.Contracts;
using Crm.Analytics.Sql.Parsing;
using Crm.Analytics.Sql.QueryBuilder;
using Crm.Analytics.Sql.Service;
using CrmAnalytics.Infrastructure.Integrations;

namespace CrmAnalytics.UnitTests;

public sealed class SemanticCatalogDrivenPlanningTests
{
    private const string RequestId = "semantic-request-1";
    private const string ConversationId = "semantic-conversation-1";

    [Fact]
    public void DefaultCatalog_HasCompleteSemanticMetadataAndConceptAliases()
    {
        var registry = SemanticCatalogRegistry.CreateDefault();

        foreach (var source in registry.Sources.Values)
        {
            SemanticCatalogMetadataValidator.Validate(source.Catalog);
            Assert.All(source.Catalog.Metrics, metric =>
            {
                Assert.False(string.IsNullOrWhiteSpace(metric.Value.Description));
                Assert.False(string.IsNullOrWhiteSpace(metric.Value.QueryMappingReference));
                Assert.All(metric.Value.Aliases, AssertConceptAlias);
            });
            Assert.All(source.Catalog.Dimensions, dimension =>
            {
                Assert.False(string.IsNullOrWhiteSpace(dimension.Value.Description));
                Assert.False(string.IsNullOrWhiteSpace(dimension.Value.QueryMappingReference));
                Assert.All(dimension.Value.Aliases, AssertConceptAlias);
            });
        }
    }

    [Fact]
    public void ModelProjection_ContainsSemanticMetadataButNoPhysicalMapping()
    {
        var prompt = new OllamaCanonicalContract().CreateCatalogPrompt();

        Assert.Contains("conceptualAliases", prompt, StringComparison.Ordinal);
        Assert.Contains("compatibleDimensions", prompt, StringComparison.Ordinal);
        Assert.Contains("supportedOperations", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("queryMappingReference", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("expression", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"column\"", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("vw_", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("mart.", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("dbo.", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Schema_IsCatalogDrivenSourceAwareAndStrict()
    {
        var schema = new OllamaCanonicalContract().CreateSchema(RequestId, ConversationId);

        Assert.False(schema["additionalProperties"]!.GetValue<bool>());
        Assert.NotEmpty(schema["$defs"]!["canonicalRequest"]!["allOf"]!.AsArray());
        var metricKeys = schema["$defs"]!["canonicalRequest"]!["properties"]!
            ["metrics"]!["items"]!["enum"]!
            .AsArray().Select(item => item!.GetValue<string>()).ToArray();
        Assert.Contains("item_sales", metricKeys);
        Assert.DoesNotContain("recency_days", metricKeys);
    }

    [Fact]
    public void Validator_RejectsCrossMappingCompatibilityEvenWhenKeysExist()
    {
        var contract = new OllamaCanonicalContract();
        using var document = JsonDocument.Parse(CanonicalJson(
            "item_sales", "payment_type"));

        Assert.False(contract.Validate(document.RootElement, RequestId, ConversationId));
    }

    [Fact]
    public void SyntheticMetricFixture_AppearsEverywhereWithoutPlannerCodeChange()
    {
        var defaults = SemanticCatalogRegistry.CreateDefault();
        var original = defaults.GetRequired(DataSource.Dwh);
        var metrics = original.Catalog.Metrics.ToDictionary(item => item.Key, item => item.Value);
        metrics.Add("synthetic_margin", new MetricDefinition
        {
            Label = "Sentetik Marj",
            Description = "Yalniz test fixture'inda bulunan sentetik parasal olcum.",
            Kind = "aggregate",
            Expression = "SUM(price)",
            Source = "vw_sales",
            Unit = "BRL",
            ValueType = "currency",
            Aliases = ["sentetik marj", "synthetic margin"],
            CompatibleDimensions = ["product_category"],
            CompatibleFilters = ["product_category"],
            RequiresDateRange = true,
            QueryMappingReference = "metric.synthetic_margin.test.v1",
            ApprovalStatus = "documented"
        });
        var dimensions = original.Catalog.Dimensions.ToDictionary(
            item => item.Key,
            item => item.Key == "product_category"
                ? CloneWithMetric(item.Value, "synthetic_margin")
                : item.Value);
        var fixtureCatalog = new MetricCatalogDocument
        {
            Metrics = metrics,
            Dimensions = dimensions,
            UseCases = original.Catalog.UseCases
        };
        var fixtureRegistry = new SemanticCatalogRegistry(
            new Dictionary<DataSource, SemanticCatalogSource>
            {
                [DataSource.Dwh] = new(fixtureCatalog, original.AllowList),
                [DataSource.Oltp] = defaults.GetRequired(DataSource.Oltp)
            });
        new CatalogValidator(new TSqlParserFactory()).Validate(
            fixtureCatalog, original.AllowList);
        var contract = new OllamaCanonicalContract(fixtureRegistry);
        var schema = contract.CreateSchema(RequestId, ConversationId);
        var schemaText = schema.ToJsonString();
        var prompt = contract.CreateCatalogPrompt();

        Assert.Contains("synthetic_margin", schemaText, StringComparison.Ordinal);
        Assert.Contains("synthetic_margin", prompt, StringComparison.Ordinal);
        using var canonical = JsonDocument.Parse(CanonicalJson(
            "synthetic_margin", "product_category"));
        Assert.True(contract.Validate(canonical.RootElement, RequestId, ConversationId));

        var request = CanonicalRequestSerializer.Deserialize(canonical.RootElement
            .GetProperty("canonicalRequest").GetRawText());
        var build = new DeterministicQueryBuilder(
            new TSqlParserFactory(), fixtureCatalog, original.AllowList).Build(request);
        Assert.True(build.IsSuccessful);

        using var unknown = JsonDocument.Parse(CanonicalJson(
            "synthetic_margin", "product_category"));
        Assert.False(new OllamaCanonicalContract().Validate(
            unknown.RootElement, RequestId, ConversationId));
    }

    [Fact]
    public void ClarificationQuestion_IsDerivedFromMissingSemanticField()
    {
        var service = SqlProductionFactory.CreateForOlist(new NullAuditWriter());
        var productionRequest = new SqlProductionRequest
        {
            RequestId = RequestId,
            ConversationId = ConversationId,
            Prompt = string.Empty,
            Today = new DateOnly(2026, 8, 6),
            Source = DataSource.Dwh,
            Scope = UserDataScope.Unrestricted
        };

        var missingMetric = service.ProduceCanonical(productionRequest,
            Canonical(metrics: [], dimensions: ["product_category"],
                RequestIntent.Breakdown, DateRangeSpec.NotApplicable));
        var missingDate = service.ProduceCanonical(productionRequest,
            Canonical(metrics: ["order_count"], dimensions: [],
                RequestIntent.SingleValue, DateRangeSpec.NotApplicable));
        var missingDimension = service.ProduceCanonical(productionRequest,
            Canonical(metrics: ["order_count"], dimensions: [],
                RequestIntent.Breakdown, AbsoluteRange()));

        Assert.Equal(GuardrailDecision.NeedsClarification, missingMetric.Decision);
        Assert.Contains("metri", missingMetric.UserMessage!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("tarih", missingDate.UserMessage!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("dimension", missingDimension.UserMessage!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void HighConfidence_CannotHideUnsupportedMetricSubstitution()
    {
        var service = SqlProductionFactory.CreateForOlist(new NullAuditWriter());
        var canonical = Canonical(
            metrics: ["item_sales"],
            dimensions: [],
            RequestIntent.SingleValue,
            AbsoluteRange()) with
        {
            Confidence = 0.99,
            UnresolvedTerms = ["unsupported_business_metric"]
        };

        var result = service.ProduceCanonical(new SqlProductionRequest
        {
            RequestId = RequestId,
            ConversationId = ConversationId,
            Prompt = string.Empty,
            Today = new DateOnly(2026, 8, 6),
            Source = DataSource.Dwh,
            Scope = UserDataScope.Unrestricted
        }, canonical);

        Assert.Equal(GuardrailDecision.NeedsClarification, result.Decision);
        Assert.Null(result.Sql);
    }

    [Fact]
    public void UnknownMetric_IsRejectedWithoutSupportedMetricSubstitution()
    {
        var service = SqlProductionFactory.CreateForOlist(new NullAuditWriter());
        var canonical = Canonical(
            metrics: ["profitability_not_in_catalog"],
            dimensions: [],
            RequestIntent.SingleValue,
            AbsoluteRange()) with { Confidence = 1 };

        var result = service.ProduceCanonical(new SqlProductionRequest
        {
            RequestId = RequestId,
            ConversationId = ConversationId,
            Prompt = string.Empty,
            Today = new DateOnly(2026, 8, 6),
            Source = DataSource.Dwh,
            Scope = UserDataScope.Unrestricted
        }, canonical);

        Assert.Equal(GuardrailDecision.NeedsClarification, result.Decision);
        Assert.Null(result.Sql);
        Assert.Equal(["profitability_not_in_catalog"],
            result.CanonicalRequest!.Metrics);
        Assert.DoesNotContain("item_sales", result.CanonicalRequest.Metrics);
    }

    private static DimensionDefinition CloneWithMetric(
        DimensionDefinition value,
        string metric) => new()
    {
        Label = value.Label,
        Description = value.Description,
        Column = value.Column,
        Source = value.Source,
        ValueType = value.ValueType,
        Aliases = value.Aliases,
        CompatibleMetrics = value.CompatibleMetrics.Concat([metric]).ToArray(),
        QueryMappingReference = value.QueryMappingReference,
        IsScopeDimension = value.IsScopeDimension,
        IsTimeDimension = value.IsTimeDimension,
        Note = value.Note,
        Selectable = value.Selectable,
        Filterable = value.Filterable,
        Groupable = value.Groupable,
        Sortable = value.Sortable,
        AggregateFunctions = value.AggregateFunctions,
        SemanticRole = value.SemanticRole
    };

    private static string CanonicalJson(string metric, string dimension) => $$"""
        {
          "outcome":"accepted",
          "canonicalRequest":{
          "requestId":"{{RequestId}}",
          "conversationId":"{{ConversationId}}",
          "previousRequestId":null,
          "source":"dwh",
          "intent":"breakdown",
          "metrics":["{{metric}}"],
          "dimensions":["{{dimension}}"],
          "filters":[],
          "dateRange":{"kind":"absolute","relativeExpression":null,"from":"2018-01-01","to":"2018-01-31"},
          "grain":"none",
          "limit":null,
          "orderBy":null,
          "orderDirection":"asc",
          "scenarioKey":null,
          "confidence":0.9,
          "unresolvedTerms":[]
          },
          "unresolvedConcepts":[],
          "clarification":null
        }
        """;

    private static CanonicalRequest Canonical(
        IReadOnlyList<string> metrics,
        IReadOnlyList<string> dimensions,
        RequestIntent intent,
        DateRangeSpec dateRange) => new()
    {
        RequestId = RequestId,
        ConversationId = ConversationId,
        Source = DataSource.Dwh,
        Intent = intent,
        Metrics = metrics,
        Dimensions = dimensions,
        DateRange = dateRange,
        Confidence = 1
    };

    private static DateRangeSpec AbsoluteRange() => new()
    {
        Kind = DateRangeKind.Absolute,
        From = new DateOnly(2018, 1, 1),
        To = new DateOnly(2018, 1, 31)
    };

    private static void AssertConceptAlias(string alias)
    {
        Assert.InRange(alias.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length, 1, 6);
        Assert.DoesNotContain('.', alias);
        Assert.DoesNotContain('?', alias);
        Assert.DoesNotContain('!', alias);
    }

    private sealed class NullAuditWriter : Crm.Analytics.Sql.Audit.IDecisionAuditWriter
    {
        public void Write(Crm.Analytics.Sql.Audit.DecisionAuditRecord record)
        {
        }
    }
}
