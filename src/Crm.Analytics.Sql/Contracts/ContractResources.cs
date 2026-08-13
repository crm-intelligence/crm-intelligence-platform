using System.Reflection;

namespace Crm.Analytics.Sql.Contracts;

/// <summary>
/// Assembly'ye gomulu sozlesme dosyalarina erisim. Backend, Canonical Request semasini
/// dosya yolu tahmin etmeden buradan okuyabilir; boylece sema surumu assembly surumuyle
/// birlikte tasinir ve iki taraf farkli sema kopyalari tutmaz.
/// </summary>
public static class ContractResources
{
    private const string CanonicalRequestSchemaName = "Crm.Analytics.Sql.Contracts.canonical_request.schema.json";
    private const string SampleMetricCatalogName = "Crm.Analytics.Sql.Catalog.metric_catalog.sample.json";
    private const string OlistAllowListName = "Crm.Analytics.Sql.Catalog.allowlist.olist.json";
    private const string OlistMetricCatalogName = "Crm.Analytics.Sql.Catalog.metric_catalog.olist.json";
    private const string OltpAllowListName = "Crm.Analytics.Sql.Catalog.allowlist.fabric-oltp-v1.json";
    private const string OltpMetricCatalogName = "Crm.Analytics.Sql.Catalog.metric_catalog.fabric-oltp-v1.json";

    /// <summary>Backend ile paylasilan Canonical Request JSON Schema'si.</summary>
    public static string ReadCanonicalRequestSchema() => Read(CanonicalRequestSchemaName);

    /// <summary>
    /// Ornek Metric Catalog. <b>Uretim dosyasi degildir</b> — gercek tablo ve kolon adlari
    /// Veri Muhendisi'nden geldiginde ayri bir dosya olarak yazilir.
    /// </summary>
    public static string ReadSampleMetricCatalog() => Read(SampleMetricCatalogName);

    /// <summary>
    /// Olist veri setine gore yazilmis allow-list. <b>Gorunumler olusturulmadan kullanilamaz</b>
    /// (bkz. <c>olist_views.contract.sql</c>): kapsam kolonu yalnizca musteri tablosunda
    /// bulundugu icin ham tablolar allow-list'e alinamaz.
    /// </summary>
    public static string ReadOlistAllowList() => Read(OlistAllowListName);

    /// <summary>Olist veri setine gore yazilmis Metric Catalog.</summary>
    public static string ReadOlistMetricCatalog() => Read(OlistMetricCatalogName);

    public static string ReadFabricOltpAllowList() => Read(OltpAllowListName);

    public static string ReadFabricOltpMetricCatalog() => Read(OltpMetricCatalogName);

    private static string Read(string resourceName)
    {
        var assembly = typeof(ContractResources).Assembly;
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException(
                $"Gomulu kaynak bulunamadi: {resourceName}. " +
                $"Mevcut kaynaklar: {string.Join(", ", assembly.GetManifestResourceNames())}");

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
