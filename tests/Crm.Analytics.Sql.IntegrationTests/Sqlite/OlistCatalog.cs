using Crm.Analytics.Sql.Catalog;
using Crm.Analytics.Sql.Contracts;

namespace Crm.Analytics.Sql.IntegrationTests.Sqlite;

/// <summary>
/// Uretim Olist allow-list ve metric catalog belgelerine tek noktadan erisim.
/// </summary>
/// <remarks>
/// Varsayilan <c>new AllowListDocument()</c> yerine GERCEK gomulu belgeler
/// okunuyor: harness'in dogruladigi sey, uretimde kullanilan sozlesmenin
/// kendisi olmali. Belgeler <c>ContractResources</c> uzerinden alinir, boylece
/// dosya yolu tahminine bagimlilik olusmaz.
/// </remarks>
internal static class OlistCatalog
{
    private static readonly Lazy<AllowListDocument> allowList =
        new(() => AllowListLoader.FromJson(ContractResources.ReadOlistAllowList()));

    private static readonly Lazy<MetricCatalogDocument> metricCatalog =
        new(() => MetricCatalogLoader.FromJson(ContractResources.ReadOlistMetricCatalog()));

    public static AllowListDocument AllowList => allowList.Value;

    public static MetricCatalogDocument MetricCatalog => metricCatalog.Value;
}
