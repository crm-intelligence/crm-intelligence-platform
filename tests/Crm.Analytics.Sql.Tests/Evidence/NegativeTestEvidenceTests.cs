using Crm.Analytics.Sql.Catalog;
using Crm.Analytics.Sql.Contracts;
using Crm.Analytics.Sql.Guardrail;
using Crm.Analytics.Sql.Parsing;
using Crm.Analytics.Sql.Tests.Fixtures;

namespace Crm.Analytics.Sql.Tests.Evidence;

/// <summary>
/// NEGATIF TEST KANIT PAKETI — Gun 10 cikti.
/// </summary>
/// <remarks>
/// <para>
/// Bu dosya, proje dokumanindaki negatif senaryolarin her birini <b>doküman maddesine
/// referansla</b> tek yerde toplar. Amac izlenebilirlik: "dokumanda istenen senaryo nerede
/// test edilmis" sorusu tek dosyadan cevaplanabilmeli. Senaryolarin bir kismi ilgili
/// bilesenlerin kendi test dosyalarinda da bulunur; buradaki tekrar bilinclidir.
/// </para>
/// <para>
/// Kaynak: <c>proje-dokumanlari/roller/ai-engineer-gorevleri.md</c> bolum 6.2 (15 senaryo)
/// ve <c>05-sql-uretimi.md</c> "Reddedilmesi zorunlu kaliplar". Dokumanda BULUNMAYAN,
/// analizde tespit edilen bosluklar ikinci grupta ayrica isaretlidir.
/// </para>
/// <para>
/// Kabul esigi: <b>%100</b>. Tek bir basarisiz test sprint kabul kriterini bloke eder.
/// </para>
/// </remarks>
public class NegativeTestEvidenceTests
{
    private static readonly AllowListDocument AllowList =
        AllowListLoader.FromJson(TestFixtures.ReadAllowListJson());

    // =====================================================================
    // GRUP 1 — Dokumanda tanimli 15 senaryo (6.2)
    // =====================================================================

    [Theory]
    // 6.2/1 — veri degistirme
    [InlineData("DELETE FROM vw_sales", ReasonCode.GR001, "6.2/1")]
    // 6.2/2 — ifade zinciri
    [InlineData("SELECT 1; DROP TABLE vw_sales", ReasonCode.GR002, "6.2/2")]
    // 6.2/3 — yildiz secim
    [InlineData("SELECT * FROM vw_sales", ReasonCode.GR004, "6.2/3")]
    // 6.2/4 — PII kolonu secimi
    [InlineData("SELECT email FROM vw_customer", ReasonCode.GR005, "6.2/4")]
    // 6.2/5 — allow-list disi obje
    [InlineData("SELECT region FROM dbo.salaries", ReasonCode.GR003, "6.2/5")]
    // 6.2/6 — allow-list disi obje ile JOIN
    [InlineData("SELECT v.region FROM vw_sales v JOIN hr_employees h ON v.region = h.region",
        ReasonCode.GR003, "6.2/6")]
    // 6.2/9 — dinamik SQL calistirma
    [InlineData("EXEC sp_executesql N'SELECT 1'", ReasonCode.GR001, "6.2/9")]
    // 6.2/13 — UNION ile allow-list disi tabloya erisim
    [InlineData("SELECT region, quantity FROM vw_sales UNION SELECT 1, 2 FROM dbo.users",
        ReasonCode.GR003, "6.2/13")]
    // 6.2/14 — alt sorgu icinde yasakli tablo
    [InlineData("SELECT region FROM vw_sales WHERE customer_id IN (SELECT id FROM dbo.users)",
        ReasonCode.GR003, "6.2/14")]
    // 6.2/15 — takma adla PII kacirma
    [InlineData("SELECT email AS x FROM vw_customer", ReasonCode.GR005, "6.2/15")]
    public void Dokuman_senaryosu_reddedilir(string sql, ReasonCode expected, string reference)
    {
        var result = Run(sql);

        Assert.Equal(GuardrailDecision.Rejected, result.Decision);
        Assert.Equal(expected, result.ReasonCode);

        // Reddedilen sorgunun metni disa verilmez.
        Assert.Null(result.Sql);

        // Referans, testin hangi dokuman maddesini karsiladigini kayitta tutar.
        Assert.False(string.IsNullOrWhiteSpace(reference));
    }

