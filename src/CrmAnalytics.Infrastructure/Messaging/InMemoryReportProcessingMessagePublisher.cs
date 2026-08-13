using CrmAnalytics.Application.Outbox;
using CrmAnalytics.Application.ReportProcessing;

namespace CrmAnalytics.Infrastructure.Messaging;

public sealed class InMemoryReportProcessingMessagePublisher
    : IReportProcessingMessagePublisher
{
    private readonly IReportProcessingQueue _queue;
    public InMemoryReportProcessingMessagePublisher(IReportProcessingQueue queue) =>
        _queue = queue;

    public async Task PublishAsync(ReportProcessingRequestedMessage message,
        string messageId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentException.ThrowIfNullOrWhiteSpace(messageId);
        await _queue.EnqueueAsync(new QueuedReportProcessingRequest(
            message.RequestId,
            message.CorrelationId,
            message.RequestedAt,
            messageId,
            message.SemanticPlan), cancellationToken);
    }
}
