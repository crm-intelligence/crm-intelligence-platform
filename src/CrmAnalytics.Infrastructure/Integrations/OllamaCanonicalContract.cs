using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Crm.Analytics.Sql.Catalog;
using Crm.Analytics.Sql.Contracts;

namespace CrmAnalytics.Infrastructure.Integrations;

/// <summary>
/// Builds the strict Ollama contract exclusively from the authoritative semantic registry.
/// The projection deliberately excludes physical objects, columns and SQL mappings.
/// </summary>
public sealed partial class OllamaCanonicalContract
{
    private readonly IReadOnlyDictionary<DataSource, MetricCatalogDocument> catalogs;
    private readonly int maximumLimit;

    public OllamaCanonicalContract() : this(SemanticCatalogRegistry.CreateDefault())
    {
    }

    public OllamaCanonicalContract(SemanticCatalogRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        catalogs = registry.Sources.ToDictionary(
            source => source.Key, source => source.Value.Catalog);
        maximumLimit = registry.Sources.Values.Max(source => source.AllowList.MaxRows);
    }

    public JsonObject CreateSchema(
        string requestId,
        string conversationId,
        SemanticCandidateConstraints? constraints = null)
    {
        var canonical = JsonNode.Parse(ContractResources.ReadCanonicalRequestSchema())!
            .AsObject();
        canonical.Remove("$schema");
        canonical.Remove("$id");
        canonical["title"] = "Accepted Canonical Request";
        var properties = canonical["properties"]!.AsObject();
        properties["requestId"]!["const"] = requestId;
        properties["conversationId"]!["const"] = conversationId;
        properties["source"] = new JsonObject
        {
            ["type"] = "string",
            ["enum"] = EnumArray(constraints?.Source is { } constrainedSource
                ? [SourceName(constrainedSource)]
                : catalogs.Keys.Select(SourceName))
        };
        properties["metrics"]!["items"] = EnumSchema(MetricKeys(constraints));
        properties["dimensions"]!["items"] = EnumSchema(DimensionKeys(constraints: constraints));
        if (constraints is not null)
        {
            properties["metrics"]!["minItems"] =
                constraints.MetricKeys.Count > 0 ? 1 : 0;
            properties["dimensions"]!["minItems"] =
                constraints.DimensionKeys.Count > 0 ? 1 : 0;
            if (constraints.FixedMetricKeys.Count > 0
                && constraints.AmbiguousSlotKinds?.Contains(
                    UnresolvedConceptKind.Metric) != true)
            {
                properties["metrics"]!["const"] =
                    EnumArray(constraints.FixedMetricKeys);
            }
            if (constraints.FixedDimensionKeys.Count > 0
                && constraints.AmbiguousSlotKinds?.Contains(
                    UnresolvedConceptKind.Dimension) != true)
            {
                properties["dimensions"]!["const"] =
                    EnumArray(constraints.FixedDimensionKeys);
            }
        }
        canonical["$defs"]!["filter"]!["properties"]!["field"] =
            EnumSchema(DimensionKeys(filterable: true, constraints: constraints));
        var dateRange = canonical["$defs"]!["dateRange"]!.AsObject();
        dateRange["required"] = new JsonArray(
            "kind", "relativeExpression", "from", "to");
        var effectiveDateConstraint = constraints?.DateRange
            ?? InferNotApplicableDate(constraints);
        if (effectiveDateConstraint is { } constrainedDate)
        {
            var dateProperties = dateRange["properties"]!.AsObject();
            dateProperties["kind"] = ConstValue(DateKindName(constrainedDate.Kind));
            dateProperties["relativeExpression"] = ConstValue(
                constrainedDate.RelativeExpression);
            dateProperties["from"] = ConstValue(
                constrainedDate.From?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            dateProperties["to"] = ConstValue(
                constrainedDate.To?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        }
        properties["limit"]!["maximum"] = maximumLimit;
        properties["orderBy"] = NullableEnumSchema(
            DimensionKeys(sortable: true, constraints: constraints));
        properties["scenarioKey"] = new JsonObject
        {
            ["type"] = "null",
            ["const"] = null
        };
        properties["unresolvedTerms"]!["maxItems"] = 0;
        canonical["required"] = new JsonArray(properties.Select(item =>
            JsonValue.Create(item.Key)).ToArray());

        // Global enums keep the schema compact. These source branches prevent a key from a
        // different catalog being emitted for the selected source.
        canonical["allOf"] = new JsonArray(catalogs.Keys
            .OrderBy(SourceName, StringComparer.Ordinal)
            .Select(source => CreateSourceConstraint(source, constraints))
            .ToArray());

        var definitions = canonical["$defs"]!.DeepClone().AsObject();
        canonical.Remove("$defs");
        definitions["canonicalRequest"] = canonical;
        definitions["unresolvedConcept"] = new JsonObject
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["required"] = new JsonArray("kind"),
            ["properties"] = new JsonObject
            {
                ["kind"] = EnumSchema(UnresolvedConceptKinds())
            }
        };
        definitions["clarification"] = new JsonObject
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["required"] = new JsonArray("kind"),
            ["properties"] = new JsonObject
            {
                ["kind"] = EnumSchema(UnresolvedConceptKinds())
            }
        };

        return new JsonObject
        {
            ["title"] = "Semantic Planning Result",
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["required"] = new JsonArray(
                "outcome", "canonicalRequest", "unresolvedConcepts", "clarification"),
            ["properties"] = new JsonObject
            {
                ["outcome"] = new JsonObject
                {
                    ["type"] = "string",
                    ["enum"] = EnumArray(
                        ["unsupported", "needs_clarification", "accepted"]),
                    ["description"] = "accepted only when every explicitly requested semantic concept is supported; needs_clarification for missing or ambiguous information; unsupported for an explicit business metric or dimension absent from the catalog"
                },
                ["unresolvedConcepts"] = new JsonObject
                {
                    ["type"] = "array",
                    ["maxItems"] = 5,
                    ["uniqueItems"] = true,
                    ["description"] = "Typed semantic slots that were not resolved; never copy raw user text",
                    ["items"] = new JsonObject
                    {
                        ["$ref"] = "#/$defs/unresolvedConcept"
                    }
                },
                ["clarification"] = new JsonObject
                {
                    ["anyOf"] = new JsonArray(
                        new JsonObject { ["$ref"] = "#/$defs/clarification" },
                        new JsonObject { ["type"] = "null" }),
                    ["description"] = "Backend-owned clarification slot; null for accepted and unsupported"
                },
                ["canonicalRequest"] = new JsonObject
                {
                    ["anyOf"] = new JsonArray(
                        new JsonObject { ["$ref"] = "#/$defs/canonicalRequest" },
                        new JsonObject { ["type"] = "null" }),
                    ["description"] = "Validated canonical request for accepted only; null for every non-accepted outcome"
                }
            },
            ["oneOf"] = new JsonArray(
                OutcomeBranch("unsupported", canonicalRequired: false,
                    unresolvedMinimum: 1, unresolvedMaximum: 5,
                    clarificationRequired: false),
                OutcomeBranch("needs_clarification", canonicalRequired: false,
                    unresolvedMinimum: 0, unresolvedMaximum: 5,
                    clarificationRequired: true),
                OutcomeBranch("accepted", canonicalRequired: true,
                    unresolvedMinimum: 0, unresolvedMaximum: 0,
                    clarificationRequired: false)),
            ["$defs"] = definitions
        };
    }

