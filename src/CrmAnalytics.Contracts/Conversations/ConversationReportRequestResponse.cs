namespace CrmAnalytics.Contracts.Conversations;

public sealed record ConversationReportRequestResponse(
    string RequestId,
    string? PreviousRequestId,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string? Summary,
    string? PowerBiUrl,
    string? ClarificationQuestion,
    string? RejectionMessage = null);
