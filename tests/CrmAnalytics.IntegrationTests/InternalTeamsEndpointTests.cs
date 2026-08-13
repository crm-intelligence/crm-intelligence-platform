using System.Net;
using System.Net.Http.Json;
using CrmAnalytics.Contracts.InternalTeams;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace CrmAnalytics.IntegrationTests;

public sealed class InternalTeamsEndpointTests
    : IClassFixture<IntegrationTestWebApplicationFactory>
{
    private const string ApiKey = "integration-internal-api-key";
    private readonly IntegrationTestWebApplicationFactory _factory;

    public InternalTeamsEndpointTests(
        IntegrationTestWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task TargetPut_IsApiKeyProtectedIdempotentAndConflictSafe()
    {
        await using var factory = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["TeamsNotifications:ApiKey"] = ApiKey
                    })));
        using var client = factory.CreateClient();
        var requestId = Guid.NewGuid().ToString("N");
        var path = $"/api/internal/teams-targets/{requestId}";

        var unauthorized = await client.PutAsJsonAsync(path,
            new RegisterTeamsTargetRequest
            {
                ConversationId = "private-conversation"
            });
        Assert.Equal(HttpStatusCode.Unauthorized,
            unauthorized.StatusCode);

        client.DefaultRequestHeaders.TryAddWithoutValidation(
            TeamsInternalHttpConstants.ApiKeyHeaderName, ApiKey);
        var created = await client.PutAsJsonAsync(path,
            new RegisterTeamsTargetRequest
            {
                ConversationId = "private-conversation"
            });
        var repeated = await client.PutAsJsonAsync(path,
            new RegisterTeamsTargetRequest
            {
                ConversationId = "private-conversation"
            });
        var conflict = await client.PutAsJsonAsync(path,
            new RegisterTeamsTargetRequest
            {
                ConversationId = "different-conversation"
            });

        Assert.Equal(HttpStatusCode.NoContent, created.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, repeated.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        Assert.DoesNotContain("private-conversation",
            await conflict.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task ActionEndpoints_CompleteReturnsPersistentDuplicate()
    {
        await using var factory = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["TeamsNotifications:ApiKey"] = ApiKey
                    })));
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.TryAddWithoutValidation(
            TeamsInternalHttpConstants.ApiKeyHeaderName, ApiKey);
        var token = new string('A', 64);
        var claimPath = $"/api/internal/teams-actions/{token}/claim";
        var claimRequest = new ClaimTeamsActionRequest
        {
            RequestId = Guid.NewGuid().ToString("N"),
            ActionType = "revise-report",
            LockOwner = "worker-1"
        };
        var claimed = await client.PostAsJsonAsync(claimPath, claimRequest);
        var claim = await claimed.Content
            .ReadFromJsonAsync<ClaimTeamsActionResponse>();
        Assert.Equal("Claimed", claim!.Result);

        var resultRequestId = Guid.NewGuid().ToString("N");
        var completed = await client.PostAsJsonAsync(
            $"/api/internal/teams-actions/{token}/complete",
            new CompleteTeamsActionRequest
            {
                LockOwner = "worker-1",
                ResultRequestId = resultRequestId
            });
        Assert.Equal(HttpStatusCode.NoContent, completed.StatusCode);

        var duplicate = await client.PostAsJsonAsync(claimPath,
            claimRequest with { LockOwner = "worker-2" });
        var duplicateClaim = await duplicate.Content
            .ReadFromJsonAsync<ClaimTeamsActionResponse>();
        Assert.Equal("Completed", duplicateClaim!.Result);
        Assert.Equal(resultRequestId, duplicateClaim.ResultRequestId);
    }
}
