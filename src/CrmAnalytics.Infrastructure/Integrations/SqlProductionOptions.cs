namespace CrmAnalytics.Infrastructure.Integrations;

public sealed class SqlProductionProviderOptions
{
    public const string SectionName = "SqlProduction";
    public const string MockProvider = "Mock";
    public const string CrmAnalyticsSqlProvider = "CrmAnalyticsSql";

    public string Provider { get; set; } = MockProvider;
    public string SqlVersionName { get; set; } = "Sql150";
    public double ConfidenceThreshold { get; set; } = 0.60;
}
