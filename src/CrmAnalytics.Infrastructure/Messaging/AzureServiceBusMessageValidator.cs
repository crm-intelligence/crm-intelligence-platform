using Azure.Messaging.ServiceBus;
using CrmAnalytics.Application.Outbox;
using CrmAnalytics.Infrastructure.ReportProcessing;

namespace CrmAnalytics.Infrastructure.Messaging;

public sealed class AzureServiceBusMessageValidator
{
    private readonly IOutboxMessageSerializer _serializer;
    public AzureServiceBusMessageValidator(IOutboxMessageSerializer serializer) =>
        _serializer = serializer;

    public ReportProcessingRequestedMessage ValidateAndDeserialize(
        ServiceBusReceivedMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (string.IsNullOrWhiteSpace(message.MessageId))
            throw new InvalidDataException("MessageId is required.");
        if (!string.Equals(message.ContentType,
                AzureServiceBusReportProcessingMessagePublisher.ContentType,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("ContentType is invalid.");
        if (!string.Equals(message.Subject,
                AzureServiceBusReportProcessingMessagePublisher.Subject,
                StringComparison.Ordinal))
            throw new InvalidDataException("Subject is invalid.");
        if (!message.ApplicationProperties.TryGetValue(
                "messageType", out var messageType)
            || !string.Equals(messageType?.ToString(),
                OutboxMessageType.ReportProcessingRequested.ToString(),
                StringComparison.Ordinal))
            throw new ReportProcessingPermanentException("UnknownMessageType");
        if (!message.ApplicationProperties.TryGetValue(
                "schemaVersion", out var schemaVersion)
            || !int.TryParse(schemaVersion?.ToString(), out var version)
            || !ReportProcessingRequestedMessage.IsSupportedSchemaVersion(
                version))
            throw new NotSupportedException("Schema version is unsupported.");
        var envelope = _serializer.DeserializeReportProcessingRequested(
            message.Body.ToString());
        if (envelope.SchemaVersion != version)
            throw new InvalidDataException(
                "Envelope schema version does not match broker metadata.");
        return envelope;
    }
}
