using Microsoft.Extensions.Options;

namespace CrmAnalytics.Infrastructure.Integrations;

public sealed class MockExternalServicesOptionsValidator
    : IValidateOptions<MockExternalServicesOptions>
{
    public ValidateOptionsResult Validate(
        string? name,
        MockExternalServicesOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();

        if (options.DelayMilliseconds is < 0 or > 5000)
        {
            failures.Add(
                $"{nameof(options.DelayMilliseconds)} must be between "
                    + "0 and 5000.");
        }

        if (!Uri.TryCreate(
                options.PowerBiBaseUrl,
                UriKind.Absolute,
                out var powerBiBaseUri)
            || !string.Equals(
                powerBiBaseUri.Scheme,
                Uri.UriSchemeHttps,
                StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(powerBiBaseUri.Host))
        {
            failures.Add(
                $"{nameof(options.PowerBiBaseUrl)} must be an absolute "
                    + "HTTPS URL.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