    public string CreateCatalogPrompt(SemanticCandidateConstraints? constraints = null)
    {
        var safeCatalog = catalogs
            .Where(source => constraints?.Source is null
                || source.Key == constraints.Source)
            .SelectMany(source =>
            source.Value.Metrics
                .Where(metric => metric.Value.IsUsable)
                .Where(metric => constraints is null
                    || constraints.MetricKeys.Contains(metric.Key, StringComparer.Ordinal))
                .Select(metric => (object)new
                {
                    semanticType = "metric",
                    key = metric.Key,
                    name = metric.Value.Label,
                    description = metric.Value.Description,
                    conceptualAliases = metric.Value.Aliases,
                    aggregation = metric.Value.Kind,
                    valueType = metric.Value.ValueType,
                    unit = metric.Value.Unit,
                    supportedSource = SourceName(source.Key),
                    compatibleDimensions = metric.Value.CompatibleDimensions
                        .Where(key => constraints is null
                            || constraints.DimensionKeys.Contains(key,
                                StringComparer.Ordinal)),
                    compatibleFilters = metric.Value.CompatibleFilters
                        .Where(key => constraints is null
                            || constraints.DimensionKeys.Contains(key,
                                StringComparer.Ordinal)),
                    requiresDateRange = metric.Value.RequiresDateRange
                })
                .Concat(source.Value.Dimensions
                    .Where(dimension => dimension.Value.Selectable
                        || dimension.Value.Filterable
                        || dimension.Value.Sortable)
                    .Where(dimension => constraints is null
                        || constraints.DimensionKeys.Contains(
                            dimension.Key, StringComparer.Ordinal))
                    .Select(dimension => (object)new
                    {
                        semanticType = "dimension",
                        key = dimension.Key,
                        name = dimension.Value.Label,
                        description = dimension.Value.Description,
                        conceptualAliases = dimension.Value.Aliases,
                        valueType = dimension.Value.ValueType,
                        supportedSource = SourceName(source.Key),
                        supportedOperations = new
                        {
                            select = dimension.Value.Selectable,
                            groupBy = dimension.Value.Groupable,
                            filter = dimension.Value.Filterable,
                            sort = dimension.Value.Sortable
                        },
                        compatibleMetrics = dimension.Value.CompatibleMetrics
                            .Where(key => constraints is null
                                || constraints.MetricKeys.Contains(key,
                                    StringComparer.Ordinal))
                    })))
            .ToArray();
        return JsonSerializer.Serialize(safeCatalog);
    }

