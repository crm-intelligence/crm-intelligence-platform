using CrmAnalytics.Application.Identity;

namespace CrmAnalytics.Application.Conversations;

public interface IConversationContextService
{
    Task RegisterRequestAsync(
        string teamsConversationId,
        string requestId,
        DateTimeOffset registeredAt,
        string? userId,
        CancellationToken cancellationToken);

    Task RegisterAuthenticatedRequestAsync(
        string teamsConversationId,
        string requestId,
        DateTimeOffset registeredAt,
        AuthenticatedUserContext user,
        CancellationToken cancellationToken) =>
        RegisterRequestAsync(
            teamsConversationId,
            requestId,
            registeredAt,
            user.UserId,
            cancellationToken);

    Task<string?> GetLastRequestIdAsync(
        string teamsConversationId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<ConversationReportRequestHistoryItem>> GetHistoryAsync(
        string teamsConversationId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<ConversationReportRequestHistoryItem>> GetHistoryAsync(
        string teamsConversationId,
        AuthenticatedUserContext user,
        CancellationToken cancellationToken) =>
        GetHistoryAsync(teamsConversationId, cancellationToken);
}
