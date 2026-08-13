using System.Globalization;
using Crm.Analytics.Sql.Parsing;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Crm.Analytics.Sql.IntegrationTests.Sqlite;

/// <summary>
/// Guardrail'in onayladigi T-SQL'i yerel SQLite fixture'inda calistirilabilir
/// hale getirir. YALNIZCA test/harness katmaninda yasar; uretim kodu T-SQL'de
/// kalir.
/// </summary>
/// <remarks>
/// <para><b>Tasarim: UDF oncelikli, yalnizca yerinde AST mutasyonu.</b></para>
/// <para>
/// Fonksiyonlarin cogu SQL'e cevrilmek yerine <see cref="TSqlCompatibilityFunctions"/>
/// tarafindan C# UDF olarak kaydedilir. Boylece calistirilan metin, guardrail'in
/// onayladigi metne neredeyse birebir ayni kalir — eklenen her yeniden yazma
/// kurali, guardrail'in gercek bir hatasini gizleyebilecek bir kuraldir.
/// </para>
/// <para>
/// AST'ye yalnizca SQLite GRAMERI fonksiyonu imkansiz kildigi yerlerde dokunulur
/// ve dokunulan her yerde islem YERINDEDIR (node degistirilmez, node'un kendi
/// alanlari degistirilir). Bunun zorunlu olmasinin nedeni
/// <c>DeterministicQueryBuilder.AddGroupByClause</c>: SELECT ile GROUP BY AYNI
/// <c>ScalarExpression</c> ORNEGINI paylasir. Node'u degistirmek iki ebeveynin
/// duzeltilmesini gerektirir; biri atlanirsa SELECT ile GROUP BY ayrisir, SQLite
/// bunu KABUL EDER ve sessizce yanlis gruplama uretir. Yerinde mutasyonda bu
/// risk yapisal olarak yoktur.
/// </para>
/// <para>
/// Metin duzeyinde tek islem <c>LIMIT</c> eklemesidir (asagida gerekcesi).
/// SQL metnini ayristiran hicbir regex yoktur — kutuphanenin "regex ile
/// yetinilmez" ilkesi harness'ta da korunur.
/// </para>
/// </remarks>
internal sealed class SqliteDialectTranslator(TSqlParserFactory parserFactory)
{
    /// <summary>
    /// T-SQL metnini SQLite'ta calistirilabilir metne cevirir.
    /// </summary>
    /// <exception cref="SqliteTranslationNotSupportedException">
    /// Guvenle cevrilemeyen bir yapi bulundugunda.
    /// </exception>
    public string Translate(string tsql)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tsql);

        var parsed = parserFactory.Parse(tsql);

        if (parsed.Errors.Count > 0)
        {
            throw SqliteTranslationNotSupportedException.For(
                "Girdi",
                $"T-SQL olarak ayristirilamadi: {parsed.Errors[0].Message}.");
        }

        var fragment = parsed.Fragment
            ?? throw SqliteTranslationNotSupportedException.For(
                "Girdi", "ayristirma sonucu bos.");

        // Production contract schema-qualified MART adlari kullanir. Yerel fixture ise
        // ayni logical view contract'larini SQLite'in tek-parcali adlariyla kurar. Yalnizca
        // merkezi contract'ta acikca tanimli fiziksel adlar test logical adina cevrilir;
        // kullanici girdisinden schema/object tahmini yapilmaz.
        var objectMapper = new FixtureObjectMappingRewriter();
        fragment.Accept(objectMapper);

        // 1) Bastan reddedilen yapilar. Erken reddetmek, yarim cevrilmis bir
        //    AST uzerinde calismaktan iyidir.
        var shapes = new UnsupportedShapeVisitor();
        fragment.Accept(shapes);
        shapes.ThrowIfUnsupported();

        // 2) Fonksiyon adlari ve datepart argumanlari: yerinde.
        var rewriter = new SqliteFunctionRewriter();
        fragment.Accept(rewriter);

        // 3) TOP -> LIMIT: deger okunur, AST'den kaldirilir.
        var rowLimit = ExtractRowLimit(fragment);

        // 4) Cevrilemeyen bir sey kaldi mi? Uretmeden once son kontrol.
        var verifier = new PostTranslationVerifier();
        fragment.Accept(verifier);
        verifier.ThrowIfUnsupported();

        var sql = parserFactory.GenerateScript(fragment, out var versioningErrors);

        if (versioningErrors.Count > 0)
        {
            throw SqliteTranslationNotSupportedException.For(
                "Yeniden uretim",
                $"AST hedef surum icin gecersiz: {versioningErrors[0].Message}.");
        }

        if (rowLimit is { } limit)
        {
            // Metin duzeyindeki TEK islem. Savunmasi: eklenen metin
            // "LIMIT " + int bicimindedir ve int, AST'deki IntegerLiteral'dan
            // gelir — kullanici verisi bu yola hic degmez. T-SQL AST'sinde
            // LIMIT dugumu olmadigi icin baska yol yok.
            // ORDER BY generator tarafindan en sona yazildigi ve SQLite'ta
            // LIMIT'in ORDER BY'dan sonra gelmesi gerektigi icin sona eklemek
            // dogru konumdur.
            var trimmed = sql.TrimEnd().TrimEnd(';');
            sql = $"{trimmed}\nLIMIT {limit.ToString(CultureInfo.InvariantCulture)}";
        }

        return sql;
    }

    /// <summary>
    /// En dis sorgu bloklarindaki <c>TOP</c> filtresini okur ve AST'den kaldirir.
    /// </summary>
    /// <remarks>
    /// Guardrail'in <c>RowLimitInjector</c>'i limiti yalnizca EN DIS bloklara
    /// ekler ve yuzde bazli / sabit olmayan TOP'u normalize eder. Buna ragmen
    /// beklenmeyen bicimlerde istisna atiyoruz: bu bicimlerden birine
    /// rastlanmasi guardrail'in degistigi anlamina gelir ve harness bunu
    /// ortmemelidir.
    /// </remarks>
    private static int? ExtractRowLimit(TSqlFragment fragment)
    {
        if (fragment is not TSqlScript script)
        {
            return null;
        }

        int? limit = null;

        foreach (var batch in script.Batches)
        {
            foreach (var statement in batch.Statements)
            {
                if (statement is not SelectStatement select)
                {
                    continue;
                }

                // BinaryQueryExpression (UNION vb.) UnsupportedShapeVisitor
                // tarafindan zaten reddedildi.
                if (select.QueryExpression is not QuerySpecification block)
                {
                    continue;
                }

                if (block.TopRowFilter is not { } top)
                {
                    continue;
                }

                limit = ReadLimitValue(top);
                block.TopRowFilter = null;
            }
        }

        return limit;
    }

    private static int ReadLimitValue(TopRowFilter top)
    {
        if (top.Percent)
        {
            throw SqliteTranslationNotSupportedException.For(
                "TOP ... PERCENT",
                "yuzde bazli limit satir sayisini garanti etmez. Guardrail'in " +
                "RowLimitInjector'i bunu normalize etmeliydi; normalize edilmemis " +
                "bir deger guardrail'in degistigini gosterir.");
        }

        if (top.WithTies)
        {
            throw SqliteTranslationNotSupportedException.For(
                "TOP ... WITH TIES",
                "SQLite'ta karsiligi yok ve dondurulen satir sayisi ust sinirin " +
                "uzerine cikabilir.");
        }

        // TOP (5000) biciminde parantez, ScriptDom'da ParenthesisExpression
        // olarak temsil edilir; deger icte. Guardrail'in RowLimitInjector'i
        // parantezsiz IntegerLiteral uretse de ham taslak parantezli gelebilir.
        if (Unwrap(top.Expression) is not IntegerLiteral integerLiteral)
        {
            throw SqliteTranslationNotSupportedException.For(
                "TOP (sabit olmayan ifade)",
                "limit degeri derleme aninda bilinmiyor.");
        }

        return int.Parse(integerLiteral.Value, CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Ic ice parantezleri soyup asil ifadeyi dondurur.
    /// </summary>
    private static ScalarExpression Unwrap(ScalarExpression expression)
    {
        while (expression is ParenthesisExpression parenthesis)
        {
            expression = parenthesis.Expression;
        }

        return expression;
    }

    private sealed class FixtureObjectMappingRewriter : TSqlFragmentVisitor
    {
        public override void Visit(NamedTableReference node)
        {
            var physicalName = string.Join(
                '.', node.SchemaObject.Identifiers.Select(identifier => identifier.Value));
            var logicalName = OlistCatalog.AllowList
                .FindLogicalNameBySqlObject(physicalName);
            if (logicalName is null
                || physicalName.Equals(logicalName, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            node.SchemaObject.Identifiers.Clear();
            node.SchemaObject.Identifiers.Add(new Identifier { Value = logicalName });
        }
    }
}
