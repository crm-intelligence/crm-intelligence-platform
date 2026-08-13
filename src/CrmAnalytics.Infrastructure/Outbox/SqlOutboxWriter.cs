using CrmAnalytics.Application.Outbox;
using CrmAnalytics.Infrastructure.Persistence.SqlServer;
using Microsoft.EntityFrameworkCore;

namespace CrmAnalytics.Infrastructure.Outbox;

public sealed class SqlOutboxWriter : IOutboxWriter
{
    private readonly CrmAnalyticsDbContext _dbContext;

    public SqlOutboxWriter(CrmAnalyticsDbContext dbContext) =>
        _dbContext = dbContext;

    public async Task AppendAsync(
        OutboxMessage message,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        var existing = await _dbContext.Set<OutboxMessageEntity>()
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.MessageId == message.MessageId,
                cancellationToken);
        if (existing is not null)
        {
            EnsureSame(existing, message);
            return;
        }

        var entity = new OutboxMessageEntity
        {
            MessageId = message.MessageId,
            MessageType = message.MessageType.ToString(),
            AggregateId = message.AggregateId,
            OccurredAt = message.OccurredAt,
            PayloadJson = message.PayloadJson,
            Status = OutboxStatus.Pending,
            AttemptCount = 0,
            NextAttemptAt = message.OccurredAt,
            CreatedAt = message.OccurredAt,
            UpdatedAt = message.OccurredAt
        };
        await _dbContext.Set<OutboxMessageEntity>().AddAsync(
            entity, cancellationToken);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
            when (SqlServerExceptionClassifier.IsDuplicateKey(exception))
        {
            _dbContext.Entry(entity).State = EntityState.Detached;
            var duplicate = await _dbContext.Set<OutboxMessageEntity>()
                .AsNoTracking()
                .SingleAsync(item => item.MessageId == message.MessageId,
                    cancellationToken);
            EnsureSame(duplicate, message);
        }
    }

    private static void EnsureSame(
        OutboxMessageEntity entity,
        OutboxMessage message)
    {
        if (!string.Equals(entity.MessageType, message.MessageType.ToString(),
                StringComparison.Ordinal)
            || !string.Equals(entity.AggregateId, message.AggregateId,
                StringComparison.Ordinal)
            || entity.OccurredAt != message.OccurredAt
            || !string.Equals(entity.PayloadJson, message.PayloadJson,
                StringComparison.Ordinal))
        {
            throw new OutboxMessageConflictException();
        }
    }
}