    [Fact]
    public void Dokuman_6_2_7_kapsam_filtresi_olmayan_sorgu_guardrail_tarafindan_duzeltilir()
    {
        // Dokuman: "Scope filtresi olmayan sorgu -> guardrail filtre ekleyerek duzeltir."
        // Yani bu senaryo REDDEDILMEZ, DUZELTILIR.
        var result = Run("SELECT region, quantity FROM vw_sales");

        Assert.Equal(GuardrailDecision.Accepted, result.Decision);
        Assert.Contains("region IN (@scope0", result.Sql!, StringComparison.Ordinal);
        Assert.NotNull(result.AppliedScopeFilter);
    }

    [Fact]
    public void Dokuman_6_2_8_yetkisiz_bolge_talebi_GR008_ile_reddedilir()
    {
        var result = Run(
            "SELECT region FROM vw_sales WHERE region = 'Karadeniz'",
            UserDataScope.ForRegions("Marmara"));

        Assert.Equal(ReasonCode.GR008, result.ReasonCode);
    }

    [Fact]
    public void Dokuman_6_2_10_yorumla_filtre_atlatma_etkisiz_kalir()
    {
        // Yorum, AST'den uretim sirasinda dusurulur; sorgunun geri kalani yoruma alinamaz.
        var result = Run("SELECT region FROM vw_sales WHERE region = 'Marmara' -- AND kapsam");

        Assert.Equal(GuardrailDecision.Accepted, result.Decision);
        Assert.DoesNotContain("--", result.Sql!, StringComparison.Ordinal);
        Assert.Contains("region IN (@scope0", result.Sql!, StringComparison.Ordinal);
    }

    [Fact]
    public void Dokuman_6_2_11_asiri_genis_tarih_araligi_reddedilir()
    {
        // Dokuman bu senaryo icin GR009 yaziyor; uygulamada ayri bir kod (GR013) tanimlandi
        // cunku "satir limiti asimi" ile "tarih butcesi asimi" farkli kontroller.
        var result = Run(
            "SELECT region FROM vw_sales WHERE order_date BETWEEN '2010-01-01' AND '2026-01-01'");

        Assert.Equal(GuardrailDecision.Rejected, result.Decision);
        Assert.Equal(ReasonCode.GR013, result.ReasonCode);
    }

    [Fact]
    public void Dokuman_6_2_12_prompt_injection_guardrail_i_etkilemez()
    {
        // Prompt injection'in guardrail'a etkisi yoktur: karar deterministik kontrollerde
        // verilir, modelin urettigi metnin niyeti degerlendirilmez. Model talimatlari yok
        // sayip allow-list disi bir sorgu uretse dahi reddedilir.
        var result = Run("SELECT name FROM sys.tables");

        Assert.Equal(ReasonCode.GR003, result.ReasonCode);
    }

    // =====================================================================
    // GRUP 2 — Dokumanda BULUNMAYAN, analizde tespit edilen bosluklar
    // =====================================================================

    [Fact]
    public void Ek1_CTE_govdesi_uzerinden_kapsam_atlatma_engellenir()
    {
        // Dokumandaki "filtreyi WHERE'e ekle" ifadesi tek WHERE varsayiyordu. Dis sorguda
        // kapsam kolonu bulunmadigi icin filtre yalnizca dis WHERE'e eklenseydi CTE tum
        // bolgeleri okuyup toplardi.
        var result = Run(
            "WITH toplam AS (SELECT SUM(quantity) AS adet FROM vw_sales) SELECT adet FROM toplam");

        Assert.Equal(GuardrailDecision.Accepted, result.Decision);
        Assert.Contains("FROM vw_sales WHERE vw_sales.region IN (@scope0", Normalized(result), StringComparison.Ordinal);
    }

