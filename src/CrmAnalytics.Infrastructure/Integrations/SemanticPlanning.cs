using System.Diagnostics;
using Crm.Analytics.Sql.Catalog;
using Crm.Analytics.Sql.Contracts;
using Crm.Analytics.Sql.Nlu;

namespace CrmAnalytics.Infrastructure.Integrations;

public sealed record SemanticSlotGap(
    UnresolvedConceptKind Kind,
    IReadOnlyList<string> CandidateKeys);

public sealed record SemanticPlanningState(
    string? ResolvedMetric,
    IReadOnlyList<string> ResolvedDimensions,
    DateRangeSpec? ResolvedDate,
    IReadOnlyList<RequestFilter> ResolvedFilters,
    DataSource? ResolvedSource,
    IReadOnlyList<SemanticSlotGap> AmbiguousSlots,
    IReadOnlyList<UnresolvedConceptKind> UnsupportedSlots,
    IReadOnlyList<UnresolvedConceptKind> MissingSlots,
    double Confidence,
    SemanticSlotIntent SlotIntent,
    SemanticCompletenessResult Completeness,
    IReadOnlyList<SemanticSlotGap> CandidateSlots,
    FilterLiteralResolution? FilterLiteral = null,
    SemanticSourceDecision? SourceDecision = null)
{
    public IReadOnlyList<SemanticSlotGap> QwenResolvableSlots => CandidateSlots
        .Where(candidate => AmbiguousSlots.Any(slot => slot.Kind == candidate.Kind)
            || MissingSlots.Contains(candidate.Kind))
        .ToArray();

    public bool IsFullyResolved => Completeness.IsComplete
        && ResolvedMetric is not null
        && ResolvedSource is not null
        && ResolvedDate is not null
        && AmbiguousSlots.Count == 0
        && UnsupportedSlots.Count == 0
        && MissingSlots.Count == 0;
}

public enum SemanticCanonicalAssemblyOutcome
{
    Assembled,
    Ambiguous,
    Unsupported,
    Missing,
    Invalid
}

public sealed record SemanticCanonicalAssemblyResult(
    SemanticCanonicalAssemblyOutcome Outcome,
    SemanticPlanningState State,
    CanonicalRequest? CanonicalRequest,
    long DurationMilliseconds,
    string? FailureKind = null);

public interface ISemanticCanonicalRequestAssembler
{
    SemanticCanonicalAssemblyResult Assemble(
        string requestId,
        string conversationId,
        string prompt,
        DataSource? requestedSource,
        CanonicalRequest? partiallyResolved,
        SemanticResolverResult semantic);

    SemanticCanonicalAssemblyResult AssembleState(
        string requestId,
        string conversationId,
        string prompt,
        CanonicalRequest? partiallyResolved,
        SemanticPlanningState state);
}

