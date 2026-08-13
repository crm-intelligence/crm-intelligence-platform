using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Crm.Analytics.Sql.Tests.Environment;

/// <summary>
/// Ortam kaniti. Guardrail tasariminin tamami ScriptDom'un net10.0 uzerinde calismasina ve
/// parse -> mutate -> regenerate dongusunun desteklenmesine dayaniyor. Paket net8.0 TFM'i
/// tasidigi icin bunu varsaymak yerine dogruluyoruz.
/// </summary>
public class ScriptDomSmokeTests
{
    // SQL Server 2019 hedefi (onaylanan karar). Sunucudan daha yeni bir parser secmek,
    // sunucunun anlamadigi sozdizimini kabul etmek anlamina gelirdi.
    private static TSqlParser CreateParser() => new TSql150Parser(initialQuotedIdentifiers: false);

    [Fact]
    public void Parser_gecerli_select_ifadesini_hatasiz_parse_eder()
    {
        var fragment = CreateParser().Parse(new StringReader("SELECT region FROM vw_sales"), out var errors);

        Assert.Empty(errors);
        var script = Assert.IsType<TSqlScript>(fragment);
        var batch = Assert.Single(script.Batches);
        var statement = Assert.Single(batch.Statements);
        Assert.IsType<SelectStatement>(statement);
    }

    [Fact]
    public void Parser_gecersiz_sozdizimini_ParseError_ile_bildirir()
    {
        // Fail-closed davranisin kaniti: parser sessizce "en iyi tahmin" uretmez.
        var fragment = CreateParser().Parse(new StringReader("SELECT FROM WHERE"), out var errors);

        Assert.NotEmpty(errors);
        // fragment null olmasa dahi errors dolu ise guardrail reddeder.
        Assert.NotNull(fragment);
    }

    [Fact]
    public void ScriptGenerator_AST_uzerinden_SQL_yeniden_uretir()
    {
        var fragment = CreateParser().Parse(new StringReader("select region from vw_sales"), out var errors);
        Assert.Empty(errors);

        var generator = new Sql150ScriptGenerator();
        generator.GenerateScript(fragment, out var regenerated, out var versioningErrors);

        // versioningErrors: uretilen AST hedef SqlVersion icin gecersizse burada bildirilir.
        // Guardrail bu listeyi kontrol etmeden cikti dondurmez.
        Assert.Empty(versioningErrors);
        Assert.Contains("SELECT", regenerated, StringComparison.Ordinal);
        Assert.Contains("vw_sales", regenerated, StringComparison.Ordinal);
    }

    [Fact]
    public void Yeniden_uretilen_SQL_tekrar_parse_edilebilir()
    {
        // Kontrol 15'in (RegenerateAndRevalidate) temel varsayimi.
        var parser = CreateParser();
        var fragment = parser.Parse(new StringReader("SELECT TOP 10 region FROM vw_sales WHERE region = @p0"), out var errors);
        Assert.Empty(errors);

        new Sql150ScriptGenerator().GenerateScript(fragment, out var regenerated, out _);
        CreateParser().Parse(new StringReader(regenerated), out var reparseErrors);

        Assert.Empty(reparseErrors);
    }

    [Fact]
    public void Yorumlar_yeniden_uretimde_dusurulur()
    {
        // Bu, guardrail icin bir AVANTAJ: yorum tabanli filtre atlatma denemeleri
        // (ornek: "WHERE region = 'Ege' -- AND scope") normalize edilirken silinir.
        var fragment = CreateParser().Parse(
            new StringReader("SELECT region FROM vw_sales -- gizli yorum"), out var errors);
        Assert.Empty(errors);

        new Sql150ScriptGenerator().GenerateScript(fragment, out var regenerated, out _);

        Assert.DoesNotContain("gizli yorum", regenerated, StringComparison.Ordinal);
    }

    [Fact]
    public void VariableReference_adi_at_isaretini_icerir()
    {
        // Literal -> parametre donusumunde uretecegimiz dugumun ad bicimini sabitliyoruz.
        // Arastirmada dokumante edilmemisti; davranisi test ile kilitliyoruz.
        var fragment = CreateParser().Parse(
            new StringReader("SELECT region FROM vw_sales WHERE region = @scopeRegion0"), out var errors);
        Assert.Empty(errors);

        var collector = new VariableReferenceCollector();
        fragment.Accept(collector);

        var variable = Assert.Single(collector.Found);
        Assert.Equal("@scopeRegion0", variable.Name);
    }

    [Fact]
    public void StringLiteral_tirnaksiz_ve_unicode_bilgisiyle_gelir()
    {
        // IsNational kaybi, parametreyi NVARCHAR yerine VARCHAR baglamaya ve collation'a
        // bagli farkli satir kumesine yol acar. Turkce I/i cifti bu yuzden kritiktir.
        var fragment = CreateParser().Parse(
            new StringReader("SELECT region FROM vw_sales WHERE region = N'İzmir'"), out var errors);
        Assert.Empty(errors);

        var collector = new StringLiteralCollector();
        fragment.Accept(collector);

        var literal = Assert.Single(collector.Found);
        Assert.Equal("İzmir", literal.Value);
        Assert.True(literal.IsNational);
    }

    private sealed class VariableReferenceCollector : TSqlFragmentVisitor
    {
        public List<VariableReference> Found { get; } = [];

        public override void Visit(VariableReference node) => Found.Add(node);
    }

    private sealed class StringLiteralCollector : TSqlFragmentVisitor
    {
        public List<StringLiteral> Found { get; } = [];

        public override void Visit(StringLiteral node) => Found.Add(node);
    }
}
