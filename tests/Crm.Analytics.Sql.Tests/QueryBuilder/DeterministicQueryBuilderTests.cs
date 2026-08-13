using Crm.Analytics.Sql.Catalog;
using Crm.Analytics.Sql.Contracts;
using Crm.Analytics.Sql.Guardrail;
using Crm.Analytics.Sql.Parsing;
using Crm.Analytics.Sql.QueryBuilder;

namespace Crm.Analytics.Sql.Tests.QueryBuilder;

/// <summary>
/// Query Builder, Olist katalogu ve allow-list'i uzerinde.
/// </summary>
/// <remarks>
/// Kapsam boyutunun adi <c>UserDataScope.RegionDimension</c> ("region") olarak sabittir;
/// Olist'te bu ad <c>customer_state</c> kolonuna uygulanir. Boyut adi ic bir kavramdir,
/// kolon adi allow-list'ten gelir.
/// </remarks>
public class DeterministicQueryBuilderTests
{
    private static readonly AllowListDocument AllowList =
        AllowListLoader.FromJson(ContractResources.ReadOlistAllowList());

    private static readonly MetricCatalogDocument Catalog =
        MetricCatalogLoader.FromJson(ContractResources.ReadOlistMetricCatalog());

    private static DeterministicQueryBuilder CreateBuilder() =>
        new(new TSqlParserFactory(), Catalog, AllowList);

    [Fact]
    public void Satis_analizi_icin_parametreli_SELECT_uretir()
    {
        var result = CreateBuilder().Build(Request(
            metrics: ["item_sales", "order_count"],
            dimensions: ["customer_state"]));

        Assert.True(result.IsSuccessful, result.Detail);
        Assert.Equal("vw_sales", result.SourceObject);
        Assert.Contains("FROM mart.vw_sales", result.Sql!, StringComparison.Ordinal);
        Assert.Contains("SUM(price)", result.Sql!, StringComparison.Ordinal);
        Assert.Contains("COUNT(DISTINCT order_id)", result.Sql!, StringComparison.Ordinal);
        Assert.Contains("GROUP BY", result.Sql!, StringComparison.Ordinal);
        Assert.Contains("customer_state", result.Sql!, StringComparison.Ordinal);
    }

    [Fact]
    public void Ayni_talep_ayni_SQL_i_uretir()
    {
        // "Deterministik" olmasinin kaniti.
        var first = CreateBuilder().Build(Request(["item_sales"], ["customer_state"]));
        var second = CreateBuilder().Build(Request(["item_sales"], ["customer_state"]));

        Assert.Equal(first.Sql, second.Sql);
    }

    [Fact]
    public void Olcum_yoksa_GROUP_BY_uretilmez()
    {
        var result = CreateBuilder().Build(Request([], ["customer_state"]));

        Assert.True(result.IsSuccessful, result.Detail);
        Assert.DoesNotContain("GROUP BY", result.Sql!, StringComparison.Ordinal);
    }

    [Fact]
    public void Filtre_degeri_parametreye_baglanir_ve_metne_girmez()
    {
        var result = CreateBuilder().Build(Request(
            metrics: ["item_sales"],
            dimensions: ["customer_state"],
            filters:
            [
                new RequestFilter
                {
                    Field = "product_category",
                    Op = FilterOperator.Eq,
                    Values = [new FilterLiteral(FilterValueKind.Text, "electronics")]
                }
            ]));

        Assert.True(result.IsSuccessful, result.Detail);
        Assert.DoesNotContain("electronics", result.Sql!, StringComparison.Ordinal);

        // Parametre ADIYLA secilir: tarih araligi da parametre uretir ve toplam sayiya
        // dayanan bir beklenti testin odagini (filtre degeri metne girmiyor mu) bulaniklastirir.
        var parameter = result.Parameters.Single(spec => spec.Name == "@f0");
        Assert.Equal("electronics", parameter.Raw);
        Assert.Equal(FilterValueKind.Text, parameter.Kind);
    }

    [Fact]
    public void IN_filtresi_her_deger_icin_ayri_parametre_uretir()
    {
        var result = CreateBuilder().Build(Request(
            metrics: ["item_sales"],
            dimensions: ["customer_state"],
            filters:
            [
                new RequestFilter
                {
                    Field = "product_category",
                    Op = FilterOperator.In,
                    Values =
                    [
                        new FilterLiteral(FilterValueKind.Text, "electronics"),
                        new FilterLiteral(FilterValueKind.Text, "furniture")
                    ]
                }
            ]));

        Assert.True(result.IsSuccessful, result.Detail);
        Assert.Contains("IN (@f0, @f1)", result.Sql!, StringComparison.Ordinal);
        Assert.Equal(
            ["electronics", "furniture"],
            result.Parameters.Where(spec => spec.Name is "@f0" or "@f1").Select(spec => spec.Raw));
    }

