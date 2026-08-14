using System.Collections.Immutable;
using System.Reflection;
using Crm.Analytics.Sql.Agentic;
using Crm.Analytics.Sql.Audit;
using Crm.Analytics.Sql.Catalog;
using Crm.Analytics.Sql.Contracts;
using Crm.Analytics.Sql.Contracts.V2;
using Crm.Analytics.Sql.Parsing;
using Crm.Analytics.Sql.QueryBuilder;
using Crm.Analytics.Sql.Routing;
using Crm.Analytics.Sql.SemanticContext;
using Crm.Analytics.Sql.Service;

namespace Crm.Analytics.Sql.Tests.Routing;

public sealed class AgenticSqlStrategyPipelineTests
{
    private const string ValidPeriodComparisonCandidate =
        """
        SELECT
            customer_state,
            SUM(CASE
                WHEN order_purchase_timestamp >= '2018-07-01' THEN price
                ELSE 0
            END) AS current_value,
            SUM(CASE
                WHEN order_purchase_timestamp < '2018-07-01' THEN price
                ELSE 0
            END) AS previous_value
        FROM mart.vw_sales
        WHERE order_purchase_timestamp >= '2018-01-01'
          AND order_purchase_timestamp <= '2018-12-31'
        GROUP BY customer_state
        """;

    private static readonly SemanticCatalogRegistry Registry =
        SemanticCatalogRegistry.CreateDefault();
    private static readonly SemanticCatalogSource Dwh = Registry.GetRequired(DataSource.Dwh);

    [Fact]
    public async Task AgenticRequired_WithTestStrategy_SelectsAgenticStrategy()
    {
        var setup = CreateRouter(ValidPeriodComparisonCandidate);

        var result = await setup.StrategyRouter.ProduceAsync(
            Request(), AgenticQuery(), CancellationToken.None);

        Assert.True(result.IsSuccessful, result.Detail);
        Assert.Equal(1, setup.Model.InvocationCount);
        Assert.Equal("vw_sales", result.SourceObject);
        Assert.Empty(result.Parameters);
    }

    [Fact]
    public async Task AgenticStrategy_RejectsDeterministicDecisionWithoutInvokingModel()
    {
        var setup = CreateRouter(ValidPeriodComparisonCandidate);
        var deterministicQuery = CanonicalV1ToV2Adapter.Adapt(Request());
        var decision = new QueryCapabilityAnalyzer(
            Dwh.Catalog,
            DeterministicQueryCapabilities.Current).Analyze(deterministicQuery);
        var context = new SqlQueryStrategyContext(Request(), deterministicQuery, decision);

        var result = await setup.AgenticStrategy.ProduceAsync(
            context, CancellationToken.None);

        Assert.False(result.IsSuccessful);
        Assert.Equal(ReasonCode.GR014, result.ReasonCode);
        Assert.Equal(0, setup.Model.InvocationCount);
    }

    [Fact]
    public async Task ModelReceivesCanonicalV2AndOnlyMinimumRelevantSemanticContext()
    {
        var setup = CreateRouter(ValidPeriodComparisonCandidate);
        var query = AgenticQuery();

        var result = await setup.StrategyRouter.ProduceAsync(
            Request(), query, CancellationToken.None);

        Assert.True(result.IsSuccessful, result.Detail);
        var modelRequest = Assert.IsType<SqlReasoningRequest>(setup.Model.LastRequest);
        Assert.Same(query, modelRequest.Query);
        Assert.Equal(["item_sales"],
            modelRequest.InitialSemanticContext.Metrics.Select(metric => metric.Key).ToArray());
        Assert.Contains(modelRequest.InitialSemanticContext.Dimensions,
            dimension => dimension.Key == "customer_state");
        Assert.DoesNotContain(modelRequest.InitialSemanticContext.Metrics,
            metric => metric.Key == "payment_total");
        Assert.DoesNotContain(modelRequest.InitialSemanticContext.Dimensions,
            dimension => dimension.Key == "payment_type");
        var source = Assert.Single(modelRequest.InitialSemanticContext.Sources);
        Assert.Equal("mart.vw_sales", source.ApprovedPhysicalObject);
        Assert.DoesNotContain("payment_value", source.ApprovedColumns);
        Assert.Contains(
            QueryCapabilityReasonCode.PeriodComparisonRequiresAgentic,
            modelRequest.AllowedCapabilities.CapabilityReasons);
        Assert.Equal(0, modelRequest.AllowedCapabilities.MaximumExecutableJoins);
        Assert.False(modelRequest.AllowedCapabilities.JoinExecutionEnabled);
        Assert.Equal(1, modelRequest.Constraints.MaximumCandidates);
        Assert.True(modelRequest.Constraints.BackendOwnsDataScope);
        Assert.True(modelRequest.Constraints.BackendOwnsLiteralParameterization);
        Assert.Equal(
            Enum.GetValues<SqlSemanticToolKind>(),
            modelRequest.AvailableSemanticTools.ToArray());
    }

