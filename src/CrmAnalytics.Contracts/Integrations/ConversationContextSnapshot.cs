namespace CrmAnalytics.Contracts.Integrations;

public sealed record ConversationContextSnapshot(
    string ConversationId,
    string? PreviousRequestId,
    string? PreviousSummary,
    string? PreviousPowerBiUrl);
