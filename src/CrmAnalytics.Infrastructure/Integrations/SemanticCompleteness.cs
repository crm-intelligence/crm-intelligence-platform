using System.Diagnostics;
using Crm.Analytics.Sql.Contracts;
using Crm.Analytics.Sql.Nlu;

namespace CrmAnalytics.Infrastructure.Integrations;

public sealed record SemanticSlotIntent(
    bool MetricRequested,
    bool GroupingRequested,
    bool DimensionRequested,
    bool DateRequested,
    bool FilterRequested,
    bool ComparisonRequested,
    bool RankingRequested,
    bool SourceExplicitlyRequested);

public interface ISemanticSlotIntentDetector
{
    SemanticSlotIntent Detect(
        string prompt,
        CanonicalRequest? partiallyResolved = null,
        DataSource? explicitlyRequestedSource = null);
}

/// <summary>
/// Detects requested semantic slot types only. It deliberately has no catalog and cannot
/// select a metric, dimension, source, or literal value.
/// </summary>
public sealed class SemanticSlotIntentDetector : ISemanticSlotIntentDetector
{
    private static readonly HashSet<string> TemporalSuffixes = new(StringComparer.Ordinal)
    {
        "", "a", "e", "i", "u", "in", "un", "da", "de", "dan", "den",
        "ki", "lik", "inda", "inde", "sinda", "sinde", "lar", "ler",
        "lari", "leri", "larda", "lerde"
    };

    public SemanticSlotIntent Detect(
        string prompt,
        CanonicalRequest? partiallyResolved = null,
        DataSource? explicitlyRequestedSource = null)
    {
        ArgumentNullException.ThrowIfNull(prompt);
        var tokens = TurkishTextNormalizer.Tokenize(prompt);
        var words = tokens.Select(token => token.Normalized).ToArray();
        var comparison = words.Any(IsComparisonCue)
            || ContainsPhrase(words, "birbirine", "gore");
        var grouping = comparison || words.Any(IsGroupingCue)
            || words.GroupBy(word => word, StringComparer.Ordinal)
                .Any(group => group.Key.Length > 3 && group.Count() > 1);
        var ranking = words.Any(IsRankingCue)
            || ContainsPhrase(words, "en", "cok")
            || ContainsPhrase(words, "en", "az");
        var date = RelativeDateResolver.Resolve(tokens, new DateOnly(2000, 6, 15)) is not null
            || words.Any(IsUnresolvedDateCue);
        var filter = (partiallyResolved?.Filters.Count ?? 0) > 0
            || HasQuotedValue(prompt)
            || HasUppercaseLiteral(tokens)
            || words.Any(word => word.StartsWith("filtre", StringComparison.Ordinal))
            || comparison && words.Any(word => word is "ve" or "ile" or "versus" or "vs");
        var dimension = grouping;
        var source = explicitlyRequestedSource is not null
            || words.Any(word => word.StartsWith("kaynak", StringComparison.Ordinal))
            || ContainsPhrase(words, "veri", "kaynagi");

        // Every non-empty analytics request needs a metric. This is a slot requirement,
        // not a default metric selection.
        var metric = words.Length > 0;
        return new SemanticSlotIntent(
            metric,
            grouping,
            dimension,
            date,
            filter,
            comparison,
            ranking,
            source);
    }

    private static bool IsGroupingCue(string word) =>
        word is "gore" or "by"
        || StartsWithAny(word, "baz", "kirilim", "dagil", "ayril");

    private static bool IsComparisonCue(string word) =>
        StartsWithAny(word, "karsilast", "kiyas", "mukayese")
        || word is "versus" or "vs";

    private static bool IsRankingCue(string word) =>
        StartsWithAny(word, "sirala", "siralam")
        || word is "top" or "ilk";

    private static bool IsUnresolvedDateCue(string word) =>
        IsTemporalStem(word, "bugun")
        || IsTemporalStem(word, "dun")
        || IsTemporalStem(word, "yarin")
        || IsTemporalStem(word, "hafta")
        || IsTemporalStem(word, "ay")
        || IsTemporalStem(word, "yil")
        || IsTemporalStem(word, "sene")
        || IsTemporalStem(word, "donem")
        || IsTemporalStem(word, "ceyrek")
        || IsTemporalStem(word, "tarih")
        || IsTemporalStem(word, "gun");

