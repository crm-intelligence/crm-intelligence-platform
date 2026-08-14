using Crm.Analytics.Sql.Catalog;
using Crm.Analytics.Sql.Tests.Fixtures;

namespace Crm.Analytics.Sql.Tests.Catalog;

/// <summary>
/// Allow-list bir konfigurasyon degil guvenlik sinirdir: yanlis yapilandirilmis bir dosya,
/// tum kontrolleri gecen bir sizinti demektir. Bu yuzden yukleme dogrulamasi negatif
/// testlerle kapatiliyor.
/// </summary>
public class AllowListLoaderTests
{
    [Fact]
    public void Gecerli_fixture_yuklenir()
    {
        var allowList = AllowListLoader.FromJson(TestFixtures.ReadAllowListJson());

        Assert.True(allowList.HasObject("vw_sales"));
        Assert.Equal("region", allowList.FindObject("vw_sales")!.ScopeColumn);
        Assert.True(allowList.IsDeniedColumn("email"));
        Assert.True(allowList.IsIdentityColumn("customer_id"));
        Assert.Equal(5000, allowList.MaxRows);
    }

    [Fact]
    public void Kapsamdan_muaf_obje_acikca_isaretlenebilir()
    {
        var allowList = AllowListLoader.FromJson(TestFixtures.ReadAllowListJson());

        var dateDimension = allowList.FindObject("vw_date")!;
        Assert.True(dateDimension.IsScopeExempt);
        Assert.Null(dateDimension.ScopeColumn);
    }

