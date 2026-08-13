namespace CrmAnalytics.Teams.Configuration;

public sealed class TeamsHostOptions
{
    public const string SectionName = "Teams";

    public bool SkipAuth { get; init; }

    public string ClientId { get; init; } = string.Empty;

    public string TenantId { get; init; } = string.Empty;

    public string AppType { get; init; } = string.Empty;

    public string ClientSecret { get; init; } = string.Empty;
}
