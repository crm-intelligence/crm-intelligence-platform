using CrmAnalytics.Application.Exceptions;
using CrmAnalytics.Contracts.Integrations;

namespace CrmAnalytics.Application.Identity;

public static class UserDataScopeValidator
{
    public static UserDataScope CreateSnapshot(UserDataScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);

        return new UserDataScope
        {
            UserId = scope.UserId,
            TenantId = scope.TenantId,
            Roles = scope.Roles.ToArray(),
            AllowAllRegions = scope.AllowAllRegions,
            AllowAllStores = scope.AllowAllStores,
            AllowedRegions = scope.AllowedRegions.ToArray(),
            AllowedStoreIds = scope.AllowedStoreIds.ToArray()
        };
    }

    public static UserDataScope CreateRequiredSnapshot(
        UserDataScope scope,
        AuthenticatedUserContext? user = null)
    {
        ArgumentNullException.ThrowIfNull(scope);

        if (string.IsNullOrWhiteSpace(scope.UserId)
            || string.IsNullOrWhiteSpace(scope.TenantId)
            || !Guid.TryParse(scope.UserId.Trim(), out var scopeUserId)
            || !Guid.TryParse(scope.TenantId.Trim(), out var scopeTenantId))
        {
            throw new ForbiddenAccessException();
        }

        if (user is not null
            && (scopeUserId != Guid.Parse(user.UserId)
                || scopeTenantId != Guid.Parse(user.TenantId)))
        {
            throw new ForbiddenAccessException();
        }

        var regions = Normalize(scope.AllowedRegions);
        var stores = Normalize(scope.AllowedStoreIds);

        if (regions is null
            || stores is null
            || regions.Any(value => value == "*")
            || stores.Any(value => value == "*")
            || (scope.AllowAllRegions && regions.Count != 0)
            || (scope.AllowAllStores && stores.Count != 0)
            || (!scope.AllowAllRegions
                && !scope.AllowAllStores
                && regions.Count == 0
                && stores.Count == 0))
        {
            throw new ForbiddenAccessException();
        }

        return CreateSnapshot(new UserDataScope
        {
            UserId = scopeUserId.ToString("D"),
            TenantId = scopeTenantId.ToString("D"),
            Roles = (user?.Roles ?? scope.Roles).ToArray(),
            AllowAllRegions = scope.AllowAllRegions,
            AllowAllStores = scope.AllowAllStores,
            AllowedRegions = regions,
            AllowedStoreIds = stores
        });
    }

    private static IReadOnlyCollection<string>? Normalize(
        IReadOnlyCollection<string>? values)
    {
        if (values is null
            || values.Any(value => string.IsNullOrWhiteSpace(value)))
        {
            return null;
        }

        return Array.AsReadOnly(values
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray());
    }
}
