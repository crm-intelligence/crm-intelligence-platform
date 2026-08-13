using System.Net;

namespace CrmAnalytics.Infrastructure.Notifications;

public sealed class TeamsNotificationException : Exception
{
    public TeamsNotificationException(HttpStatusCode statusCode)
        : base(
            "The Teams report notification callback returned an "
            + "unsuccessful response.")
    {
        StatusCode = statusCode;
    }

    public TeamsNotificationException(Exception innerException)
        : base(
            "The Teams report notification callback could not be "
            + "completed.")
    {
        ArgumentNullException.ThrowIfNull(innerException);
    }

    public HttpStatusCode? StatusCode { get; }
}