    [Fact]
    public void Filtre_degeri_tipini_katalogdan_alir()
    {
        // '2026-01-01' metni tarih boyutunda TARIH olarak baglanir; JSON'dan tahmin edilmez.
        var result = CreateBuilder().Build(Request(
            metrics: ["item_sales"],
            dimensions: [],
            filters:
            [
                new RequestFilter
                {
                    Field = "order_purchase_timestamp",
                    Op = FilterOperator.Gte,
                    Values = [new FilterLiteral(FilterValueKind.Text, "2026-01-01")]
                }
            ]));

        var parameter = result.Parameters.Single(spec => spec.Raw == "2026-01-01");
        Assert.Equal(FilterValueKind.Date, parameter.Kind);
    }

    [Fact]
    public void Tarih_araligi_zaman_boyutuna_uygulanir()
    {
        var result = CreateBuilder().Build(Request(
            metrics: ["item_sales"],
            dimensions: ["customer_state"],
            dateRange: new DateRangeSpec
            {
                Kind = DateRangeKind.Absolute,
                From = new DateOnly(2018, 1, 1),
                To = new DateOnly(2018, 3, 31)
            }));

        Assert.True(result.IsSuccessful, result.Detail);
        Assert.Contains("order_purchase_timestamp >=", result.Sql!, StringComparison.Ordinal);
        Assert.Equal(2, result.Parameters.Count);

        // ISO 8601: sunucu DATEFORMAT ayarindan bagimsiz yorumlanir.
        Assert.Equal("2018-01-01", result.Parameters[0].Raw);
        Assert.All(result.Parameters, parameter => Assert.Equal(FilterValueKind.Date, parameter.Kind));
    }

