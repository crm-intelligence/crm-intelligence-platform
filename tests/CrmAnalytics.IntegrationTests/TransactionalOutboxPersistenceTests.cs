using CrmAnalytics.Application.Outbox;
using CrmAnalytics.Infrastructure.Outbox;
using CrmAnalytics.Infrastructure.Persistence.SqlServer;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace CrmAnalytics.IntegrationTests;

public sealed class TransactionalOutboxPersistenceTests
{
    [Fact]
    public async Task SqlWriter_PersistsPendingAndModelHasConcurrencyAndIndexes()
    {
        var now = new DateTimeOffset(
            2026, 7, 31, 10, 0, 0, TimeSpan.Zero);
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<CrmAnalyticsDbContext>()
            .UseSqlite(connection).Options;
        await using var context = new CrmAnalyticsDbContext(options);
        await context.Database.EnsureCreatedAsync();
        var serializer = new OutboxMessageSerializer();
        var message = new OutboxMessage(
            "sql-id",
            OutboxMessageType.ReportProcessingRequested,
            "request",
            now,
            serializer.SerializeReportProcessingRequested(
                new ReportProcessingRequestedMessage(
                    "request", "correlation", now)));
        var writer = new SqlOutboxWriter(context);

        await writer.AppendAsync(message, CancellationToken.None);
        await writer.AppendAsync(message, CancellationToken.None);

        var entityType = context.Model.GetEntityTypes().Single(item =>
            item.GetTableName() == "OutboxMessages");
        Assert.True(entityType.FindProperty("RowVersion")!.IsConcurrencyToken);
        Assert.Equal(5, entityType.GetIndexes().Count());
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Status FROM OutboxMessages";
        Assert.Equal("Pending", await command.ExecuteScalarAsync());
    }
}
