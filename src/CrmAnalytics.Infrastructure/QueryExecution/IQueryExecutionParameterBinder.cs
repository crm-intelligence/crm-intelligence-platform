using CrmAnalytics.Application.SqlProduction;
using Microsoft.Data.SqlClient;

namespace CrmAnalytics.Infrastructure.QueryExecution;

public interface IQueryExecutionParameterBinder
{
    void Bind(
        SqlCommand command,
        IReadOnlyCollection<SqlExecutionParameter> parameters,
        SqlDataSource source);
}
