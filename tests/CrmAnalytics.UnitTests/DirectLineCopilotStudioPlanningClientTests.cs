using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using CrmAnalytics.Teams.Configuration;
using CrmAnalytics.Teams.Planning;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CrmAnalytics.UnitTests;

public sealed class DirectLineCopilotStudioPlanningClientTests
{
    private const string DirectLineSecret = "test-direct-line-secret";
    private const string EuropeDirectLineBaseUri =
        "https://europe.directline.botframework.com";
    private const string SemanticPayload =
        """
        {
          "outcome": "accepted",
          "semanticIntent": {
            "metric": "order_count",
            "groupBy": [],
            "filters": [],
            "date": {
              "kind": "unspecified",
              "relativeExpression": "",
              "count": 0,
              "from": "",
              "to": "",
              "grain": "none"
            },
            "ranking": {
              "topN": 0,
              "orderBy": "",
              "direction": ""
            }
          },
          "unresolvedConcepts": [],
          "clarification": { "kind": "" }
        }
        """;

    [Fact]
    public async Task PlanAsync_UsesDirectLineFlowAndAcceptsJsonFence()
    {
        var handler = new DirectLineFlowHandler(
            $"```json\n{SemanticPayload}\n```");
        using var httpClient = new HttpClient(handler)
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
        var client = CreateClient(httpClient);

        var result = await client.PlanAsync(
            "  satış özeti 📊  ",
            "  teams-conversation-42  ",
            CancellationToken.None);

        Assert.Equal("satış özeti 📊", result.Prompt);
        Assert.Equal("teams-conversation-42", result.ConversationId);
        Assert.Equal("accepted", result.Outcome);
        Assert.Equal("order_count", result.SemanticIntent.Metric);

        var requests = handler.Requests.ToArray();
        Assert.Equal(4, requests.Length);
        Assert.Equal(HttpMethod.Post, requests[0].Method);
        Assert.Equal(
            $"{EuropeDirectLineBaseUri}/v3/directline/tokens/generate",
            requests[0].Uri);
        Assert.Equal(DirectLineSecret, requests[0].Authorization);
        var directLineUserId = ReadDirectLineUserId(requests[0].Body);
        Assert.StartsWith("dl_", directLineUserId, StringComparison.Ordinal);
        Assert.True(
            Guid.TryParseExact(directLineUserId[3..], "D", out _));
        Assert.Equal(
            $"{EuropeDirectLineBaseUri}/v3/directline/conversations",
            requests[1].Uri);
        Assert.Equal("direct-line-token", requests[1].Authorization);
        Assert.Equal(HttpMethod.Post, requests[2].Method);
        Assert.Equal(
            $"{EuropeDirectLineBaseUri}/v3/directline/conversations/"
                + "direct-line-conversation/activities",
            requests[2].Uri);
        Assert.Equal("conversation-token", requests[2].Authorization);
        using (var activity = JsonDocument.Parse(requests[2].Body!))
        {
            Assert.Equal(
                ["type", "from", "text", "locale"],
                activity.RootElement
                    .EnumerateObject()
                    .Select(property => property.Name));
            Assert.Equal(
                ["id"],
                activity.RootElement
                    .GetProperty("from")
                    .EnumerateObject()
                    .Select(property => property.Name));
            Assert.Equal(
                "message",
                activity.RootElement.GetProperty("type").GetString());
            Assert.Equal(
                directLineUserId,
                activity.RootElement
                    .GetProperty("from")
                    .GetProperty("id")
                    .GetString());
            Assert.Equal(
                "satış özeti 📊",
                activity.RootElement.GetProperty("text").GetString());
            Assert.Equal(
                "tr-TR",
                activity.RootElement.GetProperty("locale").GetString());
        }
        Assert.NotNull(requests[2].BodyBytes);
        Assert.NotEmpty(requests[2].BodyBytes!);
        Assert.Equal(
            Encoding.UTF8.GetBytes(requests[2].Body!),
            requests[2].BodyBytes);
        Assert.Equal(
            "application/json; charset=utf-8",
            requests[2].ContentType);
        Assert.True(requests[2].ContentLength > 0);
        Assert.Equal(
            requests[2].BodyBytes!.LongLength,
            requests[2].ContentLength);
        Assert.NotEqual(true, requests[2].IsChunked);
        Assert.Equal(HttpMethod.Get, requests[3].Method);
        Assert.Equal(
            $"{EuropeDirectLineBaseUri}/v3/directline/conversations/"
                + "direct-line-conversation/activities",
            requests[3].Uri);
        Assert.All(
            requests,
            request => Assert.Equal(
                "europe.directline.botframework.com",
                new Uri(request.Uri).Host));
    }

