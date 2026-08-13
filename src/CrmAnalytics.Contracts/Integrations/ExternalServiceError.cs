namespace CrmAnalytics.Contracts.Integrations;

public sealed record ExternalServiceError(
    string Code,
    string Message,
    bool IsTransient);
