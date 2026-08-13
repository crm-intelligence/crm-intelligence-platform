using CrmAnalytics.Application.SqlProduction;
using CrmAnalytics.Contracts.Integrations;

namespace CrmAnalytics.Application.Abstractions.Integrations;

public interface IPowerBiReportRouter
{
    PowerBiReportRoute Route(
        SqlDataSource source,
        SqlResultShapeMetadata? resultShape,
        SubmittedSemanticPlanningResult? semanticPlan);
}

public sealed record PowerBiReportRoute(
    string ReportId,
    string PageId,
    string Url);
