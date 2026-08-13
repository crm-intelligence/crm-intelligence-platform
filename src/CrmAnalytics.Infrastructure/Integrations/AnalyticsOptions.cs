namespace CrmAnalytics.Infrastructure.Integrations;

public sealed class AnalyticsOptions
{
    public const string SectionName = "Analytics";

    public string Provider { get; set; } = AnalyticsProviders.Mock;
    public bool AllowDirectInProtectedEnvironments { get; set; }
    public FabricOptions Fabric { get; set; } = new();
}

public static class AnalyticsProviders
{
    public const string Mock = "Mock";
    public const string Direct = "Direct";
    public const string FabricJob = "FabricJob";
}

public sealed class FabricOptions
{
    public string WorkspaceId { get; set; } = string.Empty;
    public string ItemId { get; set; } = string.Empty;
    public string JobType { get; set; } = string.Empty;
    public string ScenarioKey { get; set; } = string.Empty;
    public int PollingIntervalSeconds { get; set; } = 5;
    public int TimeoutSeconds { get; set; } = 600;
    public int MaxRetries { get; set; } = 4;
    public string AuthenticationMode { get; set; } = "ManagedIdentity";
    public string ManagedIdentityClientId { get; set; } = string.Empty;
}
