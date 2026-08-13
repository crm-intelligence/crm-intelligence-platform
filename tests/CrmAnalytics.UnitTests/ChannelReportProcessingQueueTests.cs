using CrmAnalytics.Application.ReportProcessing;
using CrmAnalytics.Contracts.Integrations;
using CrmAnalytics.Infrastructure.ReportProcessing;
using Microsoft.Extensions.Options;

namespace CrmAnalytics.UnitTests;

public sealed class ChannelReportProcessingQueueTests
{
    [Fact]
    public async Task EnqueueAsync_ItemCanBeDequeuedWithValuesPreserved()
    {
        var queue = CreateQueue();
        var scope = new UserDataScope
        {
            UserId = "user-1",
            TenantId = "tenant-1",
            Roles = ["analyst"],
            AllowedRegions = ["TR"],
            AllowedStoreIds = ["store-1"]
        };
        var item = new QueuedReportProcessingRequest(
            "request-1",
            "correlation-1",
            scope,
            DateTimeOffset.UtcNow);

        await queue.EnqueueAsync(item, CancellationToken.None);
        var dequeued = await queue.DequeueAsync(CancellationToken.None);

        Assert.Same(item, dequeued);
        Assert.Equal("request-1", dequeued.RequestId);
        Assert.Equal("correlation-1", dequeued.CorrelationId);
        var dequeuedScope = Assert.IsType<UserDataScope>(
            dequeued.UserDataScope);
        Assert.NotSame(scope, dequeuedScope);
        Assert.Equal(["analyst"], dequeuedScope.Roles);
        Assert.Equal(["TR"], dequeuedScope.AllowedRegions);
        Assert.Equal(["store-1"], dequeuedScope.AllowedStoreIds);
    }

    [Fact]
    public async Task EnqueueAsync_MultipleItemsPreserveFifoOrder()
    {
        var queue = CreateQueue(capacity: 2);
        var first = CreateItem("request-1");
        var second = CreateItem("request-2");

        await queue.EnqueueAsync(first, CancellationToken.None);
        await queue.EnqueueAsync(second, CancellationToken.None);

        Assert.Same(
            first,
            await queue.DequeueAsync(CancellationToken.None));
        Assert.Same(
            second,
            await queue.DequeueAsync(CancellationToken.None));
    }

    [Fact]
    public async Task DequeueAsync_CancelledTokenCancelsWait()
    {
        var queue = CreateQueue();
        using var cancellationSource = new CancellationTokenSource();
        cancellationSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await queue.DequeueAsync(
                cancellationSource.Token));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(10001)]
    public void OptionsValidator_InvalidCapacityFails(int capacity)
    {
        var validator = new ReportProcessingQueueOptionsValidator();

        var result = validator.Validate(
            null,
            new ReportProcessingQueueOptions { Capacity = capacity });

        Assert.True(result.Failed);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(10000)]
    public void OptionsValidator_BoundaryCapacitySucceeds(int capacity)
    {
        var validator = new ReportProcessingQueueOptionsValidator();

        var result = validator.Validate(
            null,
            new ReportProcessingQueueOptions { Capacity = capacity });

        Assert.True(result.Succeeded);
    }

    private static ChannelReportProcessingQueue CreateQueue(
        int capacity = 1)
    {
        return new ChannelReportProcessingQueue(
            Options.Create(
                new ReportProcessingQueueOptions
                {
                    Enabled = true,
                    Capacity = capacity
                }));
    }

    private static QueuedReportProcessingRequest CreateItem(
        string requestId)
    {
        return new QueuedReportProcessingRequest(
            requestId,
            $"correlation-{requestId}",
            UserDataScope.Empty,
            DateTimeOffset.UtcNow);
    }
}
