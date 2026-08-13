using Crm.Analytics.Sql.Catalog;
using Crm.Analytics.Sql.Contracts;
using Crm.Analytics.Sql.Parsing;

namespace Crm.Analytics.Sql.Tests.Catalog;

/// <summary>
/// Katalog bir enjeksiyon yuzeyidir: expression alanlari serbest SQL metni tasir ve Query
/// Builder onlara guvenir. Dogrulanmazsa katalog'a yazilan tek bir satir tum guardrail'i
/// baypas edebilir.
/// </summary>
public class CatalogValidatorTests
{
    private static readonly AllowListDocument OlistAllowList =
        AllowListLoader.FromJson(ContractResources.ReadOlistAllowList());

    private static readonly CatalogValidator Validator = new(new TSqlParserFactory());

    [Fact]
    public void Olist_katalogu_allow_list_ile_uyumlu()
    {
        // Uretim ciftinin gercekten tutarli oldugunun runtime kaniti.
        var catalog = MetricCatalogLoader.FromJson(ContractResources.ReadOlistMetricCatalog());

        Validator.Validate(catalog, OlistAllowList);
    }

    [Fact]
    public void Olist_katalogu_yuklenebilir_ve_engelli_use_case_gerekce_tasir()
    {
        var catalog = MetricCatalogLoader.FromJson(ContractResources.ReadOlistMetricCatalog());

        var campaign = catalog.UseCases["campaign_performance"];
        Assert.Equal("impossible", campaign.Status);
        Assert.False(campaign.IsUsable);
        Assert.False(string.IsNullOrWhiteSpace(campaign.BlockedReason));

        Assert.True(catalog.UseCases["category_performance"].IsUsable);
    }

    [Fact]
    public void Allow_list_disi_kaynak_reddedilir()
    {
        var catalog = MetricCatalogLoader.FromJson("""
        {
          "metrics": {
            "x": { "label": "X", "expression": "SUM(price)", "source": "gizli_tablo", "approvalStatus": "documented" }
          },
          "dimensions": {}
        }
        """);

        var exception = Assert.Throws<CatalogValidationException>(
            () => Validator.Validate(catalog, OlistAllowList));

        Assert.Contains("gizli_tablo", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Allow_list_disi_kolon_kullanan_ifade_reddedilir()
    {
        // Katalog'a izinsiz bir kolon yazmak, allow-list kontrolunu Query Builder uzerinden
        // atlatmanin en dogrudan yolu olurdu.
        var catalog = MetricCatalogLoader.FromJson("""
        {
          "metrics": {
            "x": { "label": "X", "expression": "SUM(gizli_tutar)", "source": "vw_sales", "approvalStatus": "documented" }
          },
          "dimensions": {}
        }
        """);

        var exception = Assert.Throws<CatalogValidationException>(
            () => Validator.Validate(catalog, OlistAllowList));

        Assert.Contains("gizli_tutar", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Izinli_olmayan_fonksiyon_kullanan_ifade_reddedilir()
    {
        // DB_NAME() sema bilgisi sizdirir ve allowedFunctions listesinde yer almaz.
        var catalog = MetricCatalogLoader.FromJson("""
        {
          "metrics": {
            "x": { "label": "X", "expression": "MAX(DB_NAME())", "source": "vw_sales", "approvalStatus": "documented" }
          },
          "dimensions": {}
        }
        """);

        var exception = Assert.Throws<CatalogValidationException>(
            () => Validator.Validate(catalog, OlistAllowList));

        Assert.Contains("DB_NAME", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Ifadede_tablo_aliasi_kullanimi_reddedilir()
    {
        var catalog = MetricCatalogLoader.FromJson("""
        {
          "metrics": {
            "x": { "label": "X", "expression": "SUM(v.price)", "source": "vw_sales", "approvalStatus": "documented" }
          },
          "dimensions": {}
        }
        """);

        var exception = Assert.Throws<CatalogValidationException>(
            () => Validator.Validate(catalog, OlistAllowList));

        Assert.Contains("alias", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_edilemeyen_ifade_reddedilir()
    {
        var catalog = MetricCatalogLoader.FromJson("""
        {
          "metrics": {
            "x": { "label": "X", "expression": "SUM(price", "source": "vw_sales", "approvalStatus": "documented" }
          },
          "dimensions": {}
        }
        """);

        var exception = Assert.Throws<CatalogValidationException>(
            () => Validator.Validate(catalog, OlistAllowList));

        Assert.Contains("parse edilemedi", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Kapsam_boyutu_gercek_kapsam_kolonuyla_ayni_olmali()
    {
        // "Kapsam boyutu" olarak isaretli ama filtre uygulanmayan bir kolon, yanlis bir
        // guvenlik hissi yaratirdi.
        var catalog = MetricCatalogLoader.FromJson("""
        {
          "metrics": {},
          "dimensions": {
            "sehir": {
              "label": "Sehir", "column": "customer_city", "source": "vw_sales",
              "valueType": "text", "isScopeDimension": true
            }
          }
        }
        """);

        var exception = Assert.Throws<CatalogValidationException>(
            () => Validator.Validate(catalog, OlistAllowList));

        Assert.Contains("scopeColumn", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Ifadesiz_metrik_designPending_olmak_zorunda()
    {
        var exception = Assert.Throws<CatalogValidationException>(() => MetricCatalogLoader.FromJson("""
        {
          "metrics": {
            "x": { "label": "X", "source": "vw_sales", "approvalStatus": "documented" }
          },
          "dimensions": {}
        }
        """));

        Assert.Contains("designPending", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Engelli_use_case_gerekcesiz_olamaz()
    {
        var exception = Assert.Throws<CatalogValidationException>(() => MetricCatalogLoader.FromJson("""
        {
          "metrics": {},
          "dimensions": {},
          "useCases": {
            "x": { "label": "X", "status": "impossible" }
          }
        }
        """));

        Assert.Contains("blockedReason", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Bilinmeyen_alan_reddedilir()
    {
        var exception = Assert.Throws<CatalogValidationException>(() => MetricCatalogLoader.FromJson("""
        {
          "metrics": {
            "x": { "label": "X", "expression": "SUM(price)", "source": "vw_sales", "approvalState": "documented" }
          },
          "dimensions": {}
        }
        """));

        Assert.Contains("okunamadi", exception.Message, StringComparison.Ordinal);
    }
}