    [Fact]
    public async Task ValidCandidate_EntersCommonGuardrailsAndGetsBackendScopeAndParameters()
    {
        var setup = CreateRouter(ValidPeriodComparisonCandidate);

        var routing = await setup.ProductionRouter.ProduceAsync(
            Request(),
            AgenticQuery(),
            UserDataScope.ForRegions("SP"),
            CancellationToken.None);

        Assert.Equal(GuardrailDecision.Accepted, routing.Guardrail.Decision);
        Assert.All(routing.Guardrail.Checks,
            check => Assert.Equal(CheckOutcome.Passed, check.Outcome));
        Assert.Contains("@scope0", routing.Guardrail.Sql!, StringComparison.Ordinal);
        Assert.DoesNotContain("'2018-07-01'", routing.Guardrail.Sql!, StringComparison.Ordinal);
        Assert.Contains(routing.Guardrail.Parameters,
            parameter => parameter.Name == "@scope0" && parameter.Raw == "SP");
        Assert.Contains(routing.Guardrail.Parameters,
            parameter => parameter.Raw == "2018-07-01");
        Assert.Equal(1, setup.Model.InvocationCount);
    }

    [Theory]
    [InlineData("DELETE FROM mart.vw_sales;", GuardrailCheckName.SelectOnly, ReasonCode.GR001)]
    [InlineData("SELECT confidential_value FROM dbo.secret_table;", GuardrailCheckName.AllowListObjects, ReasonCode.GR003)]
    [InlineData("SELECT * FROM mart.vw_sales;", GuardrailCheckName.NoStarSelect, ReasonCode.GR004)]
    [InlineData("SELECT secret_column FROM mart.vw_sales;", GuardrailCheckName.AllowListColumns, ReasonCode.GR004)]
    public async Task MaliciousCandidate_IsRejectedByCommonGuardrails(
        string candidate,
        GuardrailCheckName failedCheck,
        ReasonCode reasonCode)
    {
        var setup = CreateRouter(candidate);

        var routing = await setup.ProductionRouter.ProduceAsync(
            Request(),
            AgenticQuery(),
            UserDataScope.Unrestricted,
            CancellationToken.None);

        Assert.Equal(GuardrailDecision.Rejected, routing.Guardrail.Decision);
        Assert.Equal(reasonCode, routing.Guardrail.ReasonCode);
        Assert.Null(routing.Guardrail.Sql);
        Assert.Contains(routing.Guardrail.Checks,
            check => check.Name == failedCheck && check.Outcome == CheckOutcome.Failed);
        Assert.Equal(1, setup.Model.InvocationCount);
    }

    [Fact]
    public async Task UnresolvedScope_RejectsAgenticCandidateWithoutSql()
    {
        var setup = CreateRouter(ValidPeriodComparisonCandidate);

        var routing = await setup.ProductionRouter.ProduceAsync(
            Request(),
            AgenticQuery(),
            UserDataScope.Unresolved,
            CancellationToken.None);

        Assert.Equal(GuardrailDecision.Rejected, routing.Guardrail.Decision);
        Assert.Equal(ReasonCode.GR007, routing.Guardrail.ReasonCode);
        Assert.Null(routing.Guardrail.Sql);
        Assert.Contains(routing.Guardrail.Checks,
            check => check.Name == GuardrailCheckName.ScopeFilterInjection
                && check.Outcome == CheckOutcome.Failed);
    }

    [Fact]
    public async Task ModelCannotSupplyOrReferenceBackendOwnedScopeParameter()
    {
        var setup = CreateRouter(
            "SELECT customer_state, SUM(price) AS total " +
            "FROM mart.vw_sales WHERE customer_state = @scope0 GROUP BY customer_state;");

        var result = await setup.StrategyRouter.ProduceAsync(
            Request(), AgenticQuery(), CancellationToken.None);

        Assert.False(result.IsSuccessful);
        Assert.Equal(ReasonCode.GR014, result.ReasonCode);
        Assert.Null(result.Sql);
        Assert.Empty(result.Parameters);
    }

    [Fact]
    public async Task UnableToResolve_IsSingleInvocationAndFailsClosedWithoutSql()
    {
        var setup = CreateRouter(
            sql: null,
            status: SqlReasoningResponseStatus.UnableToResolve);

        var result = await setup.StrategyRouter.ProduceAsync(
            Request(), AgenticQuery(), CancellationToken.None);

        Assert.False(result.IsSuccessful);
        Assert.Equal(ReasonCode.GR014, result.ReasonCode);
        Assert.Null(result.Sql);
        Assert.Equal(1, setup.Model.InvocationCount);
    }

