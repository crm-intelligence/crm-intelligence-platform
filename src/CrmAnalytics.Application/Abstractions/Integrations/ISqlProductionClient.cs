using CrmAnalytics.Application.SqlProduction;

namespace CrmAnalytics.Application.Abstractions.Integrations;

public interface ISqlProductionClient
{
    Task<SqlProductionClientResult> ProduceAsync(
        SqlProductionClientRequest request,
        CancellationToken cancellationToken);
}
