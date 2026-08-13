using CrmAnalytics.Application.Identity;
using CrmAnalytics.Domain.ReportRequests;

namespace CrmAnalytics.Application.ReportRequests;

public sealed class ReportRequestAccessService
    : IReportRequestAccessService
{
    public void EnsureCanAccess(
        ReportRequest reportRequest,
        AuthenticatedUserContext user)
    {
        ArgumentNullException.ThrowIfNull(reportRequest);
        ArgumentNullException.ThrowIfNull(user);

        if (!IdentifiersEqual(reportRequest.UserId, user.UserId)
            || !IdentifiersEqual(reportRequest.TenantId, user.TenantId))
        {
            throw new KeyNotFoundException(
                "The requested report request was not found.");
        }
    }

    private static bool IdentifiersEqual(string? left, string right)
    {
        if (string.IsNullOrWhiteSpace(left))
        {
            return false;
        }

        return Guid.TryParse(left.Trim(), out var leftGuid)
            && Guid.TryParse(right, out var rightGuid)
            ? leftGuid == rightGuid
            : string.Equals(
                left.Trim(),
                right,
                StringComparison.Ordinal);
    }
}
