using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Crm.Analytics.Sql.Parsing;

/// <summary>
/// Hedef T-SQL surumune uygun parser ve script uretici saglar.
/// </summary>
/// <remarks>
/// <para>
/// Surum secimi guvenlik acisindan onemlidir: sunucudan <b>daha yeni</b> bir parser secmek,
/// sunucunun anlamadigi sozdizimini kabul etmek anlamina gelir. Ters yon (daha eski parser)
/// guvenlidir cunku taninmayan sozdizimi parse hatasi uretir ve fail-closed davranir.
/// </para>
/// <para>
/// Yalnizca acikca desteklenen surumler kabul edilir; bilinmeyen bir surum icin sessizce
/// varsayilana dusmek yerine hata verilir.
/// </para>
/// </remarks>
public sealed class TSqlParserFactory
{
    /// <summary>Onaylanan hedef: SQL Server 2019.</summary>
    public const SqlVersion DefaultVersion = SqlVersion.Sql150;

    private readonly SqlVersion version;

    public TSqlParserFactory(SqlVersion version = DefaultVersion)
    {
        if (version is not (SqlVersion.Sql150 or SqlVersion.Sql160))
        {
            throw new ArgumentOutOfRangeException(
                nameof(version), version,
                "Yalnizca Sql150 (SQL Server 2019) ve Sql160 (SQL Server 2022) desteklenir. " +
                "Yeni bir surum eklemek, o surumun sozdizimi icin kontrollerin gozden gecirilmesini gerektirir.");
        }

        this.version = version;
    }

    /// <summary>Audit'e yazilacak surum adi. Karari sonradan yeniden uretebilmek icin gerekli.</summary>
    public string VersionName => version.ToString();

    /// <summary>
    /// Verilen SQL'i parse eder. Hata listesi bos degilse fragment dolu olsa dahi sonuc
    /// basarisiz sayilir.
    /// </summary>
    public SqlParseResult Parse(string sql)
    {
        ArgumentNullException.ThrowIfNull(sql);

        var parser = CreateParser();
        using var reader = new StringReader(sql);

        var fragment = parser.Parse(reader, out var errors);
        return new SqlParseResult(fragment, errors.ToArray());
    }

    /// <summary>
    /// Hedef surume karsilik gelen parser. <c>TSqlParser.Create</c> statik bir fabrika
    /// DEGILDIR; surum-spesifik tipler dogrudan kurulur.
    /// </summary>
    private TSqlParser CreateParser() => version switch
    {
        SqlVersion.Sql150 => new TSql150Parser(initialQuotedIdentifiers: false),
        SqlVersion.Sql160 => new TSql160Parser(initialQuotedIdentifiers: false),
        _ => throw new InvalidOperationException($"Desteklenmeyen surum: {version}")
    };

    /// <summary>
    /// Bir boolean ifadeyi (WHERE kosulu) AST dugumune cevirir.
    /// </summary>
    /// <remarks>
    /// Kapsam filtresini elle AST kurarak degil, sabit ve guvenli bir metni parse ederek
    /// uretiyoruz. Metne <b>yalnizca allow-list'ten gelen, dar bir desene karsi dogrulanmis
    /// kolon adlari ve kendi urettigimiz parametre adlari</b> girer; kullanicidan gelen hicbir
    /// deger bu metne gomulmez.
    /// </remarks>
    public BooleanExpression ParseBooleanExpression(string expression)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expression);

        var parser = CreateParser();
        using var reader = new StringReader(expression);

        var parsed = parser.ParseBooleanExpression(reader, out var errors);

        if (errors.Count > 0 || parsed is null)
        {
            throw new InvalidOperationException(
                $"Kapsam filtresi ifadesi parse edilemedi: '{expression}'. " +
                $"Ilk hata: {errors.FirstOrDefault()?.Message ?? "(bilinmiyor)"}");
        }

        return parsed;
    }

    /// <summary>
    /// Bir skaler ifadeyi (metrik ifadesi gibi) AST dugumune cevirir.
    /// </summary>
    /// <remarks>
    /// Metric Catalog'daki <c>expression</c> alanlari serbest SQL metnidir. Query Builder bu
    /// metni SQL'e <b>gomerek</b> degil, parse edip AST olarak yerlestirerek kullanir; boylece
    /// katalogdaki bir metin hicbir zaman string birlestirme yoluyla sorguya girmez.
    /// </remarks>
    public ScalarExpression? TryParseExpression(string expression, out IList<ParseError> errors)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expression);

        var parser = CreateParser();
        using var reader = new StringReader(expression);

        return parser.ParseExpression(reader, out errors);
    }

    /// <summary>
    /// AST'yi tekrar SQL metnine cevirir. <paramref name="versioningErrors"/> bos degilse
    /// uretilen AST hedef surum icin gecersizdir ve cikti KULLANILMAMALIDIR.
    /// </summary>
    public string GenerateScript(TSqlFragment fragment, out IList<ParseError> versioningErrors)
    {
        ArgumentNullException.ThrowIfNull(fragment);

        var generator = CreateGenerator();
        generator.GenerateScript(fragment, out var script, out versioningErrors);
        return script;
    }

    private SqlScriptGenerator CreateGenerator()
    {
        var options = new SqlScriptGeneratorOptions
        {
            SqlVersion = version,
            KeywordCasing = KeywordCasing.Uppercase,
            IncludeSemicolons = false,
            AlignClauseBodies = false
        };

        return version switch
        {
            SqlVersion.Sql150 => new Sql150ScriptGenerator(options),
            SqlVersion.Sql160 => new Sql160ScriptGenerator(options),
            _ => throw new InvalidOperationException($"Desteklenmeyen surum: {version}")
        };
    }
}
