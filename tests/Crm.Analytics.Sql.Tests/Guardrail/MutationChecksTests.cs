using Crm.Analytics.Sql.Catalog;
using Crm.Analytics.Sql.Contracts;
using Crm.Analytics.Sql.Guardrail;
using Crm.Analytics.Sql.Parsing;
using Crm.Analytics.Sql.Tests.Fixtures;

namespace Crm.Analytics.Sql.Tests.Guardrail;

/// <summary>
/// Kontrol 11 (DateRangeBudget), 13 (LiteralParameterization) ve 14 (RowLimit).
/// </summary>
public class MutationChecksTests
{
    private static readonly AllowListDocument AllowList =
        AllowListLoader.FromJson(TestFixtures.ReadAllowListJson());

    // --- LiteralParameterization ---------------------------------------------

    [Fact]
    public void Filtre_literali_parametreye_cevrilir()
    {
        var result = Run("SELECT region FROM vw_sales WHERE product_category = 'elektronik'");

        Assert.Equal(GuardrailDecision.Accepted, result.Decision);

        // Deger SQL metninde YER ALMAMALI.
        Assert.DoesNotContain("elektronik", result.Sql!, StringComparison.Ordinal);

        var parameter = Assert.Single(result.Parameters, p => p.Raw == "elektronik");
        Assert.Equal(FilterValueKind.Text, parameter.Kind);
        Assert.True(parameter.IsUnicode);
    }

    [Fact]
    public void Sayisal_literal_dogru_tiple_parametrelenir()
    {
        var result = Run("SELECT region FROM vw_sales WHERE quantity > 5");

        Assert.DoesNotContain("> 5", result.Sql!, StringComparison.Ordinal);

        var parameter = Assert.Single(result.Parameters, p => p.Raw == "5");
        Assert.Equal(FilterValueKind.Integer, parameter.Kind);
    }

    [Fact]
    public void IN_listesindeki_her_deger_ayri_parametre_olur()
    {
        var result = Run("SELECT region FROM vw_sales WHERE product_category IN ('a', 'b', 'c')");

        Assert.Equal(GuardrailDecision.Accepted, result.Decision);
        Assert.Equal(3, result.Parameters.Count(p => p.Raw is "a" or "b" or "c"));
        Assert.DoesNotContain("'a'", result.Sql!, StringComparison.Ordinal);
    }

    [Fact]
    public void BETWEEN_sinirlari_parametrelenir()
    {
        var result = Run("SELECT region FROM vw_sales WHERE quantity BETWEEN 1 AND 9");

        Assert.Equal(GuardrailDecision.Accepted, result.Decision);
        Assert.Contains(result.Parameters, p => p.Raw == "1");
        Assert.Contains(result.Parameters, p => p.Raw == "9");
    }

    [Fact]
    public void LIKE_kalibi_parametrelenir()
    {
        var result = Run("SELECT region FROM vw_sales WHERE product_category LIKE 'elek%'");

        Assert.DoesNotContain("elek%", result.Sql!, StringComparison.Ordinal);
        Assert.Contains(result.Parameters, p => p.Raw == "elek%");
    }

    [Fact]
    public void NULL_karsilastirmasi_parametrelenmez()
    {
        // 'x = NULL' ile 'x = @p' semantik olarak ayni degildir (ANSI_NULLS).
        var result = Run("SELECT region FROM vw_sales WHERE product_category IS NULL");

        Assert.Equal(GuardrailDecision.Accepted, result.Decision);
        Assert.Contains("NULL", result.Sql!, StringComparison.Ordinal);
    }

    [Fact]
    public void Turkce_karakterli_deger_unicode_olarak_baglanir()
    {
        var result = Run("SELECT region FROM vw_sales WHERE product_category = N'İç Giyim'");

        var parameter = Assert.Single(result.Parameters, p => p.Raw == "İç Giyim");
        Assert.True(parameter.IsUnicode);
        Assert.DoesNotContain("İç Giyim", result.Sql!, StringComparison.Ordinal);
    }

    // --- RowLimit ------------------------------------------------------------

    [Fact]
    public void Limitsiz_sorguya_TOP_eklenir()
    {
        var result = Run("SELECT region FROM vw_sales");

        Assert.Contains("TOP", result.Sql!, StringComparison.Ordinal);
        Assert.Contains("5000", result.Sql!, StringComparison.Ordinal);
    }

