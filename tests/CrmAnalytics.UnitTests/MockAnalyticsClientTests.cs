using CrmAnalytics.Application.Analytics;
using CrmAnalytics.Contracts.Integrations;
using CrmAnalytics.Infrastructure.Integrations;
using Microsoft.Extensions.Options;

namespace CrmAnalytics.UnitTests;

public sealed class MockAnalyticsClientTests
{
    [Fact]
    public async Task AnalyzeAsync_DefaultOptions_ReturnsDeterministicCompletedResult()
    {
        var client = CreateClient();
        var request = CreateRequest();

        var first = await client.AnalyzeAsync(
            request,
            CancellationToken.None);
        var second = await client.AnalyzeAsync(
            request,
            CancellationToken.None);

        Assert.Equal(ExternalOperationStatus.Completed, first.Status);
        Assert.Equal(first.JobId, second.JobId);
        Assert.Equal(first.ResultReference, second.ResultReference);
        Assert.Equal(
            "analytics-result://request-1",
            first.ResultReference);
        Assert.Equal(
            "Mock analiz başarıyla tamamlandı.",
            first.Summary);
        Assert.Null(first.Error);
    }

    [Fact]
    public async Task AnalyzeAsync_ForcedFailure_ReturnsExpectedError()
    {
        var client = CreateClient(options =>
            options.ForceAnalyticsFailure = true);

        var response = await client.AnalyzeAsync(
            CreateRequest(),
            CancellationToken.None);

        Assert.Equal(ExternalOperationStatus.Failed, response.Status);
        Assert.NotNull(response.Error);
        Assert.Equal("ANALYTICS_FAILED", response.Error.Code);
        Assert.True(response.Error.IsTransient);
        Assert.Null(response.ResultReference);
        Assert.Null(response.Summary);
    }

    [Fact]
    public async Task AnalyzeAsync_CancelledToken_CancelsOperation()
    {
        var client = CreateClient(options =>
            options.DelayMilliseconds = 1000);
        using var cancellationTokenSource =
            new CancellationTokenSource();
        cancellationTokenSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.AnalyzeAsync(
                CreateRequest(),
                cancellationTokenSource.Token));
    }

    private static MockAnalyticsClient CreateClient(
        Action<MockExternalServicesOptions>? configure = null)
    {
        var options = new MockExternalServicesOptions
        {
            DelayMilliseconds = 0
        };
        configure?.Invoke(options);
        return new MockAnalyticsClient(Options.Create(options));
    }

    private static AnalyticsExecutionRequest CreateRequest()
    {
        return new AnalyticsExecutionRequest(
            new AnalyticsRequest(
                RequestId: "request-1",
                AnalysisType: "SalesTrend",
                QueryResultReference: "query-result://request-1",
                Parameters: new Dictionary<string, string?>
                {
                    ["region"] = "TR"
                }),
            null);
    }
}
