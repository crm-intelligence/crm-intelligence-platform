using Crm.Analytics.Sql.Catalog;
using Crm.Analytics.Sql.Contracts;
using Crm.Analytics.Sql.Guardrail;
using Crm.Analytics.Sql.Guardrail.Checks;
using Crm.Analytics.Sql.Parsing;
using Crm.Analytics.Sql.Tests.Fixtures;

namespace Crm.Analytics.Sql.Tests.Guardrail;

/// <summary>
/// Yapisal kontroller (0-4): girdi siniri, parse, tek ifade, SELECT-only, yildiz yasagi.
/// Proje dokumanindaki negatif test paketinin buyuk bolumu burada karsilaniyor.
/// </summary>
public class StructuralChecksTests
{
    private static readonly AllowListDocument AllowList =
        AllowListLoader.FromJson(TestFixtures.ReadAllowListJson());

    // --- InputLimits ---------------------------------------------------------

    [Fact]
    public void InputLimits_normal_sorguyu_gecirir()
    {
        var result = Run(new InputLimitsCheck(), "SELECT region FROM vw_sales", parseFirst: false);

        Assert.Equal(CheckOutcome.Passed, result.Outcome);
    }

    [Fact]
    public void InputLimits_asiri_uzun_girdiyi_reddeder()
    {
        var longSql = "SELECT region FROM vw_sales WHERE region = 'x'" + new string('x', 9000);

        var result = Run(new InputLimitsCheck(), longSql, parseFirst: false);

        Assert.Equal(ReasonCode.GR015, result.ReasonCode);
    }

    [Fact]
    public void InputLimits_bos_girdiyi_reddeder()
    {
        var result = Run(new InputLimitsCheck(), "   ", parseFirst: false);

        Assert.Equal(ReasonCode.GR015, result.ReasonCode);
    }

    // --- ParseToAst ----------------------------------------------------------

    [Fact]
    public void ParseToAst_gecerli_sorguyu_parse_eder_ve_agaci_baglar()
    {
        var context = CreateContext("SELECT region FROM vw_sales");

        var result = new ParseToAstCheck().Execute(context);

        Assert.Equal(CheckOutcome.Passed, result.Outcome);
        Assert.NotNull(context.Fragment);
    }

    [Theory]
    [InlineData("SELECT FROM WHERE")]
    [InlineData("SELEKT region FROM vw_sales")]
    [InlineData("SELECT region FROM")]
    public void ParseToAst_gecersiz_sozdizimini_reddeder(string sql)
    {
        var context = CreateContext(sql);

        var result = new ParseToAstCheck().Execute(context);

        Assert.Equal(ReasonCode.GR002, result.ReasonCode);
    }

    // --- SingleStatement -----------------------------------------------------

    [Fact]
    public void SingleStatement_tek_ifadeyi_gecirir()
    {
        var result = Run(new SingleStatementCheck(), "SELECT region FROM vw_sales");

        Assert.Equal(CheckOutcome.Passed, result.Outcome);
    }

    [Theory]
    [InlineData("SELECT 1; SELECT 2")]
    [InlineData("SELECT 1; DROP TABLE vw_sales")]
    public void SingleStatement_ifade_zincirini_reddeder(string sql)
    {
        var result = Run(new SingleStatementCheck(), sql);

        Assert.Equal(ReasonCode.GR002, result.ReasonCode);
    }

    [Fact]
    public void SingleStatement_noktali_virgul_iceren_literali_yanlis_pozitif_uretmez()
    {
        // Metin tabanli ';' sayimi bu sorguyu reddederdi. AST uzerinden sayim dogru sonuc verir.
        var result = Run(new SingleStatementCheck(),
            "SELECT region FROM vw_sales WHERE product_category = 'a;b'");

        Assert.Equal(CheckOutcome.Passed, result.Outcome);
    }

    [Fact]
    public void SingleStatement_GO_ile_ayrilmis_batch_zincirini_reddeder()
    {
        var result = Run(new SingleStatementCheck(),
            "SELECT region FROM vw_sales" + System.Environment.NewLine +
            "GO" + System.Environment.NewLine +
            "SELECT region FROM vw_customer");

        Assert.Equal(ReasonCode.GR002, result.ReasonCode);
    }

    // --- SelectOnly ----------------------------------------------------------

    [Fact]
    public void SelectOnly_select_ifadesini_gecirir()
    {
        var result = Run(new SelectOnlyCheck(), "SELECT region FROM vw_sales");

        Assert.Equal(CheckOutcome.Passed, result.Outcome);
    }

