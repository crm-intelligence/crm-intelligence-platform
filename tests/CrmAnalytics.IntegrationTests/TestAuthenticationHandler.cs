using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CrmAnalytics.IntegrationTests;

internal static class TestAuthenticationDefaults
{
    public const string Scheme = "IntegrationTest";
    public const string AuthenticatedHeader = "X-Test-Authenticated";
    public const string UserIdHeader = "X-Test-UserId";
    public const string TenantIdHeader = "X-Test-TenantId";
    public const string ScopesHeader = "X-Test-Scopes";
    public const string RolesHeader = "X-Test-Roles";
    public const string GroupsHeader = "X-Test-Groups";
    public const string DirectoryRolesHeader = "X-Test-Wids";
}

internal sealed class TestAuthenticationHandler
    : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public TestAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(
                TestAuthenticationDefaults.AuthenticatedHeader,
                out var authenticated)
            || !string.Equals(
                authenticated.ToString(),
                "true",
                StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var claims = new List<Claim>
        {
            new("idtyp", "user")
        };
        AddClaim(TestAuthenticationDefaults.UserIdHeader, "oid", claims);
        AddClaim(TestAuthenticationDefaults.TenantIdHeader, "tid", claims);
        AddClaim(TestAuthenticationDefaults.ScopesHeader, "scp", claims);
        AddClaim(TestAuthenticationDefaults.GroupsHeader, "groups", claims);
        AddClaim(
            TestAuthenticationDefaults.DirectoryRolesHeader,
            "wids",
            claims);

        if (Request.Headers.TryGetValue(
                TestAuthenticationDefaults.RolesHeader,
                out var roles))
        {
            claims.AddRange(
                roles.ToString().Split(
                    ',',
                    StringSplitOptions.RemoveEmptyEntries
                        | StringSplitOptions.TrimEntries)
                .Select(role => new Claim("roles", role)));
        }

        var principal = new ClaimsPrincipal(
            new ClaimsIdentity(claims, TestAuthenticationDefaults.Scheme));
        return Task.FromResult(AuthenticateResult.Success(
            new AuthenticationTicket(
                principal,
                TestAuthenticationDefaults.Scheme)));
    }

    private void AddClaim(
        string headerName,
        string claimType,
        ICollection<Claim> claims)
    {
        if (Request.Headers.TryGetValue(headerName, out var value)
            && !string.IsNullOrWhiteSpace(value))
        {
            claims.Add(new Claim(claimType, value.ToString().Trim()));
        }
    }
}
