using System.Diagnostics;
using System.Text.Json;
using Crm.Analytics.Sql.Catalog;
using Crm.Analytics.Sql.Contracts;
using Crm.Analytics.Sql.Nlu;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CrmAnalytics.Infrastructure.Integrations;

public enum SemanticResolutionKind
{
    Resolved,
    Ambiguous,
    Unsupported,
    Missing
}

public enum CandidateEvidenceKind
{
    Embedding,
    CatalogKey,
    BusinessName,
    ConceptualAlias,
    SemanticDescription,
    NormalizedPhrase,
    MorphologicalMatch,
    SourceCompatibility,
    SlotCompatibility,
    PhraseCoverage,
    OperationCompatibility,
    GroupingFilterContext
}

public sealed record CandidateEvidence(
    string SemanticKey,
    double EmbeddingScore,
    double LexicalEvidenceScore,
    bool CompatibilityValid,
    int FinalRank,
    IReadOnlyList<CandidateEvidenceKind> EvidenceKinds,
    double FinalScore = 0,
    double PhraseCoverage = 0,
    double DescriptionRelevance = 0);

public sealed record SemanticResolutionResult(
    SemanticResolutionKind Kind,
    SemanticSlotKind SlotKind,
    string? CandidateKey,
    double Similarity,
    double SecondBestSimilarity,
    double Margin,
    IReadOnlyList<string> CandidateKeys,
    SemanticRepresentationType SupportingRepresentationType =
        SemanticRepresentationType.FullNormalized,
    int LexicalEvidenceCount = 0,
    IReadOnlyList<string>? RetrievalCandidateKeys = null,
    IReadOnlyList<CandidateEvidence>? CandidateEvidence = null,
    SemanticCandidateDecision? Decision = null)
{
    public double BestSimilarity => Similarity;
}

public sealed record SemanticResolverResult(
    bool Available,
    SemanticResolutionResult? Metric,
    SemanticResolutionResult? Dimension,
    bool MetricRequested,
    bool DimensionRequested,
    long QueryEmbeddingMilliseconds,
    long SimilaritySearchMilliseconds,
    string? FailureKind = null,
    long PreprocessingMilliseconds = 0,
    DateRangeSpec? ResolvedDate = null,
    int RepresentationCount = 0,
    SemanticSlotIntent? SlotIntent = null,
    long IntentDetectionMilliseconds = 0,
    FilterLiteralResolution? FilterLiteral = null)
{
    public bool HasUnsupportedRequestedSlot =>
        MetricRequested && Metric?.Kind == SemanticResolutionKind.Unsupported
        || DimensionRequested && Dimension?.Kind == SemanticResolutionKind.Unsupported;
}

public interface ISemanticEmbeddingResolver
{
    Task<SemanticResolverResult> ResolveAsync(
        string prompt,
        DataSource? source,
        CanonicalRequest? partiallyResolved,
        CancellationToken cancellationToken);

    Task<SemanticResolverResult> ResolveAsync(
        string prompt,
        DataSource? source,
        CanonicalRequest? partiallyResolved,
        DateOnly today,
        CancellationToken cancellationToken) =>
        ResolveAsync(prompt, source, partiallyResolved, cancellationToken);
}

