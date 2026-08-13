using Crm.Analytics.Sql.Contracts;
using Crm.Analytics.Sql.Guardrail.Visitors;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Crm.Analytics.Sql.Guardrail.Checks;

/// <summary>
/// Kontrol 7: referans edilen tum kolonlar allow-list'te olmalidir.
/// </summary>
/// <remarks>
/// <para>
/// Kolonlar SELECT listesiyle sinirli degil, agacin tamamindan toplanir (WHERE, GROUP BY,
/// ORDER BY, HAVING, JOIN ON dahil).
/// </para>
/// <para>
/// Sorgunun kendi urettigi adlar (SELECT alias'lari, CTE ve turetilmis tablo kolonlari)
/// muaf tutulur: <c>SUM(price) AS tutar</c> ifadesindeki <c>tutar</c> veritabaninda bir kolon
/// degildir ve allow-list'te aranirsa mesru sorgular reddedilirdi.
/// </para>
/// </remarks>
public sealed class AllowListColumnsCheck : IGuardrailCheck
{
    public GuardrailCheckName Name => GuardrailCheckName.AllowListColumns;

    public CheckResult Execute(GuardrailContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var collector = TableAndColumnCollector.CollectFrom(context.RequireFragment());

        foreach (var column in collector.Columns)
        {
            var failure = Validate(context, collector, column);

            if (failure is not null)
            {
                return failure;
            }
        }

        return CheckResult.Pass(Name);
    }

    private CheckResult? Validate(
        GuardrailContext context,
        TableAndColumnCollector collector,
        ColumnReferenceExpression column)
    {
        // Sahte kolonlar ($IDENTITY, $ROWGUID, $ACTION) allow-list disi kabul edilir:
        // sema kesfine ve satir kimligine erisim saglayabilirler.
        if (column.ColumnType is not (ColumnType.Regular or ColumnType.Wildcard))
        {
            return CheckResult.Fail(Name, ReasonCode.GR004,
                $"Desteklenmeyen kolon tipi: {column.ColumnType}.");
        }

        // Wildcard (COUNT(*) icindeki '*') bir kolon adi tasimaz. SELECT listesindeki
        // yildiz zaten NoStarSelect tarafindan reddedilir; COUNT(*) mesrudur.
        if (column.ColumnType == ColumnType.Wildcard || column.MultiPartIdentifier is null)
        {
            return null;
        }

        var identifiers = column.MultiPartIdentifier.Identifiers;

        if (identifiers.Count == 0)
        {
            return null;
        }

        var columnName = identifiers[^1].Value;

        // Sorgunun kendi urettigi ad.
        if (collector.DerivedNames.Contains(columnName))
        {
            return null;
        }

        return identifiers.Count > 1
            ? ValidateQualified(context, collector, identifiers[^2].Value, columnName)
            : ValidateUnqualified(context, collector, columnName);
    }

    private CheckResult? ValidateQualified(
        GuardrailContext context,
        TableAndColumnCollector collector,
        string qualifier,
        string columnName)
    {
        // CTE veya turetilmis tablo alias'i ile nitelendirilmis kolon: kaynak obje bir
        // veritabani objesi degil, kolonlar govdede zaten dogrulanir.
        if (collector.DerivedNames.Contains(qualifier))
        {
            return null;
        }

        if (!collector.QualifierToObject.TryGetValue(qualifier, out var objectName))
        {
            // Sorguda tanimli olmayan bir nitelendirici. Fail-closed: tahmin etmiyoruz.
            return CheckResult.Fail(Name, ReasonCode.GR004,
                $"Tanimsiz nitelendirici: '{qualifier}'.");
        }

        var allowed = context.AllowList.FindSqlObject(objectName);

        if (allowed is null || !allowed.HasColumn(columnName))
        {
            return CheckResult.Fail(Name, ReasonCode.GR004,
                $"Allow-list disi kolon: '{objectName}.{columnName}'.");
        }

        return null;
    }

    private CheckResult? ValidateUnqualified(
        GuardrailContext context,
        TableAndColumnCollector collector,
        string columnName)
    {
        // Nitelendirilmemis kolon: sorgudaki objelerden en az birinde bulunmasi yeterli.
        // Hangi objeye ait oldugunu tahmin etmeye calismiyoruz; onemli olan kolonun izinli
        // bir objeye ait olmasi. Yanlis objeye ait olmasi durumunda sorgu zaten T-SQL
        // tarafinda gecersizdir.
        var existsInSomeObject = collector.Tables
            .Select(table => context.AllowList.FindSqlObject(table.ObjectName))
            .Any(allowed => allowed?.HasColumn(columnName) == true);

        if (!existsInSomeObject)
        {
            return CheckResult.Fail(Name, ReasonCode.GR004,
                $"Allow-list disi kolon: '{columnName}'.");
        }

        return null;
    }
}
