using CrmAnalytics.Application.Abstractions.Persistence;
using CrmAnalytics.Application.Auditing;
using CrmAnalytics.Application.Conversations;
using CrmAnalytics.Application.Exceptions;
using CrmAnalytics.Application.Identity;
using CrmAnalytics.Application.Outbox;
using CrmAnalytics.Application.ReportProcessing;
using CrmAnalytics.Application.ReportRequests;
using CrmAnalytics.Application.SqlProduction;
using CrmAnalytics.Contracts.Integrations;
using CrmAnalytics.Domain.ReportRequests;
using CrmAnalytics.Infrastructure.Auditing;
using CrmAnalytics.Infrastructure.Identity;
using CrmAnalytics.Infrastructure.Outbox;
using CrmAnalytics.Infrastructure.Persistence.SqlServer;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace CrmAnalytics.IntegrationTests;

public sealed class ApplicationTransactionAndPolicyPersistenceTests
{
    private const string UserId =
        "11111111-1111-4111-8111-111111111111";
    private const string TenantId =
        "22222222-2222-4222-8222-222222222222";

    [Fact]
    public async Task CreateConversationAndAudit_CommitTogether()
    {
        await using var fixture = await SqliteFixture.CreateAsync();
        var service = fixture.CreateService(new SqlApplicationAuditWriter(
            fixture.Context));

        var result = await service.CreateAsync(
            new CreateReportRequestCommand(
                "Sales", "conversation-1", null, "correlation-1"),
            User(),
            CancellationToken.None);

        fixture.Context.ChangeTracker.Clear();
        Assert.NotNull(await fixture.Reports.GetByIdAsync(
            result.RequestId, CancellationToken.None));
        Assert.Equal(result.RequestId,
            await fixture.Conversations.GetByTeamsConversationIdAsync(
                "conversation-1", CancellationToken.None)
                .ContinueWith(task => task.Result?.LastRequestId));
        Assert.Equal(1, await fixture.CountAsync("ApplicationAuditEvents"));
    }

    [Fact]
    public async Task AuditFailure_RollsBackCreateAndConversation()
    {
        await using var fixture = await SqliteFixture.CreateAsync();
        var service = fixture.CreateService(new FailingAuditWriter());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateAsync(
                new CreateReportRequestCommand(
                    "Sales", "conversation-rollback", null,
                    "correlation-rollback"),
                User(),
                CancellationToken.None));

