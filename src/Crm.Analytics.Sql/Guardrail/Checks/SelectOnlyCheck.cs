using Crm.Analytics.Sql.Contracts;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Crm.Analytics.Sql.Guardrail.Checks;

/// <summary>
/// Kontrol 3: yalnizca <c>SELECT</c> ifadelerine izin verir.
/// </summary>
/// <remarks>
/// <para>
/// Kok ifadenin <see cref="SelectStatement"/> olmasi zorunludur. DML (INSERT/UPDATE/DELETE/MERGE),
/// DDL (DROP/ALTER/CREATE/TRUNCATE), izin ifadeleri (GRANT) ve <c>EXEC</c> / <c>sp_executesql</c>
/// bu kontrolde reddedilir: hicbiri <see cref="SelectStatement"/> degildir.
/// </para>
/// <para>
/// Bu kontrol tek basina yeterli DEGILDIR. Bir SELECT'in <b>icinde</b> yer alabilen tehlikeli
/// yapilar (<c>OPENROWSET</c>, <c>OPENJSON</c>, inline tablo fonksiyonu, <c>APPLY</c>) gramere
/// uygun oldugu icin buraya takilmaz; onlari <see cref="GuardrailCheckName.NodeTypeWhitelist"/>
/// reddeder.
/// </para>
/// </remarks>
public sealed class SelectOnlyCheck : IGuardrailCheck
{
    public GuardrailCheckName Name => GuardrailCheckName.SelectOnly;

    public CheckResult Execute(GuardrailContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var fragment = context.RequireFragment();

        if (fragment is not TSqlScript script)
        {
            return CheckResult.Fail(Name, ReasonCode.GR001,
                $"Beklenmeyen kok dugum tipi: {fragment.GetType().Name}");
        }

        // SingleStatement kontrolu bu kontrolden ONCE calisir, dolayisiyla tek ifade
        // garantilidir. Yine de savunma amacli tekrar dogrulaniyor: sirasi bozulursa
        // sessizce ilk ifadeye bakip gerisini gormemek olurdu.
        var statements = script.Batches.SelectMany(batch => batch.Statements).ToArray();

        if (statements.Length != 1)
        {
            return CheckResult.Fail(Name, ReasonCode.GR002,
                $"Tek ifade beklenirken {statements.Length} ifade bulundu.");
        }

        if (statements[0] is not SelectStatement select)
        {
            return CheckResult.Fail(Name, ReasonCode.GR001,
                $"SELECT disi ifade: {statements[0].GetType().Name}");
        }


        if (select.Into is not null)
        {
            return CheckResult.Fail(Name, ReasonCode.GR001,
                "SELECT INTO veri yazan bir ifade oldugu icin izinli degildir.");
        }

        return CheckResult.Pass(Name);
    }
}
