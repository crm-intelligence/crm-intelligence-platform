using Microsoft.Extensions.Options;

namespace CrmAnalytics.Infrastructure.Integrations;

public sealed class OllamaOptionsValidator : IValidateOptions<OllamaOptions>
{
    public ValidateOptionsResult Validate(string? name, OllamaOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var failures = new List<string>();
        if (!Enum.IsDefined(options.PlanningMode))
        {
            failures.Add("Ollama:PlanningMode must be EmbeddingFirst or LlmFirst.");
        }
        if (!Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https"))
        {
            failures.Add("Ollama:BaseUrl must be an absolute HTTP(S) URL.");
        }

        if (string.IsNullOrWhiteSpace(options.Model))
        {
            failures.Add("Ollama:Model is required.");
        }

        if (options.TimeoutSeconds is < 1 or > 300)
        {
            failures.Add("Ollama:TimeoutSeconds must be between 1 and 300.");
        }

        if (options.Temperature != 0)
        {
            failures.Add("Ollama:Temperature must be 0 for canonical planning.");
        }

        if (string.IsNullOrWhiteSpace(options.KeepAlive))
        {
            failures.Add("Ollama:KeepAlive is required.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
