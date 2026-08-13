using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using CrmAnalytics.Contracts.CopilotStudio;
using CrmAnalytics.Contracts.ReportRequests;
using CrmAnalytics.Teams.Authentication;
using CrmAnalytics.Teams.Backend;

namespace CrmAnalytics.UnitTests;

public sealed class ReportRequestsApiClientTests
{
    [Fact]
    public async Task CreatePlannedAsync_PostsSemanticContractToPlannedEndpoint()
    {
        var handler = new RecordingHttpMessageHandler(
            _ => AcceptedCreateResponse());
        using var httpClient = CreateHttpClient(handler);
        var client = new ReportRequestsApiClient(httpClient);

        var response = await client.CreatePlannedAsync(
            CreatePlannedRequest(),
            BackendApiAuthorization.Development(),
            CancellationToken.None);

        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.Equal(
            "https://backend.example/api/report-requests/planned",
            handler.RequestUri?.AbsoluteUri);
        Assert.Equal("request-123", response.RequestId);

        using var document = JsonDocument.Parse(handler.RequestBody!);
        Assert.Equal(
            "accepted",
            document.RootElement.GetProperty("outcome").GetString());
        Assert.Equal(
            "order_count",
            document.RootElement
                .GetProperty("semanticIntent")
                .GetProperty("metric")
                .GetString());
    }

    [Fact]
    public async Task CreatePlannedAsync_PreservesTeamsBearerOnBackendRequest()
    {
        var handler = new RecordingHttpMessageHandler(
            _ => AcceptedCreateResponse());
        using var httpClient = CreateHttpClient(handler);
        var client = new ReportRequestsApiClient(httpClient);

        await client.CreatePlannedAsync(
            CreatePlannedRequest(),
            BackendApiAuthorization.Bearer("opaque-teams-token"),
            CancellationToken.None);

        Assert.Equal("Bearer", handler.Authorization?.Scheme);
        Assert.Equal(
            "opaque-teams-token",
            handler.Authorization?.Parameter);
        Assert.Null(httpClient.DefaultRequestHeaders.Authorization);
    }

    [Fact]
    public async Task CreateAsync_PostsExpectedContractAndDeserializesAcceptedResponse()
    {
        var handler = new RecordingHttpMessageHandler(
            _ => new HttpResponseMessage(HttpStatusCode.Accepted)
            {
                Content = new StringContent(
                    """
                    {
                      "requestId": "request-123",
                      "status": "Received",
                      "message": "accepted"
                    }
                    """,
                    Encoding.UTF8,
                    "application/json")
            });
        using var httpClient = CreateHttpClient(handler);
        var client = new ReportRequestsApiClient(httpClient);

        var response = await client.CreateAsync(
            new CreateReportRequestRequest
            {
                Prompt = "  satış raporu  ",
                ConversationId = "conversation-42",
                PreviousRequestId = null
            },
            BackendApiAuthorization.Development(),
            CancellationToken.None);

        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.Equal(
            "https://backend.example/api/report-requests",
            handler.RequestUri?.AbsoluteUri);
        Assert.Equal("request-123", response.RequestId);
        Assert.Equal("Received", response.Status);
        Assert.Null(handler.Authorization);

        using var document = JsonDocument.Parse(
            Assert.IsType<string>(handler.RequestBody));
        var root = document.RootElement;
        Assert.Equal(
            "  satış raporu  ",
            root.GetProperty("prompt").GetString());
        Assert.Equal(
            "conversation-42",
            root.GetProperty("conversationId").GetString());
        Assert.Equal(
            JsonValueKind.Null,
            root.GetProperty("previousRequestId").ValueKind);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task CreateAsync_NonAcceptedResponseThrowsSafeException(
        HttpStatusCode statusCode)
    {
        const string sensitiveBody =
            "database=secret; prompt=confidential";
        var handler = new RecordingHttpMessageHandler(
            _ => new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(sensitiveBody)
            });
        using var httpClient = CreateHttpClient(handler);
        var client = new ReportRequestsApiClient(httpClient);
        const string accessToken = "opaque-sensitive-token";

        var exception = await Assert.ThrowsAsync<BackendApiException>(
            () => client.CreateAsync(
                CreateRequest(),
                BackendApiAuthorization.Bearer(accessToken),
                CancellationToken.None));

        Assert.Equal(statusCode, exception.StatusCode);
        Assert.DoesNotContain(
            sensitiveBody,
            exception.Message,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "confidential",
            exception.ToString(),
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            accessToken,
            exception.ToString(),
            StringComparison.Ordinal);
        Assert.Empty(exception.Data);
    }

    [Fact]
    public async Task CreateAsync_AddsBearerOnlyToThatRequest()
    {
        var handler = new RecordingHttpMessageHandler(
            _ => AcceptedCreateResponse());
        using var httpClient = CreateHttpClient(handler);
        var client = new ReportRequestsApiClient(httpClient);

        await client.CreateAsync(
            CreateRequest(),
            BackendApiAuthorization.Bearer("opaque-token"),
            CancellationToken.None);

        Assert.Equal("Bearer", handler.Authorization?.Scheme);
        Assert.Equal(
            "opaque-token",
            handler.Authorization?.Parameter);
        Assert.Null(httpClient.DefaultRequestHeaders.Authorization);
    }

