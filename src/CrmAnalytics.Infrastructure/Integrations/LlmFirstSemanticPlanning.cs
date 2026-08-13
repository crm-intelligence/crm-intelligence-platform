using System.Globalization;
using Crm.Analytics.Sql.Catalog;
using Crm.Analytics.Sql.Contracts;
using Crm.Analytics.Sql.Nlu;

namespace CrmAnalytics.Infrastructure.Integrations;

public enum LlmFirstAssemblyOutcome
{
    Assembled,
    NeedsClarification,
    Unsupported,
    Invalid
}

public sealed record LlmFirstCanonicalAssemblyResult(
    LlmFirstAssemblyOutcome Outcome,
    CanonicalRequest? CanonicalRequest,
    string ReasonCode,
    PlanningClarification? Clarification = null,
    SemanticInterpretationDiagnostics? Diagnostics = null);

public sealed record SemanticInterpretationDiagnostics(
    bool? DeterministicResolverAgreement,
    string DivergenceCategory,
    bool ExplicitAnchorConflict);

public interface ILlmFirstCanonicalRequestAssembler
{
    LlmFirstCanonicalAssemblyResult Assemble(
        string requestId,
        string conversationId,
        string prompt,
        DateOnly today,
        DataSource? requestedSource,
        CanonicalRequest? previous,
        ExtractedSemanticIntent intent);
}

