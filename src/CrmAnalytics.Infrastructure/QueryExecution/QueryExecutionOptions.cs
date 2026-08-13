using CrmAnalytics.Application.SqlProduction;

namespace CrmAnalytics.Infrastructure.QueryExecution;

public sealed class QueryExecutionOptions
{
    public const string SectionName = "QueryExecution";

    public string Provider { get; set; } = QueryExecutionProviders.SqlClient;
    public QuerySourceOptions Dwh { get; set; } = new();
    public QuerySourceOptions Oltp { get; set; } = new();

    public QuerySourceOptions GetSource(SqlDataSource source) => source switch
    {
        SqlDataSource.Dwh => Dwh,
        SqlDataSource.Oltp => Oltp,
        _ => throw new QueryExecutionPermanentException(
            QueryExecutionErrorCodes.Failed,
            source,
            TimeSpan.Zero)
    };
}

public sealed class QuerySourceOptions
{
    public bool Enabled { get; set; }
    public string AuthenticationMode { get; set; } =
        QueryAuthenticationModes.ManagedIdentity;
    public string ConnectionStringName { get; set; } = string.Empty;
    public string ManagedIdentityClientId { get; set; } = string.Empty;
    public int CommandTimeoutCeilingSeconds { get; set; }
    public int MaxRows { get; set; }
    public int MaxColumns { get; set; }
    public int MaxCellCharacters { get; set; }
    public long MaxResultBytes { get; set; }
    public int MaxRetryCount { get; set; }
    public int RetryBaseDelayMilliseconds { get; set; }
}

public static class QueryExecutionProviders
{
    public const string Mock = "Mock";
    public const string SqlClient = "SqlClient";
}

public static class QueryAuthenticationModes
{
    public const string ManagedIdentity = "ManagedIdentity";
    public const string DefaultAzureCredential = "DefaultAzureCredential";
    public const string ConnectionString = "ConnectionString";
}
