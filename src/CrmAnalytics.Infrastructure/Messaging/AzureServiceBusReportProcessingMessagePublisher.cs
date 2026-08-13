using Azure.Messaging.ServiceBus;
using CrmAnalytics.Application.Outbox;

namespace CrmAnalytics.Infrastructure.Messaging;

public sealed class AzureServiceBusReportProcessingMessagePublisher
    : IReportProcessingMessagePublisher
{
    public const string Subject = "report-processing-requested.v1";
    public const string ContentType = "application/json";
    private readonly ServiceBusSender _sender;
    private readonly IOutboxMessageSerializer _serializer;

    public AzureServiceBusReportProcessingMessagePublisher(
        ServiceBusSender sender,
        IOutboxMessageSerializer serializer)
    {
        _sender = sender;
        _serializer = serializer;
    }

    public Task PublishAsync(ReportProcessingRequestedMessage message,
        string messageId, CancellationToken cancellationToken) =>
        _sender.SendMessageAsync(
            CreateServiceBusMessage(message, messageId, _serializer),
            cancellationToken);

    public static ServiceBusMessage CreateServiceBusMessage(
        ReportProcessingRequestedMessage message,
        string messageId,
        IOutboxMessageSerializer serializer)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentException.ThrowIfNullOrWhiteSpace(messageId);
        ArgumentNullException.ThrowIfNull(serializer);
        var brokerMessage = new ServiceBusMessage(
            BinaryData.FromString(
                serializer.SerializeReportProcessingRequested(message)))
        {
            MessageId = messageId.Trim(),
            Subject = Subject,
            ContentType = ContentType,
            CorrelationId = message.CorrelationId
        };
        brokerMessage.ApplicationProperties["schemaVersion"] =
            message.SchemaVersion;
        brokerMessage.ApplicationProperties["messageType"] =
            OutboxMessageType.ReportProcessingRequested.ToString();
        return brokerMessage;
    }
}
