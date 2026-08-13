using CrmAnalytics.Application.Identity;

namespace CrmAnalytics.Application.Authorization;

public interface IReportPermissionEvaluator
{
    bool HasPermission(
        AuthenticatedUserContext user,
        ReportPermission permission);

    void EnsureHasPermission(
        AuthenticatedUserContext user,
        ReportPermission permission);
}
