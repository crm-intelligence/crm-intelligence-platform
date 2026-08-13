using System.Net;
using System.Net.Http.Json;
using crm_project.Models;

namespace crm_project.Tests;

public class RlsAuthorizationTests :
    IClassFixture<TestWebApplicationFactory>
{
    private readonly HttpClient _client;

    public RlsAuthorizationTests(
        TestWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task TrManager_CanAccess_TrRegion()
    {
        AddTestUserHeaders(
            userId: "tr-manager",
            role: "RegionManager",
            region: "TR");

        var request = CreateRequest(
            requestId: "rls-tr-001",
            region: "TR");

        var response = await _client.PostAsJsonAsync(
            "/api/requests",
            request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task TrManager_CannotAccess_EuRegion()
    {
        AddTestUserHeaders(
            userId: "tr-manager",
            role: "RegionManager",
            region: "TR");

        var request = CreateRequest(
            requestId: "rls-tr-002",
            region: "EU");

        var response = await _client.PostAsJsonAsync(
            "/api/requests",
            request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task EuManager_CanAccess_EuRegion()
    {
        AddTestUserHeaders(
            userId: "eu-manager",
            role: "RegionManager",
            region: "EU");

        var request = CreateRequest(
            requestId: "rls-eu-001",
            region: "EU");

        var response = await _client.PostAsJsonAsync(
            "/api/requests",
            request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task EuManager_CannotAccess_TrRegion()
    {
        AddTestUserHeaders(
            userId: "eu-manager",
            role: "RegionManager",
            region: "EU");

        var request = CreateRequest(
            requestId: "rls-eu-002",
            region: "TR");

        var response = await _client.PostAsJsonAsync(
            "/api/requests",
            request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Admin_CanAccess_TrRegion()
    {
        AddTestUserHeaders(
            userId: "admin-user",
            role: "Admin",
            region: "ALL");

        var request = CreateRequest(
            requestId: "rls-admin-001",
            region: "TR");

        var response = await _client.PostAsJsonAsync(
            "/api/requests",
            request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Admin_CanAccess_EuRegion()
    {
        AddTestUserHeaders(
            userId: "admin-user",
            role: "Admin",
            region: "ALL");

        var request = CreateRequest(
            requestId: "rls-admin-002",
            region: "EU");

        var response = await _client.PostAsJsonAsync(
            "/api/requests",
            request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private void AddTestUserHeaders(
        string userId,
        string role,
        string region)
    {
        _client.DefaultRequestHeaders.Remove("X-Test-User");
        _client.DefaultRequestHeaders.Remove("X-Test-Role");
        _client.DefaultRequestHeaders.Remove("X-Test-Region");

        _client.DefaultRequestHeaders.Add(
            "X-Test-User",
            userId);

        _client.DefaultRequestHeaders.Add(
            "X-Test-Role",
            role);

        _client.DefaultRequestHeaders.Add(
            "X-Test-Region",
            region);
    }

    private static CanonicalRequest CreateRequest(
        string requestId,
        string region)
    {
        return new CanonicalRequest
        {
            RequestId = requestId,
            Prompt = "Satış raporunu göster",
            UseCase = "sales_report",
            UserId = "test-user",
            Source = "crm",
            TargetTable = "sales",
            Region = region,
            Parameters = new Dictionary<string, object>()
        };
    }
}