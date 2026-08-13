using System.Security.Claims;
using CrmAnalytics.Application.Identity;

namespace CrmAnalytics.Api.Authentication;

public sealed class HttpContextCurrentUserContextAccessor
    : ICurrentUserContextAccessor
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public HttpContextCurrentUserContextAccessor(
        IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public AuthenticatedUserContext GetRequiredUser()
    {
        var principal = _httpContextAccessor.HttpContext?.User
            ?? throw new InvalidOperationException(
                "An active HTTP user context is required.");

        if (principal.Identity?.IsAuthenticated != true)
        {
            throw new InvalidOperationException(
                "An authenticated user is required.");
        }

        var userId = GetRequiredClaim(
            principal,
            "object identifier",
            MicrosoftIdentityClaimTypes.ObjectId,
            MicrosoftIdentityClaimTypes.ObjectIdUri);
        var tenantId = GetRequiredClaim(
            principal,
            "tenant identifier",
            MicrosoftIdentityClaimTypes.TenantId,
            MicrosoftIdentityClaimTypes.TenantIdUri);
        var roles = principal.Claims
            .Where(claim =>
                string.Equals(
                    claim.Type,
                    MicrosoftIdentityClaimTypes.Roles,
                    StringComparison.Ordinal)
                || string.Equals(
                    claim.Type,
                    ClaimTypes.Role,
                    StringComparison.Ordinal))
            .Select(claim => claim.Value)
            .Where(role => !string.IsNullOrWhiteSpace(role))
            .Select(role => role.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return new AuthenticatedUserContext(userId, tenantId, roles);
    }

    private static string GetRequiredClaim(
        ClaimsPrincipal principal,
        string claimDescription,
        params string[] claimTypes)
    {
        foreach (var claimType in claimTypes)
        {
            var value = principal.FindFirst(claimType)?.Value;

            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        throw new InvalidOperationException(
            $"The authenticated user is missing the {claimDescription}.");
    }
}
