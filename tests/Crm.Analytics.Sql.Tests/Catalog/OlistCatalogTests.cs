using System.Text.Json;
using Crm.Analytics.Sql.Catalog;
using Crm.Analytics.Sql.Contracts;

namespace Crm.Analytics.Sql.Tests.Catalog;

/// <summary>
/// Olist veri setine gore yazilan allow-list ve Metric Catalog'un dogrulanmasi.
/// </summary>
/// <remarks>
/// En degerli testler capraz dogrulama yapanlar: katalogdaki bir kolon adi yazim hatasi
/// iceriyorsa veya allow-list'te bulunmayan bir objeye isaret ediyorsa, bu hata calisma
/// aninda Query Builder patlamasi olarak degil, burada gorunmelidir.
/// </remarks>
public class OlistCatalogTests
{
    private static readonly AllowListDocument AllowList =
        AllowListLoader.FromJson(ContractResources.ReadOlistAllowList());

    private static JsonDocument OpenCatalog() =>
        JsonDocument.Parse(ContractResources.ReadOlistMetricCatalog());

    [Fact]
    public void Olist_allow_list_yuklenebilir()
    {
        Assert.Equal(3, AllowList.Objects.Count);
        Assert.True(AllowList.HasObject("vw_sales"));
        Assert.True(AllowList.HasObject("vw_customer_rfm"));
        Assert.True(AllowList.HasObject("vw_payment"));
    }

    [Theory]
    [InlineData("vw_sales", "mart.vw_sales")]
    [InlineData("VW_CUSTOMER_RFM", "mart.vw_customer_rfm")]
    [InlineData("vw_payment", "mart.vw_payment")]
    public void Logical_adlar_sabit_schema_qualified_fiziksel_ada_cozulur(
        string logicalName,
        string expectedPhysicalName)
    {
        Assert.Equal(
            expectedPhysicalName,
            AllowList.ResolvePhysicalObject(logicalName));
        Assert.True(AllowList.HasSqlObject(expectedPhysicalName));
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("mart.vw_sales")]
    [InlineData("dwh.fact_sales")]
    public void Kullanici_girdisi_logical_ad_olarak_tahmin_edilmez(string value)
    {
        Assert.Null(AllowList.ResolvePhysicalObject(value));
    }

    [Fact]
    public void Yalniz_contract_bagli_ozel_MART_viewlari_execution_yuzeyindedir()
    {
        string[] expected =
        [
            "mart.vw_sales_detail",
            "mart.vw_monthly_sales",
            "mart.vw_sales_by_region",
            "mart.vw_sales_by_category",
            "mart.vw_customer_rfm_segmented",
            "mart.vw_payment_summary"
        ];

        Assert.Equal(expected.Order(), AllowList.ScenarioObjects.Keys.Order());
        Assert.All(expected, name =>
            Assert.True(AllowList.IsAllowedExecutionObject(name)));
    }

    [Theory]
    [InlineData("dwh.dim_customer")]
    [InlineData("dwh.dim_product")]
    [InlineData("dwh.dim_date")]
    [InlineData("dwh.fact_sales")]
    public void Dwh_base_tablolari_genel_ve_execution_yuzeyinde_degildir(
        string objectName)
    {
        Assert.False(AllowList.HasObject(objectName));
        Assert.False(AllowList.HasSqlObject(objectName));
        Assert.False(AllowList.IsAllowedExecutionObject(objectName));
    }

    [Fact]
    public void Her_gorunumun_kapsam_kolonu_customer_state()
    {
        // Kapsam kolonu tutarla ayni satirda olmak zorunda; gorunumler tam bunun icin var.
        foreach (var (name, allowed) in AllowList.Objects)
        {
            Assert.False(allowed.IsScopeExempt, $"'{name}' kapsamdan muaf olmamali.");
            Assert.Equal("customer_state", allowed.ScopeColumn);
            Assert.True(allowed.HasColumn("customer_state"));
        }
    }

    [Fact]
    public void JOIN_tamamen_kapali()
    {
        // Gorunumler denormalize; ayrica vw_sales ile vw_customer_rfm arasinda ortak anahtar
        // yok (customer_id vs customer_unique_id), yani JOIN teknik olarak da kurulamaz.
        Assert.Equal(0, AllowList.MaxJoins);
        Assert.All(AllowList.Objects.Values, allowed => Assert.Empty(allowed.JoinPaths));
    }

    [Fact]
    public void Kisiyi_tekilleyen_kolonlar_kimlik_kolonu_olarak_isaretli()
    {
        // Kirilimda kullanilmasi MinCellSize tarafindan reddedilecek; filtrede serbest.
        Assert.True(AllowList.IsIdentityColumn("customer_unique_id"));
        Assert.True(AllowList.IsIdentityColumn("customer_id"));
        Assert.True(AllowList.IsIdentityColumn("order_id"));
    }

    [Fact]
    public void Dogrudan_PII_kolonu_bulunmadigi_icin_denied_liste_bos()
    {
        // Temizlenmis dataset'te ad, e-posta, telefon, adres yok. Liste bos ama bu bir
        // eksiklik degil bilincli durum; testin varligi bunun gozden kacmadigini gosterir.
        Assert.Empty(AllowList.DeniedColumns);
    }

    [Fact]
    public void Katalogdaki_her_metrigin_kaynagi_allow_listede_var()
    {
        using var catalog = OpenCatalog();

        foreach (var metric in catalog.RootElement.GetProperty("metrics").EnumerateObject())
        {
            var source = metric.Value.GetProperty("source").GetString()!;

            Assert.True(
                AllowList.HasObject(source),
                $"'{metric.Name}' metriginin kaynagi '{source}' allow-list'te yok.");
        }
    }

