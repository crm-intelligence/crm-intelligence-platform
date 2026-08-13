using CrmAnalytics.Application.Teams;
using CrmAnalytics.Infrastructure.Teams;

namespace CrmAnalytics.UnitTests;

public sealed class DurableTeamsStoreTests
{
    private static readonly DateTimeOffset Now = new(
        2026, 7, 31, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Target_UpsertIsIdempotentConflictSafeAndExact()
    {
        var store = new InMemoryTeamsNotificationTargetStore();
        await store.UpsertAsync("request-1", "conversation-1", Now,
            CancellationToken.None);
        await store.UpsertAsync("request-1", "conversation-1",
            Now.AddSeconds(1), CancellationToken.None);
        await Assert.ThrowsAsync<TeamsNotificationTargetConflictException>(
            () => store.UpsertAsync("request-1", "conversation-2",
                Now.AddSeconds(2), CancellationToken.None));
        Assert.NotNull(await store.FindAsync("request-1",
            CancellationToken.None));
        Assert.Null(await store.FindAsync("REQUEST-1",
            CancellationToken.None));
    }

    [Fact]
    public async Task Target_PropagatesCancellation()
    {
        var store = new InMemoryTeamsNotificationTargetStore();
        using var source = new CancellationTokenSource();
        source.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            store.FindAsync("request-1", source.Token));
    }

    [Fact]
    public async Task Delivery_AtomicClaimExpiryReleaseAndDeliveredDedup()
    {
        var store = new InMemoryTeamsNotificationDeliveryStore();
        var id = TeamsDeliveryId.Create("request-1", "Completed", Now);
        var claims = await Task.WhenAll(Enumerable.Range(0, 12).Select(i =>
            store.ClaimAsync(id, "request-1", "Completed", Now,
                $"owner-{i}", Now, TimeSpan.FromMinutes(1),
                CancellationToken.None)));
        Assert.Equal(1, claims.Count(x => x == TeamsDeliveryClaimResult.Claimed));
        Assert.Equal(11, claims.Count(x => x == TeamsDeliveryClaimResult.Busy));

        Assert.Equal(TeamsDeliveryClaimResult.Claimed,
            await store.ClaimAsync(id, "request-1", "Completed", Now,
                "reclaimer", Now.AddMinutes(2), TimeSpan.FromMinutes(1),
                CancellationToken.None));
        await store.ReleaseAsync(id, "reclaimer", Now.AddMinutes(2),
            CancellationToken.None);
        Assert.Equal(TeamsDeliveryClaimResult.Claimed,
            await store.ClaimAsync(id, "request-1", "Completed", Now,
                "final", Now.AddMinutes(2), TimeSpan.FromMinutes(1),
                CancellationToken.None));
        await store.MarkDeliveredAsync(id, "final", Now.AddMinutes(2),
            CancellationToken.None);
        Assert.Equal(TeamsDeliveryClaimResult.Delivered,
            await store.ClaimAsync(id, "request-1", "Completed", Now,
                "duplicate", Now.AddMinutes(3), TimeSpan.FromMinutes(1),
                CancellationToken.None));
    }

    [Fact]
    public void DeliveryId_IsDeterministicAndSensitiveDataFree()
    {
        var first = TeamsDeliveryId.Create("request-1", "Completed", Now);
        var second = TeamsDeliveryId.Create("request-1", "Completed", Now);
        Assert.Equal(first, second);
        Assert.Equal(64, first.Length);
        Assert.DoesNotContain("request", first,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Action_ClaimCompleteDuplicateReleaseAndExpiry()
    {
        var store = new InMemoryTeamsCardActionSubmissionStore();
        var first = await store.ClaimAsync("token-1", "request-1",
            "revise-report", "owner-1", Now, TimeSpan.FromMinutes(1),
            CancellationToken.None);
        Assert.Equal(TeamsActionClaimResult.Claimed, first.Result);
        var busy = await store.ClaimAsync("token-1", "request-1",
            "revise-report", "owner-2", Now, TimeSpan.FromMinutes(1),
            CancellationToken.None);
        Assert.Equal(TeamsActionClaimResult.Busy, busy.Result);
        await store.ReleaseAsync("token-1", "owner-1", Now,
            CancellationToken.None);
        var reclaimed = await store.ClaimAsync("token-1", "request-1",
            "revise-report", "owner-2", Now.AddSeconds(1),
            TimeSpan.FromMinutes(1), CancellationToken.None);
        Assert.Equal(TeamsActionClaimResult.Claimed, reclaimed.Result);
        await store.CompleteAsync("token-1", "owner-2", "request-2",
            Now.AddSeconds(2), CancellationToken.None);
        var duplicate = await store.ClaimAsync("token-1", "request-1",
            "revise-report", "owner-3", Now.AddMinutes(2),
            TimeSpan.FromMinutes(1), CancellationToken.None);
        Assert.Equal(TeamsActionClaimResult.Completed, duplicate.Result);
        Assert.Equal("request-2", duplicate.ResultRequestId);
    }
}
