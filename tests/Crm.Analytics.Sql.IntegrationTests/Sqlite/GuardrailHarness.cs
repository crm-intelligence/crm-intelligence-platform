using Crm.Analytics.Sql.Audit;
using Crm.Analytics.Sql.Contracts;
using Crm.Analytics.Sql.Guardrail;
using Crm.Analytics.Sql.Parsing;
using Crm.Analytics.Sql.Service;

namespace Crm.Analytics.Sql.IntegrationTests.Sqlite;

/// <summary>
/// Uretim SQL uretim hattini test icinden cagirmak icin ince sarmalayici.
/// </summary>
/// <remarks>
/// <c>Crm.Analytics.Sql.Tests/Demo/GuardrailDemoTests</c> icindeki
/// <c>Produce</c> / <c>RunGuardrail</c> desenini tekrar eder — kasitli olarak
/// ayni: entegrasyon testleri, birim testlerinin dogruladigi ayni yolu
/// calistirmali, farkli bir yolu degil.
/// </remarks>
internal static class GuardrailHarness
{
    /// <summary>
    /// Veri setinin son gunu 2018-10-17; "2018" gibi goreli ifadelerin veri
    /// icinde kalmasi icin bugun tarihi sabit tutulur.
    /// </summary>
    public static DateOnly Today { get; } = new(2026, 7, 30);

    /// <summary>
    /// Dogal dil talebini uretim hattindan gecirir (Query Builder + guardrail).
    /// </summary>
    public static SqlProductionResponse Produce(
        string prompt,
        UserDataScope scope,
        CanonicalRequest? previous = null)
    {
        return SqlProductionFactory
            .CreateForOlist(new DiscardingAuditWriter())
            .Produce(new SqlProductionRequest
            {
                Prompt = prompt,
                RequestId = $"req_{prompt.Length:D3}",
                ConversationId = "conv_integration",
                Scope = scope,
                Today = Today,
                PreviousRequest = previous,
                UserId = "user_integration"
            });
    }

    /// <summary>
    /// Hazir SQL'i dogrudan guardrail'a verir: bir dil modelinin uretecegi ham
    /// taslagi temsil eder.
    /// </summary>
    public static GuardrailResult RunGuardrail(string sql, UserDataScope scope)
    {
        var context = new GuardrailContext(
            sql,
            OlistCatalog.AllowList,
            scope,
            new TSqlParserFactory(),
            request: new CanonicalRequest
            {
                RequestId = "req_integration_guardrail",
                ConversationId = "conv_integration",
                Intent = RequestIntent.Breakdown,
                Metrics = ["item_sales"],
                DateRange = new DateRangeSpec
                {
                    Kind = DateRangeKind.Absolute,
                    From = new DateOnly(2018, 1, 1),
                    To = new DateOnly(2018, 12, 31)
                }
            });

        return GuardrailFactory.Create().Execute(context);
    }

    private sealed class DiscardingAuditWriter : IDecisionAuditWriter
    {
        public void Write(DecisionAuditRecord record)
        {
            // Entegrasyon testlerinde denetim kaydi dogrulanmiyor; o is
            // Crm.Analytics.Sql.Tests/Audit altinda yapiliyor.
        }
    }
}
