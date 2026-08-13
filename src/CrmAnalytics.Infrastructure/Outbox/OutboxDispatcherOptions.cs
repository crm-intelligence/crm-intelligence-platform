namespace CrmAnalytics.Infrastructure.Outbox;

public sealed class OutboxDispatcherOptions
{
    public const string SectionName = "OutboxDispatcher";
    public bool Enabled { get; set; } = true;
    public int BatchSize { get; set; } = 20;
    public int PollingIntervalSeconds { get; set; } = 2;
    public int LockDurationSeconds { get; set; } = 60;
    public int MaxAttempts { get; set; } = 10;
    public int BaseRetryDelaySeconds { get; set; } = 5;
    public int MaxRetryDelaySeconds { get; set; } = 300;
    public int PublishedRetentionDays { get; set; } = 7;
}
