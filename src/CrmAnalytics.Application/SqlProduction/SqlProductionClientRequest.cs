using CrmAnalytics.Contracts.Integrations;

namespace CrmAnalytics.Application.SqlProduction;

public sealed record SqlProductionClientRequest(
    string RequestId,
    string ConversationId,
    string Prompt,
    DateOnly Today,
    UserDataScope UserDataScope,
    string? PreviousCanonicalRequestJson,
    string? UserId,
    SqlDataSource Source = SqlDataSource.Unknown,
    string? OriginalPrompt = null,
    string? ClarificationQuestion = null,
    string? ClarificationAnswer = null,
    SubmittedSemanticPlanningResult? SemanticPlan = null);

public enum SqlDataSource
{
    Unknown = 0,
    Dwh = 1,
    Oltp = 2
}
