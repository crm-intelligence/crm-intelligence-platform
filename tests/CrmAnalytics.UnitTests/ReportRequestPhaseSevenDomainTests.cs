using CrmAnalytics.Application.Abstractions.Persistence;
using CrmAnalytics.Application.Conversations;
using CrmAnalytics.Application.Identity;
using CrmAnalytics.Application.ReportRequests;
using CrmAnalytics.Domain.ReportRequests;

namespace CrmAnalytics.UnitTests;

public sealed class ReportRequestPhaseSevenDomainTests
{
    [Theory]
    [InlineData(ReportRequestStatus.Validating)]
    [InlineData(ReportRequestStatus.Processing)]
    public void Reject_ActiveRequest_StoresSafeDetails(
        ReportRequestStatus status)
    {
        var request = CreateInStatus(status);
        var rejectedAt = request.UpdatedAt.AddMinutes(1);

        request.Reject(
            "  GR014  ",
            "  Bu talep sorgu politikası nedeniyle işlenemedi.  ",
            rejectedAt);

        Assert.Equal(ReportRequestStatus.Rejected, request.Status);
        Assert.Equal("GR014", request.RejectionCode);
        Assert.Equal(
            "Bu talep sorgu politikası nedeniyle işlenemedi.",
            request.RejectionMessage);
        Assert.Equal(rejectedAt, request.UpdatedAt);
        Assert.Null(request.ErrorCode);
        Assert.Null(request.ErrorMessage);
        Assert.Null(request.ClarificationQuestion);
        Assert.Null(request.ClarificationResponse);
    }

    [Fact]
    public void Reject_AfterClarification_ClearsClarificationState()
    {
        var request = CreateInStatus(ReportRequestStatus.Validating);
        request.RequestClarification(
            "Hangi dönem?",
            request.UpdatedAt.AddMinutes(1));
        request.SubmitClarificationResponse(
            "2018 yılı",
            request.UpdatedAt.AddMinutes(1));
        request.TransitionTo(
            ReportRequestStatus.Validating,
            request.UpdatedAt.AddMinutes(1));

        request.Reject(
            "GR007",
            "Bu veri kapsamı desteklenmiyor.",
            request.UpdatedAt.AddMinutes(1));

        Assert.Null(request.ClarificationQuestion);
        Assert.Null(request.ClarificationResponse);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("gr007")]
    [InlineData("GR-007")]
    [InlineData("GR 007")]
    public void Reject_InvalidCode_PreservesState(string rejectionCode)
    {
        var request = CreateInStatus(ReportRequestStatus.Validating);
        var updatedAt = request.UpdatedAt;

        Assert.ThrowsAny<ArgumentException>(
            () => request.Reject(
                rejectionCode,
                "Güvenli mesaj.",
                updatedAt.AddMinutes(1)));

        Assert.Equal(ReportRequestStatus.Validating, request.Status);
        Assert.Equal(updatedAt, request.UpdatedAt);
        Assert.Null(request.RejectionCode);
        Assert.Null(request.RejectionMessage);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Reject_EmptyMessage_PreservesState(string message)
    {
        var request = CreateInStatus(ReportRequestStatus.Validating);
        var updatedAt = request.UpdatedAt;

        Assert.Throws<ArgumentException>(
            () => request.Reject(
                "GR007",
                message,
                updatedAt.AddMinutes(1)));

        Assert.Equal(ReportRequestStatus.Validating, request.Status);
        Assert.Equal(updatedAt, request.UpdatedAt);
    }

    [Fact]
    public void Reject_InvalidTimestamp_PreservesState()
    {
        var request = CreateInStatus(ReportRequestStatus.Validating);
        var updatedAt = request.UpdatedAt;

        Assert.Throws<ArgumentException>(
            () => request.Reject(
                "GR007",
                "Güvenli mesaj.",
                updatedAt.ToOffset(TimeSpan.FromHours(3))));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => request.Reject(
                "GR007",
                "Güvenli mesaj.",
                updatedAt.AddTicks(-1)));

        Assert.Equal(ReportRequestStatus.Validating, request.Status);
        Assert.Equal(updatedAt, request.UpdatedAt);
        Assert.Null(request.RejectionCode);
    }

    [Fact]
    public void Rejected_IsTerminal()
    {
        var request = CreateRejected();

        Assert.Throws<InvalidOperationException>(
            () => request.Complete(
                "report-1",
                "Özet",
                "https://app.powerbi.com/report/1",
                request.UpdatedAt.AddMinutes(1)));
        Assert.Throws<InvalidOperationException>(
            () => request.Fail(
                "TECHNICAL_FAILURE",
                "Teknik hata.",
                request.UpdatedAt.AddMinutes(1)));
        Assert.Throws<InvalidOperationException>(
            () => request.RequestClarification(
                "Hangi dönem?",
                request.UpdatedAt.AddMinutes(1)));
        Assert.Throws<InvalidOperationException>(
            () => request.TransitionTo(
                ReportRequestStatus.Rejected,
                request.UpdatedAt.AddMinutes(1)));

        Assert.Equal(ReportRequestStatus.Rejected, request.Status);
        Assert.Equal("GR007", request.RejectionCode);
    }

