using Crm.Analytics.Sql.Catalog;
using Crm.Analytics.Sql.Contracts;
using Crm.Analytics.Sql.Guardrail;
using Crm.Analytics.Sql.Parsing;
using Crm.Analytics.Sql.Tests.Fixtures;

namespace Crm.Analytics.Sql.Tests.Guardrail;

public class GuardrailPipelineTests
{
    private static GuardrailContext CreateContext(string sql = "SELECT region FROM vw_sales") =>
        new(
            sql,
            AllowListLoader.FromJson(TestFixtures.ReadAllowListJson()),
            UserDataScope.ForRegions("Marmara"),
            new TSqlParserFactory());

    [Fact]
    public void Tum_kontroller_gecerse_ve_kapsam_kaydedilirse_kabul_edilir()
    {
        var context = CreateContext();
        var pipeline = new GuardrailPipeline([
            Fake(GuardrailCheckName.SingleStatement, PassAlways),
            Fake(GuardrailCheckName.ScopeFilterInjection, ctx =>
            {
                ctx.RecordScopeFilter("region IN (@scope0)", injectionCount: 1);
                return CheckResult.Pass(GuardrailCheckName.ScopeFilterInjection);
            })
        ]);

        var result = pipeline.Execute(context);

        Assert.Equal(GuardrailDecision.Accepted, result.Decision);
        Assert.Equal("region IN (@scope0)", result.AppliedScopeFilter);
        Assert.Equal(ReasonCode.None, result.ReasonCode);
        Assert.Equal(30, result.QueryTimeoutSeconds);
        Assert.Equal("Sql150", result.ParserVersion);
        Assert.All(result.Checks, check => Assert.Equal(CheckOutcome.Passed, check.Outcome));
    }

    [Fact]
    public void Tum_kontroller_gecse_dahi_kapsam_kaydedilmezse_reddedilir()
    {
        // En kritik savunma testi: pipeline'da bir hata olsa ve kapsam filtresi hic
        // uygulanmasa bile filtresiz sorgu disa cikmamali.
        var context = CreateContext();
        var pipeline = new GuardrailPipeline([
            Fake(GuardrailCheckName.SingleStatement, PassAlways),
            Fake(GuardrailCheckName.ScopeFilterInjection, PassAlways)
        ]);

        var result = pipeline.Execute(context);

        Assert.Equal(GuardrailDecision.Rejected, result.Decision);
        Assert.Equal(ReasonCode.GR014, result.ReasonCode);
        Assert.Null(result.Sql);
    }

    [Fact]
    public void Ilk_basarisizlikta_durur_ve_kalan_kontroller_atlanmis_isaretlenir()
    {
        var context = CreateContext();
        var thirdCheck = Fake(GuardrailCheckName.AllowListObjects, PassAlways);

        var pipeline = new GuardrailPipeline([
            Fake(GuardrailCheckName.SingleStatement, PassAlways),
            Fake(GuardrailCheckName.SelectOnly, _ =>
                CheckResult.Fail(GuardrailCheckName.SelectOnly, ReasonCode.GR001)),
            thirdCheck
        ]);

        var result = pipeline.Execute(context);

        Assert.Equal(GuardrailDecision.Rejected, result.Decision);
        Assert.Equal(ReasonCode.GR001, result.ReasonCode);

        // Basarisizliktan sonraki kontrol GERCEKTEN kosulmadi.
        Assert.Equal(0, thirdCheck.ExecutionCount);

        var skipped = Assert.Single(result.Checks, check => check.Outcome == CheckOutcome.Skipped);
        Assert.Equal(GuardrailCheckName.AllowListObjects, skipped.Name);
    }

    [Fact]
    public void Reddedilen_sorgunun_SQL_metni_disa_verilmez()
    {
        var context = CreateContext("DELETE FROM vw_sales");
        var pipeline = new GuardrailPipeline([
            Fake(GuardrailCheckName.SelectOnly, _ =>
                CheckResult.Fail(GuardrailCheckName.SelectOnly, ReasonCode.GR001))
        ]);

        var result = pipeline.Execute(context);

        Assert.Null(result.Sql);
        Assert.Empty(result.Parameters);
        Assert.Null(result.AppliedScopeFilter);
    }

    [Fact]
    public void Kontrol_istisna_firlatirsa_kabul_degil_ret_uretilir()
    {
        // "Kontrol calismadi" ile "kontrol gecti" asla ayni sey degildir.
        var context = CreateContext();
        var pipeline = new GuardrailPipeline([
            Fake(GuardrailCheckName.ParseToAst, _ => throw new InvalidOperationException("beklenmeyen"))
        ]);

        var result = pipeline.Execute(context);

        Assert.Equal(GuardrailDecision.Rejected, result.Decision);
        Assert.Equal(ReasonCode.GR014, result.ReasonCode);

        var failed = Assert.Single(result.Checks, check => check.Outcome == CheckOutcome.Failed);
        Assert.Contains("InvalidOperationException", failed.Detail!, StringComparison.Ordinal);
    }

    [Fact]
    public void CL_kodu_netlestirme_karari_uretir()
    {
        var context = CreateContext();
        var pipeline = new GuardrailPipeline([
            Fake(GuardrailCheckName.InputLimits, _ =>
                CheckResult.Fail(GuardrailCheckName.InputLimits, ReasonCode.CL001))
        ]);

        var result = pipeline.Execute(context);

        Assert.Equal(GuardrailDecision.NeedsClarification, result.Decision);
        Assert.Equal(ReasonCode.CL001, result.ReasonCode);
        Assert.Equal("Hangi metriği görmek istediğinizi belirtir misiniz?", result.ReasonMessage);
    }

    [Fact]
    public void Kontroller_sira_disi_verilirse_pipeline_kurulamaz()
    {
        // Sira bir detay degil guvenlik gereksinimi: kapsam enjeksiyonu allow-list
        // dogrulamasindan once calisirsa izinsiz bir objeye filtre eklenmis olur.
        var exception = Assert.Throws<ArgumentException>(() => new GuardrailPipeline([
            Fake(GuardrailCheckName.ScopeFilterInjection, PassAlways),
            Fake(GuardrailCheckName.AllowListObjects, PassAlways)
        ]));

        Assert.Contains("sirasina gore", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Ayni_kontrol_iki_kez_tanimlanamaz()
    {
        var exception = Assert.Throws<ArgumentException>(() => new GuardrailPipeline([
            Fake(GuardrailCheckName.SelectOnly, PassAlways),
            Fake(GuardrailCheckName.SelectOnly, PassAlways)
        ]));

        Assert.Contains("birden fazla kez", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Bos_kontrol_listesiyle_pipeline_kurulamaz()
    {
        Assert.Throws<ArgumentException>(() => new GuardrailPipeline([]));
    }

    private static CheckResult PassAlways(GuardrailContext context) =>
        CheckResult.Pass(GuardrailCheckName.SingleStatement);

    private static FakeCheck Fake(GuardrailCheckName name, Func<GuardrailContext, CheckResult> behavior) =>
        new(name, behavior);

    private sealed class FakeCheck(GuardrailCheckName name, Func<GuardrailContext, CheckResult> behavior)
        : IGuardrailCheck
    {
        public int ExecutionCount { get; private set; }

        public GuardrailCheckName Name => name;

        public CheckResult Execute(GuardrailContext context)
        {
            ExecutionCount++;
            var result = behavior(context);
            // Fake'in dondurdugu adi kendi adiyla hizala; testler sonucu ada gore ariyor.
            return result with { Name = name };
        }
    }
}
