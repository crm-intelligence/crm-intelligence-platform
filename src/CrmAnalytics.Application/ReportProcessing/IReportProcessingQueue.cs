namespace CrmAnalytics.Application.ReportProcessing;

public interface IReportProcessingQueue
{
    ValueTask EnqueueAsync(
        QueuedReportProcessingRequest request,
        CancellationToken cancellationToken);

    ValueTask<QueuedReportProcessingRequest> DequeueAsync(
        CancellationToken cancellationToken);
}
