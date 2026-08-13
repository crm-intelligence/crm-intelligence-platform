using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CrmAnalytics.Application.Abstractions.Persistence;
using CrmAnalytics.Application.ReportRequests;
using CrmAnalytics.Contracts.Conversations;
using CrmAnalytics.Contracts.ReportRequests;
using CrmAnalytics.Contracts.CopilotStudio;
using CrmAnalytics.Domain.ReportRequests;
using Microsoft.Extensions.DependencyInjection;

namespace CrmAnalytics.IntegrationTests;

public sealed class BackendAuthenticationAndOwnershipTests
{
    private const string UserA =
        "11111111-1111-4111-8111-111111111111";
    private const string UserB =
        "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa";
    private const string TenantA =
        "22222222-2222-4222-8222-222222222222";
    private const string TenantB =
        "bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb";

    [Fact]
    public async Task SecuredHost_EnforcesAuthenticationAndScope()
    {
        await using var factory = new SecuredTestWebApplicationFactory();
        using var anonymous = factory.CreateClient();

        var unauthorized = await anonymous.PostAsJsonAsync(
            "/api/report-requests",
            CreateRequest("anonymous"));
        var health = await anonymous.GetAsync("/health");

        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);

        using var missingScope = factory.CreateClient();
        SetIdentity(missingScope, UserA, TenantA, scopes: null);
        var forbidden = await missingScope.PostAsJsonAsync(
            "/api/report-requests",
            CreateRequest("missing-scope"));

        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        using var authorized = factory.CreateClient();
        SetIdentity(authorized, UserA, TenantA, "read access_as_user");
        var accepted = await authorized.PostAsJsonAsync(
            "/api/report-requests",
            CreateRequest("authorized"));

        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
    }

    [Fact]
    public async Task ReportEndpoints_EnforceUserAndTenantOwnership()
    {
        await using var factory = new SecuredTestWebApplicationFactory();
        using var userA = factory.CreateClient();
        SetIdentity(userA, UserA, TenantA, "access_as_user");
        var createResponse = await userA.PostAsJsonAsync(
            "/api/report-requests",
            CreateRequest("ownership"));
        var created = await createResponse.Content
            .ReadFromJsonAsync<CreateReportRequestResponse>();

        Assert.Equal(HttpStatusCode.Accepted, createResponse.StatusCode);
        Assert.NotNull(created);

        var ownGet = await userA.GetAsync(
            $"/api/report-requests/{created.RequestId}");
        var ownBody = await ownGet.Content.ReadAsStringAsync();
        using var ownDocument = JsonDocument.Parse(ownBody);
        Assert.Equal(HttpStatusCode.OK, ownGet.StatusCode);
        Assert.False(ownDocument.RootElement.TryGetProperty("userId", out _));
        Assert.False(ownDocument.RootElement.TryGetProperty("tenantId", out _));

        using var userB = factory.CreateClient();
        SetIdentity(userB, UserB, TenantA, "access_as_user");
        var otherUserGet = await userB.GetAsync(
            $"/api/report-requests/{created.RequestId}");
        Assert.Equal(HttpStatusCode.NotFound, otherUserGet.StatusCode);

        using var otherTenant = factory.CreateClient();
        SetIdentity(otherTenant, UserA, TenantB, "access_as_user");
        var otherTenantGet = await otherTenant.GetAsync(
            $"/api/report-requests/{created.RequestId}");
        Assert.Equal(HttpStatusCode.NotFound, otherTenantGet.StatusCode);

        await CompleteAsync(factory.Services, created.RequestId);
        var revise = await userB.PostAsJsonAsync(
            $"/api/report-requests/{created.RequestId}/revise",
            new ReviseReportRequestRequest
            {
                Prompt = "Try to revise another user's report."
            });
        Assert.Equal(HttpStatusCode.NotFound, revise.StatusCode);
        var plannedRevise = await userB.PostAsJsonAsync(
            $"/api/report-requests/{created.RequestId}/planned-revision",
            new CopilotPlannedRevisionRequest
            {
                RevisionInstruction = "Revise another user's report.",
                Plan = AcceptedPlan()
            });
        Assert.Equal(HttpStatusCode.NotFound, plannedRevise.StatusCode);

        var waiting = await CreateWaitingForClarificationAsync(
            factory.Services,
            userA);
        var clarify = await otherTenant.PostAsJsonAsync(
            $"/api/report-requests/{waiting}/clarifications",
            new SubmitReportClarificationRequest
            {
                Response = "Last quarter."
            });
        Assert.Equal(HttpStatusCode.NotFound, clarify.StatusCode);
        var plannedClarify = await otherTenant.PostAsJsonAsync(
            $"/api/report-requests/{waiting}/planned-clarification",
            new CopilotPlannedClarificationRequest
            {
                Answer = "2018 yili",
                Plan = AcceptedPlan()
            });
        Assert.Equal(HttpStatusCode.NotFound, plannedClarify.StatusCode);
        var planningContext = await userB.GetAsync(
            $"/api/report-requests/{created.RequestId}/planning-context");
        Assert.Equal(HttpStatusCode.NotFound, planningContext.StatusCode);

        using var scope = factory.Services.CreateScope();
        var repository = scope.ServiceProvider
            .GetRequiredService<IReportRequestRepository>();
        var ownerHistory = await repository.GetByConversationIdAsync(
            "ownership",
            CancellationToken.None);
        Assert.Single(ownerHistory);
        var waitingRequest = await repository.GetByIdAsync(
            waiting,
            CancellationToken.None);
        Assert.Null(waitingRequest?.ClarificationResponse);
    }

    private static CopilotSemanticPlan AcceptedPlan() => new()
    {
        Outcome = "accepted",
        SemanticIntent = new CopilotSemanticIntent
        {
            Metric = "order_count",
            GroupBy = [],
            Filters = [],
            Date = new CopilotDateIntent
            {
                Kind = "absolute",
                From = "2018-05-01",
                To = "2018-05-31",
                Grain = "none"
            },
            Ranking = new CopilotRankingIntent()
        },
        UnresolvedConcepts = [],
        Clarification = new CopilotClarification()
    };

    [Fact]
    public async Task ConversationHistory_IsFilteredAndLastIdIsUserSpecific()
    {
        await using var factory = new SecuredTestWebApplicationFactory();
        const string conversationId = "shared-seeded-conversation";
        var userAFirst = OwnedRequest(UserA, TenantA, conversationId);
        var userBOnly = OwnedRequest(UserB, TenantA, conversationId);
        var userALast = OwnedRequest(UserA, TenantA, conversationId);

        using (var scope = factory.Services.CreateScope())
        {
            var repository = scope.ServiceProvider
                .GetRequiredService<IReportRequestRepository>();

            foreach (var request in new[]
                     {
                         userAFirst,
                         userBOnly,
                         userALast
                     })
            {
                await repository.AddAsync(
                    request,
                    CancellationToken.None);
            }
        }

        using var userA = factory.CreateClient();
        SetIdentity(userA, UserA, TenantA, "access_as_user");
        var responseA = await userA.GetFromJsonAsync<
            GetConversationReportRequestsResponse>(
                $"/api/conversations/{conversationId}/report-requests");

        using var userB = factory.CreateClient();
        SetIdentity(userB, UserB, TenantA, "access_as_user");
        var responseB = await userB.GetFromJsonAsync<
            GetConversationReportRequestsResponse>(
                $"/api/conversations/{conversationId}/report-requests");

        Assert.NotNull(responseA);
        Assert.Equal(2, responseA.ReportRequests.Count);
        Assert.Equal(userALast.RequestId, responseA.LastRequestId);
        Assert.DoesNotContain(
            responseA.ReportRequests,
            request => request.RequestId == userBOnly.RequestId);
        Assert.NotNull(responseB);
        Assert.Single(responseB.ReportRequests);
        Assert.Equal(userBOnly.RequestId, responseB.LastRequestId);
    }

    private static async Task<string> CreateWaitingForClarificationAsync(
        IServiceProvider services,
        HttpClient user)
    {
        var response = await user.PostAsJsonAsync(
            "/api/report-requests",
            CreateRequest("clarification-owner"));
        var created = await response.Content
            .ReadFromJsonAsync<CreateReportRequestResponse>();
        Assert.NotNull(created);

        using var scope = services.CreateScope();
        var service = scope.ServiceProvider
            .GetRequiredService<IReportRequestService>();
        var now = DateTimeOffset.UtcNow.AddMinutes(1);
        await service.TransitionStatusAsync(
            new TransitionReportRequestStatusCommand(
                created.RequestId,
                ReportRequestStatus.Validating,
                now),
            CancellationToken.None);
        await service.RequestClarificationAsync(
            new RequestReportClarificationCommand(
                created.RequestId,
                "Which reporting period should be used?",
                now.AddMinutes(1)),
            CancellationToken.None);
        return created.RequestId;
    }

    private static async Task CompleteAsync(
        IServiceProvider services,
        string requestId)
    {
        using var scope = services.CreateScope();
        var service = scope.ServiceProvider
            .GetRequiredService<IReportRequestService>();
        var now = DateTimeOffset.UtcNow.AddMinutes(1);
        await service.TransitionStatusAsync(
            new TransitionReportRequestStatusCommand(
                requestId,
                ReportRequestStatus.Validating,
                now),
            CancellationToken.None);
        await service.TransitionStatusAsync(
            new TransitionReportRequestStatusCommand(
                requestId,
                ReportRequestStatus.Processing,
                now.AddMinutes(1)),
            CancellationToken.None);
        await service.CompleteAsync(
            new CompleteReportRequestCommand(
                requestId,
                "report",
                "Safe summary.",
                "https://app.powerbi.com/reports/report",
                now.AddMinutes(2)),
            CancellationToken.None);
    }

    private static void SetIdentity(
        HttpClient client,
        string userId,
        string tenantId,
        string? scopes)
    {
        client.DefaultRequestHeaders.Add(
            TestAuthenticationDefaults.AuthenticatedHeader,
            "true");
        client.DefaultRequestHeaders.Add(
            TestAuthenticationDefaults.UserIdHeader,
            userId);
        client.DefaultRequestHeaders.Add(
            TestAuthenticationDefaults.TenantIdHeader,
            tenantId);
        client.DefaultRequestHeaders.Add(
            TestAuthenticationDefaults.RolesHeader,
            "Report.User");

        if (scopes is not null)
        {
            client.DefaultRequestHeaders.Add(
                TestAuthenticationDefaults.ScopesHeader,
                scopes);
        }
    }

    private static CreateReportRequestRequest CreateRequest(
        string conversationId) =>
        new()
        {
            Prompt = "Show quarterly sales.",
            ConversationId = conversationId
        };

    private static ReportRequest OwnedRequest(
        string userId,
        string tenantId,
        string conversationId) =>
        ReportRequest.Create(
            Guid.NewGuid().ToString("N"),
            conversationId,
            null,
            "Secret prompt.",
            Guid.NewGuid().ToString("N"),
            userId,
            tenantId);
}
