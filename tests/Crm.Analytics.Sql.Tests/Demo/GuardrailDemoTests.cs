using Crm.Analytics.Sql.Audit;
using Crm.Analytics.Sql.Catalog;
using Crm.Analytics.Sql.Contracts;
using Crm.Analytics.Sql.Guardrail;
using Crm.Analytics.Sql.Parsing;
using Crm.Analytics.Sql.QueryBuilder;
using Crm.Analytics.Sql.Routing;
using Crm.Analytics.Sql.Service;
using Xunit.Abstractions;

namespace Crm.Analytics.Sql.Tests.Demo;

/// <summary>
/// Sprint Gun 11 canli gosterimi: bir gecerli talep, bir belirsiz talep, bir reddedilen talep.
/// </summary>
/// <remarks>
/// <para>
/// Ayri bir konsol uygulamasi yerine test olarak yazilmasi bilincli: demo boylece <b>her CI
/// kosusunda dogrulanir</b>. Slayttaki cikti ile gercek davranisin ayrisma ihtimali kalmaz —
/// demo bozulursa test kirmizi doner.
/// </para>
/// <para>
/// Calistirma: <c>dotnet test --filter "FullyQualifiedName~Demo" --logger "console;verbosity=detailed"</c>
/// </para>
/// </remarks>
public class GuardrailDemoTests(ITestOutputHelper output)
{
    private static readonly DateOnly Today = new(2026, 7, 30);

    private static readonly AllowListDocument AllowList =
        AllowListLoader.FromJson(ContractResources.ReadOlistAllowList());

    private static readonly MetricCatalogDocument Catalog =
        MetricCatalogLoader.FromJson(ContractResources.ReadOlistMetricCatalog());

    // --- 1. Gecerli talep -----------------------------------------------------

    [Fact]
    public void Senaryo_1_gecerli_talep_calistirilabilir_SQL_uretir()
    {
        Header("SENARYO 1 — GECERLI TALEP");

        const string prompt = "2018 satış tutarını eyalete göre göster";
        var response = Produce(prompt, UserDataScope.ForRegions("SP", "RJ"));

        output.WriteLine($"Talep      : {prompt}");
        output.WriteLine($"Kullanici  : SP ve RJ eyaletlerini gorebiliyor");
        output.WriteLine($"Karar      : {response.Decision}");
        output.WriteLine($"Yol        : {response.Path}");
        output.WriteLine($"Gorsel     : {response.ResultShape!.SuggestedVisual} — {response.ResultShape.Rationale}");
        output.WriteLine($"Timeout    : {response.CommandTimeoutSeconds} sn");
        output.WriteLine($"Dogrulanan kontrol sayisi: {response.Checks.Count(c => c.Outcome == CheckOutcome.Passed)}/16");
        output.WriteLine("");
        output.WriteLine("Uretilen SQL:");
        output.WriteLine(response.Sql!);
        output.WriteLine("");
        output.WriteLine($"ZORLA EKLENEN KAPSAM FILTRESI: {response.AppliedScopeFilter}");
        output.WriteLine("");
        output.WriteLine("Parametreler (DEGERLER audit'e YAZILMAZ):");

        foreach (var parameter in response.Parameters)
        {
            output.WriteLine($"  {parameter.Name} : {parameter.Kind} = {parameter.Raw}");
        }

        Assert.Equal(GuardrailDecision.Accepted, response.Decision);
        Assert.False(string.IsNullOrWhiteSpace(response.AppliedScopeFilter));
        Assert.DoesNotContain("2018-01-01", response.Sql!, StringComparison.Ordinal);
    }

    [Fact]
    public void Senaryo_1b_ayni_talep_farkli_yetkide_farkli_SQL_uretir()
    {
        Header("SENARYO 1b — KAPSAM FILTRESI KULLANICIYA GORE DEGISIR");

        const string prompt = "2018 satış tutarını eyalete göre göster";

        var restricted = Produce(prompt, UserDataScope.ForRegions("SP"));
        var wider = Produce(prompt, UserDataScope.ForRegions("SP", "RJ", "MG"));

        output.WriteLine($"Talep metni IKISINDE DE ayni: {prompt}");
        output.WriteLine("");
        output.WriteLine($"Kullanici A (SP)         : {restricted.AppliedScopeFilter}");
        output.WriteLine($"Kullanici B (SP, RJ, MG) : {wider.AppliedScopeFilter}");
        output.WriteLine("");
        output.WriteLine("Filtre kullanicinin metninden DEGIL, yetkisinden gelir.");

        Assert.NotEqual(restricted.AppliedScopeFilter, wider.AppliedScopeFilter);
        Assert.Single(restricted.Parameters, p => p.Name.StartsWith("@scope", StringComparison.Ordinal));
        Assert.Equal(3, wider.Parameters.Count(p => p.Name.StartsWith("@scope", StringComparison.Ordinal)));
    }

