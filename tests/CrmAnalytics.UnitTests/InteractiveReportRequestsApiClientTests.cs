using System.Net;
using System.Text;
using System.Text.Json;
using CrmAnalytics.Contracts.ReportRequests;
using CrmAnalytics.Contracts.CopilotStudio;
using CrmAnalytics.Teams.Authentication;
using CrmAnalytics.Teams.Backend;

namespace CrmAnalytics.UnitTests;

public sealed class InteractiveReportRequestsApiClientTests
{
    [Fact]
    public async Task ReviseAsync_UsesEncodedRouteAndDeserializesAccepted()
    {
        var handler = new RecordingHandler(
            HttpStatusCode.Accepted,
            """
            {
              "requestId": "revision-1",
              "previousRequestId": "source/id",
              "conversationId": "conversation-1",
              "status": "Received",
              "message": "accepted"
            }
            """);
        var client = CreateClient(handler);

        var response = await client.ReviseAsync(
            "source/id",
            new ReviseReportRequestRequest
            {
                Prompt = "Net kârı göster"
            },
            BackendApiAuthorization.Bearer("revision-token"),
            CancellationToken.None);

        Assert.Equal("revision-1", response.RequestId);
        Assert.Equal(
            "/api/report-requests/source%2Fid/revise",
            handler.Request!.RequestUri!.AbsolutePath);
        using var body = JsonDocument.Parse(handler.Body);
        Assert.Equal(
            "Net kârı göster",
            body.RootElement.GetProperty("prompt").GetString());
        Assert.Equal(
            "revision-token",
            handler.AuthorizationParameter);
    }

    [Fact]
    public async Task SubmitClarificationAsync_UsesRouteAndDeserializesAccepted()
    {
        var handler = new RecordingHandler(
            HttpStatusCode.Accepted,
            """
            {
              "requestId": "request-1",
              "status": "WaitingForClarification",
              "message": "accepted"
            }
            """);
        var client = CreateClient(handler);

        var response = await client.SubmitClarificationAsync(
            "request-1",
            new SubmitReportClarificationRequest
            {
                Response = "2026 ilk çeyrek"
            },
            BackendApiAuthorization.Bearer("clarification-token"),
            CancellationToken.None);

        Assert.Equal("request-1", response.RequestId);
        Assert.Equal(
            "/api/report-requests/request-1/clarifications",
            handler.Request!.RequestUri!.AbsolutePath);
        Assert.Equal(
            "clarification-token",
            handler.AuthorizationParameter);
    }

    [Fact]
    public async Task PlannedRevision_UsesExplicitRouteAndFullPlan()
    {
        var handler = new RecordingHandler(
            HttpStatusCode.Accepted,
            """
            {
              "requestId": "revision-1",
              "previousRequestId": "source/id",
              "conversationId": "conversation-1",
              "status": "Received",
              "message": "accepted"
            }
            """);
        var client = CreateClient(handler);

        await client.RevisePlannedAsync(
            "source/id",
            new CopilotPlannedRevisionRequest
            {
                RevisionInstruction = "kategori bazinda goster",
                Plan = Plan()
            },
            BackendApiAuthorization.Bearer("planned-token"),
            CancellationToken.None);

        Assert.Equal(
            "/api/report-requests/source%2Fid/planned-revision",
            handler.Request!.RequestUri!.AbsolutePath);
        using var body = JsonDocument.Parse(handler.Body);
        Assert.Equal("order_count",
            body.RootElement.GetProperty("plan")
                .GetProperty("semanticIntent")
                .GetProperty("metric").GetString());
        Assert.Equal("planned-token", handler.AuthorizationParameter);
    }

    private static CopilotSemanticPlan Plan() => new()
    {
        Outcome = "accepted",
        SemanticIntent = new CopilotSemanticIntent
        {
            Metric = "order_count",
            GroupBy = ["product_category"],
            Filters = [],
            Date = new CopilotDateIntent
            {
                Kind = "absolute",
                From = "2018-05-01",
                To = "2018-05-31",
                Grain = "none"
            },
            Ranking = new CopilotRankingIntent()
        },
        UnresolvedConcepts = [],
        Clarification = new CopilotClarification()
    };

    [Fact]
    public async Task BackendError_DoesNotExposeResponseBody()
    {
        const string technicalBody = "secret technical backend body";
        var handler = new RecordingHandler(
            HttpStatusCode.Conflict,
            technicalBody);
        var client = CreateClient(handler);

        var exception = await Assert.ThrowsAsync<BackendApiException>(
            () => client.SubmitClarificationAsync(
                "request-1",
                new SubmitReportClarificationRequest
                {
                    Response = "Valid response"
                },
                BackendApiAuthorization.Development(),
                CancellationToken.None));

        Assert.DoesNotContain(
            technicalBody,
            exception.ToString(),
            StringComparison.Ordinal);
    }

    private static ReportRequestsApiClient CreateClient(
        HttpMessageHandler handler) =>
        new(new HttpClient(handler)
        {
            BaseAddress = new Uri("https://backend.example")
        });

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        private readonly string _responseBody;

        public RecordingHandler(
            HttpStatusCode statusCode,
            string responseBody)
        {
            _statusCode = statusCode;
            _responseBody = responseBody;
        }

        public HttpRequestMessage? Request { get; private set; }
        public string Body { get; private set; } = string.Empty;
        public string? AuthorizationParameter { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Request = request;
            AuthorizationParameter =
                request.Headers.Authorization?.Parameter;
            Body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(
                    cancellationToken);
            return new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(
                    _responseBody,
                    Encoding.UTF8,
                    "application/json")
            };
        }
    }
}
