using System.Collections.Concurrent;
using System.Net;
using System.Text;
using CrmAnalytics.Contracts.Notifications;
using CrmAnalytics.Infrastructure.Notifications;
using Microsoft.Extensions.Options;

namespace CrmAnalytics.UnitTests;

public sealed class TeamsReportStatusNotificationClientTests
{
    [Fact]
    public async Task Disabled_DoesNotSendHttpRequest()
    {
        var handler = new SequenceHttpMessageHandler(
            _ => new HttpResponseMessage(HttpStatusCode.NoContent));
        using var httpClient = CreateHttpClient(handler);
        var client = CreateClient(
            httpClient,
            new TeamsNotificationOptions { Enabled = false });

        await client.NotifyAsync(
            CreateNotification(),
            CancellationToken.None);

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Enabled_PostsRouteHeaderAndMinimalPayload()
    {
        var handler = new SequenceHttpMessageHandler(
            _ => new HttpResponseMessage(HttpStatusCode.NoContent));
        using var httpClient = CreateHttpClient(handler);
        var options = CreateEnabledOptions();
        var client = CreateClient(httpClient, options);

        await client.NotifyAsync(
            CreateNotification(),
            CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(
            "https://teams.example/api/internal/report-notifications",
            request.Uri.AbsoluteUri);
        Assert.Equal(
            "unit-test-secret",
            request.ApiKey);
        Assert.DoesNotContain(
            "prompt",
            request.Body,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "conversationId",
            request.Body,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"requestId\":\"request-1\"", request.Body);
    }

    [Fact]
    public async Task NoContent_IsSuccessful()
    {
        var handler = new SequenceHttpMessageHandler(
            _ => new HttpResponseMessage(HttpStatusCode.NoContent));
        using var httpClient = CreateHttpClient(handler);
        var client = CreateClient(
            httpClient,
            CreateEnabledOptions());

        await client.NotifyAsync(
            CreateNotification(),
            CancellationToken.None);

        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task TargetNotReadyThenNoContent_RetriesAndSucceeds()
    {
        var call = 0;
        var handler = new SequenceHttpMessageHandler(_ =>
        {
            call++;
            return call == 1
                ? TargetNotReadyResponse()
                : new HttpResponseMessage(HttpStatusCode.NoContent);
        });
        using var httpClient = CreateHttpClient(handler);
        var client = CreateClient(
            httpClient,
            CreateEnabledOptions(
                retryCount: 5,
                initialDelayMilliseconds: 10));

        await client.NotifyAsync(
            CreateNotification(),
            CancellationToken.None);

        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task TargetNotReady_DoesNotExceedRetryLimit()
    {
        var handler = new SequenceHttpMessageHandler(
            _ => TargetNotReadyResponse());
        using var httpClient = CreateHttpClient(handler);
        var client = CreateClient(
            httpClient,
            CreateEnabledOptions(
                retryCount: 2,
                initialDelayMilliseconds: 10));

        await Assert.ThrowsAsync<TeamsNotificationException>(
            () => client.NotifyAsync(
                CreateNotification(),
                CancellationToken.None));

        Assert.Equal(3, handler.Requests.Count);
    }

    [Fact]
    public async Task Unauthorized_IsNotRetried()
    {
        var handler = new SequenceHttpMessageHandler(
            _ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        using var httpClient = CreateHttpClient(handler);
        var client = CreateClient(
            httpClient,
            CreateEnabledOptions(retryCount: 5));

        var exception =
            await Assert.ThrowsAsync<TeamsNotificationException>(
                () => client.NotifyAsync(
                    CreateNotification(),
                    CancellationToken.None));

        Assert.Equal(HttpStatusCode.Unauthorized, exception.StatusCode);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task GenericServerError_IsNotRetried()
    {
        var handler = new SequenceHttpMessageHandler(
            _ => new HttpResponseMessage(
                HttpStatusCode.ServiceUnavailable));
        using var httpClient = CreateHttpClient(handler);
        var client = CreateClient(
            httpClient,
            CreateEnabledOptions(retryCount: 5));

        await Assert.ThrowsAsync<TeamsNotificationException>(
            () => client.NotifyAsync(
                CreateNotification(),
                CancellationToken.None));

        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Cancellation_StopsRetryDelay()
    {
        var handler = new SequenceHttpMessageHandler(
            _ => TargetNotReadyResponse());
        using var httpClient = CreateHttpClient(handler);
        var client = CreateClient(
            httpClient,
            CreateEnabledOptions(
                retryCount: 5,
                initialDelayMilliseconds: 5000));
        using var cancellation = new CancellationTokenSource();

        var task = client.NotifyAsync(
            CreateNotification(),
            cancellation.Token);
        await handler.FirstRequest.WaitAsync(TimeSpan.FromSeconds(2));
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => task);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Exception_DoesNotContainSecretOrResponseBody()
    {
        const string sensitiveBody =
            "secret-response-body prompt=confidential";
        var handler = new SequenceHttpMessageHandler(
            _ => new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent(sensitiveBody)
            });
        using var httpClient = CreateHttpClient(handler);
        var client = CreateClient(
            httpClient,
            CreateEnabledOptions(apiKey: "super-secret-api-key"));

        var exception =
            await Assert.ThrowsAsync<TeamsNotificationException>(
                () => client.NotifyAsync(
                    CreateNotification(),
                    CancellationToken.None));

        Assert.DoesNotContain(
            sensitiveBody,
            exception.ToString(),
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "super-secret-api-key",
            exception.ToString(),
            StringComparison.Ordinal);
        Assert.Empty(exception.Data);
    }

    private static TeamsReportStatusNotificationClient CreateClient(
        HttpClient httpClient,
        TeamsNotificationOptions options) =>
        new(
            new SingleHttpClientFactory(httpClient),
            Options.Create(options));

    private static HttpClient CreateHttpClient(
        HttpMessageHandler handler) =>
        new(handler)
        {
            Timeout = TimeSpan.FromSeconds(10)
        };

    private static TeamsNotificationOptions CreateEnabledOptions(
        int retryCount = 5,
        int initialDelayMilliseconds = 10,
        string apiKey = "unit-test-secret") =>
        new()
        {
            Enabled = true,
            BaseUrl = "https://teams.example",
            ApiKey = apiKey,
            TimeoutSeconds = 10,
            TargetNotReadyRetryCount = retryCount,
            TargetNotReadyInitialDelayMilliseconds =
                initialDelayMilliseconds
        };

    private static ReportStatusNotificationRequest CreateNotification() =>
        new()
        {
            RequestId = "request-1",
            Status = ReportNotificationStatus.Completed,
            UpdatedAt = DateTimeOffset.UtcNow,
            CorrelationId = "correlation-1",
            Summary = "summary",
            PowerBiUrl = "https://app.powerbi.com/report/1"
        };

    private static HttpResponseMessage TargetNotReadyResponse() =>
        new(HttpStatusCode.Conflict)
        {
            Content = new StringContent(
                """
                {
                  "errorCode": "NOTIFICATION_TARGET_NOT_READY",
                  "message": "not ready"
                }
                """,
                Encoding.UTF8,
                "application/json")
        };

    private sealed class SingleHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpClient _httpClient;

        public SingleHttpClientFactory(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        public HttpClient CreateClient(string name) => _httpClient;
    }

    private sealed class SequenceHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<
            HttpRequestMessage,
            HttpResponseMessage> _responseFactory;
        private readonly TaskCompletionSource _firstRequest =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public SequenceHttpMessageHandler(
            Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        {
            _responseFactory = responseFactory;
        }

        public ConcurrentQueue<RecordedRequest> Requests { get; } =
            new();

        public Task FirstRequest => _firstRequest.Task;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(
                    cancellationToken);
            Requests.Enqueue(
                new RecordedRequest(
                    request.Method,
                    request.RequestUri!,
                    request.Headers.TryGetValues(
                        ReportNotificationHttpConstants
                            .ApiKeyHeaderName,
                        out var values)
                        ? Assert.Single(values)
                        : null,
                    body));
            _firstRequest.TrySetResult();
            return _responseFactory(request);
        }
    }

    private sealed record RecordedRequest(
        HttpMethod Method,
        Uri Uri,
        string? ApiKey,
        string Body);
}
