using Azure.Messaging.ServiceBus;
using CrmAnalytics.Infrastructure.Notifications;
using System.Net;

namespace CrmAnalytics.Infrastructure.Messaging;

public enum MessagePublishFailureCategory
{
    Transient,
    Permanent
}

public sealed record MessagePublishFailure(
    MessagePublishFailureCategory Category,
    string FailureCode);

public interface IMessagePublishFailureClassifier
{
    MessagePublishFailure Classify(Exception exception);
}

public sealed class MessagePublishFailureClassifier
    : IMessagePublishFailureClassifier
{
    public MessagePublishFailure Classify(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return exception switch
        {
            ServiceBusException serviceBusException
                when serviceBusException.IsTransient =>
                new(MessagePublishFailureCategory.Transient,
                    "SERVICE_BUS_TRANSIENT"),
            ServiceBusException =>
                new(MessagePublishFailureCategory.Permanent,
                    "SERVICE_BUS_PERMANENT"),
            InvalidDataException or NotSupportedException
                or ArgumentException =>
                new(MessagePublishFailureCategory.Permanent,
                    "INVALID_OUTBOX_PAYLOAD"),
            TeamsNotificationException notification
                when notification.StatusCode is HttpStatusCode.BadRequest
                    or HttpStatusCode.Unauthorized
                    or HttpStatusCode.Forbidden
                    or HttpStatusCode.NotFound =>
                new(MessagePublishFailureCategory.Permanent,
                    "TEAMS_NOTIFICATION_PERMANENT"),
            TeamsNotificationException =>
                new(MessagePublishFailureCategory.Transient,
                    "TEAMS_NOTIFICATION_TRANSIENT"),
            _ => new(MessagePublishFailureCategory.Transient,
                "SERVICE_BUS_TRANSIENT")
        };
    }
}
