using CrmAnalytics.Teams.Configuration;
using Microsoft.Extensions.Options;

namespace CrmAnalytics.Teams.Authentication;

public sealed class TeamsBackendAuthorizationCoordinator
    : ITeamsBackendAuthorizationCoordinator
{
    private readonly TeamsUserAuthenticationMode _mode;

    public TeamsBackendAuthorizationCoordinator(
        IOptions<TeamsUserAuthenticationOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _mode = options.Value.Mode;
    }

    public async Task<BackendAuthorizationResult> AuthorizeAsync(
        Func<CancellationToken, Task<string?>> acquireTeamsUserToken,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(acquireTeamsUserToken);

        if (_mode == TeamsUserAuthenticationMode.Development)
        {
            return BackendAuthorizationResult.Authorized(
                BackendApiAuthorization.Development());
        }

        if (_mode != TeamsUserAuthenticationMode.Entra)
        {
            throw new InvalidOperationException(
                "The Teams user authentication mode is invalid.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        var accessToken = await acquireTeamsUserToken(cancellationToken);

        if (accessToken is null)
        {
            return BackendAuthorizationResult.SignInStarted();
        }

        if (string.IsNullOrWhiteSpace(accessToken))
        {
            throw new InvalidOperationException(
                "Teams sign-in returned an invalid access token.");
        }

        return BackendAuthorizationResult.Authorized(
            BackendApiAuthorization.Bearer(accessToken));
    }
}
