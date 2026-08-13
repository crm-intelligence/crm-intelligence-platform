using CrmAnalytics.Application.Abstractions.Persistence;
using CrmAnalytics.Application.Exceptions;
using CrmAnalytics.Domain.Conversations;
using Microsoft.EntityFrameworkCore;

namespace CrmAnalytics.Infrastructure.Persistence.SqlServer;

public sealed class SqlConversationRepository
    : IConversationRepository
{
    private readonly CrmAnalyticsDbContext _dbContext;

    public SqlConversationRepository(CrmAnalyticsDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task AddAsync(
        Conversation conversation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(conversation);

        await _dbContext.Conversations.AddAsync(
            conversation,
            cancellationToken);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
            when (SqlServerExceptionClassifier.IsDuplicateKey(exception))
        {
            _dbContext.Entry(conversation).State = EntityState.Detached;
            throw new InvalidOperationException(
                "A conversation with the same Teams conversation "
                    + "identity already exists.");
        }
    }

    public async Task<Conversation?> GetByTeamsConversationIdAsync(
        string teamsConversationId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(teamsConversationId);
        var normalizedTeamsConversationId =
            teamsConversationId.Trim();

        return await _dbContext.Conversations.SingleOrDefaultAsync(
            conversation =>
                conversation.TeamsConversationId ==
                    normalizedTeamsConversationId,
            cancellationToken);
    }

    public async Task UpdateAsync(
        Conversation conversation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(conversation);

        var entry = _dbContext.Entry(conversation);

        if (entry.State == EntityState.Detached)
        {
            var trackedConversation =
                await _dbContext.Conversations.SingleOrDefaultAsync(
                    candidate => candidate.Id == conversation.Id,
                    cancellationToken);

            if (trackedConversation is null)
            {
                throw new KeyNotFoundException(
                    "The conversation was not found.");
            }

            if (!string.Equals(
                trackedConversation.TeamsConversationId,
                conversation.TeamsConversationId,
                StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "The conversation identity cannot be changed.");
            }

            _dbContext.Entry(trackedConversation)
                .CurrentValues.SetValues(conversation);
        }

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new PersistenceConcurrencyException(exception);
        }
    }
}