    [Fact]
    public void Katalogdaki_her_boyutun_kolonu_kaynak_objede_var()
    {
        using var catalog = OpenCatalog();

        foreach (var dimension in catalog.RootElement.GetProperty("dimensions").EnumerateObject())
        {
            var source = dimension.Value.GetProperty("source").GetString()!;
            var column = dimension.Value.GetProperty("column").GetString()!;

            var allowed = AllowList.FindObject(source);
            Assert.NotNull(allowed);
            Assert.True(
                allowed!.HasColumn(column),
                $"'{dimension.Name}' boyutunun kolonu '{source}.{column}' allow-list'te yok.");
        }
    }

    [Fact]
    public void Metrik_ifadelerinde_gecen_kolonlar_kaynak_objede_var()
    {
        // Kaba ama etkili capraz dogrulama: ifadedeki tanimlayici benzeri parcalar,
        // SQL fonksiyon adlari ve tip adlari haric, kaynak objenin kolonlari arasinda olmali.
        string[] sqlKeywords =
        [
            "SUM", "COUNT", "AVG", "MIN", "MAX", "DISTINCT", "NULLIF", "CAST", "AS", "DECIMAL"
        ];

        using var catalog = OpenCatalog();

        foreach (var metric in catalog.RootElement.GetProperty("metrics").EnumerateObject())
        {
            if (!metric.Value.TryGetProperty("expression", out var expression)
                || expression.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var source = metric.Value.GetProperty("source").GetString()!;
            var allowed = AllowList.FindObject(source)!;

            var identifiers = System.Text.RegularExpressions.Regex
                .Matches(expression.GetString()!, "[A-Za-z_][A-Za-z0-9_]*")
                .Select(match => match.Value)
                .Where(token => !sqlKeywords.Contains(token, StringComparer.OrdinalIgnoreCase));

            foreach (var identifier in identifiers)
            {
                Assert.True(
                    allowed.HasColumn(identifier),
                    $"'{metric.Name}' ifadesindeki '{identifier}', '{source}' objesinin kolonlari arasinda yok.");
            }
        }
    }

    [Fact]
    public void Kampanya_use_case_i_imkansiz_olarak_isaretlenmis()
    {
        // Olist'te kampanya verisi yok. Bu senaryonun "ready" gorunmesi, demoda surpriz
        // olarak ortaya cikmasi demek olurdu.
        using var catalog = OpenCatalog();

        var campaign = catalog.RootElement.GetProperty("useCases").GetProperty("campaign_performance");

        Assert.Equal("impossible", campaign.GetProperty("status").GetString());
        Assert.Empty(campaign.GetProperty("metrics").EnumerateArray());
        Assert.False(string.IsNullOrWhiteSpace(campaign.GetProperty("blockedReason").GetString()));
    }

    [Fact]
    public void Kategori_performansi_ucuncu_senaryo_olarak_hazir()
    {
        using var catalog = OpenCatalog();

        var category = catalog.RootElement.GetProperty("useCases").GetProperty("category_performance");

        Assert.Equal("ready", category.GetProperty("status").GetString());
        Assert.NotEmpty(category.GetProperty("metrics").EnumerateArray());
    }

    [Fact]
    public void Net_satis_karari_verilmemis_ve_iki_aday_da_onay_bekliyor()
    {
        // Sessiz varsayim yapilmadiginin kaniti: hicbir metrik 'net_sales' anahtarini almadi.
        using var catalog = OpenCatalog();
        var metrics = catalog.RootElement.GetProperty("metrics");

        Assert.False(metrics.TryGetProperty("net_sales", out _));

        foreach (var candidate in new[] { "item_sales", "customer_paid_total" })
        {
            Assert.Equal(
                "businessApprovalPending",
                metrics.GetProperty(candidate).GetProperty("approvalStatus").GetString());
        }
    }

    [Fact]
    public void Ifadesi_olmayan_metrik_designPending_olarak_isaretlenmis()
    {
        using var catalog = OpenCatalog();

        foreach (var metric in catalog.RootElement.GetProperty("metrics").EnumerateObject())
        {
            var hasExpression = metric.Value.TryGetProperty("expression", out var expression)
                && expression.ValueKind == JsonValueKind.String;

            if (!hasExpression)
            {
                Assert.Equal("designPending", metric.Value.GetProperty("approvalStatus").GetString());
            }
        }
    }

    [Fact]
    public void Musteri_sayisi_customer_unique_id_uzerinden_hesaplanir()
    {
        // Olist tuzagi: customer_id siparis basina uretilir (customers ve orders tablolari
        // birebir ayni satir sayisina sahip). customer_id ile sayim yapilirsa musteri sayisi
        // siparis sayisina esit cikar ve Frequency metrigi anlamsizlasir.
        using var catalog = OpenCatalog();

        var expression = catalog.RootElement
            .GetProperty("metrics").GetProperty("customer_count")
            .GetProperty("expression").GetString()!;

        Assert.Contains("customer_unique_id", expression, StringComparison.Ordinal);
    }

    [Fact]
    public void Katalogdaki_ifadeler_tablo_aliasi_kullanmaz()
    {
        using var catalog = OpenCatalog();

        foreach (var metric in catalog.RootElement.GetProperty("metrics").EnumerateObject())
        {
            if (!metric.Value.TryGetProperty("expression", out var expression)
                || expression.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var text = expression.GetString()!;
            Assert.DoesNotContain("vw_sales.", text, StringComparison.Ordinal);
            Assert.DoesNotContain("v.", text, StringComparison.Ordinal);
        }
    }
}