    // --- 2. Belirsiz talep ----------------------------------------------------

    [Fact]
    public void Senaryo_2_belirsiz_talep_netlestirmeye_gider()
    {
        Header("SENARYO 2 — BELIRSIZ TALEP");

        foreach (var prompt in new[]
        {
            "bana bir şeyler göster",
            "2018 kâr marjı nedir",
            "satış tutarını eyalete göre göster"
        })
        {
            var response = Produce(prompt, UserDataScope.ForRegions("SP", "RJ"));

            output.WriteLine($"Talep    : {prompt}");
            output.WriteLine($"Karar    : {response.Decision} / {response.ReasonCode}");
            output.WriteLine($"Kullanici mesaji: {response.UserMessage}");

            if (response.UnresolvedTerms.Count > 0)
            {
                output.WriteLine($"Cozumlenemeyen  : {string.Join(", ", response.UnresolvedTerms)}");
            }

            output.WriteLine($"SQL uretildi mi : {(response.Sql is null ? "HAYIR" : "EVET")}");
            output.WriteLine("");

            Assert.Equal(GuardrailDecision.NeedsClarification, response.Decision);
            Assert.Null(response.Sql);
        }

        output.WriteLine("Ucuncu talep tarih araligi icermiyor: aralik olmadan sorgu tum donemi");
        output.WriteLine("tarar ve tarih butcesi denetlenemez. Varsayilan aralik UYDURULMAZ.");
    }

    // --- 3. Reddedilen talepler -----------------------------------------------

    [Fact]
    public void Senaryo_3_gun_sonu_kontrolu_uc_ret()
    {
        Header("SENARYO 3 — SPRINT GUN 4 SONU KONTROLU");
        output.WriteLine("Doküman sorusu: DELETE, yetkisiz tablo ve kapsam disi bolge talepleri");
        output.WriteLine("reddediliyor mu? Asagidaki SQL'ler dil modelinin uretmis oldugu");
        output.WriteLine("varsayilan taslaklardir; her biri ayni guardrail hattindan gecer.");
        output.WriteLine("");

        (string Label, string Sql, ReasonCode Expected)[] scenarios =
        [
            ("Veri degistirme", "DELETE FROM vw_sales", ReasonCode.GR001),
            ("Yetkisiz tablo", "SELECT customer_state FROM dbo.hr_employees", ReasonCode.GR003),
            // Dokumandaki ucuncu gun-sonu kontrolu: kullanici SP ve RJ gorebiliyor,
            // BA ve CE talep ediyor.
            ("Kapsam disi bolge talebi",
                "SELECT customer_state FROM mart.vw_sales WHERE customer_state IN ('SP', 'BA', 'CE')",
                ReasonCode.GR008),
            ("Ifade zinciri", "SELECT customer_state FROM vw_sales; DROP TABLE vw_sales", ReasonCode.GR002),
            ("Yildiz secim", "SELECT * FROM vw_sales", ReasonCode.GR004),
            ("Dinamik SQL", "EXEC sp_executesql N'SELECT 1'", ReasonCode.GR001),
            ("Kimlik seviyesinde detay",
                "SELECT customer_unique_id, SUM(total_price) AS t FROM mart.vw_customer_rfm GROUP BY customer_unique_id",
                ReasonCode.GR012)
        ];

        foreach (var (label, sql, expected) in scenarios)
        {
            var result = RunGuardrail(sql, UserDataScope.ForRegions("SP"));
            var failed = result.Checks.FirstOrDefault(check => check.Outcome == CheckOutcome.Failed);

            output.WriteLine($"{label,-26} -> {result.Decision} / {result.ReasonCode}");
            output.WriteLine($"{"",-26}    takilan kontrol: {failed?.Name.ToString() ?? "(yok)"}");
            output.WriteLine($"{"",-26}    kullaniciya    : {result.ReasonMessage}");
            output.WriteLine("");

            Assert.Equal(GuardrailDecision.Rejected, result.Decision);
            Assert.Equal(expected, result.ReasonCode);
            Assert.Null(result.Sql);
        }
    }

