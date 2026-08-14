using Crm.Analytics.Sql.Catalog;
using Crm.Analytics.Sql.Contracts;
using Crm.Analytics.Sql.Contracts.V2;
using Crm.Analytics.Sql.QueryBuilder;

namespace Crm.Analytics.Sql.Routing;

/// <summary>
/// Deterministically classifies a validated semantic query against backend-owned catalog
/// operations and the current builder capability descriptor. It is not an authorization,
/// data-scope, credential, execution-policy or final SQL security authority.
/// </summary>
internal sealed class QueryCapabilityAnalyzer(
    MetricCatalogDocument catalog,
    DeterministicQueryCapabilities deterministicCapabilities)
{
    public QueryStrategyDecision Analyze(CanonicalQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);

        var features = DetectFeatures(query);
        var unsupported = ValidateSemantics(query);
        if (unsupported.Count > 0)
        {
            return Decision(
                QueryCapabilityOutcome.Unsupported,
                features,
                unsupported,
                UnsupportedFailureReason(unsupported));
        }

        var agenticReasons = DetectCapabilityGaps(query);
        if (agenticReasons.Count > 0)
        {
            return Decision(
                QueryCapabilityOutcome.AgenticRequired,
                features,
                agenticReasons,
                agenticReasons.Contains(QueryCapabilityReasonCode.TimeGrainRequiresAgentic)
                    ? ReasonCode.CL002
                    : ReasonCode.GR014);
        }

        return Decision(
            QueryCapabilityOutcome.Deterministic,
            features,
            [QueryCapabilityReasonCode.DeterministicCapabilitiesSatisfied],
            ReasonCode.None);
    }

    private List<QueryCapabilityReasonCode> ValidateSemantics(CanonicalQuery query)
    {
        var reasons = new List<QueryCapabilityReasonCode>();

        if (query.Version != CanonicalQuery.CurrentVersion)
        {
            reasons.Add(QueryCapabilityReasonCode.UnsupportedCanonicalVersion);
            return reasons;
        }

        if (query.Metrics.Count == 0 && query.Dimensions.Count == 0)
        {
            reasons.Add(QueryCapabilityReasonCode.EmptyProjection);
        }

        var metrics = query.Metrics
            .Select(key => (Key: key, Definition: catalog.FindMetric(key)))
            .ToArray();
        foreach (var metric in metrics)
        {
            if (metric.Definition is null)
            {
                Add(reasons, QueryCapabilityReasonCode.UnknownMetric);
            }
            else if (!metric.Definition.IsUsable)
            {
                Add(reasons, QueryCapabilityReasonCode.UnusableMetric);
            }
        }

        var dimensions = query.Dimensions
            .Select(key => (Key: key, Definition: catalog.FindDimension(key)))
            .ToArray();
        foreach (var dimension in dimensions)
        {
            if (dimension.Definition is null)
            {
                Add(reasons, QueryCapabilityReasonCode.UnknownDimension);
            }
            else if (!dimension.Definition.Selectable
                || (query.Metrics.Count > 0 && !dimension.Definition.Groupable))
            {
                Add(reasons, QueryCapabilityReasonCode.SemanticOperationNotAllowed);
            }
        }

        var filters = query.Filters
            .Select(filter => (Filter: filter, Definition: catalog.FindDimension(filter.Field)))
            .ToArray();
        foreach (var filter in filters)
        {
            if (!IsValidFilterShape(filter.Filter))
            {
                Add(reasons, QueryCapabilityReasonCode.InvalidFilter);
            }
            else if (filter.Definition is null)
            {
                Add(reasons, QueryCapabilityReasonCode.UnknownFilterDimension);
            }
            else if (!filter.Definition.Filterable)
            {
                Add(reasons, QueryCapabilityReasonCode.SemanticOperationNotAllowed);
            }
        }

        if (!Enum.IsDefined(query.Time.Grain)
            || !Enum.IsDefined(query.Time.Range.Kind))
        {
            Add(reasons, QueryCapabilityReasonCode.InvalidTime);
        }

        foreach (var metric in metrics.Where(item => item.Definition is not null))
        {
            if (dimensions.Any(dimension => dimension.Definition is not null
                && !catalog.IsMetricDimensionCompatible(metric.Key, dimension.Key)))
            {
                Add(reasons, QueryCapabilityReasonCode.IncompatibleMetricDimension);
            }

            if (filters.Any(filter => filter.Definition is not null
                && !catalog.IsMetricFilterCompatible(metric.Key, filter.Filter.Field)))
            {
                Add(reasons, QueryCapabilityReasonCode.IncompatibleMetricFilter);
            }
        }

        var sources = metrics.Where(item => item.Definition is not null)
            .Select(item => item.Definition!.Source)
            .Concat(dimensions.Where(item => item.Definition is not null)
                .Select(item => item.Definition!.Source))
            .Concat(filters.Where(item => item.Definition is not null)
                .Select(item => item.Definition!.Source))
            .Concat(query.Ordering
                .Where(item => item.TargetKind == CanonicalOrderingTargetKind.Dimension)
                .Select(item => catalog.FindDimension(item.TargetKey)?.Source)
                .Where(item => item is not null)
                .Select(item => item!))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(2)
            .ToArray();
        if (sources.Length > 1)
        {
            Add(reasons, QueryCapabilityReasonCode.UnapprovedMultiSourceCombination);
        }

        ValidateOrdering(query, reasons);
        ValidateLimit(query, reasons);
        ValidateComparisons(query, reasons);
        ValidateCalculations(query, reasons);

        return reasons;
    }

    private void ValidateOrdering(
        CanonicalQuery query,
        ICollection<QueryCapabilityReasonCode> reasons)
    {
        foreach (var ordering in query.Ordering)
        {
            if (!Enum.IsDefined(ordering.TargetKind)
                || !Enum.IsDefined(ordering.Direction)
                || string.IsNullOrWhiteSpace(ordering.TargetKey))
            {
                Add(reasons, QueryCapabilityReasonCode.InvalidOrdering);
                continue;
            }

            if (ordering.TargetKind == CanonicalOrderingTargetKind.Dimension)
            {
                var dimension = catalog.FindDimension(ordering.TargetKey);
                if (dimension is not { Sortable: true })
                {
                    Add(reasons, QueryCapabilityReasonCode.InvalidOrdering);
                }
            }
            else
            {
                var metric = catalog.FindMetric(ordering.TargetKey);
                if (metric?.IsUsable != true
                    || !query.Metrics.Contains(ordering.TargetKey, StringComparer.Ordinal))
                {
                    Add(reasons, QueryCapabilityReasonCode.InvalidOrdering);
                }
            }
        }
    }

    private static bool IsValidFilterShape(RequestFilter filter)
    {
        if (!Enum.IsDefined(filter.Op) || filter.Values.Count == 0)
        {
            return false;
        }

        return filter.Op switch
        {
            FilterOperator.In or FilterOperator.NotIn => true,
            FilterOperator.Between => filter.Values.Count == 2,
            _ => filter.Values.Count == 1
        };
    }

    private static void ValidateLimit(
        CanonicalQuery query,
        ICollection<QueryCapabilityReasonCode> reasons)
    {
        if (query.Limit is { } limit
            && (limit.Count <= 0
                || !Enum.IsDefined(limit.Kind)
                || limit.Kind == CanonicalLimitKind.Top
                    && (query.Ordering.Count == 0
                        || query.Ordering[0].Direction != SortDirection.Desc)
                || limit.Kind == CanonicalLimitKind.Bottom
                    && (query.Ordering.Count == 0
                        || query.Ordering[0].Direction != SortDirection.Asc)))
        {
            Add(reasons, QueryCapabilityReasonCode.InvalidLimit);
        }
    }

    private static void ValidateComparisons(
        CanonicalQuery query,
        ICollection<QueryCapabilityReasonCode> reasons)
    {
        if (query.Comparisons.Any(comparison => !Enum.IsDefined(comparison.Kind)))
        {
            Add(reasons, QueryCapabilityReasonCode.InvalidPeriodComparison);
        }
    }

    private static void ValidateCalculations(
        CanonicalQuery query,
        ICollection<QueryCapabilityReasonCode> reasons)
    {
        foreach (var calculation in query.Calculations)
        {
            if (!Enum.IsDefined(calculation.Kind)
                || calculation.MetricKeys.Count == 0
                || calculation.MetricKeys.Any(metric =>
                    !query.Metrics.Contains(metric, StringComparer.Ordinal)))
            {
                Add(reasons, QueryCapabilityReasonCode.InvalidCalculation);
            }
        }
    }

    private List<QueryCapabilityReasonCode> DetectCapabilityGaps(CanonicalQuery query)
    {
        var reasons = new List<QueryCapabilityReasonCode>();
        var hasSelectedTimeDimension = query.Dimensions.Any(key =>
            catalog.FindDimension(key)?.IsTimeDimension == true);

        if (hasSelectedTimeDimension
            && !deterministicCapabilities.SupportsTimeGrain(query.Time.Grain))
        {
            reasons.Add(QueryCapabilityReasonCode.TimeGrainRequiresAgentic);
        }
        if (query.Comparisons.Count > 0
            && !deterministicCapabilities.SupportsPeriodComparisons)
        {
            reasons.Add(QueryCapabilityReasonCode.PeriodComparisonRequiresAgentic);
        }
        if (query.Calculations.Count > 0
            && !deterministicCapabilities.SupportsCalculations)
        {
            reasons.Add(QueryCapabilityReasonCode.CalculationRequiresAgentic);
        }
        if (query.Ordering.Any(ordering =>
                !deterministicCapabilities.SupportsOrdering(ordering.TargetKind)))
        {
            reasons.Add(QueryCapabilityReasonCode.MetricOrderingRequiresAgentic);
        }
        if (query.Ordering.Count > deterministicCapabilities.MaximumExplicitOrderings)
        {
            reasons.Add(QueryCapabilityReasonCode.MultipleOrderingsRequireAgentic);
        }

        return reasons;
    }

    private static IReadOnlyList<QueryFeature> DetectFeatures(CanonicalQuery query)
    {
        var features = new List<QueryFeature>();
        if (query.Metrics.Count > 1) features.Add(QueryFeature.MultipleMetrics);
        if (query.Dimensions.Count > 1) features.Add(QueryFeature.MultipleDimensions);
        if (query.Filters.Count > 0) features.Add(QueryFeature.Filters);
        if (query.Time.Range.Kind != DateRangeKind.NotApplicable) features.Add(QueryFeature.TimeRange);
        if (query.Time.Grain != TimeGrain.None) features.Add(QueryFeature.TimeGrain);
        if (query.Ordering.Count > 0) features.Add(QueryFeature.Ordering);
        if (query.Ordering.Count > 1) features.Add(QueryFeature.MultipleOrderings);
        if (query.Ordering.Any(item => item.TargetKind == CanonicalOrderingTargetKind.Metric))
            features.Add(QueryFeature.MetricOrdering);
        if (query.Limit is not null) features.Add(QueryFeature.Limit);
        if (query.Comparisons.Count > 0) features.Add(QueryFeature.PeriodComparison);
        if (query.Calculations.Count > 0) features.Add(QueryFeature.Calculation);
        return features;
    }

    private static QueryStrategyDecision Decision(
        QueryCapabilityOutcome outcome,
        IReadOnlyList<QueryFeature> features,
        IReadOnlyList<QueryCapabilityReasonCode> reasons,
        ReasonCode failureReasonCode) => new()
        {
            Outcome = outcome,
            Complexity = ClassifyComplexity(features),
            Features = features,
            Reasons = reasons,
            FailureReasonCode = failureReasonCode
        };

    private static QueryComplexity ClassifyComplexity(IReadOnlyList<QueryFeature> features)
    {
        var advancedCount = features.Count(feature => feature is
            QueryFeature.PeriodComparison or QueryFeature.Calculation
            or QueryFeature.MetricOrdering or QueryFeature.MultipleOrderings);
        if (advancedCount > 1) return QueryComplexity.VeryComplex;
        if (advancedCount == 1) return QueryComplexity.Complex;
        return features.Count == 0 ? QueryComplexity.Simple : QueryComplexity.Moderate;
    }

    private static ReasonCode UnsupportedFailureReason(
        IReadOnlyCollection<QueryCapabilityReasonCode> reasons)
    {
        if (reasons.Contains(QueryCapabilityReasonCode.UnsupportedCanonicalVersion))
            return ReasonCode.GR014;
        if (reasons.Contains(QueryCapabilityReasonCode.UnapprovedMultiSourceCombination))
            return ReasonCode.GR006;
        if (reasons.Any(reason => reason is QueryCapabilityReasonCode.InvalidOrdering
            or QueryCapabilityReasonCode.InvalidLimit
            or QueryCapabilityReasonCode.InvalidTime))
        {
            return ReasonCode.CL002;
        }
        return ReasonCode.CL001;
    }

    private static void Add(
        ICollection<QueryCapabilityReasonCode> reasons,
        QueryCapabilityReasonCode reason)
    {
        if (!reasons.Contains(reason)) reasons.Add(reason);
    }
}
