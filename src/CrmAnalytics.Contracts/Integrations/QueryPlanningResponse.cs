namespace CrmAnalytics.Contracts.Integrations;

public sealed record QueryPlanningResponse(
    ExternalOperationStatus Status,
    string? CanonicalRequest,
    string? ClarificationQuestion,
    string? GeneratedQueryReference,
    ExternalServiceError? Error);
