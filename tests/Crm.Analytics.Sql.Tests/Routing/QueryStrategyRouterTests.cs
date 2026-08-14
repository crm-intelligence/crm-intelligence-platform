using Crm.Analytics.Sql.Audit;
using Crm.Analytics.Sql.Catalog;
using Crm.Analytics.Sql.Contracts;
using Crm.Analytics.Sql.Contracts.V2;
using Crm.Analytics.Sql.Parsing;
using Crm.Analytics.Sql.QueryBuilder;
using Crm.Analytics.Sql.Routing;

namespace Crm.Analytics.Sql.Tests.Routing;

public sealed class QueryStrategyRouterTests
{
    private static readonly MetricCatalogDocument Catalog =
        MetricCatalogLoader.FromJson(ContractResources.ReadOlistMetricCatalog());
    private static readonly AllowListDocument AllowList =
        AllowListLoader.FromJson(ContractResources.ReadOlistAllowList());

    [Fact]
    public void SupportedCanonicalRequest_SelectsDeterministicFastPath()
    {
        var request = Request();
        var builder = Builder();
        var expected = builder.Build(request);
        var deterministic = new DeterministicSqlQueryStrategy(builder);
        var router = new QueryStrategyRouter([deterministic], builder);

        var actual = router.Produce(request);

        Assert.True(actual.IsSuccessful, actual.Detail);
        Assert.Equal(expected.Sql, actual.Sql);
        Assert.Equal(expected.SourceObject, actual.SourceObject);
        Assert.Equal(expected.Parameters, actual.Parameters);
    }

    [Fact]
    public void MissingExplicitlySelectedStrategy_FailsClosedWithoutSql()
    {
        var builder = Builder();
        var router = new QueryStrategyRouter([], builder);

        var result = router.Produce(Request());

        Assert.False(result.IsSuccessful);
        Assert.Equal(ReasonCode.GR014, result.ReasonCode);
        Assert.Null(result.Sql);
        Assert.Empty(result.Parameters);
    }

    [Fact]
    public void AgenticRequired_DoesNotFallBackToDeterministicStrategy()
    {
        var builder = Builder();
        var deterministic = new StubStrategy(SqlQueryStrategyKind.Deterministic);
        var router = new QueryStrategyRouter([deterministic], builder);
        var v2 = CanonicalV1ToV2Adapter.Adapt(Request()) with
        {
            Comparisons =
            [
                new CanonicalPeriodComparison
                {
                    Kind = PeriodComparisonKind.PreviousPeriod
                }
            ]
        };

        var result = router.Produce(Request(), v2);

        Assert.False(result.IsSuccessful);
        Assert.Equal(ReasonCode.GR014, result.ReasonCode);
        Assert.False(deterministic.WasInvoked);
        Assert.Null(result.Sql);
    }

    [Fact]
    public void DuplicateStrategyKind_IsRejectedAtConfigurationTime()
    {
        var builder = Builder();

        var error = Assert.Throws<ArgumentException>(() =>
            new QueryStrategyRouter(
                [
                    new StubStrategy(SqlQueryStrategyKind.Deterministic),
                    new StubStrategy(SqlQueryStrategyKind.Deterministic)
                ],
                builder));

        Assert.Contains("birden fazla", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void StrategyOutput_AlwaysEntersCommonGuardrailPipeline()
    {
        var builder = Builder();
        var strategyRouter = new QueryStrategyRouter(
            [new StubStrategy(SqlQueryStrategyKind.Deterministic, "DELETE FROM dbo.anything;")],
            builder);
        var productionRouter = new SqlProductionRouter(
            strategyRouter,
            AllowList,
            new TSqlParserFactory(),
            new NullAuditWriter());

        var routing = productionRouter.Produce(
            Request(), UserDataScope.Unrestricted);

        Assert.Equal(GuardrailDecision.Rejected, routing.Guardrail.Decision);
        Assert.Null(routing.Guardrail.Sql);
        Assert.Contains(routing.Guardrail.Checks, check =>
            check.Name == GuardrailCheckName.SelectOnly
            && check.Outcome == CheckOutcome.Failed);
    }

    private static DeterministicQueryBuilder Builder() =>
        new(new TSqlParserFactory(), Catalog, AllowList);

    private static CanonicalRequest Request() => new()
    {
        RequestId = "request-strategy",
        ConversationId = "conversation-strategy",
        Source = DataSource.Dwh,
        Intent = RequestIntent.Breakdown,
        Metrics = ["item_sales"],
        Dimensions = ["customer_state"],
        DateRange = new DateRangeSpec
        {
            Kind = DateRangeKind.Absolute,
            From = new DateOnly(2018, 1, 1),
            To = new DateOnly(2018, 12, 31)
        },
        Confidence = 1
    };

    private sealed class StubStrategy(
        SqlQueryStrategyKind kind,
        string? sql = null) : ISqlQueryStrategy
    {
        public SqlQueryStrategyKind Kind => kind;

        public bool WasInvoked { get; private set; }

        public Task<QueryBuildResult> ProduceAsync(
            SqlQueryStrategyContext context,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            WasInvoked = true;
            var result = sql is null
                ? QueryBuildResult.Failure(
                    ReasonCode.GR014, "Stub strategy must not be invoked.")
                : QueryBuildResult.Success(sql, [], "candidate");
            return Task.FromResult(result);
        }
    }

    private sealed class NullAuditWriter : IDecisionAuditWriter
    {
        public void Write(DecisionAuditRecord record)
        {
        }
    }
}
