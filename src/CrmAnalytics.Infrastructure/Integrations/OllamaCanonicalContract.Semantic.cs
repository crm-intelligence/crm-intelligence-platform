using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Crm.Analytics.Sql.Contracts;

namespace CrmAnalytics.Infrastructure.Integrations;

public sealed partial class OllamaCanonicalContract
{
    private static readonly string[] SemanticRelativeExpressions =
    [
        "today", "yesterday",
        "current_week", "previous_week",
        "current_month", "previous_month",
        "current_quarter", "previous_quarter",
        "current_year", "previous_year",
        "last_n_days", "last_n_weeks", "last_n_months", "last_n_years"
    ];

    public JsonObject CreateSemanticSchema(DateRangeKind? dateKindConstraint = null)
    {
        var filter = new JsonObject
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["required"] = new JsonArray("dimension", "operator", "values"),
            ["properties"] = new JsonObject
            {
                ["dimension"] = SemanticKeySchema(),
                ["operator"] = SemanticEnumSchema(
                    ["eq", "not_eq", "in", "not_in", "gt", "gte", "lt", "lte", "between"]),
                ["values"] = new JsonObject
                {
                    ["type"] = "array",
                    ["minItems"] = 1,
                    ["maxItems"] = 20,
                    ["items"] = new JsonObject
                    {
                        ["type"] = "string",
                        ["minLength"] = 1,
                        ["maxLength"] = 512
                    }
                }
            }
        };
        var date = new JsonObject
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["required"] = new JsonArray(
                "kind", "relativeExpression", "count", "from", "to", "grain"),
            ["properties"] = new JsonObject
            {
                ["kind"] = SemanticEnumSchema(dateKindConstraint switch
                {
                    DateRangeKind.Relative => ["relative"],
                    DateRangeKind.Absolute => ["absolute"],
                    _ => ["unspecified", "relative", "absolute"]
                }),
                ["relativeExpression"] = SemanticNullableEnumSchema(
                    SemanticRelativeExpressions),
                ["count"] = new JsonObject
                {
                    ["anyOf"] = new JsonArray(
                        new JsonObject
                        {
                            ["type"] = "integer",
                            ["minimum"] = 1,
                            ["maximum"] = 3660
                        },
                        new JsonObject { ["type"] = "null" })
                },
                ["from"] = SemanticNullableDateSchema(),
                ["to"] = SemanticNullableDateSchema(),
                ["grain"] = SemanticEnumSchema(
                    ["none", "day", "week", "month", "quarter", "year"])
            }
        };
        var ranking = new JsonObject
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["required"] = new JsonArray("topN", "orderBy", "direction"),
            ["properties"] = new JsonObject
            {
                ["topN"] = new JsonObject
                {
                    ["type"] = "integer",
                    ["minimum"] = 1,
                    ["maximum"] = maximumLimit
                },
                ["orderBy"] = SemanticKeySchema(),
                ["direction"] = SemanticEnumSchema(["asc", "desc"])
            }
        };
        var semanticIntent = new JsonObject
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["required"] = new JsonArray(
                "metric", "groupBy", "filters", "date", "ranking"),
            ["properties"] = new JsonObject
            {
                ["metric"] = SemanticKeySchema(),
                ["groupBy"] = new JsonObject
                {
                    ["type"] = "array",
                    ["maxItems"] = 4,
                    ["uniqueItems"] = true,
                    ["items"] = SemanticKeySchema()
                },
                ["filters"] = new JsonObject
                {
                    ["type"] = "array",
                    ["maxItems"] = 16,
                    ["items"] = filter.DeepClone()
                },
                ["date"] = date.DeepClone(),
                ["ranking"] = new JsonObject
                {
                    ["anyOf"] = new JsonArray(
                        ranking.DeepClone(),
                        new JsonObject { ["type"] = "null" })
                }
            }
        };
        var unresolved = SemanticSlotSchema();
        var clarification = SemanticSlotSchema();

        return new JsonObject
        {
            ["title"] = "Structured Semantic Intent Planning Result",
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["required"] = new JsonArray(
                "outcome", "semanticIntent", "unresolvedConcepts", "clarification"),
            ["properties"] = new JsonObject
            {
                ["outcome"] = SemanticEnumSchema(
                    ["accepted", "needs_clarification", "unsupported"]),
                ["semanticIntent"] = new JsonObject
                {
                    ["anyOf"] = new JsonArray(
                        semanticIntent.DeepClone(),
                        new JsonObject { ["type"] = "null" })
                },
                ["unresolvedConcepts"] = new JsonObject
                {
                    ["type"] = "array",
                    ["maxItems"] = 5,
                    ["uniqueItems"] = true,
                    ["items"] = unresolved.DeepClone()
                },
                ["clarification"] = new JsonObject
                {
                    ["anyOf"] = new JsonArray(
                        clarification.DeepClone(),
                        new JsonObject { ["type"] = "null" })
                }
            }
        };
    }

    public string CreateSemanticCatalogPrompt()
    {
        var metrics = catalogs.Values
            .SelectMany(catalog => catalog.Metrics)
            .Where(item => item.Value.IsUsable)
            .GroupBy(item => item.Key, StringComparer.Ordinal)
            .Select(group => group.First())
            .Select(item => new
            {
                semanticKey = item.Key,
                label = item.Value.Label,
                description = item.Value.Description,
                conceptualAliases = item.Value.Aliases,
                allowedSemanticOperations = new[] { "measure" }
            });
        var dimensions = catalogs.Values
            .SelectMany(catalog => catalog.Dimensions)
            .GroupBy(item => item.Key, StringComparer.Ordinal)
            .Select(group => group.First())
            .Select(item => new
            {
                semanticKey = item.Key,
                label = item.Value.Label,
                description = item.Value.Description,
                conceptualAliases = item.Value.Aliases,
                allowedSemanticOperations = AllowedOperations(item.Key)
            });
        return JsonSerializer.Serialize(metrics.Cast<object>().Concat(dimensions));
    }

    public bool ValidateSemantic(JsonElement root)
    {
        if (!SemanticObjectHasExactly(root,
                "outcome", "semanticIntent", "unresolvedConcepts", "clarification")
            || !TryParseOutcome(root.GetProperty("outcome"), out var outcome)
            || !ValidateUnresolvedConcepts(root.GetProperty("unresolvedConcepts"))
            || !ValidateClarification(root.GetProperty("clarification")))
        {
            return false;
        }

        var intent = root.GetProperty("semanticIntent");
        var unresolvedCount = root.GetProperty("unresolvedConcepts").GetArrayLength();
        var clarification = root.GetProperty("clarification");
        return outcome switch
        {
            PlanningOutcome.Accepted => intent.ValueKind == JsonValueKind.Object
                && unresolvedCount == 0
                && clarification.ValueKind == JsonValueKind.Null
                && ValidateSemanticIntent(intent),
            PlanningOutcome.NeedsClarification => intent.ValueKind == JsonValueKind.Null
                && clarification.ValueKind == JsonValueKind.Object,
            PlanningOutcome.Unsupported => intent.ValueKind == JsonValueKind.Null
                && unresolvedCount > 0
                && clarification.ValueKind == JsonValueKind.Null,
            _ => false
        };
    }

    public ExtractedSemanticPlanningResult DeserializeSemantic(JsonElement root)
    {
        if (!TryParseOutcome(root.GetProperty("outcome"), out var outcome))
        {
            throw new JsonException("Unknown semantic planning outcome.");
        }

        var intentElement = root.GetProperty("semanticIntent");
        var intent = intentElement.ValueKind == JsonValueKind.Null
            ? null
            : DeserializeSemanticIntent(intentElement);
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
        return new ExtractedSemanticPlanningResult(
            outcome, intent, unresolved, clarification);
    }

    public string ClassifySemanticValidationFailure(JsonElement root)
    {
        if (!SemanticObjectHasExactly(root,
                "outcome", "semanticIntent", "unresolvedConcepts", "clarification")
            || !TryParseOutcome(root.GetProperty("outcome"), out var outcome)
            || !ValidateUnresolvedConcepts(root.GetProperty("unresolvedConcepts"))
            || !ValidateClarification(root.GetProperty("clarification")))
        {
            return "Envelope";
        }

        var intent = root.GetProperty("semanticIntent");
        var unresolved = root.GetProperty("unresolvedConcepts");
        var clarification = root.GetProperty("clarification");
        if (outcome == PlanningOutcome.NeedsClarification)
        {
            return intent.ValueKind == JsonValueKind.Null
                && clarification.ValueKind == JsonValueKind.Object
                    ? "Unknown" : "OutcomeInvariant";
        }
        if (outcome == PlanningOutcome.Unsupported)
        {
            return intent.ValueKind == JsonValueKind.Null
                && unresolved.GetArrayLength() > 0
                && clarification.ValueKind == JsonValueKind.Null
                    ? "Unknown" : "OutcomeInvariant";
        }
        if (intent.ValueKind != JsonValueKind.Object
            || unresolved.GetArrayLength() != 0
            || clarification.ValueKind != JsonValueKind.Null)
        {
            return "OutcomeInvariant";
        }
        if (!SemanticObjectHasExactly(intent,
                "metric", "groupBy", "filters", "date", "ranking"))
        {
            return "IntentShape";
        }
        var metric = intent.GetProperty("metric");
        if (metric.ValueKind != JsonValueKind.String
            || !MetricKeys().Contains(metric.GetString()!, StringComparer.Ordinal))
        {
            return "Metric";
        }
        if (!SemanticStringArray(
                intent.GetProperty("groupBy"), DimensionKeys(groupable: true), 4))
        {
            return "GroupBy";
        }
        var filters = intent.GetProperty("filters");
        if (filters.ValueKind != JsonValueKind.Array
            || filters.GetArrayLength() > 16
            || filters.EnumerateArray().Any(filter => !ValidateSemanticFilter(filter)))
        {
            return "Filter";
        }
        if (!ValidateSemanticDate(intent.GetProperty("date")))
        {
            return "Date";
        }
        var ranking = intent.GetProperty("ranking");
        return ranking.ValueKind == JsonValueKind.Null
            || ValidateSemanticRanking(ranking)
                ? "Unknown" : "Ranking";
    }

    private bool ValidateSemanticIntent(JsonElement intent)
    {
        if (!SemanticObjectHasExactly(intent,
                "metric", "groupBy", "filters", "date", "ranking"))
        {
            return false;
        }

        var metric = intent.GetProperty("metric");
        var groupBy = intent.GetProperty("groupBy");
        var filters = intent.GetProperty("filters");
        var ranking = intent.GetProperty("ranking");
        return metric.ValueKind == JsonValueKind.String
            && MetricKeys().Contains(metric.GetString()!, StringComparer.Ordinal)
            && SemanticStringArray(groupBy, DimensionKeys(groupable: true), 4)
            && filters.ValueKind == JsonValueKind.Array
            && filters.GetArrayLength() <= 16
            && filters.EnumerateArray().All(ValidateSemanticFilter)
            && ValidateSemanticDate(intent.GetProperty("date"))
            && (ranking.ValueKind == JsonValueKind.Null
                || ValidateSemanticRanking(ranking));
    }

    private bool ValidateSemanticFilter(JsonElement filter)
    {
        if (!SemanticObjectHasExactly(filter, "dimension", "operator", "values"))
        {
            return false;
        }
        var dimension = filter.GetProperty("dimension");
        var op = filter.GetProperty("operator");
        var values = filter.GetProperty("values");
        if (dimension.ValueKind != JsonValueKind.String
            || !DimensionKeys(filterable: true).Contains(
                dimension.GetString()!, StringComparer.Ordinal)
            || op.ValueKind != JsonValueKind.String
            || !TryParseFilterOperator(op.GetString(), out var parsed)
            || values.ValueKind != JsonValueKind.Array
            || values.GetArrayLength() is < 1 or > 20
            || values.EnumerateArray().Any(value => value.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(value.GetString())
                || value.GetString()!.Length > 512))
        {
            return false;
        }
        var count = values.GetArrayLength();
        return parsed switch
        {
            FilterOperator.Between => count == 2,
            FilterOperator.In or FilterOperator.NotIn => true,
            _ => count == 1
        };
    }

    private static bool ValidateSemanticDate(JsonElement date)
    {
        if (!SemanticObjectHasExactly(date,
                "kind", "relativeExpression", "count", "from", "to", "grain")
            || date.GetProperty("kind").ValueKind != JsonValueKind.String
            || !TryParseGrain(date.GetProperty("grain").GetString(), out _))
        {
            return false;
        }
        var kind = date.GetProperty("kind").GetString();
        var expression = date.GetProperty("relativeExpression");
        var count = date.GetProperty("count");
        var from = date.GetProperty("from");
        var to = date.GetProperty("to");
        return kind switch
        {
            "unspecified" => expression.ValueKind == JsonValueKind.Null
                && count.ValueKind == JsonValueKind.Null
                && from.ValueKind == JsonValueKind.Null
                && to.ValueKind == JsonValueKind.Null
                && date.GetProperty("grain").GetString() == "none",
            "relative" => expression.ValueKind == JsonValueKind.String
                && SemanticRelativeExpressions.Contains(
                    expression.GetString()!, StringComparer.Ordinal)
                && IsValidRelativeCount(expression.GetString()!, count)
                && from.ValueKind == JsonValueKind.Null
                && to.ValueKind == JsonValueKind.Null,
            "absolute" => expression.ValueKind == JsonValueKind.Null
                && count.ValueKind == JsonValueKind.Null
                && TryReadDate(from, out var fromDate)
                && TryReadDate(to, out var toDate)
                && fromDate <= toDate,
            _ => false
        };
    }

    private bool ValidateSemanticRanking(JsonElement ranking) =>
        SemanticObjectHasExactly(ranking, "topN", "orderBy", "direction")
        && ranking.GetProperty("topN").TryGetInt32(out var topN)
        && topN is > 0
        && topN <= maximumLimit
        && ranking.GetProperty("orderBy").ValueKind == JsonValueKind.String
        && DimensionKeys(sortable: true).Contains(
            ranking.GetProperty("orderBy").GetString()!, StringComparer.Ordinal)
        && ranking.GetProperty("direction").ValueKind == JsonValueKind.String
        && ranking.GetProperty("direction").GetString() is "asc" or "desc";

    private static ExtractedSemanticIntent DeserializeSemanticIntent(JsonElement intent)
    {
        var date = intent.GetProperty("date");
        var rankingElement = intent.GetProperty("ranking");
        return new ExtractedSemanticIntent(
            intent.GetProperty("metric").GetString()!,
            intent.GetProperty("groupBy").EnumerateArray()
                .Select(item => item.GetString()!).ToArray(),
            intent.GetProperty("filters").EnumerateArray()
                .Select(item => new ExtractedSemanticFilter(
                    item.GetProperty("dimension").GetString()!,
                    ParseFilterOperator(item.GetProperty("operator").GetString()),
                    item.GetProperty("values").EnumerateArray()
                        .Select(value => value.GetString()!).ToArray()))
                .ToArray(),
            new ExtractedDateIntent(
                date.GetProperty("kind").GetString() switch
                {
                    "relative" => ExtractedDateKind.Relative,
                    "absolute" => ExtractedDateKind.Absolute,
                    _ => ExtractedDateKind.Unspecified
                },
                SemanticNullableString(date.GetProperty("relativeExpression")),
                date.GetProperty("count").ValueKind == JsonValueKind.Number
                    ? date.GetProperty("count").GetInt32() : null,
                ReadNullableDate(date.GetProperty("from")),
                ReadNullableDate(date.GetProperty("to")),
                ParseGrain(date.GetProperty("grain").GetString())),
            rankingElement.ValueKind == JsonValueKind.Null
                ? null
                : new ExtractedRankingIntent(
                    rankingElement.GetProperty("topN").GetInt32(),
                    rankingElement.GetProperty("orderBy").GetString()!,
                    rankingElement.GetProperty("direction").GetString() == "desc"
                        ? SortDirection.Desc : SortDirection.Asc));
    }

    private IReadOnlyList<string> AllowedOperations(string dimensionKey)
    {
        var definitions = catalogs.Values
            .Select(catalog => catalog.FindDimension(dimensionKey))
            .Where(definition => definition is not null)
            .Cast<Crm.Analytics.Sql.Catalog.DimensionDefinition>()
            .ToArray();
        var operations = new List<string>();
        if (definitions.Any(item => item.Groupable)) operations.Add("group_by");
        if (definitions.Any(item => item.Filterable)) operations.Add("filter");
        if (definitions.Any(item => item.Sortable)) operations.Add("rank");
        return operations;
    }

    private static JsonObject SemanticSlotSchema() => new()
    {
        ["type"] = "object",
        ["additionalProperties"] = false,
        ["required"] = new JsonArray("kind"),
        ["properties"] = new JsonObject
        {
            ["kind"] = SemanticEnumSchema(
                ["metric", "dimension", "filter", "date"])
        }
    };

    private static JsonObject SemanticEnumSchema(IEnumerable<string> values) => new()
    {
        ["type"] = "string",
        ["enum"] = new JsonArray(values.Select(value =>
            JsonValue.Create(value)).ToArray())
    };

    private static JsonObject SemanticKeySchema() => new()
    {
        ["type"] = "string",
        ["minLength"] = 2,
        ["maxLength"] = 64,
        ["pattern"] = "^[a-z][a-z0-9_]{1,63}$"
    };

    private static JsonObject SemanticNullableEnumSchema(IEnumerable<string> values) =>
        new()
        {
            ["anyOf"] = new JsonArray(
                SemanticEnumSchema(values),
                new JsonObject { ["type"] = "null" })
        };

    private static JsonObject SemanticNullableDateSchema() => new()
    {
        ["anyOf"] = new JsonArray(
            new JsonObject
            {
                ["type"] = "string",
                ["format"] = "date",
                ["pattern"] = "^[0-9]{4}-[0-9]{2}-[0-9]{2}$"
            },
            new JsonObject { ["type"] = "null" })
    };

    private static bool SemanticObjectHasExactly(
        JsonElement element,
        params string[] names)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return false;
        }
        var properties = element.EnumerateObject().Select(item => item.Name).ToArray();
        return properties.Length == names.Length
            && names.All(name => properties.Contains(name, StringComparer.Ordinal));
    }

    private static bool SemanticStringArray(
        JsonElement element,
        IReadOnlyList<string> allowed,
        int maximum) => element.ValueKind == JsonValueKind.Array
        && element.GetArrayLength() <= maximum
        && element.EnumerateArray().All(item => item.ValueKind == JsonValueKind.String
            && allowed.Contains(item.GetString()!, StringComparer.Ordinal))
        && element.EnumerateArray().Select(item => item.GetString()).Distinct().Count()
            == element.GetArrayLength();

    private static bool IsValidRelativeCount(string expression, JsonElement count) =>
        expression.StartsWith("last_n_", StringComparison.Ordinal)
            ? count.TryGetInt32(out var value) && value is > 0 and <= 3660
            : count.ValueKind == JsonValueKind.Null;

    private static bool TryReadDate(JsonElement element, out DateOnly date)
    {
        date = default;
        return element.ValueKind == JsonValueKind.String
            && DateOnly.TryParseExact(element.GetString(), "yyyy-MM-dd",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
    }

    private static DateOnly? ReadNullableDate(JsonElement element) =>
        TryReadDate(element, out var date) ? date : null;

    private static string? SemanticNullableString(JsonElement element) =>
        element.ValueKind == JsonValueKind.String ? element.GetString() : null;

    private static bool TryParseFilterOperator(string? value, out FilterOperator op)
    {
        op = value switch
        {
            "eq" => FilterOperator.Eq,
            "not_eq" => FilterOperator.NotEq,
            "in" => FilterOperator.In,
            "not_in" => FilterOperator.NotIn,
            "gt" => FilterOperator.Gt,
            "gte" => FilterOperator.Gte,
            "lt" => FilterOperator.Lt,
            "lte" => FilterOperator.Lte,
            "between" => FilterOperator.Between,
            _ => (FilterOperator)(-1)
        };
        return Enum.IsDefined(op);
    }

    private static FilterOperator ParseFilterOperator(string? value) =>
        TryParseFilterOperator(value, out var op)
            ? op : throw new JsonException("Unknown filter operator.");

    private static bool TryParseGrain(string? value, out TimeGrain grain)
    {
        grain = value switch
        {
            "none" => TimeGrain.None,
            "day" => TimeGrain.Day,
            "week" => TimeGrain.Week,
            "month" => TimeGrain.Month,
            "quarter" => TimeGrain.Quarter,
            "year" => TimeGrain.Year,
            _ => (TimeGrain)(-1)
        };
        return Enum.IsDefined(grain);
    }

    private static TimeGrain ParseGrain(string? value) =>
        TryParseGrain(value, out var grain)
            ? grain : throw new JsonException("Unknown time grain.");
}
