namespace CrmAnalytics.Application.Outbox;

public sealed class OutboxMessageConflictException : InvalidOperationException
{
    public OutboxMessageConflictException()
        : base("An outbox message with conflicting content already exists.")
    {
    }
}
