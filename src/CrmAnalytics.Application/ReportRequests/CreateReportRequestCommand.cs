namespace CrmAnalytics.Application.ReportRequests;

public sealed record CreateReportRequestCommand(
    string Prompt,
    string ConversationId,
    string? PreviousRequestId,
    string CorrelationId);