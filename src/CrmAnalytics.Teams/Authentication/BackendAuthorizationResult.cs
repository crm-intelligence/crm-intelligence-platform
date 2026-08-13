namespace CrmAnalytics.Teams.Authentication;

public enum BackendAuthorizationStatus
{
    Authorized,
    SignInStarted
}

public sealed class BackendAuthorizationResult
{
    private BackendAuthorizationResult(
        BackendAuthorizationStatus status,
        BackendApiAuthorization? authorization)
    {
        Status = status;
        Authorization = authorization;
    }

    public BackendAuthorizationStatus Status { get; }

    public BackendApiAuthorization? Authorization { get; }

    public bool IsAuthorized =>
        Status == BackendAuthorizationStatus.Authorized;

    public static BackendAuthorizationResult Authorized(
        BackendApiAuthorization authorization)
    {
        ArgumentNullException.ThrowIfNull(authorization);
        return new BackendAuthorizationResult(
            BackendAuthorizationStatus.Authorized,
            authorization);
    }

    public static BackendAuthorizationResult SignInStarted() =>
        new(BackendAuthorizationStatus.SignInStarted, null);

    public override string ToString() =>
        $"BackendAuthorizationResult {{ Status = {Status} }}";
}
