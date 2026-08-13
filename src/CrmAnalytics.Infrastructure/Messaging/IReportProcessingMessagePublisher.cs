using CrmAnalytics.Application.Outbox;

namespace CrmAnalytics.Infrastructure.Messaging;

public interface IReportProcessingMessagePublisher
{
    Task PublishAsync(ReportProcessingRequestedMessage message,
        string messageId, CancellationToken cancellationToken);
}
