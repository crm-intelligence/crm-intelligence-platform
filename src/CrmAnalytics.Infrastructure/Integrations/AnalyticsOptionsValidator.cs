using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace CrmAnalytics.Infrastructure.Integrations;

public sealed class AnalyticsOptionsValidator : IValidateOptions<AnalyticsOptions>
{
    public const string MissingFabricContractMessage =
        "FabricJob cannot be activated: an approved Blob/OneLake/Delta staging and job parameter contract is not present in this repository.";

    private readonly IHostEnvironment _environment;

    public AnalyticsOptionsValidator(IHostEnvironment environment) =>
        _environment = environment;

    public ValidateOptionsResult Validate(string? name, AnalyticsOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var protectedEnvironment = _environment.IsProduction()
            || _environment.IsStaging();

        if (Is(options.Provider, AnalyticsProviders.Mock))
        {
            return protectedEnvironment
                ? ValidateOptionsResult.Fail(
                    "Mock analytics is not allowed in Production or Staging.")
                : ValidateOptionsResult.Success;
        }

        if (Is(options.Provider, AnalyticsProviders.Direct))
        {
            return protectedEnvironment
                && !options.AllowDirectInProtectedEnvironments
                ? ValidateOptionsResult.Fail(
                    "Direct analytics in Production or Staging requires an explicit demo decision.")
                : ValidateOptionsResult.Success;
        }

        if (!Is(options.Provider, AnalyticsProviders.FabricJob))
        {
            return ValidateOptionsResult.Fail(
                "Analytics.Provider must be Mock, Direct, or FabricJob.");
        }

        var failures = new List<string>();
        if (!Guid.TryParse(options.Fabric.WorkspaceId, out _))
            failures.Add("Analytics.Fabric.WorkspaceId must be a GUID.");
        if (!Guid.TryParse(options.Fabric.ItemId, out _))
            failures.Add("Analytics.Fabric.ItemId must be a GUID.");
        if (string.IsNullOrWhiteSpace(options.Fabric.JobType))
            failures.Add("Analytics.Fabric.JobType is required.");
        if (options.Fabric.PollingIntervalSeconds is < 1 or > 60)
            failures.Add("Analytics.Fabric.PollingIntervalSeconds must be between 1 and 60.");
        if (options.Fabric.TimeoutSeconds is < 1 or > 3600)
            failures.Add("Analytics.Fabric.TimeoutSeconds must be between 1 and 3600.");
        if (options.Fabric.MaxRetries is < 0 or > 8)
            failures.Add("Analytics.Fabric.MaxRetries must be between 0 and 8.");
        if (!Is(options.Fabric.AuthenticationMode, "ManagedIdentity")
            && !Is(options.Fabric.AuthenticationMode, "DefaultAzureCredential"))
            failures.Add("Analytics.Fabric.AuthenticationMode is invalid.");
        if (protectedEnvironment
            && !Is(options.Fabric.AuthenticationMode, "ManagedIdentity"))
            failures.Add("Fabric must use managed identity in Production or Staging.");

        // The current qry_* reference is process-local. Do not permit an operator
        // to mistake it for durable Fabric-readable staging.
        failures.Add(MissingFabricContractMessage);
        return ValidateOptionsResult.Fail(failures);
    }

    internal static bool Is(string? value, string expected) =>
        string.Equals(value, expected, StringComparison.OrdinalIgnoreCase);
}
