using CrmAnalytics.Domain.Conversations;
using CrmAnalytics.Domain.ReportRequests;
using CrmAnalytics.Infrastructure.Persistence.SqlServer;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace CrmAnalytics.IntegrationTests;

public sealed class SqlPersistenceRepositoryTests : IAsyncLifetime
{
    private const string UserId =
        "11111111-1111-4111-8111-111111111111";
    private const string TenantId =
        "22222222-2222-4222-8222-222222222222";

    private readonly SqliteConnection _connection =
        new("Data Source=:memory:");
    private DbContextOptions<CrmAnalyticsDbContext> _options = null!;

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        _connection.CreateCollation(
            "Latin1_General_100_CI_AS",
            (left, right) => string.Compare(
                left,
                right,
                StringComparison.OrdinalIgnoreCase));

        _options =
            new DbContextOptionsBuilder<CrmAnalyticsDbContext>()
                .UseSqlite(_connection)
                .Options;

        await using var context =
            new CrmAnalyticsDbContext(_options);
        await context.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task ReportRequest_ReceivedAndCanonicalValues_RoundTrip()
    {
        const string canonical =
            "{ \"keep\" : [ 3, 2, 1 ], \"spacing\" : true }";
        var reportRequest = CreateReportRequest("received-request");
        reportRequest.TransitionTo(
            ReportRequestStatus.Validating,
            Next(reportRequest));
        reportRequest.RecordCanonicalRequest(
            canonical,
            Next(reportRequest));

        await AddReportRequestAsync(reportRequest);

        await using var readContext =
            new CrmAnalyticsDbContext(_options);
        var repository =
            new SqlReportRequestRepository(readContext);
        var loaded = await repository.GetByIdAsync(
            reportRequest.RequestId,
            CancellationToken.None);
        Assert.NotNull(loaded);

        Assert.Equal(canonical, loaded!.CanonicalRequestJson);
        Assert.Equal(reportRequest.ReferenceDate, loaded.ReferenceDate);
        Assert.Equal(
            reportRequest.PreviousRequestId,
            loaded.PreviousRequestId);
    }

    [Fact]
    public async Task CompletedAndFailedValues_RoundTrip()
    {
        var completed = CreateReportRequest("completed-request");
        completed.TransitionTo(
            ReportRequestStatus.Validating,
            Next(completed));
        completed.TransitionTo(
            ReportRequestStatus.Processing,
            Next(completed));
        completed.Complete(
            "report-1",
            "summary",
            "https://app.powerbi.com/reports/1",
            Next(completed));

        var failed = CreateReportRequest("failed-request");
        failed.Fail(
            "TECHNICAL_ERROR",
            "Safe technical failure.",
            Next(failed));

        await AddReportRequestAsync(completed);
        await AddReportRequestAsync(failed);

        await using var context =
            new CrmAnalyticsDbContext(_options);
        var repository = new SqlReportRequestRepository(context);
        var completedRoundTrip = await repository.GetByIdAsync(
            completed.RequestId,
            CancellationToken.None);
        var failedRoundTrip = await repository.GetByIdAsync(
            failed.RequestId,
            CancellationToken.None);
        Assert.NotNull(completedRoundTrip);
        Assert.NotNull(failedRoundTrip);

        Assert.Equal("report-1", completedRoundTrip!.ReportId);
        Assert.Equal("summary", completedRoundTrip.Summary);
        Assert.Equal(
            "https://app.powerbi.com/reports/1",
            completedRoundTrip.PowerBiUrl);
        Assert.Equal(ReportRequestStatus.Failed, failedRoundTrip!.Status);
        Assert.Equal("TECHNICAL_ERROR", failedRoundTrip.ErrorCode);
        Assert.Equal(
            "Safe technical failure.",
            failedRoundTrip.ErrorMessage);
    }

