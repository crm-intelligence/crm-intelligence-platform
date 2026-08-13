extern alias TeamsHost;

using System.Net;
using CrmAnalytics.Contracts.ReportRequests;
using BackendApiAuthorization =
    TeamsHost::CrmAnalytics.Teams.Authentication.BackendApiAuthorization;
using ReportRequestsApiClient =
    TeamsHost::CrmAnalytics.Teams.Backend.ReportRequestsApiClient;

namespace CrmAnalytics.IntegrationTests;

public sealed class DevelopmentTeamsBackendCompatibilityTests
    : IClassFixture<IntegrationTestWebApplicationFactory>
{
    private readonly IntegrationTestWebApplicationFactory _factory;

    public DevelopmentTeamsBackendCompatibilityTests(
        IntegrationTestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task DevelopmentBackend_AcceptsTeamsClientWithoutBearerToken()
    {
        using var httpClient = _factory.CreateClient();
        var teamsClient = new ReportRequestsApiClient(httpClient);

        var response = await teamsClient.CreateAsync(
            new CreateReportRequestRequest
            {
                Prompt = "Show quarterly sales.",
                ConversationId = $"teams-local-{Guid.NewGuid():N}"
            },
            BackendApiAuthorization.Development(),
            CancellationToken.None);

        Assert.False(string.IsNullOrWhiteSpace(response.RequestId));
        Assert.Equal("Received", response.Status);
        Assert.DoesNotContain(
            httpClient.DefaultRequestHeaders,
            header => string.Equals(
                header.Key,
                "Authorization",
                StringComparison.OrdinalIgnoreCase));
    }
}
