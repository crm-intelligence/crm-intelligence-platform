using Microsoft.Extensions.Options;

namespace CrmAnalytics.Teams.Configuration;

public sealed class BackendApiOptionsValidator
    : IValidateOptions<BackendApiOptions>
{
    private readonly IHostEnvironment _environment;

    public BackendApiOptionsValidator(IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        _environment = environment;
    }

    public ValidateOptionsResult Validate(
        string? name,
        BackendApiOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();
        var hasValidUri = Uri.TryCreate(
            options.BaseUrl,
            UriKind.Absolute,
            out var baseUri);

        if (!hasValidUri
            || baseUri is null
            || (baseUri.Scheme != Uri.UriSchemeHttp
                && baseUri.Scheme != Uri.UriSchemeHttps))
        {
            failures.Add(
                "BackendApi:BaseUrl must be an absolute HTTP or HTTPS URL.");
        }
        else if (_environment.IsProduction()
            && baseUri.Scheme != Uri.UriSchemeHttps)
        {
            failures.Add(
                "BackendApi:BaseUrl must use HTTPS in Production.");
        }

        if (options.TimeoutSeconds is < 1 or > 60)
        {
            failures.Add(
                "BackendApi:TimeoutSeconds must be between 1 and 60.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