/// <summary>
/// Converts model-extracted semantic meaning into a canonical request using only the
/// authoritative catalog. Source, value types, date arithmetic and compatibility are all
/// backend decisions; rejected extractions never reach Query Builder.
/// </summary>
public sealed class LlmFirstCanonicalRequestAssembler(
    SemanticCatalogRegistry registry,
    ISemanticSourceResolver? sourceResolver = null)
    : ILlmFirstCanonicalRequestAssembler
{
    private const int MaximumLiteralLength = 512;
    private static readonly HashSet<string> ExplicitYearSuffixes =
        new(StringComparer.Ordinal)
        {
            "", "a", "e", "da", "de", "ta", "te", "dan", "den", "tan", "ten",
            "in", "nin", "yili", "yilinda", "senesi", "senesinde"
        };
    private readonly ISemanticSourceResolver effectiveSourceResolver =
        sourceResolver ?? new CatalogSemanticSourceResolver(registry);

    public LlmFirstCanonicalAssemblyResult Assemble(
        string requestId,
        string conversationId,
        string prompt,
        DateOnly today,
        DataSource? requestedSource,
        CanonicalRequest? previous,
        ExtractedSemanticIntent intent)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestId);
        ArgumentException.ThrowIfNullOrWhiteSpace(conversationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);
        ArgumentNullException.ThrowIfNull(intent);

        if (!MetricExists(intent.Metric))
        {
            return Unsupported("UNKNOWN_METRIC");
        }

        if (intent.GroupBy.Count > 4
            || intent.GroupBy.Distinct(StringComparer.Ordinal).Count()
                != intent.GroupBy.Count)
        {
            return Invalid("INVALID_GROUP_BY");
        }
        if (intent.GroupBy.Any(dimension => !DimensionExists(dimension)))
        {
            return Unsupported("UNKNOWN_GROUP_BY_DIMENSION");
        }

        if (intent.Filters.Count > 16)
        {
            return Invalid("TOO_MANY_FILTERS");
        }
        if (intent.Filters.Any(filter => !DimensionExists(filter.Dimension)))
        {
            return Unsupported("UNKNOWN_FILTER_DIMENSION");
        }

        var untypedFilters = intent.Filters.Select(filter => new RequestFilter
        {
            Field = filter.Dimension,
            Op = filter.Operator,
            Values = []
        }).ToArray();
        var sourceDecision = effectiveSourceResolver.Resolve(
            requestedSource, intent.Metric, intent.GroupBy, untypedFilters);
        if (sourceDecision.Kind == SemanticSourceDecisionKind.Ambiguous)
        {
            return Clarify("SOURCE_AMBIGUOUS", UnresolvedConceptKind.Source);
        }
        if (sourceDecision is not
            { Kind: SemanticSourceDecisionKind.Resolved, Source: not null })
        {
            return Unsupported("INCOMPATIBLE_SEMANTIC_COMBINATION");
        }

        var source = sourceDecision.Source.Value;
        var semanticSource = registry.GetRequired(source);
        var catalog = semanticSource.Catalog;
        var metric = catalog.FindMetric(intent.Metric);
        if (metric?.IsUsable != true)
        {
            return Unsupported("UNUSABLE_METRIC");
        }

        if (intent.GroupBy.Any(key =>
                catalog.FindDimension(key) is not { Groupable: true, Selectable: true })
            || intent.Filters.Any(filter =>
                catalog.FindDimension(filter.Dimension) is not { Filterable: true }))
        {
            return Unsupported("SEMANTIC_OPERATION_NOT_ALLOWED");
        }

        var filters = new List<RequestFilter>(intent.Filters.Count);
        foreach (var extracted in intent.Filters)
        {
            var definition = catalog.FindDimension(extracted.Dimension)!;
            if (!TryCreateFilter(extracted, definition, out var filter))
            {
                return Invalid("INVALID_FILTER_LITERAL");
            }
            filters.Add(filter!);
        }

        if (!intent.GroupBy.All(dimension =>
                catalog.IsMetricDimensionCompatible(intent.Metric, dimension))
            || !filters.All(filter =>
                catalog.IsMetricFilterCompatible(intent.Metric, filter.Field)))
        {
            return Unsupported("INCOMPATIBLE_SEMANTIC_COMBINATION");
        }

        var date = ResolveDate(intent.Date, today);
        if (intent.Date.Kind != ExtractedDateKind.Unspecified && date is null)
        {
            return Invalid("INVALID_DATE_INTENT");
        }
        var dateValidation = ValidateDateInterpretation(
            prompt, today, intent.Date, date);
        if (dateValidation.ExplicitAnchorConflict)
        {
            return Clarify(
                "EXPLICIT_DATE_ANCHOR_CONFLICT",
                UnresolvedConceptKind.Date,
                dateValidation);
        }
        if (intent.Date.Kind == ExtractedDateKind.Unspecified)
        {
            if (metric.RequiresDateRange)
            {
                return Clarify("DATE_REQUIRED", UnresolvedConceptKind.Date);
            }
            date = DateRangeSpec.NotApplicable;
        }
        else if (!metric.RequiresDateRange)
        {
            return Unsupported("DATE_NOT_APPLICABLE");
        }

        if (!ValidateGrain(intent.Date.Grain, intent.GroupBy, catalog))
        {
            return Invalid("INVALID_TIME_GRAIN");
        }

        string? orderBy = null;
        int? limit = null;
        var direction = SortDirection.Asc;
        if (intent.Ranking is { } ranking)
        {
            var orderDefinition = catalog.FindDimension(ranking.OrderBy);
            if (ranking.TopN <= 0
                || ranking.TopN > semanticSource.AllowList.MaxRows
                || orderDefinition is not { Sortable: true }
                || !intent.GroupBy.Contains(ranking.OrderBy, StringComparer.Ordinal))
            {
                return Invalid("INVALID_RANKING");
            }
            orderBy = ranking.OrderBy;
            limit = ranking.TopN;
            direction = ranking.Direction;
        }

        var canonical = new CanonicalRequest
        {
            RequestId = requestId,
            ConversationId = conversationId,
            PreviousRequestId = previous?.RequestId,
            Source = source,
            Intent = ResolveIntent(intent, catalog),
            Metrics = [intent.Metric],
            Dimensions = intent.GroupBy,
            Filters = filters,
            DateRange = date!,
            Grain = intent.Date.Grain,
            Limit = limit,
            OrderBy = orderBy,
            OrderDirection = direction,
            ScenarioKey = null,
            Confidence = 1,
            UnresolvedTerms = []
        };
        return new LlmFirstCanonicalAssemblyResult(
            LlmFirstAssemblyOutcome.Assembled, canonical, "NONE",
            Diagnostics: dateValidation);
    }

    private bool MetricExists(string key) => !string.IsNullOrWhiteSpace(key)
        && registry.Sources.Values.Any(source =>
            source.Catalog.FindMetric(key)?.IsUsable == true);

    private bool DimensionExists(string key) => !string.IsNullOrWhiteSpace(key)
        && registry.Sources.Values.Any(source =>
            source.Catalog.FindDimension(key) is not null);

    private static DateRangeSpec? ResolveDate(
        ExtractedDateIntent date,
        DateOnly today) => date.Kind switch
        {
            ExtractedDateKind.Unspecified => null,
            ExtractedDateKind.Relative when date.RelativeExpression is not null
                && date.From is null && date.To is null =>
                RelativeDateResolver.ResolveSemanticExpression(
                    date.RelativeExpression, date.Count, today),
            ExtractedDateKind.Absolute when date.RelativeExpression is null
                && date.Count is null
                && date.From is { } from && date.To is { } to && from <= to => new()
                {
                    Kind = DateRangeKind.Absolute,
                    From = from,
                    To = to
                },
            _ => null
        };

    private static SemanticInterpretationDiagnostics ValidateDateInterpretation(
        string prompt,
        DateOnly today,
        ExtractedDateIntent extracted,
        DateRangeSpec? resolved)
    {
        var deterministic = RelativeDateResolver.Resolve(
            TurkishTextNormalizer.Tokenize(prompt), today)?.Range;
        bool? agreement = deterministic is null
            ? null
            : DateRangesAgree(deterministic, extracted, resolved);
        var divergenceCategory = agreement switch
        {
            null => "NO_DETERMINISTIC_DATE",
            true => "NONE",
            false when deterministic!.Kind != resolved?.Kind => "DATE_KIND",
            false => "DATE_RANGE"
        };

        return new SemanticInterpretationDiagnostics(
            agreement,
            divergenceCategory,
            HasExplicitDateAnchorConflict(prompt, resolved));
    }

    private static bool DateRangesAgree(
        DateRangeSpec deterministic,
        ExtractedDateIntent extracted,
        DateRangeSpec? resolved)
    {
        if (resolved is null)
        {
            return false;
        }
        if (deterministic.Kind == DateRangeKind.Relative)
        {
            return extracted.Kind == ExtractedDateKind.Relative
                && resolved.From == deterministic.From
                && resolved.To == deterministic.To;
        }
        return deterministic.Kind != DateRangeKind.Absolute
            || extracted.Kind == ExtractedDateKind.Absolute
                && resolved?.From == deterministic.From
                && resolved?.To == deterministic.To;
    }

    private static bool HasExplicitDateAnchorConflict(
        string prompt,
        DateRangeSpec? resolved)
    {
        if (resolved?.From is not { } from || resolved.To is not { } to)
        {
            return false;
        }

        var tokens = TurkishTextNormalizer.Tokenize(prompt);
        var explicitDates = tokens
            .Select(token => ParseExplicitDate(token.Normalized))
            .Where(date => date is not null)
            .Select(date => date!.Value)
            .Take(2)
            .ToArray();
        if (explicitDates.Length == 1)
        {
            return from != explicitDates[0] || to != explicitDates[0];
        }
        if (explicitDates.Length == 2)
        {
            var expectedFrom = explicitDates.Min();
            var expectedTo = explicitDates.Max();
            return from != expectedFrom || to != expectedTo;
        }

        var years = tokens
            .Select(token => ParseExplicitYear(token.Normalized))
            .Where(year => year is not null)
            .Select(year => year!.Value)
            .Distinct()
            .Order()
            .ToArray();
        return years.Length switch
        {
            0 => false,
            1 => from.Year != years[0] || to.Year != years[0],
            _ => from.Year != years[0] || to.Year != years[^1]
        };
    }

    private static DateOnly? ParseExplicitDate(string value) =>
        DateOnly.TryParseExact(
            value,
            ["yyyy-MM-dd", "yyyy/MM/dd", "dd.MM.yyyy"],
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var parsed)
            ? parsed
            : null;

    private static int? ParseExplicitYear(string value)
    {
        if (value.Length < 4
            || !int.TryParse(value[..4], NumberStyles.None,
                CultureInfo.InvariantCulture, out var year)
            || year is < 1990 or > 2100
            || !ExplicitYearSuffixes.Contains(value[4..]))
        {
            return null;
        }

        return year;
    }

    private static bool ValidateGrain(
        TimeGrain grain,
        IReadOnlyList<string> groupBy,
        MetricCatalogDocument catalog) => grain == TimeGrain.None
        || groupBy.Any(key => catalog.FindDimension(key)?.IsTimeDimension == true);

    private static RequestIntent ResolveIntent(
        ExtractedSemanticIntent intent,
        MetricCatalogDocument catalog)
    {
        if (intent.Ranking is not null)
        {
            return RequestIntent.List;
        }
        if (intent.Date.Grain != TimeGrain.None
            && intent.GroupBy.Any(key =>
                catalog.FindDimension(key)?.IsTimeDimension == true))
        {
            return RequestIntent.Trend;
        }
        return intent.GroupBy.Count > 0
            ? RequestIntent.Breakdown
            : RequestIntent.SingleValue;
    }

    private static bool TryCreateFilter(
        ExtractedSemanticFilter extracted,
        DimensionDefinition definition,
        out RequestFilter? filter)
    {
        filter = null;
        if (!Enum.IsDefined(extracted.Operator)
            || !HasValidValueCount(extracted.Operator, extracted.Values.Count)
            || extracted.Values.Any(value => string.IsNullOrWhiteSpace(value)
                || value.Length > MaximumLiteralLength)
            || !TryMapValueKind(definition.ValueType, out var kind)
            || extracted.Values.Any(value => !IsValidLiteral(kind, value)))
        {
            return false;
        }

        filter = new RequestFilter
        {
            Field = extracted.Dimension,
            Op = extracted.Operator,
            Values = extracted.Values.Select(value =>
                new FilterLiteral(kind, value)).ToArray()
        };
        return true;
    }

    private static bool HasValidValueCount(FilterOperator op, int count) => op switch
    {
        FilterOperator.Between => count == 2,
        FilterOperator.In or FilterOperator.NotIn => count is >= 1 and <= 20,
        _ => count == 1
    };

    private static bool TryMapValueKind(string valueType, out FilterValueKind kind)
    {
        kind = valueType switch
        {
            "text" => FilterValueKind.Text,
            "integer" => FilterValueKind.Integer,
            "decimal" => FilterValueKind.Decimal,
            "boolean" => FilterValueKind.Boolean,
            "date" => FilterValueKind.Date,
            _ => (FilterValueKind)(-1)
        };
        return Enum.IsDefined(kind);
    }

    private static bool IsValidLiteral(FilterValueKind kind, string value) => kind switch
    {
        FilterValueKind.Text => true,
        FilterValueKind.Integer => int.TryParse(
            value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _),
        FilterValueKind.Decimal => decimal.TryParse(
            value, NumberStyles.Number, CultureInfo.InvariantCulture, out _),
        FilterValueKind.Boolean => bool.TryParse(value, out _),
        FilterValueKind.Date => DateOnly.TryParseExact(
            value, "yyyy-MM-dd", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out _),
        _ => false
    };

    private static LlmFirstCanonicalAssemblyResult Clarify(
        string reasonCode,
        UnresolvedConceptKind kind,
        SemanticInterpretationDiagnostics? diagnostics = null) => new(
            LlmFirstAssemblyOutcome.NeedsClarification,
            null,
            reasonCode,
            new PlanningClarification(kind),
            diagnostics);

    private static LlmFirstCanonicalAssemblyResult Unsupported(string reasonCode) =>
        new(LlmFirstAssemblyOutcome.Unsupported, null, reasonCode);

    private static LlmFirstCanonicalAssemblyResult Invalid(string reasonCode) =>
        new(LlmFirstAssemblyOutcome.Invalid, null, reasonCode);
}
