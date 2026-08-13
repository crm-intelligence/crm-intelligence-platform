using Crm.Analytics.Sql.Catalog;
using Crm.Analytics.Sql.Contracts;
using Crm.Analytics.Sql.Guardrail;
using Crm.Analytics.Sql.Parsing;
using Crm.Analytics.Sql.Tests.Fixtures;

namespace Crm.Analytics.Sql.Tests.Guardrail;

/// <summary>
/// Kontrol 5 (NodeTypeWhitelist): bir SELECT'in <b>icinde</b> yer alabilen tehlikeli yapilar.
/// </summary>
/// <remarks>
/// Bu yapilarin hepsi gramere uygun birer SELECT'tir, dolayisiyla SelectOnly kontrolune
/// takilmazlar. Allow-list kontrolu de yakalayamaz: <c>AllowListObjects</c> yalnizca
/// <c>NamedTableReference</c> denetler, oysa OPENROWSET / OPENJSON / APPLY / inline tablo
/// fonksiyonu farkli dugum tipleridir ve allow-list'te aranmadan gecerler.
/// </remarks>
public class NodeTypeWhitelistTests
{
    private static readonly AllowListDocument AllowList =
        AllowListLoader.FromJson(TestFixtures.ReadAllowListJson());

    [Theory]
    // ---- Asil acik: yapiya ait KOLON SECILMEDIGI icin AllowListColumns devreye girmez ----
    // Bu senaryolarda tek gercek savunma NodeTypeWhitelist'tir.
    [InlineData("SELECT 1 AS x FROM OPENROWSET(BULK 'C:\\gizli.txt', SINGLE_CLOB) AS c")]
    [InlineData("SELECT v.region FROM vw_sales v CROSS APPLY dbo.fn_gizli(v.region) AS f")]
    [InlineData("SELECT v.region FROM vw_sales v OUTER APPLY dbo.fn_gizli(v.region) AS f")]
    [InlineData("SELECT v.region FROM vw_sales v CROSS JOIN OPENJSON('[1,2]') AS j")]

    // ---- Kolon secilen bicimler: bunlar zaten GR004'e takiliyor, yine de GR011 beklenir ----
    // Dosya sisteminden okuma
    [InlineData("SELECT c.BulkColumn FROM OPENROWSET(BULK 'C:\\gizli.txt', SINGLE_CLOB) AS c")]
    // JSON ayristirma: allow-list'te olmayan bir kaynaktan satir uretir
    [InlineData("SELECT j.value FROM OPENJSON('[1,2,3]') AS j")]
    // Kullanici tanimli fonksiyon: govdesi allow-list disi tabloya erisebilir
    [InlineData("SELECT f.x FROM dbo.fn_gizli_veri() AS f")]
    // CROSS APPLY ile fonksiyon cagrisi
    [InlineData("SELECT v.region, f.x FROM vw_sales v CROSS APPLY dbo.fn_gizli(v.region) AS f")]
    // OUTER APPLY ayni yolu acar
    [InlineData("SELECT v.region, f.x FROM vw_sales v OUTER APPLY dbo.fn_gizli(v.region) AS f")]
    public void Beyaz_liste_disi_yapilar_reddedilmeli(string sql)
    {
        var result = Run(sql);

        // Beklenen: bu yapilar GR011 ile reddedilir.
        Assert.Equal(GuardrailDecision.Rejected, result.Decision);
        Assert.Equal(ReasonCode.GR011, result.ReasonCode);
    }

    [Theory]
    // Mesru yapilar reddedilmemeli: kontrolun asiri genis olmadiginin kaniti.
    [InlineData("SELECT region FROM vw_sales")]
    [InlineData("SELECT region, SUM(quantity) AS adet FROM vw_sales GROUP BY region")]
    [InlineData("SELECT t.adet FROM (SELECT SUM(quantity) AS adet FROM vw_sales) t")]
    [InlineData("WITH c AS (SELECT region FROM vw_sales) SELECT region FROM c")]
    [InlineData("SELECT region FROM vw_sales UNION ALL SELECT region FROM vw_customer")]
    [InlineData("SELECT COUNT(*) AS adet FROM vw_sales")]
    [InlineData("SELECT region FROM vw_sales WHERE customer_id IN (SELECT customer_id FROM vw_customer)")]
    [InlineData("SELECT v.region FROM vw_sales v JOIN vw_customer c ON v.customer_id = c.customer_id")]
    [InlineData("SELECT region, CASE WHEN quantity > 5 THEN 'yuksek' ELSE 'dusuk' END AS bant FROM vw_sales")]
    public void Mesru_yapilar_gecmeli(string sql)
    {
        var result = Run(sql);

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
