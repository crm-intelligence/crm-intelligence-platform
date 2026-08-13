using Crm.Analytics.Sql.Catalog;
using Crm.Analytics.Sql.Contracts;
using Crm.Analytics.Sql.Nlu;

namespace CrmAnalytics.UnitTests;

/// <summary>
/// Development examples are test-only data. Regression and unseen evaluation cases live
/// in CrmAnalytics.OllamaSmoke and are intentionally different from these phrases.
/// </summary>
public sealed class SemanticDevelopmentExampleCoverageTests
{
    private static readonly IReadOnlyDictionary<string, string[]> Examples =
        new Dictionary<string, string[]>
        {
            ["item_sales"] = ["2018 urun satis tutari raporu", "2018 ciro olcumu", "2018 satilan mallarin fiyat toplami"],
            ["customer_paid_total"] = ["2018 musteri odemesi", "2018 paid amount", "2018 alicilarin urun ve kargo odemesi"],
            ["freight_total"] = ["2018 kargo tutari", "2018 navlun olcumu", "2018 teslimat icin alinan ucret toplami"],
            ["order_count"] = ["2018 siparis sayisi", "2018 order count", "2018 kac tekil siparis olustu"],
            ["item_count"] = ["2018 satilan kalem sayisi", "2018 item count", "2018 siparis satirlarinin adedi"],
            ["avg_basket"] = ["2018 ortalama sepet", "2018 average basket", "2018 siparis basina urun degeri"],
            ["payment_total"] = ["2018 odeme tutari", "2018 payment total", "2018 tahsil edilen meblag toplami"],
            ["avg_installments"] = ["2018 ortalama taksit sayisi", "2018 installment average", "2018 odeme basina taksit adedi"],
            ["customer_count"] = ["toplam musteri sayisini ver", "customer count sonucunu getir", "kac farkli alici bulunuyor"],
            ["avg_monetary"] = ["musteri basina ortalama harcama", "buyer spending mean", "bir alicinin ortalama urun harcamasi"],
            ["avg_frequency"] = ["musteri basina ortalama siparis", "customer ordering cadence", "alici basina dusen siparis adedi"]
        };

    [Fact]
    public void EverySupportedMetric_HasThreeDistinctTestOnlyExpressionStyles()
    {
        var catalog = SemanticCatalogRegistry.CreateDefault()
            .GetRequired(DataSource.Dwh).Catalog;
        var supported = catalog.Metrics
            .Where(metric => metric.Value.IsUsable)
            .Select(metric => metric.Key)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(supported, Examples.Keys.Order(StringComparer.Ordinal));
        foreach (var metric in supported)
        {
            Assert.Equal(3, Examples[metric].Distinct(StringComparer.Ordinal).Count());
            var aliases = catalog.Metrics[metric].Aliases
                .Select(TurkishTextNormalizer.Normalize).ToHashSet(StringComparer.Ordinal);
            Assert.All(Examples[metric], prompt =>
                Assert.DoesNotContain(TurkishTextNormalizer.Normalize(prompt), aliases));
        }
    }
}
