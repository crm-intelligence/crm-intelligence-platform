using System.Collections.Concurrent;
using CrmAnalytics.Application.Abstractions.Persistence;
using CrmAnalytics.Domain.Conversations;

namespace CrmAnalytics.Infrastructure.Persistence;

public sealed class InMemoryConversationRepository
    : IConversationRepository
{
    private readonly ConcurrentDictionary<string, Conversation> _conversations =
        new(StringComparer.OrdinalIgnoreCase);

    public Task AddAsync(
        Conversation conversation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(conversation);
        cancellationToken.ThrowIfCancellationRequested();

        if (!_conversations.TryAdd(
            conversation.TeamsConversationId,
            conversation))
        {
            throw new InvalidOperationException(
                "A conversation with Teams conversation ID "
                    + $"'{conversation.TeamsConversationId}' already exists.");
        }

        return Task.CompletedTask;
    }

    public Task<Conversation?> GetByTeamsConversationIdAsync(
        string teamsConversationId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(teamsConversationId);
        cancellationToken.ThrowIfCancellationRequested();

        _conversations.TryGetValue(
            teamsConversationId.Trim(),
            out var conversation);

        return Task.FromResult<Conversation?>(conversation);
    }

    public Task UpdateAsync(
        Conversation conversation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(conversation);
        cancellationToken.ThrowIfCancellationRequested();

        while (_conversations.TryGetValue(
            conversation.TeamsConversationId,
            out var currentConversation))
        {
            if (!string.Equals(
                currentConversation.Id,
                conversation.Id,
                StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "The conversation identity cannot be changed.");
            }

            if (_conversations.TryUpdate(
                conversation.TeamsConversationId,
                conversation,
                currentConversation))
            {
                return Task.CompletedTask;
            }

            cancellationToken.ThrowIfCancellationRequested();
        }

        throw new KeyNotFoundException(
            "A conversation with Teams conversation ID "
                + $"'{conversation.TeamsConversationId}' was not found.");
    }
}