    [Fact]
    public async Task Rejected_RoundTripsAndRemainsTerminal()
    {
        var reportRequest = CreateReportRequest("rejected-request");
        reportRequest.TransitionTo(
            ReportRequestStatus.Validating,
            Next(reportRequest));
        reportRequest.Reject(
            "POLICY_REJECTED",
            "Bu talep güvenli biçimde reddedildi.",
            Next(reportRequest));

        await AddReportRequestAsync(reportRequest);

        await using var context =
            new CrmAnalyticsDbContext(_options);
        var repository = new SqlReportRequestRepository(context);
        var loaded = await repository.GetByIdAsync(
            reportRequest.RequestId,
            CancellationToken.None);
        Assert.NotNull(loaded);

        Assert.Equal(ReportRequestStatus.Rejected, loaded!.Status);
        Assert.Equal("POLICY_REJECTED", loaded.RejectionCode);
        Assert.Equal(
            "Bu talep güvenli biçimde reddedildi.",
            loaded.RejectionMessage);
        Assert.Throws<InvalidOperationException>(
            (Action)(() => loaded.TransitionTo(
                ReportRequestStatus.Processing,
                Next(loaded))));
    }

    [Fact]
    public async Task ClarificationQuestionAndResponse_RoundTrip()
    {
        var reportRequest = CreateReportRequest("clarification-request");
        reportRequest.TransitionTo(
            ReportRequestStatus.Validating,
            Next(reportRequest));
        reportRequest.RequestClarification(
            "Hangi bölge?",
            Next(reportRequest));
        reportRequest.SubmitClarificationResponse(
            "Marmara bölgesi",
            Next(reportRequest));

        await AddReportRequestAsync(reportRequest);

        await using var context =
            new CrmAnalyticsDbContext(_options);
        var repository = new SqlReportRequestRepository(context);
        var loaded = await repository.GetByIdAsync(
            reportRequest.RequestId,
            CancellationToken.None);
        Assert.NotNull(loaded);

        Assert.Equal(
            ReportRequestStatus.WaitingForClarification,
            loaded!.Status);
        Assert.Equal("Hangi bölge?", loaded.ClarificationQuestion);
        Assert.Equal(
            "Marmara bölgesi",
            loaded.ClarificationResponse);
    }

    [Fact]
    public async Task History_IsOrderedAndScopedByConversation()
    {
        var first = CreateReportRequest(
            "00000000000000000000000000000001",
            "conversation-history");
        await AddReportRequestAsync(first);
        await Task.Delay(5);
        var second = CreateReportRequest(
            "00000000000000000000000000000002",
            "conversation-history",
            first.RequestId);
        await AddReportRequestAsync(second);
        await AddReportRequestAsync(
            CreateReportRequest(
                "00000000000000000000000000000003",
                "other-conversation"));

        await using var context =
            new CrmAnalyticsDbContext(_options);
        var repository = new SqlReportRequestRepository(context);
        var history = await repository.GetByConversationIdAsync(
            "conversation-history",
            CancellationToken.None);

        Assert.Equal(
            new[] { first.RequestId, second.RequestId },
            history.Select(item => item.RequestId));
        Assert.All(
            history,
            item => Assert.Equal(
                "conversation-history",
                item.ConversationId));
    }

