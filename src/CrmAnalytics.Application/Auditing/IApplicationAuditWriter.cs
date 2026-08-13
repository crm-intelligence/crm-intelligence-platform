namespace CrmAnalytics.Application.Auditing;

public interface IApplicationAuditWriter
{
    Task AppendAsync(
        ApplicationAuditEvent auditEvent,
        CancellationToken cancellationToken);
}
