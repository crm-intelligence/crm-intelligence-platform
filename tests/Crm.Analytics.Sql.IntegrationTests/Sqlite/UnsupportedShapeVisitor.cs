using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Crm.Analytics.Sql.IntegrationTests.Sqlite;

/// <summary>
/// Ceviriye BASLAMADAN once, bilinen ve bilincli olarak ertelenmis yapilari
/// reddeder.
/// </summary>
internal sealed class UnsupportedShapeVisitor : TSqlFragmentVisitor
{
    private readonly List<SqliteTranslationNotSupportedException> failures = [];

    /// <summary>
    /// Bilesik sorgular (<c>UNION</c>, <c>EXCEPT</c>, <c>INTERSECT</c>).
    /// </summary>
    /// <remarks>
    /// GERCEK ve BELGELENMIS bir bosluk. Guardrail'in <c>RowLimitInjector</c> ve
    /// <c>ScopeFilterInjector</c>'i limiti ve kapsam filtresini HER KOLA ekler
    /// (bilincli bir guvenlik karari). Ancak SQLite bilesik bir sorgunun tek bir
    /// koluna <c>LIMIT</c> konmasini yasaklar, ve <c>LIMIT</c>'i bilesigin
    /// tamamina tasimak ANLAMI DEGISTIRIR.
    ///
    /// Mekanik cozum her kolu <c>SELECT * FROM (kol LIMIT n)</c> icine sarmaktir;
    /// bu yapisal bir yeniden yazma oldugu icin kendi altin testleriyle ayri bir
    /// asamaya birakildi.
    ///
    /// Kapsam notu: Query Builder yolu DAIMA tek bir QuerySpecification uretir,
    /// bu yuzden bu bosluk yalnizca ham NL2SQL taslagi yolundan tetiklenebilir.
    /// </remarks>
    public override void Visit(BinaryQueryExpression node)
    {
        failures.Add(SqliteTranslationNotSupportedException.For(
            $"Bilesik sorgu ({node.BinaryQueryExpressionType})",
            "guardrail limiti/kapsam filtresini her kola ekler; SQLite ise bilesik " +
            "bir sorgunun tek koluna LIMIT konmasina izin vermez. LIMIT'i disa " +
            "tasimak anlami degistirir, bu yuzden sessizce cevrilmiyor."));

        base.Visit(node);
    }

    /// <summary>
    /// <c>OFFSET ... FETCH</c>: SQLite'ta <c>LIMIT ... OFFSET</c> karsiligi var
    /// ama guardrail bunu uretmiyor. Uretmeye baslarsa harness sessiz kalmasin.
    /// </summary>
    public override void Visit(OffsetClause node)
    {
        failures.Add(SqliteTranslationNotSupportedException.For(
            "OFFSET / FETCH",
            "guardrail bu yapiyi uretmiyor; uretmeye basladiysa ceviri kurali " +
            "bilincli olarak eklenmelidir."));

        base.Visit(node);
    }

    /// <summary>
    /// Cok parcali nesne adlari (<c>dbo.vw_sales</c>): SQLite'ta sema yok.
    /// </summary>
    /// <remarks>
    /// Allow-list tek parcali adlar kullaniyor, ama <c>AllowListLoader</c> tek
    /// seviyeli <c>schema.object</c> bicimine de izin veriyor. Sema onekli bir
    /// ad gelirse SQLite bunu bir sema degil, ekli bir veritabani adi olarak
    /// yorumlar — sessizce yanlis yer.
    /// </remarks>
    public override void Visit(NamedTableReference node)
    {
        var identifiers = node.SchemaObject.Identifiers;

        if (identifiers.Count > 1)
        {
            var name = string.Join(".", identifiers.Select(i => i.Value));

            failures.Add(SqliteTranslationNotSupportedException.For(
                $"Cok parcali nesne adi ({name})",
                "SQLite'ta sema kavrami yok; onekli ad ekli bir veritabani adi " +
                "olarak yorumlanir ve sessizce yanlis nesneye gider."));
        }

        base.Visit(node);
    }

    public void ThrowIfUnsupported()
    {
        if (failures.Count > 0)
        {
            throw failures[0];
        }
    }
}
