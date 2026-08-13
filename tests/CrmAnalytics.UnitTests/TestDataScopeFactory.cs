using CrmAnalytics.Contracts.Integrations;

namespace CrmAnalytics.UnitTests;

internal static class TestDataScopeFactory
{
    public const string UserId =
        "11111111-1111-4111-8111-111111111111";

    public const string TenantId =
        "22222222-2222-4222-8222-222222222222";

    public static UserDataScope Create(
        string? userId = null,
        string? tenantId = null,
        IReadOnlyCollection<string>? roles = null) =>
        new()
        {
            UserId = userId ?? UserId,
            TenantId = tenantId ?? TenantId,
            Roles = roles ?? ["Report.User"],
            AllowAllRegions = true,
            AllowAllStores = true,
            AllowedRegions = Array.Empty<string>(),
            AllowedStoreIds = Array.Empty<string>()
        };
}
