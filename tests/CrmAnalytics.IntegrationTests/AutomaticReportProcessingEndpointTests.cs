using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CrmAnalytics.Contracts.Conversations;
using CrmAnalytics.Contracts.ReportRequests;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace CrmAnalytics.IntegrationTests;

public sealed class AutomaticReportProcessingEndpointTests
    : IClassFixture<IntegrationTestWebApplicationFactory>
{
    private static readonly HashSet<string> TerminalStatuses =
        new(StringComparer.Ordinal)
        {
            "Completed",
            "Failed",
            "WaitingForClarification"
        };

    private readonly IntegrationTestWebApplicationFactory _baseFactory;

    public AutomaticReportProcessingEndpointTests(
        IntegrationTestWebApplicationFactory baseFactory)
    {
        _baseFactory = baseFactory;
    }

    [Fact]
    public async Task Create_AutomaticallyProcessesToCompleted()
    {
        await using var factory = CreateProcessingFactory();
        using var client = factory.CreateClient();
        var conversationId = $"automatic-create-{Guid.NewGuid():N}";

        var response = await client.PostAsJsonAsync(
            "/api/report-requests",
            new CreateReportRequestRequest
            {
                Prompt = "Secret automatic processing prompt.",
                ConversationId = conversationId
            });
        var responseBody = await response.Content.ReadAsStringAsync();
        var created = JsonSerializer.Deserialize<CreateReportRequestResponse>(
            responseBody,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.NotNull(created);
        Assert.Equal("Received", created.Status);
        Assert.DoesNotContain("prompt", responseBody, StringComparison.OrdinalIgnoreCase);

        var completed = await WaitForTerminalStatusAsync(
            client,
            created.RequestId,
            TimeSpan.FromSeconds(10));

        Assert.Equal("Completed", completed.Response.Status);
        Assert.Equal(
            "Mock analiz başarıyla tamamlandı.",
            completed.Response.Summary);
        Assert.False(string.IsNullOrWhiteSpace(completed.Response.ReportId));
        Assert.True(
            Uri.TryCreate(
                completed.Response.PowerBiUrl,
                UriKind.Absolute,
                out var powerBiUri));
        Assert.Equal(Uri.UriSchemeHttps, powerBiUri.Scheme);
        Assert.DoesNotContain(
            "prompt",
            completed.Body,
            StringComparison.OrdinalIgnoreCase);

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
        Assert.DoesNotContain(
            "prompt",
            historyBody,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Create_ForcedClarification_AutomaticallyWaitsForClarification()
    {
        await using var factory = CreateProcessingFactory(
            forceClarification: true);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/report-requests",
            new CreateReportRequestRequest
            {
                Prompt = "Analyze sales.",
                ConversationId =
                    $"automatic-clarification-{Guid.NewGuid():N}"
            });
        var created = await response.Content
            .ReadFromJsonAsync<CreateReportRequestResponse>();

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.NotNull(created);
        Assert.Equal("Received", created.Status);

        var waiting = await WaitForTerminalStatusAsync(
            client,
            created.RequestId,
            TimeSpan.FromSeconds(10));

        Assert.Equal(
            "WaitingForClarification",
            waiting.Response.Status);
        Assert.False(
            string.IsNullOrWhiteSpace(
                waiting.Response.ClarificationQuestion));
        Assert.Null(waiting.Response.Summary);
        Assert.Null(waiting.Response.ReportId);
        Assert.Null(waiting.Response.PowerBiUrl);
    }

    [Fact]
    public async Task Revise_AutomaticallyProcessesNewRevision()
    {
        await using var factory = CreateProcessingFactory();
        using var client = factory.CreateClient();
        var conversationId = $"automatic-revise-{Guid.NewGuid():N}";

        var createResponse = await client.PostAsJsonAsync(
            "/api/report-requests",
            new CreateReportRequestRequest
            {
                Prompt = "Show sales count.",
                ConversationId = conversationId
            });
        var source = await createResponse.Content
            .ReadFromJsonAsync<CreateReportRequestResponse>();
        Assert.NotNull(source);
        Assert.Equal(
            "Completed",
            (await WaitForTerminalStatusAsync(
                client,
                source.RequestId,
                TimeSpan.FromSeconds(10))).Response.Status);

        var reviseResponse = await client.PostAsJsonAsync(
            $"/api/report-requests/{source.RequestId}/revise",
            new ReviseReportRequestRequest
            {
                Prompt = "Show net profit instead."
            });
        var revision = await reviseResponse.Content
            .ReadFromJsonAsync<ReviseReportRequestResponse>();

        Assert.Equal(HttpStatusCode.Accepted, reviseResponse.StatusCode);
        Assert.NotNull(revision);
        Assert.NotEqual(source.RequestId, revision.RequestId);
        Assert.Equal(source.RequestId, revision.PreviousRequestId);
        Assert.Equal("Received", revision.Status);

        var revised = await WaitForTerminalStatusAsync(
            client,
            revision.RequestId,
            TimeSpan.FromSeconds(10));
        Assert.Equal("Completed", revised.Response.Status);
        Assert.Equal(
            source.RequestId,
            revised.Response.PreviousRequestId);

        var history = await client.GetFromJsonAsync<
            GetConversationReportRequestsResponse>(
                $"/api/conversations/{conversationId}/report-requests");
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
                && item.Status == "Completed");

        var unchangedSource = await client.GetFromJsonAsync<
            GetReportRequestResponse>(
                $"/api/report-requests/{source.RequestId}");
        Assert.NotNull(unchangedSource);
        Assert.Equal("Completed", unchangedSource.Status);
    }

    private WebApplicationFactory<Program> CreateProcessingFactory(
        bool forceClarification = false)
    {
        return _baseFactory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["ReportProcessing:Queue:Enabled"] = "true",
                        ["ReportProcessing:Queue:Capacity"] = "100",
                        ["ExternalServices:Mocks:DelayMilliseconds"] = "0",
                        ["ExternalServices:Mocks:ForceClarification"] =
                            forceClarification.ToString()
                    });
            });
        });
    }

    private static async Task<(
        GetReportRequestResponse Response,
        string Body)> WaitForTerminalStatusAsync(
        HttpClient client,
        string requestId,
        TimeSpan timeout)
    {
        using var timeoutSource = new CancellationTokenSource(timeout);
        string? lastStatus = null;

        try
        {
            while (true)
            {
                var httpResponse = await client.GetAsync(
                    $"/api/report-requests/{requestId}",
                    timeoutSource.Token);
                var body = await httpResponse.Content.ReadAsStringAsync(
                    timeoutSource.Token);
                var response =
                    JsonSerializer.Deserialize<GetReportRequestResponse>(
                        body,
                        new JsonSerializerOptions(
                            JsonSerializerDefaults.Web));

                Assert.Equal(HttpStatusCode.OK, httpResponse.StatusCode);
                Assert.NotNull(response);
                lastStatus = response.Status;

                if (TerminalStatuses.Contains(response.Status))
                {
                    return (response, body);
                }

                await Task.Delay(
                    TimeSpan.FromMilliseconds(25),
                    timeoutSource.Token);
            }
        }
        catch (OperationCanceledException)
            when (timeoutSource.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"Request '{requestId}' did not reach a terminal status "
                    + $"within {timeout}. Last status: '{lastStatus}'.");
        }
    }
}
