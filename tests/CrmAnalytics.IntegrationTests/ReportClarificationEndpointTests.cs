using System.Net;
using System.Net.Http.Json;
using CrmAnalytics.Application.Abstractions.Integrations;
using CrmAnalytics.Contracts.Integrations;
using CrmAnalytics.Contracts.ReportRequests;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CrmAnalytics.IntegrationTests;

public sealed class ReportClarificationEndpointTests
    : IClassFixture<IntegrationTestWebApplicationFactory>
{
    private readonly IntegrationTestWebApplicationFactory _baseFactory;

    public ReportClarificationEndpointTests(
        IntegrationTestWebApplicationFactory baseFactory)
    {
        _baseFactory = baseFactory;
    }

    [Fact]
    public async Task Clarification_ResumesSameRequestToCompleted()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var createResponse = await client.PostAsJsonAsync(
            "/api/report-requests",
            new CreateReportRequestRequest
            {
                Prompt = "Original secret prompt",
                ConversationId =
                    $"clarification-{Guid.NewGuid():N}"
            });
        var created = await createResponse.Content
            .ReadFromJsonAsync<CreateReportRequestResponse>();
        Assert.NotNull(created);
        await WaitForStatusAsync(
            client,
            created.RequestId,
            "WaitingForClarification");

        var response = await client.PostAsJsonAsync(
            $"/api/report-requests/{created.RequestId}/clarifications",
            new SubmitReportClarificationRequest
            {
                Response = "2026 ilk çeyrek"
            });
        var accepted = await response.Content
            .ReadFromJsonAsync<SubmitReportClarificationResponse>();

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.NotNull(accepted);
        Assert.Equal(created.RequestId, accepted.RequestId);

        var completed = await WaitForStatusAsync(
            client,
            created.RequestId,
            "Completed");
        var getBody = await client.GetStringAsync(
            $"/api/report-requests/{created.RequestId}");
        Assert.Equal("Completed", completed.Status);
        Assert.DoesNotContain(
            "Original secret prompt",
            getBody,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "2026 ilk çeyrek",
            getBody,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "clarificationResponse",
            getBody,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Clarification_InvalidStateMissingIdAndBody_AreSafe()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var missing = await client.PostAsJsonAsync(
            "/api/report-requests/missing/clarifications",
            new SubmitReportClarificationRequest
            {
                Response = "Valid response"
            });
        var invalidBody = await client.PostAsJsonAsync(
            "/api/report-requests/missing/clarifications",
            new { response = "ab" });

        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, invalidBody.StatusCode);
    }

    [Fact]
    public async Task Clarification_ReceivedRequestIsConflictAndLongBodyIsBadRequest()
    {
        using var client = _baseFactory.CreateClient();
        var create = await client.PostAsJsonAsync(
            "/api/report-requests",
            new CreateReportRequestRequest
            {
                Prompt = "Original prompt",
                ConversationId = $"invalid-state-{Guid.NewGuid():N}"
            });
        var created = await create.Content
            .ReadFromJsonAsync<CreateReportRequestResponse>();
        Assert.NotNull(created);

        var conflict = await client.PostAsJsonAsync(
            $"/api/report-requests/{created.RequestId}/clarifications",
            new SubmitReportClarificationRequest
            {
                Response = "Valid response"
            });
        var longBody = await client.PostAsJsonAsync(
            $"/api/report-requests/{created.RequestId}/clarifications",
            new { response = new string('a', 2001) });

        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, longBody.StatusCode);
    }

    private WebApplicationFactory<Program> CreateFactory() =>
        _baseFactory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["ReportProcessing:Queue:Enabled"] = "true",
                        ["ReportProcessing:Queue:Capacity"] = "100",
                        ["ExternalServices:Mocks:DelayMilliseconds"] = "0"
                    });
            });
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IQueryPlanningClient>();
                services.AddSingleton<
                    IQueryPlanningClient,
                    StateAwarePlanner>();
            });
        });

    private static async Task<GetReportRequestResponse> WaitForStatusAsync(
        HttpClient client,
        string requestId,
        string expectedStatus)
    {
        using var timeout = new CancellationTokenSource(
            TimeSpan.FromSeconds(10));
        string? lastStatus = null;

        try
        {
            while (true)
            {
                var response = await client.GetFromJsonAsync<
                    GetReportRequestResponse>(
                        $"/api/report-requests/{requestId}",
                        timeout.Token);
                Assert.NotNull(response);
                lastStatus = response.Status;
                if (response.Status == expectedStatus)
                {
                    return response;
                }

                await Task.Delay(25, timeout.Token);
            }
        }
        catch (OperationCanceledException)
            when (timeout.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"Expected '{expectedStatus}', last status "
                    + $"was '{lastStatus}'.");
        }
    }

    private sealed class StateAwarePlanner : IQueryPlanningClient
    {
        public Task<QueryPlanningResponse> PlanAsync(
            QueryPlanningRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(
                string.IsNullOrWhiteSpace(
                    request.ClarificationResponse)
                    ? new QueryPlanningResponse(
                        ExternalOperationStatus
                            .WaitingForClarification,
                        null,
                        "Hangi dönem?",
                        null,
                        null)
                    : new QueryPlanningResponse(
                        ExternalOperationStatus.Completed,
                        "{}",
                        null,
                        "query://clarified",
                        null));
    }
}