    [Fact]
    public void Bilinmeyen_alan_reddedilir()
    {
        // "deniedColumn" (tekil) yazim hatasi sessizce bos listeye donusurse PII kontrolu
        // tamamen devre disi kalir. Sessiz kalmamali.
        var json = """
        {
          "objects": { "vw_sales": { "columns": ["region"], "scopeColumn": "region" } },
          "deniedColumn": ["email"]
        }
        """;

        var exception = Assert.Throws<CatalogValidationException>(() => AllowListLoader.FromJson(json));
        Assert.Contains("okunamadi", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Kapsam_kolonu_izinli_kolonlar_arasinda_degilse_reddedilir()
    {
        var json = """
        {
          "objects": { "vw_sales": { "columns": ["region"], "scopeColumn": "bolge_kodu" } }
        }
        """;

        var exception = Assert.Throws<CatalogValidationException>(() => AllowListLoader.FromJson(json));
        Assert.Contains("scopeColumn", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Kapsam_kolonu_olmayan_obje_acik_muafiyet_olmadan_reddedilir()
    {
        // Sessiz muafiyet en tehlikeli senaryo: kapsam filtresi uygulanamayan bir obje
        // farkinda olmadan filtresiz sorgulanabilir hale gelir.
        var json = """
        {
          "objects": { "vw_sales": { "columns": ["region", "net_amount"] } }
        }
        """;

        var exception = Assert.Throws<CatalogValidationException>(() => AllowListLoader.FromJson(json));
        Assert.Contains("scopeExempt", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Muafiyet_ile_kapsam_kolonu_birlikte_tanimlanamaz()
    {
        var json = """
        {
          "objects": {
            "vw_date": { "columns": ["date_key"], "scopeExempt": true, "scopeColumn": "date_key" }
          }
        }
        """;

        var exception = Assert.Throws<CatalogValidationException>(() => AllowListLoader.FromJson(json));
        Assert.Contains("birlikte kullanilamaz", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Tanimsiz_JOIN_hedefi_reddedilir()
    {
        var json = """
        {
          "objects": {
            "vw_sales": {
              "columns": ["region", "customer_id"],
              "scopeColumn": "region",
              "joinPaths": [{
                "id": "sales.employee",
                "to": "hr_employees",
                "leftColumn": "customer_id",
                "rightColumn": "id",
                "cardinality": "manyToOne",
                "leftRuntime": "dwh",
                "rightRuntime": "dwh",
                "allowedJoinTypes": ["inner"]
              }]
            }
          }
        }
        """;

        var exception = Assert.Throws<CatalogValidationException>(() => AllowListLoader.FromJson(json));
        Assert.Contains("hr_employees", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Desteklenmeyen_JOIN_tipi_reddedilir()
    {
        var json = """
        {
          "objects": {
            "vw_sales": {
              "columns": ["region", "customer_id"],
              "scopeColumn": "region",
              "joinPaths": [{
                "id": "sales.customer",
                "to": "vw_customer",
                "leftColumn": "customer_id",
                "rightColumn": "customer_id",
                "cardinality": "manyToOne",
                "leftRuntime": "dwh",
                "rightRuntime": "dwh",
                "allowedJoinTypes": ["cross"]
              }]
            },
            "vw_customer": {
              "columns": ["region", "customer_id"],
              "scopeColumn": "region"
            }
          }
        }
        """;

        var exception = Assert.Throws<CatalogValidationException>(
            () => AllowListLoader.FromJson(json));
        Assert.Contains("okunamadi", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("region]; DROP TABLE vw_sales --")]
    [InlineData("region FROM sys.objects")]
    [InlineData("[region]")]
    [InlineData("re gion")]
    [InlineData("region;")]
    public void Gecersiz_kolon_adi_reddedilir(string columnName)
    {
        // Allow-list'ten gelen adlar AST'ye yazilir. Buraya kotu bir ad girmesi, guardrail'in
        // kendi eliyle enjeksiyon uretmesi demek olurdu.
        var json = $$"""
        {
          "objects": {
            "vw_sales": { "columns": ["region", {{System.Text.Json.JsonSerializer.Serialize(columnName)}}], "scopeColumn": "region" }
          }
        }
        """;

        var exception = Assert.Throws<CatalogValidationException>(() => AllowListLoader.FromJson(json));
        Assert.Contains("gecersiz kolon adi", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("vw_sales; DROP TABLE x")]
    [InlineData("db.schema.vw_sales")]
    [InlineData("[vw_sales]")]
    public void Gecersiz_obje_adi_reddedilir(string objectName)
    {
        var json = $$"""
        {
          "objects": {
            {{System.Text.Json.JsonSerializer.Serialize(objectName)}}: { "columns": ["region"], "scopeColumn": "region" }
          }
        }
        """;

        var exception = Assert.Throws<CatalogValidationException>(() => AllowListLoader.FromJson(json));
        Assert.Contains("obje adi", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Ayni_kolonun_hem_izinli_hem_yasakli_olmasi_reddedilir()
    {
        // Hangisinin kazandigini varsaymak yerine belirsizligi yapilandirma hatasi sayiyoruz.
        var json = """
        {
          "objects": {
            "vw_customer": { "columns": ["region", "email"], "scopeColumn": "region" }
          },
          "deniedColumns": ["email"]
        }
        """;

        var exception = Assert.Throws<CatalogValidationException>(() => AllowListLoader.FromJson(json));
        Assert.Contains("deniedColumns", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Bos_obje_listesi_reddedilir()
    {
        var exception = Assert.Throws<CatalogValidationException>(
            () => AllowListLoader.FromJson("""{ "objects": {} }"""));

        Assert.Contains("En az bir izinli obje", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Gecersiz_limit_degerleri_reddedilir()
    {
        var json = """
        {
          "objects": { "vw_sales": { "columns": ["region"], "scopeColumn": "region" } },
          "maxRows": 0,
          "queryTimeoutSeconds": -1,
          "maxDateRangeDays": 0,
          "minCellSize": 0
        }
        """;

        var exception = Assert.Throws<CatalogValidationException>(() => AllowListLoader.FromJson(json));
        Assert.Contains("maxRows", exception.Message, StringComparison.Ordinal);
        Assert.Contains("queryTimeoutSeconds", exception.Message, StringComparison.Ordinal);
        Assert.Contains("maxDateRangeDays", exception.Message, StringComparison.Ordinal);
        Assert.Contains("minCellSize", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Ayni_hedefe_birden_fazla_JOIN_yolu_reddedilir()
    {
        var json = """
        {
          "objects": {
            "vw_sales": {
              "columns": ["region", "customer_id"],
              "scopeColumn": "region",
              "joinPaths": [
                {
                  "id": "sales.customer.id",
                  "to": "vw_customer",
                  "leftColumn": "customer_id",
                  "rightColumn": "customer_id",
                  "cardinality": "manyToOne",
                  "leftRuntime": "dwh",
                  "rightRuntime": "dwh",
                  "allowedJoinTypes": ["inner"]
                },
                {
                  "id": "sales.customer.region",
                  "to": "vw_customer",
                  "leftColumn": "region",
                  "rightColumn": "region",
                  "cardinality": "manyToMany",
                  "leftRuntime": "dwh",
                  "rightRuntime": "dwh",
                  "allowedJoinTypes": ["inner"]
                }
              ]
            },
            "vw_customer": { "columns": ["customer_id", "region"], "scopeColumn": "region" }
          }
        }
        """;

        var exception = Assert.Throws<CatalogValidationException>(() => AllowListLoader.FromJson(json));
        Assert.Contains("birden fazla JOIN yolu", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Bos_json_reddedilir()
    {
        Assert.Throws<ArgumentException>(() => AllowListLoader.FromJson("   "));
    }
}
