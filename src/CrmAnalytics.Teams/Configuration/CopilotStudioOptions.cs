namespace CrmAnalytics.Teams.Configuration;

public sealed class CopilotStudioOptions
{
    public const string SectionName = "CopilotStudio";

    public bool Enabled { get; init; }

    public string DirectLineSecret { get; init; } = string.Empty;

    public string DirectLineBaseUri { get; init; } = string.Empty;

    public string AgentName { get; init; } = string.Empty;

    public int RequestTimeoutSeconds { get; init; } = 30;
}
