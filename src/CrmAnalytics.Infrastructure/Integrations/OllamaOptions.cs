namespace CrmAnalytics.Infrastructure.Integrations;

public enum OllamaPlanningMode
{
    EmbeddingFirst,
    LlmFirst
}

public sealed class OllamaOptions
{
    public const string SectionName = "Ollama";

    public bool Enabled { get; set; }
    public OllamaPlanningMode PlanningMode { get; set; } =
        OllamaPlanningMode.EmbeddingFirst;
    public string BaseUrl { get; set; } = "http://localhost:11434";
    public string Model { get; set; } = "qwen3:4b";
    public int TimeoutSeconds { get; set; } = 45;
    public double Temperature { get; set; }
    public string KeepAlive { get; set; } = "10m";
}
