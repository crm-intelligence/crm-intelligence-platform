namespace CrmAnalytics.Application.Outbox;

public sealed record OutboxMessage
{
    public OutboxMessage(
        string messageId,
        OutboxMessageType messageType,
        string aggregateId,
        DateTimeOffset occurredAt,
        string payloadJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(messageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(aggregateId);
        ArgumentException.ThrowIfNullOrWhiteSpace(payloadJson);

        if (messageId.Trim().Length > 64)
        {
            throw new ArgumentException(
                "The outbox message ID cannot exceed 64 characters.",
                nameof(messageId));
        }

        if (!Enum.IsDefined(messageType))
        {
            throw new ArgumentOutOfRangeException(nameof(messageType));
        }

        if (occurredAt.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException(
                "The outbox occurrence time must be in UTC.",
                nameof(occurredAt));
        }

        MessageId = messageId.Trim();
        MessageType = messageType;
        AggregateId = aggregateId.Trim();
        OccurredAt = occurredAt;
        PayloadJson = payloadJson;
    }

    public string MessageId { get; }
    public OutboxMessageType MessageType { get; }
    public string AggregateId { get; }
    public DateTimeOffset OccurredAt { get; }
    public string PayloadJson { get; }

    public override string ToString() =>
        $"OutboxMessage {{ MessageType = {MessageType}, Redacted }}";
}
