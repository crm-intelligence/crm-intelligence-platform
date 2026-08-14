using Crm.Analytics.Sql.QueryBuilder;

namespace Crm.Analytics.Sql.Routing;

/// <summary>
/// Bir canonical talep icin onaylanmamis SQL ve parametre uretim yaklasimini temsil eder.
/// </summary>
/// <remarks>
/// Strategy ciktisi calistirilabilir plan degildir. Yetkilendirme, data scope, son SQL
/// guvenlik karari ve execution bu sinirin disindadir; basarili cikti ortak guardrail
/// hattindan gecmek zorundadir.
/// </remarks>
internal enum SqlQueryStrategyKind
{
    Deterministic,
    Agentic
}

internal interface ISqlQueryStrategy
{
    SqlQueryStrategyKind Kind { get; }

    Task<QueryBuildResult> ProduceAsync(
        SqlQueryStrategyContext context,
        CancellationToken cancellationToken);
}
