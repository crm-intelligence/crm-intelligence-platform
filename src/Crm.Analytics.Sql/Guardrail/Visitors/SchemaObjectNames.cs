using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Crm.Analytics.Sql.Guardrail.Visitors;

/// <summary>
/// Obje adi cozumleme. Allow-list araması icin <b>tam ad</b> kullanilir.
/// </summary>
/// <remarks>
/// Adin yalnizca son parcasini almak (ornek: <c>dbo.vw_sales</c> -> <c>vw_sales</c>) bir
/// guvenlik acigidir: <c>baska_sema.vw_sales</c> da ayni son parcaya sahiptir ve allow-list'te
/// <c>vw_sales</c> varsa kabul edilirdi. Bu yuzden parcalar birlestirilerek karsilastirilir;
/// sorgu <c>dbo.vw_sales</c> yaziyorsa allow-list'te de <c>dbo.vw_sales</c> tanimli olmalidir.
/// Katilik bilinclidir: hangi objenin izinli oldugu tahmine birakilmaz.
/// </remarks>
public static class SchemaObjectNames
{
    /// <summary>Nokta ile birlestirilmis tam ad.</summary>
    public static string FullName(SchemaObjectName schemaObject)
    {
        ArgumentNullException.ThrowIfNull(schemaObject);

        return string.Join('.', schemaObject.Identifiers.Select(identifier => identifier.Value));
    }
}
