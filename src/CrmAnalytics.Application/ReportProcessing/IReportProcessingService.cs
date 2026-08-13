namespace CrmAnalytics.Application.ReportProcessing;

public interface IReportProcessingService
{
    Task<ProcessReportRequestResult> ProcessAsync(
        ProcessReportRequestCommand command,
        CancellationToken cancellationToken);
}
