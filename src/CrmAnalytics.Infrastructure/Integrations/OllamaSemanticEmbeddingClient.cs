using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace CrmAnalytics.Infrastructure.Integrations;

public sealed class OllamaSemanticEmbeddingClient(
    HttpClient httpClient,
    IOptions<SemanticEmbeddingOptions> options) : ISemanticEmbeddingClient
{
    private static readonly JsonSerializerOptions TransportOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    public async Task<IReadOnlyList<float[]>> EmbedAsync(
        IReadOnlyList<string> inputs,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        if (inputs.Count == 0)
        {
            return [];
        }

        if (inputs.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("Embedding inputs cannot be empty.", nameof(inputs));
        }

        using var response = await httpClient.PostAsJsonAsync(
            "/api/embed",
            new OllamaEmbedRequest(options.Value.Model, inputs),
            TransportOptions,
            cancellationToken);
        response.EnsureSuccessStatusCode();
        var envelope = await response.Content.ReadFromJsonAsync<OllamaEmbedResponse>(
            TransportOptions, cancellationToken);
        if (envelope?.Embeddings is null || envelope.Embeddings.Count != inputs.Count)
        {
            throw new InvalidDataException("Ollama returned an invalid embedding batch shape.");
        }

        return envelope.Embeddings;
    }

    internal sealed record OllamaEmbedRequest(
        string Model,
        IReadOnlyList<string> Input);

    internal sealed record OllamaEmbedResponse(
        string? Model,
        IReadOnlyList<float[]>? Embeddings,
        [property: JsonPropertyName("total_duration")] long? TotalDuration,
        [property: JsonPropertyName("load_duration")] long? LoadDuration,
        [property: JsonPropertyName("prompt_eval_count")] int? PromptEvalCount);
}
