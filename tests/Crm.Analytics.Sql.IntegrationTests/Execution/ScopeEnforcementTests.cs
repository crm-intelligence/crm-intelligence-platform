using Crm.Analytics.Sql.Contracts;
using Crm.Analytics.Sql.IntegrationTests.Sqlite;

namespace Crm.Analytics.Sql.IntegrationTests.Execution;

/// <summary>
/// Kapsam filtresinin GERCEK VERIDE gercekten kisitladigini kanitlar.
/// </summary>
/// <remarks>
/// Bu dosyadaki testler, guardrail'in birim testleriyle dogrulanamayan tek seyi
/// dogrular: enjekte edilen kapsam yukleminin dondurulen SATIRLARI degistirmesi.
/// </remarks>
[Collection(OlistDatabaseCollection.Name)]
public sealed class ScopeEnforcementTests(OlistDatabaseFixture fixture)
{
    [OlistDatabaseFact]
    public void Kapsam_filtresi_dondurulen_satirlari_gercekten_kisitlar()
    {
        // Karsilastirma tabani: veri setinde 27 eyalet var. Bu taban olmadan
        // "filtre calisti" ile "veride zaten 2 eyalet vardi" ayirt edilemez.
        var toplamEyalet = fixture.ScalarLong(
            "SELECT COUNT(DISTINCT customer_state) FROM vw_sales");

        Assert.Equal(27, toplamEyalet);

        var response = GuardrailHarness.Produce(
            "2018 satış tutarını eyalete göre göster",
            UserDataScope.ForRegions("SP", "RJ"));

        Assert.Equal(GuardrailDecision.Accepted, response.Decision);

        var rows = fixture.QueryRunner.Execute(response);

        var eyaletler = rows
            .Select(r => (string?)r["customer_state"])
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(new[] { "RJ", "SP" }, eyaletler);
    }

    [OlistDatabaseFact]
    public void Kapsamini_genisletmeye_calisan_taslak_reddedilir_ve_veri_donmez()
    {
        // Kotu niyetli bir taslak kendi kapsamini genisletmeye calisiyor.
        //
        // Gercek davranis, "daraltarak etkisizlestirme" DEGIL, TAM RET (GR008)
        // — yani fail-closed. Bu daha guclu bir garanti: guardrail kapsam disi
        // bir literal gordugunde sorguyu sessizce duzeltmeye calismiyor,
        // reddediyor.
        //
        // BA ve CE veride GERCEKTEN var; yani reddetme "veri yoktu" degil,
        // "yetki yoktu" anlamina geliyor.
        var baSatir = fixture.ScalarLong(
            "SELECT COUNT(*) FROM vw_sales WHERE customer_state = 'BA'");
        var ceSatir = fixture.ScalarLong(
            "SELECT COUNT(*) FROM vw_sales WHERE customer_state = 'CE'");

        Assert.True(baSatir > 0, "Test anlamsiz: BA veride yok.");
        Assert.True(ceSatir > 0, "Test anlamsiz: CE veride yok.");

        var result = GuardrailHarness.RunGuardrail(
            """
            SELECT customer_state, SUM(price) AS toplam
            FROM mart.vw_sales
            WHERE customer_state IN ('SP', 'BA', 'CE')
            GROUP BY customer_state
            """,
            UserDataScope.ForRegions("SP"));

        Assert.Equal(GuardrailDecision.Rejected, result.Decision);
        Assert.Equal(ReasonCode.GR008, result.ReasonCode);

        // Reddedilen sorgunun metni disa verilmez ve veritabanina ulasmaz.
        Assert.Null(result.Sql);
        Assert.Throws<InvalidOperationException>(
            () => fixture.QueryRunner.Execute(result));
    }

    [OlistDatabaseFact]
    public void Kullanicinin_filtresi_kapsamla_kesisir_birlesmez()
    {
        // Kapsam SP+RJ, kullanici yalnizca SP istiyor: iki yuklem AND'lenir,
        // OR'lanmaz. OR'lansaydi kullanici kendi filtresini genisletme araci
        // olarak kullanabilirdi.
        var result = GuardrailHarness.RunGuardrail(
            """
            SELECT customer_state, SUM(price) AS toplam
            FROM mart.vw_sales
            WHERE customer_state IN ('SP')
            GROUP BY customer_state
            """,
            UserDataScope.ForRegions("SP", "RJ"));

        Assert.Equal(GuardrailDecision.Accepted, result.Decision);

        var rows = fixture.QueryRunner.Execute(result);

        // Kapsam RJ'yi de iceriyor ama kullanici istemedigi icin donmuyor.
        Assert.Equal(["SP"], rows.Select(r => (string?)r["customer_state"]).ToList());

        // Kullanicinin literali de parametreye cevrilmis olmali.
        var executed = fixture.QueryRunner.LastExecutedSql!;

        Assert.DoesNotContain("'SP'", executed, StringComparison.Ordinal);
        Assert.Contains("@scope0", executed, StringComparison.Ordinal);
    }

    [OlistDatabaseFact]
    public void Ayni_sorgu_farkli_kapsamla_ayni_metni_farkli_sonucu_uretir()
    {
        // Bu test olmadan bir inceleyici, yukaridaki testlerin guardrail
        // sayesinde mi yoksa harness degerleri metne gomdugu icin mi gectigini
        // ayirt edemez.
        const string Prompt = "2018 satış tutarını eyalete göre göster";

        var sp = GuardrailHarness.Produce(Prompt, UserDataScope.ForRegions("SP"));
        var rj = GuardrailHarness.Produce(Prompt, UserDataScope.ForRegions("RJ"));

        Assert.Equal(GuardrailDecision.Accepted, sp.Decision);
        Assert.Equal(GuardrailDecision.Accepted, rj.Decision);

        // SQL metni birebir AYNI: kapsam parametreyle tasiniyor.
        Assert.Equal(sp.Sql, rj.Sql);
        Assert.DoesNotContain("'SP'", sp.Sql!, StringComparison.Ordinal);

        var spRows = fixture.QueryRunner.Execute(sp);
        var rjRows = fixture.QueryRunner.Execute(rj);

        // Sonuclar FARKLI.
        Assert.Equal("SP", spRows.Single()["customer_state"]);
        Assert.Equal("RJ", rjRows.Single()["customer_state"]);
    }

    [OlistDatabaseFact]
    public void Kapsam_disi_deger_isteyen_sorgu_veritabanina_hic_ulasmaz()
    {
        var result = GuardrailHarness.RunGuardrail(
            """
            SELECT customer_state, SUM(price) AS toplam
            FROM vw_sales
            WHERE customer_state = 'MG'
            GROUP BY customer_state
            """,
            UserDataScope.ForRegions("SP"));

        Assert.NotEqual(GuardrailDecision.Accepted, result.Decision);
        Assert.Null(result.Sql);

        // Kosucu reddedilmis sonucu calistirmayi REDDEDER.
        var ex = Assert.Throws<InvalidOperationException>(
            () => fixture.QueryRunner.Execute(result));

        Assert.Contains("Accepted", ex.Message, StringComparison.Ordinal);
    }

}
