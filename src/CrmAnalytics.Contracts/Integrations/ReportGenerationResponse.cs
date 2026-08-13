namespace CrmAnalytics.Contracts.Integrations;

public sealed record ReportGenerationResponse(
    string? ReportId,
    string? PageName,
    string? PowerBiUrl,
    ExternalOperationStatus Status,
    ExternalServiceError? Error);
