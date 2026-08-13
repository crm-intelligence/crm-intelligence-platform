using System.Security.Claims;
using CrmAnalytics.Api.Authentication;
using CrmAnalytics.Application.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace CrmAnalytics.UnitTests;

public sealed class IdentityAndAuthorizationTests
{
    private const string UserId =
        "11111111-1111-4111-8111-111111111111";
    private const string TenantId =
        "22222222-2222-4222-8222-222222222222";

    [Fact]
    public void Accessor_ReadsRawClaimsAndDeduplicatesRoles()
    {
        var principal = Principal(
            new Claim("oid", UserId),
            new Claim("tid", TenantId),
            new Claim("roles", " Report.User "),
            new Claim(ClaimTypes.Role, "report.user"),
            new Claim(ClaimTypes.Role, "Sales"));

        var user = Accessor(principal).GetRequiredUser();

        Assert.Equal(UserId, user.UserId);
        Assert.Equal(TenantId, user.TenantId);
        Assert.Equal(3, user.Roles.Count);
        Assert.Contains("Report.User", user.Roles);
        Assert.Contains("report.user", user.Roles);
        Assert.Contains("Sales", user.Roles);
    }

    [Fact]
    public void Accessor_ReadsSupportedUriClaims()
    {
        var principal = Principal(
            new Claim(MicrosoftIdentityClaimTypes.ObjectIdUri, UserId),
            new Claim(MicrosoftIdentityClaimTypes.TenantIdUri, TenantId));

        var user = Accessor(principal).GetRequiredUser();

        Assert.Equal(UserId, user.UserId);
        Assert.Equal(TenantId, user.TenantId);
    }

    [Theory]
    [InlineData("oid")]
    [InlineData("tid")]
    public void Accessor_MissingRequiredIdentityClaim_IsRejected(
        string missingClaim)
    {
        var claims = new List<Claim>();

        if (missingClaim != "oid")
        {
            claims.Add(new Claim("oid", UserId));
        }

        if (missingClaim != "tid")
        {
            claims.Add(new Claim("tid", TenantId));
        }

        Assert.Throws<InvalidOperationException>(
            () => Accessor(Principal(claims.ToArray())).GetRequiredUser());
    }

    [Theory]
    [InlineData(ClaimTypes.Name, "Alice")]
    [InlineData(ClaimTypes.Email, "alice@example.test")]
    [InlineData("sub", "subject")]
    public void Accessor_DoesNotUseUnsafeUserIdFallback(
        string claimType,
        string claimValue)
    {
        var principal = Principal(
            new Claim(claimType, claimValue),
            new Claim("tid", TenantId));

        Assert.Throws<InvalidOperationException>(
            () => Accessor(principal).GetRequiredUser());
    }

