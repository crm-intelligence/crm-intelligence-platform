using Crm.Analytics.Sql.Contracts;
using Crm.Analytics.Sql.Contracts.V2;
using Crm.Analytics.Sql.QueryBuilder;

namespace Crm.Analytics.Sql.Routing;

/// <summary>
/// Canonical talep icin tek SQL uretim strategy'sini backend-owned kurallarla secer.
/// </summary>
internal sealed record SqlQueryStrategyContext(
    CanonicalRequest V1Request,
    CanonicalQuery Query,
    QueryStrategyDecision Decision);

internal sealed class QueryStrategyRouter
{
    private readonly IReadOnlyDictionary<SqlQueryStrategyKind, ISqlQueryStrategy> strategies;
    private readonly QueryCapabilityAnalyzer capabilityAnalyzer;

    public QueryStrategyRouter(
        IEnumerable<ISqlQueryStrategy> strategies,
        DeterministicQueryBuilder deterministicBuilder)
        : this(
            strategies,
            new QueryCapabilityAnalyzer(
                deterministicBuilder.Catalog,
                deterministicBuilder.Capabilities))
    {
    }

    internal QueryStrategyRouter(
        IEnumerable<ISqlQueryStrategy> strategies,
        QueryCapabilityAnalyzer capabilityAnalyzer)
    {
        ArgumentNullException.ThrowIfNull(strategies);
        ArgumentNullException.ThrowIfNull(capabilityAnalyzer);

        var configured = strategies.ToArray();
        var duplicate = configured.GroupBy(strategy => strategy.Kind)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new ArgumentException(
                $"Ayni SQL query strategy kind birden fazla kaydedilemez: '{duplicate.Key}'.",
                nameof(strategies));
        }

        this.strategies = configured.ToDictionary(strategy => strategy.Kind);
        this.capabilityAnalyzer = capabilityAnalyzer;
    }

    public QueryBuildResult Produce(CanonicalRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return ProduceAsync(request, CancellationToken.None).GetAwaiter().GetResult();
    }

    internal QueryBuildResult Produce(CanonicalRequest request, CanonicalQuery query)
    {
        return ProduceAsync(request, query, CancellationToken.None).GetAwaiter().GetResult();
    }

    internal Task<QueryBuildResult> ProduceAsync(
        CanonicalRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return ProduceAsync(request, CanonicalV1ToV2Adapter.Adapt(request), cancellationToken);
    }

    internal Task<QueryBuildResult> ProduceAsync(
        CanonicalRequest request,
        CanonicalQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();

        var decision = capabilityAnalyzer.Analyze(query);
        var requiredStrategy = decision.Outcome switch
        {
            QueryCapabilityOutcome.Deterministic => SqlQueryStrategyKind.Deterministic,
            QueryCapabilityOutcome.AgenticRequired => SqlQueryStrategyKind.Agentic,
            QueryCapabilityOutcome.Unsupported => (SqlQueryStrategyKind?)null,
            _ => null
        };

        if (requiredStrategy is null)
        {
            return Task.FromResult(QueryBuildResult.Failure(
                decision.FailureReasonCode,
                $"Canonical V2 capability sonucu desteklenmiyor: {string.Join(", ", decision.Reasons)}."));
        }

        if (!strategies.TryGetValue(requiredStrategy.Value, out var strategy))
        {
            return Task.FromResult(QueryBuildResult.Failure(
                decision.FailureReasonCode == ReasonCode.None
                    ? ReasonCode.GR014
                    : decision.FailureReasonCode,
                $"Capability karari '{decision.Outcome}' ancak '{requiredStrategy}' strategy'si " +
                "production runtime'da kayitli degil."));
        }

        return strategy.ProduceAsync(
            new SqlQueryStrategyContext(request, query, decision),
            cancellationToken);
    }
}
