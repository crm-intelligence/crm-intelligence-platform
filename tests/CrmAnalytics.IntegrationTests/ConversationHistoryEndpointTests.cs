using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CrmAnalytics.Contracts.Conversations;
using CrmAnalytics.Contracts.ReportRequests;

namespace CrmAnalytics.IntegrationTests;

public sealed class ConversationHistoryEndpointTests
    : IClassFixture<IntegrationTestWebApplicationFactory>
{
    private readonly HttpClient _client;

    public ConversationHistoryEndpointTests(
        IntegrationTestWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task TwoRequestsInSameConversation_HistoryReturnsBothSafely()
    {
        var conversationId = $"teams-conversation-{Guid.NewGuid():N}";
        var firstResponse = await PostReportRequestAsync(
            conversationId,
            previousRequestId: null,
            prompt: "Secret first prompt");
        var first = await firstResponse.Content
            .ReadFromJsonAsync<CreateReportRequestResponse>();
        Assert.Equal(HttpStatusCode.Accepted, firstResponse.StatusCode);
        Assert.NotNull(first);
        Assert.Equal("Received", first.Status);

        var existingGetResponse = await _client.GetAsync(
            $"/api/report-requests/{first.RequestId}");
        var existingGetBody = await existingGetResponse.Content
            .ReadFromJsonAsync<GetReportRequestResponse>();
        Assert.Equal(HttpStatusCode.OK, existingGetResponse.StatusCode);
        Assert.NotNull(existingGetBody);
        Assert.Equal(first.RequestId, existingGetBody.RequestId);
        Assert.Equal(conversationId, existingGetBody.ConversationId);

        var secondResponse = await PostReportRequestAsync(
            conversationId,
            previousRequestId: first.RequestId,
            prompt: "Secret second prompt");
        var second = await secondResponse.Content
            .ReadFromJsonAsync<CreateReportRequestResponse>();
        Assert.Equal(HttpStatusCode.Accepted, secondResponse.StatusCode);
        Assert.NotNull(second);

        var response = await _client.GetAsync(
            $"/api/conversations/{conversationId}/report-requests");
        var body = await response.Content.ReadAsStringAsync();
        var history =
            JsonSerializer.Deserialize<GetConversationReportRequestsResponse>(
                body,
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
        using var document = JsonDocument.Parse(body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(history);
        Assert.Equal(second.RequestId, history.LastRequestId);
        Assert.Collection(
            history.ReportRequests,
            item => Assert.Equal(first.RequestId, item.RequestId),
            item => Assert.Equal(second.RequestId, item.RequestId));
        Assert.All(
            document.RootElement.GetProperty("reportRequests")
                .EnumerateArray(),
            item => Assert.False(item.TryGetProperty("prompt", out _)));
        Assert.False(document.RootElement.TryGetProperty("userId", out _));
        Assert.False(document.RootElement.TryGetProperty("tenantId", out _));
    }

    [Fact]
    public async Task MissingConversation_ReturnsNotFound()
    {
        var conversationId = $"missing-{Guid.NewGuid():N}";

        var response = await _client.GetAsync(
            $"/api/conversations/{conversationId}/report-requests");
        var body = await response.Content
            .ReadFromJsonAsync<ConversationNotFoundResponse>();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.NotNull(body);
        Assert.Equal(conversationId, body.ConversationId);
        Assert.Equal("Konuşma bulunamadı.", body.Message);
    }

    private async Task<HttpResponseMessage> PostReportRequestAsync(
        string conversationId,
        string? previousRequestId,
        string prompt)
    {
        return await _client.PostAsJsonAsync(
            "/api/report-requests",
            new CreateReportRequestRequest
            {
                Prompt = prompt,
                ConversationId = conversationId,
                PreviousRequestId = previousRequestId
            });
    }
}
