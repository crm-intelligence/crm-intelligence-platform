namespace CrmAnalytics.Teams.Authentication;

public interface ITeamsBackendAuthorizationCoordinator
{
    Task<BackendAuthorizationResult> AuthorizeAsync(
        Func<CancellationToken, Task<string?>> acquireTeamsUserToken,
        CancellationToken cancellationToken);
}