    [Fact]
    public async Task Cancellation_IsPropagatedToModelClient()
    {
        var model = new RecordingModelClient(ValidPeriodComparisonCandidate)
        {
            WaitForCancellation = true
        };
        var setup = CreateRouter(model);
        using var cancellation = new CancellationTokenSource();

        var operation = setup.StrategyRouter.ProduceAsync(
            Request(), AgenticQuery(), cancellation.Token);
        await model.InvocationStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation);

        Assert.Equal(1, model.InvocationCount);
        Assert.True(model.LastCancellationToken.CanBeCanceled);
    }

    [Fact]
    public void ProductionComposition_ExplicitlyKeepsAgenticUnavailable()
    {
        Assert.Equal(
            AgenticSqlAvailability.DisabledNoProvider,
            SqlProductionFactory.ProductionAgenticAvailability);
    }

    [Fact]
    public void AgenticLayer_HasNoExecutionOrCredentialDependencies()
    {
        var forbiddenNames = new[]
        {
            "SqlConnection",
            "IQueryExecutionClient",
            "TokenCredential",
            "ManagedIdentity"
        };
        var inspectedTypes = new[]
        {
            typeof(AgenticSqlQueryStrategy),
            typeof(SqlReasoningOrchestrator),
            typeof(ISqlReasoningModelClient)
        };

        var dependencyNames = inspectedTypes
            .SelectMany(type => type.GetConstructors(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            .SelectMany(constructor => constructor.GetParameters())
            .Select(parameter => parameter.ParameterType.FullName ?? parameter.ParameterType.Name)
            .Concat(typeof(ISqlReasoningModelClient).GetMethods()
                .SelectMany(method => method.GetParameters())
                .Select(parameter => parameter.ParameterType.FullName
                    ?? parameter.ParameterType.Name))
            .ToArray();

        Assert.DoesNotContain(dependencyNames, dependency =>
            forbiddenNames.Any(forbidden =>
                dependency.Contains(forbidden, StringComparison.OrdinalIgnoreCase)));
    }

    private static TestSetup CreateRouter(
        string? sql,
        SqlReasoningResponseStatus status = SqlReasoningResponseStatus.Candidate) =>
        CreateRouter(new RecordingModelClient(sql, status));

    private static TestSetup CreateRouter(RecordingModelClient model)
    {
        var parserFactory = new TSqlParserFactory();
        var builder = new DeterministicQueryBuilder(
            parserFactory, Dwh.Catalog, Dwh.AllowList);
        var semanticTools = new SemanticContextService(Registry, parserFactory);
        var orchestrator = new SqlReasoningOrchestrator(
            DataSource.Dwh, semanticTools, model, parserFactory);
        var agentic = new AgenticSqlQueryStrategy(DataSource.Dwh, orchestrator);
        var strategyRouter = new QueryStrategyRouter(
            [new DeterministicSqlQueryStrategy(builder), agentic],
            builder);
        var productionRouter = new SqlProductionRouter(
            strategyRouter,
            Dwh.AllowList,
            parserFactory,
            new NullAuditWriter(),
            source: DataSource.Dwh);

        return new TestSetup(strategyRouter, productionRouter, agentic, model);
    }

    private static CanonicalRequest Request() => new()
    {
        RequestId = "request-agentic",
        ConversationId = "conversation-agentic",
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

    private static CanonicalQuery AgenticQuery() =>
        CanonicalV1ToV2Adapter.Adapt(Request()) with
        {
            Comparisons =
            [
                new CanonicalPeriodComparison
                {
                    Kind = PeriodComparisonKind.PreviousPeriod
                }
            ]
        };

    private sealed record TestSetup(
        QueryStrategyRouter StrategyRouter,
        SqlProductionRouter ProductionRouter,
        AgenticSqlQueryStrategy AgenticStrategy,
        RecordingModelClient Model);

    private sealed class RecordingModelClient(
        string? sql,
        SqlReasoningResponseStatus status = SqlReasoningResponseStatus.Candidate)
        : ISqlReasoningModelClient
    {
        public int InvocationCount { get; private set; }

        public SqlReasoningRequest? LastRequest { get; private set; }

        public CancellationToken LastCancellationToken { get; private set; }

        public TaskCompletionSource<bool> InvocationStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool WaitForCancellation { get; init; }

        public async Task<SqlReasoningResponse> GenerateAsync(
            SqlReasoningRequest request,
            CancellationToken cancellationToken)
        {
            InvocationCount++;
            LastRequest = request;
            LastCancellationToken = cancellationToken;
            InvocationStarted.TrySetResult(true);
            if (WaitForCancellation)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            return new SqlReasoningResponse
            {
                Status = status,
                Sql = sql,
                ReferencedSemanticKeys = ["item_sales", "customer_state"],
                ReferencedRelationshipIds = ImmutableArray<string>.Empty
            };
        }
    }

    private sealed class NullAuditWriter : IDecisionAuditWriter
    {
        public void Write(DecisionAuditRecord record)
        {
        }
    }
}
