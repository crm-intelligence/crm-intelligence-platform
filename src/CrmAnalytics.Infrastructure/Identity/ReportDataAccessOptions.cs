namespace CrmAnalytics.Infrastructure.Identity;

public sealed class ReportDataAccessOptions
{
    public const string SectionName = "ReportDataAccess";
    public const string ConfigurationProvider = "Configuration";
    public const string SqlServerProvider = "SqlServer";

    public string Provider { get; set; } = ConfigurationProvider;

    public ReportDataAccessAssignmentOptions[]? Assignments { get; set; } =
        Array.Empty<ReportDataAccessAssignmentOptions>();
}

public sealed class ReportDataAccessAssignmentOptions
{
    public string? TenantId { get; set; }

    public string? UserId { get; set; }

    public bool AllowAllRegions { get; set; }

    public bool AllowAllStores { get; set; }

    public string[]? AllowedRegions { get; set; } = Array.Empty<string>();

    public string[]? AllowedStoreIds { get; set; } = Array.Empty<string>();
}
