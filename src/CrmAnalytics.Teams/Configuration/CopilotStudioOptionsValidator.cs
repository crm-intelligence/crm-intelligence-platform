using Microsoft.Extensions.Options;

namespace CrmAnalytics.Teams.Configuration;

public sealed class CopilotStudioOptionsValidator
    : IValidateOptions<CopilotStudioOptions>
{
    public ValidateOptionsResult Validate(
        string? name,
        CopilotStudioOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();
        if (options.RequestTimeoutSeconds is < 1 or > 120)
        {
            failures.Add(
                "CopilotStudio:RequestTimeoutSeconds must be between 1 and 120.");
        }

        if (options.Enabled)
        {
            if (!Uri.TryCreate(
                    options.DirectLineBaseUri,
                    UriKind.Absolute,
                    out var directLineBaseUri)
                || directLineBaseUri.Scheme != Uri.UriSchemeHttps
                || !string.IsNullOrEmpty(directLineBaseUri.UserInfo)
                || !string.IsNullOrEmpty(directLineBaseUri.Query)
                || !string.IsNullOrEmpty(directLineBaseUri.Fragment)
                || directLineBaseUri.AbsolutePath != "/")
            {
                failures.Add(
                    "CopilotStudio:DirectLineBaseUri must be an HTTPS origin "
                        + "without a path, query, fragment, or user info when enabled.");
            }

            if (string.IsNullOrWhiteSpace(options.DirectLineSecret))
            {
                failures.Add(
                    "CopilotStudio:DirectLineSecret is required when enabled.");
            }

            if (string.IsNullOrWhiteSpace(options.AgentName))
            {
                failures.Add(
                    "CopilotStudio:AgentName is required when enabled.");
            }
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
