using CrmAnalytics.Application.SqlProduction;
using Microsoft.Data.SqlClient;

namespace CrmAnalytics.Infrastructure.QueryExecution;

public interface ISqlQueryConnectionFactory
{
    ValueTask<SqlConnection> CreateOpenConnectionAsync(
        SqlDataSource source,
        CancellationToken cancellationToken);
}