    private static bool IsTemporalStem(string word, string stem) =>
        word.StartsWith(stem, StringComparison.Ordinal)
        && TemporalSuffixes.Contains(word[stem.Length..]);

    private static bool IsPluralGroupingForm(string word) => word.Length > 5
        && (word.EndsWith("lar", StringComparison.Ordinal)
            || word.EndsWith("ler", StringComparison.Ordinal)
            || word.EndsWith("lari", StringComparison.Ordinal)
            || word.EndsWith("leri", StringComparison.Ordinal)
            || word.EndsWith("larin", StringComparison.Ordinal)
            || word.EndsWith("lerin", StringComparison.Ordinal)
            || word.EndsWith("larinda", StringComparison.Ordinal)
            || word.EndsWith("lerinde", StringComparison.Ordinal));

    private static bool StartsWithAny(string value, params string[] stems) =>
        stems.Any(stem => value.StartsWith(stem, StringComparison.Ordinal));

    private static bool ContainsPhrase(
        IReadOnlyList<string> words,
        string first,
        string second)
    {
        for (var index = 0; index + 1 < words.Count; index++)
        {
            if (words[index] == first && words[index + 1] == second)
            {
                return true;
            }
        }
        return false;
    }

    private static bool HasQuotedValue(string prompt) =>
        prompt.Count(character => character is '\'' or '"') >= 2;

    private static bool HasUppercaseLiteral(IReadOnlyList<PromptToken> tokens) =>
        tokens.Any(token => token.Raw.Length is >= 2 and <= 8
            && token.Raw.Any(char.IsLetter)
            && token.Raw.Where(char.IsLetter).All(char.IsUpper));
}

public enum SemanticCompletenessSlot
{
    Metric,
    Dimension,
    Date,
    Filter,
    Comparison,
    Ranking,
    Source
}

public enum SemanticCompletenessReasonCode
{
    Complete,
    MissingMetric,
    MissingDimension,
    MissingDate,
    MissingFilter,
    MissingComparisonContext,
    MissingRankingContext,
    MissingSource,
    AmbiguousRequiredSlot,
    UnsupportedRequiredSlot,
    SourceIncompatible
}

public sealed record SemanticCompletenessResult(
    bool IsComplete,
    IReadOnlyList<SemanticCompletenessSlot> RequiredSlots,
    IReadOnlyList<SemanticCompletenessSlot> ResolvedSlots,
    IReadOnlyList<SemanticCompletenessSlot> MissingSlots,
    IReadOnlyList<SemanticCompletenessSlot> AmbiguousSlots,
    IReadOnlyList<SemanticCompletenessSlot> UnsupportedSlots,
    IReadOnlyList<SemanticCompletenessReasonCode> ReasonCodes,
    long DurationMilliseconds);

public interface ISemanticCompletenessGate
{
    SemanticCompletenessResult Evaluate(
        SemanticSlotIntent intent,
        string? metric,
        IReadOnlyList<string> dimensions,
        DateRangeSpec? date,
        IReadOnlyList<RequestFilter> filters,
        DataSource? source,
        IReadOnlyList<SemanticSlotGap> ambiguous,
        IReadOnlyList<UnresolvedConceptKind> unsupported,
        IReadOnlyList<UnresolvedConceptKind> missing,
        CanonicalRequest? partiallyResolved,
        FilterLiteralResolution? filterLiteral = null);
}

