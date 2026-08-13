using Microsoft.Extensions.Options;

namespace CrmAnalytics.Teams.Configuration;

public sealed class ReportNotificationEndpointOptionsValidator
    : IValidateOptions<ReportNotificationEndpointOptions>
{
    public ValidateOptionsResult Validate(
        string? name,
        ReportNotificationEndpointOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return !options.Enabled
            || !string.IsNullOrWhiteSpace(options.ApiKey)
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(
                "ReportNotifications:ApiKey is required when "
                + "notifications are enabled.");
    }
}