public sealed class SemanticEmbeddingResolver(
    ISemanticEmbeddingIndex index,
    ISemanticEmbeddingClient embeddingClient,
    IOptions<SemanticEmbeddingOptions> options,
    ISemanticSlotIntentDetector? intentDetector = null,
    SemanticCatalogRegistry? registry = null,
    IFilterLiteralResolver? filterLiteralResolver = null,
    ISemanticCandidateDiscriminator? candidateDiscriminator = null,
    ILogger<SemanticEmbeddingResolver>? logger = null) : ISemanticEmbeddingResolver
{
    private static readonly HashSet<string> LexicalStopWords = new(StringComparer.Ordinal)
    {
        "acaba", "ama", "bana", "bir", "bu", "da", "de", "icin", "ile",
        "ise", "mi", "mu", "olarak", "olan", "ve", "veya", "gore", "bazinda",
        "basina", "kiriliminda", "goster", "getir", "ver", "hesapla", "ozetle",
        "raporla", "son", "gecen", "onceki", "bugun", "dun", "hafta", "ay",
        "yil", "sene", "gun", "donem", "ceyrek", "toplam"
    };

    private readonly ISemanticSlotIntentDetector effectiveIntentDetector =
        intentDetector ?? new SemanticSlotIntentDetector();
    private readonly SemanticCatalogRegistry effectiveRegistry =
        registry ?? SemanticCatalogRegistry.CreateDefault();
    private readonly IFilterLiteralResolver effectiveFilterLiteralResolver =
        filterLiteralResolver ?? new FilterLiteralResolver(
            registry ?? SemanticCatalogRegistry.CreateDefault());
    private readonly ISemanticCandidateDiscriminator effectiveCandidateDiscriminator =
        candidateDiscriminator ?? new DeterministicSemanticCandidateDiscriminator();
    private readonly ILogger<SemanticEmbeddingResolver> effectiveLogger =
        logger ?? NullLogger<SemanticEmbeddingResolver>.Instance;

    public Task<SemanticResolverResult> ResolveAsync(
        string prompt,
        DataSource? source,
        CanonicalRequest? partiallyResolved,
        CancellationToken cancellationToken) =>
        ResolveAsync(prompt, source, partiallyResolved, new DateOnly(2000, 1, 1),
            cancellationToken);

    public async Task<SemanticResolverResult> ResolveAsync(
        string prompt,
        DataSource? source,
        CanonicalRequest? partiallyResolved,
        DateOnly today,
        CancellationToken cancellationToken)
    {
        try
        {
            var preprocessingTimer = Stopwatch.StartNew();
            var tokens = TurkishTextNormalizer.Tokenize(prompt);
            var dateMatch = RelativeDateResolver.Resolve(tokens, today);
            var intentTimer = Stopwatch.StartNew();
            var slotIntent = effectiveIntentDetector.Detect(
                prompt, partiallyResolved);
            intentTimer.Stop();
            var representations = SemanticRepresentationFactory.Create(
                prompt,
                dateMatch,
                partiallyResolved,
                options.Value.MaximumPhraseTokens,
                options.Value.MaximumRepresentations);
            preprocessingTimer.Stop();
            if (representations.Count == 0)
            {
                return Unavailable("NoSemanticRepresentations",
                    preprocessingTimer.ElapsedMilliseconds);
            }

            // Catalog construction is intentionally completed before per-request timing.
            var snapshot = await index.GetAsync(cancellationToken);
            var embedTimer = Stopwatch.StartNew();
            var queryVectors = await embeddingClient.EmbedAsync(
                representations.Select(item => item.Text).ToArray(), cancellationToken);
            embedTimer.Stop();
            ValidateQueryShape(queryVectors, representations.Count, snapshot.Entries);

            var searchTimer = Stopwatch.StartNew();
            var eligible = source is null
                ? snapshot.Entries
                : snapshot.Entries.Where(entry => entry.Document.Source == source).ToArray();
            var metric = Decide(SemanticSlotKind.Metric, eligible,
                prompt, representations, queryVectors, tokens, partiallyResolved,
                slotIntent, null, []);
            var dimension = Decide(SemanticSlotKind.Dimension, eligible,
                prompt, representations, queryVectors, tokens, partiallyResolved,
                slotIntent, metric.Kind == SemanticResolutionKind.Resolved
                    ? metric.CandidateKey : partiallyResolved?.Metrics.SingleOrDefault(),
                metric.CandidateKeys);
            if (metric.Kind == SemanticResolutionKind.Ambiguous
                && dimension.Kind == SemanticResolutionKind.Resolved
                && (slotIntent.DimensionRequested || slotIntent.GroupingRequested))
            {
                dimension = dimension with
                {
                    Kind = SemanticResolutionKind.Ambiguous,
                    CandidateKey = null
                };
            }
            var filterLiteral = effectiveFilterLiteralResolver.Resolve(
                prompt, source,
                metric.Kind == SemanticResolutionKind.Resolved
                    ? metric.CandidateKey : partiallyResolved?.Metrics.SingleOrDefault(),
                dimension,
                slotIntent);
            if (filterLiteral.LiteralCount > 0 && !slotIntent.FilterRequested)
            {
                slotIntent = slotIntent with { FilterRequested = true };
            }

            var missingMetric = HasExplicitMissingConceptCue(prompt);
            var metricRequested = slotIntent.MetricRequested;
            var dimensionRequested = slotIntent.DimensionRequested;
            if (!metricRequested || missingMetric)
            {
                metric = metric with
                {
                    Kind = SemanticResolutionKind.Missing,
                    CandidateKey = null,
                    CandidateKeys = missingMetric ? [] : metric.CandidateKeys
                };
            }
            if (!dimensionRequested)
            {
                dimension = dimension with
                {
                    Kind = SemanticResolutionKind.Missing,
                    CandidateKey = null
                };
            }
            searchTimer.Stop();

            return new SemanticResolverResult(
                true,
                metric,
                dimension,
                metricRequested,
                dimensionRequested,
                embedTimer.ElapsedMilliseconds,
                searchTimer.ElapsedMilliseconds,
                PreprocessingMilliseconds: preprocessingTimer.ElapsedMilliseconds,
                ResolvedDate: dateMatch?.Range,
                RepresentationCount: representations.Count,
                SlotIntent: slotIntent,
                IntentDetectionMilliseconds: intentTimer.ElapsedMilliseconds,
                FilterLiteral: filterLiteral);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Unavailable("Timeout");
        }
        catch (Exception exception) when (exception is HttpRequestException
            or JsonException or InvalidDataException or ArgumentException)
        {
            return Unavailable(exception.GetType().Name);
        }
    }

    private SemanticResolutionResult Decide(
        SemanticSlotKind slotKind,
        IEnumerable<SemanticEmbeddingIndexEntry> entries,
        string rawQuery,
        IReadOnlyList<SemanticRepresentation> representations,
        IReadOnlyList<float[]> queryVectors,
        IReadOnlyList<PromptToken> promptTokens,
        CanonicalRequest? partiallyResolved,
        SemanticSlotIntent intent,
        string? resolvedMetric,
        IReadOnlyList<string> metricCandidateKeys)
    {
        var queryConcepts = promptTokens.Select(token => token.Normalized)
            .Where(IsLexicalConcept)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var all = entries.Where(entry => entry.Document.SlotKind == slotKind)
            .Select(entry =>
            {
                var support = queryVectors.Select((query, position) => new
                    {
                        Score = Cosine(query, entry.Vector),
                        Type = representations[position].Type
                    })
                    .OrderByDescending(item => item.Score)
                    .ThenBy(item => item.Type)
                    .First();
                var contextualSupport = queryVectors.Select((query, position) => new
                    {
                        Score = Cosine(query, entry.Vector),
                        Type = representations[position].Type
                    })
                    .Where(item => item.Type !=
                        SemanticRepresentationType.SemanticPhrase)
                    .OrderByDescending(item => item.Score)
                    .ThenBy(item => item.Type)
                    .First();
                var metadata = ReadCandidateMetadata(entry.Document);
                var lexical = LexicalEvidence(queryConcepts, metadata);
                var phraseCoverage = PhraseCoverage(queryConcepts, metadata);
                var descriptionRelevance = DescriptionRelevance(
                    queryConcepts, metadata.BusinessDescription);
                var groupingFilterCompatible = IsCompatible(entry.Document, slotKind,
                    partiallyResolved, intent, resolvedMetric,
                    metricCandidateKeys);
                var operationCompatible = IsOperationCompatible(
                    entry.Document, slotKind, intent);
                return new CandidateScore(
                    entry.Document.Key,
                    entry.Document.Source,
                    support.Score,
                    contextualSupport.Score,
                    support.Type,
                    lexical.Count,
                    lexical.Score,
                    phraseCoverage,
                    descriptionRelevance,
                    lexical.Kinds,
                    metadata,
                    SourceCompatibility: true,
                    OperationCompatibility: operationCompatible,
                    SlotCompatibility: true,
                    GroupingFilterCompatibility: groupingFilterCompatible);
            })
            .GroupBy(item => item.Key, StringComparer.Ordinal)
            .Select(group => group.OrderByDescending(item => item.Compatibility)
                .ThenByDescending(item => item.Score + item.LexicalScore * .08)
                .ThenBy(item => item.Type).First())
            .ToArray();
        var retrievalPoolSize = Math.Max(options.Value.CandidateCount,
            options.Value.RetrievalPoolSize);
        var embeddingPool = all.OrderByDescending(item => item.Score)
            .ThenBy(item => item.Key, StringComparer.Ordinal)
            .Take(retrievalPoolSize);
        var lexicalPool = all.Where(item => item.Evidence > 0)
            .OrderByDescending(item => item.LexicalScore)
            .ThenByDescending(item => item.Score)
            .ThenBy(item => item.Key, StringComparer.Ordinal)
            .Take(retrievalPoolSize);
        var retrievalPool = embeddingPool.Concat(lexicalPool)
            .GroupBy(item => item.Key, StringComparer.Ordinal)
            .Select(group => group.OrderByDescending(item => item.Compatibility)
                .ThenByDescending(item => item.Score + item.LexicalScore * .08)
                .First())
            .OrderByDescending(item => item.Compatibility)
            .ThenByDescending(item => item.Score + item.LexicalScore * .08)
            .ThenByDescending(item => item.Score)
            .ThenBy(item => item.Key, StringComparer.Ordinal)
            .ToArray();
        var decision = effectiveCandidateDiscriminator.Decide(
            new SemanticCandidateDiscriminationRequest(
                rawQuery,
                slotKind,
                retrievalPool.Select(item => new SemanticCandidateSignal(
                    item.Metadata,
                    item.ContextualScore,
                    item.LexicalScore,
                    item.PhraseCoverage,
                    item.DescriptionRelevance,
                    item.SourceCompatibility,
                    item.OperationCompatibility,
                    item.SlotCompatibility,
                    item.GroupingFilterCompatibility,
                    item.Evidence,
                    item.Type,
                    item.Kinds.Append(CandidateEvidenceKind.Embedding)
                        .Distinct().ToArray())).ToArray(),
                options.Value.MinSimilarity,
                options.Value.AmbiguityMargin,
                options.Value.PartialQwenMinSimilarity,
                options.Value.MinLexicalEvidenceForBypass,
                options.Value.UnsupportedHighMargin,
                options.Value.CandidateCount));
        effectiveLogger.LogInformation(
            "Semantic candidate discrimination slot {SlotKind}; decision {Decision}; best {BestCandidate}; runner-up {RunnerUp}; score {Score}; margin {Margin}; evidence {EvidenceKinds}.",
            slotKind,
            decision.Kind,
            decision.BestCandidate,
            decision.RunnerUp,
            decision.Score,
            decision.Margin,
            string.Join(',', decision.EvidenceKinds));

        var kind = decision.Kind switch
        {
            SemanticCandidateDecisionKind.Resolved => SemanticResolutionKind.Resolved,
            SemanticCandidateDecisionKind.Ambiguous => SemanticResolutionKind.Ambiguous,
            _ => SemanticResolutionKind.Unsupported
        };
        var best = decision.RankedCandidates.FirstOrDefault(candidate =>
            candidate.CompatibilityValid);
        var candidates = decision.RankedCandidates
            .Where(candidate => candidate.CompatibilityValid)
            .Take(options.Value.CandidateCount)
            .Select(candidate => candidate.SemanticKey)
            .ToArray();
        var evidence = decision.RankedCandidates.Select((item, index) =>
            new CandidateEvidence(
                item.SemanticKey,
                item.EmbeddingSimilarity,
                item.LexicalEvidence,
                item.CompatibilityValid,
                index + 1,
                item.EvidenceKinds,
                item.Score,
                item.PhraseCoverage,
                item.DescriptionRelevance)).ToArray();
        var runnerUpScore = decision.RankedCandidates
            .Where(candidate => candidate.CompatibilityValid)
            .Skip(1).Select(candidate => candidate.Score)
            .DefaultIfEmpty(-1).First();
        return new SemanticResolutionResult(
            kind,
            slotKind,
            kind == SemanticResolutionKind.Resolved ? decision.BestCandidate : null,
            decision.Score,
            runnerUpScore,
            decision.Margin,
            candidates,
            best?.SupportingRepresentationType
                ?? SemanticRepresentationType.FullNormalized,
            best?.LexicalEvidenceCount ?? 0,
            retrievalPool.Select(item => item.Key).ToArray(),
            evidence,
            decision);
    }

    private bool IsCompatible(
        SemanticDocument document,
        SemanticSlotKind slotKind,
        CanonicalRequest? partiallyResolved,
        SemanticSlotIntent intent,
        string? resolvedMetric,
        IReadOnlyList<string> metricCandidateKeys)
    {
        var catalog = effectiveRegistry.GetRequired(document.Source).Catalog;
        if (slotKind == SemanticSlotKind.Metric)
        {
            var metric = catalog.FindMetric(document.Key);
            if (metric is null)
            {
                // Synthetic/custom indexes used by isolated resolver hosts do not
                // necessarily share this process' registry. Runtime indexes are built
                // from the same registry and always take the validated branch below.
                return true;
            }
            return metric.IsUsable
                && (partiallyResolved?.Dimensions ?? []).All(dimension =>
                    catalog.IsMetricDimensionCompatible(document.Key, dimension))
                && (partiallyResolved?.Filters ?? []).All(filter =>
                    catalog.IsMetricFilterCompatible(document.Key, filter.Field));
        }

        var definition = catalog.FindDimension(document.Key);
        if (definition is null)
        {
            return true;
        }
        if (!(definition.Selectable || definition.Filterable))
        {
            return false;
        }
        var compatibleMetrics = resolvedMetric is null
            ? metricCandidateKeys : [resolvedMetric];
        if (compatibleMetrics.Count == 0)
        {
            return true;
        }
        return compatibleMetrics.Any(metricKey =>
        {
            var groupingCompatible = !intent.DimensionRequested
                && !intent.GroupingRequested
                || catalog.IsMetricDimensionCompatible(metricKey, document.Key);
            var filterCompatible = !intent.FilterRequested
                || catalog.IsMetricFilterCompatible(metricKey, document.Key);
            return groupingCompatible && filterCompatible;
        });
    }

    private bool IsOperationCompatible(
        SemanticDocument document,
        SemanticSlotKind slotKind,
        SemanticSlotIntent intent)
    {
        if (slotKind == SemanticSlotKind.Metric)
        {
            return effectiveRegistry.GetRequired(document.Source).Catalog
                .FindMetric(document.Key)?.IsUsable ?? true;
        }
        var definition = effectiveRegistry.GetRequired(document.Source).Catalog
            .FindDimension(document.Key);
        if (definition is null)
        {
            return true;
        }
        return (!intent.GroupingRequested && !intent.DimensionRequested
                || definition.Selectable && definition.Groupable)
            && (!intent.FilterRequested || definition.Filterable)
            && (!intent.RankingRequested || definition.Sortable);
    }

    private static bool IsLexicalConcept(string value) => value.Length >= 3
        && !LexicalStopWords.Contains(value)
        && !value.All(char.IsDigit);

    private static int CountLexicalEvidence(
        IReadOnlyList<string> queryConcepts,
        string documentText)
    {
        var documentConcepts = ExtractDocumentValues(documentText)
            .SelectMany(value => TurkishTextNormalizer.Tokenize(value))
            .Select(token => token.Normalized)
            .Where(IsLexicalConcept)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return queryConcepts.Count(query => documentConcepts.Any(document =>
            AreMorphologicallyRelated(query, document)));
    }

    private static (int Count, double Score,
        IReadOnlyList<CandidateEvidenceKind> Kinds) LexicalEvidence(
        IReadOnlyList<string> queryConcepts,
        SemanticCandidateDescriptor metadata)
    {
        var values = metadata.ConceptualAliases
            .Append(metadata.BusinessDescription)
            .ToArray();
        var documentConcepts = values
            .SelectMany(value => TurkishTextNormalizer.Tokenize(value))
            .Select(token => token.Normalized)
            .Where(IsLexicalConcept)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var exact = queryConcepts.Count(query => documentConcepts.Contains(
            query, StringComparer.Ordinal));
        var morphological = queryConcepts.Count(query => documentConcepts.Any(document =>
            !query.Equals(document, StringComparison.Ordinal)
            && AreMorphologicallyRelated(query, document)));
        var normalizedKey = TurkishTextNormalizer.Normalize(
            metadata.SemanticKey.Replace('_', ' '));
        var key = queryConcepts.Count(query => AreMorphologicallyRelated(
            query, normalizedKey));
        var count = queryConcepts.Count(query => documentConcepts.Any(document =>
            AreMorphologicallyRelated(query, document)));
        var kinds = new List<CandidateEvidenceKind>();
        if (exact > 0)
        {
            kinds.Add(CandidateEvidenceKind.NormalizedPhrase);
            kinds.Add(CandidateEvidenceKind.ConceptualAlias);
        }
        if (morphological > 0)
        {
            kinds.Add(CandidateEvidenceKind.MorphologicalMatch);
        }
        if (key > 0)
        {
            kinds.Add(CandidateEvidenceKind.CatalogKey);
        }
        if (count > 0)
        {
            kinds.Add(CandidateEvidenceKind.SemanticDescription);
        }
        return (count, queryConcepts.Count == 0 ? 0
            : Math.Clamp((exact + morphological * .7 + key * .3)
                / queryConcepts.Count, 0, 1), kinds);
    }

    private static double PhraseCoverage(
        IReadOnlyList<string> queryConcepts,
        SemanticCandidateDescriptor metadata)
    {
        if (queryConcepts.Count == 0)
        {
            return 0;
        }
        return metadata.ConceptualAliases
            .Append(metadata.BusinessDescription)
            .Select(value => TurkishTextNormalizer.Tokenize(value)
                .Select(token => token.Normalized)
                .Where(IsLexicalConcept)
                .Distinct(StringComparer.Ordinal)
                .ToArray())
            .Where(tokens => tokens.Length > 0)
            .Select(tokens =>
                (double)tokens.Count(candidate => queryConcepts.Any(query =>
                    AreMorphologicallyRelated(query, candidate))) / tokens.Length
                * Math.Min(1, tokens.Length / 2d))
            .DefaultIfEmpty(0)
            .Max();
    }

    private static double DescriptionRelevance(
        IReadOnlyList<string> queryConcepts,
        string description)
    {
        var descriptionConcepts = TurkishTextNormalizer.Tokenize(description)
            .Select(token => token.Normalized)
            .Where(IsLexicalConcept)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (queryConcepts.Count == 0 || descriptionConcepts.Length == 0)
        {
            return 0;
        }
        var matches = queryConcepts.Count(query => descriptionConcepts.Any(candidate =>
            AreMorphologicallyRelated(query, candidate)));
        return Math.Clamp(matches / Math.Sqrt(
            queryConcepts.Count * descriptionConcepts.Length), 0, 1);
    }

    private static SemanticCandidateDescriptor ReadCandidateMetadata(
        SemanticDocument document)
    {
        try
        {
            using var parsed = JsonDocument.Parse(document.Text);
            var root = parsed.RootElement;
            var description = root.TryGetProperty("businessDescription", out var value)
                ? value.GetString() ?? string.Empty : string.Empty;
            var name = root.TryGetProperty("businessName", out value)
                ? value.GetString() : null;
            var aliases = root.TryGetProperty("conceptualAliases", out value)
                && value.ValueKind == JsonValueKind.Array
                    ? value.EnumerateArray()
                        .Where(item => item.ValueKind == JsonValueKind.String)
                        .Select(item => item.GetString())
                        .Where(item => !string.IsNullOrWhiteSpace(item))
                        .Select(item => item!)
                        .ToList()
                    : [];
            if (!string.IsNullOrWhiteSpace(name))
            {
                aliases.Insert(0, name);
            }
            var operations = new List<string>();
            if (root.TryGetProperty("supportedOperations", out value)
                && value.ValueKind == JsonValueKind.Object)
            {
                operations.AddRange(value.EnumerateObject()
                    .Where(operation => operation.Value.ValueKind == JsonValueKind.True)
                    .Select(operation => operation.Name));
            }
            return new SemanticCandidateDescriptor(
                document.Key,
                description,
                aliases.Distinct(StringComparer.Ordinal).ToArray(),
                operations.Order(StringComparer.Ordinal).ToArray());
        }
        catch (JsonException)
        {
            return new SemanticCandidateDescriptor(
                document.Key, document.Text, [document.Text], []);
        }
    }

    private static IReadOnlyList<string> ExtractDocumentValues(string text)
    {
        try
        {
            using var document = JsonDocument.Parse(text);
            var values = new List<string>();
            AddStringValues(document.RootElement, values);
            return values;
        }
        catch (JsonException)
        {
            return [text];
        }
    }

    private static void AddStringValues(JsonElement element, List<string> values)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                if (element.GetString() is { Length: > 0 } value)
                {
                    values.Add(value);
                }
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    AddStringValues(item, values);
                }
                break;
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    AddStringValues(property.Value, values);
                }
                break;
        }
    }

    private static bool AreMorphologicallyRelated(string left, string right)
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

    internal static IReadOnlyList<string> ExtractConceptFragments(
        string prompt,
        int maximumPhraseTokens = 8) =>
        SemanticRepresentationFactory.Create(prompt, null, null,
                maximumPhraseTokens, 256)
            .Where(item => item.Type == SemanticRepresentationType.SemanticPhrase)
            .Select(item => item.Text)
            .Order(StringComparer.Ordinal)
            .ToArray();

    private static bool HasDimensionCue(IReadOnlyList<PromptToken> tokens)
    {
        var normalized = tokens.Select(item => item.Normalized).ToArray();
        return normalized.Any(token => token is "bazinda" or "kiriliminda"
                or "kirilimi" or "gore" or "by" or "dagilan" or "ayrilmis"
                or "kiyasla"
                || token.StartsWith("eyalet", StringComparison.Ordinal)
                || token.StartsWith("sehir", StringComparison.Ordinal)
                || token.StartsWith("kategori", StringComparison.Ordinal))
            || normalized.GroupBy(token => token, StringComparer.Ordinal)
                .Any(group => group.Count() > 1 && group.Key.Length > 3);
    }

    private static bool HasMetricCue(IReadOnlyList<PromptToken> tokens) =>
        tokens.Select(item => item.Normalized).Any(token =>
            token.StartsWith("adet", StringComparison.Ordinal)
            || token.StartsWith("sayi", StringComparison.Ordinal)
            || token.StartsWith("tutar", StringComparison.Ordinal)
            || token.StartsWith("bedel", StringComparison.Ordinal)
            || token.StartsWith("ucret", StringComparison.Ordinal)
            || token.StartsWith("toplam", StringComparison.Ordinal)
            || token.StartsWith("total", StringComparison.Ordinal)
            || token.StartsWith("ortalama", StringComparison.Ordinal)
            || token.StartsWith("average", StringComparison.Ordinal)
            || token.StartsWith("mean", StringComparison.Ordinal)
            || token.StartsWith("count", StringComparison.Ordinal)
            || token.StartsWith("marj", StringComparison.Ordinal)
            || token.StartsWith("tahmin", StringComparison.Ordinal)
            || token.StartsWith("getiri", StringComparison.Ordinal)
            || token.StartsWith("deger", StringComparison.Ordinal)
            || token.StartsWith("frequency", StringComparison.Ordinal));

    private static bool HasExplicitMissingConceptCue(string prompt)
    {
        var normalized = TurkishTextNormalizer.Normalize(prompt);
        string[] genericUncertaintyPhrases =
        [
            "karar vermedim", "karar veremedim", "bilmiyorum",
            "emin degilim", "belirsiz", "farketmez", "neyi olcecegim"
        ];
        return genericUncertaintyPhrases.Any(phrase =>
            normalized.Contains(phrase, StringComparison.Ordinal));
    }

    private static void ValidateQueryShape(
        IReadOnlyList<float[]> queryVectors,
        int expectedCount,
        IReadOnlyList<SemanticEmbeddingIndexEntry> entries)
    {
        var expectedDimension = entries.FirstOrDefault()?.Vector.Length ?? 0;
        if (queryVectors.Count != expectedCount || expectedDimension == 0
            || queryVectors.Any(vector => vector.Length != expectedDimension
                || vector.Any(value => !float.IsFinite(value))))
        {
            throw new InvalidDataException("Query embeddings have an invalid vector shape.");
        }
    }

    private static double Cosine(float[] left, float[] right)
    {
        if (left.Length != right.Length || left.Length == 0)
        {
            throw new InvalidDataException("Embedding dimensions do not match.");
        }

        double dot = 0, leftMagnitude = 0, rightMagnitude = 0;
        for (var index = 0; index < left.Length; index++)
        {
            dot += left[index] * right[index];
            leftMagnitude += left[index] * left[index];
            rightMagnitude += right[index] * right[index];
        }

        if (leftMagnitude == 0 || rightMagnitude == 0)
        {
            throw new InvalidDataException("Zero-length embedding magnitude.");
        }

        return dot / (Math.Sqrt(leftMagnitude) * Math.Sqrt(rightMagnitude));
    }

    private static SemanticResolverResult Unavailable(
        string failureKind,
        long preprocessingMilliseconds = 0) =>
        new(false, null, null, false, false, 0, 0, failureKind,
            preprocessingMilliseconds);

    private sealed record CandidateScore(
        string Key,
        DataSource Source,
        double Score,
        double ContextualScore,
        SemanticRepresentationType Type,
        int Evidence,
        double LexicalScore,
        double PhraseCoverage,
        double DescriptionRelevance,
        IReadOnlyList<CandidateEvidenceKind> Kinds,
        SemanticCandidateDescriptor Metadata,
        bool SourceCompatibility,
        bool OperationCompatibility,
        bool SlotCompatibility,
        bool GroupingFilterCompatibility)
    {
        public bool Compatibility => SourceCompatibility
            && OperationCompatibility
            && SlotCompatibility
            && GroupingFilterCompatibility;
    }
}
