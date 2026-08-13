using CrmAnalytics.Application.SqlProduction;
using CrmAnalytics.Contracts.Integrations;
using CrmAnalytics.Infrastructure.Integrations;
using Microsoft.Extensions.Options;

namespace CrmAnalytics.UnitTests;

public sealed class PowerBiReportRouterTests
{
    private const string WorkspaceId =
        "7fe125e9-705b-4d1a-ac37-eb0e22801e87";
    private const string AnalyticsReportId =
        "36b69ded-e421-412b-b187-70caa70d4292";

    [Theory]
    [InlineData("order_count", PowerBiOptions.DefaultSalesAnalysisPageId)]
    [InlineData("payment_total", PowerBiOptions.DefaultPaymentAnalysisPageId)]
    [InlineData("avg_frequency",
        PowerBiOptions.DefaultCustomerSegmentationPageId)]
    [InlineData("unknown_metric", PowerBiOptions.DefaultSalesAnalysisPageId)]
    public void AnalyticsIntent_RoutesToAllowlistedPage(
        string metric,
        string expectedPage)
    {
        var route = CreateRouter().Route(
            SqlDataSource.Dwh, Shape(metric), null);

        Assert.Equal(AnalyticsReportId, route.ReportId);
        Assert.Equal(expectedPage, route.PageId);
        Assert.Equal(
            $"https://app.powerbi.com/groups/{WorkspaceId}/reports/"
            + $"{AnalyticsReportId}/{expectedPage}",
            route.Url);
        Assert.DoesNotContain("evil", route.Url,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Oltp_RoutesToSeparateAllowlistedReportAndPage()
    {
        var route = CreateRouter().Route(
            SqlDataSource.Oltp,
            Shape("unknown_metric"),
            AcceptedPlan(
                filters:
                [Filter("customer_city", "eq", "Istanbul")]));

        Assert.Equal(PowerBiOptions.DefaultOltpReportId, route.ReportId);
        Assert.Equal(PowerBiOptions.DefaultOltpPageId, route.PageId);
        Assert.Equal(
            $"https://app.powerbi.com/groups/{WorkspaceId}/reports/"
            + $"{PowerBiOptions.DefaultOltpReportId}/"
            + PowerBiOptions.DefaultOltpPageId,
            route.Url);
        Assert.Null(GetDecodedFilter(route.Url));
    }

    [Fact]
    public void AcceptedMay2018AndIstanbul_AddsDecodedFiltersToSalesPage()
    {
        var route = CreateRouter().Route(
            SqlDataSource.Dwh,
            Shape("order_count"),
            AcceptedPlan(
                "2018-05-01",
                "2018-05-31",
                Filter("customer_city", "eq", "Istanbul")));

        Assert.Equal(PowerBiOptions.DefaultSalesAnalysisPageId,
            route.PageId);
        Assert.Equal(
            "dim_date/calendar_year eq 2018 and "
            + "dim_date/month_number eq 5 and "
            + "dim_customer/customer_city eq 'Istanbul'",
            GetDecodedFilter(route.Url));
        Assert.DoesNotContain(" ", route.Url);
        Assert.Contains("%2F", route.Url,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("%27Istanbul%27", route.Url,
            StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(
        "product_category",
        "bed_bath_table",
        "dim_product/product_category_name eq 'bed_bath_table'")]
    [InlineData(
        "customer_state",
        "SP",
        "dim_customer/customer_state eq 'SP'")]
    [InlineData(
        "order_id",
        "order-42",
        "fact_sales/order_id eq 'order-42'")]
    public void AcceptedAllowlistedFilter_AddsMappedExpression(
        string dimension,
        string value,
        string expectedExpression)
    {
        var route = CreateRouter().Route(
            SqlDataSource.Dwh,
            Shape("order_count"),
            AcceptedPlan(filters: [Filter(dimension, "eq", value)]));

        Assert.Equal(expectedExpression, GetDecodedFilter(route.Url));
    }

    [Theory]
    [InlineData("totally_unknown_dimension", "eq")]
    [InlineData("customer_city", "contains")]
    public void UnsafeFilter_IsSkippedWithoutFailing(
        string dimension,
        string filterOperator)
    {
        var route = CreateRouter().Route(
            SqlDataSource.Dwh,
            Shape("order_count"),
            AcceptedPlan(
                filters: [Filter(dimension, filterOperator, "foo")]));

        Assert.Null(GetDecodedFilter(route.Url));
    }

    [Fact]
    public void MultipleFilterValues_AreSkipped()
    {
        var route = CreateRouter().Route(
            SqlDataSource.Dwh,
            Shape("order_count"),
            AcceptedPlan(
                filters:
                [Filter("customer_city", "eq", "Istanbul", "Ankara")]));

        Assert.Null(GetDecodedFilter(route.Url));
    }

    [Fact]
    public void StringLiteralContainingApostrophe_IsEscaped()
    {
        var route = CreateRouter().Route(
            SqlDataSource.Dwh,
            Shape("order_count"),
            AcceptedPlan(
                filters: [Filter("customer_city", "eq", "O'Brien")]));

        Assert.Equal(
            "dim_customer/customer_city eq 'O''Brien'",
            GetDecodedFilter(route.Url));
    }

    [Fact]
    public void NullSemanticPlan_PreservesUnfilteredUrl()
    {
        var route = CreateRouter().Route(
            SqlDataSource.Dwh,
            Shape("order_count"),
            null);

        Assert.Equal(
            $"https://app.powerbi.com/groups/{WorkspaceId}/reports/"
            + $"{AnalyticsReportId}/"
            + PowerBiOptions.DefaultSalesAnalysisPageId,
            route.Url);
    }

    [Fact]
    public void NonAcceptedOutcome_PreservesUnfilteredUrl()
    {
        var plan = AcceptedPlan(
            filters: [Filter("customer_city", "eq", "Istanbul")])
            with { Outcome = "needs_clarification" };

        var route = CreateRouter().Route(
            SqlDataSource.Dwh,
            Shape("order_count"),
            plan);

        Assert.Null(GetDecodedFilter(route.Url));
    }

    [Theory]
    [InlineData("2018-05-02", "2018-05-31")]
    [InlineData("2018-05-01", "2018-05-30")]
    [InlineData("invalid", "2018-05-31")]
    [InlineData("2018-05-01", null)]
    public void InvalidOrPartialDate_IsSkippedWithoutFailing(
        string from,
        string? to)
    {
        var route = CreateRouter().Route(
            SqlDataSource.Dwh,
            Shape("order_count"),
            AcceptedPlan(from, to));

        Assert.Null(GetDecodedFilter(route.Url));
    }

    [Theory]
    [InlineData("2019-02-01", "2019-02-28", 2019)]
    [InlineData("2020-02-01", "2020-02-29", 2020)]
    public void FullCalendarFebruary_AddsYearAndMonthFilters(
        string from,
        string to,
        int expectedYear)
    {
        var route = CreateRouter().Route(
            SqlDataSource.Dwh,
            Shape("order_count"),
            AcceptedPlan(from, to));

        Assert.Equal(
            $"dim_date/calendar_year eq {expectedYear} and "
            + "dim_date/month_number eq 2",
            GetDecodedFilter(route.Url));
    }

    private static PowerBiReportRouter CreateRouter() => new(
        Options.Create(new ReportingOptions
        {
            Provider = ReportingProviders.PowerBi,
            PowerBi = new PowerBiOptions
            {
                WorkspaceId = WorkspaceId,
                ReportId = AnalyticsReportId
            }
        }));

    private static SqlResultShapeMetadata Shape(string metric) => new(
        "KpiCard", "test", [], [], [metric], [metric]);

    private static SubmittedSemanticPlanningResult AcceptedPlan(
        string? from = null,
        string? to = null,
        params SubmittedSemanticFilter[] filters) => new(
            "accepted",
            new SubmittedSemanticIntent(
                "order_count",
                [],
                filters,
                new SubmittedDateIntent(
                    "absolute",
                    null,
                    null,
                    from,
                    to,
                    null),
                null),
            [],
            null);

    private static SubmittedSemanticFilter Filter(
        string dimension,
        string filterOperator,
        params string?[] values) => new(
            dimension,
            filterOperator,
            values);

    private static string? GetDecodedFilter(string url)
    {
        const string prefix = "?filter=";
        var query = new Uri(url).Query;
        return query.StartsWith(prefix, StringComparison.Ordinal)
            ? Uri.UnescapeDataString(query[prefix.Length..])
            : null;
    }
}
