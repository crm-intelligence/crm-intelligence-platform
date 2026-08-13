using Microsoft.Extensions.Options;

namespace CrmAnalytics.Teams.Configuration;

public sealed class TeamsHostOptionsValidator
    : IValidateOptions<TeamsHostOptions>
{
    private readonly IHostEnvironment _environment;

    public TeamsHostOptionsValidator(IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        _environment = environment;
    }

    public ValidateOptionsResult Validate(
        string? name,
        TeamsHostOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!_environment.IsProduction() && !_environment.IsStaging())
        {
            return ValidateOptionsResult.Success;
        }

        var failures = new List<string>();
        if (!Guid.TryParse(options.ClientId, out _))
        {
            failures.Add("Teams:ClientId must be a valid GUID.");
        }

        if (!Guid.TryParse(options.TenantId, out _))
        {
            failures.Add("Teams:TenantId must be a valid GUID.");
        }

        if (!string.Equals(
                options.AppType,
                "SingleTenant",
                StringComparison.Ordinal))
        {
            failures.Add(
                "Teams:AppType must be SingleTenant in Production or Staging.");
        }

        if (string.IsNullOrWhiteSpace(options.ClientSecret))
        {
            failures.Add(
                "Teams:ClientSecret is required in Production or Staging.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
