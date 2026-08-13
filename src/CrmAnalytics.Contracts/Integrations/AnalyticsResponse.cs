namespace CrmAnalytics.Contracts.Integrations;

public sealed record AnalyticsResponse(
    string JobId,
    ExternalOperationStatus Status,
    string? ResultReference,
    string? Summary,
    ExternalServiceError? Error);