    [Fact]
    public async Task ConcurrentCalls_DoNotLeakBearerTokens()
    {
        var handler = new ConcurrentRecordingHandler();
        using var httpClient = CreateHttpClient(handler);
        var client = new ReportRequestsApiClient(httpClient);

        await Task.WhenAll(
            client.CreateAsync(
                RequestWithPrompt("request-a"),
                BackendApiAuthorization.Bearer("token-a"),
                CancellationToken.None),
            client.CreateAsync(
                RequestWithPrompt("request-b"),
                BackendApiAuthorization.Bearer("token-b"),
                CancellationToken.None));

        Assert.Equal("token-a", handler.TokensByPrompt["request-a"]);
        Assert.Equal("token-b", handler.TokensByPrompt["request-b"]);
        Assert.Null(httpClient.DefaultRequestHeaders.Authorization);
    }

    [Fact]
    public async Task CreateAsync_PassesCancellationTokenToHttpCall()
    {
        var handler = new RecordingHttpMessageHandler(
            async (_, cancellationToken) =>
            {
                await Task.Delay(
                    Timeout.InfiniteTimeSpan,
                    cancellationToken);
                return new HttpResponseMessage(HttpStatusCode.Accepted);
            });
        using var httpClient = CreateHttpClient(handler);
        var client = new ReportRequestsApiClient(httpClient);
        using var cancellation = new CancellationTokenSource();

        var task = client.CreateAsync(
            CreateRequest(),
            BackendApiAuthorization.Development(),
            cancellation.Token);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => task);
        Assert.True(handler.ObservedCancellation);
    }

    private static HttpClient CreateHttpClient(
        HttpMessageHandler handler)
    {
        return new HttpClient(handler)
        {
            BaseAddress = new Uri("https://backend.example"),
            Timeout = TimeSpan.FromSeconds(15)
        };
    }

    private static CreateReportRequestRequest CreateRequest()
    {
        return new CreateReportRequestRequest
        {
            Prompt = "sales report",
            ConversationId = "conversation-1",
            PreviousRequestId = null
        };
    }

    private static CopilotPlannedReportRequest CreatePlannedRequest() =>
        new()
        {
            Prompt = "sales report",
            ConversationId = "conversation-1",
            Outcome = "accepted",
            SemanticIntent = new CopilotSemanticIntent
            {
                Metric = "order_count",
                GroupBy = [],
                Filters = [],
                Date = new CopilotDateIntent
                {
                    Kind = "unspecified",
                    Grain = "none"
                },
                Ranking = new CopilotRankingIntent()
            },
            UnresolvedConcepts = [],
            Clarification = new CopilotClarification()
        };

    private static CreateReportRequestRequest RequestWithPrompt(
        string prompt) =>
        new()
        {
            Prompt = prompt,
            ConversationId = "conversation-1"
        };

    private static HttpResponseMessage AcceptedCreateResponse() =>
        new(HttpStatusCode.Accepted)
        {
            Content = new StringContent(
                """
                {
                  "requestId": "request-123",
                  "status": "Received",
                  "message": "accepted"
                }
                """,
                Encoding.UTF8,
                "application/json")
        };

    private sealed class RecordingHttpMessageHandler
        : HttpMessageHandler
    {
        private readonly Func<
            HttpRequestMessage,
            CancellationToken,
            Task<HttpResponseMessage>> _responseFactory;

        public RecordingHttpMessageHandler(
            Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
            : this(
                (request, _) =>
                    Task.FromResult(responseFactory(request)))
        {
        }

        public RecordingHttpMessageHandler(
            Func<
                HttpRequestMessage,
                CancellationToken,
                Task<HttpResponseMessage>> responseFactory)
        {
            _responseFactory = responseFactory;
        }

        public HttpMethod? Method { get; private set; }

        public Uri? RequestUri { get; private set; }

        public string? RequestBody { get; private set; }

        public bool ObservedCancellation { get; private set; }

        public AuthenticationHeaderValue? Authorization
        {
            get;
            private set;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Method = request.Method;
            RequestUri = request.RequestUri;
            Authorization = request.Headers.Authorization;
            RequestBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(
                    cancellationToken);

            try
            {
                return await _responseFactory(
                    request,
                    cancellationToken);
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                ObservedCancellation = true;
                throw;
            }
        }
    }

    private sealed class ConcurrentRecordingHandler
        : HttpMessageHandler
    {
        public ConcurrentDictionary<string, string> TokensByPrompt
        {
            get;
        } = new(StringComparer.Ordinal);

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = await request.Content!.ReadAsStringAsync(
                cancellationToken);
            using var document = JsonDocument.Parse(body);
            var prompt = document.RootElement
                .GetProperty("prompt")
                .GetString()!;
            TokensByPrompt[prompt] =
                request.Headers.Authorization?.Parameter
                ?? string.Empty;
            return AcceptedCreateResponse();
        }
    }
}
