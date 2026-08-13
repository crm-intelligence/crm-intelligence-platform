using Microsoft.Extensions.Options;

namespace CrmAnalytics.Api.Authentication;

public sealed class CrmAnalyticsAuthenticationOptionsValidator
    : IValidateOptions<CrmAnalyticsAuthenticationOptions>
{
    private static readonly string[] MultiTenantAliases =
    {
        "common",
        "organizations",
        "consumers"
    };

    private readonly IHostEnvironment _environment;
    private readonly IOptions<AzureAdOptions> _azureAdOptions;

    public CrmAnalyticsAuthenticationOptionsValidator(
        IHostEnvironment environment,
        IOptions<AzureAdOptions> azureAdOptions)
    {
        _environment = environment;
        _azureAdOptions = azureAdOptions;
    }

    public ValidateOptionsResult Validate(
        string? name,
        CrmAnalyticsAuthenticationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.RequiredScope))
        {
            failures.Add(
                "CrmAnalyticsAuthentication:RequiredScope is required.");
        }

        if (string.Equals(
                options.Mode,
                AuthenticationModes.Development,
                StringComparison.OrdinalIgnoreCase))
        {
            ValidateDevelopment(options, failures);
        }
        else if (string.Equals(
            options.Mode,
            AuthenticationModes.Entra,
            StringComparison.OrdinalIgnoreCase))
        {
            ValidateEntra(_azureAdOptions.Value, failures);
        }
        else
        {
            failures.Add(
                "CrmAnalyticsAuthentication:Mode must be Entra or "
                + "Development.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private void ValidateDevelopment(
        CrmAnalyticsAuthenticationOptions options,
        ICollection<string> failures)
    {
        if (!_environment.IsDevelopment())
        {
            failures.Add(
                "Development authentication is allowed only in the "
                + "Development environment.");
        }

        if (!IsGuid(options.Development?.UserId))
        {
            failures.Add(
                "CrmAnalyticsAuthentication:Development:UserId must "
                + "be a valid GUID.");
        }

        if (!IsGuid(options.Development?.TenantId))
        {
            failures.Add(
                "CrmAnalyticsAuthentication:Development:TenantId must "
                + "be a valid GUID.");
        }

        if (options.Development?.Roles is null)
        {
            failures.Add(
                "CrmAnalyticsAuthentication:Development:Roles must "
                + "not be null.");
        }
    }

    private static void ValidateEntra(
        AzureAdOptions azureAd,
        ICollection<string> failures)
    {
        if (!IsGuid(azureAd.TenantId)
            || MultiTenantAliases.Contains(
                azureAd.TenantId?.Trim(),
                StringComparer.OrdinalIgnoreCase))
        {
            failures.Add("AzureAd:TenantId must be a single-tenant GUID.");
        }

        if (!IsGuid(azureAd.ClientId))
        {
            failures.Add("AzureAd:ClientId must be a valid GUID.");
        }

        if (!IsSafeHttpsUri(azureAd.Instance))
        {
            failures.Add(
                "AzureAd:Instance must be an absolute HTTPS URL.");
        }

        if (!string.IsNullOrWhiteSpace(azureAd.Audience)
            && !IsSafeAudience(azureAd.Audience))
        {
            failures.Add(
                "AzureAd:Audience must be a GUID or an absolute "
                + "HTTPS/api URI.");
        }
    }

    private static bool IsGuid(string? value) =>
        Guid.TryParse(value?.Trim(), out _);

    private static bool IsSafeHttpsUri(string? value) =>
        Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var uri)
        && string.Equals(
            uri.Scheme,
            Uri.UriSchemeHttps,
            StringComparison.OrdinalIgnoreCase)
        && !string.IsNullOrWhiteSpace(uri.Host)
        && string.IsNullOrEmpty(uri.UserInfo)
        && string.IsNullOrEmpty(uri.Query)
        && string.IsNullOrEmpty(uri.Fragment);

    private static bool IsSafeAudience(string value)
    {
        var normalized = value.Trim();

        if (normalized.Any(char.IsControl)
            || normalized.Any(char.IsWhiteSpace))
        {
            return false;
        }

        if (Guid.TryParse(normalized, out _))
        {
            return true;
        }

        return Uri.TryCreate(normalized, UriKind.Absolute, out var uri)
            && (string.Equals(
                    uri.Scheme,
                    Uri.UriSchemeHttps,
                    StringComparison.OrdinalIgnoreCase)
                || string.Equals(
                    uri.Scheme,
                    "api",
                    StringComparison.OrdinalIgnoreCase))
            && !string.IsNullOrWhiteSpace(uri.Host)
            && string.IsNullOrEmpty(uri.UserInfo)
            && string.IsNullOrEmpty(uri.Query)
            && string.IsNullOrEmpty(uri.Fragment);
    }
}
