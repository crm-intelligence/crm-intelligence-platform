using Crm.Analytics.Sql.Audit;
using Crm.Analytics.Sql.Catalog;
using Crm.Analytics.Sql.Contracts;
using Crm.Analytics.Sql.Nlu;
using Crm.Analytics.Sql.Parsing;
using Crm.Analytics.Sql.QueryBuilder;
using Crm.Analytics.Sql.Routing;
using Crm.Analytics.Sql.Tests.Fixtures;

namespace Crm.Analytics.Sql.Tests.Nlu;

public class AmbiguityGateTests
{
    private static readonly MetricCatalogDocument Catalog =
        MetricCatalogLoader.FromJson(ContractResources.ReadOlistMetricCatalog());

    private static readonly AllowListDocument AllowList =
        AllowListLoader.FromJson(ContractResources.ReadOlistAllowList());

    [Fact]
    public void Net_talep_gecer()
    {
        Assert.False(new AmbiguityGate().Evaluate(Request()).NeedsClarification);
    }

    [Fact]
    public void Cozumlenemeyen_terim_ve_dusuk_kapsama_birlikte_netlestirme_uretir()
    {
        var verdict = new AmbiguityGate().Evaluate(
            Request(confidence: 0.4, unresolved: ["zamazingo"]));

        Assert.Equal(ReasonCode.CL001, verdict.ReasonCode);
    }

    [Fact]
    public void Cozumlenemeyen_business_terimi_yuksek_confidence_ile_gizlenemez()
    {
        // Unsupported metric substitution guvenli bicimde netlestirmeye gitmelidir.
        var verdict = new AmbiguityGate().Evaluate(
            Request(confidence: 0.9, unresolved: ["zamazingo"]));

        Assert.True(verdict.NeedsClarification);
    }

    [Fact]
    public void Dusuk_kapsama_tek_basina_talebi_bloke_etmez()
    {
        var verdict = new AmbiguityGate().Evaluate(Request(confidence: 0.2, unresolved: []));

        Assert.False(verdict.NeedsClarification);
    }

    [Fact]
    public void Olcum_ve_kirilim_yoksa_netlestirme_istenir()
    {
        var verdict = new AmbiguityGate().Evaluate(Request(metrics: [], dimensions: []));

        Assert.Equal(ReasonCode.CL001, verdict.ReasonCode);
    }

    [Fact]
    public void Celisen_tarih_araligi_yakalanir()
    {
        // From > To her zaman bos sonuc dondururdu; kullaniciya "veri yok" demek yerine
        // araligin kendisini sormak dogrudur.
        var verdict = new AmbiguityGate().Evaluate(Request(
            from: new DateOnly(2018, 6, 30),
            to: new DateOnly(2018, 1, 1)));

        Assert.Equal(ReasonCode.CL002, verdict.ReasonCode);
    }

    [Fact]
    public void Confidence_esigi_unresolved_business_terimini_gecirmez()
    {
        var request = Request(confidence: 0.7, unresolved: ["zamazingo"]);

        Assert.True(new AmbiguityGate(0.6).Evaluate(request).NeedsClarification);
        Assert.True(new AmbiguityGate(0.8).Evaluate(request).NeedsClarification);
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(1.1)]
    public void Gecersiz_esik_reddedilir(double threshold)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new AmbiguityGate(threshold));
    }

    [Fact]
    public void Gerekce_ic_teshis_notu_tasir()
    {
        var verdict = new AmbiguityGate().Evaluate(
            Request(confidence: 0.3, unresolved: ["zamazingo"]));

        Assert.Contains("zamazingo", verdict.Detail!, StringComparison.Ordinal);
    }

    // --- kapinin atlanamazligi ------------------------------------------------

    [Fact]
    public void Belirsiz_talep_SQL_uretilmeden_netlestirmeye_gider()
    {
        // Kapi Router'in ICINDE, SQL uretiminden once kosar. Cagiranin kapiyi atlayabilecegi
        // bir yol yok: Produce tek giris noktasi.
        var (result, _) = Route(Request(confidence: 0.3, unresolved: ["zamazingo"]));

        Assert.Equal(GuardrailDecision.NeedsClarification, result.Decision);
        Assert.Equal(ReasonCode.CL001, result.ReasonCode);
        Assert.Null(result.Sql);
    }

    [Fact]
    public void Netlestirme_karari_da_audit_e_yazilir()
    {
        var (_, audit) = Route(Request(confidence: 0.3, unresolved: ["zamazingo"]));

        var record = Assert.Single(audit.Records);
        Assert.Equal(GuardrailDecision.NeedsClarification, record.Decision);
        Assert.Equal("req_gate", record.RequestId);
    }

    [Fact]
    public void Net_talep_kapiyi_gecer_ve_kapsam_filtresi_uygulanir()
    {
        var (result, _) = Route(Request());

        Assert.Equal(GuardrailDecision.Accepted, result.Decision);
        Assert.False(string.IsNullOrWhiteSpace(result.AppliedScopeFilter));
    }

    [Fact]
    public void Ayristiricidan_gelen_talep_uctan_uca_calisir()
    {
        // Serbest metin -> Canonical Request -> guardrail onayli SQL. Zincirin tamami.
        var outcome = new CatalogTermRequestParser(Catalog).Parse(new RequestParseInput(
            "Bu yıl satış tutarını eyalete göre göster", "req_e2e", "conv_e2e", new DateOnly(2026, 7, 30)));

        Assert.True(outcome.IsSuccessful, outcome.Detail);

        var (result, _) = Route(outcome.Request!);

        Assert.Equal(GuardrailDecision.Accepted, result.Decision);
        Assert.Contains("customer_state", result.Sql!, StringComparison.Ordinal);
        Assert.DoesNotContain("2026-01-01", result.Sql!, StringComparison.Ordinal);
    }

    // --- yardimcilar ---------------------------------------------------------

    private static (GuardrailResult Result, RecordingAuditWriter Audit) Route(CanonicalRequest request)
    {
        var audit = new RecordingAuditWriter();

        var router = new SqlProductionRouter(
            new DeterministicQueryBuilder(new TSqlParserFactory(), Catalog, AllowList),
            AllowList,
            new TSqlParserFactory(),
            audit);

        var routing = router.Produce(request, UserDataScope.ForRegions("SP", "RJ"));

        return (routing.Guardrail, audit);
    }

    private static CanonicalRequest Request(
        IReadOnlyList<string>? metrics = null,
        IReadOnlyList<string>? dimensions = null,
        double confidence = 0.95,
        IReadOnlyList<string>? unresolved = null,
        DateOnly? from = null,
        DateOnly? to = null) =>
        new()
        {
            RequestId = "req_gate",
            ConversationId = "conv_gate",
            Intent = RequestIntent.Breakdown,
            Metrics = metrics ?? ["item_sales"],
            Dimensions = dimensions ?? ["customer_state"],
            DateRange = new DateRangeSpec
            {
                Kind = DateRangeKind.Absolute,
                From = from ?? new DateOnly(2018, 1, 1),
                To = to ?? new DateOnly(2018, 3, 31)
            },
            Confidence = confidence,
            UnresolvedTerms = unresolved ?? []
        };

    private sealed class RecordingAuditWriter : IDecisionAuditWriter
    {
        public List<DecisionAuditRecord> Records { get; } = [];

        public void Write(DecisionAuditRecord record) => Records.Add(record);
    }
}
