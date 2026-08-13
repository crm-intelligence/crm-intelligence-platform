using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Crm.Analytics.Sql.IntegrationTests.Sqlite;

/// <summary>
/// Fonksiyon cagrilarini SQLite'in kabul edecegi bicime YERINDE cevirir.
/// </summary>
/// <remarks>
/// Hicbir node degistirilmez; yalnizca mevcut node'un alanlari guncellenir.
/// Bunun zorunlulugu <see cref="SqliteDialectTranslator"/> aciklamasinda:
/// SELECT ile GROUP BY ayni ifade ornegini paylasir.
/// </remarks>
internal sealed class SqliteFunctionRewriter : TSqlFragmentVisitor
{
    /// <summary>
    /// Ilk argumani tarih parcasi ADI olan T-SQL fonksiyonlari.
    /// </summary>
    private static readonly HashSet<string> DatePartFunctions =
        new(StringComparer.OrdinalIgnoreCase) { "DATEPART", "DATEDIFF", "DATEADD" };

    /// <summary>
    /// Kabul edilen tarih parcasi adlari.
    /// </summary>
    /// <remarks>
    /// <c>week</c> ve <c>weekday</c> BILINCLI olarak DISARIDA: T-SQL'de sonuc
    /// <c>DATEFIRST</c> oturum ayarina baglidir, yani deterministik degildir.
    /// Kutuphane bunu <c>DeterministicQueryBuilder.BuildGrainExpression</c>
    /// icinde de belirtiyor. Deterministik olmayan bir seyin "dogru" cevirisi
    /// yoktur; sessiz bir varsayim yapmak yerine reddedilir.
    /// </remarks>
    private static readonly HashSet<string> KnownDateParts =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "year", "quarter", "month", "day", "dayofyear", "hour", "minute", "second"
        };

    public override void Visit(FunctionCall node)
    {
        var name = node.FunctionName.Value;

        // 1) Yeniden adlandirma (ISNULL -> COALESCE, COUNT_BIG -> COUNT).
        if (TSqlFunctionSets.Renamed.TryGetValue(name, out var sqliteName))
        {
            node.FunctionName.Value = sqliteName;
            name = sqliteName;
        }

        // 2) Tarih parcasi argumani: bare identifier -> string literal.
        if (DatePartFunctions.Contains(name))
        {
            NormalizeDatePartArgument(node, name);
        }

        base.Visit(node);
    }

    /// <summary>
    /// <c>DATEPART(quarter, c)</c> ifadesinde <c>quarter</c> ScriptDom
    /// tarafindan bir KOLON REFERANSI olarak ayristirilir. SQLite bunu oldugu
    /// gibi alirsa <c>no such column: quarter</c> hatasi verir (dogrulandi).
    /// Bu yuzden ilk arguman yerinde bir metin sabitine cevrilir.
    /// </summary>
    private static void NormalizeDatePartArgument(FunctionCall node, string functionName)
    {
        if (node.Parameters.Count == 0)
        {
            throw SqliteTranslationNotSupportedException.For(
                $"{functionName}()",
                "tarih parcasi argumani yok.");
        }

        var part = ReadDatePartName(node.Parameters[0], functionName);

        if (!KnownDateParts.Contains(part))
        {
            var reason = part.Equals("week", StringComparison.OrdinalIgnoreCase)
                || part.Equals("weekday", StringComparison.OrdinalIgnoreCase)
                ? "T-SQL sonucu DATEFIRST oturum ayarina baglidir, deterministik " +
                  "karsiligi yoktur."
                : "taninmayan tarih parcasi.";

            throw SqliteTranslationNotSupportedException.For(
                $"{functionName}('{part}', ...)",
                reason,
                "Sqlite/SqliteFunctionRewriter.cs");
        }

        // Yerinde degisim: FunctionCall node'unun KENDI listesi guncellenir,
        // node'un kendisi degistirilmez. Paylasilan referanslar tutarli kalir.
        node.Parameters[0] = new StringLiteral { Value = part };
    }

    private static string ReadDatePartName(ScalarExpression parameter, string functionName)
    {
        return parameter switch
        {
            // Olagan durum: ScriptDom tarih parcasi adini IdentifierLiteral
            // olarak ayristirir (dogrulandi — ColumnReferenceExpression DEGIL).
            IdentifierLiteral identifier => identifier.Value,

            // DeterministicQueryBuilder.BuildGrainExpression parcayi kolon
            // referansi olarak kurabilir.
            ColumnReferenceExpression { MultiPartIdentifier.Identifiers.Count: 1 } column
                => column.MultiPartIdentifier.Identifiers[0].Value,

            // Ham SQL'de tirnakla yazilmis olabilir; zaten dogru bicimde.
            StringLiteral literal => literal.Value,

            _ => throw SqliteTranslationNotSupportedException.For(
                $"{functionName}(...)",
                $"tarih parcasi argumani beklenmeyen bicimde: {parameter.GetType().Name}.")
        };
    }
}
