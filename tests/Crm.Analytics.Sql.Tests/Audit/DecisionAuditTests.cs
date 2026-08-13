using Crm.Analytics.Sql.Audit;
using Crm.Analytics.Sql.Catalog;
using Crm.Analytics.Sql.Contracts;
using Crm.Analytics.Sql.Guardrail;
using Crm.Analytics.Sql.Parsing;
using Crm.Analytics.Sql.Tests.Fixtures;

namespace Crm.Analytics.Sql.Tests.Audit;

public class DecisionAuditTests
{
    private static readonly AllowListDocument AllowList =
        AllowListLoader.FromJson(TestFixtures.ReadAllowListJson());

    [Fact]
    public void Kabul_karari_denetim_kaydina_yazilir()
    {
        var (context, result) = Execute("SELECT region, quantity FROM vw_sales");

        var record = DecisionAuditRecordFactory.From(
            context, result, ProductionPath.QueryBuilder, userId: "u-42", rawPrompt: "bolgeye gore satis");

        Assert.Equal(GuardrailDecision.Accepted, record.Decision);
        var serialized = System.Text.Json.JsonSerializer.Serialize(record);
        Assert.DoesNotContain("u-42", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("bolgeye gore satis", serialized, StringComparison.Ordinal);
        Assert.Equal("region:count=2", record.EffectiveScope);
        Assert.Equal("Sql150", record.ParserVersion);
        Assert.Equal(ProductionPath.QueryBuilder, record.Path);
        Assert.NotNull(record.AppliedScopeFilter);
        Assert.Null(record.FailedCheck);
    }

    [Fact]
    public void Parametre_DEGERLERI_denetim_kaydina_YAZILMAZ()
    {
        // Filtre degerleri kullanici verisidir. Guardrail bir yandan PII kontrolu uygularken
        // ayni veriyi log altyapisina sizdirmamali.
        var (context, result) = Execute("SELECT region FROM vw_sales WHERE product_category = 'gizli-deger'");

        var record = DecisionAuditRecordFactory.From(context, result, ProductionPath.QueryBuilder);

        Assert.NotEmpty(record.ParameterNames);
        Assert.All(record.ParameterNames, name => Assert.StartsWith("@", name, StringComparison.Ordinal));

        // Kayittaki hicbir alan degeri tasimamali.
        var serialized = System.Text.Json.JsonSerializer.Serialize(record);
        Assert.DoesNotContain("gizli-deger", serialized, StringComparison.Ordinal);
    }

    [Fact]
    public void Ret_karari_gerekce_ve_basarisiz_kontrolle_kaydedilir()
    {
        var (context, result) = Execute("DELETE FROM vw_sales");

        var record = DecisionAuditRecordFactory.From(context, result, ProductionPath.QueryBuilder);

        Assert.Equal(GuardrailDecision.Rejected, record.Decision);
        Assert.Equal(ReasonCode.GR001, record.ReasonCode);
        Assert.Equal(GuardrailCheckName.SelectOnly, record.FailedCheck);

        // SQL ve ham girdi audit contract'inda alan olarak hic yer almaz.
        Assert.Null(typeof(DecisionAuditRecord).GetProperty("Sql"));
        Assert.Null(typeof(DecisionAuditRecord).GetProperty("RawPrompt"));
        Assert.Null(typeof(DecisionAuditRecord).GetProperty("UserId"));
    }

    [Fact]
    public void Atlanan_kontroller_dogrulanmis_sayilmaz()
    {
        // "Kontrol calismadi" ile "kontrol gecti" ayrimi audit'te korunmali; aksi halde
        // guvenlik kaniti yaniltici olur.
        var (context, result) = Execute("DELETE FROM vw_sales");

        var record = DecisionAuditRecordFactory.From(context, result, ProductionPath.QueryBuilder);

        var skipped = record.Checks.Count(check => check.Outcome == CheckOutcome.Skipped);

        Assert.True(skipped > 0);
        Assert.Equal(
            record.Checks.Count(check => check.Outcome == CheckOutcome.Passed),
            record.VerifiedCheckCount);
        Assert.True(record.VerifiedCheckCount < record.Checks.Count);
    }

    [Fact]
    public void Cozumlenemeyen_kapsam_kayitta_acikca_gorunur()
    {
        var (context, result) = Execute("SELECT region FROM vw_sales", UserDataScope.Unresolved);

        var record = DecisionAuditRecordFactory.From(context, result, ProductionPath.QueryBuilder);

        Assert.Equal("COZUMLENEMEDI", record.EffectiveScope);
        Assert.Equal(ReasonCode.GR007, record.ReasonCode);
    }

    [Fact]
    public void Sinirsiz_kapsam_kayitta_acikca_gorunur()
    {
        var (context, result) = Execute("SELECT region FROM vw_sales", UserDataScope.Unrestricted);

        var record = DecisionAuditRecordFactory.From(context, result, ProductionPath.QueryBuilder);

        Assert.Equal("SINIRSIZ", record.EffectiveScope);
    }

    // --- GR008 ---------------------------------------------------------------

    [Fact]
    public void Kapsam_disi_bolge_talebi_artik_GR008_ile_reddedilir()
    {
        // Onceki davranis: filtre enjekte ediliyor, sonuc mantiksal olarak bos donuyordu ve
        // kullaniciya "veri bulunamadi" gibi gorunuyordu. Artik yetkisiz erisim denemesi
        // kendi koduyla reddediliyor ve audit'te gorunuyor.
        var (_, result) = Execute(
            "SELECT region FROM vw_sales WHERE region = 'Karadeniz'",
            UserDataScope.ForRegions("Marmara"));

        Assert.Equal(GuardrailDecision.Rejected, result.Decision);
        Assert.Equal(ReasonCode.GR008, result.ReasonCode);
    }

    [Fact]
    public void Kapsam_icindeki_bolge_talebi_kabul_edilir()
    {
        var (_, result) = Execute(
            "SELECT region FROM vw_sales WHERE region = 'Marmara'",
            UserDataScope.ForRegions("Marmara", "Ege"));

        Assert.Equal(GuardrailDecision.Accepted, result.Decision);
    }

    [Fact]
    public void IN_listesinde_bir_deger_kapsam_disiysa_reddedilir()
    {
        var (_, result) = Execute(
            "SELECT region FROM vw_sales WHERE region IN ('Marmara', 'Karadeniz')",
            UserDataScope.ForRegions("Marmara", "Ege"));

        Assert.Equal(GuardrailDecision.Rejected, result.Decision);
        Assert.Equal(ReasonCode.GR008, result.ReasonCode);
    }

    [Fact]
    public void Kapsam_disi_degeri_DISLAYAN_kosul_reddedilmez()
    {
        // 'region <> X' ve 'NOT IN' bir kapsam TALEBI degil, dislama ifadesidir.
        var (_, result) = Execute(
            "SELECT region FROM vw_sales WHERE region <> 'Karadeniz'",
            UserDataScope.ForRegions("Marmara"));

        Assert.Equal(GuardrailDecision.Accepted, result.Decision);
    }

    [Fact]
    public void Sinirsiz_yetkili_kullanici_her_bolgeyi_talep_edebilir()
    {
        var (_, result) = Execute(
            "SELECT region FROM vw_sales WHERE region = 'Karadeniz'",
            UserDataScope.Unrestricted);

        Assert.Equal(GuardrailDecision.Accepted, result.Decision);
    }

    private static (GuardrailContext Context, GuardrailResult Result) Execute(
        string sql,
        UserDataScope? scope = null)
    {
        var request = new CanonicalRequest
        {
            RequestId = "req_1",
            ConversationId = "conv_1",
            PreviousRequestId = null,
            Intent = RequestIntent.Breakdown,
            Metrics = ["net_sales"],
            DateRange = new DateRangeSpec { Kind = DateRangeKind.Relative, RelativeExpression = "last_quarter" }
        };

        var context = new GuardrailContext(
            sql,
            AllowList,
            scope ?? UserDataScope.ForRegions("Marmara", "Ege"),
            new TSqlParserFactory(),
            request: request);

        return (context, GuardrailFactory.Create().Execute(context));
    }
}
