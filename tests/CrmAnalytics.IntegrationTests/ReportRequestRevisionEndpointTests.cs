using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using CrmAnalytics.Application.ReportRequests;
using CrmAnalytics.Contracts.Common;
using CrmAnalytics.Contracts.Conversations;
using CrmAnalytics.Contracts.ReportRequests;
using CrmAnalytics.Domain.ReportRequests;
using Microsoft.Extensions.DependencyInjection;

namespace CrmAnalytics.IntegrationTests;

public sealed class ReportRequestRevisionEndpointTests
    : IClassFixture<IntegrationTestWebApplicationFactory>
{
    private readonly IntegrationTestWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public ReportRequestRevisionEndpointTests(
        IntegrationTestWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task CompletedRequest_ReviseCreatesLinkedRequestAndUpdatesHistory()
    {
        var conversationId = $"revision-success-{Guid.NewGuid():N}";
        var createResponse = await _client.PostAsJsonAsync(
            "/api/report-requests",
            new CreateReportRequestRequest
            {
                Prompt = "Show sales count.",
                ConversationId = conversationId
            });
        var source = await createResponse.Content
            .ReadFromJsonAsync<CreateReportRequestResponse>();

        Assert.Equal(HttpStatusCode.Accepted, createResponse.StatusCode);
        Assert.NotNull(source);
        await CompleteRequestAsync(source.RequestId);

        var revisionHttpResponse = await _client.PostAsJsonAsync(
            $"/api/report-requests/{source.RequestId}/revise",
            new ReviseReportRequestRequest
            {
                Prompt = "Show net profit instead of sales count."
            });
        var revisionBody = await revisionHttpResponse.Content
            .ReadAsStringAsync();
        var revision = JsonSerializer.Deserialize<ReviseReportRequestResponse>(
            revisionBody,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Equal(
            HttpStatusCode.Accepted,
            revisionHttpResponse.StatusCode);
        Assert.NotNull(revision);
        Assert.NotEqual(source.RequestId, revision.RequestId);
        Assert.Equal(source.RequestId, revision.PreviousRequestId);
        Assert.Equal(conversationId, revision.ConversationId);
        Assert.Equal("Received", revision.Status);
        Assert.NotNull(revisionHttpResponse.Headers.Location);
        Assert.EndsWith(
            $"/api/report-requests/{revision.RequestId}",
            revisionHttpResponse.Headers.Location.OriginalString);
        using (var revisionDocument = JsonDocument.Parse(revisionBody))
        {
            Assert.False(
                revisionDocument.RootElement.TryGetProperty(
                    "prompt",
                    out _));
        }

        var newRequestResponse = await _client.GetAsync(
            $"/api/report-requests/{revision.RequestId}");
        var newRequestBody = await newRequestResponse.Content
            .ReadAsStringAsync();
        var newRequest =
            JsonSerializer.Deserialize<GetReportRequestResponse>(
                newRequestBody,
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Equal(HttpStatusCode.OK, newRequestResponse.StatusCode);
        Assert.NotNull(newRequest);
        Assert.Equal(source.RequestId, newRequest.PreviousRequestId);
        Assert.Equal(conversationId, newRequest.ConversationId);
        Assert.Equal("Received", newRequest.Status);
        Assert.NotEqual(
            (await GetRequestAsync(source.RequestId)).CorrelationId,
            newRequest.CorrelationId);
        Assert.Null(newRequest.ReportId);
        Assert.Null(newRequest.Summary);
        Assert.Null(newRequest.PowerBiUrl);
        using (var newRequestDocument = JsonDocument.Parse(newRequestBody))
        {
            Assert.False(
                newRequestDocument.RootElement.TryGetProperty(
                    "prompt",
                    out _));
        }

        var historyResponse = await _client.GetAsync(
            $"/api/conversations/{conversationId}/report-requests");
        var historyBody = await historyResponse.Content.ReadAsStringAsync();
        var history =
            JsonSerializer.Deserialize<GetConversationReportRequestsResponse>(
                historyBody,
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Equal(HttpStatusCode.OK, historyResponse.StatusCode);
        Assert.NotNull(history);
        Assert.Equal(revision.RequestId, history.LastRequestId);
        Assert.Equal(2, history.ReportRequests.Count);
        Assert.Contains(
            history.ReportRequests,
            item =>
                item.RequestId == source.RequestId
                && item.Status == "Completed");
        Assert.Contains(
            history.ReportRequests,
            item =>
                item.RequestId == revision.RequestId
                && item.PreviousRequestId == source.RequestId
                && item.Status == "Received");
        using var historyDocument = JsonDocument.Parse(historyBody);
        Assert.All(
            historyDocument.RootElement
                .GetProperty("reportRequests")
                .EnumerateArray(),
            item => Assert.False(item.TryGetProperty("prompt", out _)));

        var unchangedSource = await GetRequestAsync(source.RequestId);
        Assert.Equal("Completed", unchangedSource.Status);
        Assert.Equal("report-integration", unchangedSource.ReportId);
        Assert.Equal("Integration report summary.", unchangedSource.Summary);
        Assert.Equal(
            "https://app.powerbi.com/reports/integration",
            unchangedSource.PowerBiUrl);
    }

    [Fact]
    public async Task MissingSource_ReviseReturnsResourceNotFound()
    {
        var requestId = $"missing-{Guid.NewGuid():N}";

        var response = await _client.PostAsJsonAsync(
            $"/api/report-requests/{requestId}/revise",
            new ReviseReportRequestRequest
            {
                Prompt = "Show net profit."
            });
        var error = await response.Content
            .ReadFromJsonAsync<ApiErrorResponse>();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.NotNull(error);
        Assert.Equal("RESOURCE_NOT_FOUND", error.ErrorCode);
    }

    [Fact]
    public async Task ReceivedSource_ReviseReturnsInvalidOperation()
    {
        var conversationId = $"revision-conflict-{Guid.NewGuid():N}";
        var createResponse = await _client.PostAsJsonAsync(
            "/api/report-requests",
            new CreateReportRequestRequest
            {
                Prompt = "Show sales count.",
                ConversationId = conversationId
            });
        var source = await createResponse.Content
            .ReadFromJsonAsync<CreateReportRequestResponse>();
        Assert.NotNull(source);

        var response = await _client.PostAsJsonAsync(
            $"/api/report-requests/{source.RequestId}/revise",
            new ReviseReportRequestRequest
            {
                Prompt = "Show net profit."
            });
        var error = await response.Content
            .ReadFromJsonAsync<ApiErrorResponse>();

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.NotNull(error);
        Assert.Equal("INVALID_OPERATION", error.ErrorCode);

        var history = await _client.GetFromJsonAsync<
            GetConversationReportRequestsResponse>(
                $"/api/conversations/{conversationId}/report-requests");
        Assert.NotNull(history);
        Assert.Equal(source.RequestId, history.LastRequestId);
        Assert.Single(history.ReportRequests);
    }

    [Theory]
    [InlineData("")]
    [InlineData("ab")]
    public async Task InvalidShortPrompt_ReviseReturnsValidationProblem(
        string prompt)
    {
        await AssertInvalidPromptAsync(prompt);
    }

    [Fact]
    public async Task PromptLongerThanMaximum_ReviseReturnsValidationProblem()
    {
        await AssertInvalidPromptAsync(new string('a', 2001));
    }

    private async Task CompleteRequestAsync(string requestId)
    {
        using var scope = _factory.Services.CreateScope();
        var service = scope.ServiceProvider
            .GetRequiredService<IReportRequestService>();
        var firstTransitionAt = DateTimeOffset.UtcNow.AddMinutes(1);

        await service.TransitionStatusAsync(
            new TransitionReportRequestStatusCommand(
                RequestId: requestId,
                TargetStatus: ReportRequestStatus.Validating,
                TransitionedAt: firstTransitionAt),
            CancellationToken.None);
        await service.TransitionStatusAsync(
            new TransitionReportRequestStatusCommand(
                RequestId: requestId,
                TargetStatus: ReportRequestStatus.Processing,
                TransitionedAt: firstTransitionAt.AddMinutes(1)),
            CancellationToken.None);
        await service.CompleteAsync(
            new CompleteReportRequestCommand(
                RequestId: requestId,
                ReportId: "report-integration",
                Summary: "Integration report summary.",
                PowerBiUrl:
                    "https://app.powerbi.com/reports/integration",
                CompletedAt: firstTransitionAt.AddMinutes(2)),
            CancellationToken.None);
    }

    private async Task<GetReportRequestResponse> GetRequestAsync(
        string requestId)
    {
        var response = await _client.GetFromJsonAsync<
            GetReportRequestResponse>(
                $"/api/report-requests/{requestId}");
        return Assert.IsType<GetReportRequestResponse>(response);
    }

    private async Task AssertInvalidPromptAsync(string prompt)
    {
        var requestId = $"validation-{Guid.NewGuid():N}";
        using var content = new StringContent(
            JsonSerializer.Serialize(new { prompt }),
            Encoding.UTF8,
            "application/json");

        var response = await _client.PostAsync(
            $"/api/report-requests/{requestId}/revise",
            content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(
            "application/problem+json",
            response.Content.Headers.ContentType?.MediaType);
    }
}
