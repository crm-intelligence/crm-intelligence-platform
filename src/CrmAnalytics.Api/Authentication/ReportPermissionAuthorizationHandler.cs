using System.Security.Claims;
using CrmAnalytics.Application.Authorization;
using CrmAnalytics.Application.Identity;
using Microsoft.AspNetCore.Authorization;

namespace CrmAnalytics.Api.Authentication;

public sealed class ReportPermissionAuthorizationHandler
    : AuthorizationHandler<ReportPermissionRequirement>
{
    private readonly IReportPermissionEvaluator _evaluator;

    public ReportPermissionAuthorizationHandler(
        IReportPermissionEvaluator evaluator)
    {
        ArgumentNullException.ThrowIfNull(evaluator);
        _evaluator = evaluator;
    }

    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        ReportPermissionRequirement requirement)
    {
        var userId = FindClaim(
            context.User,
            MicrosoftIdentityClaimTypes.ObjectId,
            MicrosoftIdentityClaimTypes.ObjectIdUri);
        var tenantId = FindClaim(
            context.User,
            MicrosoftIdentityClaimTypes.TenantId,
            MicrosoftIdentityClaimTypes.TenantIdUri);

        if (userId is null || tenantId is null)
        {
            return Task.CompletedTask;
        }

        var roles = context.User.Claims
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

        AuthenticatedUserContext user;
        try
        {
            user = new AuthenticatedUserContext(
                userId,
                tenantId,
                roles);
        }
        catch (ArgumentException)
        {
            return Task.CompletedTask;
        }

        if (_evaluator.HasPermission(user, requirement.Permission))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }

    private static string? FindClaim(
        ClaimsPrincipal principal,
        params string[] claimTypes) =>
        principal.Claims
            .FirstOrDefault(claim =>
                claimTypes.Contains(
                    claim.Type,
                    StringComparer.Ordinal)
                && !string.IsNullOrWhiteSpace(claim.Value))
            ?.Value
            .Trim();
}
