using CrmAnalytics.Contracts.Integrations;
using CrmAnalytics.Infrastructure.Integrations;
using Microsoft.Extensions.Options;

namespace CrmAnalytics.UnitTests;

public sealed class MockReportClientTests
{
    [Fact]
    public async Task GenerateAsync_DefaultOptions_ReturnsDeterministicCompletedResult()
    {
        var client = CreateClient();
        var request = CreateRequest();

        var first = await client.GenerateAsync(
            request,
            CancellationToken.None);
        var second = await client.GenerateAsync(
            request,
            CancellationToken.None);

        Assert.Equal(ExternalOperationStatus.Completed, first.Status);
        Assert.Equal(first.ReportId, second.ReportId);
        Assert.Equal("ReportSection", first.PageName);
        Assert.NotNull(first.PowerBiUrl);
        Assert.True(
            Uri.TryCreate(
                first.PowerBiUrl,
                UriKind.Absolute,
                out var powerBiUri));
        Assert.Equal(Uri.UriSchemeHttps, powerBiUri.Scheme);
        Assert.Null(first.Error);
    }

    [Fact]
    public async Task GenerateAsync_ForcedFailure_ReturnsExpectedError()
    {
        var client = CreateClient(options =>
            options.ForceReportFailure = true);

        var response = await client.GenerateAsync(
            CreateRequest(),
            CancellationToken.None);

        Assert.Equal(ExternalOperationStatus.Failed, response.Status);
        Assert.NotNull(response.Error);
        Assert.Equal("REPORT_GENERATION_FAILED", response.Error.Code);
        Assert.True(response.Error.IsTransient);
        Assert.Null(response.ReportId);
        Assert.Null(response.PowerBiUrl);
    }

    [Fact]
    public async Task GenerateAsync_CancelledToken_CancelsOperation()
    {
        var client = CreateClient(options =>
            options.DelayMilliseconds = 1000);
        using var cancellationTokenSource =
            new CancellationTokenSource();
        cancellationTokenSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.GenerateAsync(
                CreateRequest(),
                cancellationTokenSource.Token));
    }

    private static MockReportClient CreateClient(
        Action<MockExternalServicesOptions>? configure = null)
    {
        var options = new MockExternalServicesOptions
        {
            DelayMilliseconds = 0
        };
        configure?.Invoke(options);
        return new MockReportClient(Options.Create(options));
    }

    private static ReportGenerationRequest CreateRequest()
    {
        return new ReportGenerationRequest(
            RequestId: "request-1",
            ReportType: "PowerBi",
            ResultReference: "analytics-result://request-1",
            UserDataScope: new UserDataScope());
    }
}
