using System.Text.RegularExpressions;
using Crm.Analytics.Sql.Catalog;
using Crm.Analytics.Sql.Contracts;
using Crm.Analytics.Sql.Guardrail;
using Crm.Analytics.Sql.Guardrail.Mutation;
using Crm.Analytics.Sql.Parsing;
using Crm.Analytics.Sql.Tests.Fixtures;

namespace Crm.Analytics.Sql.Tests.Guardrail;

/// <summary>
/// Kapsam filtresi enjeksiyonu. Dokumandaki "filtreyi WHERE'e ekle" ifadesi tek bir WHERE
/// oldugunu varsayar; bu testler CTE, turetilmis tablo ve UNION yapilarinda o varsayimin
/// coktugu ve filtrenin her sorgu blokuna ayri ayri eklenmesi gerektigini sabitler.
/// </summary>
public class ScopeFilterInjectorTests
{
    private static readonly AllowListDocument AllowList =
        AllowListLoader.FromJson(TestFixtures.ReadAllowListJson());

    [Fact]
    public void Filtresiz_sorguya_WHERE_eklenir()
    {
        var (sql, outcome) = Inject("SELECT region FROM vw_sales");

        Assert.Equal(1, outcome.InjectedBlockCount);
        Assert.Contains("WHERE vw_sales.region IN (@scope0, @scope1)", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Alias_varsa_kolon_alias_ile_nitelendirilir()
    {
        var (sql, _) = Inject("SELECT v.region FROM vw_sales v");

        Assert.Contains("WHERE v.region IN (@scope0, @scope1)", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void JOIN_edilen_her_kapsamli_tabloya_ayri_filtre_eklenir()
    {
        // Yalnizca bir tarafi filtrelemek digerinden veri kacirir: musteri tarafindan
        // secilen region kolonu filtresiz kalirdi.
        var (sql, outcome) = Inject(
            "SELECT v.region, c.segment FROM vw_sales v JOIN vw_customer c ON v.customer_id = c.customer_id");

        Assert.Equal(2, outcome.InjectedPredicateCount);
        Assert.Contains("v.region IN (@scope0, @scope1)", sql, StringComparison.Ordinal);
        Assert.Contains("c.region IN (@scope0, @scope1)", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void CTE_govdesine_filtre_eklenir()
    {
        // Dokumandaki en buyuk bosluk: dis sorguda region kolonu bile yoktur. Filtre yalnizca
        // dis WHERE'e eklenirse CTE tum bolgeleri okuyup toplar ve kapsam tamamen atlanir.
        var (sql, outcome) = Inject(
            "WITH toplam AS (SELECT SUM(net_amount) AS tutar FROM vw_sales) SELECT tutar FROM toplam");

        Assert.Equal(1, outcome.InjectedBlockCount);
        Assert.Contains("FROM vw_sales WHERE vw_sales.region IN (@scope0, @scope1)", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void CTE_referansi_allow_list_disi_sayilmaz_ve_filtre_almaz()
    {
        var (sql, outcome) = Inject(
            "WITH toplam AS (SELECT region, SUM(net_amount) AS tutar FROM vw_sales GROUP BY region) " +
            "SELECT region, tutar FROM toplam");

        // Yalnizca CTE govdesi filtre alir; CTE referansi bir obje degildir.
        Assert.Equal(1, outcome.InjectedPredicateCount);
        Assert.DoesNotContain("toplam.region IN", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Turetilmis_tablonun_icine_filtre_eklenir()
    {
        var (sql, outcome) = Inject(
            "SELECT t.tutar FROM (SELECT SUM(net_amount) AS tutar FROM vw_sales) t");

        Assert.Equal(1, outcome.InjectedBlockCount);
        Assert.Contains("FROM vw_sales WHERE vw_sales.region IN (@scope0, @scope1)", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void UNION_in_her_iki_koluna_filtre_eklenir()
    {
        // BinaryQueryExpression iki ayri sorgu blogu uretir; birini filtrelemek yetmez.
        var (sql, outcome) = Inject(
            "SELECT region FROM vw_sales UNION ALL SELECT region FROM vw_customer");

        Assert.Equal(2, outcome.InjectedBlockCount);
        Assert.Contains("FROM vw_sales WHERE vw_sales.region IN", sql, StringComparison.Ordinal);
        Assert.Contains("FROM vw_customer WHERE vw_customer.region IN", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void WHERE_icindeki_alt_sorguya_da_filtre_eklenir()
    {
        var (sql, outcome) = Inject(
            "SELECT region FROM vw_sales WHERE customer_id IN (SELECT customer_id FROM vw_customer)");

        Assert.Equal(2, outcome.InjectedBlockCount);
        Assert.Contains("FROM vw_customer WHERE vw_customer.region IN", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Mevcut_OR_kosulu_parantez_icine_alinir()
    {
        // Dogruluk gereksinimi: parantezsiz birlestirme "a OR b AND scope" uretir ve AND'in
        // onceligi nedeniyle 'a' dalindaki satirlar kapsam filtresinden KACAR.
        var (sql, _) = Inject(
            "SELECT region FROM vw_sales WHERE product_category = 'A' OR product_category = 'B'");

        Assert.Contains(
            "WHERE (product_category = 'A' OR product_category = 'B') AND vw_sales.region IN (@scope0, @scope1)",
            sql,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Mevcut_AND_kosulu_korunur()
    {
        var (sql, _) = Inject("SELECT region FROM vw_sales WHERE quantity > 5");

        Assert.Contains("quantity > 5", sql, StringComparison.Ordinal);
        Assert.Contains("vw_sales.region IN (@scope0, @scope1)", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Kapsamdan_muaf_objeye_filtre_eklenmez()
    {
        var (sql, outcome) = Inject("SELECT year, quarter FROM vw_date");

        Assert.Equal(0, outcome.InjectedPredicateCount);
        Assert.Equal(1, outcome.ExemptTableCount);
        Assert.DoesNotContain("IN (@scope", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Kapsamli_ve_muaf_obje_birlikte_kullanilirsa_yalnizca_kapsamli_filtrelenir()
    {
        var (sql, outcome) = Inject(
            "SELECT v.region FROM vw_sales v JOIN vw_date d ON v.order_date = d.date_key");

        Assert.Equal(1, outcome.InjectedPredicateCount);
        Assert.Equal(1, outcome.ExemptTableCount);
        Assert.Contains("v.region IN (@scope0, @scope1)", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("d.date_key IN (@scope", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Kotu_niyetli_alias_metne_gomulmez_ve_kacis_korunur()
    {
        // Alias kullanicidan gelir. Metne gomulse guardrail kendi eliyle enjeksiyon uretirdi.
        var (sql, _) = Inject("SELECT x.region FROM vw_sales AS [x]] WHERE 1=1 --]");

        // Uretici kacis karakterini yeniden yazar; ifade tek bir tanimlayici olarak kalir.
        Assert.Contains("[x]] WHERE 1=1 --].region IN (@scope0, @scope1)", sql, StringComparison.Ordinal);
        // Yorum, yeniden uretimde dusurulur; filtre sorgunun sonunda ve etkin kalir.
        // (Uretici, IncludeSemicolons=false ayarina ragmen noktali virgul ekliyor.)
        Assert.EndsWith("(@scope0, @scope1)", sql.TrimEnd(';', ' '), StringComparison.Ordinal);
    }

    [Fact]
    public void Parametreler_blok_sayisindan_bagimsiz_olarak_bir_kez_uretilir()
    {
        var context = CreateContext(
            "SELECT region FROM vw_sales UNION ALL SELECT region FROM vw_customer");
        new ScopeFilterInjector().Inject(context);

        // Iki blok, ayni iki parametre. Blok basina yeni parametre uretmek plan cache'i
        // sisirir ve ayni degeri birden fazla kez baglamak gerekirdi.
        Assert.Equal(2, context.Parameters.Count);
        Assert.Equal(["@scope0", "@scope1"], context.Parameters.Select(p => p.Name));
    }

    [Fact]
    public void Kapsam_parametreleri_unicode_metin_olarak_baglanir()
    {
        // Turkce karakter kaybi collation'a bagli olarak farkli satir kumesi dondurebilir.
        var context = CreateContext("SELECT region FROM vw_sales", UserDataScope.ForRegions("İç Anadolu"));
        new ScopeFilterInjector().Inject(context);

        var parameter = Assert.Single(context.Parameters);
        Assert.Equal(FilterValueKind.Text, parameter.Kind);
        Assert.True(parameter.IsUnicode);
        Assert.Equal("İç Anadolu", parameter.Raw);
    }

    [Fact]
    public void FROM_suz_sorgu_filtre_almaz()
    {
        var (_, outcome) = Inject("SELECT 1");

        Assert.Equal(0, outcome.InjectedPredicateCount);
        Assert.Equal(0, outcome.ExemptTableCount);
    }

    private static GuardrailContext CreateContext(string sql, UserDataScope? scope = null)
    {
        var factory = new TSqlParserFactory();
        var context = new GuardrailContext(
            sql,
            AllowList,
            scope ?? UserDataScope.ForRegions("Marmara", "Ege"),
            factory);

        var parsed = factory.Parse(sql);
        Assert.True(parsed.IsSuccessful, $"Test SQL'i parse edilemedi: {parsed.ErrorSummary}");
        context.SetFragment(parsed.RequireFragment());

        return context;
    }

    private static (string Sql, ScopeFilterInjector.Outcome Outcome) Inject(
        string sql,
        UserDataScope? scope = null)
    {
        var context = CreateContext(sql, scope);
        var outcome = new ScopeFilterInjector().Inject(context);
        var generated = context.ParserFactory.GenerateScript(context.RequireFragment(), out var versioningErrors);

        Assert.Empty(versioningErrors);
        return (Normalize(generated), outcome);
    }

    /// <summary>Uretici bicimlendirmesini tek bosluga indirger; testler bicime bagli olmasin.</summary>
    private static string Normalize(string sql) =>
        Regex.Replace(sql, @"\s+", " ", RegexOptions.CultureInvariant).Trim();
}
