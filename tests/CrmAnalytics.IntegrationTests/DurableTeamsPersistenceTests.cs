using CrmAnalytics.Application.Teams;
using CrmAnalytics.Infrastructure.Persistence.SqlServer;
using CrmAnalytics.Infrastructure.Teams;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace CrmAnalytics.IntegrationTests;

public sealed class DurableTeamsPersistenceTests
{
    private static readonly DateTimeOffset Now = new(
        2026, 7, 31, 13, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task SqlStores_RoundTripAcrossDbContextRestart()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<CrmAnalyticsDbContext>()
            .UseSqlite(connection).Options;

        await using (var first = new CrmAnalyticsDbContext(options))
        {
            await first.Database.EnsureCreatedAsync();
            var targets = new SqlTeamsNotificationTargetStore(first);
            await targets.UpsertAsync("request-1", "conversation-1", Now,
                CancellationToken.None);

            var actions = new SqlTeamsCardActionSubmissionStore(first);
            Assert.Equal(TeamsActionClaimResult.Claimed,
                (await actions.ClaimAsync(
                    new string('A', 64), "request-1", "revise-report",
                    "owner-1", Now, TimeSpan.FromMinutes(1),
                    CancellationToken.None)).Result);
            await actions.CompleteAsync(new string('A', 64), "owner-1",
                "request-2", Now.AddSeconds(1), CancellationToken.None);
        }

        await using (var restarted = new CrmAnalyticsDbContext(options))
        {
            var target = await new SqlTeamsNotificationTargetStore(restarted)
                .FindAsync("request-1", CancellationToken.None);
            Assert.Equal("conversation-1", target!.ConversationId);

            var duplicate = await new SqlTeamsCardActionSubmissionStore(restarted)
                .ClaimAsync(new string('A', 64), "request-1",
                    "revise-report", "owner-2", Now.AddMinutes(2),
                    TimeSpan.FromMinutes(1), CancellationToken.None);
            Assert.Equal(TeamsActionClaimResult.Completed, duplicate.Result);
            Assert.Equal("request-2", duplicate.ResultRequestId);
        }
    }

    [Fact]
    public async Task TransactionalStores_WorkWithRetryingExecutionStrategy()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<CrmAnalyticsDbContext>()
            .UseSqlite(connection)
            .ReplaceService<IExecutionStrategyFactory,
                TestRetryingExecutionStrategyFactory>()
            .Options;
        await using var context = new CrmAnalyticsDbContext(options);
        await context.Database.EnsureCreatedAsync();

        await new SqlTeamsNotificationTargetStore(context).UpsertAsync(
            "request-retry", "conversation-retry", Now,
            CancellationToken.None);

        var delivery = await new SqlTeamsNotificationDeliveryStore(context)
            .ClaimAsync("delivery-retry", "request-retry", "Completed",
                Now, "delivery-owner", Now, TimeSpan.FromMinutes(1),
                CancellationToken.None);
        Assert.Equal(TeamsDeliveryClaimResult.Claimed, delivery);

        var action = await new SqlTeamsCardActionSubmissionStore(context)
            .ClaimAsync("action-retry", "request-retry", "revise-report",
                "action-owner", Now, TimeSpan.FromMinutes(1),
                CancellationToken.None);
        Assert.Equal(TeamsActionClaimResult.Claimed, action.Result);
    }

    [Fact]
    public async Task Model_ContainsRequiredTablesIndexesAndNoInputTextColumns()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<CrmAnalyticsDbContext>()
            .UseSqlite(connection).Options;
        await using var context = new CrmAnalyticsDbContext(options);

        var target = context.Model.GetEntityTypes().Single(x =>
            x.GetTableName() == "TeamsNotificationTargets");
        var delivery = context.Model.GetEntityTypes().Single(x =>
            x.GetTableName() == "TeamsNotificationDeliveries");
        var action = context.Model.GetEntityTypes().Single(x =>
            x.GetTableName() == "TeamsCardActionSubmissions");
        Assert.True(target.FindProperty("RowVersion")!.IsConcurrencyToken);
        Assert.Equal(2, delivery.GetIndexes().Count());
        Assert.True(action.FindProperty("RowVersion")!.IsConcurrencyToken);
        Assert.DoesNotContain(action.GetProperties(), p =>
            p.Name.Contains("Prompt", StringComparison.OrdinalIgnoreCase)
            || p.Name.Contains("Revision", StringComparison.OrdinalIgnoreCase)
            || p.Name.Contains("Clarification", StringComparison.OrdinalIgnoreCase)
            || p.Name.Contains("Response", StringComparison.OrdinalIgnoreCase));
    }

    private sealed class TestRetryingExecutionStrategyFactory(
        ExecutionStrategyDependencies dependencies)
        : IExecutionStrategyFactory
    {
        public IExecutionStrategy Create() =>
            new TestRetryingExecutionStrategy(dependencies);
    }

    private sealed class TestRetryingExecutionStrategy(
        ExecutionStrategyDependencies dependencies)
        : ExecutionStrategy(dependencies, 1, TimeSpan.Zero)
    {
        protected override bool ShouldRetryOn(Exception exception) => false;
    }
}