/// <summary>Pure in-memory fail-closed gate executed before canonical assembly.</summary>
public sealed class SemanticCompletenessGate : ISemanticCompletenessGate
{
    public SemanticCompletenessResult Evaluate(
        SemanticSlotIntent intent,
        string? metric,
        IReadOnlyList<string> dimensions,
        DateRangeSpec? date,
        IReadOnlyList<RequestFilter> filters,
        DataSource? source,
        IReadOnlyList<SemanticSlotGap> ambiguous,
        IReadOnlyList<UnresolvedConceptKind> unsupported,
        IReadOnlyList<UnresolvedConceptKind> missing,
        CanonicalRequest? partiallyResolved,
        FilterLiteralResolution? filterLiteral = null)
    {
        var timer = Stopwatch.StartNew();
        var required = new HashSet<SemanticCompletenessSlot>();
        var resolved = new HashSet<SemanticCompletenessSlot>();
        var missingSlots = new HashSet<SemanticCompletenessSlot>();
        var ambiguousSlots = new HashSet<SemanticCompletenessSlot>();
        var unsupportedSlots = new HashSet<SemanticCompletenessSlot>();
        var reasons = new HashSet<SemanticCompletenessReasonCode>();

        Require(intent.MetricRequested, SemanticCompletenessSlot.Metric,
            metric is not null, SemanticCompletenessReasonCode.MissingMetric);
        Require(intent.DimensionRequested || intent.GroupingRequested,
            SemanticCompletenessSlot.Dimension,
            dimensions.Count > 0,
            SemanticCompletenessReasonCode.MissingDimension);
        Require(intent.DateRequested,
            SemanticCompletenessSlot.Date,
            date is not null && date.Kind != DateRangeKind.NotApplicable,
            SemanticCompletenessReasonCode.MissingDate);
        Require(intent.FilterRequested,
            SemanticCompletenessSlot.Filter,
            filters.Count > 0
                && filters.All(filter => !string.IsNullOrWhiteSpace(filter.Field)
                    && filter.Values.Count > 0)
                && (filterLiteral is null
                    || filterLiteral.ResolutionKind
                        == FilterLiteralResolutionKind.Resolved),
            SemanticCompletenessReasonCode.MissingFilter);
        Require(intent.ComparisonRequested,
            SemanticCompletenessSlot.Comparison,
            (dimensions.Count > 0 || filters.Count > 0)
                && !(filterLiteral is { LiteralCount: >= 2 }
                    && filterLiteral.ResolutionKind
                        != FilterLiteralResolutionKind.Resolved),
            SemanticCompletenessReasonCode.MissingComparisonContext);
        Require(intent.RankingRequested,
            SemanticCompletenessSlot.Ranking,
            partiallyResolved?.Limit is not null
                && (partiallyResolved.OrderBy is not null || metric is not null),
            SemanticCompletenessReasonCode.MissingRankingContext);
        Require(true, SemanticCompletenessSlot.Source, source is not null,
            SemanticCompletenessReasonCode.MissingSource);

        foreach (var kind in ambiguous.Select(slot => slot.Kind))
        {
            var slot = Map(kind);
            ambiguousSlots.Add(slot);
            resolved.Remove(slot);
            reasons.Add(SemanticCompletenessReasonCode.AmbiguousRequiredSlot);
        }
        foreach (var kind in unsupported)
        {
            var slot = Map(kind);
            unsupportedSlots.Add(slot);
            resolved.Remove(slot);
            reasons.Add(SemanticCompletenessReasonCode.UnsupportedRequiredSlot);
        }
        foreach (var kind in missing)
        {
            var slot = Map(kind);
            missingSlots.Add(slot);
            resolved.Remove(slot);
        }

        var isComplete = missingSlots.Count == 0
            && ambiguousSlots.Count == 0
            && unsupportedSlots.Count == 0
            && required.All(resolved.Contains);
        if (isComplete)
        {
            reasons.Add(SemanticCompletenessReasonCode.Complete);
        }
        timer.Stop();
        return new SemanticCompletenessResult(
            isComplete,
            required.Order().ToArray(),
            resolved.Order().ToArray(),
            missingSlots.Order().ToArray(),
            ambiguousSlots.Order().ToArray(),
            unsupportedSlots.Order().ToArray(),
            reasons.Order().ToArray(),
            timer.ElapsedMilliseconds);

        void Require(
            bool requested,
            SemanticCompletenessSlot slot,
            bool available,
            SemanticCompletenessReasonCode reason)
        {
            if (!requested)
            {
                return;
            }
            required.Add(slot);
            if (available)
            {
                resolved.Add(slot);
            }
            else
            {
                missingSlots.Add(slot);
                reasons.Add(reason);
            }
        }
    }

    private static SemanticCompletenessSlot Map(UnresolvedConceptKind kind) => kind switch
    {
        UnresolvedConceptKind.Metric => SemanticCompletenessSlot.Metric,
        UnresolvedConceptKind.Dimension => SemanticCompletenessSlot.Dimension,
        UnresolvedConceptKind.Filter => SemanticCompletenessSlot.Filter,
        UnresolvedConceptKind.Date => SemanticCompletenessSlot.Date,
        UnresolvedConceptKind.Source => SemanticCompletenessSlot.Source,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
    };
}
