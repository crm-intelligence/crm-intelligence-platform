using System.Security.Claims;
using CrmAnalytics.Api.Authentication;
using CrmAnalytics.Application.Authorization;
using CrmAnalytics.Application.Identity;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CrmAnalytics.UnitTests;

public sealed class ReportRequestAuthorizationDiagnosticMiddlewareTests
{
    private const string UserId =
        "eac148db-46b9-4895-9a5e-834ef358384b";
    private const string TenantId =
        "2e010224-86ea-4b34-93ea-f9833137c80e";
    private const string BearerToken =
        "Bearer raw-token-must-never-appear";

    [Fact]
    public async Task MissingRole_ProducesRoleMappingReason()
    {
        var log = await InvokeAsync(
            roles: [],
            permissionGranted: false,
            UserDataAccessAssignmentInspection.NotFound);

        Assert.Contains("ForbiddenStage=RoleMapping", log);
        Assert.Contains("ReportUserRoleMappingResult=False", log);
    }

    [Fact]
    public async Task MissingScopeAssignment_ProducesNotFoundReason()
    {
        var log = await InvokeAsync(
            roles: [ReportAppRoles.User],
            permissionGranted: true,
            UserDataAccessAssignmentInspection.NotFound);

        Assert.Contains("ForbiddenStage=DataScopeNotFound", log);
        Assert.Contains("ScopeAssignmentFound=False", log);
    }

    [Fact]
    public async Task InactiveScopeAssignment_ProducesInactiveReason()
    {
        var log = await InvokeAsync(
            roles: [ReportAppRoles.User],
            permissionGranted: true,
            new UserDataAccessAssignmentInspection(
                true,
                false,
                true,
                true,
                true));

        Assert.Contains("ForbiddenStage=DataScopeInactive", log);
        Assert.Contains("ScopeAssignmentFound=True", log);
        Assert.Contains("ScopeIsActive=False", log);
    }

    [Fact]
    public async Task DiagnosticLog_DoesNotContainFullIdentifiersOrBearerToken()
    {
        var log = await InvokeAsync(
            roles: [ReportAppRoles.User],
            permissionGranted: true,
            new UserDataAccessAssignmentInspection(
                true,
                true,
                true,
                true,
                true));

        Assert.DoesNotContain(UserId, log, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(TenantId, log, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(BearerToken, log, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "raw-token-must-never-appear",
            log,
            StringComparison.Ordinal);
        Assert.Contains("TokenOidSuffix=58384b", log);
        Assert.Contains("TokenTidSuffix=37c80e", log);
    }

    [Fact]
    public async Task DiagnosticAndAuthorization_UseSameResolverForMappedScope()
    {
        var principal = Principal(
            [ReportAppRoles.User],
            MicrosoftIdentityClaimTypes.ScopeUri);
        var log = await InvokeAsync(
            roles: [ReportAppRoles.User],
            permissionGranted: true,
            UserDataAccessAssignmentInspection.NotFound,
            principal);
        var requirement = new ReportsAccessRequirement();
        var authorizationContext = new AuthorizationHandlerContext(
            [requirement],
            principal,
            resource: null);
        var handler = new ReportsAccessAuthorizationHandler(
            Options.Create(new CrmAnalyticsAuthenticationOptions
            {
                RequiredScope = "access_as_user"
            }));

        await handler.HandleAsync(authorizationContext);

        Assert.Contains("HasAccessAsUserScope=True", log);
        Assert.True(authorizationContext.HasSucceeded);
    }

    private static async Task<string> InvokeAsync(
        string[] roles,
        bool permissionGranted,
        UserDataAccessAssignmentInspection inspection,
        ClaimsPrincipal? principal = null)
    {
        var logger = new RecordingLogger<
            ReportRequestAuthorizationDiagnosticMiddleware>();
        var middleware = new ReportRequestAuthorizationDiagnosticMiddleware(
            context =>
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return Task.CompletedTask;
            },
            logger,
            Options.Create(new CrmAnalyticsAuthenticationOptions
            {
                Mode = AuthenticationModes.Entra,
                RequiredScope = "access_as_user"
            }),
            Options.Create(new AzureAdOptions
            {
                TenantId = TenantId,
                ClientId = "33333333-3333-4333-8333-333333333333",
                Audience =
                    "api://33333333-3333-4333-8333-333333333333"
            }));
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = "/api/report-requests";
        context.Request.Headers.Authorization = BearerToken;
        context.User = principal ?? Principal(roles);

        await middleware.InvokeAsync(
            context,
            new Policy(),
            new Evaluator(permissionGranted),
            new Store(inspection));

        return Assert.Single(logger.Messages);
    }

    private static ClaimsPrincipal Principal(
        IEnumerable<string> roles,
        string scopeClaimType = MicrosoftIdentityClaimTypes.Scope)
    {
        var claims = new List<Claim>
        {
            new(MicrosoftIdentityClaimTypes.ObjectId, UserId),
            new(MicrosoftIdentityClaimTypes.TenantId, TenantId),
            new(scopeClaimType, "access_as_user"),
            new(MicrosoftIdentityClaimTypes.IdentityType, "user")
        };
        claims.AddRange(roles.Select(role =>
            new Claim(MicrosoftIdentityClaimTypes.Roles, role)));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
    }

    private sealed class Policy : IReportRolePermissionPolicy
    {
        public bool RoleHasPermission(
            string role,
            ReportPermission permission) =>
            role == ReportAppRoles.User
            && permission == ReportPermission.Create;
    }

    private sealed class Evaluator(bool permissionGranted)
        : IReportPermissionEvaluator
    {
        public bool HasPermission(
            AuthenticatedUserContext user,
            ReportPermission permission) => permissionGranted;

        public void EnsureHasPermission(
            AuthenticatedUserContext user,
            ReportPermission permission)
        {
            if (!permissionGranted)
            {
                throw new InvalidOperationException();
            }
        }
    }

    private sealed class Store(UserDataAccessAssignmentInspection inspection)
        : IUserDataAccessAssignmentStore
    {
        public UserDataAccessAssignment? Find(
            string tenantId,
            string userId) => null;

        public Task<UserDataAccessAssignmentInspection> InspectAsync(
            string tenantId,
            string userId,
            CancellationToken cancellationToken) =>
            Task.FromResult(inspection);
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Messages.Add(formatter(state, exception));
    }
}
