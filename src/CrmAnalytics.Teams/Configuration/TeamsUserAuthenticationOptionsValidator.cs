using Microsoft.Extensions.Options;

namespace CrmAnalytics.Teams.Configuration;

public sealed class TeamsUserAuthenticationOptionsValidator
    : IValidateOptions<TeamsUserAuthenticationOptions>
{
    private readonly IHostEnvironment _environment;
    private readonly IOptions<TeamsHostOptions> _teamsHostOptions;

    public TeamsUserAuthenticationOptionsValidator(
        IHostEnvironment environment,
        IOptions<TeamsHostOptions> teamsHostOptions)
    {
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(teamsHostOptions);
        _environment = environment;
        _teamsHostOptions = teamsHostOptions;
    }

    public ValidateOptionsResult Validate(
        string? name,
        TeamsUserAuthenticationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var failures = new List<string>();

        if (!Enum.IsDefined(options.Mode))
        {
            failures.Add(
                "TeamsUserAuthentication:Mode must be Entra or "
                + "Development.");
        }
        else if (options.Mode
                 == TeamsUserAuthenticationMode.Development)
        {
            if (!_environment.IsDevelopment())
            {
                failures.Add(
                    "Development Teams user authentication is allowed "
                    + "only in the Development environment.");
            }
        }
        else
        {
            if (string.IsNullOrWhiteSpace(
                    options.OAuthConnectionName))
            {
                failures.Add(
                    "TeamsUserAuthentication:OAuthConnectionName is "
                    + "required in Entra mode.");
            }

            if (_teamsHostOptions.Value.SkipAuth)
            {
                failures.Add(
                    "Teams:SkipAuth must be false in Entra mode.");
            }
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
