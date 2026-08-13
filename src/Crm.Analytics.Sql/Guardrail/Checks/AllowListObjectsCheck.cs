using Crm.Analytics.Sql.Contracts;
using Crm.Analytics.Sql.Guardrail.Visitors;

namespace Crm.Analytics.Sql.Guardrail.Checks;

/// <summary>
/// Kontrol 6: tum FROM/JOIN hedefleri allow-list'te olmalidir.
/// </summary>
/// <remarks>
/// Karsilastirma <b>tam ad</b> ile yapilir. Yalnizca son parcayi almak
/// (<c>baska_sema.vw_sales</c> -> <c>vw_sales</c>) allow-list'i atlatmanin en kolay yolu olurdu.
/// CTE adlari obje sayilmaz; onlar sorgunun kendi urettigi adlardir.
/// </remarks>
public sealed class AllowListObjectsCheck : IGuardrailCheck
{
    public GuardrailCheckName Name => GuardrailCheckName.AllowListObjects;

    public CheckResult Execute(GuardrailContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var collector = TableAndColumnCollector.CollectFrom(context.RequireFragment());
        string? verifiedPhysicalObject = null;

        foreach (var table in collector.Tables)
        {
            // CTE referansi: sorgunun kendi tanimladigi ad, veritabaninda obje degil.
            if (collector.DerivedNames.Contains(table.ObjectName))
            {
                continue;
            }

            var canonicalPhysicalObject =
                context.AllowList.ResolveCanonicalSqlObject(table.ObjectName);
            if (canonicalPhysicalObject is null)
            {
                return CheckResult.Fail(Name, ReasonCode.GR003,
                    $"Allow-list disi obje: '{table.ObjectName}'.");
            }

            verifiedPhysicalObject ??= canonicalPhysicalObject;
        }

        if (verifiedPhysicalObject is not null)
        {
            context.RecordVerifiedPhysicalObject(verifiedPhysicalObject);
        }

        return CheckResult.Pass(Name);
    }
}
