using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CrmAnalytics.Application.ReportProcessing;
using CrmAnalytics.Contracts.Conversations;
using CrmAnalytics.Contracts.Integrations;
using CrmAnalytics.Contracts.ReportRequests;
using CrmAnalytics.Domain.ReportRequests;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CrmAnalytics.IntegrationTests;

public sealed class ReportProcessingServiceIntegrationTests
    : IClassFixture<IntegrationTestWebApplicationFactory>
{
    private readonly IntegrationTestWebApplicationFactory _factory;

    public ReportProcessingServiceIntegrationTests(
        IntegrationTestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task ProcessAsync_WithMockAdapters_CompletesAndAppearsInGetAndHistory()
    {
        var conversationId = $"processing-{Guid.NewGuid():N}";
        var client = _factory.CreateClient();
        var created = await CreateRequestAsync(
            client,
            conversationId);

        ProcessReportRequestResult result;
        using (var scope = _factory.Services.CreateScope())
        {
            var processingService = scope.ServiceProvider
                .GetRequiredService<IReportProcessingService>();
            result = await processingService.ProcessAsync(
                new ProcessReportRequestCommand(
                    created.RequestId,
                    TestDataScopeFactory.Create()),
                CancellationToken.None);
        }

        Assert.Equal(ReportRequestStatus.Completed, result.Status);
        var getResponse = await client.GetAsync(
            $"/api/report-requests/{created.RequestId}");
        var getBody = await getResponse.Content.ReadAsStringAsync();
        var reportRequest =
            JsonSerializer.Deserialize<GetReportRequestResponse>(
                getBody,
                new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        Assert.NotNull(reportRequest);
        Assert.Equal("Completed", reportRequest.Status);
        Assert.Equal(
            "Mock analiz başarıyla tamamlandı.",
            reportRequest.Summary);
        Assert.False(string.IsNullOrWhiteSpace(reportRequest.ReportId));
        Assert.True(
            Uri.TryCreate(
                reportRequest.PowerBiUrl,
                UriKind.Absolute,
                out var powerBiUri));
        Assert.Equal(Uri.UriSchemeHttps, powerBiUri.Scheme);
        Assert.Null(reportRequest.ClarificationQuestion);
        using (var getDocument = JsonDocument.Parse(getBody))
        {
            Assert.False(
                getDocument.RootElement.TryGetProperty("prompt", out _));
        }

        var historyResponse = await client.GetAsync(
            $"/api/conversations/{conversationId}/report-requests");
        var historyBody = await historyResponse.Content.ReadAsStringAsync();
        var history =
            JsonSerializer.Deserialize<GetConversationReportRequestsResponse>(
                historyBody,
                new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Equal(HttpStatusCode.OK, historyResponse.StatusCode);
        Assert.NotNull(history);
        var historyItem = Assert.Single(history.ReportRequests);
        Assert.Equal(created.RequestId, historyItem.RequestId);
        Assert.Equal("Completed", historyItem.Status);
        Assert.Null(historyItem.ClarificationQuestion);
        using var historyDocument = JsonDocument.Parse(historyBody);
        Assert.False(
            historyDocument.RootElement
                .GetProperty("reportRequests")[0]
                .TryGetProperty("prompt", out _));
    }

    [Fact]
    public async Task ProcessAsync_ForcedClarification_IsVisibleInGet()
    {
        await using var clarificationFactory =
            _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((_, configuration) =>
                {
                    configuration.AddInMemoryCollection(
                        new Dictionary<string, string?>
                        {
                            ["ExternalServices:Mocks:ForceClarification"] =
                                "true",
                            ["ExternalServices:Mocks:DelayMilliseconds"] =
                                "0"
                        });
                });
            });
        var client = clarificationFactory.CreateClient();
        var conversationId = $"clarification-{Guid.NewGuid():N}";
        var created = await CreateRequestAsync(client, conversationId);

        using (var scope = clarificationFactory.Services.CreateScope())
        {
            var processingService = scope.ServiceProvider
                .GetRequiredService<IReportProcessingService>();
            var result = await processingService.ProcessAsync(
                new ProcessReportRequestCommand(
                    created.RequestId,
                    TestDataScopeFactory.Create()),
                CancellationToken.None);

            Assert.Equal(
                ReportRequestStatus.WaitingForClarification,
                result.Status);
        }

        var reportRequest = await client.GetFromJsonAsync<
            GetReportRequestResponse>(
                $"/api/report-requests/{created.RequestId}");

        Assert.NotNull(reportRequest);
        Assert.Equal("WaitingForClarification", reportRequest.Status);
        Assert.Equal(
            "Analiz için tarih aralığını belirtir misiniz?",
            reportRequest.ClarificationQuestion);
        Assert.Null(reportRequest.Summary);
        Assert.Null(reportRequest.PowerBiUrl);
    }

    private static async Task<CreateReportRequestResponse>
        CreateRequestAsync(
            HttpClient client,
            string conversationId)
    {
        var response = await client.PostAsJsonAsync(
            "/api/report-requests",
            new CreateReportRequestRequest
            {
                Prompt = "Show CRM sales trends.",
                ConversationId = conversationId
            });
        var created = await response.Content
            .ReadFromJsonAsync<CreateReportRequestResponse>();

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        return Assert.IsType<CreateReportRequestResponse>(created);
    }
}
