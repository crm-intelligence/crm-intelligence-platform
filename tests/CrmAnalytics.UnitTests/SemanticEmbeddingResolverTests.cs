using System.Net;
using System.Text;
using System.Text.Json;
using Crm.Analytics.Sql.Catalog;
using Crm.Analytics.Sql.Contracts;
using Crm.Analytics.Sql.Nlu;
using CrmAnalytics.Infrastructure.Integrations;
using Microsoft.Extensions.Options;

namespace CrmAnalytics.UnitTests;

public sealed class SemanticEmbeddingResolverTests
{
    [Fact]
    public void DocumentsComeFromRegistryAndExcludePhysicalMetadata()
    {
        var registry = SemanticCatalogRegistry.CreateDefault();
        var documents = new SemanticDocumentFactory(registry).CreateDocuments();

        Assert.Contains(documents, document => document.Key == "item_sales"
            && document.Text.Contains("Urun Satis Tutari", StringComparison.Ordinal));
        Assert.Contains(documents, document => document.Key == "product_category");
        var corpus = string.Join('\n', documents.Select(document => document.Text));
        Assert.DoesNotContain("SUM(", corpus, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("vw_", corpus, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("queryMappingReference", corpus, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"column\"", corpus, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"source\"", corpus, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CatalogEmbeddingsAreCachedAndFingerprintChangeRebuildsIndex()
    {
        var registry = SemanticCatalogRegistry.CreateDefault();
        var embedding = new CountingEmbeddingClient(inputs =>
            inputs.Select(_ => new float[] { 1, 0 }).ToArray());
        var index = new SemanticEmbeddingIndex(
            new SemanticDocumentFactory(registry), embedding, Options());

        var first = await index.GetAsync(CancellationToken.None);
        var second = await index.GetAsync(CancellationToken.None);
        Assert.Same(first, second);
        Assert.Equal(1, embedding.CallCount);

        var metrics = Assert.IsType<Dictionary<string, MetricDefinition>>(
            registry.GetRequired(DataSource.Dwh).Catalog.Metrics);
        var exemplar = metrics["item_sales"];
        metrics["test_only_extensible_metric"] = new MetricDefinition
        {
            Label = "Test Only Extensible Metric",
            Description = "Test-only business measure used to verify index extensibility.",
            Kind = exemplar.Kind,
            Expression = exemplar.Expression,
            Source = exemplar.Source,
            Unit = exemplar.Unit,
            ValueType = exemplar.ValueType,
            Aliases = ["test only measure"],
            CompatibleDimensions = [],
            CompatibleFilters = [],
            RequiresDateRange = exemplar.RequiresDateRange,
            QueryMappingReference = "test.only.not.model.facing",
            ApprovalStatus = "documented"
        };

        var changed = await index.GetAsync(CancellationToken.None);
        Assert.NotEqual(first.CacheKey, changed.CacheKey);
        Assert.Contains(changed.Entries,
            entry => entry.Document.Key == "test_only_extensible_metric");
        Assert.Equal(2, embedding.CallCount);

        var semanticEmbedding = new CountingEmbeddingClient(inputs => inputs
            .Select(input => input.StartsWith('{')
                ? input.Contains("test_only_extensible_metric", StringComparison.Ordinal)
                    ? new float[] { 1, 0 }
                    : new float[] { 0, 1 }
                : new float[] { 1, 0 })
            .ToArray());
        var semanticIndex = new SemanticEmbeddingIndex(
            new SemanticDocumentFactory(registry), semanticEmbedding,
            Options(.5, .1));
        var resolver = new SemanticEmbeddingResolver(
            semanticIndex, semanticEmbedding, Options(.5, .1));
        var resolved = await resolver.ResolveAsync(
            "2018 test only measure total", DataSource.Dwh, null,
            new DateOnly(2026, 8, 6), CancellationToken.None);
        Assert.Equal("test_only_extensible_metric", resolved.Metric!.CandidateKey);

        var assembly = new SemanticCanonicalRequestAssembler(registry).Assemble(
            "request0000000000000000000000001", "conversation-1",
            "2018 test only measure total", DataSource.Dwh, null, resolved);
        Assert.Equal(SemanticCanonicalAssemblyOutcome.Assembled, assembly.Outcome);
        Assert.Equal(["test_only_extensible_metric"],
            assembly.CanonicalRequest!.Metrics);
    }

    [Fact]
    public void Representations_MaskDateAndFilterWithoutThreeWordLimit()
    {
        var prompt = "Marmara bolgesindeki urunlerden gelen toplam para akisini son 30 gun icin ozetle";
        var date = RelativeDateResolver.Resolve(
            TurkishTextNormalizer.Tokenize(prompt), new DateOnly(2026, 8, 6));
        var representations = SemanticRepresentationFactory.Create(
            prompt, date, null, maximumPhraseTokens: 8,
            maximumRepresentations: 128);

        Assert.Contains(representations, item =>
            item.Type == SemanticRepresentationType.DateMasked
            && item.Text.Contains("semantic_date", StringComparison.Ordinal)
            && !item.Text.Contains("30 gun", StringComparison.Ordinal));
        Assert.Contains(representations, item =>
            item.Type == SemanticRepresentationType.FilterValueMasked
            && item.Text.Contains("semantic_filter_value", StringComparison.Ordinal)
            && !item.Text.Contains("marmara", StringComparison.Ordinal));
        Assert.Contains(representations, item =>
            item.Type == SemanticRepresentationType.SemanticPhrase
            && item.Text.Split(' ').Length > 3);
    }

    [Fact]
    public async Task DateMaskedViewCanResolveMetricWhenFullDateViewIsDistracting()
    {
        var snapshot = Snapshot([[1, 0], [.7f, .7f]]);
        var resolver = new SemanticEmbeddingResolver(
            new FixedIndex(snapshot),
            new CountingEmbeddingClient(inputs => inputs.Select(input =>
                input.Contains("semantic_date", StringComparison.Ordinal)
                    ? new float[] { 1, 0 }
                    : new float[] { 0, 1 }).ToArray()),
            Options(.5, .1));

        var result = await resolver.ResolveAsync(
            "son 30 gun business metric total", DataSource.Dwh, null,
            new DateOnly(2026, 8, 6), CancellationToken.None);

        Assert.Equal("metric_a", result.Metric!.CandidateKey);
        Assert.Equal(SemanticRepresentationType.DateMasked,
            result.Metric.SupportingRepresentationType);
    }

    [Fact]
    public async Task FilterValueMaskedViewCanResolveMetricWithoutLiteralDistortion()
    {
        var snapshot = Snapshot([[1, 0], [.7f, .7f]]);
        var resolver = new SemanticEmbeddingResolver(
            new FixedIndex(snapshot),
            new CountingEmbeddingClient(inputs => inputs.Select(input =>
                input.Contains("semantic_filter_value", StringComparison.Ordinal)
                    ? new float[] { 1, 0 }
                    : new float[] { 0, 1 }).ToArray()),
            Options(.5, .1));

        var result = await resolver.ResolveAsync(
            "Marmara bolgesindeki business metric total", DataSource.Dwh, null,
            CancellationToken.None);

        Assert.Equal("metric_a", result.Metric!.CandidateKey);
        Assert.Equal(SemanticRepresentationType.FilterValueMasked,
            result.Metric.SupportingRepresentationType);
    }

    [Fact]
    public async Task MetricAndDimensionRetrievalUseOnlyTheirOwnCatalogPartitions()
    {
        var snapshot = new SemanticEmbeddingIndexSnapshot("test",
        [
            new(new SemanticDocument(DataSource.Dwh, SemanticSlotKind.Metric,
                "metric_only", "safe"), new float[] { 0.8f, 0.6f }),
            new(new SemanticDocument(DataSource.Dwh, SemanticSlotKind.Dimension,
                "dimension_only", "safe"), new float[] { 1, 0 })
        ]);
        var resolver = new SemanticEmbeddingResolver(
            new FixedIndex(snapshot),
            new CountingEmbeddingClient(inputs => inputs.Select(_ =>
                new float[] { 1, 0 }).ToArray()),
            Options(.5, .05));

        var result = await resolver.ResolveAsync(
            "business metric total kategori bazinda", DataSource.Dwh, null,
            CancellationToken.None);

        Assert.Equal("metric_only", result.Metric!.CandidateKey);
        Assert.DoesNotContain("dimension_only", result.Metric.CandidateKeys);
        Assert.Equal("dimension_only", result.Dimension!.CandidateKey);
        Assert.DoesNotContain("metric_only", result.Dimension.CandidateKeys);
    }

    [Fact]
    public async Task StrongTopOneAndMarginResolves()
    {
        var result = await Resolve(
            query: [1, 0],
            metricVectors: [[1, 0], [0, 1]],
            minSimilarity: .5,
            margin: .1);

        Assert.Equal(SemanticResolutionKind.Resolved, result.Metric!.Kind);
        Assert.Equal("metric_a", result.Metric.CandidateKey);
    }

    [Fact]
    public async Task LowSimilarityIsUnsupported()
    {
        var result = await Resolve(
            query: [0, 1],
            metricVectors: [[1, 0], [1, 0]],
            minSimilarity: .5,
            margin: .1);

        Assert.Equal(SemanticResolutionKind.Unsupported, result.Metric!.Kind);
    }

    [Fact]
    public async Task CloseTopOneAndTopTwoIsAmbiguous()
    {
        var result = await Resolve(
            query: [1, 0],
            metricVectors: [[1, 0], [.99f, .1f]],
            minSimilarity: .5,
            margin: .02);

        Assert.Equal(SemanticResolutionKind.Ambiguous, result.Metric!.Kind);
        Assert.Equal(["metric_a", "metric_b"], result.Metric.CandidateKeys);
    }

    [Fact]
    public async Task HighEmbeddingConfidenceWithoutLexicalEvidenceCannotBypass()
    {
        var resolver = new SemanticEmbeddingResolver(
            new FixedIndex(Snapshot([[1, 0], [0, 1]])),
            new CountingEmbeddingClient(inputs => inputs.Select(_ =>
                new float[] { 1, 0 }).ToArray()),
            Microsoft.Extensions.Options.Options.Create(
                new SemanticEmbeddingOptions
                {
                    MinSimilarity = .7,
                    AmbiguityMargin = .02,
                    PartialQwenMinSimilarity = .55,
                    MinLexicalEvidenceForBypass = 2,
                    UnsupportedHighMargin = .05
                }));

        var result = await resolver.ResolveAsync(
            "unknown business concept", DataSource.Dwh, null,
            CancellationToken.None);

        Assert.Equal(SemanticResolutionKind.Unsupported, result.Metric!.Kind);
        Assert.Equal(0, result.Metric.LexicalEvidenceCount);
    }

    [Fact]
    public async Task CandidateUnionAddsLexicalConceptOutsideEmbeddingTopPool()
    {
        var entries = Enumerable.Range(0, 7).Select(index =>
                new SemanticEmbeddingIndexEntry(
                    new SemanticDocument(DataSource.Dwh, SemanticSlotKind.Metric,
                        $"embedding_{index}", "unrelated"), [1, 0]))
            .Append(new SemanticEmbeddingIndexEntry(
                new SemanticDocument(DataSource.Dwh, SemanticSlotKind.Metric,
                    "lexical_target",
                    "{\"conceptualAliases\":[\"business velocity measure\"]}"),
                [0, 1]))
            .Append(new SemanticEmbeddingIndexEntry(
                new SemanticDocument(DataSource.Dwh, SemanticSlotKind.Dimension,
                    "dimension_a", "safe"), [0, 1]))
            .ToArray();
        var resolver = new SemanticEmbeddingResolver(
            new FixedIndex(new SemanticEmbeddingIndexSnapshot("union", entries)),
            new CountingEmbeddingClient(inputs => inputs.Select(_ =>
                new float[] { 1, 0 }).ToArray()),
            Microsoft.Extensions.Options.Options.Create(
                new SemanticEmbeddingOptions
                {
                    CandidateCount = 3,
                    RetrievalPoolSize = 6,
                    MinSimilarity = .7,
                    AmbiguityMargin = .02,
                    PartialQwenMinSimilarity = .55,
                    MinLexicalEvidenceForBypass = 2
                }));

        var result = await resolver.ResolveAsync(
            "business velocity measure", DataSource.Dwh, null,
            CancellationToken.None);

        Assert.Contains("lexical_target",
            result.Metric!.RetrievalCandidateKeys!);
        Assert.True(result.Metric.CandidateKeys.Count <= 3);
        Assert.Equal("lexical_target", result.Metric.CandidateKeys[0]);
        Assert.Equal(SemanticResolutionKind.Ambiguous, result.Metric.Kind);
    }

    [Fact]
    public async Task TwoGenericCatalogAnchorsPermitHighConfidenceResolution()
    {
        var snapshot = new SemanticEmbeddingIndexSnapshot("lexical",
        [
            new SemanticEmbeddingIndexEntry(
                new SemanticDocument(DataSource.Dwh, SemanticSlotKind.Metric,
                    "metric_a", "{\"conceptualAliases\":[\"business metric\"]}"),
                [1, 0]),
            new SemanticEmbeddingIndexEntry(
                new SemanticDocument(DataSource.Dwh, SemanticSlotKind.Metric,
                    "metric_b", "{\"conceptualAliases\":[\"other measure\"]}"),
                [0, 1]),
            new SemanticEmbeddingIndexEntry(
                new SemanticDocument(DataSource.Dwh, SemanticSlotKind.Dimension,
                    "dimension_a", "safe"), [0, 1])
        ]);
        var resolver = new SemanticEmbeddingResolver(
            new FixedIndex(snapshot),
            new CountingEmbeddingClient(inputs => inputs.Select(_ =>
                new float[] { 1, 0 }).ToArray()),
            Microsoft.Extensions.Options.Options.Create(
                new SemanticEmbeddingOptions
                {
                    MinSimilarity = .7,
                    AmbiguityMargin = .02,
                    PartialQwenMinSimilarity = .55,
                    MinLexicalEvidenceForBypass = 2,
                    UnsupportedHighMargin = .05
                }));

        var result = await resolver.ResolveAsync(
            "requested business metric", DataSource.Dwh, null,
            CancellationToken.None);

        Assert.Equal(SemanticResolutionKind.Resolved, result.Metric!.Kind);
        Assert.Equal(2, result.Metric.LexicalEvidenceCount);
    }

    [Fact]
    public async Task UnknownQueryVectorShapeFailsSafe()
    {
        var snapshot = Snapshot([[1, 0], [0, 1]]);
        var resolver = new SemanticEmbeddingResolver(
            new FixedIndex(snapshot),
            new CountingEmbeddingClient(inputs => inputs.Select(_ =>
                new float[] { 1, 0, 0 }).ToArray()),
            Options());

        var result = await resolver.ResolveAsync(
            "business measure", DataSource.Dwh, null, CancellationToken.None);

        Assert.False(result.Available);
        Assert.Equal("InvalidDataException", result.FailureKind);
    }

    [Fact]
    public async Task OllamaClientUsesBatchEmbedContract()
    {
        var handler = new RecordingHandler();
        var client = new OllamaSemanticEmbeddingClient(
            new HttpClient(handler) { BaseAddress = new Uri("http://localhost:11434") },
            Options());

        var vectors = await client.EmbedAsync(["first", "second"],
            CancellationToken.None);

        Assert.Equal(2, vectors.Count);
        Assert.Equal("/api/embed", handler.Path);
        using var request = JsonDocument.Parse(handler.Body!);
        Assert.Equal("qwen3-embedding:0.6b",
            request.RootElement.GetProperty("model").GetString());
        Assert.Equal(2, request.RootElement.GetProperty("input").GetArrayLength());
    }

    private static async Task<SemanticResolverResult> Resolve(
        float[] query,
        float[][] metricVectors,
        double minSimilarity,
        double margin)
    {
        var options = Options(minSimilarity, margin);
        var resolver = new SemanticEmbeddingResolver(
            new FixedIndex(Snapshot(metricVectors)),
            new CountingEmbeddingClient(inputs => inputs.Select(_ => query).ToArray()),
            options);
        return await resolver.ResolveAsync(
            "requested business metric total", DataSource.Dwh, null,
            CancellationToken.None);
    }

    private static SemanticEmbeddingIndexSnapshot Snapshot(float[][] metricVectors)
    {
        var entries = metricVectors.Select((vector, index) =>
                new SemanticEmbeddingIndexEntry(
                    new SemanticDocument(DataSource.Dwh, SemanticSlotKind.Metric,
                        index == 0 ? "metric_a" : "metric_b", "safe"), vector))
            .Append(new SemanticEmbeddingIndexEntry(
                new SemanticDocument(DataSource.Dwh, SemanticSlotKind.Dimension,
                    "dimension_a", "safe"), new float[] { 0, 1 }))
            .ToArray();
        return new SemanticEmbeddingIndexSnapshot("test", entries);
    }

    private static IOptions<SemanticEmbeddingOptions> Options(
        double minSimilarity = .55,
        double margin = .05) => Microsoft.Extensions.Options.Options.Create(
        new SemanticEmbeddingOptions
        {
            Enabled = true,
            MinSimilarity = minSimilarity,
            AmbiguityMargin = margin,
            CandidateCount = 3,
            MinLexicalEvidenceForBypass = 0
        });

    private sealed class FixedIndex(SemanticEmbeddingIndexSnapshot snapshot)
        : ISemanticEmbeddingIndex
    {
        public Task<SemanticEmbeddingIndexSnapshot> GetAsync(
            CancellationToken cancellationToken) => Task.FromResult(snapshot);
    }

    private sealed class CountingEmbeddingClient(
        Func<IReadOnlyList<string>, IReadOnlyList<float[]>> factory)
        : ISemanticEmbeddingClient
    {
        public int CallCount { get; private set; }

        public Task<IReadOnlyList<float[]>> EmbedAsync(
            IReadOnlyList<string> inputs,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(factory(inputs));
        }
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public string? Path { get; private set; }
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Path = request.RequestUri?.AbsolutePath;
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "{\"model\":\"qwen3-embedding:0.6b\",\"embeddings\":[[1,0],[0,1]]}",
                    Encoding.UTF8,
                    "application/json")
            };
        }
    }
}
