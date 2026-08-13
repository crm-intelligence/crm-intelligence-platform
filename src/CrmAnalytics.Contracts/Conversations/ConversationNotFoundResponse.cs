namespace CrmAnalytics.Contracts.Conversations;

public sealed record ConversationNotFoundResponse(
    string ConversationId,
    string Message);
