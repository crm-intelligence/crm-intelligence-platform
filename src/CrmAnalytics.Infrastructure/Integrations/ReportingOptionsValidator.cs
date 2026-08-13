using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace CrmAnalytics.Infrastructure.Integrations;

public sealed class ReportingOptionsValidator : IValidateOptions<ReportingOptions>
{
    private readonly IHostEnvironment _environment;

    public ReportingOptionsValidator(IHostEnvironment environment) =>
        _environment = environment;

    public ValidateOptionsResult Validate(string? name, ReportingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var protectedEnvironment = _environment.IsProduction()
            || _environment.IsStaging();
        if (AnalyticsOptionsValidator.Is(options.Provider, ReportingProviders.Mock))
        {
            return protectedEnvironment
                ? ValidateOptionsResult.Fail(
                    "Mock reporting is not allowed in Production or Staging.")
                : ValidateOptionsResult.Success;
        }

        if (!AnalyticsOptionsValidator.Is(
                options.Provider, ReportingProviders.PowerBi))
            return ValidateOptionsResult.Fail(
                "Reporting.Provider must be Mock or PowerBi.");

        var failures = new List<string>();
        if (!Guid.TryParse(options.PowerBi.WorkspaceId, out _))
            failures.Add("Reporting.PowerBi.WorkspaceId must be a GUID.");
        if (!Guid.TryParse(options.PowerBi.ReportId, out _))
            failures.Add("Reporting.PowerBi.ReportId must be a GUID.");
        if (!Guid.TryParse(options.PowerBi.OltpReportId, out _))
            failures.Add("Reporting.PowerBi.OltpReportId must be a GUID.");
        ValidatePageId(options.PowerBi.SalesAnalysisPageId,
            "SalesAnalysisPageId", failures);
        ValidatePageId(options.PowerBi.CustomerSegmentationPageId,
            "CustomerSegmentationPageId", failures);
        ValidatePageId(options.PowerBi.PaymentAnalysisPageId,
            "PaymentAnalysisPageId", failures);
        ValidatePageId(options.PowerBi.OltpPageId,
            "OltpPageId", failures);
        if (options.PowerBi.RefreshBeforeReturn
            && !Guid.TryParse(options.PowerBi.SemanticModelId, out _))
            failures.Add("Reporting.PowerBi.SemanticModelId must be a GUID when refresh is enabled.");
        if (options.PowerBi.RefreshTimeoutSeconds is < 1 or > 3600)
            failures.Add("Reporting.PowerBi.RefreshTimeoutSeconds must be between 1 and 3600.");
        if (options.PowerBi.RefreshPollingIntervalSeconds is < 1 or > 60)
            failures.Add("Reporting.PowerBi.RefreshPollingIntervalSeconds must be between 1 and 60.");
        if (options.PowerBi.MaxRetries is < 0 or > 8)
            failures.Add("Reporting.PowerBi.MaxRetries must be between 0 and 8.");
        if (!AnalyticsOptionsValidator.Is(
                options.PowerBi.AuthenticationMode, "ManagedIdentity")
            && !AnalyticsOptionsValidator.Is(
                options.PowerBi.AuthenticationMode, "DefaultAzureCredential"))
            failures.Add("Reporting.PowerBi.AuthenticationMode is invalid.");
        if (protectedEnvironment
            && !AnalyticsOptionsValidator.Is(
                options.PowerBi.AuthenticationMode, "ManagedIdentity"))
            failures.Add("Power BI must use managed identity in Production or Staging.");

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private static void ValidatePageId(
        string value,
        string propertyName,
        ICollection<string> failures)
    {
        if (value.Length != 20 || !value.All(Uri.IsHexDigit))
            failures.Add($"Reporting.PowerBi.{propertyName} is invalid.");
    }
}
