using CrmAnalytics.Application.Exceptions;
using CrmAnalytics.Application.Identity;

namespace CrmAnalytics.Application.Authorization;

public sealed class ReportPermissionEvaluator : IReportPermissionEvaluator
{
    private readonly IReportRolePermissionPolicy _policy;

    public ReportPermissionEvaluator(IReportRolePermissionPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        _policy = policy;
    }

    public bool HasPermission(
        AuthenticatedUserContext user,
        ReportPermission permission)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(user.Roles);

        return user.Roles.Any(role =>
            _policy.RoleHasPermission(role, permission));
    }

    public void EnsureHasPermission(
        AuthenticatedUserContext user,
        ReportPermission permission)
    {
        if (!HasPermission(user, permission))
        {
            throw new ForbiddenAccessException();
        }
    }
}
