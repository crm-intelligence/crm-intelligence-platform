using CrmAnalytics.Contracts.Integrations;

namespace CrmAnalytics.Application.Abstractions.Integrations;

public interface IReportClient
{
    Task<ReportGenerationResponse> GenerateAsync(
        ReportGenerationRequest request,
        CancellationToken cancellationToken);
}
