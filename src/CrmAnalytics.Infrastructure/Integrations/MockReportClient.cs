using CrmAnalytics.Application.Abstractions.Integrations;
using CrmAnalytics.Contracts.Integrations;
using Microsoft.Extensions.Options;

namespace CrmAnalytics.Infrastructure.Integrations;

public sealed class MockReportClient : IReportClient
{
    private readonly MockExternalServicesOptions _options;

    public MockReportClient(
        IOptions<MockExternalServicesOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;
    }

    public async Task<ReportGenerationResponse> GenerateAsync(
        ReportGenerationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        await Task.Delay(
            _options.DelayMilliseconds,
            cancellationToken);

        if (_options.ForceReportFailure)
        {
            return new ReportGenerationResponse(
                ReportId: null,
                PageName: null,
                PowerBiUrl: null,
                Status: ExternalOperationStatus.Failed,
                Error: new ExternalServiceError(
                    Code: "REPORT_GENERATION_FAILED",
                    Message: "Report generation could not be completed.",
                    IsTransient: true));
        }

        var escapedRequestId = Uri.EscapeDataString(request.RequestId);
        var powerBiUrl =
            $"{_options.PowerBiBaseUrl.TrimEnd('/')}/{escapedRequestId}";

        return new ReportGenerationResponse(
            ReportId: $"mock-report-{request.RequestId}",
            PageName: "ReportSection",
            PowerBiUrl: powerBiUrl,
            Status: ExternalOperationStatus.Completed,
            Error: null);
    }
}
