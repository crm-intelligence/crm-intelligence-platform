using Crm.Analytics.Sql.Guardrail.Visitors;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Crm.Analytics.Sql.Guardrail.Mutation;

/// <summary>
/// Disa cikan sorgu bloklarina satir siniri (<c>TOP</c>) uygular.
/// </summary>
/// <remarks>
/// <para>
/// Limit <b>yalnizca en dis bloklara</b> eklenir. Alt sorguya veya CTE govdesine <c>TOP</c>
/// eklemek sonucu bozar: agregasyon kismi veri uzerinden hesaplanir ve kullanici yanlis
/// toplam gorur. Bu, guvenlik degil DOGRULUK gereksinimidir.
/// </para>
/// <para>
/// Mevcut bir <c>TOP</c> sinirin uzerindeyse dusurulur (reddedilmez): kullanicinin daha
/// yuksek bir deger istemesi limiti yukseltmez, ancak mesru talebi tamamen bosa cikarmak da
/// gerekmez.
/// </para>
/// <para>
/// <b>Bilinen sinirlama:</b> <c>ORDER BY</c> bulunmayan bir sorguda <c>TOP</c> hangi satirlarin
/// donecegini belirsiz birakir. Guardrail siralama EKLEMEZ — hangi siralamanin dogru oldugu
/// bir is kararidir ve varsayim yapmak yanlis veri gostermek olurdu.
/// </para>
/// </remarks>
public sealed class RowLimitInjector
{
    public sealed record Outcome(int InjectedCount, int LoweredCount);

    public Outcome Apply(GuardrailContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var maxRows = context.AllowList.MaxRows;
        var defaultRows = context.AllowList.DefaultRows;
        var injected = 0;
        var lowered = 0;

        foreach (var block in QueryStructure.OutermostBlocks(context.RequireFragment()))
        {
            if (block.TopRowFilter is null)
            {
                block.TopRowFilter = new TopRowFilter
                {
                    Expression = new IntegerLiteral { Value = defaultRows.ToString() },
                    Percent = false,
                    WithTies = false
                };

                injected++;
                continue;
            }

            if (IsAboveLimit(block.TopRowFilter, maxRows))
            {
                block.TopRowFilter.Expression = new IntegerLiteral { Value = maxRows.ToString() };
                block.TopRowFilter.Percent = false;
                lowered++;
            }
        }

        return new Outcome(injected, lowered);
    }

    private static bool IsAboveLimit(TopRowFilter filter, int maxRows)
    {
        // Yuzde bazli TOP satir sayisini garanti etmez; her zaman sabit degere cevrilir.
        if (filter.Percent)
        {
            return true;
        }

        // Sabit olmayan bir TOP ifadesi (degisken, alt sorgu) degerlendirilemez;
        // fail-closed davranarak sinira cekilir.
        if (filter.Expression is not IntegerLiteral literal
            || !int.TryParse(literal.Value, out var requested))
        {
            return true;
        }

        return requested > maxRows;
    }
}
