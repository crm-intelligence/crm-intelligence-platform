using CrmAnalytics.Contracts.Integrations;
using CrmAnalytics.Infrastructure.Integrations;

namespace CrmAnalytics.UnitTests;

public sealed class SqlProductionScopeCompatibilityMapperTests
{
    private readonly SqlProductionScopeCompatibilityMapper _mapper = new();

    [Fact]
    public void FullAllowAll_MapsToUnrestricted()
    {
        var result = _mapper.Map(new UserDataScope
        {
            AllowAllRegions = true,
            AllowAllStores = true
        });

        Assert.Equal(
            SqlProductionScopeMappingKind.Unrestricted,
            result.Kind);
        Assert.Empty(result.Regions);
    }

    [Fact]
    public void ExplicitCanonicalRegions_MapsToRegionsWithoutChangingValues()
    {
        var result = _mapper.Map(new UserDataScope
        {
            AllowAllRegions = false,
            AllowAllStores = true,
            AllowedRegions = ["SP", "RJ", " SP "]
        });

        Assert.Equal(SqlProductionScopeMappingKind.Regions, result.Kind);
        Assert.Equal(["SP", "RJ"], result.Regions);
    }

    [Theory]
    [MemberData(nameof(UnsupportedScopes))]
    public void UnsupportedOrAmbiguousScope_MapsToUnresolved(
        UserDataScope scope)
    {
        var result = _mapper.Map(scope);

        Assert.Equal(
            SqlProductionScopeMappingKind.Unresolved,
            result.Kind);
        Assert.Empty(result.Regions);
    }

    public static TheoryData<UserDataScope> UnsupportedScopes =>
        new()
        {
            new UserDataScope(),
            new UserDataScope
            {
                AllowAllRegions = true,
                AllowAllStores = false,
                AllowedStoreIds = ["store-1"]
            },
            new UserDataScope
            {
                AllowAllRegions = false,
                AllowAllStores = false,
                AllowedRegions = ["SP"],
                AllowedStoreIds = ["store-1"]
            },
            new UserDataScope
            {
                AllowAllRegions = false,
                AllowAllStores = false,
                AllowedStoreIds = ["store-1"]
            },
            new UserDataScope
            {
                AllowAllRegions = true,
                AllowAllStores = false
            },
            new UserDataScope
            {
                AllowAllRegions = false,
                AllowAllStores = true
            },
            new UserDataScope
            {
                AllowAllRegions = false,
                AllowAllStores = true,
                AllowedRegions = ["Marmara"]
            },
            new UserDataScope
            {
                AllowAllRegions = false,
                AllowAllStores = true,
                AllowedRegions = ["sp"]
            },
            new UserDataScope
            {
                AllowAllRegions = true,
                AllowAllStores = true,
                AllowedRegions = ["SP"]
            }
        };

    [Fact]
    public void MappingDoesNotRevealScopeValues()
    {
        var result = _mapper.Map(new UserDataScope
        {
            AllowAllRegions = false,
            AllowAllStores = true,
            AllowedRegions = ["SP"]
        });

        Assert.DoesNotContain("SP", result.ToString());
    }
}