    [Fact]
    public async Task DuplicateRequestIdAndMissingUpdate_AreRejected()
    {
        await AddReportRequestAsync(
            CreateReportRequest("duplicate-request"));

        await using var context =
            new CrmAnalyticsDbContext(_options);
        var repository = new SqlReportRequestRepository(context);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => repository.AddAsync(
                CreateReportRequest("duplicate-request"),
                CancellationToken.None));
        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => repository.UpdateAsync(
                CreateReportRequest("missing-request"),
                CancellationToken.None));
    }

    [Fact]
    public async Task Conversation_AddLookupUpdateAndDuplicate_AreRelational()
    {
        var createdAt = DateTimeOffset.UtcNow;
        var conversation = Conversation.Create(
            "conversation-id-1",
            "Teams-Conversation-1",
            "request-1",
            createdAt,
            UserId,
            TenantId);

        await using (var addContext =
                     new CrmAnalyticsDbContext(_options))
        {
            await new SqlConversationRepository(addContext)
                .AddAsync(
                    conversation,
                    CancellationToken.None);
        }

        await using (var updateContext =
                     new CrmAnalyticsDbContext(_options))
        {
            var repository =
                new SqlConversationRepository(updateContext);
            var loaded =
                await repository.GetByTeamsConversationIdAsync(
                    "teams-conversation-1",
                    CancellationToken.None);
            Assert.NotNull(loaded);
            loaded!.UpdateLastRequest(
                "request-2",
                createdAt.AddMinutes(1));
            await repository.UpdateAsync(
                loaded,
                CancellationToken.None);
        }

        await using var verifyContext =
            new CrmAnalyticsDbContext(_options);
        var verifyRepository =
            new SqlConversationRepository(verifyContext);
        var updated =
            await verifyRepository.GetByTeamsConversationIdAsync(
                "TEAMS-CONVERSATION-1",
                CancellationToken.None);
        Assert.NotNull(updated);
        Assert.Equal("request-2", updated!.LastRequestId);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => verifyRepository.AddAsync(
                Conversation.Create(
                    "conversation-id-2",
                    "teams-conversation-1",
                    "request-3",
                    createdAt,
                    UserId,
                    TenantId),
                CancellationToken.None));
    }

    [Fact]
    public async Task Cancellation_IsPropagated()
    {
        await using var context =
            new CrmAnalyticsDbContext(_options);
        var repository = new SqlReportRequestRepository(context);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => repository.GetByIdAsync(
                "any-request",
                cancellation.Token));
    }

    [Fact]
    public async Task TwoContexts_TranslateDetectedConcurrencyConflict()
    {
        var reportRequest =
            CreateReportRequest("concurrency-request");
        await AddReportRequestAsync(reportRequest);

        await using (var triggerContext =
                     new CrmAnalyticsDbContext(_options))
        {
            await triggerContext.Database.ExecuteSqlRawAsync(
                """
                CREATE TRIGGER SetReportRequestRowVersion
                AFTER UPDATE ON ReportRequests
                BEGIN
                    UPDATE ReportRequests
                    SET RowVersion = randomblob(8)
                    WHERE RequestId = NEW.RequestId;
                END;
                """);
        }

        await using var firstContext =
            new CrmAnalyticsDbContext(_options);
        await using var secondContext =
            new CrmAnalyticsDbContext(_options);
        var firstRepository =
            new SqlReportRequestRepository(firstContext);
        var secondRepository =
            new SqlReportRequestRepository(secondContext);
        var first = await firstRepository.GetByIdAsync(
            reportRequest.RequestId,
            CancellationToken.None);
        var second = await secondRepository.GetByIdAsync(
            reportRequest.RequestId,
            CancellationToken.None);
        Assert.NotNull(first);
        Assert.NotNull(second);

        first!.TransitionTo(
            ReportRequestStatus.Validating,
            Next(first));
        second!.TransitionTo(
            ReportRequestStatus.Validating,
            Next(second));

        await firstRepository.UpdateAsync(
            first,
            CancellationToken.None);
        await Assert.ThrowsAsync<
            CrmAnalytics.Application.Exceptions
                .PersistenceConcurrencyException>(
            () => secondRepository.UpdateAsync(
                second,
                CancellationToken.None));
    }

    private async Task AddReportRequestAsync(
        ReportRequest reportRequest)
    {
        await using var context =
            new CrmAnalyticsDbContext(_options);
        await new SqlReportRequestRepository(context)
            .AddAsync(reportRequest, CancellationToken.None);
    }

    private static ReportRequest CreateReportRequest(
        string requestId,
        string conversationId = "conversation-1",
        string? previousRequestId = null)
    {
        return ReportRequest.Create(
            requestId,
            conversationId,
            previousRequestId,
            "Son 30 günün satış trendini göster.",
            $"correlation-{requestId}",
            UserId,
            TenantId);
    }

    private static DateTimeOffset Next(ReportRequest reportRequest)
    {
        return reportRequest.UpdatedAt.AddMilliseconds(1);
    }
}
