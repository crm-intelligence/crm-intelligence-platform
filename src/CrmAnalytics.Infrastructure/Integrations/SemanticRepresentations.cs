using Crm.Analytics.Sql.Contracts;
using Crm.Analytics.Sql.Nlu;

namespace CrmAnalytics.Infrastructure.Integrations;

public enum SemanticRepresentationType
{
    FullNormalized,
    DateMasked,
    FilterValueMasked,
    Clause,
    SemanticPhrase
}

/// <summary>
/// Request text used only in-memory for embedding. Text is intentionally absent from
/// resolver diagnostics and production telemetry.
/// </summary>
public sealed record SemanticRepresentation(
    SemanticRepresentationType Type,
    string Text);

public static class SemanticRepresentationFactory
{
    private const string DatePlaceholder = "semantic_date";
    private const string FilterPlaceholder = "semantic_filter_value";

    private static readonly HashSet<string> StopWords = new(StringComparer.Ordinal)
    {
        "acaba", "ama", "bana", "bir", "bu", "da", "de", "icin", "ile",
        "ise", "mi", "mu", "mı", "mü", "olarak", "olan", "ve", "veya",
        "goster", "getir", "ver", "hesapla", "ozetle", "raporla"
    };

    private static readonly HashSet<string> ClauseBoundaries = new(StringComparer.Ordinal)
    {
        "ve", "veya", "ile", "gore", "bazinda", "kiriliminda", "kirilimi"
    };

    private static readonly string[] FilterFieldCues =
    [
        "bolge", "eyalet", "sehir", "il", "kategori", "urun", "musteri",
        "magaza", "depo", "kanal", "tip", "tur"
    ];

    public static IReadOnlyList<SemanticRepresentation> Create(
        string prompt,
        DateRangeMatch? dateMatch,
        CanonicalRequest? partiallyResolved,
        int maximumPhraseTokens,
        int maximumRepresentations)
    {
        ArgumentNullException.ThrowIfNull(prompt);
        var tokens = TurkishTextNormalizer.Tokenize(prompt);
        if (tokens.Count == 0)
        {
            return [];
        }

        var normalized = tokens.Select(token => token.Normalized).ToArray();
        var representations = new List<SemanticRepresentation>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        Add(SemanticRepresentationType.FullNormalized, normalized);

        var dateMasked = (string[])normalized.Clone();
        if (dateMatch is not null)
        {
            MaskRange(dateMasked, dateMatch.TokenStart, dateMatch.TokenCount,
                DatePlaceholder);
            Add(SemanticRepresentationType.DateMasked, dateMasked);
        }

        var filterMasked = (string[])dateMasked.Clone();
        var filterMaskApplied = MaskKnownFilterValues(filterMasked, partiallyResolved)
            | MaskGenericFilterValues(filterMasked, tokens);
        if (filterMaskApplied)
        {
            Add(SemanticRepresentationType.FilterValueMasked, filterMasked);
        }

        foreach (var clause in SplitClauses(filterMasked))
        {
            if (IsMeaningful(clause))
            {
                Add(SemanticRepresentationType.Clause, clause);
            }
        }

        var phraseCandidates = new List<string[]>();
        for (var start = 0; start < filterMasked.Length; start++)
        {
            for (var length = 1;
                 length <= maximumPhraseTokens && start + length <= filterMasked.Length;
                 length++)
            {
                var phrase = filterMasked.Skip(start).Take(length).ToArray();
                if (IsMeaningful(phrase))
                {
                    phraseCandidates.Add(phrase);
                }
            }
        }

        // Retaining longer spans first prevents the cap from recreating the former
        // three-token bias on long business paraphrases.
        foreach (var phrase in phraseCandidates
            .OrderByDescending(candidate => candidate.Length)
            .ThenBy(candidate => string.Join(' ', candidate), StringComparer.Ordinal))
        {
            Add(SemanticRepresentationType.SemanticPhrase, phrase);
            if (representations.Count >= maximumRepresentations)
            {
                break;
            }
        }

        return representations;

        void Add(SemanticRepresentationType type, IEnumerable<string> words)
        {
            if (representations.Count >= maximumRepresentations)
            {
                return;
            }

            var text = string.Join(' ', words.Where(word => word.Length > 0));
            var identity = $"{type}:{text}";
            if (text.Length > 0 && seen.Add(identity))
            {
                representations.Add(new SemanticRepresentation(type, text));
            }
        }
    }

    private static void MaskRange(string[] tokens, int start, int count,
        string placeholder)
    {
        if (start < 0 || count <= 0 || start >= tokens.Length)
        {
            return;
        }

        tokens[start] = placeholder;
        for (var index = start + 1;
             index < tokens.Length && index < start + count;
             index++)
        {
            tokens[index] = string.Empty;
        }
    }

    private static bool MaskKnownFilterValues(
        string[] tokens,
        CanonicalRequest? partiallyResolved)
    {
        var applied = false;
        foreach (var literal in partiallyResolved?.Filters
            .SelectMany(filter => filter.Values) ?? [])
        {
            var literalTokens = TurkishTextNormalizer.Tokenize(literal.Raw)
                .Select(token => token.Normalized).ToArray();
            if (literalTokens.Length == 0)
            {
                continue;
            }

            for (var start = 0; start + literalTokens.Length <= tokens.Length; start++)
            {
                if (!tokens.Skip(start).Take(literalTokens.Length)
                    .SequenceEqual(literalTokens, StringComparer.Ordinal))
                {
                    continue;
                }

                MaskRange(tokens, start, literalTokens.Length, FilterPlaceholder);
                applied = true;
            }
        }

        return applied;
    }

    private static bool MaskGenericFilterValues(
        string[] normalized,
        IReadOnlyList<PromptToken> original)
    {
        var applied = false;
        for (var index = 0; index < original.Count; index++)
        {
            var raw = original[index].Raw;
            var isUpperCode = raw.Length is >= 2 and <= 8
                && raw.Any(char.IsLetter)
                && raw.Where(char.IsLetter).All(char.IsUpper);
            var isProperValue = raw.Length > 1 && char.IsUpper(raw[0])
                && index + 1 < original.Count
                && FilterFieldCues.Any(cue => original[index + 1].Normalized
                    .StartsWith(cue, StringComparison.Ordinal));
            if (!isUpperCode && !isProperValue)
            {
                continue;
            }

            normalized[index] = FilterPlaceholder;
            applied = true;
        }

        return applied;
    }

    private static IEnumerable<string[]> SplitClauses(string[] tokens)
    {
        var current = new List<string>();
        foreach (var token in tokens)
        {
            if (ClauseBoundaries.Contains(token))
            {
                if (current.Count > 0)
                {
                    yield return current.ToArray();
                    current.Clear();
                }
                continue;
            }

            if (token.Length > 0)
            {
                current.Add(token);
            }
        }

        if (current.Count > 0)
        {
            yield return current.ToArray();
        }
    }

    private static bool IsMeaningful(IReadOnlyCollection<string> tokens) =>
        tokens.Any(token => token.Length > 2
            && token is not DatePlaceholder and not FilterPlaceholder
            && !token.All(char.IsDigit)
            && !StopWords.Contains(token));
}
