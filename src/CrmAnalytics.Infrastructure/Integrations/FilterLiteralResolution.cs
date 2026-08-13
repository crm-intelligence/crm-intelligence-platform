using System.Globalization;
using Crm.Analytics.Sql.Catalog;
using Crm.Analytics.Sql.Contracts;
using Crm.Analytics.Sql.Nlu;

namespace CrmAnalytics.Infrastructure.Integrations;

public enum FilterLiteralResolutionKind
{
    Resolved,
    Ambiguous,
    Unsupported,
    Unbound
}

public enum FilterLiteralEvidenceKind
{
    QuotedLiteral,
    UppercaseCode,
    NamedLiteral,
    Conjunction,
    List,
    NearbyDimensionSemantics,
    CatalogCompatibility,
    ResolverCandidate
}

/// <summary>
/// An in-memory reference to a literal. Raw values are needed by the parameterized
/// canonical contract, but are deliberately excluded from diagnostics and ToString().
/// </summary>
public sealed record SafeFilterLiteralReference(
    int TokenIndex,
    int TokenCount,
    FilterLiteral Literal)
{
    public override string ToString() =>
        $"FilterLiteralReference {{ TokenIndex = {TokenIndex}, TokenCount = {TokenCount}, Redacted }}";
}

public sealed record FilterLiteralResolution(
    string? DimensionCandidate,
    int LiteralCount,
    FilterLiteralResolutionKind ResolutionKind,
    double Confidence,
    IReadOnlyList<FilterLiteralEvidenceKind> EvidenceKinds,
    IReadOnlyList<string> DimensionCandidateKeys,
    IReadOnlyList<SafeFilterLiteralReference> SafeLiteralReferences)
{
    public RequestFilter? ToRequestFilter()
    {
        if (ResolutionKind != FilterLiteralResolutionKind.Resolved
            || DimensionCandidate is null || SafeLiteralReferences.Count == 0)
        {
            return null;
        }

        return new RequestFilter
        {
            Field = DimensionCandidate,
            Op = SafeLiteralReferences.Count == 1
                ? FilterOperator.Eq : FilterOperator.In,
            Values = SafeLiteralReferences.Select(item => item.Literal).ToArray()
        };
    }

    public override string ToString() =>
        $"FilterLiteralResolution {{ DimensionCandidate = {DimensionCandidate ?? "None"}, LiteralCount = {LiteralCount}, ResolutionKind = {ResolutionKind}, Confidence = {Confidence:0.000}, EvidenceKinds = {string.Join(',', EvidenceKinds)} }}";
}

public interface IFilterLiteralResolver
{
    FilterLiteralResolution Resolve(
        string prompt,
        DataSource? source,
        string? metric,
        SemanticResolutionResult? dimensionResolution,
        SemanticSlotIntent intent);
}

