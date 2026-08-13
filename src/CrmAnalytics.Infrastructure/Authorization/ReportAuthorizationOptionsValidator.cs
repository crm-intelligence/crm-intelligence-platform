using CrmAnalytics.Application.Authorization;
using Microsoft.Extensions.Options;

namespace CrmAnalytics.Infrastructure.Authorization;

public sealed class ReportAuthorizationOptionsValidator
    : IValidateOptions<ReportAuthorizationOptions>
{
    public ValidateOptionsResult Validate(
        string? name,
        ReportAuthorizationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var failures = new List<string>();
        var mappings = options.RolePermissions;

        if (mappings is null || mappings.Count == 0)
        {
            return ValidateOptionsResult.Fail(
                "ReportAuthorization:RolePermissions must not be empty.");
        }

        var duplicateRoles = mappings.Keys
            .Where(role => role is not null)
            .GroupBy(role => role, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);

        if (duplicateRoles is not null)
        {
            failures.Add(
                "Role names must be unique ignoring case.");
        }

        var parsed = new List<ReportPermission>();

        foreach (var mapping in mappings)
        {
            var role = mapping.Key;
            var permissions = mapping.Value;

            if (string.IsNullOrWhiteSpace(role)
                || !string.Equals(
                    role,
                    role.Trim(),
                    StringComparison.Ordinal))
            {
                failures.Add(
                    "Role names must be non-empty and trimmed.");
            }

            if (permissions is null || permissions.Length == 0)
            {
                failures.Add(
                    "Every role must define at least one permission.");
                continue;
            }

            var rolePermissions = new List<ReportPermission>();
            foreach (var permissionName in permissions)
            {
                if (string.IsNullOrWhiteSpace(permissionName)
                    || !Enum.TryParse<ReportPermission>(
                        permissionName,
                        ignoreCase: false,
                        out var permission)
                    || !Enum.IsDefined(permission)
                    || !string.Equals(
                        permissionName,
                        permission.ToString(),
                        StringComparison.Ordinal))
                {
                    failures.Add(
                        "Role permissions must use known exact names.");
                    continue;
                }

                rolePermissions.Add(permission);
                parsed.Add(permission);
            }

            if (rolePermissions.Count
                != rolePermissions.Distinct().Count())
            {
                failures.Add(
                    "A role cannot contain duplicate permissions.");
            }
        }

        if (!parsed.Contains(ReportPermission.Create))
        {
            failures.Add(
                "At least one role must grant Create.");
        }

        if (!parsed.Contains(ReportPermission.ReadOwn))
        {
            failures.Add(
                "At least one role must grant ReadOwn.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
