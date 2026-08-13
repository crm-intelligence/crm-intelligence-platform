using CrmAnalytics.Contracts.Integrations;
using CrmAnalytics.Infrastructure.Integrations;
using Microsoft.Extensions.Options;

namespace CrmAnalytics.UnitTests;

public sealed class MockQueryPlanningClientTests
{
    [Fact]
    public async Task PlanAsync_DefaultOptions_ReturnsCompletedSafeResult()
    {
        const string prompt = "Show confidential customer activity.";
        var client = CreateClient();
        var request = CreateRequest("request-123", prompt);

        var response = await client.PlanAsync(
            request,
            CancellationToken.None);

        Assert.Equal(ExternalOperationStatus.Completed, response.Status);
        Assert.Equal(
            "query-result://request-123",
            response.GeneratedQueryReference);
        Assert.Contains("request-123", response.GeneratedQueryReference);
        Assert.NotNull(response.CanonicalRequest);
        Assert.DoesNotContain(prompt, response.CanonicalRequest);
        Assert.DoesNotContain(
            "Marmara-Sensitive",
            response.CanonicalRequest);
        Assert.DoesNotContain(
            "STORE-SENSITIVE",
            response.CanonicalRequest);
        Assert.DoesNotContain(
            "Report.Secret",
            response.CanonicalRequest);
        Assert.Contains(
            "\"regionCount\":1",
            response.CanonicalRequest);
        Assert.Null(response.ClarificationQuestion);
        Assert.Null(response.Error);
    }

    [Fact]
    public async Task PlanAsync_ForcedClarification_ReturnsQuestion()
    {
        var client = CreateClient(options =>
            options.ForceClarification = true);

        var response = await client.PlanAsync(
            CreateRequest(),
            CancellationToken.None);

        Assert.Equal(
            ExternalOperationStatus.WaitingForClarification,
            response.Status);
        Assert.Equal(
            "Analiz için tarih aralığını belirtir misiniz?",
            response.ClarificationQuestion);
        Assert.Null(response.CanonicalRequest);
        Assert.Null(response.GeneratedQueryReference);
        Assert.Null(response.Error);
    }

    [Fact]
    public async Task PlanAsync_ForcedFailure_ReturnsSafeError()
    {
        var client = CreateClient(options =>
            options.ForceQueryPlanningFailure = true);

        var response = await client.PlanAsync(
            CreateRequest(),
            CancellationToken.None);

        Assert.Equal(ExternalOperationStatus.Failed, response.Status);
        Assert.Null(response.CanonicalRequest);
        Assert.Null(response.GeneratedQueryReference);
        Assert.NotNull(response.Error);
        Assert.Equal("QUERY_PLANNING_FAILED", response.Error.Code);
        Assert.False(response.Error.IsTransient);
        Assert.DoesNotContain(
            CreateRequest().Prompt,
            response.Error.Message);
    }

    [Fact]
    public async Task PlanAsync_CancelledToken_CancelsOperation()
    {
        var client = CreateClient(options =>
            options.DelayMilliseconds = 1000);
        using var cancellationTokenSource =
            new CancellationTokenSource();
        cancellationTokenSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.PlanAsync(
                CreateRequest(),
                cancellationTokenSource.Token));
    }

    private static MockQueryPlanningClient CreateClient(
        Action<MockExternalServicesOptions>? configure = null)
    {
        var options = new MockExternalServicesOptions
        {
            DelayMilliseconds = 0
        };
        configure?.Invoke(options);
        return new MockQueryPlanningClient(Options.Create(options));
    }

    private static QueryPlanningRequest CreateRequest(
        string requestId = "request-1",
        string prompt = "Show sales trends.")
    {
        return new QueryPlanningRequest(
            RequestId: requestId,
            Prompt: prompt,
            PreviousRequestId: "request-0",
            UserDataScope: new UserDataScope
            {
                UserId = TestDataScopeFactory.UserId,
                TenantId = TestDataScopeFactory.TenantId,
                Roles = ["Report.Secret"],
                AllowedRegions = ["Marmara-Sensitive"],
                AllowedStoreIds = ["STORE-SENSITIVE"]
            },
            ConversationContext: new ConversationContextSnapshot(
                ConversationId: "conversation-1",
                PreviousRequestId: "request-0",
                PreviousSummary: "Previous summary.",
                PreviousPowerBiUrl:
                    "https://app.powerbi.com/reports/previous"));
    }
}
