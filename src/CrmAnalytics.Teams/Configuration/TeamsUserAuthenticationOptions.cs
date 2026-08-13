namespace CrmAnalytics.Teams.Configuration;

public enum TeamsUserAuthenticationMode
{
    Development,
    Entra
}

public sealed class TeamsUserAuthenticationOptions
{
    public const string SectionName = "TeamsUserAuthentication";

    public TeamsUserAuthenticationMode Mode { get; init; } =
        TeamsUserAuthenticationMode.Entra;

    public string OAuthConnectionName { get; init; } = string.Empty;
}
