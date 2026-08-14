using Crm.Analytics.Sql.Contracts;
using Crm.Analytics.Sql.Contracts.V2;

namespace Crm.Analytics.Sql.QueryBuilder;

/// <summary>
/// Small explicit descriptor for shapes implemented by <see cref="DeterministicQueryBuilder"/>.
/// Semantic keys and compatibility remain owned by the authoritative catalog.
/// </summary>
internal sealed class DeterministicQueryCapabilities
{
    public static DeterministicQueryCapabilities Current { get; } = new();

    private DeterministicQueryCapabilities()
    {
    }

    public int MaximumExplicitOrderings => 1;

    public bool SupportsPeriodComparisons => false;

    public bool SupportsCalculations => false;

    public bool SupportsTimeGrain(TimeGrain grain) => grain is
        TimeGrain.None or TimeGrain.Month or TimeGrain.Quarter or TimeGrain.Year;

    public bool SupportsOrdering(CanonicalOrderingTargetKind targetKind) =>
        targetKind == CanonicalOrderingTargetKind.Dimension;
}
