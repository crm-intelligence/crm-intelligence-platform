using CrmAnalytics.Contracts.Integrations;

namespace CrmAnalytics.Application.Abstractions.Integrations;

public interface IQueryPlanningClient
{
    Task<QueryPlanningResponse> PlanAsync(
        QueryPlanningRequest request,
        CancellationToken cancellationToken);
}
