namespace CrmAnalytics.Infrastructure.Authorization;

public sealed class ReportAuthorizationOptions
{
    public const string SectionName = "ReportAuthorization";

    public IDictionary<string, string[]?>? RolePermissions { get; set; } =
        new Dictionary<string, string[]?>();
}