    [Theory]
    [InlineData("DELETE FROM vw_sales")]
    [InlineData("INSERT INTO vw_sales (region) VALUES ('Ege')")]
    [InlineData("UPDATE vw_sales SET region = 'Ege'")]
    [InlineData("DROP TABLE vw_sales")]
    [InlineData("TRUNCATE TABLE vw_sales")]
    [InlineData("ALTER TABLE vw_sales ADD x INT")]
    [InlineData("CREATE TABLE x (y INT)")]
    [InlineData("GRANT SELECT ON vw_sales TO public")]
    [InlineData("EXEC sp_executesql N'SELECT 1'")]
    [InlineData("EXEC sp_set_session_context @key = N'UserId', @value = 1")]
    [InlineData("MERGE vw_sales AS t USING vw_customer AS s ON t.region = s.region WHEN MATCHED THEN DELETE;")]
    public void SelectOnly_veri_degistiren_ve_calistiran_ifadeleri_reddeder(string sql)
    {
        var result = Run(new SelectOnlyCheck(), sql);

        // Gerekce kodu tam olarak GR001 olmali. "Reddedildi" yetmez: yanlis gerekce ile
        // reddetmek audit'i yaniltir ve kullaniciya yanlis mesaj gonderir.
        Assert.Equal(CheckOutcome.Failed, result.Outcome);
        Assert.Equal(ReasonCode.GR001, result.ReasonCode);
    }

    [Fact]
    public void SelectOnly_sp_set_session_context_cagrisini_reddeder()
    {
        // Kritik: DB tarafindaki RLS kimligini degistirme denemesi. sp_set_session_context
        // @read_only=1 ile korunuyor olsa dahi guardrail bu cagriyi hic gecirmemeli.
        var result = Run(new SelectOnlyCheck(),
            "EXEC sp_set_session_context @key = N'UserState', @value = N'SP'");

        Assert.Equal(CheckOutcome.Failed, result.Outcome);
    }

    // --- NoStarSelect --------------------------------------------------------

    [Fact]
    public void NoStarSelect_acik_kolon_listesini_gecirir()
    {
        var result = Run(new NoStarSelectCheck(), "SELECT region, net_amount FROM vw_sales");

        Assert.Equal(CheckOutcome.Passed, result.Outcome);
    }

    [Theory]
    [InlineData("SELECT * FROM vw_sales")]
    [InlineData("SELECT v.* FROM vw_sales v")]
    [InlineData("SELECT region, v.* FROM vw_sales v")]
    [InlineData("SELECT t.region FROM (SELECT * FROM vw_sales) t")]
    [InlineData("WITH c AS (SELECT * FROM vw_sales) SELECT region FROM c")]
    public void NoStarSelect_yildiz_kullanimini_her_bicimde_reddeder(string sql)
    {
        // Nitelendirilmis 'v.*' ve alt sorgu icindeki yildiz da reddedilir: dokumanda yalnizca
        // "SELECT *" yaziyordu, kapsam bilincli genisletildi. Yildiz calisma aninda kolonlara
        // genisledigi icin allow-list kontrolunu etkisiz kilar.
        var result = Run(new NoStarSelectCheck(), sql);

        Assert.Equal(ReasonCode.GR004, result.ReasonCode);
    }

    [Fact]
    public void NoStarSelect_COUNT_yildizi_reddetmez()
    {
        // COUNT(*) bir kolon genislemesi degildir; reddedilmesi mesru sorgulari engellerdi.
        var result = Run(new NoStarSelectCheck(), "SELECT COUNT(*) FROM vw_sales");

        Assert.Equal(CheckOutcome.Passed, result.Outcome);
    }

    // --- yardimcilar ---------------------------------------------------------

    private static GuardrailContext CreateContext(string sql) =>
        new(sql, AllowList, UserDataScope.ForRegions("Marmara"), new TSqlParserFactory());

    private static CheckResult Run(IGuardrailCheck check, string sql, bool parseFirst = true)
    {
        var context = CreateContext(sql);

        if (parseFirst)
        {
            var parseResult = new ParseToAstCheck().Execute(context);

            // Parse basarisizsa test edilen kontrol zaten kosulmaz; bunu testte gorunur kil.
            Assert.True(
                parseResult.Outcome == CheckOutcome.Passed,
                $"Test SQL'i parse edilemedi, kontrol kosulamadi: {parseResult.Detail}");
        }

        return check.Execute(context);
    }
}
