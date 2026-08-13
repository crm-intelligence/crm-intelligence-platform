using Crm.Analytics.Sql.Catalog;
using Crm.Analytics.Sql.Contracts;
using Crm.Analytics.Sql.Guardrail;
using Crm.Analytics.Sql.Parsing;
using Crm.Analytics.Sql.Tests.Fixtures;

namespace Crm.Analytics.Sql.Tests.Guardrail;

/// <summary>
/// Kontrol 9 (JoinPathAllowed) ve 10 (MinCellSize).
/// </summary>
/// <remarks>
/// MinCellSize, analizde tespit edilen "gecerli ama uygunsuz sorgu" bosluguna karsilik gelir:
/// <c>SELECT customer_id, SUM(quantity) ... GROUP BY customer_id</c> sorgusu allow-list'te,
/// PII degil, kapsam icinde ve tum diger kontrolleri geciyor — ama tek bir kisiyi izole eder.
/// Guardrail'in sozdizimsel denetimi bu tur bir ifsayi kendiliginden yakalamaz.
/// </remarks>
public class PrivacyChecksTests
{
    private static readonly AllowListDocument AllowList =
        AllowListLoader.FromJson(TestFixtures.ReadAllowListJson());

    // --- JoinPathAllowed -----------------------------------------------------

    [Fact]
    public void Tanimli_JOIN_yolu_kabul_edilir()
    {
        var result = Run(
            "SELECT v.region, c.segment FROM vw_sales v JOIN vw_customer c ON v.customer_id = c.customer_id");

        Assert.Equal(GuardrailDecision.Accepted, result.Decision);
    }

    [Fact]
    public void Tanimli_JOIN_yolu_ters_yonde_de_kabul_edilir()
    {
        // Allow-list'te yol vw_sales -> vw_customer olarak tanimli; sorgunun yazim yonu
        // guvenlik acisindan bir anlam tasimaz.
        var result = Run(
            "SELECT v.region, c.segment FROM vw_customer c JOIN vw_sales v ON c.customer_id = v.customer_id");

        Assert.Equal(GuardrailDecision.Accepted, result.Decision);
    }

    [Fact]
    public void Tanimsiz_JOIN_yolu_reddedilir()
    {
        // vw_sales -> vw_campaign yolu allow-list'te YOK. Iki obje de izinli olsa dahi
        // birlikte raporlanmalari ayrica izinli olmak zorunda.
        var result = Run(
            "SELECT v.region, k.channel FROM vw_sales v JOIN vw_campaign k ON v.region = k.region");

        Assert.Equal(GuardrailDecision.Rejected, result.Decision);
        Assert.Equal(ReasonCode.GR006, result.ReasonCode);
    }

    [Fact]
    public void JOIN_sayisi_siniri_asilirsa_reddedilir()
    {
        // fixture: maxJoins = 2
        var result = Run(
            "SELECT v.region FROM vw_sales v " +
            "JOIN vw_customer c ON v.customer_id = c.customer_id " +
            "JOIN vw_date d ON v.order_date = d.date_key " +
            "JOIN vw_campaign k ON v.region = k.region");

        Assert.Equal(GuardrailDecision.Rejected, result.Decision);
        Assert.Equal(ReasonCode.GR006, result.ReasonCode);
    }

    [Fact]
    public void Virgullu_eski_stil_JOIN_de_yol_denetimine_tabidir()
    {
        // Virgullu yazim JOIN anahtar kelimesi icermez; sayilmazsa denetimden kacardi.
        var result = Run(
            "SELECT v.region, k.channel FROM vw_sales v, vw_campaign k WHERE v.region = k.region");

        Assert.Equal(GuardrailDecision.Rejected, result.Decision);
        Assert.Equal(ReasonCode.GR006, result.ReasonCode);
    }

    // --- MinCellSize ---------------------------------------------------------

    [Fact]
    public void Kimlik_kolonu_ile_kirilim_reddedilir()
    {
        var result = Run("SELECT customer_id, SUM(quantity) AS adet FROM vw_sales GROUP BY customer_id");

        Assert.Equal(GuardrailDecision.Rejected, result.Decision);
        Assert.Equal(ReasonCode.GR012, result.ReasonCode);
    }

    [Fact]
    public void Kimlik_kolonu_SELECT_listesinde_kirilim_olarak_reddedilir()
    {
        var result = Run("SELECT order_id, quantity FROM vw_sales");

        Assert.Equal(GuardrailDecision.Rejected, result.Decision);
        Assert.Equal(ReasonCode.GR012, result.ReasonCode);
    }

    [Fact]
    public void Kimlik_kolonunun_agregasyon_icinde_kullanilmasi_serbest()
    {
        // COUNT(DISTINCT order_id) mesru bir metrik (siparis sayisi); kimlik ifsa etmez.
        var result = Run("SELECT region, COUNT(DISTINCT order_id) AS siparis FROM vw_sales GROUP BY region");

        Assert.Equal(GuardrailDecision.Accepted, result.Decision);
    }

    [Fact]
    public void Kimlik_kolonunun_filtrede_kullanilmasi_serbest()
    {
        // Filtre bir kirilim degildir; belirli bir siparisi sorgulamak kimlik ifsasi yaratmaz.
        var result = Run("SELECT region, SUM(quantity) AS adet FROM vw_sales WHERE order_id = 1 GROUP BY region");

        Assert.Equal(GuardrailDecision.Accepted, result.Decision);
    }

    [Theory]
    [InlineData("HAVING COUNT(*) = 1")]
    [InlineData("HAVING COUNT(*) < 3")]
    [InlineData("HAVING COUNT(*) <= 2")]
    public void HAVING_ile_tek_kaydi_izole_etme_denemesi_reddedilir(string having)
    {
        // fixture: minCellSize = 5. Kucuk gruplari secmek, tek kisiyi tespit etmenin
        // klasik yoludur (differencing attack).
        var result = Run($"SELECT region, COUNT(*) AS adet FROM vw_sales GROUP BY region {having}");

        Assert.Equal(GuardrailDecision.Rejected, result.Decision);
        Assert.Equal(ReasonCode.GR012, result.ReasonCode);
    }

    [Fact]
    public void HAVING_ile_buyuk_gruplari_secmek_serbest()
    {
        var result = Run("SELECT region, COUNT(*) AS adet FROM vw_sales GROUP BY region HAVING COUNT(*) >= 10");

        Assert.Equal(GuardrailDecision.Accepted, result.Decision);
    }

    [Fact]
    public void Normal_kirilim_serbest()
    {
        var result = Run("SELECT region, SUM(quantity) AS adet FROM vw_sales GROUP BY region");

        Assert.Equal(GuardrailDecision.Accepted, result.Decision);
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
