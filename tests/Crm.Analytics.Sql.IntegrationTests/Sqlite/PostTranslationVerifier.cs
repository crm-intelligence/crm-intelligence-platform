using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Crm.Analytics.Sql.IntegrationTests.Sqlite;

/// <summary>
/// Ceviriden SONRA, SQL uretilmeden once AST'yi son bir kez gezer ve SQLite'in
/// calistiramayacagi bir kalinti olup olmadigini kontrol eder.
/// </summary>
/// <remarks>
/// Bu sinifin varlik sebebi: ceviri kurallarindan biri sessizce atlanirsa
/// (ornek: yeni bir fonksiyon allow-list'e eklendi ama harness'a eklenmedi)
/// hatanin SQLite'in belirsiz bir calisma zamani mesajina degil, acik bir
/// harness istisnasina donusmesi.
/// </remarks>
internal sealed class PostTranslationVerifier : TSqlFragmentVisitor
{
    private readonly List<SqliteTranslationNotSupportedException> failures = [];

    /// <summary>
    /// Ceviriden sonra kalan her fonksiyon adi ya SQLite'ta yerlesik olmali ya
    /// da UDF olarak kaydedilmis olmali.
    /// </summary>
    public override void Visit(FunctionCall node)
    {
        var name = node.FunctionName.Value;

        if (!TSqlFunctionSets.ExecutableAfterTranslation.Contains(name))
        {
            failures.Add(SqliteTranslationNotSupportedException.For(
                $"{name}()",
                "ceviriden sonra SQLite'ta ne yerlesik ne de kayitli bir UDF. " +
                "Allow-list'e yeni bir fonksiyon eklendiyse TSqlFunctionSets " +
                "icindeki uc kumeden birine de eklenmelidir.",
                "Sqlite/TSqlFunctionSets.cs"));
        }

        base.Visit(node);
    }

    /// <summary>
    /// <c>TOP</c> kaldirilmis olmali; kalmissa <c>LIMIT</c>'e cevrilmemis
    /// demektir ve SQLite sozdizimi hatasi verir.
    /// </summary>
    public override void Visit(TopRowFilter node)
    {
        failures.Add(SqliteTranslationNotSupportedException.For(
            "TOP",
            "ceviriden sonra AST'de kalmis. Ic bir sorgu blogunda TOP varsa " +
            "LIMIT'e cevrilemez; guardrail limiti yalnizca en dis bloklara " +
            "eklemeliydi."));

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
