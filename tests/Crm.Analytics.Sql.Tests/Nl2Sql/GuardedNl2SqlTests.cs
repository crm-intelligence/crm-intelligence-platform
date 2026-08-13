using Crm.Analytics.Sql.Audit;
using Crm.Analytics.Sql.Catalog;
using Crm.Analytics.Sql.Contracts;
using Crm.Analytics.Sql.Parsing;
using Crm.Analytics.Sql.QueryBuilder;
using Crm.Analytics.Sql.Routing;

namespace Crm.Analytics.Sql.Tests.Nl2Sql;

/// <summary>Regression tests for the fail-closed, no-model-SQL boundary.</summary>
public sealed class GuardedNl2SqlTests
{
    private static readonly MetricCatalogDocument Catalog =
        MetricCatalogLoader.FromJson(ContractResources.ReadOlistMetricCatalog());
    private static readonly AllowListDocument AllowList =
        AllowListLoader.FromJson(ContractResources.ReadOlistAllowList());

    [Fact]
    public void DirectRouterFailure_StaysOnDeterministicPath()
    {
        var audit = new RecordingAuditWriter();
        var router = new SqlProductionRouter(
            new DeterministicQueryBuilder(new TSqlParserFactory(), Catalog, AllowList),
            AllowList,
            new TSqlParserFactory(),
            audit);

        var routing = router.Produce(Request(["unknown_metric"]),
            UserDataScope.ForRegions("SP"), rawPrompt: "raw private prompt", userId: "pii-user");

        Assert.Equal(ProductionPath.QueryBuilder, routing.Path);
        Assert.Null(routing.Guardrail.Sql);
        var serialized = System.Text.Json.JsonSerializer.Serialize(Assert.Single(audit.Records));
        Assert.DoesNotContain("raw private prompt", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("pii-user", serialized, StringComparison.Ordinal);
    }

    private static CanonicalRequest Request(IReadOnlyList<string> metrics) => new()
    {
        RequestId = "request-1",
        ConversationId = "conversation-1",
        Source = DataSource.Dwh,
        Intent = RequestIntent.SingleValue,
        Metrics = metrics,
        DateRange = DateRangeSpec.NotApplicable,
        Confidence = 1
    };

    private sealed class RecordingAuditWriter : IDecisionAuditWriter
    {
        public List<DecisionAuditRecord> Records { get; } = [];
        public void Write(DecisionAuditRecord record) => Records.Add(record);
    }
}
