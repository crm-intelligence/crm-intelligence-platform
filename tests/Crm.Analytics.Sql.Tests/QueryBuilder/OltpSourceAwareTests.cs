using System.Text.Json;
using Crm.Analytics.Sql.Audit;
using Crm.Analytics.Sql.Catalog;
using Crm.Analytics.Sql.Contracts;
using Crm.Analytics.Sql.Guardrail;
using Crm.Analytics.Sql.Guardrail.Checks;
using Crm.Analytics.Sql.Parsing;
using Crm.Analytics.Sql.QueryBuilder;
using Crm.Analytics.Sql.Service;

namespace Crm.Analytics.Sql.Tests.QueryBuilder;

public sealed class OltpSourceAwareTests
{
    private static readonly AllowListDocument OltpAllowList =
        AllowListLoader.FromJson(ContractResources.ReadFabricOltpAllowList());
    private static readonly MetricCatalogDocument OltpCatalog =
        MetricCatalogLoader.FromJson(ContractResources.ReadFabricOltpMetricCatalog());
    private static readonly DateOnly Today = new(2026, 8, 5);

    [Fact]
    public void Oltp_contract_is_versioned_and_has_only_the_operational_view()
    {
        using var document = JsonDocument.Parse(ContractResources.ReadFabricOltpAllowList());
        Assert.Equal(
            "fabric-oltp-operational-orders-v1",
            document.RootElement.GetProperty("_meta").GetProperty("contractVersion").GetString());
        var allowed = Assert.Single(OltpAllowList.Objects);
        Assert.Equal("dbo.vw_operational_orders", allowed.Value.PhysicalName);
        Assert.Equal(1000, OltpAllowList.MaxRows);
        Assert.Equal(100, OltpAllowList.DefaultRows);
        Assert.Equal(15, OltpAllowList.QueryTimeoutSeconds);
        Assert.Empty(OltpCatalog.Metrics);

        string[] baseTables =
        [
            "dbo.customers", "dbo.products", "dbo.orders",
            "dbo.order_items", "dbo.order_payments"
        ];
        Assert.All(baseTables, table => Assert.False(OltpAllowList.HasSqlObject(table)));
    }

    [Fact]
    public void Oltp_builder_generates_parameterized_schema_qualified_golden_sql()
    {
        var result = Builder().Build(OltpRequest(
            filters:
            [
                new RequestFilter
                {
                    Field = "operational_customer_city",
                    Op = FilterOperator.Eq,
                    Values = [new FilterLiteral(FilterValueKind.Text, "Istanbul")]
                }
            ],
            limit: 25,
            orderBy: "operational_order_created_at",
            direction: SortDirection.Desc));

        Assert.True(result.IsSuccessful, result.Detail);
        Assert.Equal("operational_orders", result.SourceObject);
        var sql = Normalize(result.Sql!);
        const string expected = "SELECT TOP 25 order_id AS operational_order "
            + "FROM dbo.vw_operational_orders "
            + "WHERE customer_city = @f0 AND order_purchase_timestamp >= @f1 "
            + "AND order_purchase_timestamp < @f2 "
            + "ORDER BY order_purchase_timestamp DESC, order_id DESC;";
        Assert.Equal(expected, sql);
        Assert.DoesNotContain("Istanbul", result.Sql!, StringComparison.Ordinal);
        Assert.Equal(["@f0", "@f1", "@f2"], result.Parameters.Select(p => p.Name));
    }

