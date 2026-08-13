namespace CrmAnalytics.Contracts.Conversations;

public sealed record GetConversationReportRequestsResponse(
    string ConversationId,
    string LastRequestId,
    IReadOnlyList<ConversationReportRequestResponse> ReportRequests);