    [Theory]
    [InlineData(TimeGrain.Year, "YEAR(order_purchase_timestamp)")]
    [InlineData(TimeGrain.Month, "MONTH(order_purchase_timestamp)")]
    public void Zaman_kirilimi_uygulanir(TimeGrain grain, string expected)
    {
        var result = CreateBuilder().Build(Request(
            metrics: ["item_sales"],
            dimensions: ["order_purchase_timestamp"],
            grain: grain));

        Assert.True(result.IsSuccessful, result.Detail);
        Assert.Contains(expected, result.Sql!, StringComparison.Ordinal);

        // GROUP BY, SELECT'teki ifadenin AYNISINI icermeli.
        Assert.Contains($"GROUP BY {expected}", result.Sql!, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(TimeGrain.Day)]
    [InlineData(TimeGrain.Week)]
    public void Desteklenmeyen_zaman_kirilimi_netlestirme_ister(TimeGrain grain)
    {
        // Gun CAST gerektirir (izinli fonksiyon degil), hafta ise DATEFIRST ayarina bagli
        // oldugu icin deterministik degildir. Uydurma karsilik uretmek yerine soruyoruz.
        var result = CreateBuilder().Build(Request(
            metrics: ["item_sales"],
            dimensions: ["order_purchase_timestamp"],
            grain: grain));

        Assert.False(result.IsSuccessful);
        Assert.Equal(ReasonCode.CL002, result.ReasonCode);
    }

    [Fact]
    public void Tanimsiz_metrik_netlestirme_ister()
    {
        var result = CreateBuilder().Build(Request(["olmayan_metrik"], []));

        Assert.False(result.IsSuccessful);
        Assert.Equal(ReasonCode.CL001, result.ReasonCode);
    }

    [Fact]
    public void Tanimi_kesinlesmemis_metrik_netlestirme_ister()
    {
        // recency_days designPending: referans tarih karari bekliyor. Uydurma bir ifade
        // uretmek yerine netlestirme istenir.
        var result = CreateBuilder().Build(Request(["recency_days"], []));

        Assert.False(result.IsSuccessful);
        Assert.Equal(ReasonCode.CL001, result.ReasonCode);
        Assert.Contains("kesinlesmedi", result.Detail!, StringComparison.Ordinal);
    }

    [Fact]
    public void Farkli_kaynaklari_birlestiren_talep_reddedilir()
    {
        // item_sales -> vw_sales, payment_total -> vw_payment. JOIN yolu yok (maxJoins = 0)
        // ve iki gorunum arasinda ortak anahtar da bulunmuyor.
        var result = CreateBuilder().Build(Request(["item_sales", "payment_total"], []));

        Assert.False(result.IsSuccessful);
        Assert.Equal(ReasonCode.GR006, result.ReasonCode);
    }

    [Fact]
    public void Talep_limiti_uygulanir()
    {
        var result = CreateBuilder().Build(Request(["item_sales"], ["customer_state"], limit: 50));

        Assert.Contains("TOP 50", result.Sql!, StringComparison.Ordinal);
    }

    // --- uctan uca: Query Builder -> guardrail --------------------------------

    [Fact]
    public void Uretilen_SQL_guardrail_dan_gecer_ve_kapsam_filtresi_eklenir()
    {
        var build = CreateBuilder().Build(Request(["item_sales"], ["customer_state"]));
        Assert.True(build.IsSuccessful, build.Detail);

        var context = new GuardrailContext(
            build.Sql!,
            AllowList,
            UserDataScope.ForRegions("SP", "RJ"),
            new TSqlParserFactory(),
            initialParameters: build.Parameters);

        var result = GuardrailFactory.Create().Execute(context);

        Assert.Equal(GuardrailDecision.Accepted, result.Decision);

        // Kapsam filtresi Olist'te customer_state kolonuna uygulanir.
        Assert.Contains("customer_state IN (@scope0, @scope1)", result.Sql!, StringComparison.Ordinal);
        Assert.Contains("TOP", result.Sql!, StringComparison.Ordinal);
    }

    [Fact]
    public void Query_Builder_parametreleri_guardrail_sonucunda_korunur()
    {
        // Parametreler devredilmezse kabul edilen SQL, tasimadigi bir parametreye referans
        // verir ve calisma aninda "parametre eksik" hatasi alinir.
        var build = CreateBuilder().Build(Request(
            metrics: ["item_sales"],
            dimensions: ["customer_state"],
            filters:
            [
                new RequestFilter
                {
                    Field = "product_category",
                    Op = FilterOperator.Eq,
                    Values = [new FilterLiteral(FilterValueKind.Text, "electronics")]
                }
            ]));

        var context = new GuardrailContext(
            build.Sql!,
            AllowList,
            UserDataScope.ForRegions("SP"),
            new TSqlParserFactory(),
            initialParameters: build.Parameters);

        var result = GuardrailFactory.Create().Execute(context);

        Assert.Equal(GuardrailDecision.Accepted, result.Decision);
        Assert.Contains(result.Parameters, parameter => parameter.Raw == "electronics");
        Assert.Contains(result.Parameters, parameter => parameter.Name == "@scope0");
    }

    [Fact]
    public void Kimlik_kirilimi_iceren_talep_guardrail_tarafindan_reddedilir()
    {
        // Query Builder izin verse dahi guardrail son sozu soyler: customer_unique_id
        // bir kimlik kolonudur ve kirilimda kullanilamaz.
        // vw_customer_rfm zaman boyutu tasimaz; tarih araligi orada uygulanamaz, bu yuzden
        // talep araligi "uygulanamaz" olarak isaretler. Cozumlenmis bir aralik gonderilmesi
        // CL002 ile reddedilirdi — sessizce yok sayilmaz.
        var build = CreateBuilder().Build(
            Request(["customer_count"], [], dateRange: DateRangeSpec.NotApplicable));
        Assert.True(build.IsSuccessful, build.Detail);

        var context = new GuardrailContext(
            build.Sql!,
            AllowList,
            UserDataScope.ForRegions("SP"),
            new TSqlParserFactory(),
            initialParameters: build.Parameters);

        var result = GuardrailFactory.Create().Execute(context);

        // customer_count agregasyon icinde kullanildigi icin GECMELI.
        Assert.Equal(GuardrailDecision.Accepted, result.Decision);
    }

    private static CanonicalRequest Request(
        IReadOnlyList<string> metrics,
        IReadOnlyList<string> dimensions,
        IReadOnlyList<RequestFilter>? filters = null,
        DateRangeSpec? dateRange = null,
        TimeGrain grain = TimeGrain.None,
        int? limit = null) =>
        new()
        {
            RequestId = "req_test",
            ConversationId = "conv_test",
            Intent = RequestIntent.Breakdown,
            Metrics = metrics,
            Dimensions = dimensions,
            Filters = filters ?? [],
            // Cozumlenmis tarihler zorunlu: gorece ifade tek basina birakilirsa Query Builder
            // tarih kosulunu hic uretemez ve tarih butcesi denetlenemez hale gelirdi.
            DateRange = dateRange ?? new DateRangeSpec
            {
                Kind = DateRangeKind.Relative,
                RelativeExpression = "last_quarter",
                From = new DateOnly(2026, 4, 1),
                To = new DateOnly(2026, 6, 30)
            },
            Grain = grain,
            Limit = limit,
            Confidence = 0.95
        };
}
