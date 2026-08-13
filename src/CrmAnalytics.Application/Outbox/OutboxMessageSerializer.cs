using System.Text.Json;

namespace CrmAnalytics.Application.Outbox;

public sealed class OutboxMessageSerializer : IOutboxMessageSerializer
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false
    };

    public string SerializeReportProcessingRequested(
        ReportProcessingRequestedMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        return JsonSerializer.Serialize(message, SerializerOptions);
    }

    public ReportProcessingRequestedMessage DeserializeReportProcessingRequested(
        string payloadJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(payloadJson);

        ReportProcessingRequestedMessage? message;
        try
        {
            message = JsonSerializer.Deserialize<ReportProcessingRequestedMessage>(
                payloadJson,
                SerializerOptions);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                "The report processing envelope is invalid.",
                exception);
        }

        return message ?? throw new InvalidDataException(
            "The report processing envelope is empty.");
    }

    public string SerializeReportNotificationRequested(
        ReportNotificationRequestedMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        return JsonSerializer.Serialize(message, SerializerOptions);
    }

    public ReportNotificationRequestedMessage DeserializeReportNotificationRequested(
        string payloadJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(payloadJson);
        try
        {
            return JsonSerializer.Deserialize<ReportNotificationRequestedMessage>(
                payloadJson, SerializerOptions)
                ?? throw new InvalidDataException(
                    "The report notification envelope is empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                "The report notification envelope is invalid.", exception);
        }
    }
}
