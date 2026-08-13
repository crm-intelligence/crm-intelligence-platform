using CrmAnalytics.Application.Auditing;
using CrmAnalytics.Application.Exceptions;
using CrmAnalytics.Infrastructure.Persistence.SqlServer;
using Microsoft.EntityFrameworkCore;

namespace CrmAnalytics.Infrastructure.Auditing;

public sealed class SqlApplicationAuditWriter : IApplicationAuditWriter
{
    private readonly CrmAnalyticsDbContext _dbContext;

    public SqlApplicationAuditWriter(CrmAnalyticsDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task AppendAsync(
        ApplicationAuditEvent auditEvent,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(auditEvent);
        var entities = _dbContext.Set<ApplicationAuditEventEntity>();
        var existing = await entities.AsNoTracking().SingleOrDefaultAsync(
            value => value.EventId == auditEvent.EventId,
            cancellationToken);

        if (existing is not null)
        {
            if (!HasSameValues(existing, auditEvent))
            {
                throw new InvalidOperationException(
                    "An audit event persistence conflict occurred.");
            }

            return;
        }

        var entity = Map(auditEvent);
        entities.Add(entity);
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
            when (SqlServerExceptionClassifier.IsDuplicateKey(exception))
        {
            _dbContext.Entry(entity).State = EntityState.Detached;
            existing = await entities.AsNoTracking().SingleOrDefaultAsync(
                value => value.EventId == auditEvent.EventId,
                cancellationToken);
            if (existing is null || !HasSameValues(existing, auditEvent))
            {
                throw new InvalidOperationException(
                    "An audit event persistence conflict occurred.");
            }
        }
        catch (DbUpdateException)
        {
            throw new ApplicationAuditPersistenceException();
        }
    }

    private static ApplicationAuditEventEntity Map(
        ApplicationAuditEvent value) => new()
    {
        EventId = value.EventId,
        EventType = value.EventType.ToString(),
        Outcome = value.Outcome.ToString(),
        OccurredAt = value.OccurredAt,
        RequestId = value.RequestId,
        PreviousRequestId = value.PreviousRequestId,
        CorrelationId = value.CorrelationId,
        ActorUserId = value.ActorUserId,
        TenantId = value.TenantId,
        ReportStatus = value.ReportStatus,
        ReasonCode = value.ReasonCode,
        DataSource = value.DataSource,
        DurationMilliseconds = value.DurationMilliseconds,
        RowCount = value.RowCount,
        ResultTruncated = value.ResultTruncated,
        AuditMetadataJson = value.AuditMetadata is null
            ? null
            : ApplicationAuditMetadataSerializer.Serialize(
                value.AuditMetadata)
    };

    private static bool HasSameValues(
        ApplicationAuditEventEntity left,
        ApplicationAuditEvent right) =>
        left.EventType == right.EventType.ToString()
        && left.Outcome == right.Outcome.ToString()
        && left.OccurredAt == right.OccurredAt
        && left.RequestId == right.RequestId
        && left.PreviousRequestId == right.PreviousRequestId
        && left.CorrelationId == right.CorrelationId
        && left.ActorUserId == right.ActorUserId
        && left.TenantId == right.TenantId
        && left.ReportStatus == right.ReportStatus
        && left.ReasonCode == right.ReasonCode
        && left.DataSource == right.DataSource
        && left.DurationMilliseconds == right.DurationMilliseconds
        && left.RowCount == right.RowCount
        && left.ResultTruncated == right.ResultTruncated
        && left.AuditMetadataJson == (right.AuditMetadata is null
            ? null
            : ApplicationAuditMetadataSerializer.Serialize(
                right.AuditMetadata));
}