    [Fact]
    public void Senaryo_3c_atlatma_denemeleri_reddedilmez_etkisiz_kalir()
    {
        Header("SENARYO 3c — ATLATMA DENEMELERI ETKISIZ KALIR");
        output.WriteLine("Bu SQL'ler reddedilmez: guardrail onlari DUZELTIR ve calistirilabilir");
        output.WriteLine("hale getirir. Onemli olan atlatmanin ise yaramamasi.");
        output.WriteLine("");

        (string Label, string Sql, string Note)[] scenarios =
        [
            ("Filtreyi yoruma alma",
                "SELECT customer_state FROM mart.vw_sales WHERE customer_state = 'SP' -- AND kapsam",
                "Yorum AST'de yok; sorgunun geri kalani yoruma alinamaz."),
            // Yetkili oldugu bolgeleri yaziyor: talep gecerli, ama guardrail yine kendi
            // filtresini ekler. Kullanicinin filtresi "kapsam uygulandi" saydirmaz.
            ("Kendi kapsam filtresini yazma",
                "SELECT customer_state FROM mart.vw_sales WHERE customer_state IN ('SP', 'RJ')",
                "Kullanicinin filtresi kendi filtresini GECERLI KILMAZ; guardrail kendi filtresini ayrica ekler."),
            ("Yuzde bazli TOP",
                "SELECT TOP 50 PERCENT customer_state FROM mart.vw_sales",
                "TOP 50 PERCENT satir sayisini garanti etmez; sabit degere cevrilir."),
            ("Alt sorgu ve UNION",
                "SELECT customer_state FROM mart.vw_sales UNION SELECT customer_state FROM mart.vw_sales",
                "Kapsam filtresi UNION'in HER KOLUNA ayri ayri enjekte edilir.")
        ];

        foreach (var (label, sql, note) in scenarios)
        {
            var result = RunGuardrail(sql, UserDataScope.ForRegions("SP", "RJ"));

            output.WriteLine($"{label}");
            output.WriteLine($"  Girdi  : {sql}");
            output.WriteLine($"  Karar  : {result.Decision}");
            // Tam nitelendirme gerekli: test projesinde "Environment" adli bir ad alani var.
            output.WriteLine($"  Cikti  : {result.Sql?.Replace(System.Environment.NewLine, " ")}");
            output.WriteLine($"  Neden  : {note}");
            output.WriteLine("");

            Assert.Equal(GuardrailDecision.Accepted, result.Decision);
            Assert.DoesNotContain("--", result.Sql!, StringComparison.Ordinal);
            Assert.DoesNotContain("PERCENT", result.Sql!, StringComparison.Ordinal);
            Assert.Contains("@scope0", result.Sql!, StringComparison.Ordinal);
        }

        // UNION'in her iki kolunda ayri kapsam filtresi bulunmali: tek kola eklemek
        // digerinden filtrelenmemis satir dondururdu.
        var union = RunGuardrail(
            "SELECT customer_state FROM mart.vw_sales UNION SELECT customer_state FROM mart.vw_sales",
            UserDataScope.ForRegions("SP"));

        var occurrences = union.Sql!.Split("IN (@scope0").Length - 1;
        output.WriteLine($"UNION'daki kapsam filtresi sayisi: {occurrences} (her kola bir tane)");

        Assert.Equal(2, occurrences);
    }

    [Fact]
    public void Senaryo_3b_kapsam_cozumlenemezse_sorgu_calismaz()
    {
        Header("SENARYO 3b — FAIL-CLOSED");

        var response = Produce(
            "2018 satış tutarını eyalete göre göster", UserDataScope.Unresolved);

        output.WriteLine("Kullanicinin veri kapsami cozumlenemedi (ornek: yetki servisi cevap vermedi).");
        output.WriteLine($"Karar : {response.Decision} / {response.ReasonCode}");
        output.WriteLine($"Mesaj : {response.UserMessage}");
        output.WriteLine("");
        output.WriteLine("Cozumlenemeyen kapsam SINIRSIZ sayilmaz. Belirsizlik durumunda sistem");
        output.WriteLine("veri gostermeyi degil GOSTERMEMEYI secer.");

        Assert.Equal(GuardrailDecision.Rejected, response.Decision);
        Assert.Equal(ReasonCode.GR007, response.ReasonCode);
    }

    // --- 4. Takip sorusu ------------------------------------------------------

    [Fact]
    public void Senaryo_4_takip_sorusu_onceki_talebi_revize_eder()
    {
        Header("SENARYO 4 — TAKIP SORUSU");

        var scope = UserDataScope.ForRegions("SP", "RJ");
        var first = Produce("2018 satış tutarını eyalete göre göster", scope);

        output.WriteLine("1. Talep : 2018 satış tutarını eyalete göre göster");
        output.WriteLine($"   Metrik: {string.Join(", ", first.CanonicalRequest!.Metrics)}");
        output.WriteLine($"   Kirilim: {string.Join(", ", first.CanonicalRequest.Dimensions)}");
        output.WriteLine($"   Tarih : {first.CanonicalRequest.DateRange.From} .. {first.CanonicalRequest.DateRange.To}");
        output.WriteLine("");

        var second = Produce("kategoriye göre", scope, first.CanonicalRequest);

        output.WriteLine("2. Talep : kategoriye göre       <- yalnizca kirilim degisti");
        output.WriteLine($"   Metrik: {string.Join(", ", second.CanonicalRequest!.Metrics)}   (korundu)");
        output.WriteLine($"   Kirilim: {string.Join(", ", second.CanonicalRequest.Dimensions)}   (degisti)");
        output.WriteLine($"   Tarih : {second.CanonicalRequest.DateRange.From} .. {second.CanonicalRequest.DateRange.To}   (korundu)");
        output.WriteLine($"   Zincir: {second.CanonicalRequest.PreviousRequestId} -> {second.CanonicalRequest.RequestId}");

        Assert.Equal(first.CanonicalRequest.Metrics, second.CanonicalRequest.Metrics);
        Assert.Equal(["product_category"], second.CanonicalRequest.Dimensions);
        Assert.Equal(first.CanonicalRequest.RequestId, second.CanonicalRequest.PreviousRequestId);
    }

