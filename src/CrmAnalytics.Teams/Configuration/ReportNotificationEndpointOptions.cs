namespace CrmAnalytics.Teams.Configuration;

public sealed class ReportNotificationEndpointOptions
{
    public const string SectionName = "ReportNotifications";

    public bool Enabled { get; init; }

    public string ApiKey { get; init; } = string.Empty;
}