/// <summary>
/// Builds only catalog-proven canonical fields. It never guesses an unresolved key and
/// leaves final canonical/Query Builder validation to ISqlProductionService.
/// </summary>
public sealed class SemanticCanonicalRequestAssembler(
    SemanticCatalogRegistry registry,
    ISemanticSlotIntentDetector? intentDetector = null,
    ISemanticCompletenessGate? completenessGate = null,
    ISemanticSourceResolver? sourceResolver = null) : ISemanticCanonicalRequestAssembler
{
    private readonly ISemanticSlotIntentDetector effectiveIntentDetector =
        intentDetector ?? new SemanticSlotIntentDetector();
    private readonly ISemanticCompletenessGate effectiveCompletenessGate =
        completenessGate ?? new SemanticCompletenessGate();
    private readonly ISemanticSourceResolver effectiveSourceResolver =
        sourceResolver ?? new CatalogSemanticSourceResolver(registry);

    public SemanticCanonicalAssemblyResult Assemble(
        string requestId,
        string conversationId,
        string prompt,
        DataSource? requestedSource,
        CanonicalRequest? partiallyResolved,
        SemanticResolverResult semantic)
    {
        var timer = Stopwatch.StartNew();
        var ambiguous = new List<SemanticSlotGap>();
        var unsupported = new List<UnresolvedConceptKind>();
        var missing = new List<UnresolvedConceptKind>();
        var candidates = new List<SemanticSlotGap>();
        var intent = semantic.SlotIntent
            ?? effectiveIntentDetector.Detect(prompt, partiallyResolved);

        var deterministicMetric = partiallyResolved?.Metrics.Count == 1
            && semantic.Metric is
            {
                Kind: SemanticResolutionKind.Resolved,
                CandidateKey: not null
            }
            && semantic.Metric.CandidateKey.Equals(
                partiallyResolved.Metrics[0], StringComparison.Ordinal)
                ? partiallyResolved.Metrics[0] : null;
        var deterministicDimensions = partiallyResolved?.Dimensions.Count > 0
            && semantic.Dimension is
            {
                Kind: SemanticResolutionKind.Resolved,
                CandidateKey: not null
            }
            && partiallyResolved.Dimensions.Contains(
                semantic.Dimension.CandidateKey, StringComparer.Ordinal)
                ? partiallyResolved.Dimensions : null;
        if (deterministicMetric is null)
        {
            Classify(semantic.Metric, intent.MetricRequested,
                UnresolvedConceptKind.Metric, ambiguous, unsupported, missing,
                candidates);
        }
        if (deterministicDimensions is null)
        {
            Classify(semantic.Dimension,
                intent.DimensionRequested || intent.GroupingRequested,
                UnresolvedConceptKind.Dimension, ambiguous, unsupported, missing,
                candidates);
        }

        var metric = deterministicMetric
            ?? (intent.MetricRequested
                && semantic.Metric?.Kind == SemanticResolutionKind.Resolved
                ? semantic.Metric.CandidateKey : null);
        var dimensions = deterministicDimensions
            ?? (intent.DimensionRequested
                && semantic.Dimension?.Kind == SemanticResolutionKind.Resolved
                && semantic.Dimension.CandidateKey is not null
                    ? new[] { semantic.Dimension.CandidateKey }
                    : Array.Empty<string>());
        var filterLiteral = semantic.FilterLiteral;
        var filters = partiallyResolved?.Filters ?? [];
        if (filters.Count == 0 && filterLiteral?.ToRequestFilter() is { } resolvedFilter)
        {
            filters = [resolvedFilter];
        }
        if (filterLiteral is { ResolutionKind: FilterLiteralResolutionKind.Resolved,
                DimensionCandidate: not null }
            && intent.FilterRequested && !intent.GroupingRequested)
        {
            dimensions = dimensions.Where(dimension => !dimension.Equals(
                    filterLiteral.DimensionCandidate, StringComparison.Ordinal))
                .ToArray();
        }
        if (filters.Count == 0 && intent.FilterRequested)
        {
            switch (filterLiteral?.ResolutionKind)
            {
                case FilterLiteralResolutionKind.Ambiguous:
                    ambiguous.Add(new SemanticSlotGap(UnresolvedConceptKind.Filter,
                        filterLiteral.DimensionCandidateKeys));
                    candidates.Add(new SemanticSlotGap(UnresolvedConceptKind.Filter,
                        filterLiteral.DimensionCandidateKeys));
                    break;
                case FilterLiteralResolutionKind.Unsupported:
                    unsupported.Add(UnresolvedConceptKind.Filter);
                    break;
                default:
                    missing.Add(UnresolvedConceptKind.Filter);
                    if (filterLiteral?.DimensionCandidateKeys.Count > 0)
                    {
                        candidates.Add(new SemanticSlotGap(UnresolvedConceptKind.Filter,
                            filterLiteral.DimensionCandidateKeys));
                    }
                    break;
            }
        }

        var sourceDecision = effectiveSourceResolver.Resolve(
            requestedSource, metric, dimensions, filters);
        var source = sourceDecision.Source;
        if (sourceDecision.Kind == SemanticSourceDecisionKind.Ambiguous)
        {
            ambiguous.Add(new SemanticSlotGap(UnresolvedConceptKind.Source,
                sourceDecision.CandidateSources.Select(item => item.ToString()).ToArray()));
        }
        else if (sourceDecision.Kind == SemanticSourceDecisionKind.Incompatible)
        {
            unsupported.Add(UnresolvedConceptKind.Source);
        }

        var date = semantic.ResolvedDate ?? partiallyResolved?.DateRange;
        if (date is null && metric is not null && source is not null)
        {
            var definition = registry.GetRequired(source.Value).Catalog.FindMetric(metric);
            if (definition is not null && !definition.RequiresDateRange
                && !intent.DateRequested)
            {
                date = DateRangeSpec.NotApplicable;
            }
            else
            {
                missing.Add(UnresolvedConceptKind.Date);
            }
        }
        else if (date is not null && metric is not null && source is not null
            && date.Kind != DateRangeKind.NotApplicable
            && registry.GetRequired(source.Value).Catalog.FindMetric(metric) is
                { RequiresDateRange: false })
        {
            unsupported.Add(UnresolvedConceptKind.Date);
        }

        var scores = new[]
        {
            semantic.Metric is { Kind: SemanticResolutionKind.Resolved }
                ? semantic.Metric.Similarity : 1,
            semantic.Dimension is { Kind: SemanticResolutionKind.Resolved }
                ? semantic.Dimension.Similarity : 1
        };
        var distinctAmbiguous = DistinctGaps(ambiguous);
        var distinctUnsupported = unsupported.Distinct().ToArray();
        var distinctMissing = missing.Distinct().ToArray();
        var completeness = effectiveCompletenessGate.Evaluate(
            intent,
            metric,
            dimensions,
            date,
            filters,
            source,
            distinctAmbiguous,
            distinctUnsupported,
            distinctMissing,
            partiallyResolved,
            filterLiteral);
        var state = new SemanticPlanningState(
            metric,
            dimensions,
            date,
            filters,
            source,
            distinctAmbiguous,
            distinctUnsupported,
            distinctMissing,
            Math.Clamp(scores.Min(), 0, 1),
            intent,
            completeness,
            DistinctGaps(candidates),
            filterLiteral,
            sourceDecision);
        timer.Stop();
        return AssembleStateCore(requestId, conversationId, prompt,
            partiallyResolved, state, timer.ElapsedMilliseconds);
    }

    public SemanticCanonicalAssemblyResult AssembleState(
        string requestId,
        string conversationId,
        string prompt,
        CanonicalRequest? partiallyResolved,
        SemanticPlanningState state)
    {
        var timer = Stopwatch.StartNew();
        return AssembleStateCore(requestId, conversationId, prompt,
            partiallyResolved, state, timer.ElapsedMilliseconds);
    }

    private SemanticCanonicalAssemblyResult AssembleStateCore(
        string requestId,
        string conversationId,
        string prompt,
        CanonicalRequest? partiallyResolved,
        SemanticPlanningState state,
        long elapsed)
    {
        if (state.UnsupportedSlots.Count > 0)
        {
            return Result(SemanticCanonicalAssemblyOutcome.Unsupported, state, elapsed);
        }
        if (state.AmbiguousSlots.Count > 0)
        {
            return Result(SemanticCanonicalAssemblyOutcome.Ambiguous, state, elapsed);
        }
        if (state.MissingSlots.Count > 0)
        {
            return Result(SemanticCanonicalAssemblyOutcome.Missing, state, elapsed);
        }
        if (!state.Completeness.IsComplete
            || !state.IsFullyResolved
            || !IsCompatible(state))
        {
            return Result(SemanticCanonicalAssemblyOutcome.Invalid, state, elapsed,
                "CatalogCompatibility");
        }

        var canonical = new CanonicalRequest
        {
            RequestId = requestId,
            ConversationId = conversationId,
            PreviousRequestId = partiallyResolved?.PreviousRequestId,
            Source = state.ResolvedSource,
            Intent = ResolveIntent(prompt, state.ResolvedDimensions.Count,
                partiallyResolved?.Intent, state.SlotIntent),
            Metrics = [state.ResolvedMetric!],
            Dimensions = state.ResolvedDimensions,
            Filters = state.ResolvedFilters,
            DateRange = state.ResolvedDate!,
            Grain = partiallyResolved?.Grain ?? TimeGrain.None,
            Limit = partiallyResolved?.Limit,
            OrderBy = partiallyResolved?.OrderBy,
            OrderDirection = partiallyResolved?.OrderDirection ?? SortDirection.Asc,
            ScenarioKey = null,
            Confidence = state.Confidence,
            UnresolvedTerms = []
        };
        return new SemanticCanonicalAssemblyResult(
            SemanticCanonicalAssemblyOutcome.Assembled,
            state,
            canonical,
            elapsed);
    }

    private bool IsCompatible(SemanticPlanningState state)
    {
        var catalog = registry.GetRequired(state.ResolvedSource!.Value).Catalog;
        return catalog.FindMetric(state.ResolvedMetric!)?.IsUsable == true
            && state.ResolvedDimensions.All(dimension =>
                catalog.IsMetricDimensionCompatible(state.ResolvedMetric!, dimension))
            && state.ResolvedFilters.All(filter =>
                catalog.IsMetricFilterCompatible(state.ResolvedMetric!, filter.Field));
    }

    private static void Classify(
        SemanticResolutionResult? result,
        bool requested,
        UnresolvedConceptKind kind,
        List<SemanticSlotGap> ambiguous,
        List<UnresolvedConceptKind> unsupported,
        List<UnresolvedConceptKind> missing,
        List<SemanticSlotGap> candidates)
    {
        if (!requested || result is null || result.Kind == SemanticResolutionKind.Missing)
        {
            if (requested)
            {
                missing.Add(kind);
                if (result?.CandidateKeys.Count > 0)
                {
                    candidates.Add(new SemanticSlotGap(kind, result.CandidateKeys));
                }
            }
            return;
        }

        switch (result.Kind)
        {
            case SemanticResolutionKind.Ambiguous:
                ambiguous.Add(new SemanticSlotGap(kind, result.CandidateKeys));
                candidates.Add(new SemanticSlotGap(kind, result.CandidateKeys));
                break;
            case SemanticResolutionKind.Unsupported:
                unsupported.Add(kind);
                break;
        }
    }

    private static RequestIntent ResolveIntent(
        string prompt,
        int dimensionCount,
        RequestIntent? existing,
        SemanticSlotIntent slotIntent)
    {
        if (dimensionCount == 0)
        {
            return RequestIntent.SingleValue;
        }

        var normalized = TurkishTextNormalizer.Normalize(prompt);
        if (slotIntent.ComparisonRequested
            || normalized.Contains("kiyas", StringComparison.Ordinal)
            || normalized.Contains("karsilast", StringComparison.Ordinal))
        {
            return RequestIntent.Compare;
        }
        if (existing == RequestIntent.Trend
            || normalized.Contains("trend", StringComparison.Ordinal)
            || normalized.Contains("seyir", StringComparison.Ordinal))
        {
            return RequestIntent.Trend;
        }
        return dimensionCount > 0 ? RequestIntent.Breakdown : RequestIntent.SingleValue;
    }

    private static IReadOnlyList<SemanticSlotGap> DistinctGaps(
        IEnumerable<SemanticSlotGap> gaps) => gaps
        .GroupBy(gap => gap.Kind)
        .Select(group => new SemanticSlotGap(group.Key,
            group.SelectMany(gap => gap.CandidateKeys)
                .Distinct(StringComparer.Ordinal).ToArray()))
        .ToArray();

    private static SemanticCanonicalAssemblyResult Result(
        SemanticCanonicalAssemblyOutcome outcome,
        SemanticPlanningState state,
        long elapsed,
        string? failure = null) =>
        new(outcome, state, null, elapsed, failure);
}
