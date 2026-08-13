using CrmAnalytics.Application.Outbox;

namespace CrmAnalytics.Infrastructure.Outbox;

/// <summary>
/// Append-only facade over the process-local outbox store. Data is lost when
/// the process restarts.
/// </summary>
public sealed class InMemoryOutboxWriter : IOutboxWriter
{
    private readonly InMemoryOutboxStore _store;
    public InMemoryOutboxWriter(InMemoryOutboxStore store) => _store = store;
    public IReadOnlyList<OutboxStateSnapshot> Snapshot => _store.Snapshot;
    public Task AppendAsync(OutboxMessage message,
        CancellationToken cancellationToken) =>
        _store.AppendAsync(message, cancellationToken);
}
