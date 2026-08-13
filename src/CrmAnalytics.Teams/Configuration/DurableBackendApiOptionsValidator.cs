using Microsoft.Extensions.Options;

namespace CrmAnalytics.Teams.Configuration;

internal sealed class DurableBackendApiOptionsValidator(
    IConfiguration configuration,
    IHostEnvironment environment)
    : IValidateOptions<BackendApiOptions>
{
    public ValidateOptionsResult Validate(string? name,
        BackendApiOptions options) =>
        configuration.GetValue<bool>(
            $"{ReportNotificationEndpointOptions.SectionName}:Enabled")
        && !environment.IsDevelopment()
        && !environment.IsEnvironment("Test")
        && !environment.IsEnvironment("Testing")
        && string.IsNullOrWhiteSpace(options.InternalApiKey)
            ? ValidateOptionsResult.Fail(
                "BackendApi:InternalApiKey is required when durable Teams callbacks are enabled.")
            : ValidateOptionsResult.Success;
}
