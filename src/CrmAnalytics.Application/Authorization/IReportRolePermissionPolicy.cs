namespace CrmAnalytics.Application.Authorization;

public interface IReportRolePermissionPolicy
{
    bool RoleHasPermission(
        string role,
        ReportPermission permission);
}
