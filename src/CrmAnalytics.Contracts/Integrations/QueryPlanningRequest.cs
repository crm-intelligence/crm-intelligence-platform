namespace CrmAnalytics.Contracts.Integrations;

public sealed record QueryPlanningRequest(
    string RequestId,
    string Prompt,
    string? PreviousRequestId,
    UserDataScope UserDataScope,
    ConversationContextSnapshot ConversationContext,
    string? ClarificationResponse = null);
