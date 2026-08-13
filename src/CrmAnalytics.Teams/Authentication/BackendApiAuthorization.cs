using System.Text.Json.Serialization;

namespace CrmAnalytics.Teams.Authentication;

public sealed class BackendApiAuthorization
{
    private BackendApiAuthorization(
        bool requiresBearerToken,
        string? accessToken)
    {
        RequiresBearerToken = requiresBearerToken;
        AccessToken = accessToken;
    }

    public bool RequiresBearerToken { get; }

    [JsonIgnore]
    public string? AccessToken { get; }

    public static BackendApiAuthorization Development() =>
        new(false, null);

    public static BackendApiAuthorization Bearer(string accessToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);
        return new BackendApiAuthorization(true, accessToken);
    }

    public override string ToString() =>
        RequiresBearerToken
            ? "BackendApiAuthorization { RequiresBearerToken = True }"
            : "BackendApiAuthorization { RequiresBearerToken = False }";
}
