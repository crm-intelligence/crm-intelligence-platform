using Microsoft.Extensions.Options;

namespace CrmAnalytics.Infrastructure.Integrations;

public sealed class SemanticEmbeddingOptions
{
    public const string SectionName = "SemanticEmbedding";

    public bool Enabled { get; set; }
    public string BaseUrl { get; set; } = "http://localhost:11434";
    public string Model { get; set; } = "qwen3-embedding:0.6b";
    public int TimeoutSeconds { get; set; } = 15;
    public int CandidateCount { get; set; } = 3;
    public int RetrievalPoolSize { get; set; } = 6;
    public int MaximumPhraseTokens { get; set; } = 8;
    public int MaximumRepresentations { get; set; } = 64;
    public double MinSimilarity { get; set; } = 0.70;
    public double AmbiguityMargin { get; set; } = 0.02;
    public double PartialQwenMinSimilarity { get; set; } = 0.55;
    public int MinLexicalEvidenceForBypass { get; set; } = 2;
    public double UnsupportedHighMargin { get; set; } = 0.05;
    public double ConceptPresenceSimilarity { get; set; } = 0.35;
}

public sealed class SemanticEmbeddingOptionsValidator
    : IValidateOptions<SemanticEmbeddingOptions>
{
    public ValidateOptionsResult Validate(
        string? name,
        SemanticEmbeddingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var failures = new List<string>();
        if (!Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https"))
        {
            failures.Add("SemanticEmbedding:BaseUrl must be an absolute HTTP(S) URL.");
        }

        if (string.IsNullOrWhiteSpace(options.Model))
        {
            failures.Add("SemanticEmbedding:Model is required.");
        }

        if (options.TimeoutSeconds is < 1 or > 300)
        {
            failures.Add("SemanticEmbedding:TimeoutSeconds must be between 1 and 300.");
        }

        if (options.CandidateCount is < 1 or > 20)
        {
            failures.Add("SemanticEmbedding:CandidateCount must be between 1 and 20.");
        }

        if (options.RetrievalPoolSize < options.CandidateCount
            || options.RetrievalPoolSize > 64)
        {
            failures.Add("SemanticEmbedding:RetrievalPoolSize must be between CandidateCount and 64.");
        }

        if (options.MaximumPhraseTokens is < 4 or > 32)
        {
            failures.Add("SemanticEmbedding:MaximumPhraseTokens must be between 4 and 32.");
        }

        if (options.MaximumRepresentations is < 8 or > 256)
        {
            failures.Add("SemanticEmbedding:MaximumRepresentations must be between 8 and 256.");
        }

        if (options.MinSimilarity is < -1 or > 1)
        {
            failures.Add("SemanticEmbedding:MinSimilarity must be between -1 and 1.");
        }

        if (options.AmbiguityMargin is < 0 or > 2)
        {
            failures.Add("SemanticEmbedding:AmbiguityMargin must be between 0 and 2.");
        }

        if (options.PartialQwenMinSimilarity is < -1 or > 1
            || options.PartialQwenMinSimilarity > options.MinSimilarity)
        {
            failures.Add("SemanticEmbedding:PartialQwenMinSimilarity must be between -1 and MinSimilarity.");
        }

        if (options.MinLexicalEvidenceForBypass is < 0 or > 10)
        {
            failures.Add("SemanticEmbedding:MinLexicalEvidenceForBypass must be between 0 and 10.");
        }

        if (options.UnsupportedHighMargin is < 0 or > 2)
        {
            failures.Add("SemanticEmbedding:UnsupportedHighMargin must be between 0 and 2.");
        }

        if (options.ConceptPresenceSimilarity is < -1 or > 1)
        {
            failures.Add("SemanticEmbedding:ConceptPresenceSimilarity must be between -1 and 1.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
