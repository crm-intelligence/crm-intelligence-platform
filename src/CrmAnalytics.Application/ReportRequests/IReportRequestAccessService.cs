using CrmAnalytics.Application.Identity;
using CrmAnalytics.Domain.ReportRequests;

namespace CrmAnalytics.Application.ReportRequests;

public interface IReportRequestAccessService
{
    void EnsureCanAccess(
        ReportRequest reportRequest,
        AuthenticatedUserContext user);
}
