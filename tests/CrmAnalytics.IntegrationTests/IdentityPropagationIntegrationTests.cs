using System.Net;
using System.Net.Http.Json;
using CrmAnalytics.Application.Abstractions.Integrations;
using CrmAnalytics.Contracts.Integrations;
using CrmAnalytics.Contracts.ReportRequests;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Threading.Channels;

namespace CrmAnalytics.IntegrationTests;

public sealed class IdentityPropagationIntegrationTests
    : IClassFixture<IntegrationTestWebApplicationFactory>
{
    private readonly IntegrationTestWebApplicationFactory _baseFactory;

    public IdentityPropagationIntegrationTests(
        IntegrationTestWebApplicationFactory baseFactory)
    {
        _baseFactory = baseFactory;
    }

    [Fact]
    public async Task DevelopmentUserScope_IsRevalidatedWithoutTokenRoles()
    {
        var recorder = new RecordingQueryPlanningClient();
        await using var factory = _baseFactory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["ReportProcessing:Queue:Enabled"] = "true"
                    });
            });
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IQueryPlanningClient>();
                services.AddSingleton<IQueryPlanningClient>(recorder);
            });
        });
        using var client = factory.CreateClient();

        var createResponse = await client.PostAsJsonAsync(
            "/api/report-requests",
            new CreateReportRequestRequest
            {
                Prompt = "Show quarterly sales.",
                ConversationId = $"identity-scope-{Guid.NewGuid():N}"
            });
        var created = await createResponse.Content
            .ReadFromJsonAsync<CreateReportRequestResponse>();
        var planningRequest = await recorder.Request.Task.WaitAsync(
            TimeSpan.FromSeconds(5));

        Assert.Equal(HttpStatusCode.Accepted, createResponse.StatusCode);
        Assert.NotNull(created);
        Assert.Equal(
            IntegrationTestWebApplicationFactory.UserId,
            planningRequest.UserDataScope.UserId);
        Assert.Equal(
            IntegrationTestWebApplicationFactory.TenantId,
            planningRequest.UserDataScope.TenantId);
        Assert.Empty(planningRequest.UserDataScope.Roles);
        Assert.Empty(planningRequest.UserDataScope.AllowedRegions);
        Assert.Empty(planningRequest.UserDataScope.AllowedStoreIds);
        Assert.True(planningRequest.UserDataScope.AllowAllRegions);
        Assert.True(planningRequest.UserDataScope.AllowAllStores);
    }

    [Fact]
    public async Task ExplicitDataScope_ReachesQueryPlannerAsSnapshot()
    {
        var recorder = new RecordingQueryPlanningClient();
        await using var factory = _baseFactory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["ReportProcessing:Queue:Enabled"] = "true",
                        ["ReportDataAccess:Assignments:0:AllowAllRegions"] =
                            "false",
                        ["ReportDataAccess:Assignments:0:AllowAllStores"] =
                            "false",
                        ["ReportDataAccess:Assignments:0:AllowedRegions:0"] =
                            "Marmara",
                        ["ReportDataAccess:Assignments:0:AllowedRegions:1"] =
                            "Ege",
                        ["ReportDataAccess:Assignments:0:AllowedStoreIds:0"] =
                            "STORE-001"
                    });
            });
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IQueryPlanningClient>();
                services.AddSingleton<IQueryPlanningClient>(recorder);
            });
        });
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/report-requests",
            new CreateReportRequestRequest
            {
                Prompt = "Show scoped sales.",
                ConversationId = $"explicit-scope-{Guid.NewGuid():N}"
            });
        var planningRequest = await recorder.Request.Task.WaitAsync(
            TimeSpan.FromSeconds(5));

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.False(planningRequest.UserDataScope.AllowAllRegions);
        Assert.False(planningRequest.UserDataScope.AllowAllStores);
        Assert.Equal(
            ["Marmara", "Ege"],
            planningRequest.UserDataScope.AllowedRegions);
        Assert.Equal(
            ["STORE-001"],
            planningRequest.UserDataScope.AllowedStoreIds);
    }

    [Fact]
    public async Task SameTenantUsers_ReceiveOnlyTheirAssignments()
    {
        var recorder = new MultiRecordingQueryPlanningClient();
        await using var baseFactory =
            new SecuredTestWebApplicationFactory();
        await using var factory = baseFactory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["ReportProcessing:Queue:Enabled"] = "true",
                        ["ReportDataAccess:Assignments:0:AllowAllRegions"] =
                            "false",
                        ["ReportDataAccess:Assignments:0:AllowAllStores"] =
                            "false",
                        ["ReportDataAccess:Assignments:0:AllowedRegions:0"] =
                            "Marmara",
                        ["ReportDataAccess:Assignments:0:AllowedStoreIds:0"] =
                            "STORE-A",
                        ["ReportDataAccess:Assignments:1:AllowAllRegions"] =
                            "false",
                        ["ReportDataAccess:Assignments:1:AllowAllStores"] =
                            "false",
                        ["ReportDataAccess:Assignments:1:AllowedRegions:0"] =
                            "Ege",
                        ["ReportDataAccess:Assignments:1:AllowedStoreIds:0"] =
                            "STORE-B"
                    });
            });
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IQueryPlanningClient>();
                services.AddSingleton<IQueryPlanningClient>(recorder);
            });
        });
        using var userA = SecuredClient(
            factory,
            "11111111-1111-4111-8111-111111111111");
        using var userB = SecuredClient(
            factory,
            "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");

        var responseA = await userA.PostAsJsonAsync(
            "/api/report-requests",
            new CreateReportRequestRequest
            {
                Prompt = "User A report.",
                ConversationId = "scope-user-a"
            });
        var responseB = await userB.PostAsJsonAsync(
            "/api/report-requests",
            new CreateReportRequestRequest
            {
                Prompt = "User B report.",
                ConversationId = "scope-user-b"
            });
        var first = await recorder.Requests.Reader.ReadAsync()
            .AsTask()
            .WaitAsync(TimeSpan.FromSeconds(5));
        var second = await recorder.Requests.Reader.ReadAsync()
            .AsTask()
            .WaitAsync(TimeSpan.FromSeconds(5));
        var requests = new[] { first, second };
        var scopeA = requests.Single(request =>
            request.UserDataScope.UserId
                == "11111111-1111-4111-8111-111111111111")
            .UserDataScope;
        var scopeB = requests.Single(request =>
            request.UserDataScope.UserId
                == "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa")
            .UserDataScope;

        Assert.Equal(HttpStatusCode.Accepted, responseA.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, responseB.StatusCode);
        Assert.Equal(["Marmara"], scopeA.AllowedRegions);
        Assert.Equal(["STORE-A"], scopeA.AllowedStoreIds);
        Assert.Equal(["Ege"], scopeB.AllowedRegions);
        Assert.Equal(["STORE-B"], scopeB.AllowedStoreIds);
    }

    private static HttpClient SecuredClient(
        WebApplicationFactory<Program> factory,
        string userId)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(
            TestAuthenticationDefaults.AuthenticatedHeader,
            "true");
        client.DefaultRequestHeaders.Add(
            TestAuthenticationDefaults.UserIdHeader,
            userId);
        client.DefaultRequestHeaders.Add(
            TestAuthenticationDefaults.TenantIdHeader,
            "22222222-2222-4222-8222-222222222222");
        client.DefaultRequestHeaders.Add(
            TestAuthenticationDefaults.ScopesHeader,
            "access_as_user");
        client.DefaultRequestHeaders.Add(
            TestAuthenticationDefaults.RolesHeader,
            "Report.User");
        return client;
    }

    private sealed class RecordingQueryPlanningClient
        : IQueryPlanningClient
    {
        public TaskCompletionSource<QueryPlanningRequest> Request { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<QueryPlanningResponse> PlanAsync(
            QueryPlanningRequest request,
            CancellationToken cancellationToken)
        {
            Request.TrySetResult(request);
            return Task.FromResult(new QueryPlanningResponse(
                ExternalOperationStatus.Completed,
                "canonical-query",
                null,
                "query-reference",
                null));
        }
    }

    private sealed class MultiRecordingQueryPlanningClient
        : IQueryPlanningClient
    {
        public Channel<QueryPlanningRequest> Requests { get; } =
            Channel.CreateUnbounded<QueryPlanningRequest>();

        public Task<QueryPlanningResponse> PlanAsync(
            QueryPlanningRequest request,
            CancellationToken cancellationToken)
        {
            Requests.Writer.TryWrite(request);
            return Task.FromResult(new QueryPlanningResponse(
                ExternalOperationStatus.Completed,
                "canonical-query",
                null,
                $"query-reference-{request.RequestId}",
                null));
        }
    }
}
