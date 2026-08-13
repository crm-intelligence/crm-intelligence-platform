using System.Net;

namespace CrmAnalytics.Infrastructure.Integrations;

internal static class ExternalHttpRetry
{
    public static bool IsTransient(HttpStatusCode statusCode) =>
        statusCode == HttpStatusCode.TooManyRequests
        || (int)statusCode is >= 500 and <= 599;

    public static TimeSpan GetDelay(HttpResponseMessage response, int attempt)
    {
        var retryAfter = response.Headers.RetryAfter;
        if (retryAfter?.Delta is { } delta)
            return Clamp(delta);
        if (retryAfter?.Date is { } date)
            return Clamp(date - DateTimeOffset.UtcNow);
        return TimeSpan.FromMilliseconds(Math.Min(250 * Math.Pow(2, attempt), 5000));
    }

    private static TimeSpan Clamp(TimeSpan value) =>
        value <= TimeSpan.Zero
            ? TimeSpan.Zero
            : value > TimeSpan.FromSeconds(30)
                ? TimeSpan.FromSeconds(30)
                : value;
}
