using Crm.Analytics.Sql.Catalog;
using Crm.Analytics.Sql.Contracts;
using Crm.Analytics.Sql.Parsing;
using Crm.Analytics.Sql.QueryBuilder;

namespace Crm.Analytics.Sql.Tests.Contracts;

/// <summary>
/// Takip sorusu / rapor revizyonu. DoD bes senaryo istiyor: metrik degistir, kirilim degistir,
/// filtre ekle, filtre cikar, tarih degistir.
/// </summary>
public class RequestRevisionTests
{
    private static readonly AllowListDocument AllowList =
        AllowListLoader.FromJson(ContractResources.ReadOlistAllowList());

    private static readonly MetricCatalogDocument Catalog =
        MetricCatalogLoader.FromJson(ContractResources.ReadOlistMetricCatalog());

    // --- bes senaryo ---------------------------------------------------------

    [Fact]
    public void Senaryo1_metrik_degisir_digerleri_korunur()
    {
        var previous = Original();

        var revised = CanonicalRequestReviser.Apply(
            previous,
            new CanonicalRequestDelta { Metrics = ["order_count"] },
            "req_2");

        Assert.Equal(["order_count"], revised.Metrics);
        Assert.Equal(previous.Dimensions, revised.Dimensions);
        Assert.Equal(previous.Filters, revised.Filters);
        Assert.Equal(previous.DateRange, revised.DateRange);
    }

    [Fact]
    public void Senaryo2_kirilim_degisir_metrik_ve_filtre_korunur()
    {
        var previous = Original();

        var revised = CanonicalRequestReviser.Apply(
            previous,
            new CanonicalRequestDelta { Dimensions = ["product_category"] },
            "req_2");

        Assert.Equal(["product_category"], revised.Dimensions);
        Assert.Equal(previous.Metrics, revised.Metrics);
        Assert.Single(revised.Filters);
    }

    [Fact]
    public void Senaryo3_filtre_eklenir()
    {
        var previous = Original();

        var revised = CanonicalRequestReviser.Apply(
            previous,
            new CanonicalRequestDelta
            {
                FiltersToAdd = [Filter("order_status", "delivered")]
            },
            "req_2");

        Assert.Equal(2, revised.Filters.Count);
        Assert.Contains(revised.Filters, filter => filter.Field == "order_status");
        Assert.Contains(revised.Filters, filter => filter.Field == "product_category");
    }

    [Fact]
    public void Senaryo4_filtre_cikarilir()
    {
        var previous = Original();

        var revised = CanonicalRequestReviser.Apply(
            previous,
            new CanonicalRequestDelta { FilterFieldsToRemove = ["product_category"] },
            "req_2");

        Assert.Empty(revised.Filters);
        Assert.Equal(previous.Metrics, revised.Metrics);
    }

    [Fact]
    public void Senaryo5_tarih_araligi_degisir()
    {
        var previous = Original();
        var newRange = new DateRangeSpec
        {
            Kind = DateRangeKind.Absolute,
            From = new DateOnly(2018, 1, 1),
            To = new DateOnly(2018, 6, 30)
        };

        var revised = CanonicalRequestReviser.Apply(
            previous,
            new CanonicalRequestDelta { DateRange = newRange },
            "req_2");

        Assert.Equal(newRange, revised.DateRange);
        Assert.Equal(previous.Metrics, revised.Metrics);
        Assert.Equal(previous.Dimensions, revised.Dimensions);
    }

    // --- zincir ve kenar durumlar --------------------------------------------

    [Fact]
    public void Revizyon_zinciri_korunur()
    {
        // Denetimde "bu rapor hangi talebin revizyonu" sorusu cevaplanabilir olmali.
        var first = Original();
        var second = CanonicalRequestReviser.Apply(first, new CanonicalRequestDelta { Metrics = ["order_count"] }, "req_2");
        var third = CanonicalRequestReviser.Apply(second, new CanonicalRequestDelta { Grain = TimeGrain.Month }, "req_3");

        Assert.Equal("req_1", second.PreviousRequestId);
        Assert.Equal("req_2", third.PreviousRequestId);
        Assert.Equal(first.ConversationId, third.ConversationId);
    }

    [Fact]
    public void Bos_delta_talebi_degistirmez()
    {
        var previous = Original();

        var revised = CanonicalRequestReviser.Apply(previous, new CanonicalRequestDelta(), "req_2");

        Assert.Equal(previous.Metrics, revised.Metrics);
        Assert.Equal(previous.Dimensions, revised.Dimensions);
        Assert.Equal(previous.Filters, revised.Filters);
        Assert.Equal(previous.Grain, revised.Grain);
        Assert.Equal(previous.Limit, revised.Limit);
    }

    [Fact]
    public void Ayni_alana_filtre_eklemek_oncekini_DEGISTIRIR_cift_kosul_uretmez()
    {
        // Iki celisen kosul (category = 'a' AND category = 'b') her zaman bos sonuc verirdi.
        // Kullanicinin niyeti daralt degil DEGISTIR olarak yorumlanir.
        var previous = Original();

        var revised = CanonicalRequestReviser.Apply(
            previous,
            new CanonicalRequestDelta { FiltersToAdd = [Filter("product_category", "furniture")] },
            "req_2");

        var filter = Assert.Single(revised.Filters);
        Assert.Equal("furniture", filter.Values[0].Raw);
    }

    [Fact]
    public void Cozumlenemeyen_terimler_revizyonda_tasinmaz()
    {
        // Onceki talepteki belirsizlik yeni talebe miras kalmamali.
        var previous = Original() with { UnresolvedTerms = ["kar marji"] };

        var revised = CanonicalRequestReviser.Apply(
            previous, new CanonicalRequestDelta { Metrics = ["order_count"] }, "req_2");

        Assert.Empty(revised.UnresolvedTerms);
    }

    [Fact]
    public void Revize_edilmis_talep_gecerli_SQL_uretir()
    {
        // Revizyonun sonucu Query Builder'da gercekten calismali; sozlesme testi yetmez.
        var previous = Original();
        var revised = CanonicalRequestReviser.Apply(
            previous,
            new CanonicalRequestDelta
            {
                Metrics = ["order_count"],
                Dimensions = ["product_category"]
            },
            "req_2");

        var builder = new DeterministicQueryBuilder(new TSqlParserFactory(), Catalog, AllowList);
        var result = builder.Build(revised);

        Assert.True(result.IsSuccessful, result.Detail);
        Assert.Contains("COUNT(DISTINCT order_id)", result.Sql!, StringComparison.Ordinal);
        Assert.Contains("product_category", result.Sql!, StringComparison.Ordinal);
        Assert.DoesNotContain("customer_state", result.Sql!, StringComparison.Ordinal);
    }

    private static CanonicalRequest Original() => new()
    {
        RequestId = "req_1",
        ConversationId = "conv_1",
        Intent = RequestIntent.Breakdown,
        Metrics = ["item_sales"],
        Dimensions = ["customer_state"],
        Filters = [Filter("product_category", "electronics")],
        DateRange = new DateRangeSpec
        {
            Kind = DateRangeKind.Relative,
            RelativeExpression = "last_quarter",
            From = new DateOnly(2026, 4, 1),
            To = new DateOnly(2026, 6, 30)
        },
        Grain = TimeGrain.None,
        Limit = 500,
        Confidence = 0.9
    };

    private static RequestFilter Filter(string field, string value) => new()
    {
        Field = field,
        Op = FilterOperator.Eq,
        Values = [new FilterLiteral(FilterValueKind.Text, value)]
    };
}