    [Fact]
    public void Sinirin_uzerindeki_TOP_dusurulur()
    {
        var result = Run("SELECT TOP 100000 region FROM vw_sales");

        Assert.Contains("5000", result.Sql!, StringComparison.Ordinal);
        Assert.DoesNotContain("100000", result.Sql!, StringComparison.Ordinal);
    }

    [Fact]
    public void Sinirin_altindaki_TOP_korunur()
    {
        var result = Run("SELECT TOP 10 region FROM vw_sales");

        Assert.Contains("TOP 10", result.Sql!, StringComparison.Ordinal);
    }

    [Fact]
    public void Yuzde_bazli_TOP_sabit_degere_cevrilir()
    {
        // TOP 50 PERCENT satir sayisini garanti etmez.
        var result = Run("SELECT TOP 50 PERCENT region FROM vw_sales");

        Assert.DoesNotContain("PERCENT", result.Sql!, StringComparison.Ordinal);
        Assert.Contains("5000", result.Sql!, StringComparison.Ordinal);
    }

    [Fact]
    public void Alt_sorguya_TOP_eklenmez()
    {
        // Alt sorguya TOP eklemek agregasyonu kismi veri uzerinden hesaplar ve YANLIS
        // toplam uretir. Bu bir dogruluk gereksinimidir.
        var result = Run("SELECT t.adet FROM (SELECT SUM(quantity) AS adet FROM vw_sales) t");

        Assert.Equal(GuardrailDecision.Accepted, result.Decision);

        var topOccurrences = result.Sql!.Split("TOP").Length - 1;
        Assert.Equal(1, topOccurrences);
    }

    [Fact]
    public void UNION_in_her_koluna_TOP_eklenir()
    {
        var result = Run("SELECT region FROM vw_sales UNION ALL SELECT region FROM vw_customer");

        var topOccurrences = result.Sql!.Split("TOP").Length - 1;
        Assert.Equal(2, topOccurrences);
    }

    // --- DateRangeBudget -----------------------------------------------------

    [Fact]
    public void Butce_icindeki_tarih_araligi_kabul_edilir()
    {
        var result = Run(
            "SELECT region FROM vw_sales WHERE order_date BETWEEN '2026-01-01' AND '2026-06-30'");

        Assert.Equal(GuardrailDecision.Accepted, result.Decision);
    }

    [Fact]
    public void Asiri_genis_tarih_araligi_reddedilir()
    {
        // fixture: maxDateRangeDays = 1100 (~3 yil)
        var result = Run(
            "SELECT region FROM vw_sales WHERE order_date BETWEEN '2010-01-01' AND '2026-01-01'");

        Assert.Equal(GuardrailDecision.Rejected, result.Decision);
        Assert.Equal(ReasonCode.GR013, result.ReasonCode);
    }

    [Fact]
    public void Tarih_filtresi_olmayan_sorgu_reddedilmez()
    {
        // "SELECT COUNT(*)" mesru bir taleptir; reddetmek kullanilabilirligi gereksiz kirardi.
        // Koruma RowLimit ve komut timeout'una kalir.
        var result = Run("SELECT COUNT(*) AS adet FROM vw_sales");

        Assert.Equal(GuardrailDecision.Accepted, result.Decision);
    }

    [Fact]
    public void Tarih_olmayan_metin_literalleri_aralik_sayilmaz()
    {
        var result = Run(
            "SELECT region FROM vw_sales WHERE product_category IN ('elektronik', 'mobilya')");

        Assert.Equal(GuardrailDecision.Accepted, result.Decision);
    }

    // --- hat tamamlandi ------------------------------------------------------

    [Fact]
    public void Hat_tamamlandi_eksik_kontrol_yok()
    {
        Assert.Empty(GuardrailFactory.MissingChecks);
    }

    [Fact]
    public void Onalti_kontrolun_tamami_bagli_ve_sirali()
    {
        var names = GuardrailFactory.Create().CheckNames;

        Assert.Equal(Enum.GetValues<GuardrailCheckName>().Length, names.Count);
        Assert.Equal(names.OrderBy(name => name), names);
    }

    private static GuardrailResult Run(string sql)
    {
        var context = new GuardrailContext(
            sql,
            AllowList,
            UserDataScope.ForRegions("Marmara", "Ege"),
            new TSqlParserFactory());

        return GuardrailFactory.Create().Execute(context);
    }
}
