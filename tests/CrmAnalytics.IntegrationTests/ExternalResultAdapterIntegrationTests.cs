using CrmAnalytics.Application.Analytics;
using CrmAnalytics.Contracts.Integrations;
using CrmAnalytics.Infrastructure.Integrations;
using Microsoft.Extensions.Options;

namespace CrmAnalytics.IntegrationTests;

public sealed class ExternalResultAdapterIntegrationTests
{
    [Fact]
    public async Task PowerBiFakeApi_MapsCompletedSafeLink()
    {
        var id = Guid.NewGuid();
        var client = new PowerBiReportClient(
            new FakePowerBiApi(new PowerBiReportMetadata(
                id.ToString("D"),
                new Uri($"https://app.powerbi.com/groups/me/reports/{id:D}"))),
            Options.Create(new ReportingOptions()));

        var result = await client.GenerateAsync(
            new ReportGenerationRequest("req", "PowerBi", "result"),
            CancellationToken.None);

        Assert.Equal(ExternalOperationStatus.Completed, result.Status);
        Assert.Equal(id.ToString("D"), result.ReportId);
        Assert.StartsWith("https://app.powerbi.com/", result.PowerBiUrl);
    }

    [Fact]
    public async Task PowerBiFakeFailure_MapsFailedWithoutResponseBody()
    {
        var client = new PowerBiReportClient(
            new FakePowerBiApi(null), Options.Create(new ReportingOptions()));

        var result = await client.GenerateAsync(
            new ReportGenerationRequest("req", "PowerBi", "result"),
            CancellationToken.None);

        Assert.Equal(ExternalOperationStatus.Failed, result.Status);
        Assert.Null(result.PowerBiUrl);
        Assert.DoesNotContain("external-sensitive-body", result.Error!.Message);
    }

    [Fact]
    public async Task FabricFakeJob_MapsCompletedReference()
    {
        var options = new AnalyticsOptions
        {
            Fabric = new FabricOptions { ScenarioKey = "approved-scenario" }
        };
        var client = new FabricJobAnalyticsClient(
            new FakeFabricJobClient(false), Options.Create(options));

        var result = await client.AnalyzeAsync(CreateRequest(),
            CancellationToken.None);

        Assert.Equal(ExternalOperationStatus.Completed, result.Status);
        Assert.Equal("fabric-job://job-safe-id", result.ResultReference);
    }

    [Fact]
    public async Task FabricFakeFailure_MapsTechnicalFailed()
    {
        var client = new FabricJobAnalyticsClient(
            new FakeFabricJobClient(true),
            Options.Create(new AnalyticsOptions()));

        var result = await client.AnalyzeAsync(CreateRequest(),
            CancellationToken.None);

        Assert.Equal(ExternalOperationStatus.Failed, result.Status);
        Assert.Null(result.ResultReference);
        Assert.DoesNotContain("external-sensitive-body", result.Error!.Message);
    }

    private static AnalyticsExecutionRequest CreateRequest() => new(
        new AnalyticsRequest("req", "Sales", "durable-result-ref",
            new Dictionary<string, string?>()),
        QueryResult: null);

    private sealed class FakePowerBiApi(PowerBiReportMetadata? report)
        : IPowerBiReportApiClient
    {
        public Task<PowerBiReportMetadata> GetConfiguredReportAsync(
            CancellationToken cancellationToken) => report is null
            ? throw new InvalidOperationException("external-sensitive-body")
            : Task.FromResult(report);

        public Task RefreshSemanticModelAsync(
            CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FakeFabricJobClient(bool fail) : IFabricJobClient
    {
        public Task<FabricJobReference> RunToCompletionAsync(
            CancellationToken cancellationToken) => fail
            ? throw new InvalidOperationException("external-sensitive-body")
            : Task.FromResult(new FabricJobReference("job-safe-id"));
    }
}