    [Fact]
    public void Accessor_UnauthenticatedPrincipal_IsRejected()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim("oid", UserId),
            new Claim("tid", TenantId)
        ]));

        Assert.Throws<InvalidOperationException>(
            () => Accessor(principal).GetRequiredUser());
    }

    [Theory]
    [InlineData(true, true, "access_as_user", "user", true)]
    [InlineData(true, true, null, "user", false)]
    [InlineData(true, true, "other", "user", false)]
    [InlineData(false, true, "access_as_user", "user", false)]
    [InlineData(true, false, "access_as_user", "user", false)]
    [InlineData(true, true, "access_as_user_extra", "user", false)]
    [InlineData(true, true, "read access_as_user write", "user", true)]
    [InlineData(true, true, "access_as_user", "app", false)]
    public async Task ReportsAccess_EnforcesIdentityAndExactDelegatedScope(
        bool hasOid,
        bool hasTid,
        string? scopes,
        string identityType,
        bool expected)
    {
        var claims = new List<Claim>
        {
            new("idtyp", identityType),
            new("roles", "Report.Admin")
        };

        if (hasOid)
        {
            claims.Add(new Claim("oid", UserId));
        }

        if (hasTid)
        {
            claims.Add(new Claim("tid", TenantId));
        }

        if (scopes is not null)
        {
            claims.Add(new Claim("scp", scopes));
        }

        var requirement = new ReportsAccessRequirement();
        var context = new AuthorizationHandlerContext(
            [requirement],
            Principal(claims.ToArray()),
            resource: null);
        var handler = new ReportsAccessAuthorizationHandler(
            Options.Create(new CrmAnalyticsAuthenticationOptions
            {
                RequiredScope = "access_as_user"
            }));

        await handler.HandleAsync(context);

        Assert.Equal(expected, context.HasSucceeded);
    }

    [Theory]
    [InlineData(MicrosoftIdentityClaimTypes.Scope, "access_as_user", true)]
    [InlineData(MicrosoftIdentityClaimTypes.ScopeUri, "access_as_user", true)]
    [InlineData(MicrosoftIdentityClaimTypes.Scope, "read access_as_user write", true)]
    [InlineData(MicrosoftIdentityClaimTypes.Scope, "access_as_user_extra", false)]
    [InlineData(null, null, false)]
    public void ScopeResolver_RequiresExactScopeFromSupportedClaimTypes(
        string? claimType,
        string? claimValue,
        bool expected)
    {
        var claims = claimType is null || claimValue is null
            ? Array.Empty<Claim>()
            : [new Claim(claimType, claimValue)];

        var result = MicrosoftIdentityScopeResolver.HasScope(
            Principal(claims),
            "access_as_user");

        Assert.Equal(expected, result);
    }

    [Fact]
    public void ScopeResolver_CombinesBothSupportedClaimTypes()
    {
        var principal = Principal(
            new Claim(MicrosoftIdentityClaimTypes.Scope, "read"),
            new Claim(MicrosoftIdentityClaimTypes.ScopeUri,
                "access_as_user write"));

        var scopes = MicrosoftIdentityScopeResolver.Resolve(principal);

        Assert.Equal(["read", "access_as_user", "write"], scopes);
    }

    [Fact]
    public void ScopeResolver_MapsVerifiedIdentityWithEmptyDataFilters()
    {
        var user = new AuthenticatedUserContext(
            UserId,
            TenantId,
            ["Report.User"]);

        var assignment = new UserDataAccessAssignment(
            TenantId,
            UserId,
            allowAllRegions: true,
            allowAllStores: true,
            Array.Empty<string>(),
            Array.Empty<string>());
        var scope = new CurrentUserDataScopeResolver(
            new SingleAssignmentStore(assignment))
            .ResolveRequired(user);

        Assert.Equal(UserId, scope.UserId);
        Assert.Equal(TenantId, scope.TenantId);
        Assert.Equal(["Report.User"], scope.Roles);
        Assert.NotNull(scope.AllowedRegions);
        Assert.Empty(scope.AllowedRegions);
        Assert.NotNull(scope.AllowedStoreIds);
        Assert.Empty(scope.AllowedStoreIds);
        Assert.True(scope.AllowAllRegions);
        Assert.True(scope.AllowAllStores);
    }

    private static HttpContextCurrentUserContextAccessor Accessor(
        ClaimsPrincipal principal)
    {
        var context = new DefaultHttpContext { User = principal };
        return new HttpContextCurrentUserContextAccessor(
            new HttpContextAccessor { HttpContext = context });
    }

    private static ClaimsPrincipal Principal(params Claim[] claims) =>
        new(new ClaimsIdentity(claims, "UnitTestAuthentication"));

    private sealed class SingleAssignmentStore
        : IUserDataAccessAssignmentStore
    {
        private readonly UserDataAccessAssignment _assignment;

        public SingleAssignmentStore(
            UserDataAccessAssignment assignment)
        {
            _assignment = assignment;
        }

        public UserDataAccessAssignment? Find(
            string tenantId,
            string userId) => _assignment;
    }
}
