using CrmAnalytics.Domain.Conversations;

namespace CrmAnalytics.Application.Abstractions.Persistence;

public interface IConversationRepository
{
    Task AddAsync(
        Conversation conversation,
        CancellationToken cancellationToken);

    Task<Conversation?> GetByTeamsConversationIdAsync(
        string teamsConversationId,
        CancellationToken cancellationToken);

    Task UpdateAsync(
        Conversation conversation,
        CancellationToken cancellationToken);
}
