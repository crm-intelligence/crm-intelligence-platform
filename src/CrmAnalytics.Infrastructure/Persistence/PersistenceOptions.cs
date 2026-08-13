namespace CrmAnalytics.Infrastructure.Persistence;

public sealed class PersistenceOptions
{
    public const string SectionName = "Persistence";
    public const string InMemoryProvider = "InMemory";
    public const string SqlServerProvider = "SqlServer";

    public string Provider { get; set; } = SqlServerProvider;

    public SqlServerPersistenceOptions SqlServer { get; set; } = new();
}

public sealed class SqlServerPersistenceOptions
{
    public const string ConnectionStringName = "CrmAnalytics";

    public int CommandTimeoutSeconds { get; set; } = 30;
    public bool EnableRetryOnFailure { get; set; } = true;
    public int MaxRetryCount { get; set; } = 5;
    public int MaxRetryDelaySeconds { get; set; } = 30;
}
