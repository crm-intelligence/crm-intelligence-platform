namespace CrmAnalytics.Application.Outbox;

public interface IOutboxWriter
{
    Task AppendAsync(
        OutboxMessage message,
        CancellationToken cancellationToken);
}
