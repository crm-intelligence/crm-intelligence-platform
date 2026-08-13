using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace CrmAnalytics.Api.Authentication;

public static class DevelopmentAuthenticationDefaults
{
    public const string AuthenticationScheme =
        "CrmAnalytics.Development";

    public const string AuthenticationType =
        "CrmAnalyticsDevelopmentAuthentication";
}

public sealed class DevelopmentAuthenticationHandler
    : AuthenticationHandler<AuthenticationSchemeOptions>
{
    private readonly IOptions<CrmAnalyticsAuthenticationOptions>
        _authenticationOptions;
    private readonly IHostEnvironment _environment;

    public DevelopmentAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> schemeOptions,
        ILoggerFactory loggerFactory,
        UrlEncoder encoder,
        IOptions<CrmAnalyticsAuthenticationOptions> authenticationOptions,
        IHostEnvironment environment)
        : base(schemeOptions, loggerFactory, encoder)
    {
        _authenticationOptions = authenticationOptions;
        _environment = environment;
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var options = _authenticationOptions.Value;

        if (!_environment.IsDevelopment()
            || !string.Equals(
                options.Mode,
                AuthenticationModes.Development,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Development authentication cannot run outside the "
                + "Development environment and mode.");
        }

        var development = options.Development;
        var claims = new List<Claim>
        {
            new("oid", development.UserId.Trim()),
            new("tid", development.TenantId.Trim()),
            new("scp", options.RequiredScope.Trim()),
            new("idtyp", "user")
        };

        claims.AddRange(
            (development.Roles ?? Array.Empty<string>())
            .Where(role => !string.IsNullOrWhiteSpace(role))
            .Select(role => new Claim("roles", role.Trim())));

        var identity = new ClaimsIdentity(
            claims,
            DevelopmentAuthenticationDefaults.AuthenticationType);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
