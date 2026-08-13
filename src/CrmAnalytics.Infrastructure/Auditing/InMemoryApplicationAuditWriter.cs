using System.Collections.Concurrent;
using CrmAnalytics.Application.Auditing;

namespace CrmAnalytics.Infrastructure.Auditing;

public sealed class InMemoryApplicationAuditWriter
    : IApplicationAuditWriter
{
    private readonly ConcurrentDictionary<string, ApplicationAuditEvent>
        _events = new(StringComparer.Ordinal);

    public IReadOnlyList<ApplicationAuditEvent> Snapshot =>
        _events.Values.OrderBy(value => value.OccurredAt).ToArray();

    public Task AppendAsync(
        ApplicationAuditEvent auditEvent,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(auditEvent);
        cancellationToken.ThrowIfCancellationRequested();

        var existing = _events.GetOrAdd(
            auditEvent.EventId,
            auditEvent);
        if (existing != auditEvent)
        {
            throw new InvalidOperationException(
                "An audit event persistence conflict occurred.");
        }

        return Task.CompletedTask;
    }
}
