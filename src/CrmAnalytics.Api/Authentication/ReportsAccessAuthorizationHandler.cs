using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace CrmAnalytics.Api.Authentication;

public sealed class ReportsAccessAuthorizationHandler
    : AuthorizationHandler<ReportsAccessRequirement>
{
    private readonly IOptions<CrmAnalyticsAuthenticationOptions> _options;

    public ReportsAccessAuthorizationHandler(
        IOptions<CrmAnalyticsAuthenticationOptions> options)
    {
        _options = options;
    }

    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        ReportsAccessRequirement requirement)
    {
        if (context.User.Identity?.IsAuthenticated != true
            || !HasClaim(
                context,
                MicrosoftIdentityClaimTypes.ObjectId,
                MicrosoftIdentityClaimTypes.ObjectIdUri)
            || !HasClaim(
                context,
                MicrosoftIdentityClaimTypes.TenantId,
                MicrosoftIdentityClaimTypes.TenantIdUri))
        {
            return Task.CompletedTask;
        }

        var identityTypes = context.User.FindAll(
            MicrosoftIdentityClaimTypes.IdentityType);

        if (identityTypes.Any()
            && !identityTypes.Any(claim => string.Equals(
                claim.Value.Trim(),
                "user",
                StringComparison.OrdinalIgnoreCase)))
        {
            return Task.CompletedTask;
        }

        var hasRequiredDelegatedScope =
            MicrosoftIdentityScopeResolver.HasScope(
                context.User,
                _options.Value.RequiredScope);

        if (hasRequiredDelegatedScope)
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }

    private static bool HasClaim(
        AuthorizationHandlerContext context,
        params string[] claimTypes) =>
        context.User.Claims.Any(claim =>
            claimTypes.Contains(
                claim.Type,
                StringComparer.Ordinal)
            && !string.IsNullOrWhiteSpace(claim.Value));
}
