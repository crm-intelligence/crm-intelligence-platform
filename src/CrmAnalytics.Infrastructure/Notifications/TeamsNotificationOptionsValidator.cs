using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace CrmAnalytics.Infrastructure.Notifications;

public sealed class TeamsNotificationOptionsValidator
    : IValidateOptions<TeamsNotificationOptions>
{
    private readonly IHostEnvironment _environment;

    public TeamsNotificationOptionsValidator(
        IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        _environment = environment;
    }

    public ValidateOptionsResult Validate(
        string? name,
        TeamsNotificationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();

        if (options.Enabled)
        {
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
                    "TeamsNotifications:BaseUrl must be an absolute "
                    + "HTTP or HTTPS URL when notifications are enabled.");
            }
            else if (_environment.IsProduction()
                && baseUri.Scheme != Uri.UriSchemeHttps)
            {
                failures.Add(
                    "TeamsNotifications:BaseUrl must use HTTPS in "
                    + "Production.");
            }

            if (string.IsNullOrWhiteSpace(options.ApiKey))
            {
                failures.Add(
                    "TeamsNotifications:ApiKey is required when "
                    + "notifications are enabled.");
            }
        }

        if (options.TimeoutSeconds is < 1 or > 60)
        {
            failures.Add(
                "TeamsNotifications:TimeoutSeconds must be between "
                + "1 and 60.");
        }

        if (options.TargetNotReadyRetryCount is < 0 or > 10)
        {
            failures.Add(
                "TeamsNotifications:TargetNotReadyRetryCount must be "
                + "between 0 and 10.");
        }

        if (options.TargetNotReadyInitialDelayMilliseconds is < 10 or > 5000)
        {
            failures.Add(
                "TeamsNotifications:TargetNotReadyInitialDelayMilliseconds "
                + "must be between 10 and 5000.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
