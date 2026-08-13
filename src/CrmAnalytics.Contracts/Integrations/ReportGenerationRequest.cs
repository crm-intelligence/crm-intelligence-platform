namespace CrmAnalytics.Contracts.Integrations;

public sealed record ReportGenerationRequest(
    string RequestId,
    string ReportType,
    string ResultReference,
    UserDataScope? UserDataScope = null);
