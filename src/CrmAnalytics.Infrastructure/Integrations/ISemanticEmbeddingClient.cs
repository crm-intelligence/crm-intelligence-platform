namespace CrmAnalytics.Infrastructure.Integrations;

public interface ISemanticEmbeddingClient
{
    Task<IReadOnlyList<float[]>> EmbedAsync(
        IReadOnlyList<string> inputs,
        CancellationToken cancellationToken);
}