/// <summary>
/// Binds value-like spans to filterable semantic dimensions. It has no business-value
/// dictionary: values are detected structurally and dimensions come only from catalog
/// metadata and compatibility contracts.
/// </summary>
public sealed class FilterLiteralResolver(SemanticCatalogRegistry registry)
    : IFilterLiteralResolver
{
    private static readonly HashSet<string> StructuralWords = new(StringComparer.Ordinal)
    {
        "ve", "veya", "ile", "vs", "versus", "gore", "bazinda", "icin",
        "olan", "bu", "son", "gecen", "onceki", "kiyasla", "karsilastir",
        "goster", "getir", "ver", "hesapla", "raporla", "filtrele",
        "bugun", "dun", "yarin", "gun", "hafta", "ay", "yil", "sene",
        "ceyrek", "donem", "tarih"
    };

    public FilterLiteralResolution Resolve(
        string prompt,
        DataSource? source,
        string? metric,
        SemanticResolutionResult? dimensionResolution,
        SemanticSlotIntent intent)
    {
        ArgumentNullException.ThrowIfNull(prompt);
        var tokens = TurkishTextNormalizer.Tokenize(prompt);
        if (tokens.Count == 0)
        {
            return Empty(FilterLiteralResolutionKind.Unbound);
        }

        var catalogs = registry.Sources
            .Where(item => source is null || item.Key == source)
            .Select(item => item.Value.Catalog)
            .ToArray();
        var dimensionMetadata = catalogs
            .SelectMany(catalog => catalog.Dimensions.Select(item =>
                new DimensionMetadata(item.Key, item.Value, catalog)))
            .Where(item => item.Definition.Filterable)
            .GroupBy(item => item.Key, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToArray();
        var semanticTokens = BuildSemanticTokenSet(catalogs);
        var dimensionMatches = dimensionMetadata
            .Select(item => new DimensionMatch(item,
                BestPhraseMatch(tokens, MetadataPhrases(item))))
            .Where(item => item.Match is not null)
            .ToArray();

        var literalReferences = DetectLiterals(prompt, tokens, semanticTokens,
            dimensionMatches.Select(item => item.Match!).ToArray(), intent);
        if (literalReferences.Count == 0)
        {
            return Empty(intent.FilterRequested
                ? FilterLiteralResolutionKind.Unbound
                : FilterLiteralResolutionKind.Unsupported);
        }

        var evidence = new HashSet<FilterLiteralEvidenceKind>();
        foreach (var literal in literalReferences)
        {
            evidence.Add(literal.Evidence);
        }
        if (literalReferences.Count > 1)
        {
            evidence.Add(prompt.Contains(',')
                ? FilterLiteralEvidenceKind.List
                : FilterLiteralEvidenceKind.Conjunction);
        }

        var resolverCandidates = dimensionResolution?.CandidateKeys ?? [];
        var incompatibleSemanticMatch = metric is not null
            && dimensionMatches.Any(item =>
                !item.Metadata.Catalog.IsMetricFilterCompatible(
                    metric, item.Metadata.Key));
        var ranked = dimensionMetadata.Select(item =>
        {
            var match = dimensionMatches.FirstOrDefault(candidate =>
                candidate.Metadata.Key == item.Key)?.Match;
            var proximity = match is null ? 0d
                : ProximityScore(match, literalReferences);
            var resolverEvidence = dimensionMatches.Length > 0
                && resolverCandidates.Contains(item.Key,
                    StringComparer.Ordinal) ? .25 : 0;
            var compatibility = metric is null
                || item.Catalog.IsMetricFilterCompatible(metric, item.Key);
            var semanticEvidence = proximity + resolverEvidence;
            var score = semanticEvidence > 0 && compatibility
                ? semanticEvidence + .15 : semanticEvidence;
            return new { item.Key, item.Definition, Compatibility = compatibility, Score = score };
        })
        .Where(item => item.Compatibility)
        .OrderByDescending(item => item.Score)
        .ThenBy(item => item.Key, StringComparer.Ordinal)
        .ToArray();

        if (ranked.Length == 0)
        {
            return Result(null, FilterLiteralResolutionKind.Unsupported, 0,
                [], literalReferences, evidence);
        }

        var candidates = ranked.Where(item => item.Score > 0)
            .Take(3).Select(item => item.Key).ToArray();
        if (candidates.Length == 0)
        {
            return Result(null, incompatibleSemanticMatch
                    ? FilterLiteralResolutionKind.Unsupported
                    : FilterLiteralResolutionKind.Unbound, 0,
                [], literalReferences, evidence);
        }

        if (dimensionMatches.Any(item => item.Metadata.Key == candidates[0]))
        {
            evidence.Add(FilterLiteralEvidenceKind.NearbyDimensionSemantics);
        }
        if (resolverCandidates.Contains(candidates[0], StringComparer.Ordinal))
        {
            evidence.Add(FilterLiteralEvidenceKind.ResolverCandidate);
        }
        evidence.Add(FilterLiteralEvidenceKind.CatalogCompatibility);

        var best = ranked[0];
        var secondScore = ranked.Length > 1 ? ranked[1].Score : 0;
        var confidence = Math.Clamp(best.Score, 0, 1);
        var ambiguous = candidates.Length > 1
            && best.Score - secondScore < .10;
        var kind = ambiguous
            ? FilterLiteralResolutionKind.Ambiguous
            : FilterLiteralResolutionKind.Resolved;
        var typed = literalReferences.Select(reference =>
            new SafeFilterLiteralReference(reference.Index, reference.Count,
                new FilterLiteral(MapKind(best.Definition.ValueType), reference.Raw)))
            .ToArray();
        if (typed.Any(reference => !IsGuardrailValid(best.Definition,
                reference.Literal)))
        {
            return Result(null, FilterLiteralResolutionKind.Unsupported, 0,
                candidates, [], evidence);
        }

        return new FilterLiteralResolution(
            kind == FilterLiteralResolutionKind.Resolved ? best.Key : null,
            typed.Length,
            kind,
            confidence,
            evidence.Order().ToArray(),
            candidates,
            typed);
    }

    private static IReadOnlyList<DetectedLiteral> DetectLiterals(
        string prompt,
        IReadOnlyList<PromptToken> tokens,
        IReadOnlySet<string> semanticTokens,
        IReadOnlyList<PhraseMatch> dimensionMatches,
        SemanticSlotIntent intent)
    {
        var results = new List<DetectedLiteral>();
        var quoted = false;
        for (var index = 0; index < tokens.Count; index++)
        {
            var raw = tokens[index].Raw.Trim('"', '\'');
            var startsQuote = index < tokens.Count
                && prompt.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                    .ElementAtOrDefault(index)?.FirstOrDefault() is '"' or '\'';
            var endsQuote = index < tokens.Count
                && prompt.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                    .ElementAtOrDefault(index)?.LastOrDefault() is '"' or '\'';
            quoted |= startsQuote;
            var upperCode = raw.Length is >= 2 and <= 16
                && raw.Any(char.IsLetter)
                && raw.Where(char.IsLetter).All(char.IsUpper);
            var named = raw.Length > 1 && char.IsUpper(raw[0])
                && raw.Skip(1).Any(char.IsLower);
            var nearDimension = dimensionMatches.Any(match =>
                Math.Abs(index - match.Start) <= 2);
            var inList = HasListNeighbor(tokens, index);
            var hasFilterNeighbor = index + 1 < tokens.Count
                && tokens[index + 1].Normalized == "icin"
                || index > 0 && tokens[index - 1].Normalized == "icin";
            var normalized = tokens[index].Normalized;
            var isSemanticConcept = semanticTokens.Any(concept =>
                MorphologicallyRelated(normalized, concept));
            var lexicalValue = normalized.Length >= 2
                && !IsStructuralWord(normalized)
                && !IsDateLike(normalized)
                && !normalized.All(char.IsDigit);
            var candidate = !isSemanticConcept
                && !IsStructuralWord(normalized)
                && !IsDateLike(normalized)
                && !normalized.All(char.IsDigit)
                && (quoted || upperCode || named
                    && (inList || hasFilterNeighbor || nearDimension
                        && (!intent.GroupingRequested
                            || intent.FilterRequested))
                    || lexicalValue && (inList
                        || nearDimension && intent.FilterRequested));
            if (candidate)
            {
                results.Add(new DetectedLiteral(index, 1, raw,
                    quoted ? FilterLiteralEvidenceKind.QuotedLiteral
                        : upperCode ? FilterLiteralEvidenceKind.UppercaseCode
                        : FilterLiteralEvidenceKind.NamedLiteral));
            }
            if (endsQuote)
            {
                quoted = false;
            }
        }

        return results.GroupBy(item => item.Index)
            .Select(group => group.First()).ToArray();
    }

    private static bool HasListNeighbor(IReadOnlyList<PromptToken> tokens, int index) =>
        index > 1 && tokens[index - 1].Normalized is "ve" or "ile" or "veya"
        || index + 2 < tokens.Count
            && tokens[index + 1].Normalized is "ve" or "ile" or "veya";

    private static bool IsStructuralWord(string value) =>
        StructuralWords.Contains(value)
        || value.StartsWith("filtre", StringComparison.Ordinal)
        || value.StartsWith("kirilim", StringComparison.Ordinal)
        || value.StartsWith("karsilast", StringComparison.Ordinal)
        || value.StartsWith("kiyas", StringComparison.Ordinal)
        || value.StartsWith("hesap", StringComparison.Ordinal)
        || value.StartsWith("goster", StringComparison.Ordinal);

    private static bool IsDateLike(string value) =>
        DateOnly.TryParseExact(value, ["yyyy-MM-dd", "yyyy/MM/dd", "dd.MM.yyyy"],
            CultureInfo.InvariantCulture, DateTimeStyles.None, out _);

    private static double ProximityScore(
        PhraseMatch match,
        IReadOnlyList<DetectedLiteral> literals)
    {
        var distance = literals.Min(item => Math.Abs(item.Index - match.Start));
        var lexical = Math.Min(.55, .25 + match.EvidenceCount * .10);
        return distance <= 2 ? lexical : distance <= 6 ? lexical * .7 : 0;
    }

    private static PhraseMatch? BestPhraseMatch(
        IReadOnlyList<PromptToken> prompt,
        IEnumerable<IReadOnlyList<string>> phrases)
    {
        PhraseMatch? best = null;
        foreach (var phrase in phrases)
        {
            for (var start = 0; start + phrase.Count <= prompt.Count; start++)
            {
                var evidence = 0;
                for (var offset = 0; offset < phrase.Count; offset++)
                {
                    if (MorphologicallyRelated(prompt[start + offset].Normalized,
                            phrase[offset]))
                    {
                        evidence++;
                    }
                }
                if (evidence == phrase.Count && (best is null
                    || evidence > best.EvidenceCount))
                {
                    best = new PhraseMatch(start, phrase.Count, evidence);
                }
            }
        }
        return best;
    }

    private static IEnumerable<IReadOnlyList<string>> MetadataPhrases(
        DimensionMetadata metadata) => metadata.Definition.Aliases
        .Concat([metadata.Key, metadata.Definition.Label,
            metadata.Definition.Description])
        .Select(value => TurkishTextNormalizer.Tokenize(value)
            .Select(token => token.Normalized).Where(token => token.Length > 0)
            .ToArray())
        .Where(value => value.Length > 0);

    private static IReadOnlySet<string> BuildSemanticTokenSet(
        IEnumerable<MetricCatalogDocument> catalogs) => catalogs
        .SelectMany(catalog => catalog.Metrics.SelectMany(item => item.Value.Aliases
                .Concat([item.Key, item.Value.Label, item.Value.Description]))
            .Concat(catalog.Dimensions.SelectMany(item => item.Value.Aliases
                .Concat([item.Key, item.Value.Label, item.Value.Description]))))
        .SelectMany(TurkishTextNormalizer.Tokenize)
        .Select(token => token.Normalized)
        .ToHashSet(StringComparer.Ordinal);

    private static bool MorphologicallyRelated(string left, string right)
    {
        if (left.Equals(right, StringComparison.Ordinal))
        {
            return true;
        }
        var shorter = Math.Min(left.Length, right.Length);
        if (shorter < 4)
        {
            return false;
        }
        var common = 0;
        while (common < shorter && left[common] == right[common])
        {
            common++;
        }
        return common >= 4 && common >= Math.Ceiling(shorter * .7);
    }

    private static FilterValueKind MapKind(string valueType) => valueType switch
    {
        "integer" => FilterValueKind.Integer,
        "decimal" => FilterValueKind.Decimal,
        "boolean" => FilterValueKind.Boolean,
        "date" => FilterValueKind.Date,
        _ => FilterValueKind.Text
    };

    private static bool IsGuardrailValid(
        DimensionDefinition dimension,
        FilterLiteral literal)
    {
        if (string.IsNullOrWhiteSpace(literal.Raw) || literal.Raw.Length > 512
            || literal.Raw.Any(char.IsControl))
        {
            return false;
        }
        return dimension.ValueType switch
        {
            "text" => true,
            "integer" => int.TryParse(literal.Raw, NumberStyles.Integer,
                CultureInfo.InvariantCulture, out _),
            "decimal" => decimal.TryParse(literal.Raw, NumberStyles.Number,
                CultureInfo.InvariantCulture, out _),
            "boolean" => bool.TryParse(literal.Raw, out _),
            "date" => DateOnly.TryParseExact(literal.Raw, "yyyy-MM-dd",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out _),
            _ => false
        };
    }

    private static FilterLiteralResolution Empty(FilterLiteralResolutionKind kind) =>
        new(null, 0, kind, 0, [], [], []);

    private static FilterLiteralResolution Result(
        string? dimension,
        FilterLiteralResolutionKind kind,
        double confidence,
        IReadOnlyList<string> candidates,
        IReadOnlyList<DetectedLiteral> literals,
        IEnumerable<FilterLiteralEvidenceKind> evidence) =>
        new(dimension, literals.Count, kind, confidence,
            evidence.Distinct().Order().ToArray(), candidates, []);

    private sealed record DimensionMetadata(
        string Key,
        DimensionDefinition Definition,
        MetricCatalogDocument Catalog);

    private sealed record PhraseMatch(int Start, int Count, int EvidenceCount);
    private sealed record DimensionMatch(DimensionMetadata Metadata, PhraseMatch? Match);
    private sealed record DetectedLiteral(
        int Index,
        int Count,
        string Raw,
        FilterLiteralEvidenceKind Evidence);
}