    public bool Validate(
        JsonElement root,
        string requestId,
        string conversationId,
        SemanticCandidateConstraints? constraints = null)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !HasOnlyProperties(root,
                "outcome", "canonicalRequest", "unresolvedConcepts", "clarification")
            || !HasAllProperties(root,
                "outcome", "canonicalRequest", "unresolvedConcepts", "clarification")
            || !TryParseOutcome(root.GetProperty("outcome"), out var outcome)
            || !ValidateUnresolvedConcepts(root.GetProperty("unresolvedConcepts"))
            || !ValidateClarification(root.GetProperty("clarification")))
        {
            return false;
        }

        var canonical = root.GetProperty("canonicalRequest");
        var unresolvedCount = root.GetProperty("unresolvedConcepts").GetArrayLength();
        var clarification = root.GetProperty("clarification");
        return outcome switch
        {
            PlanningOutcome.Accepted => canonical.ValueKind == JsonValueKind.Object
                && unresolvedCount == 0
                && clarification.ValueKind == JsonValueKind.Null
                && ValidateCanonical(canonical, requestId, conversationId, constraints),
            PlanningOutcome.NeedsClarification => canonical.ValueKind == JsonValueKind.Null
                && clarification.ValueKind == JsonValueKind.Object,
            PlanningOutcome.Unsupported => canonical.ValueKind == JsonValueKind.Null
                && unresolvedCount > 0
                && clarification.ValueKind == JsonValueKind.Null,
            _ => false
        };
    }

    public PlanningResult Deserialize(JsonElement root)
    {
        if (!TryParseOutcome(root.GetProperty("outcome"), out var outcome))
        {
            throw new JsonException("Unknown semantic planning outcome.");
        }

        var canonicalElement = root.GetProperty("canonicalRequest");
        var canonical = canonicalElement.ValueKind == JsonValueKind.Null
            ? null
            : CanonicalRequestSerializer.Deserialize(canonicalElement.GetRawText());
        var unresolved = root.GetProperty("unresolvedConcepts")
            .EnumerateArray()
            .Select(item => new UnresolvedConcept(ParseConceptKind(
                item.GetProperty("kind").GetString())))
            .ToArray();
        var clarificationElement = root.GetProperty("clarification");
        var clarification = clarificationElement.ValueKind == JsonValueKind.Null
            ? null
            : new PlanningClarification(ParseConceptKind(
                clarificationElement.GetProperty("kind").GetString()));

        return new PlanningResult(outcome, canonical, unresolved, clarification);
    }

    private bool ValidateCanonical(
        JsonElement root,
        string requestId,
        string conversationId,
        SemanticCandidateConstraints? constraints)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !HasOnlyProperties(root,
                "requestId", "conversationId", "previousRequestId", "source", "intent",
                "metrics", "dimensions", "filters", "dateRange", "grain", "limit",
                "orderBy", "orderDirection", "scenarioKey", "confidence", "unresolvedTerms")
            || !HasAllProperties(root,
                "requestId", "conversationId", "previousRequestId", "source", "intent",
                "metrics", "dimensions", "filters", "dateRange", "grain", "limit",
                "orderBy", "orderDirection", "scenarioKey", "confidence", "unresolvedTerms"))
        {
            return false;
        }

        var sourceName = root.GetProperty("source").ValueKind == JsonValueKind.String
            ? root.GetProperty("source").GetString()
            : null;
        var sourceMatch = catalogs.Keys
            .Where(candidate => SourceName(candidate).Equals(sourceName, StringComparison.Ordinal))
            .Cast<DataSource?>()
            .SingleOrDefault();
        if (sourceMatch is null)
        {
            return false;
        }

        var source = sourceMatch.Value;
        var catalog = catalogs[source];
        return IsExactString(root, "requestId", requestId)
            && IsExactString(root, "conversationId", conversationId)
            && IsNullableBoundedString(root.GetProperty("previousRequestId"), 64)
            && IsEnum(root, "source", [SourceName(source)])
            && IsEnum(root, "intent", ["compare", "trend", "breakdown", "single_value", "list"])
            && IsStringArray(root.GetProperty("metrics"), MetricKeys(source, constraints), 8)
            && IsStringArray(root.GetProperty("dimensions"),
                DimensionKeys(source, constraints: constraints), 4)
            && ValidateResolvedConstraints(root, source, constraints)
            && ValidateFilters(root.GetProperty("filters"), source, constraints)
            && ValidateDateRange(root.GetProperty("dateRange"))
            && ValidateDateConstraint(root.GetProperty("dateRange"), constraints)
            && IsEnum(root, "grain", ["none", "day", "week", "month", "quarter", "year"])
            && ValidateNullableInteger(root.GetProperty("limit"), 1, maximumLimit)
            && ValidateNullableEnum(root.GetProperty("orderBy"),
                DimensionKeys(source, sortable: true, constraints: constraints))
            && IsEnum(root, "orderDirection", ["asc", "desc"])
            && root.GetProperty("scenarioKey").ValueKind == JsonValueKind.Null
            && ValidateConfidence(root.GetProperty("confidence"))
            && IsFreeStringArray(root.GetProperty("unresolvedTerms"), 0)
            && ValidateCompatibility(root, catalog);
    }

    private static bool ValidateResolvedConstraints(
        JsonElement root,
        DataSource source,
        SemanticCandidateConstraints? constraints)
    {
        if (constraints is null)
        {
            return true;
        }

        var metrics = root.GetProperty("metrics").EnumerateArray()
            .Select(item => item.GetString()!).ToArray();
        var dimensions = root.GetProperty("dimensions").EnumerateArray()
            .Select(item => item.GetString()!).ToArray();
        return (constraints.Source is null || constraints.Source == source)
            && constraints.FixedMetricKeys.All(metrics.Contains)
            && constraints.FixedDimensionKeys.All(dimensions.Contains)
            && (constraints.FixedMetricKeys.Count == 0
                || constraints.AmbiguousSlotKinds?.Contains(
                    UnresolvedConceptKind.Metric) == true
                || metrics.SequenceEqual(constraints.FixedMetricKeys,
                    StringComparer.Ordinal))
            && (constraints.FixedDimensionKeys.Count == 0
                || constraints.AmbiguousSlotKinds?.Contains(
                    UnresolvedConceptKind.Dimension) == true
                || dimensions.SequenceEqual(constraints.FixedDimensionKeys,
                    StringComparer.Ordinal));
    }

    public string ClassifyValidationFailure(
        JsonElement root,
        string requestId,
        string conversationId,
        SemanticCandidateConstraints? constraints = null)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return "RootShape";
        }

        if (!root.TryGetProperty("outcome", out var outcomeElement)
            || !TryParseOutcome(outcomeElement, out var outcome)
            || !root.TryGetProperty("canonicalRequest", out var canonical)
            || !root.TryGetProperty("unresolvedConcepts", out var unresolved)
            || !root.TryGetProperty("clarification", out var clarification)
            || !ValidateUnresolvedConcepts(unresolved)
            || !ValidateClarification(clarification))
        {
            return "PlanningEnvelope";
        }

        var invariantValid = outcome switch
        {
            PlanningOutcome.Accepted => canonical.ValueKind == JsonValueKind.Object
                && unresolved.GetArrayLength() == 0
                && clarification.ValueKind == JsonValueKind.Null,
            PlanningOutcome.NeedsClarification => canonical.ValueKind == JsonValueKind.Null
                && clarification.ValueKind == JsonValueKind.Object,
            PlanningOutcome.Unsupported => canonical.ValueKind == JsonValueKind.Null
                && unresolved.GetArrayLength() > 0
                && clarification.ValueKind == JsonValueKind.Null,
            _ => false
        };
        if (!invariantValid)
        {
            return "OutcomeInvariant";
        }

        if (outcome != PlanningOutcome.Accepted)
        {
            return "StructuralContract";
        }

        root = canonical;

        if (!root.TryGetProperty("requestId", out var request)
            || request.ValueKind != JsonValueKind.String
            || request.GetString() != requestId
            || !root.TryGetProperty("conversationId", out var conversation)
            || conversation.ValueKind != JsonValueKind.String
            || conversation.GetString() != conversationId)
        {
            return "IdentityContract";
        }

        if (!root.TryGetProperty("source", out var sourceElement)
            || sourceElement.ValueKind != JsonValueKind.String)
        {
            return "SourceContract";
        }

        var sourceName = sourceElement.GetString();
        var sourceMatch = catalogs.Keys
            .Where(candidate => SourceName(candidate) == sourceName)
            .Cast<DataSource?>().SingleOrDefault();
        if (sourceMatch is null)
        {
            return "UnknownSource";
        }

        var source = sourceMatch.Value;
        if (!root.TryGetProperty("metrics", out var metrics)
            || !IsStringArray(metrics, MetricKeys(source, constraints), 8))
        {
            return "MetricKeyContract";
        }

        if (!root.TryGetProperty("dimensions", out var dimensions)
            || !IsStringArray(dimensions,
                DimensionKeys(source, constraints: constraints), 4))
        {
            return "DimensionKeyContract";
        }

        if (!root.TryGetProperty("filters", out var filters)
            || !ValidateFilters(filters, source, constraints))
        {
            return "FilterContract";
        }

        if (!root.TryGetProperty("dateRange", out var dateRange)
            || !ValidateDateRange(dateRange))
        {
            return "DateContract";
        }

        if (!ValidateCompatibility(root, catalogs[source]))
        {
            return "Compatibility";
        }

        return "StructuralContract";
    }

    private bool ValidateFilters(
        JsonElement filters,
        DataSource source,
        SemanticCandidateConstraints? constraints)
    {
        if (filters.ValueKind != JsonValueKind.Array || filters.GetArrayLength() > 12)
        {
            return false;
        }

        foreach (var filter in filters.EnumerateArray())
        {
            if (filter.ValueKind != JsonValueKind.Object
                || !HasOnlyProperties(filter, "field", "op", "values")
                || !HasAllProperties(filter, "field", "op", "values")
                || !IsEnum(filter, "field",
                    DimensionKeys(source, filterable: true, constraints: constraints))
                || !IsEnum(filter, "op", ["eq", "not_eq", "in", "not_in", "gt", "gte", "lt", "lte", "between"]))
            {
                return false;
            }

            var values = filter.GetProperty("values");
            if (values.ValueKind != JsonValueKind.Array
                || values.GetArrayLength() is < 1 or > 100)
            {
                return false;
            }

            foreach (var value in values.EnumerateArray())
            {
                if (value.ValueKind != JsonValueKind.Object
                    || !HasOnlyProperties(value, "kind", "raw")
                    || !HasAllProperties(value, "kind", "raw")
                    || !IsEnum(value, "kind", ["text", "integer", "decimal", "boolean", "date"])
                    || value.GetProperty("raw").ValueKind != JsonValueKind.String)
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static bool ValidateCompatibility(JsonElement root, MetricCatalogDocument catalog)
    {
        var metrics = root.GetProperty("metrics").EnumerateArray()
            .Select(item => item.GetString()!).ToArray();
        var dimensions = root.GetProperty("dimensions").EnumerateArray()
            .Select(item => item.GetString()!).ToArray();
        var filters = root.GetProperty("filters").EnumerateArray()
            .Select(item => item.GetProperty("field").GetString()!).ToArray();

        return metrics.All(metric => dimensions.All(dimension =>
                catalog.IsMetricDimensionCompatible(metric, dimension))
            && filters.All(filter => catalog.IsMetricFilterCompatible(metric, filter)));
    }

    private static bool ValidateDateRange(JsonElement range)
    {
        if (range.ValueKind != JsonValueKind.Object
            || !HasOnlyProperties(range, "kind", "relativeExpression", "from", "to")
            || !HasAllProperties(range, "kind", "relativeExpression", "from", "to")
            || !IsEnum(range, "kind", ["relative", "absolute", "not_applicable"])
            || !IsNullableDate(range.GetProperty("from"))
            || !IsNullableDate(range.GetProperty("to")))
        {
            return false;
        }

        var kind = range.GetProperty("kind").GetString();
        var relative = range.GetProperty("relativeExpression");
        return kind switch
        {
            "absolute" => range.GetProperty("from").ValueKind == JsonValueKind.String
                && range.GetProperty("to").ValueKind == JsonValueKind.String,
            "relative" => relative.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(relative.GetString()),
            "not_applicable" => relative.ValueKind == JsonValueKind.Null
                && range.GetProperty("from").ValueKind == JsonValueKind.Null
                && range.GetProperty("to").ValueKind == JsonValueKind.Null,
            _ => false
        };
    }

    private bool ValidateDateConstraint(
        JsonElement range,
        SemanticCandidateConstraints? constraints)
    {
        var expected = constraints?.DateRange ?? InferNotApplicableDate(constraints);
        if (expected is null)
        {
            return true;
        }

        return range.GetProperty("kind").GetString() == DateKindName(expected.Kind)
            && NullableString(range.GetProperty("relativeExpression"))
                == expected.RelativeExpression
            && NullableString(range.GetProperty("from"))
                == expected.From?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            && NullableString(range.GetProperty("to"))
                == expected.To?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    private DateRangeSpec? InferNotApplicableDate(
        SemanticCandidateConstraints? constraints)
    {
        if (constraints is null || constraints.MetricKeys.Count == 0)
        {
            return null;
        }

        var metrics = catalogs.Values.SelectMany(catalog =>
                constraints.MetricKeys.Select(catalog.FindMetric))
            .Where(metric => metric is not null)
            .Distinct()
            .ToArray();
        return metrics.Length > 0 && metrics.All(metric => !metric!.RequiresDateRange)
            ? new DateRangeSpec { Kind = DateRangeKind.NotApplicable }
            : null;
    }

    private static string? NullableString(JsonElement value) =>
        value.ValueKind == JsonValueKind.Null ? null : value.GetString();

    private JsonObject CreateSourceConstraint(
        DataSource source,
        SemanticCandidateConstraints? constraints) => new()
    {
        ["if"] = new JsonObject
        {
            ["properties"] = new JsonObject
            {
                ["source"] = new JsonObject { ["const"] = SourceName(source) }
            },
            ["required"] = new JsonArray("source")
        },
        ["then"] = new JsonObject
        {
            ["properties"] = new JsonObject
            {
                ["metrics"] = new JsonObject
                {
                    ["items"] = EnumSchema(MetricKeys(source, constraints))
                },
                ["dimensions"] = new JsonObject
                {
                    ["items"] = EnumSchema(
                        DimensionKeys(source, constraints: constraints))
                },
                ["filters"] = new JsonObject
                {
                    ["type"] = "array",
                    ["maxItems"] = 12,
                    ["items"] = CreateFilterSchema(source, constraints)
                },
                ["orderBy"] = NullableEnumSchema(
                    DimensionKeys(source, sortable: true, constraints: constraints))
            }
        }
    };

    private JsonObject CreateFilterSchema(
        DataSource source,
        SemanticCandidateConstraints? constraints)
    {
        var schema = JsonNode.Parse(ContractResources.ReadCanonicalRequestSchema())!
            ["$defs"]!["filter"]!.DeepClone().AsObject();
        schema["properties"]!["field"] =
            EnumSchema(DimensionKeys(source, filterable: true, constraints: constraints));
        return schema;
    }

    private string[] MetricKeys(SemanticCandidateConstraints? constraints = null) =>
        catalogs.Keys.SelectMany(source => MetricKeys(source, constraints))
        .Distinct(StringComparer.Ordinal)
        .Order(StringComparer.Ordinal)
        .ToArray();

    private string[] MetricKeys(
        DataSource source,
        SemanticCandidateConstraints? constraints = null) => catalogs[source].Metrics
        .Where(metric => metric.Value.IsUsable)
        .Where(metric => constraints is null
            || constraints.MetricKeys.Contains(metric.Key, StringComparer.Ordinal))
        .Select(metric => metric.Key)
        .Order(StringComparer.Ordinal)
        .ToArray();

    private string[] DimensionKeys(bool groupable = false, bool filterable = false,
        bool sortable = false, SemanticCandidateConstraints? constraints = null) => catalogs.Keys
        .SelectMany(source => DimensionKeys(
            source, groupable, filterable, sortable, constraints))
        .Distinct(StringComparer.Ordinal)
        .Order(StringComparer.Ordinal)
        .ToArray();

    private string[] DimensionKeys(DataSource source, bool groupable = false,
        bool filterable = false, bool sortable = false,
        SemanticCandidateConstraints? constraints = null) => catalogs[source].Dimensions
        .Where(dimension => groupable
            ? dimension.Value.Selectable && dimension.Value.Groupable
            : filterable
                ? dimension.Value.Filterable
                : sortable
                    ? dimension.Value.Sortable
                    : dimension.Value.Selectable)
        .Select(dimension => dimension.Key)
        .Where(key => constraints is null
            || constraints.DimensionKeys.Contains(key, StringComparer.Ordinal))
        .Order(StringComparer.Ordinal)
        .ToArray();

    private static JsonObject OutcomeBranch(
        string outcome,
        bool canonicalRequired,
        int unresolvedMinimum,
        int unresolvedMaximum,
        bool clarificationRequired) => new()
    {
        ["properties"] = new JsonObject
        {
            ["outcome"] = new JsonObject { ["const"] = outcome },
            ["canonicalRequest"] = canonicalRequired
                ? new JsonObject { ["$ref"] = "#/$defs/canonicalRequest" }
                : new JsonObject { ["type"] = "null" },
            ["unresolvedConcepts"] = new JsonObject
            {
                ["type"] = "array",
                ["minItems"] = unresolvedMinimum,
                ["maxItems"] = unresolvedMaximum
            },
            ["clarification"] = clarificationRequired
                ? new JsonObject { ["$ref"] = "#/$defs/clarification" }
                : new JsonObject { ["type"] = "null" }
        },
        ["required"] = new JsonArray(
            "outcome", "canonicalRequest", "unresolvedConcepts", "clarification")
    };

    private static string[] UnresolvedConceptKinds() =>
        ["metric", "dimension", "filter", "date", "source"];

    private static bool TryParseOutcome(
        JsonElement value,
        out PlanningOutcome outcome)
    {
        outcome = value.ValueKind == JsonValueKind.String
            ? value.GetString() switch
            {
                "accepted" => PlanningOutcome.Accepted,
                "needs_clarification" => PlanningOutcome.NeedsClarification,
                "unsupported" => PlanningOutcome.Unsupported,
                _ => (PlanningOutcome)(-1)
            }
            : (PlanningOutcome)(-1);
        return Enum.IsDefined(outcome);
    }

    private static bool ValidateUnresolvedConcepts(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() > 5)
        {
            return false;
        }

        var kinds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var concept in value.EnumerateArray())
        {
            if (concept.ValueKind != JsonValueKind.Object
                || !HasOnlyProperties(concept, "kind")
                || !HasAllProperties(concept, "kind")
                || !IsEnum(concept, "kind", UnresolvedConceptKinds())
                || !kinds.Add(concept.GetProperty("kind").GetString()!))
            {
                return false;
            }
        }

        return true;
    }

    private static bool ValidateClarification(JsonElement value) =>
        value.ValueKind == JsonValueKind.Null
        || value.ValueKind == JsonValueKind.Object
            && HasOnlyProperties(value, "kind")
            && HasAllProperties(value, "kind")
            && IsEnum(value, "kind", UnresolvedConceptKinds());

    private static UnresolvedConceptKind ParseConceptKind(string? value) => value switch
    {
        "metric" => UnresolvedConceptKind.Metric,
        "dimension" => UnresolvedConceptKind.Dimension,
        "filter" => UnresolvedConceptKind.Filter,
        "date" => UnresolvedConceptKind.Date,
        "source" => UnresolvedConceptKind.Source,
        _ => throw new JsonException("Unknown unresolved semantic concept kind.")
    };

    private static JsonObject EnumSchema(IEnumerable<string> values) => new()
    {
        ["type"] = "string",
        ["enum"] = EnumArray(values)
    };

    private static JsonObject ConstValue(string? value) => new()
    {
        ["const"] = value is null ? null : JsonValue.Create(value)
    };

    private static string DateKindName(DateRangeKind kind) => kind switch
    {
        DateRangeKind.Relative => "relative",
        DateRangeKind.Absolute => "absolute",
        DateRangeKind.NotApplicable => "not_applicable",
        _ => throw new NotSupportedException("Unknown date range kind.")
    };

    private static JsonObject NullableEnumSchema(IEnumerable<string> values) => new()
    {
        ["anyOf"] = new JsonArray(EnumSchema(values), new JsonObject { ["type"] = "null" })
    };

    private static JsonArray EnumArray(IEnumerable<string> values) =>
        new(values.Select(value => JsonValue.Create(value)).ToArray());

    private static bool HasOnlyProperties(JsonElement value, params string[] allowed)
    {
        var set = allowed.ToHashSet(StringComparer.Ordinal);
        return value.EnumerateObject().All(property => set.Contains(property.Name));
    }

    private static bool HasAllProperties(JsonElement value, params string[] required) =>
        required.All(property => value.TryGetProperty(property, out _));

    private static bool IsExactString(JsonElement value, string propertyName, string expected) =>
        value.GetProperty(propertyName).ValueKind == JsonValueKind.String
        && string.Equals(value.GetProperty(propertyName).GetString(), expected, StringComparison.Ordinal);

    private static bool IsEnum(JsonElement value, string propertyName, IEnumerable<string> allowed)
    {
        var property = value.GetProperty(propertyName);
        return property.ValueKind == JsonValueKind.String
            && allowed.Contains(property.GetString(), StringComparer.Ordinal);
    }

    private static bool ValidateNullableEnum(JsonElement value, IEnumerable<string> allowed) =>
        value.ValueKind == JsonValueKind.Null
        || value.ValueKind == JsonValueKind.String
            && allowed.Contains(value.GetString(), StringComparer.Ordinal);

    private static bool IsStringArray(JsonElement value, IEnumerable<string> allowed, int max) =>
        value.ValueKind == JsonValueKind.Array
        && value.GetArrayLength() <= max
        && value.EnumerateArray().All(item => item.ValueKind == JsonValueKind.String
            && allowed.Contains(item.GetString(), StringComparer.Ordinal));

    private static bool IsFreeStringArray(JsonElement value, int max) =>
        value.ValueKind == JsonValueKind.Array
        && value.GetArrayLength() <= max
        && value.EnumerateArray().All(item => item.ValueKind == JsonValueKind.String);

    private static bool IsNullableBoundedString(JsonElement value, int max) =>
        value.ValueKind == JsonValueKind.Null
        || value.ValueKind == JsonValueKind.String && value.GetString()!.Length <= max;

    private static bool ValidateNullableInteger(JsonElement value, int min, int max) =>
        value.ValueKind == JsonValueKind.Null
        || value.ValueKind == JsonValueKind.Number
            && value.TryGetInt32(out var number) && number >= min && number <= max;

    private static bool ValidateConfidence(JsonElement value) =>
        value.ValueKind == JsonValueKind.Number
        && value.TryGetDouble(out var number) && number is >= 0 and <= 1;

    private static bool IsNullableDate(JsonElement value) =>
        value.ValueKind == JsonValueKind.Null
        || value.ValueKind == JsonValueKind.String
            && DateOnly.TryParseExact(value.GetString(), "yyyy-MM-dd",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out _);

    private static string SourceName(DataSource source) => source switch
    {
        DataSource.Dwh => "dwh",
        DataSource.Oltp => "oltp",
        _ => throw new NotSupportedException("Unknown source.")
    };
}