    [Fact]
    public void Oltp_status_filter_is_fail_closed_without_proven_enum_values()
    {
        var result = Builder().Build(OltpRequest(
            filters:
            [
                new RequestFilter
                {
                    Field = "operational_order_status",
                    Op = FilterOperator.Eq,
                    Values = [new FilterLiteral(FilterValueKind.Text, "open")]
                }
            ]));

        Assert.False(result.IsSuccessful);
        Assert.Equal(ReasonCode.CL001, result.ReasonCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(DataSource.Oltp)]
    public void Oltp_only_prompt_selects_Oltp_and_enforces_limits(DataSource? source)
    {
        var response = Service().Produce(Request("Bugunku siparisleri getir", source));

        Assert.Equal(GuardrailDecision.Accepted, response.Decision);
        Assert.Equal(DataSource.Oltp, response.Source);
        Assert.Equal(DataSource.Oltp, response.CanonicalRequest!.Source);
        Assert.Equal(15, response.CommandTimeoutSeconds);
        Assert.Contains("TOP 100", response.Sql!, StringComparison.Ordinal);
        Assert.Contains("dbo.vw_operational_orders", response.Sql!, StringComparison.Ordinal);
        Assert.Contains(
            "ORDER BY order_purchase_timestamp ASC, order_id ASC",
            Normalize(response.Sql!),
            StringComparison.Ordinal);
        Assert.DoesNotContain("mart.", response.Sql!, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(DataSource.Dwh)]
    public void Dwh_only_prompt_selects_Dwh(DataSource? source)
    {
        var response = Service().Produce(Request("Gecen ay siparis sayisi", source));

        Assert.Equal(GuardrailDecision.Accepted, response.Decision);
        Assert.Equal(DataSource.Dwh, response.Source);
        Assert.Contains("mart.vw_sales", response.Sql!, StringComparison.Ordinal);
        Assert.DoesNotContain("dbo.vw_operational_orders", response.Sql!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Ambiguous_and_unknown_source_requests_require_clarification()
    {
        var ambiguous = Service().Produce(Request("Bugunku siparis durumu"));
        var unknown = Service().Produce(Request("Bilinmeyen alan raporu"));

        Assert.Equal(GuardrailDecision.NeedsClarification, ambiguous.Decision);
        Assert.Equal(GuardrailDecision.NeedsClarification, unknown.Decision);
        Assert.Null(ambiguous.Sql);
        Assert.Null(unknown.Sql);
    }

    [Fact]
    public void Explicit_cross_source_terms_are_isolated()
    {
        var dwhInOltp = Service().Produce(Request("Gecen ay siparis sayisi", DataSource.Oltp));
        var oltpInDwh = Service().Produce(Request("Bugunku siparisleri getir", DataSource.Dwh));

        Assert.Equal(GuardrailDecision.NeedsClarification, dwhInOltp.Decision);
        Assert.Equal(GuardrailDecision.NeedsClarification, oltpInDwh.Decision);
        Assert.Null(dwhInOltp.Sql);
        Assert.Null(oltpInDwh.Sql);
    }

    [Theory]
    [InlineData("Bugunku acik siparisleri getir")]
    [InlineData("Henuz teslim edilmemis siparisleri goster")]
    [InlineData("Son bir saatte olusturulan siparisleri goster")]
    public void Unproven_operational_semantics_require_clarification(string prompt)
    {
        var response = Service().Produce(Request(prompt));
        Assert.Equal(GuardrailDecision.NeedsClarification, response.Decision);
        Assert.Null(response.Sql);
    }

    [Fact]
    public void Select_into_is_rejected_upstream()
    {
        var parser = new TSqlParserFactory();
        var context = new GuardrailContext(
            "SELECT order_id INTO #copy FROM dbo.vw_operational_orders",
            OltpAllowList,
            UserDataScope.Unrestricted,
            parser,
            DataSource.Oltp);
        context.SetFragment(parser.Parse(context.CurrentSql).Fragment!);

        var result = new SelectOnlyCheck().Execute(context);
        Assert.Equal(CheckOutcome.Failed, result.Outcome);
        Assert.Equal(ReasonCode.GR001, result.ReasonCode);
    }

    [Fact]
    public void Revision_preserves_source_filters_limit_and_stable_ordering()
    {
        var previous = OltpRequest();
        var revised = CanonicalRequestReviser.Apply(
            previous,
            new CanonicalRequestDelta
            {
                FiltersToAdd =
                [
                    new RequestFilter
                    {
                        Field = "operational_customer_city",
                        Op = FilterOperator.Eq,
                        Values = [new FilterLiteral(FilterValueKind.Text, "Ankara")]
                    }
                ],
                Limit = 10,
                OrderBy = "operational_order_created_at",
                OrderDirection = SortDirection.Desc
            },
            "request-2");

        Assert.Equal(DataSource.Oltp, revised.Source);
        Assert.Equal(10, revised.Limit);
        Assert.Equal("operational_order_created_at", revised.OrderBy);
        var result = Builder().Build(revised);
        Assert.True(result.IsSuccessful, result.Detail);
        Assert.Contains("ORDER BY order_purchase_timestamp DESC, order_id DESC", result.Sql!, StringComparison.Ordinal);
    }

    [Fact]
    public void Natural_language_revisions_preserve_Oltp_source()
    {
        var service = Service();
        var initial = service.Produce(Request("Bugunku siparisleri getir"));
        Assert.Equal(GuardrailDecision.Accepted, initial.Decision);

        var limited = service.Produce(Request("ilk 10") with
        {
            PreviousRequest = initial.CanonicalRequest
        });
        Assert.Equal(GuardrailDecision.Accepted, limited.Decision);
        Assert.Equal(DataSource.Oltp, limited.CanonicalRequest!.Source);
        Assert.Contains("TOP 10", limited.Sql!, StringComparison.Ordinal);

        var filtered = service.Produce(Request("sehir Ankara olsun") with
        {
            PreviousRequest = limited.CanonicalRequest
        });
        Assert.Equal(GuardrailDecision.Accepted, filtered.Decision);
        Assert.Equal(DataSource.Oltp, filtered.CanonicalRequest!.Source);
        Assert.DoesNotContain("Ankara", filtered.Sql!, StringComparison.Ordinal);
        Assert.Contains(filtered.Parameters, parameter => parameter.Raw == "Ankara");

        var ordered = service.Produce(Request("siparis zamani azalan sirala") with
        {
            PreviousRequest = filtered.CanonicalRequest
        });
        Assert.Equal(GuardrailDecision.Accepted, ordered.Decision);
        Assert.Equal(DataSource.Oltp, ordered.CanonicalRequest!.Source);
        Assert.Contains(
            "ORDER BY order_purchase_timestamp DESC, order_id DESC",
            ordered.Sql!,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Invalid_cross_source_revision_requires_clarification()
    {
        var service = Service();
        var initial = service.Produce(Request("Bugunku siparisleri getir"));
        var revision = service.Produce(Request("ilk 10", DataSource.Dwh) with
        {
            PreviousRequest = initial.CanonicalRequest
        });

        Assert.Equal(GuardrailDecision.NeedsClarification, revision.Decision);
        Assert.Null(revision.Sql);
    }

    private static DeterministicQueryBuilder Builder() =>
        new(new TSqlParserFactory(), OltpCatalog, OltpAllowList);

    private static ISqlProductionService Service() =>
        SqlProductionFactory.CreateForOlist(new NullAuditWriter());

    private static SqlProductionRequest Request(string prompt, DataSource? source = null) => new()
    {
        Prompt = prompt,
        RequestId = Guid.NewGuid().ToString("N"),
        ConversationId = "conversation-1",
        Scope = UserDataScope.Unrestricted,
        Today = Today,
        Source = source
    };

    private static CanonicalRequest OltpRequest(
        IReadOnlyList<RequestFilter>? filters = null,
        int? limit = null,
        string? orderBy = null,
        SortDirection direction = SortDirection.Asc) => new()
    {
        RequestId = "request-1",
        ConversationId = "conversation-1",
        Source = DataSource.Oltp,
        Intent = RequestIntent.List,
        Metrics = [],
        Dimensions = ["operational_order"],
        Filters = filters ?? [],
        DateRange = new DateRangeSpec
        {
            Kind = DateRangeKind.Absolute,
            From = Today,
            To = Today
        },
        Limit = limit,
        OrderBy = orderBy,
        OrderDirection = direction,
        Confidence = 1
    };

    private static string Normalize(string sql) =>
        string.Join(' ', sql.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private sealed class NullAuditWriter : IDecisionAuditWriter
    {
        public void Write(DecisionAuditRecord record)
        {
        }
    }
}
