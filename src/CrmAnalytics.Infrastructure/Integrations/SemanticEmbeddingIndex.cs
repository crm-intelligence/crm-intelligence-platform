using System.Collections.Concurrent;
using Microsoft.Extensions.Options;

namespace CrmAnalytics.Infrastructure.Integrations;

public sealed record SemanticEmbeddingIndexEntry(
    SemanticDocument Document,
    float[] Vector);

public sealed record SemanticEmbeddingIndexSnapshot(
    string CacheKey,
    IReadOnlyList<SemanticEmbeddingIndexEntry> Entries);

public interface ISemanticEmbeddingIndex
{
    Task<SemanticEmbeddingIndexSnapshot> GetAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Process-local, deterministic catalog index. The cache key changes with either the
/// semantic projection or embedding model and Lazy<Task> prevents duplicate startup work.
/// </summary>
public sealed class SemanticEmbeddingIndex(
    SemanticDocumentFactory documentFactory,
    ISemanticEmbeddingClient embeddingClient,
    IOptions<SemanticEmbeddingOptions> options) : ISemanticEmbeddingIndex
{
    private readonly ConcurrentDictionary<string, Lazy<Task<SemanticEmbeddingIndexSnapshot>>>
        cache = new(StringComparer.Ordinal);

    public Task<SemanticEmbeddingIndexSnapshot> GetAsync(
        CancellationToken cancellationToken)
    {
        var cacheKey = $"{documentFactory.CreateFingerprint()}:{options.Value.Model}";
        var lazy = cache.GetOrAdd(cacheKey, key => new Lazy<Task<SemanticEmbeddingIndexSnapshot>>(
            () => BuildAsync(key, cancellationToken), LazyThreadSafetyMode.ExecutionAndPublication));
        return AwaitAndEvictFailureAsync(cacheKey, lazy);
    }

    private async Task<SemanticEmbeddingIndexSnapshot> BuildAsync(
        string cacheKey,
        CancellationToken cancellationToken)
    {
        var documents = documentFactory.CreateDocuments();
        var vectors = await embeddingClient.EmbedAsync(
            documents.Select(item => item.Text).ToArray(), cancellationToken);
        if (vectors.Count != documents.Count || vectors.Count == 0)
        {
            throw new InvalidDataException("Catalog embedding count does not match the catalog.");
        }

        var dimension = vectors[0].Length;
        if (dimension == 0 || vectors.Any(vector => vector.Length != dimension
            || vector.Any(value => !float.IsFinite(value))))
        {
            throw new InvalidDataException("Catalog embeddings have an invalid vector shape.");
        }

        return new SemanticEmbeddingIndexSnapshot(cacheKey,
            documents.Zip(vectors, (document, vector) =>
                new SemanticEmbeddingIndexEntry(document, vector)).ToArray());
    }

    private async Task<SemanticEmbeddingIndexSnapshot> AwaitAndEvictFailureAsync(
        string cacheKey,
        Lazy<Task<SemanticEmbeddingIndexSnapshot>> lazy)
    {
        try
        {
            return await lazy.Value;
        }
        catch
        {
            cache.TryRemove(new KeyValuePair<string,
                Lazy<Task<SemanticEmbeddingIndexSnapshot>>>(cacheKey, lazy));
            throw;
        }
    }
}
