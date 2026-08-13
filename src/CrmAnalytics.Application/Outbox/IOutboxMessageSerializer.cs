namespace CrmAnalytics.Application.Outbox;

public interface IOutboxMessageSerializer
{
    string SerializeReportProcessingRequested(
        ReportProcessingRequestedMessage message);

    ReportProcessingRequestedMessage DeserializeReportProcessingRequested(
        string payloadJson);

    string SerializeReportNotificationRequested(
        ReportNotificationRequestedMessage message);

    ReportNotificationRequestedMessage DeserializeReportNotificationRequested(
        string payloadJson);
}
