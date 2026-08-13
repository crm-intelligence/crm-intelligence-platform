using CrmAnalytics.Contracts.Notifications;

namespace CrmAnalytics.Application.ReportRequests;

public sealed record CompleteReportRequestCommand(
    string RequestId,
    string ReportId,
    string Summary,
    string PowerBiUrl,
    DateTimeOffset CompletedAt,
    ReportVisualizationPreview? VisualizationPreview = null);
