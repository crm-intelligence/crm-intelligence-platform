using CrmAnalytics.Contracts.Integrations;

namespace CrmAnalytics.Infrastructure.Integrations;

public sealed class SqlProductionScopeCompatibilityMapper
{
    public SqlProductionScopeMapping Map(UserDataScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);

        var regions = scope.AllowedRegions.ToArray();
        var stores = scope.AllowedStoreIds.ToArray();

        if (scope.AllowAllRegions
            && scope.AllowAllStores
            && regions.Length == 0
            && stores.Length == 0)
        {
            return SqlProductionScopeMapping.Unrestricted();
        }

        if (!scope.AllowAllRegions
            && scope.AllowAllStores
            && stores.Length == 0
            && regions.Length > 0
            && regions.All(IsCanonicalCustomerState))
        {
            return SqlProductionScopeMapping.ForRegions(regions);
        }

        return SqlProductionScopeMapping.Unresolved();
    }

    private static bool IsCanonicalCustomerState(string value)
    {
        return value.Length == 2
            && value.All(character =>
                character is >= 'A' and <= 'Z');
    }
}

public sealed class SqlProductionScopeMapping
{
    private SqlProductionScopeMapping(
        SqlProductionScopeMappingKind kind,
        IReadOnlyCollection<string> regions)
    {
        Kind = kind;
        Regions = regions;
    }

    public SqlProductionScopeMappingKind Kind { get; }

    public IReadOnlyCollection<string> Regions { get; }

    internal static SqlProductionScopeMapping Unrestricted() =>
        new(
            SqlProductionScopeMappingKind.Unrestricted,
            Array.Empty<string>());

    internal static SqlProductionScopeMapping ForRegions(
        IReadOnlyCollection<string> regions) =>
        new(
            SqlProductionScopeMappingKind.Regions,
            Array.AsReadOnly(regions.ToArray()));

    internal static SqlProductionScopeMapping Unresolved() =>
        new(
            SqlProductionScopeMappingKind.Unresolved,
            Array.Empty<string>());

    public override string ToString() =>
        $"SqlProductionScopeMapping {{ Kind = {Kind}, Values = Redacted }}";
}

public enum SqlProductionScopeMappingKind
{
    Unrestricted = 1,
    Regions = 2,
    Unresolved = 3
}
