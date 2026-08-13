using Crm.Analytics.Sql.Contracts;
using Crm.Analytics.Sql.Guardrail.Visitors;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Crm.Analytics.Sql.Guardrail.Checks;

/// <summary>
/// Kontrol 8: yasakli kisisel veri kolonlari hicbir yerde kullanilamaz.
/// </summary>
/// <remarks>
/// Proje dokumani bu kontrolu "deniedColumns listesinden hicbiri <b>secilmemis</b>" olarak
/// tanimliyordu; yani yalnizca SELECT listesine bakiyordu. Bu bir bosluktu:
/// <c>SELECT region, COUNT(*) FROM vw_customer WHERE email LIKE 'ahmet%' GROUP BY region</c>
/// sorgusu PII'yi SECMIYOR ama SIZDIRIYOR — sonuc, aranan e-postanin varligini ve dagilimini
/// aciga cikarir. Bu yuzden kontrol agacin tamamina uygulanir.
/// </remarks>
public sealed class NoDeniedPiiColumnsCheck : IGuardrailCheck
{
    public GuardrailCheckName Name => GuardrailCheckName.NoDeniedPiiColumns;

    public CheckResult Execute(GuardrailContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var collector = TableAndColumnCollector.CollectFrom(context.RequireFragment());

        foreach (var column in collector.Columns)
        {
            if (column.ColumnType != ColumnType.Regular || column.MultiPartIdentifier is null)
            {
                continue;
            }

            var identifiers = column.MultiPartIdentifier.Identifiers;

            if (identifiers.Count == 0)
            {
                continue;
            }

            var columnName = identifiers[^1].Value;

            // Yalnizca kolon REFERANSLARI denetlenir, alias'lar denetlenmez:
            // 'SELECT price AS email' bir PII erisimi degildir, yalnizca kotu bir adlandirmadir.
            // Tersi olan 'SELECT email AS x' ise burada yakalanir.
            if (context.AllowList.IsDeniedColumn(columnName))
            {
                return CheckResult.Fail(Name, ReasonCode.GR005,
                    $"Yasakli kisisel veri kolonu: '{columnName}'.");
            }
        }

        return CheckResult.Pass(Name);
    }
}
