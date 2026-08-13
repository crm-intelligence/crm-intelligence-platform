namespace CrmAnalytics.Infrastructure.Outbox;

internal static class OutboxStatus
{
    public const string Pending = "Pending";
    public const string Processing = "Processing";
    public const string Published = "Published";
    public const string DeadLettered = "DeadLettered";
}
