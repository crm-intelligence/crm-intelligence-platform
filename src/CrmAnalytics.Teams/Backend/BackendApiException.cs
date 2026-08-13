using System.Net;

namespace CrmAnalytics.Teams.Backend;

public sealed class BackendApiException : Exception
{
    private const string SafeMessage =
        "The CrmAnalytics backend rejected the report request.";

    public BackendApiException(HttpStatusCode statusCode)
        : base(SafeMessage)
    {
        StatusCode = statusCode;
    }

    public HttpStatusCode StatusCode { get; }
}
