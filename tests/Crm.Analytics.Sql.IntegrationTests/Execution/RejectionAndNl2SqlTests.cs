using Crm.Analytics.Sql.Contracts;
using Crm.Analytics.Sql.IntegrationTests.Sqlite;

namespace Crm.Analytics.Sql.IntegrationTests.Execution;

/// <summary>
/// Reddedilen sorgularin veritabanina HIC ulasmadigini ve NL2SQL yolunun ayni
/// guardrail'dan gectigini kanitlar.
/// </summary>
[Collection(OlistDatabaseCollection.Name)]
public sealed class RejectionAndNl2SqlTests(OlistDatabaseFixture fixture)
{
    /// <summary>
    /// Reddedilmesi zorunlu kaliplar (<c>05-sql-uretimi.md</c> satir 42-48).
    /// </summary>
    public static TheoryData<string, string> ReddedilmesiZorunlu() => new()
    {
        { "veri degistirme", "DELETE FROM vw_sales" },
        { "allow-list disi nesne", "SELECT * FROM hr_employees" },
        { "yildiz secim", "SELECT * FROM vw_sales" },
        { "ifade zincirleme", "SELECT customer_city FROM vw_sales; DROP TABLE vw_sales" },
        { "dinamik SQL", "EXEC sp_executesql N'SELECT 1'" },
        { "sema onekli allow-list disi", "SELECT customer_city FROM dbo.hr_employees" },
        { "kimlik kirilimi", "SELECT customer_unique_id, SUM(total_price) AS t FROM vw_customer_rfm GROUP BY customer_unique_id" }
    };

    [OlistDatabaseTheory]
    [MemberData(nameof(ReddedilmesiZorunlu))]
    public void Reddedilen_sorgu_veritabanina_ulasamaz(string senaryo, string sql)
    {
        var result = GuardrailHarness.RunGuardrail(sql, UserDataScope.ForRegions("SP"));

        Assert.NotEqual(GuardrailDecision.Accepted, result.Decision);

        // Reddedilen sorgunun metni disa verilmez (sema bilgisi sizdirmamak icin).
        Assert.Null(result.Sql);

        // Kosucu, reddedilmis bir sonucu calistirmayi yapisal olarak reddeder.
        var ex = Assert.Throws<InvalidOperationException>(
            () => fixture.QueryRunner.Execute(result));

        Assert.Contains("Accepted", ex.Message, StringComparison.Ordinal);
        Assert.False(string.IsNullOrEmpty(senaryo));
    }

    [OlistDatabaseFact]
    public void Kisi_bazli_kirilim_reddedilir_segment_bazli_calisir()
    {
        // Guvenlik kontrolunun ONLEDIGI seyi gercek veriyle nicelendirir.
        //
        // Kisi bazli sorgu reddedilir (GR012). Ayni sekilde bir sorgu calissaydi
        // yalnizca SP icin 40.024 tekil kisiyi tek tek ifsa edecekti.
        var kisiSayisi = fixture.ScalarLong(
            "SELECT COUNT(*) FROM vw_customer_rfm WHERE customer_state = 'SP'");

        Assert.Equal(40_024, kisiSayisi);

        var kisiBazli = GuardrailHarness.RunGuardrail(
            """
            SELECT customer_unique_id, SUM(total_price) AS toplam
            FROM mart.vw_customer_rfm
            GROUP BY customer_unique_id
            """,
            UserDataScope.ForRegions("SP"));

        Assert.Equal(GuardrailDecision.Rejected, kisiBazli.Decision);
        Assert.Equal(ReasonCode.GR012, kisiBazli.ReasonCode);

        // Segment (eyalet) bazli ayni metrik CALISIR: kontrol asiri kisitlayici degil.
        var segmentBazli = GuardrailHarness.RunGuardrail(
            """
            SELECT customer_state, COUNT(customer_unique_id) AS musteri_sayisi
            FROM mart.vw_customer_rfm
            GROUP BY customer_state
            """,
            UserDataScope.ForRegions("SP"));

        Assert.Equal(GuardrailDecision.Accepted, segmentBazli.Decision);

        var rows = fixture.QueryRunner.Execute(segmentBazli);

        Assert.Equal(
            kisiSayisi,
            Convert.ToInt64(rows.Single()["musteri_sayisi"], System.Globalization.CultureInfo.InvariantCulture));
    }

    [OlistDatabaseFact]
    public void Dogal_dil_talebi_uctan_uca_gercek_veri_dondurur()
    {
        // En temel kanit: uretim hattinin (Query Builder + 16 kontrol) urettigi
        // SQL gercekten CALISTIRILABILIR ve veri donduruyor. Bu, 414 statik
        // testin dogrulayamadigi tek sey.
        var response = GuardrailHarness.Produce(
            "2018 satış tutarını eyalete göre göster",
            UserDataScope.ForRegions("SP", "RJ"));

        Assert.Equal(GuardrailDecision.Accepted, response.Decision);

        var rows = fixture.QueryRunner.Execute(response);

        Assert.NotEmpty(rows);
        Assert.All(rows, row => Assert.Contains(
            (string?)row["customer_state"], new[] { "SP", "RJ" }));
    }

    [OlistDatabaseFact]
    public void Netlestirme_isteyen_talep_calistirilmaz()
    {
        // Kapsam cozulemediginde guardrail Accepted uretmez; kosucu de
        // calistirmaz.
        var response = GuardrailHarness.Produce(
            "2018 satış tutarını eyalete göre göster",
            UserDataScope.Unresolved);

        Assert.NotEqual(GuardrailDecision.Accepted, response.Decision);

        Assert.Throws<InvalidOperationException>(
            () => fixture.QueryRunner.Execute(response));
    }
}
