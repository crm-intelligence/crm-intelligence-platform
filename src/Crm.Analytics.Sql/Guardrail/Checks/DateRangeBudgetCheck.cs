using Crm.Analytics.Sql.Contracts;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Crm.Analytics.Sql.Guardrail.Checks;

/// <summary>
/// Kontrol 11: tarih araligi ust siniri.
/// </summary>
/// <remarks>
/// <para>
/// <c>TOP</c> DONEN satiri sinirlar, TARANAN satiri sinirlamaz: 10 yillik bir aralik uzerinde
/// <c>TOP 100</c> ile calisan bir agregasyon yine tum veriyi okur. Bu yuzden aralik ayrica
/// butcelenir.
/// </para>
/// <para>
/// Aralik iki kaynaktan okunur: Canonical Request varsa oradaki cozumlenmis tarihler
/// (Query Builder yolu), yoksa savunma amacli SQL tarih literal'leri.
/// </para>
/// <para>
/// <b>Bilincli sinirlama:</b> hic tarih filtresi olmayan sorgu reddedilmez.
/// <c>SELECT COUNT(*) FROM vw_sales</c> mesru bir taleptir ve reddetmek kullanilabilirligi
/// gereksiz kirardi; bu durumda koruma <c>RowLimit</c> ve komut timeout'una kalir.
/// </para>
/// </remarks>
public sealed class DateRangeBudgetCheck : IGuardrailCheck
{
    public GuardrailCheckName Name => GuardrailCheckName.DateRangeBudget;

    public CheckResult Execute(GuardrailContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var budget = context.AllowList.MaxDateRangeDays;
        var requestSpan = ReadRequestSpan(context);

        if (requestSpan is { } spanFromRequest && spanFromRequest > budget)
        {
            return CheckResult.Fail(Name, ReasonCode.GR013,
                $"Talep edilen aralik {spanFromRequest} gun, butce {budget} gun.");
        }

        var literalSpan = ReadLiteralSpan(context);

        if (literalSpan is { } spanFromLiterals && spanFromLiterals > budget)
        {
            return CheckResult.Fail(Name, ReasonCode.GR013,
                $"Sorgudaki tarih literal'leri {spanFromLiterals} gunluk aralik gosteriyor, butce {budget} gun.");
        }

        return CheckResult.Pass(Name);
    }

    private static int? ReadRequestSpan(GuardrailContext context)
    {
        var range = context.Request?.DateRange;

        if (range?.From is not { } from || range.To is not { } to)
        {
            return null;
        }

        return to.DayNumber - from.DayNumber;
    }

    /// <summary>
    /// SQL'deki tarih benzeri metin literal'lerinin en kucugu ile en buyugu arasindaki fark.
    /// </summary>
    private static int? ReadLiteralSpan(GuardrailContext context)
    {
        var collector = new DateLiteralCollector();
        context.RequireFragment().Accept(collector);

        if (collector.Dates.Count < 2)
        {
            return null;
        }

        var minimum = collector.Dates.Min();
        var maximum = collector.Dates.Max();

        return maximum.DayNumber - minimum.DayNumber;
    }

    private sealed class DateLiteralCollector : TSqlFragmentVisitor
    {
        private readonly List<DateOnly> dates = [];

        public IReadOnlyList<DateOnly> Dates => dates;

        public override void Visit(StringLiteral node)
        {
            // Yalnizca tarih olarak yorumlanabilen literal'ler dikkate alinir; siradan metinler
            // (bolge adi, kategori) atlanir. InvariantCulture bilincli: sunucu kulturune bagli
            // yorumlama, ayni sorgunun farkli ortamlarda farkli butcelenmesine yol acardi.
            if (DateOnly.TryParse(
                    node.Value,
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None,
                    out var parsed))
            {
                dates.Add(parsed);
            }
        }
    }
}
