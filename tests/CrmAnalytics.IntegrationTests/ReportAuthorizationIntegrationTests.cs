using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CrmAnalytics.Application.Abstractions.Persistence;
using CrmAnalytics.Application.Exceptions;
using CrmAnalytics.Contracts.Conversations;
using CrmAnalytics.Contracts.ReportRequests;
using CrmAnalytics.Domain.ReportRequests;
using Microsoft.Extensions.DependencyInjection;

namespace CrmAnalytics.IntegrationTests;

public sealed class ReportAuthorizationIntegrationTests
{
    private const string AssignedUser =
        "11111111-1111-4111-8111-111111111111";
    private const string UnassignedUser =
        "cccccccc-cccc-4ccc-8ccc-cccccccccccc";
    private const string Tenant =
        "22222222-2222-4222-8222-222222222222";

    [Theory]
    [InlineData(null, HttpStatusCode.Forbidden)]
    [InlineData("Report.Viewer", HttpStatusCode.Forbidden)]
    [InlineData("report.user", HttpStatusCode.Forbidden)]
    [InlineData("Report.User", HttpStatusCode.Accepted)]
    public async Task Create_RequiresExactConfiguredAppRole(
        string? roles,
        HttpStatusCode expected)
    {
        await using var factory = new SecuredTestWebApplicationFactory();
        using var client = Client(
            factory,
            AssignedUser,
            roles);

        var response = await client.PostAsJsonAsync(
            "/api/report-requests",
            CreateRequest($"role-{roles ?? "missing"}"));

        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task GroupsAndWids_DoNotGrantApplicationPermission()
    {
        await using var factory = new SecuredTestWebApplicationFactory();
        using var client = Client(factory, AssignedUser, roles: null);
        client.DefaultRequestHeaders.Add(
            TestAuthenticationDefaults.GroupsHeader,
            "Report.User");
        client.DefaultRequestHeaders.Add(
            TestAuthenticationDefaults.DirectoryRolesHeader,
            "Report.User");

        var response = await client.PostAsJsonAsync(
            "/api/report-requests",
            CreateRequest("unsafe-claims"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task MissingAssignment_ReturnsSafe403WithoutCreatingReport()
    {
        await using var factory = new SecuredTestWebApplicationFactory();
        using var client = Client(
            factory,
            UnassignedUser,
            "Report.User");
        const string conversationId = "missing-assignment";

        var response = await client.PostAsJsonAsync(
            "/api/report-requests",
            CreateRequest(conversationId));
        var error = await response.Content
            .ReadFromJsonAsync<CrmAnalytics.Contracts.Common.ApiErrorResponse>();

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.NotNull(error);
        Assert.Equal(ForbiddenAccessException.ErrorCode, error.ErrorCode);
        Assert.Equal(ForbiddenAccessException.SafeMessage, error.Message);

        using var scope = factory.Services.CreateScope();
        var repository = scope.ServiceProvider
            .GetRequiredService<IReportRequestRepository>();
        Assert.Empty(await repository.GetByConversationIdAsync(
            conversationId,
            CancellationToken.None));
    }

    [Fact]
    public async Task Viewer_CanReadOwnExistingDataWithoutAssignment()
    {
        await using var factory = new SecuredTestWebApplicationFactory();
        const string conversationId = "viewer-existing-data";
        var report = ReportRequest.Create(
            Guid.NewGuid().ToString("N"),
            conversationId,
            null,
            "Sensitive prompt.",
            Guid.NewGuid().ToString("N"),
            UnassignedUser,
            Tenant);
        await AddAsync(factory.Services, report);
        using var viewer = Client(
            factory,
            UnassignedUser,
            "Report.Viewer");

        var get = await viewer.GetAsync(
            $"/api/report-requests/{report.RequestId}");
        var getBody = await get.Content.ReadAsStringAsync();
        var history = await viewer.GetAsync(
            $"/api/conversations/{conversationId}/report-requests");
        var historyBody = await history.Content.ReadAsStringAsync();
        var revise = await viewer.PostAsJsonAsync(
            $"/api/report-requests/{report.RequestId}/revise",
            new ReviseReportRequestRequest
            {
                Prompt = "Viewer cannot revise."
            });
        var clarify = await viewer.PostAsJsonAsync(
            $"/api/report-requests/{report.RequestId}/clarifications",
            new SubmitReportClarificationRequest
            {
                Response = "Viewer cannot clarify."
            });

        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        Assert.Equal(HttpStatusCode.OK, history.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, revise.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, clarify.StatusCode);
        AssertSafeResponse(getBody);
        AssertSafeResponse(historyBody);
        Assert.NotNull(JsonSerializer.Deserialize<
            GetConversationReportRequestsResponse>(
                historyBody,
                new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    }

    [Fact]
    public async Task MissingAssignment_StopsReviseAndClarificationMutation()
    {
        await using var factory = new SecuredTestWebApplicationFactory();
        var completed = OwnedReport(
            UnassignedUser,
            Tenant,
            "scope-revise");
        Complete(completed);
        var waiting = OwnedReport(
            UnassignedUser,
            Tenant,
            "scope-clarification");
        waiting.TransitionTo(
            ReportRequestStatus.Validating,
            waiting.UpdatedAt);
        waiting.RequestClarification(
            "Which period?",
            waiting.UpdatedAt);
        await AddAsync(factory.Services, completed);
        await AddAsync(factory.Services, waiting);
        using var user = Client(
            factory,
            UnassignedUser,
            "Report.User");

        var revise = await user.PostAsJsonAsync(
            $"/api/report-requests/{completed.RequestId}/revise",
            new ReviseReportRequestRequest
            {
                Prompt = "A valid revision prompt."
            });
        var clarify = await user.PostAsJsonAsync(
            $"/api/report-requests/{waiting.RequestId}/clarifications",
            new SubmitReportClarificationRequest
            {
                Response = "Last quarter."
            });

        Assert.Equal(HttpStatusCode.Forbidden, revise.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, clarify.StatusCode);

        using var scope = factory.Services.CreateScope();
        var repository = scope.ServiceProvider
            .GetRequiredService<IReportRequestRepository>();
        Assert.Single(await repository.GetByConversationIdAsync(
            completed.ConversationId,
            CancellationToken.None));
        var unchanged = await repository.GetByIdAsync(
            waiting.RequestId,
            CancellationToken.None);
        Assert.Null(unchanged?.ClarificationResponse);
    }

    [Fact]
    public async Task Admin_DoesNotBypassOwnership()
    {
        await using var factory = new SecuredTestWebApplicationFactory();
        var report = ReportRequest.Create(
            Guid.NewGuid().ToString("N"),
            "admin-ownership",
            null,
            "Sensitive prompt.",
            Guid.NewGuid().ToString("N"),
            UnassignedUser,
            Tenant);
        var otherTenantReport = OwnedReport(
            AssignedUser,
            "bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb",
            "admin-cross-tenant");
        await AddAsync(factory.Services, report);
        await AddAsync(factory.Services, otherTenantReport);
        using var admin = Client(
            factory,
            AssignedUser,
            "Report.Admin");

        var response = await admin.GetAsync(
            $"/api/report-requests/{report.RequestId}");
        var crossTenant = await admin.GetAsync(
            $"/api/report-requests/{otherTenantReport.RequestId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, crossTenant.StatusCode);
    }

    private static ReportRequest OwnedReport(
        string userId,
        string tenantId,
        string conversationId) =>
        ReportRequest.Create(
            Guid.NewGuid().ToString("N"),
            conversationId,
            null,
            "Sensitive prompt.",
            Guid.NewGuid().ToString("N"),
            userId,
            tenantId);

    private static void Complete(ReportRequest report)
    {
        report.TransitionTo(
            ReportRequestStatus.Validating,
            report.UpdatedAt);
        report.TransitionTo(
            ReportRequestStatus.Processing,
            report.UpdatedAt);
        report.Complete(
            "report-id",
            "Safe summary.",
            "https://app.powerbi.com/reports/report-id",
            report.UpdatedAt);
    }

    private static HttpClient Client(
        SecuredTestWebApplicationFactory factory,
        string userId,
        string? roles)
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
            Tenant);
        client.DefaultRequestHeaders.Add(
            TestAuthenticationDefaults.ScopesHeader,
            "access_as_user");

        if (roles is not null)
        {
            client.DefaultRequestHeaders.Add(
                TestAuthenticationDefaults.RolesHeader,
                roles);
        }

        return client;
    }

    private static CreateReportRequestRequest CreateRequest(
        string conversationId) =>
        new()
        {
            Prompt = "Show quarterly CRM sales.",
            ConversationId = conversationId
        };

    private static async Task AddAsync(
        IServiceProvider services,
        ReportRequest report)
    {
        using var scope = services.CreateScope();
        await scope.ServiceProvider
            .GetRequiredService<IReportRequestRepository>()
            .AddAsync(report, CancellationToken.None);
    }

    private static void AssertSafeResponse(string body)
    {
        Assert.DoesNotContain(
            "userDataScope",
            body,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "allowedRegions",
            body,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "roles",
            body,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            UnassignedUser,
            body,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            Tenant,
            body,
            StringComparison.OrdinalIgnoreCase);
    }
}
