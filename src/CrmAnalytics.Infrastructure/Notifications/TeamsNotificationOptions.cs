namespace CrmAnalytics.Infrastructure.Notifications;

public sealed class TeamsNotificationOptions
{
    public const string SectionName = "TeamsNotifications";

    public bool Enabled { get; init; }

    public string BaseUrl { get; init; } = "http://localhost:3978";

    public string ApiKey { get; init; } = string.Empty;

    public int TimeoutSeconds { get; init; } = 10;

    public int TargetNotReadyRetryCount { get; init; } = 5;

    public int TargetNotReadyInitialDelayMilliseconds { get; init; } = 100;
}