        fixture.Context.ChangeTracker.Clear();
        Assert.Equal(0, await fixture.CountAsync("ReportRequests"));
        Assert.Equal(0, await fixture.CountAsync("Conversations"));
    }

    [Fact]
    public async Task RevisionAndConversationLastRequest_RollBackOnAuditFailure()
    {
        await using var fixture = await SqliteFixture.CreateAsync();
        var service = fixture.CreateService(new SqlApplicationAuditWriter(
            fixture.Context));
        var source = await service.CreateAsync(
            new CreateReportRequestCommand(
                "Sales", "conversation-revision", null, "correlation-r"),
            User(), CancellationToken.None);
        var now = DateTimeOffset.UtcNow.AddSeconds(1);
        await service.TransitionStatusAsync(new(source.RequestId,
            ReportRequestStatus.Validating, now), CancellationToken.None);
        await service.TransitionStatusAsync(new(source.RequestId,
            ReportRequestStatus.Processing, now.AddSeconds(1)),
            CancellationToken.None);
        await service.CompleteAsync(new(source.RequestId, "report-1",
            "summary", "https://app.powerbi.com/report",
            now.AddSeconds(2)), CancellationToken.None);

        var failingService = fixture.CreateService(new FailingAuditWriter());
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            failingService.ReviseAsync(new(
                source.RequestId, "Revised sales", "correlation-r2"),
                User(), CancellationToken.None));

        fixture.Context.ChangeTracker.Clear();
        Assert.Equal(1, await fixture.CountAsync("ReportRequests"));
        var conversation = await fixture.Conversations
            .GetByTeamsConversationIdAsync(
                "conversation-revision", CancellationToken.None);
        Assert.Equal(source.RequestId, conversation!.LastRequestId);

        var revision = await service.ReviseAsync(new(
            source.RequestId, "Revised sales", "correlation-r3"),
            User(), CancellationToken.None);
        fixture.Context.ChangeTracker.Clear();
        Assert.Equal(2, await fixture.CountAsync("ReportRequests"));
        conversation = await fixture.Conversations
            .GetByTeamsConversationIdAsync(
                "conversation-revision", CancellationToken.None);
        Assert.Equal(revision.RequestId, conversation!.LastRequestId);
    }

    [Fact]
    public async Task ClarificationResponse_RollsBackOnAuditFailure()
    {
        await using var fixture = await SqliteFixture.CreateAsync();
        var service = fixture.CreateService(new SqlApplicationAuditWriter(
            fixture.Context));
        var created = await service.CreateAsync(
            new CreateReportRequestCommand(
                "Sales", "conversation-clarification", null,
                "correlation-c"), User(), CancellationToken.None);
        var now = DateTimeOffset.UtcNow.AddSeconds(1);
        await service.TransitionStatusAsync(new(created.RequestId,
            ReportRequestStatus.Validating, now), CancellationToken.None);
        await service.RequestClarificationAsync(new(created.RequestId,
            "Which region?", now.AddSeconds(1)), CancellationToken.None);

        var failingService = fixture.CreateService(new FailingAuditWriter());
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            failingService.SubmitClarificationAsync(new(
                created.RequestId, "Sao Paulo", now.AddSeconds(2)),
                User(), CancellationToken.None));

        fixture.Context.ChangeTracker.Clear();
        var persisted = await fixture.Reports.GetByIdAsync(
            created.RequestId, CancellationToken.None);
        Assert.Null(persisted!.ClarificationResponse);
        Assert.Equal(ReportRequestStatus.WaitingForClarification,
            persisted.Status);

        await service.SubmitClarificationAsync(new(
            created.RequestId, "Sao Paulo", now.AddSeconds(3)),
            User(), CancellationToken.None);
        fixture.Context.ChangeTracker.Clear();
        persisted = await fixture.Reports.GetByIdAsync(
            created.RequestId, CancellationToken.None);
        Assert.Equal("Sao Paulo", persisted!.ClarificationResponse);
    }

    [Theory]
    [InlineData("Complete", "ReportCompleted", "Succeeded", null)]
    [InlineData("Reject", "ReportRejected", "Rejected", "REJECTED_CODE")]
    [InlineData("Fail", "ReportFailed", "Failed", "FAILED_CODE")]
    public async Task TerminalMutationAndAudit_CommitTogether(
        string operation,
        string eventType,
        string outcome,
        string? reasonCode)
    {
        await using var fixture = await SqliteFixture.CreateAsync();
        var service = fixture.CreateService(new SqlApplicationAuditWriter(
            fixture.Context));
        var created = await service.CreateAsync(
            new CreateReportRequestCommand(
                "Sales", $"terminal-{operation}", null,
                $"correlation-{operation}"), User(), CancellationToken.None);
        var now = DateTimeOffset.UtcNow.AddSeconds(1);
        if (operation != "Fail")
        {
            await service.TransitionStatusAsync(new(created.RequestId,
                ReportRequestStatus.Validating, now), CancellationToken.None);
            await service.TransitionStatusAsync(new(created.RequestId,
                ReportRequestStatus.Processing, now.AddSeconds(1)),
                CancellationToken.None);
        }

        switch (operation)
        {
            case "Complete":
                await service.CompleteAsync(new(created.RequestId,
                    "report", "summary", "https://app.powerbi.com/report",
                    now.AddSeconds(2)), CancellationToken.None);
                break;
            case "Reject":
                await service.RejectAsync(new(created.RequestId,
                    reasonCode!, "safe rejection", now.AddSeconds(2)),
                    CancellationToken.None);
                break;
            default:
                await service.FailAsync(new(created.RequestId,
                    reasonCode!, "technical detail", now),
                    CancellationToken.None);
                break;
        }

        var command = fixture.Context.Database.GetDbConnection()
            .CreateCommand();
        command.CommandText =
            "SELECT COUNT(*) FROM ApplicationAuditEvents "
            + "WHERE EventType = $eventType AND Outcome = $outcome "
            + "AND (($reason IS NULL AND ReasonCode IS NULL) "
            + "OR ReasonCode = $reason)";
        command.Parameters.Add(new SqliteParameter("$eventType", eventType));
        command.Parameters.Add(new SqliteParameter("$outcome", outcome));
        command.Parameters.Add(new SqliteParameter(
            "$reason", (object?)reasonCode ?? DBNull.Value));
        Assert.Equal(1L, Convert.ToInt64(await command.ExecuteScalarAsync()));
    }

    [Fact]
    public async Task TerminalMutations_WriteMinimizedAuditInSameTransaction()
    {
        await using var fixture = await SqliteFixture.CreateAsync();
        var service = fixture.CreateService(new SqlApplicationAuditWriter(
            fixture.Context));
        var created = await service.CreateAsync(
            new CreateReportRequestCommand(
                "Sales", "conversation-terminal", null, "correlation-2"),
            User(), CancellationToken.None);
        var now = DateTimeOffset.UtcNow.AddSeconds(1);
        await service.TransitionStatusAsync(new(
            created.RequestId, ReportRequestStatus.Validating, now),
            CancellationToken.None);
        await service.TransitionStatusAsync(new(
            created.RequestId, ReportRequestStatus.Processing,
            now.AddSeconds(1)), CancellationToken.None);
        await service.RejectAsync(new(
            created.RequestId, "POLICY_REJECTED", "sensitive message",
            now.AddSeconds(2)), CancellationToken.None);

        Assert.Equal(2, await fixture.CountAsync("ApplicationAuditEvents"));
        var command = fixture.Context.Database.GetDbConnection()
            .CreateCommand();
        command.CommandText =
            "SELECT ReasonCode FROM ApplicationAuditEvents "
            + "WHERE EventType = 'ReportRejected'";
        Assert.Equal("POLICY_REJECTED",
            Convert.ToString(await command.ExecuteScalarAsync()));
        Assert.DoesNotContain(
            fixture.Context.Model.GetEntityTypes()
                .Single(value => value.GetTableName()
                    == "ApplicationAuditEvents")
                .GetProperties().Select(value => value.Name),
            name => name is "Prompt" or "RejectionMessage"
                or "ErrorMessage" or "CanonicalRequestJson");
    }

    [Fact]
    public async Task SqlAuditWriter_RoundTripsStringsAndIsIdempotent()
    {
        await using var fixture = await SqliteFixture.CreateAsync();
        var writer = new SqlApplicationAuditWriter(fixture.Context);
        var at = new DateTimeOffset(
            2026, 7, 31, 12, 0, 0, TimeSpan.Zero);
        foreach (var type in Enum.GetValues<ApplicationAuditEventType>())
        {
            var outcome = type switch
            {
                ApplicationAuditEventType.ReportRejected =>
                    ApplicationAuditOutcome.Rejected,
                ApplicationAuditEventType.ReportFailed
                    or ApplicationAuditEventType.QueryExecutionFailed =>
                    ApplicationAuditOutcome.Failed,
                _ => ApplicationAuditOutcome.Succeeded
            };
            var auditEvent = ApplicationAuditEventFactory.Create(
                type, outcome, at,
                "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", null,
                "correlation", UserId, TenantId, "Received");
            await writer.AppendAsync(auditEvent, CancellationToken.None);
            await writer.AppendAsync(auditEvent, CancellationToken.None);
        }

        Assert.Equal(11, await fixture.CountAsync("ApplicationAuditEvents"));
        var command = fixture.Context.Database.GetDbConnection()
            .CreateCommand();
        command.CommandText =
            "SELECT COUNT(*) FROM ApplicationAuditEvents "
            + "WHERE EventType = 'ReportFailed' AND Outcome = 'Failed'";
        Assert.Equal(1L, Convert.ToInt64(await command.ExecuteScalarAsync()));

        var original = ApplicationAuditEventFactory.Create(
            ApplicationAuditEventType.ReportCreated,
            ApplicationAuditOutcome.Succeeded,
            at, "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", null,
            "correlation", UserId, TenantId, "Received");
        var conflict = new ApplicationAuditEvent(
            original.EventId, original.EventType, original.Outcome,
            original.OccurredAt, original.RequestId,
            original.PreviousRequestId, "different-correlation",
            original.ActorUserId, original.TenantId,
            original.ReportStatus, original.ReasonCode);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            writer.AppendAsync(conflict, CancellationToken.None));
    }

    [Fact]
    public async Task SqlAuditWriter_PersistsVersionedJsonAndReadsLegacyNull()
    {
        await using var fixture = await SqliteFixture.CreateAsync();
        var writer = new SqlApplicationAuditWriter(fixture.Context);
        var at = new DateTimeOffset(
            2026, 8, 5, 10, 0, 0, TimeSpan.Zero);
        var requestId = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        var legacy = ApplicationAuditEventFactory.Create(
            ApplicationAuditEventType.ReportCreated,
            ApplicationAuditOutcome.Succeeded,
            at,
            requestId,
            null,
            "correlation",
            UserId,
            TenantId,
            "Received");
        await writer.AppendAsync(legacy, CancellationToken.None);

        var plan = new SqlExecutionPlan(
            SqlDataSource.Dwh,
            "SELECT amount FROM mart.vw_sales WHERE region = @region",
            [new("@region", SqlExecutionParameterKind.Text, "SP", true)],
            "region = @region",
            30,
            null,
            "mart.vw_sales",
            5000);
        var metadata = ApplicationAuditMetadata.Create(
            "conversation-1", plan, 2, "delivery-2");
        var queryAudit = ApplicationAuditEventFactory.Create(
            ApplicationAuditEventType.QueryExecutionStarted,
            ApplicationAuditOutcome.Succeeded,
            at.AddTicks(1),
            requestId,
            null,
            "correlation",
            UserId,
            TenantId,
            "Processing",
            dataSource: "Dwh",
            auditMetadata: metadata);
        await writer.AppendAsync(queryAudit, CancellationToken.None);

        var command = fixture.Context.Database.GetDbConnection()
            .CreateCommand();
        command.CommandText = "SELECT AuditMetadataJson "
            + "FROM ApplicationAuditEvents WHERE EventType = "
            + "'QueryExecutionStarted'";
        var json = Assert.IsType<string>(await command.ExecuteScalarAsync());
        Assert.Equal(metadata,
            ApplicationAuditMetadataSerializer.Deserialize(json));
        command.CommandText = "SELECT COUNT(*) FROM ApplicationAuditEvents "
            + "WHERE EventType = 'ReportCreated' "
            + "AND AuditMetadataJson IS NULL";
        Assert.Equal(1L, Convert.ToInt64(
            await command.ExecuteScalarAsync()));
    }

    [Fact]
    public async Task NestedRunner_JoinsCurrentTransaction_AndCancellationRollsBack()
    {
        await using var fixture = await SqliteFixture.CreateAsync();
        var runner = new EfCoreApplicationTransactionRunner(fixture.Context);
        object? outerTransaction = null;
        await runner.ExecuteAsync(async token =>
        {
            outerTransaction = fixture.Context.Database.CurrentTransaction;
            await runner.ExecuteAsync(innerToken =>
            {
                Assert.Same(outerTransaction,
                    fixture.Context.Database.CurrentTransaction);
                return Task.CompletedTask;
            }, token);
        }, CancellationToken.None);

        using var cancellation = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            runner.ExecuteAsync(async token =>
            {
                var report = ReportRequest.Create(
                    Guid.NewGuid().ToString("N"), "cancelled-conversation",
                    null, "Sales", "cancelled", UserId, TenantId);
                await fixture.Reports.AddAsync(report, token);
                cancellation.Cancel();
                token.ThrowIfCancellationRequested();
            }, cancellation.Token));
        fixture.Context.ChangeTracker.Clear();
        Assert.Equal(0, await fixture.CountAsync("ReportRequests"));
    }

    [Fact]
    public async Task OutboxFailureRollsBackReportConversationAndAudit()
    {
        await using var fixture = await SqliteFixture.CreateAsync();
        var service = fixture.CreateService(
            new SqlApplicationAuditWriter(fixture.Context),
            new FailingOutboxWriter());
        var submission = new ReportRequestSubmissionService(service);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            submission.SubmitAsync(
                new CreateReportRequestCommand(
                    "Sales", "conversation-queue", null, "correlation-3"),
                User(), Scope(), CancellationToken.None));

        fixture.Context.ChangeTracker.Clear();
        Assert.Equal(0, await fixture.CountAsync("ReportRequests"));
        Assert.Equal(0, await fixture.CountAsync("Conversations"));
        Assert.Equal(0, await fixture.CountAsync("ApplicationAuditEvents"));
    }

    [Fact]
    public async Task TerminalMutationAndNotificationOutboxCommitTogether_AndRollbackTogether()
    {
        await using var fixture = await SqliteFixture.CreateAsync();
        var audit = new SqlApplicationAuditWriter(fixture.Context);
        var service = fixture.CreateService(audit,
            new SqlOutboxWriter(fixture.Context));
        var created = await service.CreateAsync(
            new CreateReportRequestCommand("Sales", "conversation-terminal",
                null, "correlation-terminal"), User(), CancellationToken.None);
        var report = (await fixture.Reports.GetByIdAsync(created.RequestId,
            CancellationToken.None))!;
        await service.TransitionStatusAsync(
            new TransitionReportRequestStatusCommand(created.RequestId,
                ReportRequestStatus.Validating,
                report.UpdatedAt.AddTicks(1)), CancellationToken.None);
        report = (await fixture.Reports.GetByIdAsync(created.RequestId,
            CancellationToken.None))!;
        await service.TransitionStatusAsync(
            new TransitionReportRequestStatusCommand(created.RequestId,
                ReportRequestStatus.Processing,
                report.UpdatedAt.AddTicks(1)), CancellationToken.None);
        report = (await fixture.Reports.GetByIdAsync(created.RequestId,
            CancellationToken.None))!;
        await service.CompleteAsync(new CompleteReportRequestCommand(
            created.RequestId, "report", "summary",
            "https://app.powerbi.com/report",
            report.UpdatedAt.AddTicks(1)), CancellationToken.None);

        var command = fixture.Context.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM OutboxMessages "
            + "WHERE MessageType = 'ReportNotificationRequested'";
        Assert.Equal(1L, Convert.ToInt64(await command.ExecuteScalarAsync()));
        fixture.Context.ChangeTracker.Clear();

        var second = await service.CreateAsync(
            new CreateReportRequestCommand("Sales", "conversation-rollback",
                null, "correlation-rollback"), User(), CancellationToken.None);
        var secondReport = (await fixture.Reports.GetByIdAsync(second.RequestId,
            CancellationToken.None))!;
        await service.TransitionStatusAsync(
            new TransitionReportRequestStatusCommand(second.RequestId,
                ReportRequestStatus.Validating,
                secondReport.UpdatedAt.AddTicks(1)), CancellationToken.None);
        secondReport = (await fixture.Reports.GetByIdAsync(second.RequestId,
            CancellationToken.None))!;
        await service.TransitionStatusAsync(
            new TransitionReportRequestStatusCommand(second.RequestId,
                ReportRequestStatus.Processing,
                secondReport.UpdatedAt.AddTicks(1)), CancellationToken.None);
        secondReport = (await fixture.Reports.GetByIdAsync(second.RequestId,
            CancellationToken.None))!;
        var failing = fixture.CreateService(audit,
            new NotificationFailingOutboxWriter(
                new SqlOutboxWriter(fixture.Context)));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            failing.CompleteAsync(new CompleteReportRequestCommand(
                second.RequestId, "report", "summary",
                "https://app.powerbi.com/report",
                secondReport.UpdatedAt.AddTicks(1)), CancellationToken.None));

        fixture.Context.ChangeTracker.Clear();
        var rolledBack = await fixture.Reports.GetByIdAsync(second.RequestId,
            CancellationToken.None);
        Assert.Equal(ReportRequestStatus.Processing, rolledBack!.Status);
        command.CommandText = "SELECT COUNT(*) FROM OutboxMessages "
            + $"WHERE MessageType = 'ReportNotificationRequested' "
            + $"AND AggregateId = '{second.RequestId}'";
        Assert.Equal(0L, Convert.ToInt64(await command.ExecuteScalarAsync()));
    }

    [Fact]
    public async Task SqlAssignmentStore_IsExactNoTrackingAndFailClosed()
    {
        await using var fixture = await SqliteFixture.CreateAsync();
        var assignmentEntity = fixture.Context.Model.GetEntityTypes()
            .Single(value => value.GetTableName()
                == "UserDataAccessAssignments");
        var converter = assignmentEntity.FindProperty("CreatedAt")!
            .GetTypeMapping().Converter!;
        var now = converter.ConvertToProvider(DateTimeOffset.UtcNow)!;
        await fixture.Context.Database.ExecuteSqlRawAsync(
            "INSERT INTO UserDataAccessAssignments "
            + "(TenantId, UserId, AllowAllRegions, AllowAllStores, "
            + "IsActive, CreatedAt, UpdatedAt) VALUES "
            + "({0}, {1}, 0, 1, 1, {2}, {2})",
            TenantId, UserId, now);
        await fixture.Context.Database.ExecuteSqlRawAsync(
            "INSERT INTO UserDataAccessRegions "
            + "(TenantId, UserId, RegionCode) VALUES ({0}, {1}, 'SP')",
            TenantId, UserId);

        var store = new SqlUserDataAccessAssignmentStore(fixture.Context);
        var assignment = await store.FindAsync(
            TenantId, UserId, CancellationToken.None);
        Assert.NotNull(assignment);
        Assert.Equal(new[] { "SP" }, assignment!.AllowedRegions);
        Assert.True(assignment.AllowAllStores);
        Assert.Empty(fixture.Context.ChangeTracker.Entries());
        Assert.Null(await store.FindAsync(
            "33333333-3333-4333-8333-333333333333",
            UserId, CancellationToken.None));

        var activeInspection = await store.InspectAsync(
            TenantId, UserId, CancellationToken.None);
        Assert.True(activeInspection.AssignmentFound);
        Assert.True(activeInspection.IsActive);
        Assert.False(activeInspection.AllowAllRegions);
        Assert.True(activeInspection.AllowAllStores);

        await fixture.Context.Database.ExecuteSqlRawAsync(
            "UPDATE UserDataAccessAssignments SET IsActive = 0");
        var inactiveInspection = await store.InspectAsync(
            TenantId, UserId, CancellationToken.None);
        Assert.True(inactiveInspection.AssignmentFound);
        Assert.False(inactiveInspection.IsActive);
        Assert.Null(await store.FindAsync(
            TenantId, UserId, CancellationToken.None));

        await fixture.Context.Database.ExecuteSqlRawAsync(
            "UPDATE UserDataAccessAssignments "
            + "SET IsActive = 1, AllowAllRegions = 1");
        await Assert.ThrowsAsync<DataAccessPolicyIntegrityException>(() =>
            store.FindAsync(TenantId, UserId, CancellationToken.None));
    }

    private static AuthenticatedUserContext User() => new(
        UserId, TenantId, new[] { "Report.User" });

    private static UserDataScope Scope() => new()
    {
        UserId = UserId,
        TenantId = TenantId,
        Roles = new[] { "Report.User" },
        AllowAllRegions = true,
        AllowAllStores = true
    };

    private sealed class FailingAuditWriter : IApplicationAuditWriter
    {
        public Task AppendAsync(ApplicationAuditEvent auditEvent,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Audit unavailable.");
    }

    private sealed class FailingOutboxWriter : IOutboxWriter
    {
        public Task AppendAsync(OutboxMessage message,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Queue unavailable.");
    }

    private sealed class NotificationFailingOutboxWriter(IOutboxWriter inner)
        : IOutboxWriter
    {
        public Task AppendAsync(OutboxMessage message,
            CancellationToken cancellationToken) =>
            message.MessageType == OutboxMessageType.ReportNotificationRequested
                ? throw new InvalidOperationException("Notification unavailable.")
                : inner.AppendAsync(message, cancellationToken);
    }

    private sealed class SqliteFixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private SqliteFixture(SqliteConnection connection,
            CrmAnalyticsDbContext context)
        {
            _connection = connection;
            Context = context;
            Reports = new SqlReportRequestRepository(context);
            Conversations = new SqlConversationRepository(context);
        }

        public CrmAnalyticsDbContext Context { get; }
        public SqlReportRequestRepository Reports { get; }
        public SqlConversationRepository Conversations { get; }

        public static async Task<SqliteFixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<CrmAnalyticsDbContext>()
                .UseSqlite(connection).Options;
            var context = new CrmAnalyticsDbContext(options);
            await context.Database.EnsureCreatedAsync();
            return new SqliteFixture(connection, context);
        }

        public ReportRequestService CreateService(IApplicationAuditWriter writer)
        {
            var conversations = new ConversationContextService(
                Conversations, Reports);
            return new ReportRequestService(
                Reports, conversations,
                new EfCoreApplicationTransactionRunner(Context), writer);
        }

        public ReportRequestService CreateService(
            IApplicationAuditWriter writer,
            IOutboxWriter outboxWriter)
        {
            var conversations = new ConversationContextService(
                Conversations, Reports);
            return new ReportRequestService(
                Reports, conversations,
                new EfCoreApplicationTransactionRunner(Context), writer,
                outboxWriter,
                new OutboxMessageFactory(new OutboxMessageSerializer()));
        }

        public async Task<long> CountAsync(string table)
        {
            var command = _connection.CreateCommand();
            command.CommandText = $"SELECT COUNT(*) FROM {table}";
            return Convert.ToInt64(await command.ExecuteScalarAsync());
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }
}
