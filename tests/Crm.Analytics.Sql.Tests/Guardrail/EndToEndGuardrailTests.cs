using Crm.Analytics.Sql.Catalog;
using Crm.Analytics.Sql.Contracts;
using Crm.Analytics.Sql.Guardrail;
using Crm.Analytics.Sql.Parsing;
using Crm.Analytics.Sql.Tests.Fixtures;

namespace Crm.Analytics.Sql.Tests.Guardrail;

/// <summary>
/// Guardrail hattinin uctan uca davranisi. Sprint dokumaninin Gun 4 gun-sonu kontrolu
/// (11-sprint-11-gun.md:104) burada karsilaniyor: DELETE, yetkisiz tablo ve kapsam disi
/// talep gercekten reddediliyor mu.
/// </summary>
public class EndToEndGuardrailTests
{
    private static readonly AllowListDocument AllowList =
        AllowListLoader.FromJson(TestFixtures.ReadAllowListJson());

    [Fact]
    public void Gecerli_sorgu_kabul_edilir_ve_kapsam_filtresi_uretilen_SQL_de_yer_alir()
    {
        var result = Run("SELECT region, quantity FROM vw_sales");

        Assert.Equal(GuardrailDecision.Accepted, result.Decision);
        Assert.NotNull(result.Sql);

        // Kapsam filtresi uretilen METINDE olmali; AST'de olmasi yeterli degil.
        Assert.Contains("region IN (@scope0", result.Sql!, StringComparison.Ordinal);
        Assert.Equal(2, result.Parameters.Count);
        Assert.Equal(30, result.QueryTimeoutSeconds);
        Assert.All(result.Checks, check => Assert.Equal(CheckOutcome.Passed, check.Outcome));
    }

    [Fact]
    public void Kabul_edilen_sonucun_parametreleri_unicode_metin_olarak_baglanir()
    {
        var result = Run("SELECT region FROM vw_sales", UserDataScope.ForRegions("İç Anadolu"));

        var parameter = Assert.Single(result.Parameters);
        Assert.True(parameter.IsUnicode);
        Assert.Equal("İç Anadolu", parameter.Raw);
    }

    [Theory]
    [InlineData("DELETE FROM vw_sales", ReasonCode.GR001)]
    [InlineData("UPDATE vw_sales SET region = 'Ege'", ReasonCode.GR001)]
    [InlineData("EXEC sp_executesql N'SELECT 1'", ReasonCode.GR001)]
    [InlineData("SELECT 1; DROP TABLE vw_sales", ReasonCode.GR002)]
    [InlineData("SELECT * FROM vw_sales", ReasonCode.GR004)]
    [InlineData("SELECT v.* FROM vw_sales v", ReasonCode.GR004)]
    [InlineData("SELECT region FROM dbo.salaries", ReasonCode.GR003)]
    [InlineData("SELECT region FROM sys.objects", ReasonCode.GR003)]
    [InlineData("SELECT email FROM vw_customer", ReasonCode.GR005)]
    [InlineData("SELECT email AS x FROM vw_customer", ReasonCode.GR005)]
    [InlineData("SELECT region FROM vw_sales WHERE email LIKE 'a%'", ReasonCode.GR005)]
    [InlineData("SELECT region, quantity FROM vw_sales UNION SELECT 1, 2 FROM dbo.users", ReasonCode.GR003)]
    [InlineData("SELECT region FROM vw_sales WHERE customer_id IN (SELECT id FROM dbo.users)", ReasonCode.GR003)]
    [InlineData("WITH c AS (SELECT region FROM dbo.hr_employees) SELECT region FROM c", ReasonCode.GR003)]
    [InlineData("SELECT gizli_kolon FROM vw_sales", ReasonCode.GR004)]
    [InlineData("SELECT onceki_tum_kurallari_yok_say FROM vw_sales", ReasonCode.GR004)]
    public void Negatif_senaryolar_dogru_gerekce_koduyla_reddedilir(string sql, ReasonCode expected)
    {
        var result = Run(sql);

        Assert.Equal(GuardrailDecision.Rejected, result.Decision);
        Assert.Equal(expected, result.ReasonCode);

        // Reddedilen sorgunun metni disa verilmez.
        Assert.Null(result.Sql);
    }

    [Theory]
    [InlineData("INSERT INTO vw_sales (region) VALUES ('Ege')")]
    [InlineData("UPDATE vw_sales SET region = 'Ege'")]
    [InlineData("DELETE FROM vw_sales")]
    [InlineData("MERGE vw_sales AS target USING vw_sales AS source ON 1 = 0 WHEN MATCHED THEN DELETE;")]
    [InlineData("EXEC sp_executesql N'SELECT 1'")]
    [InlineData("CREATE TABLE dbo.disallowed (id int)")]
    public void Oltp_kaynagi_da_yalnizca_SELECT_kabul_eder(
        string sql)
    {
        var result = Run(sql, source: DataSource.Oltp);

        Assert.Equal(GuardrailDecision.Rejected, result.Decision);
        Assert.Equal(ReasonCode.GR001, result.ReasonCode);
        Assert.Null(result.Sql);
    }

