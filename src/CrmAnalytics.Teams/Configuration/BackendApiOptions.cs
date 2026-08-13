namespace CrmAnalytics.Teams.Configuration;

public sealed class BackendApiOptions
{
    public const string SectionName = "BackendApi";

    public string BaseUrl { get; init; } = string.Empty;

    public int TimeoutSeconds { get; init; }

    public string InternalApiKey { get; init; } = string.Empty;
}
