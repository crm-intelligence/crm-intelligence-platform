using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using CrmAnalytics.Contracts.Notifications;
using CrmAnalytics.Contracts.Integrations;

namespace CrmAnalytics.Application.Outbox;

public sealed class OutboxMessageFactory
{
    private readonly IOutboxMessageSerializer _serializer;

    public OutboxMessageFactory(IOutboxMessageSerializer serializer)
    {
        ArgumentNullException.ThrowIfNull(serializer);
        _serializer = serializer;
    }

    public OutboxMessage CreateReportProcessingRequested(
        string requestId,
        string correlationId,
        DateTimeOffset requestedAt,
        string processingGeneration,
        SubmittedSemanticPlanningResult? semanticPlan = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(processingGeneration);
        var envelope = new ReportProcessingRequestedMessage(
            requestId,
            correlationId,
            requestedAt,
            semanticPlan is null
                ? ReportProcessingRequestedMessage.LegacySchemaVersion
                : ReportProcessingRequestedMessage.CurrentSchemaVersion,
            semanticPlan);
        var messageId = CreateMessageId(
            requestId,
            processingGeneration,
            requestedAt);

        return new OutboxMessage(
            messageId,
            OutboxMessageType.ReportProcessingRequested,
            envelope.RequestId,
            requestedAt,
            _serializer.SerializeReportProcessingRequested(envelope));
    }

    public static string CreateMessageId(
        string requestId,
        string processingGeneration,
        DateTimeOffset requestedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestId);
        ArgumentException.ThrowIfNullOrWhiteSpace(processingGeneration);
        if (requestedAt.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException(
                "The requested time must be in UTC.",
                nameof(requestedAt));
        }

        var input = string.Join(
            "|",
            nameof(OutboxMessageType.ReportProcessingRequested),
            requestId.Trim(),
            processingGeneration.Trim(),
            requestedAt.UtcTicks.ToString(CultureInfo.InvariantCulture));
        return Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(input)));
    }

    public OutboxMessage CreateReportNotificationRequested(
        string requestId,
        ReportNotificationStatus status,
        DateTimeOffset reportUpdatedAt,
        ReportVisualizationPreview? visualizationPreview = null)
    {
        var envelope = new ReportNotificationRequestedMessage(
            requestId, status, reportUpdatedAt,
            ReportNotificationRequestedMessage.CurrentSchemaVersion,
            visualizationPreview);
        return new OutboxMessage(
            CreateNotificationMessageId(requestId, status, reportUpdatedAt),
            OutboxMessageType.ReportNotificationRequested,
            envelope.RequestId,
            reportUpdatedAt,
            _serializer.SerializeReportNotificationRequested(envelope));
    }

    public static string CreateNotificationMessageId(
        string requestId,
        ReportNotificationStatus status,
        DateTimeOffset reportUpdatedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestId);
        if (reportUpdatedAt.Offset != TimeSpan.Zero)
            throw new ArgumentException("The report time must be UTC.",
                nameof(reportUpdatedAt));
        var input = string.Join("|",
            nameof(OutboxMessageType.ReportNotificationRequested),
            requestId.Trim(), status.ToString(),
            reportUpdatedAt.UtcTicks.ToString(CultureInfo.InvariantCulture));
        return Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(input)));
    }
}