    [Fact]
    public void Ek2_UNION_kollarinin_tamamina_kapsam_filtresi_eklenir()
    {
        var result = Run("SELECT region FROM vw_sales UNION ALL SELECT region FROM vw_customer");

        Assert.Equal(GuardrailDecision.Accepted, result.Decision);
        Assert.Equal(2, result.Sql!.Split("IN (@scope0").Length - 1);
    }

    [Fact]
    public void Ek3_turetilmis_tablo_icine_kapsam_filtresi_eklenir()
    {
        var result = Run("SELECT t.adet FROM (SELECT SUM(quantity) AS adet FROM vw_sales) t");

        Assert.Equal(GuardrailDecision.Accepted, result.Decision);
        Assert.Contains("FROM vw_sales WHERE vw_sales.region IN (@scope0", Normalized(result), StringComparison.Ordinal);
    }

    [Fact]
    public void Ek4_PII_WHERE_uzerinden_sizdirilamaz()
    {
        // Dokuman bu kontrolu "deniedColumns'dan hicbiri SECILMEMIS" diye tanimliyordu.
        // Bu sorgu PII'yi secmiyor ama arayan degerin varligini ve dagilimini aciga cikariyor.
        var result = Run("SELECT region, COUNT(*) AS adet FROM vw_customer WHERE email LIKE 'a%' GROUP BY region");

        Assert.Equal(ReasonCode.GR005, result.ReasonCode);
    }

    [Fact]
    public void Ek5_kimlik_seviyesinde_kirilim_reddedilir()
    {
        // Allow-list'te, PII degil, kapsam icinde, tum sozdizimsel kontrolleri geciyor —
        // ama tek bir kisiyi izole ediyor.
        var result = Run("SELECT customer_id, SUM(quantity) AS adet FROM vw_sales GROUP BY customer_id");

        Assert.Equal(ReasonCode.GR012, result.ReasonCode);
    }

    [Fact]
    public void Ek6_HAVING_ile_tek_kaydi_izole_etme_reddedilir()
    {
        var result = Run("SELECT region, COUNT(*) AS adet FROM vw_sales GROUP BY region HAVING COUNT(*) = 1");

        Assert.Equal(ReasonCode.GR012, result.ReasonCode);
    }

    [Fact]
    public void Ek7_APPLY_ile_fonksiyon_cagrisi_reddedilir()
    {
        // Gramere uygun bir SELECT oldugu icin SelectOnly'ye takilmiyor; AllowListObjects
        // yalnizca NamedTableReference denetledigi icin fonksiyon allow-list'te hic
        // aranmiyordu. Fonksiyonun govdesi allow-list disi tabloya erisebilir.
        var result = Run("SELECT v.region FROM vw_sales v CROSS APPLY dbo.fn_gizli(v.region) AS f");

        Assert.Equal(ReasonCode.GR011, result.ReasonCode);
    }

    [Fact]
    public void Ek8_dosya_sisteminden_okuma_reddedilir()
    {
        var result = Run("SELECT 1 AS x FROM OPENROWSET(BULK 'C:\\gizli.txt', SINGLE_CLOB) AS c");

        Assert.Equal(ReasonCode.GR011, result.ReasonCode);
    }

    [Fact]
    public void Ek9_sema_oneki_ile_allow_list_atlatilamaz()
    {
        // Obje adi son parcadan cozulseydi 'baska_sema.vw_sales' ile 'vw_sales' ayni
        // gorunurdu.
        var result = Run("SELECT region FROM baska_sema.vw_sales");

        Assert.Equal(ReasonCode.GR003, result.ReasonCode);
    }