    // --- 5. Audit -------------------------------------------------------------

    [Fact]
    public void Senaryo_5_her_karar_audit_e_yazilir()
    {
        Header("SENARYO 5 — DENETIM IZI");

        var audit = new RecordingAuditWriter();
        var service = SqlProductionFactory.CreateForOlist(audit);
        var scope = UserDataScope.ForRegions("SP");

        foreach (var prompt in new[] { "2018 satış tutarını eyalete göre göster", "bana bir şeyler göster" })
        {
            service.Produce(new SqlProductionRequest
            {
                Prompt = prompt,
                RequestId = $"req_demo_{prompt.Length}",
                ConversationId = "conv_demo",
                Scope = scope,
                Today = Today,
                UserId = "user_42"
            });
        }

        foreach (var record in audit.Records)
        {
            output.WriteLine($"requestId : {record.RequestId}");
            output.WriteLine($"  kapsam  : {record.EffectiveScope}");
            output.WriteLine($"  karar   : {record.Decision} / {record.ReasonCode}");
            output.WriteLine($"  yol     : {record.Path}");
            output.WriteLine($"  dogrulanan kontrol : {record.VerifiedCheckCount}");
            output.WriteLine($"  takilan kontrol    : {record.FailedCheck?.ToString() ?? "(yok)"}");
            output.WriteLine($"  parametre ADLARI   : {string.Join(", ", record.ParameterNames)}");
            output.WriteLine($"  parser surumu      : {record.ParserVersion ?? "(guardrail kosmadi)"}");
            output.WriteLine("");
        }

        output.WriteLine("Parametre DEGERLERI kayitta YOK: filtre degerleri kullanici verisidir.");
        output.WriteLine("Guardrail'in PII kontrollerini bir yandan uygularken ayni veriyi diger");
        output.WriteLine("yandan log altyapisina sizdirmak celiskili olurdu.");

        Assert.Equal(2, audit.Records.Count);
        Assert.All(audit.Records, record => Assert.NotEqual("(bilinmiyor)", record.RequestId));
    }

    // --- yardimcilar ---------------------------------------------------------

    private void Header(string title)
    {
        output.WriteLine(new string('=', 74));
        output.WriteLine(title);
        output.WriteLine(new string('=', 74));
        output.WriteLine("");
    }

    private static SqlProductionResponse Produce(
        string prompt,
        UserDataScope scope,
        CanonicalRequest? previous = null) =>
        SqlProductionFactory.CreateForOlist(new RecordingAuditWriter()).Produce(new SqlProductionRequest
        {
            Prompt = prompt,
            RequestId = $"req_{prompt.Length:D3}",
            ConversationId = "conv_demo",
            Scope = scope,
            Today = Today,
            PreviousRequest = previous,
            UserId = "user_42"
        });

    /// <summary>
    /// Hazir SQL'i dogrudan guardrail'a verir: dil modelinin urettigi taslagi temsil eder.
    /// </summary>
    private static GuardrailResult RunGuardrail(string sql, UserDataScope scope)
    {
        var context = new GuardrailContext(
            sql,
            AllowList,
            scope,
            new TSqlParserFactory(),
            request: new CanonicalRequest
            {
                RequestId = "req_demo_guardrail",
                ConversationId = "conv_demo",
                Intent = RequestIntent.Breakdown,
                Metrics = ["item_sales"],
                DateRange = new DateRangeSpec
                {
                    Kind = DateRangeKind.Absolute,
                    From = new DateOnly(2018, 1, 1),
                    To = new DateOnly(2018, 12, 31)
                }
            });

        return GuardrailFactory.Create().Execute(context);
    }

    private sealed class RecordingAuditWriter : IDecisionAuditWriter
    {
        public List<DecisionAuditRecord> Records { get; } = [];

        public void Write(DecisionAuditRecord record) => Records.Add(record);
    }
}