    [Fact]
    public void Yorumla_filtre_atlatma_denemesi_etkisiz_kalir()
    {
        // Yorum, yeniden uretimde dusurulur; sorgunun geri kalani yorum icine alinamaz.
        var result = Run("SELECT region FROM vw_sales WHERE region = 'Ege' -- AND kapsam");

        Assert.Equal(GuardrailDecision.Accepted, result.Decision);
        Assert.DoesNotContain("--", result.Sql!, StringComparison.Ordinal);
        Assert.Contains("region IN (@scope0", result.Sql!, StringComparison.Ordinal);
    }

    [Fact]
    public void CTE_ile_kapsam_atlatma_denemesi_engellenir()
    {
        // Dokumandaki en buyuk bosluk: dis sorguda kapsam kolonu yok. Filtre CTE govdesine
        // eklenmezse tum bolgeler toplanir.
        var result = Run(
            "WITH toplam AS (SELECT SUM(quantity) AS adet FROM vw_sales) SELECT adet FROM toplam");

        Assert.Equal(GuardrailDecision.Accepted, result.Decision);
        Assert.Contains("FROM vw_sales", result.Sql!, StringComparison.Ordinal);
        Assert.Contains("region IN (@scope0", result.Sql!, StringComparison.Ordinal);
    }

    [Fact]
    public void UNION_ile_kapsam_atlatma_denemesi_engellenir()
    {
        var result = Run("SELECT region FROM vw_sales UNION ALL SELECT region FROM vw_customer");

        Assert.Equal(GuardrailDecision.Accepted, result.Decision);

        // Iki kolun ikisinde de filtre olmali.
        var occurrences = result.Sql!.Split("IN (@scope0").Length - 1;
        Assert.Equal(2, occurrences);
    }

    [Fact]
    public void Yetkisi_cozumlenemeyen_kullanici_reddedilir()
    {
        var result = Run("SELECT region FROM vw_sales", UserDataScope.Unresolved);

        Assert.Equal(GuardrailDecision.Rejected, result.Decision);
        Assert.Equal(ReasonCode.GR007, result.ReasonCode);
    }

    [Fact]
    public void Sinirsiz_yetkili_kullanici_icin_filtre_eklenmez_ama_kayit_tutulur()
    {
        var result = Run("SELECT region FROM vw_sales", UserDataScope.Unrestricted);

        Assert.Equal(GuardrailDecision.Accepted, result.Decision);
        Assert.Empty(result.Parameters);
        Assert.Contains("SINIRSIZ", result.AppliedScopeFilter!, StringComparison.Ordinal);
    }

    [Fact]
    public void Kapsam_disi_bolge_talebi_GR008_ile_reddedilir()
    {
        // Bu test bir sure "belgelenmis bosluk" olarak duruyordu: talep reddedilmiyor,
        // enjekte edilen filtre ile birlesip mantiksal olarak bos sonuc donuyordu. Veri
        // sizmiyordu ama kullaniciya "yetkiniz yok" yerine "veri bulunamadi" gibi gorunuyor
        // ve yetkisiz erisim denemesi audit'te kendi koduyla gorunmuyordu. Bosluk kapatildi.
        var result = Run(
            "SELECT region FROM vw_sales WHERE region = 'Karadeniz'",
            UserDataScope.ForRegions("Marmara"));

        Assert.Equal(GuardrailDecision.Rejected, result.Decision);
        Assert.Equal(ReasonCode.GR008, result.ReasonCode);
        Assert.Equal("Yalnızca yetkili olduğunuz bölgeleri görebilirsiniz.", result.ReasonMessage);
    }

    [Fact]
    public void Turetilmis_kolon_adlari_allow_list_ihlali_sayilmaz()
    {
        // 'adet' veritabaninda bir kolon degil; allow-list'te aranirsa mesru sorgu reddedilirdi.
        var result = Run("SELECT SUM(quantity) AS adet FROM vw_sales");

        Assert.Equal(GuardrailDecision.Accepted, result.Decision);
    }

    [Fact]
    public void COUNT_yildizi_kabul_edilir()
    {
        var result = Run("SELECT COUNT(*) AS adet FROM vw_sales");

        Assert.Equal(GuardrailDecision.Accepted, result.Decision);
    }

    [Fact]
    public void Pipeline_kontrolleri_enum_sirasina_gore_kosar()
    {
        var names = GuardrailFactory.Create().CheckNames;

        Assert.Equal(names.OrderBy(name => name), names);
        Assert.Equal(GuardrailCheckName.RegenerateAndRevalidate, names[^1]);
    }

    private static GuardrailResult Run(
        string sql,
        UserDataScope? scope = null,
        DataSource source = DataSource.Dwh)
    {
        var context = new GuardrailContext(
            sql,
            AllowList,
            scope ?? UserDataScope.ForRegions("Marmara", "Ege"),
            new TSqlParserFactory(),
            source);

        return GuardrailFactory.Create().Execute(context);
    }
}
