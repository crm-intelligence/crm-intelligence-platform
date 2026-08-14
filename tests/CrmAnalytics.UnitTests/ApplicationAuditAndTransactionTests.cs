using CrmAnalytics.Application.Abstractions.Persistence;
using CrmAnalytics.Application.Auditing;
using CrmAnalytics.Infrastructure.Auditing;
using CrmAnalytics.Infrastructure.Persistence;

namespace CrmAnalytics.UnitTests;

public sealed class ApplicationAuditAndTransactionTests
{
    [Theory]
    [InlineData(ApplicationAuditEventType.ReportCreated,
        ApplicationAuditOutcome.Succeeded)]
    [InlineData(ApplicationAuditEventType.ReportRevisionCreated,
        ApplicationAuditOutcome.Succeeded)]
    [InlineData(ApplicationAuditEventType.ReportClarificationSubmitted,
        ApplicationAuditOutcome.Succeeded)]
    [InlineData(ApplicationAuditEventType.CanonicalRequestRecorded,
        ApplicationAuditOutcome.Succeeded)]
    [InlineData(ApplicationAuditEventType.ReportClarificationRequested,
        ApplicationAuditOutcome.Succeeded)]
    [InlineData(ApplicationAuditEventType.ReportCompleted,
        ApplicationAuditOutcome.Succeeded)]
    [InlineData(ApplicationAuditEventType.ReportRejected,
        ApplicationAuditOutcome.Rejected)]
    [InlineData(ApplicationAuditEventType.ReportFailed,
        ApplicationAuditOutcome.Failed)]
    [InlineData(ApplicationAuditEventType.QueryExecutionStarted,
        ApplicationAuditOutcome.Succeeded)]
    [InlineData(ApplicationAuditEventType.QueryExecutionSucceeded,
        ApplicationAuditOutcome.Succeeded)]
    [InlineData(ApplicationAuditEventType.QueryExecutionFailed,
        ApplicationAuditOutcome.Failed)]
    public async Task EveryEventType_IsAppendOnlyAndRoundTrips(
        ApplicationAuditEventType type,
        ApplicationAuditOutcome outcome)
    {
        var writer = new InMemoryApplicationAuditWriter();
        var value = Create(type, outcome);
        await writer.AppendAsync(value, CancellationToken.None);
        await writer.AppendAsync(value, CancellationToken.None);

        Assert.Equal(value, Assert.Single(writer.Snapshot));
        Assert.DoesNotContain("actor", value.ToString(),
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("tenant", value.ToString(),
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("correlation", value.ToString(),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void QueryMetadata_IsBoundedAndContainsNoDataPayloadFields()
    {
        var audit = ApplicationAuditEventFactory.Create(
            ApplicationAuditEventType.QueryExecutionSucceeded,
            ApplicationAuditOutcome.Succeeded,
            new DateTimeOffset(2026, 7, 31, 10, 0, 0, TimeSpan.Zero),
            "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
            null,
            "correlation",
            "actor",
            "tenant",
            "Processing",
            dataSource: "Dwh",
            durationMilliseconds: 125,
            rowCount: 42,
            resultTruncated: false);

        Assert.Equal("Dwh", audit.DataSource);
        Assert.Equal(125, audit.DurationMilliseconds);
        Assert.Equal(42, audit.RowCount);
        Assert.False(audit.ResultTruncated);
        var propertyNames = typeof(ApplicationAuditEvent)
            .GetProperties()
            .Select(property => property.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("Sql", propertyNames);
        Assert.DoesNotContain("Parameters", propertyNames);
        Assert.DoesNotContain("Columns", propertyNames);
        Assert.DoesNotContain("Rows", propertyNames);
        Assert.DoesNotContain("AppliedScopeFilter", propertyNames);
        Assert.DoesNotContain("ResultReference", propertyNames);
    }

    [Fact]
    public async Task SameEventIdWithDifferentSafeContent_Conflicts()
    {
        var writer = new InMemoryApplicationAuditWriter();
        var value = Create(ApplicationAuditEventType.ReportCreated,
            ApplicationAuditOutcome.Succeeded);
        await writer.AppendAsync(value, CancellationToken.None);
        var conflict = new ApplicationAuditEvent(
            value.EventId, value.EventType, value.Outcome,
            value.OccurredAt, value.RequestId, value.PreviousRequestId,
            "different", value.ActorUserId, value.TenantId,
            value.ReportStatus, value.ReasonCode);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            writer.AppendAsync(conflict, CancellationToken.None));
    }

    [Fact]
    public void Factory_IsDeterministic_AndRejectsNonUtcTime()
    {
        var first = Create(ApplicationAuditEventType.ReportCreated,
            ApplicationAuditOutcome.Succeeded);
        var second = Create(ApplicationAuditEventType.ReportCreated,
            ApplicationAuditOutcome.Succeeded);
        Assert.Equal(first.EventId, second.EventId);
        Assert.Equal(64, first.EventId.Length);

        Assert.Throws<ArgumentException>(() => new ApplicationAuditEvent(
            first.EventId, first.EventType, first.Outcome,
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.FromHours(3)),
            first.RequestId, null, null, null, null,
            null, null));
    }

    [Fact]
    public async Task InMemoryRunner_PropagatesCancellationAndExceptions()
    {
        IApplicationTransactionRunner runner =
            new InMemoryApplicationTransactionRunner();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            runner.ExecuteAsync(_ =>
                throw new InvalidOperationException("expected"),
                CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            runner.ExecuteAsync(null!, CancellationToken.None));

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            runner.ExecuteAsync(token =>
            {
                token.ThrowIfCancellationRequested();
                return Task.CompletedTask;
            }, cancellation.Token));
    }

    private static ApplicationAuditEvent Create(
        ApplicationAuditEventType type,
        ApplicationAuditOutcome outcome) =>
        ApplicationAuditEventFactory.Create(
            type, outcome,
            new DateTimeOffset(2026, 7, 31, 10, 0, 0, TimeSpan.Zero),
            "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", null,
            "correlation", "actor", "tenant", "Received",
            type is ApplicationAuditEventType.ReportRejected
                or ApplicationAuditEventType.ReportFailed
                or ApplicationAuditEventType.QueryExecutionFailed
                ? "SAFE_CODE"
                : null);
}