    [Fact]
    public void RecordCanonicalRequest_PreservesSerializerOutputExactly()
    {
        var request = CreateInStatus(ReportRequestStatus.Validating);
        const string canonicalJson =
            "\n{\"decision\":\"needs_clarification\",\"value\":null}\n";
        var recordedAt = request.UpdatedAt.AddMinutes(1);

        request.RecordCanonicalRequest(canonicalJson, recordedAt);

        Assert.Equal(canonicalJson, request.CanonicalRequestJson);
        Assert.Equal(recordedAt, request.UpdatedAt);
        Assert.Equal(ReportRequestStatus.Validating, request.Status);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void RecordCanonicalRequest_EmptyValue_PreservesState(
        string canonicalJson)
    {
        var request = CreateInStatus(ReportRequestStatus.Validating);
        var updatedAt = request.UpdatedAt;

        Assert.Throws<ArgumentException>(
            () => request.RecordCanonicalRequest(
                canonicalJson,
                updatedAt.AddMinutes(1)));

        Assert.Null(request.CanonicalRequestJson);
        Assert.Equal(updatedAt, request.UpdatedAt);
    }

    [Fact]
    public void RecordCanonicalRequest_TerminalOrInvalidTime_PreservesState()
    {
        var completed = CreateInStatus(ReportRequestStatus.Processing);
        completed.Complete(
            "report-1",
            "Özet",
            "https://app.powerbi.com/report/1",
            completed.UpdatedAt.AddMinutes(1));

        Assert.Throws<InvalidOperationException>(
            () => completed.RecordCanonicalRequest(
                "{}",
                completed.UpdatedAt.AddMinutes(1)));
        Assert.Null(completed.CanonicalRequestJson);

        var active = CreateInStatus(ReportRequestStatus.Validating);
        var updatedAt = active.UpdatedAt;
        Assert.Throws<ArgumentException>(
            () => active.RecordCanonicalRequest(
                "{}",
                updatedAt.ToOffset(TimeSpan.FromHours(3))));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => active.RecordCanonicalRequest(
                "{}",
                updatedAt.AddTicks(-1)));
        Assert.Null(active.CanonicalRequestJson);
        Assert.Equal(updatedAt, active.UpdatedAt);
    }

    [Fact]
    public void Create_ReferenceDateComesFromCreatedAtUtc()
    {
        var request = Create();

        Assert.Equal(
            DateOnly.FromDateTime(request.CreatedAt.UtcDateTime),
            request.ReferenceDate);
    }

    [Fact]
    public async Task Service_RecordsCanonicalAndRejectsThroughRepository()
    {
        var request = CreateInStatus(ReportRequestStatus.Validating);
        var repository = new RecordingRepository(request);
        var service = new ReportRequestService(
            repository,
            new NullConversationContextService());

        await service.RecordCanonicalRequestAsync(
            new RecordCanonicalRequestCommand(
                request.RequestId,
                "{\"request\":\"canonical\"}",
                request.UpdatedAt.AddMinutes(1)),
            CancellationToken.None);
        var rejectedAt = request.UpdatedAt.AddMinutes(1);
        var result = await service.RejectAsync(
            new RejectReportRequestCommand(
                request.RequestId,
                "GR007",
                "Bu kapsam desteklenmiyor.",
                rejectedAt),
            CancellationToken.None);

        Assert.Equal(2, repository.UpdateCallCount);
        Assert.Equal(ReportRequestStatus.Rejected, result.Status);
        Assert.Equal("GR007", result.RejectionCode);
        Assert.Equal(
            "Bu kapsam desteklenmiyor.",
            result.RejectionMessage);
        Assert.Equal(
            "{\"request\":\"canonical\"}",
            request.CanonicalRequestJson);
    }

    private static ReportRequest CreateRejected()
    {
        var request = CreateInStatus(ReportRequestStatus.Validating);
        request.Reject(
            "GR007",
            "Bu kapsam desteklenmiyor.",
            request.UpdatedAt.AddMinutes(1));
        return request;
    }

    private static ReportRequest CreateInStatus(
        ReportRequestStatus status)
    {
        var request = Create();
        request.TransitionTo(
            ReportRequestStatus.Validating,
            request.UpdatedAt.AddMinutes(1));
        if (status == ReportRequestStatus.Processing)
        {
            request.TransitionTo(
                ReportRequestStatus.Processing,
                request.UpdatedAt.AddMinutes(1));
        }

        return request;
    }

    private static ReportRequest Create() =>
        ReportRequest.Create(
            "request-1",
            "conversation-1",
            null,
            "2018 satış raporu",
            "correlation-1");

    private sealed class RecordingRepository : IReportRequestRepository
    {
        private readonly ReportRequest _request;

        public RecordingRepository(ReportRequest request)
        {
            _request = request;
        }

        public int UpdateCallCount { get; private set; }

        public Task AddAsync(
            ReportRequest reportRequest,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ReportRequest?> GetByIdAsync(
            string requestId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<ReportRequest?>(
                requestId == _request.RequestId ? _request : null);
        }

        public Task<IReadOnlyList<ReportRequest>>
            GetByConversationIdAsync(
                string conversationId,
                CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task UpdateAsync(
            ReportRequest reportRequest,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            UpdateCallCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class NullConversationContextService
        : IConversationContextService
    {
        public Task RegisterRequestAsync(
            string teamsConversationId,
            string requestId,
            DateTimeOffset registeredAt,
            string? userId,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<string?> GetLastRequestIdAsync(
            string teamsConversationId,
            CancellationToken cancellationToken) =>
            Task.FromResult<string?>(null);

        public Task<IReadOnlyList<ConversationReportRequestHistoryItem>>
            GetHistoryAsync(
                string teamsConversationId,
                CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<
                ConversationReportRequestHistoryItem>>([]);
    }
}