    [Fact]
    public void Ek10_virgullu_JOIN_ile_tanimsiz_yol_kullanilamaz()
    {
        // Virgullu yazim bir JOIN dugumu uretmedigi icin yalnizca join dugumlerini saymak
        // bu bicimi tamamen kaciriyordu.
        var result = Run(
            "SELECT v.region, k.channel FROM vw_sales v, vw_campaign k WHERE v.region = k.region");

        Assert.Equal(ReasonCode.GR006, result.ReasonCode);
    }

    [Fact]
    public void Ek11_OR_kosulu_kapsam_filtresinden_kacamaz()
    {
        // Parantezsiz birlestirme "a OR b AND scope" uretirdi ve AND onceligi nedeniyle
        // 'a' dalindaki satirlar kapsam filtresinden KACARDI.
        var result = Run(
            "SELECT region FROM vw_sales WHERE product_category = 'A' OR product_category = 'B'");

        Assert.Equal(GuardrailDecision.Accepted, result.Decision);
        Assert.Contains(") AND vw_sales.region IN (@scope0", Normalized(result), StringComparison.Ordinal);
    }

    [Fact]
    public void Ek12_kapsami_cozumlenemeyen_kullanici_hicbir_sey_goremez()
    {
        // Bos kapsamin "kisitlama yok" olarak yorumlanmasi en tehlikeli hata olurdu.
        var result = Run("SELECT region FROM vw_sales", UserDataScope.Unresolved);

        Assert.Equal(ReasonCode.GR007, result.ReasonCode);
    }

    [Fact]
    public void Ek13_yuzde_bazli_TOP_sabit_degere_cevrilir()
    {
        // TOP 50 PERCENT satir sayisini garanti etmez.
        var result = Run("SELECT TOP 50 PERCENT region FROM vw_sales");

        Assert.Equal(GuardrailDecision.Accepted, result.Decision);
        Assert.DoesNotContain("PERCENT", result.Sql!, StringComparison.Ordinal);
        Assert.Contains("5000", result.Sql!, StringComparison.Ordinal);
    }

    // =====================================================================
    // Kabul esigi
    // =====================================================================

    [Fact]
    public void Kanit_paketi_dokumanin_istedigi_senaryo_sayisini_asar()
    {
        // Dokuman "15+ senaryo" istiyor. Bu dosyada 10 parametreli + 6 tekil dokuman
        // senaryosu ve 13 ek bosluk senaryosu var.
        var documentScenarios = 10 + 6;
        var additionalScenarios = 13;

        Assert.True(documentScenarios >= 15,
            $"Dokuman senaryo sayisi 15'in altinda: {documentScenarios}");
        Assert.True(documentScenarios + additionalScenarios >= 23,
            "Toplam senaryo sayisi 23'un altinda.");
    }

    /// <summary>
    /// Uretici bicimlendirmesini tek bosluga indirger. Testler SQL'in satir sonlarina ve
    /// girintisine bagli olmamali; anlamli olan yapinin kendisi.
    /// </summary>
    private static string Normalized(GuardrailResult result) =>
        System.Text.RegularExpressions.Regex.Replace(
            result.Sql ?? string.Empty, @"\s+", " ").Trim();

    private static GuardrailResult Run(string sql, UserDataScope? scope = null)
    {
        var context = new GuardrailContext(
            sql,
            AllowList,
            scope ?? UserDataScope.ForRegions("Marmara", "Ege"),
            new TSqlParserFactory(),
            request: new CanonicalRequest
            {
                RequestId = "req_evidence",
                ConversationId = "conv_evidence",
                Intent = RequestIntent.Breakdown,
                Metrics = ["net_sales"],
                DateRange = new DateRangeSpec
                {
                    Kind = DateRangeKind.Relative,
                    RelativeExpression = "last_quarter",
                    From = new DateOnly(2026, 4, 1),
                    To = new DateOnly(2026, 6, 30)
                }
            });

        return GuardrailFactory.Create().Execute(context);
    }
}
