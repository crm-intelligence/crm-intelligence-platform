using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Crm.Analytics.Sql.Guardrail.Visitors;

/// <summary>
/// Bir tablo referansina verilen ad: allow-list'te aranacak obje adi ve sorguda kullanilan
/// nitelendirici (alias veya tablo adi).
/// </summary>
/// <param name="ObjectName">
/// Allow-list'te aranacak ad. <b>Tam ad</b> (sema oneki dahil, <c>SchemaObjectNames.FullName</c>).
/// Son parcayi almak <c>baska_sema.vw_sales</c>'i <c>vw_sales</c> gibi gostererek allow-list'i
/// atlatmaya izin verirdi.
/// </param>
/// <param name="Qualifier">
/// Kolonu nitelendirmek icin kullanilacak tanimlayici. Alias varsa alias, yoksa tablo adi.
/// Kullanicidan geldigi icin <b>asla metne gomulmez</b>; AST dugumu olarak tasinir.
/// </param>
/// <param name="Reference">Kaynak AST dugumu.</param>
public sealed record TableReferenceInfo(
    string ObjectName,
    Identifier Qualifier,
    NamedTableReference Reference);

/// <summary>
/// Bir sorgu blokunun FROM ifadesindeki <b>dogrudan</b> tablo referanslarini okur.
/// </summary>
/// <remarks>
/// <para>
/// Turetilmis tablolarin (<see cref="QueryDerivedTable"/>) icine <b>girmez</b>: onlarin
/// govdesindeki sorgu bloklari <see cref="QuerySpecificationCollector"/> tarafindan ayrica
/// bulunur ve kendi filtresini alir. Buraya girmek ayni bloga iki kez filtre eklenmesine
/// yol acardi.
/// </para>
/// <para>
/// Taninmayan tablo referansi tipleri (APPLY, OPENROWSET, OPENJSON, inline TVF, PIVOT ...)
/// burada sessizce atlanmaz — <see cref="Unsupported"/> listesine yazilir. Bunlarin reddi
/// <c>NodeTypeWhitelist</c> kontrolunun isidir ve o kontrol bu kontrolden ONCE calisir;
/// yine de sessiz atlama birakmamak icin liste raporlanir.
/// </para>
/// </remarks>
public static class DirectTableReferenceReader
{
    public sealed record Result(
        IReadOnlyList<TableReferenceInfo> Tables,
        IReadOnlyList<string> Unsupported);

    public static Result Read(QuerySpecification querySpecification)
    {
        ArgumentNullException.ThrowIfNull(querySpecification);

        var tables = new List<TableReferenceInfo>();
        var unsupported = new List<string>();

        if (querySpecification.FromClause is null)
        {
            // FROM'suz SELECT (ornek: SELECT 1). Tablo yok, kapsam filtresi de gerekmez.
            return new Result(tables, unsupported);
        }

        foreach (var tableReference in querySpecification.FromClause.TableReferences)
        {
            Walk(tableReference, tables, unsupported);
        }

        return new Result(tables, unsupported);
    }

    private static void Walk(
        TableReference tableReference,
        List<TableReferenceInfo> tables,
        List<string> unsupported)
    {
        switch (tableReference)
        {
            case NamedTableReference named:
                tables.Add(Describe(named));
                break;

            case QualifiedJoin qualifiedJoin:
                Walk(qualifiedJoin.FirstTableReference, tables, unsupported);
                Walk(qualifiedJoin.SecondTableReference, tables, unsupported);
                break;

            // Virgullu eski stil join: FROM a, b
            case UnqualifiedJoin unqualifiedJoin:
                Walk(unqualifiedJoin.FirstTableReference, tables, unsupported);
                Walk(unqualifiedJoin.SecondTableReference, tables, unsupported);
                break;

            case JoinParenthesisTableReference parenthesis:
                Walk(parenthesis.Join, tables, unsupported);
                break;

            case QueryDerivedTable:
                // Bilincli olarak icine girilmez; govdesi ayri bir sorgu blogu olarak ele alinir.
                break;

            default:
                unsupported.Add(tableReference.GetType().Name);
                break;
        }
    }

    private static TableReferenceInfo Describe(NamedTableReference named)
    {
        // Allow-list araması TAM ad ile yapilir; son parcayi almak baska_sema.vw_sales'i
        // vw_sales gibi gostererek bir acik yaratirdi.
        var objectName = SchemaObjectNames.FullName(named.SchemaObject);

        // Kolonu nitelendirirken alias varsa alias kullanilir; yoksa tablo adinin son parcasi
        // (T-SQL'de kolon nitelendiricisi sema adini icermez). Identifier dugumu oldugu gibi
        // tasinir, metne cevrilmez: kacis karakteri barindiran bir alias
        // ('[evil]]; DROP ...]') metne gomulse guardrail kendi eliyle enjeksiyon uretirdi.
        var qualifier = named.Alias ?? named.SchemaObject.Identifiers[^1];

        return new TableReferenceInfo(objectName, qualifier, named);
    }
}
