using CrmAnalytics.Contracts.Integrations;

namespace CrmAnalytics.IntegrationTests;

internal static class TestDataScopeFactory
{
    public static UserDataScope Create() =>
        new()
        {
            UserId =
                "11111111-1111-4111-8111-111111111111",
            TenantId =
                "22222222-2222-4222-8222-222222222222",
            Roles = ["Report.User"],
            AllowAllRegions = true,
            AllowAllStores = true
        };
}
