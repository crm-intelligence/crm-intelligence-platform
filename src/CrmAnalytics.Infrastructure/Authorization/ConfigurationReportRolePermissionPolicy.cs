using CrmAnalytics.Application.Authorization;
using Microsoft.Extensions.Options;

namespace CrmAnalytics.Infrastructure.Authorization;

public sealed class ConfigurationReportRolePermissionPolicy
    : IReportRolePermissionPolicy
{
    private readonly IReadOnlyDictionary<
        string,
        IReadOnlySet<ReportPermission>> _mappings;

    public ConfigurationReportRolePermissionPolicy(
        IOptions<ReportAuthorizationOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _mappings = (options.Value.RolePermissions
                ?? throw new InvalidOperationException(
                    "Role permissions are required."))
            .ToDictionary(
                mapping => mapping.Key,
                mapping => (IReadOnlySet<ReportPermission>)
                    (mapping.Value ?? Array.Empty<string>())
                    .Select(value => Enum.Parse<ReportPermission>(
                        value,
                        ignoreCase: false))
                    .ToHashSet(),
                StringComparer.Ordinal);
    }

    public bool RoleHasPermission(
        string role,
        ReportPermission permission)
    {
        return !string.IsNullOrWhiteSpace(role)
            && _mappings.TryGetValue(role, out var permissions)
            && permissions.Contains(permission);
    }
}