    [Fact]
    public async Task PlanAsync_DifferentPlanningConversationsUseDifferentIds()
    {
        var firstHandler = new DirectLineFlowHandler(SemanticPayload);
        var secondHandler = new DirectLineFlowHandler(SemanticPayload);
        using var firstHttpClient = new HttpClient(firstHandler)
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
        using var secondHttpClient = new HttpClient(secondHandler)
        {
            Timeout = Timeout.InfiniteTimeSpan
        };

        await CreateClient(firstHttpClient).PlanAsync(
            "sales report",
            "teams-conversation-1",
            CancellationToken.None);
        await CreateClient(secondHttpClient).PlanAsync(
            "sales report",
            "teams-conversation-2",
            CancellationToken.None);

        var firstId = ReadDirectLineUserId(
            firstHandler.Requests.First().Body);
        var secondId = ReadDirectLineUserId(
            secondHandler.Requests.First().Body);
        Assert.NotEqual(firstId, secondId);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task PlanAsync_TokenAuthorizationFailureFailsClosed(
        HttpStatusCode statusCode)
    {
        var handler = new DirectLineFlowHandler(
            SemanticPayload,
            tokenStatusCode: statusCode);
        using var httpClient = new HttpClient(handler)
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
        var client = CreateClient(httpClient);

        var exception = await Assert.ThrowsAsync<
            CopilotStudioPlanningException>(
            () => client.PlanAsync(
                "sales report",
                "conversation-1",
                CancellationToken.None));

        Assert.Contains(
            $"HTTP status {(int)statusCode}",
            exception.Message,
            StringComparison.Ordinal);
        Assert.Single(handler.Requests);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{ \"token\": \"direct-line-token\" }")]
    [InlineData("{ \"token\": \"direct-line-token\", \"conversationId\": \"token-conversation\", \"expires_in\": 0 }")]
    [InlineData("not-json")]
    public async Task PlanAsync_MalformedTokenResponseFailsClosed(
        string tokenResponse)
    {
        var handler = new DirectLineFlowHandler(
            SemanticPayload,
            tokenResponse: tokenResponse);
        using var httpClient = new HttpClient(handler)
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
        var client = CreateClient(httpClient);

        await Assert.ThrowsAsync<CopilotStudioPlanningException>(
            () => client.PlanAsync(
                "sales report",
                "conversation-1",
                CancellationToken.None));

        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task PlanAsync_DirectLineSecretIsUsedOnlyForTokenGeneration()
    {
        var handler = new DirectLineFlowHandler(SemanticPayload);
        using var httpClient = new HttpClient(handler)
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
        var client = CreateClient(httpClient);

        var result = await client.PlanAsync(
            "sales report",
            "conversation-1",
            CancellationToken.None);

        var requests = handler.Requests.ToArray();
        Assert.Equal(DirectLineSecret, requests[0].Authorization);
        Assert.DoesNotContain(
            requests.Skip(1),
            request => string.Equals(
                request.Authorization,
                DirectLineSecret,
                StringComparison.Ordinal)
                || request.Body?.Contains(
                    DirectLineSecret,
                    StringComparison.Ordinal) == true);
        Assert.DoesNotContain(
            DirectLineSecret,
            JsonSerializer.Serialize(result),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task PlanAsync_DoesNotLeakTeamsIdentityOrBearerToken()
    {
        const string teamsBearerToken = "opaque-teams-oauth-token";
        const string teamsIdentity =
            "teams-user-29:user@example.test:teams-conversation-42";
        var handler = new DirectLineFlowHandler(SemanticPayload);
        using var httpClient = new HttpClient(handler)
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
        httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", teamsBearerToken);
        var client = CreateClient(httpClient);

        await client.PlanAsync(
            "sales report",
            teamsIdentity,
            CancellationToken.None);

        var requests = handler.Requests.ToArray();
        Assert.DoesNotContain(
            requests,
            request => string.Equals(
                request.Authorization,
                teamsBearerToken,
                StringComparison.Ordinal));
        Assert.DoesNotContain(
            requests,
            request => request.Body?.Contains(
                teamsBearerToken,
                StringComparison.Ordinal) == true);

        using var activity = JsonDocument.Parse(requests[2].Body!);
        Assert.Equal(
            "sales report",
            activity.RootElement.GetProperty("text").GetString());
        Assert.False(
            activity.RootElement.TryGetProperty(
                "conversationId",
                out _));
        Assert.All(
            requests.Where(request => request.Body is not null),
            request => Assert.DoesNotContain(
                teamsIdentity,
                request.Body!,
                StringComparison.Ordinal));
        Assert.Equal(
            ReadDirectLineUserId(requests[0].Body),
            activity.RootElement
                .GetProperty("from")
                .GetProperty("id")
                .GetString());
    }

    [Fact]
    public async Task PlanAsync_ActivityErrorLogsSafeErrorResponseOnly()
    {
        const string prompt = "gizli satış promptu";
        const string teamsBearerToken = "opaque-teams-oauth-token";
        var errorResponse = JsonSerializer.Serialize(new
        {
            error = new
            {
                code = "BadActivity",
                message = "Activity payload rejected."
            },
            raw = $"{DirectLineSecret} direct-line-token "
                + $"conversation-token {teamsBearerToken} {prompt}"
        });
        var handler = new DirectLineFlowHandler(
            SemanticPayload,
            activityStatusCode: HttpStatusCode.BadRequest,
            activityResponse: errorResponse);
        using var httpClient = new HttpClient(handler)
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
        httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", teamsBearerToken);
        var logger = new TestLogger<
            DirectLineCopilotStudioPlanningClient>();
        var client = CreateClient(httpClient, logger);

        var exception = await Assert.ThrowsAsync<
            CopilotStudioPlanningException>(
            () => client.PlanAsync(
                prompt,
                "conversation-1",
                CancellationToken.None));

        Assert.Contains("HTTP status 400", exception.Message);
        var telemetry = string.Join(Environment.NewLine, logger.Messages);
        Assert.Contains("Stage: activity-post", telemetry);
        Assert.Contains("HTTP status: 400", telemetry);
        Assert.Contains("error code: BadActivity", telemetry);
        Assert.Contains(
            "error message: Activity payload rejected.",
            telemetry);
        Assert.DoesNotContain(DirectLineSecret, telemetry);
        Assert.DoesNotContain("direct-line-token", telemetry);
        Assert.DoesNotContain("conversation-token", telemetry);
        Assert.DoesNotContain(teamsBearerToken, telemetry);
        Assert.DoesNotContain(prompt, telemetry);
    }

    [Fact]
    public async Task PlanAsync_UnparseableActivityErrorDoesNotLogRawBody()
    {
        const string prompt = "çok gizli prompt";
        var rawResponse = $"not-json {DirectLineSecret} "
            + $"conversation-token {prompt}";
        var handler = new DirectLineFlowHandler(
            SemanticPayload,
            activityStatusCode: HttpStatusCode.BadRequest,
            activityResponse: rawResponse);
        using var httpClient = new HttpClient(handler)
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
        var logger = new TestLogger<
            DirectLineCopilotStudioPlanningClient>();
        var client = CreateClient(httpClient, logger);

        await Assert.ThrowsAsync<CopilotStudioPlanningException>(
            () => client.PlanAsync(
                prompt,
                "conversation-1",
                CancellationToken.None));

        var telemetry = string.Join(Environment.NewLine, logger.Messages);
        Assert.Contains("error-response-unparseable", telemetry);
        Assert.DoesNotContain("not-json", telemetry);
        Assert.DoesNotContain(DirectLineSecret, telemetry);
        Assert.DoesNotContain("conversation-token", telemetry);
        Assert.DoesNotContain(prompt, telemetry);
    }

    [Fact]
    public async Task PlanAsync_MalformedSemanticJsonFailsClosed()
    {
        var handler = new DirectLineFlowHandler(
            "{ \"outcome\": \"accepted\" }");
        using var httpClient = new HttpClient(handler)
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
        var client = CreateClient(httpClient);

        var exception = await Assert.ThrowsAsync<
            CopilotStudioPlanningException>(
            () => client.PlanAsync(
                "sales report",
                "conversation-1",
                CancellationToken.None));

        Assert.Contains(
            "malformed semantic payload",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task PlanAsync_UnknownSemanticPropertyFailsClosed()
    {
        var payload = SemanticPayload.Replace(
            "\"outcome\": \"accepted\"",
            "\"outcome\": \"accepted\", \"unexpected\": true",
            StringComparison.Ordinal);
        var handler = new DirectLineFlowHandler(payload);
        using var httpClient = new HttpClient(handler)
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
        var client = CreateClient(httpClient);

        await Assert.ThrowsAsync<CopilotStudioPlanningException>(
            () => client.PlanAsync(
                "sales report",
                "conversation-1",
                CancellationToken.None));
    }

    [Fact]
    public async Task PlanAsync_CallerCancellationIsPropagated()
    {
        var handler = new BlockingHandler();
        using var httpClient = new HttpClient(handler)
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
        var client = CreateClient(httpClient);
        using var cancellation = new CancellationTokenSource();

        var task = client.PlanAsync(
            "sales report",
            "conversation-1",
            cancellation.Token);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
    }

    private static DirectLineCopilotStudioPlanningClient CreateClient(
        HttpClient httpClient,
        ILogger<DirectLineCopilotStudioPlanningClient>? logger = null) =>
        new(
            httpClient,
            Options.Create(new CopilotStudioOptions
            {
                Enabled = true,
                DirectLineBaseUri = EuropeDirectLineBaseUri,
                DirectLineSecret = DirectLineSecret,
                AgentName = "CRM Planner",
                RequestTimeoutSeconds = 5
            }),
            logger
                ?? NullLogger<
                    DirectLineCopilotStudioPlanningClient>.Instance);

    private static string ReadDirectLineUserId(string? requestBody)
    {
        using var document = JsonDocument.Parse(requestBody!);
        return document.RootElement
            .GetProperty("user")
            .GetProperty("id")
            .GetString()!;
    }

    private sealed class DirectLineFlowHandler(
        string agentResponse,
        HttpStatusCode tokenStatusCode = HttpStatusCode.OK,
        string? tokenResponse = null,
        HttpStatusCode activityStatusCode = HttpStatusCode.OK,
        string? activityResponse = null)
        : HttpMessageHandler
    {
        private int _callCount;

        public ConcurrentQueue<RequestSnapshot> Requests { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var bodyBytes = request.Content is null
                ? null
                : await request.Content.ReadAsByteArrayAsync(
                    cancellationToken);
            var body = bodyBytes is null
                ? null
                : Encoding.UTF8.GetString(bodyBytes);
            Requests.Enqueue(new RequestSnapshot(
                request.Method,
                request.RequestUri!.AbsoluteUri,
                request.Headers.Authorization?.Parameter,
                body,
                bodyBytes,
                request.Content?.Headers.ContentType?.ToString(),
                request.Content?.Headers.ContentLength,
                request.Headers.TransferEncodingChunked));

            return Interlocked.Increment(ref _callCount) switch
            {
                1 => JsonResponse(
                    tokenStatusCode,
                    tokenResponse ?? """
                    {
                      "token": "direct-line-token",
                      "expires_in": 1800,
                      "conversationId": "token-conversation"
                    }
                    """),
                2 => JsonResponse(
                    HttpStatusCode.Created,
                    """
                    {
                      "token": "conversation-token",
                      "conversationId": "direct-line-conversation"
                    }
                    """),
                3 => JsonResponse(
                    activityStatusCode,
                    activityResponse ?? "{ \"id\": \"activity-1\" }"),
                4 => ActivityResponse(agentResponse),
                _ => throw new InvalidOperationException(
                    "Unexpected Direct Line request.")
            };
        }

        private static HttpResponseMessage ActivityResponse(string text)
        {
            var json = JsonSerializer.Serialize(new
            {
                activities = new[]
                {
                    new
                    {
                        type = "message",
                        from = new
                        {
                            id = "copilot-agent",
                            name = "CRM Planner",
                            role = "bot"
                        },
                        text
                    }
                },
                watermark = "1"
            });
            return JsonResponse(HttpStatusCode.OK, json);
        }

        private static HttpResponseMessage JsonResponse(
            HttpStatusCode statusCode,
            string json) =>
            new(statusCode)
            {
                Content = new StringContent(
                    json,
                    Encoding.UTF8,
                    "application/json")
            };
    }

    private sealed class BlockingHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("Unreachable");
        }
    }

    private sealed record RequestSnapshot(
        HttpMethod Method,
        string Uri,
        string? Authorization,
        string? Body,
        byte[]? BodyBytes,
        string? ContentType,
        long? ContentLength,
        bool? IsChunked);

    private sealed class TestLogger<T> : ILogger<T>
    {
        public ConcurrentQueue<string> Messages { get; } = new();

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Messages.Enqueue(formatter(state, exception));
    }
}
