using CrmAnalytics.Application.Abstractions.Integrations;
using CrmAnalytics.Application.Analytics;
using CrmAnalytics.Contracts.Integrations;
using CrmAnalytics.Infrastructure.QueryExecution;
using Microsoft.Extensions.DependencyInjection;

namespace CrmAnalytics.IntegrationTests;

public sealed class ExternalServicesDependencyInjectionTests
    : IClassFixture<IntegrationTestWebApplicationFactory>
{
    private readonly IntegrationTestWebApplicationFactory _factory;

    public ExternalServicesDependencyInjectionTests(
        IntegrationTestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task DevelopmentConfiguration_ResolvesAndRunsMockClients()
    {
        using var scope = _factory.Services.CreateScope();
        var queryPlanningClient = scope.ServiceProvider
            .GetRequiredService<IQueryPlanningClient>();
        var analyticsClient = scope.ServiceProvider
            .GetRequiredService<IAnalyticsClient>();
        var reportClient = scope.ServiceProvider
            .GetRequiredService<IReportClient>();
        var executionClient = scope.ServiceProvider
            .GetRequiredService<IQueryExecutionClient>();
        const string requestId = "integration-request-1";
        var userDataScope = new UserDataScope();

        var planningResponse = await queryPlanningClient.PlanAsync(
            new QueryPlanningRequest(
                RequestId: requestId,
                Prompt: "Show sales trends.",
                PreviousRequestId: null,
                UserDataScope: userDataScope,
                ConversationContext: new ConversationContextSnapshot(
                    ConversationId: "integration-conversation-1",
                    PreviousRequestId: null,
                    PreviousSummary: null,
                    PreviousPowerBiUrl: null)),
            CancellationToken.None);

        var analyticsResponse = await analyticsClient.AnalyzeAsync(
            new AnalyticsExecutionRequest(
                new AnalyticsRequest(
                    RequestId: requestId,
                    AnalysisType: "SalesTrend",
                    QueryResultReference:
                        Assert.IsType<string>(
                            planningResponse.GeneratedQueryReference),
                    Parameters: new Dictionary<string, string?>()),
                null),
            CancellationToken.None);

        var reportResponse = await reportClient.GenerateAsync(
            new ReportGenerationRequest(
                RequestId: requestId,
                ReportType: "PowerBi",
                ResultReference:
                    Assert.IsType<string>(
                        analyticsResponse.ResultReference),
                UserDataScope: userDataScope),
            CancellationToken.None);

        Assert.Equal(
            ExternalOperationStatus.Completed,
            planningResponse.Status);
        Assert.Equal(
            ExternalOperationStatus.Completed,
            analyticsResponse.Status);
        Assert.Equal(
            ExternalOperationStatus.Completed,
            reportResponse.Status);
        Assert.IsType<MockQueryExecutionClient>(executionClient);
    }
}
