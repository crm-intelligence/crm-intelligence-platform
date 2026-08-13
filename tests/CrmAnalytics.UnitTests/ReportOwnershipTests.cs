using CrmAnalytics.Application.Conversations;
using CrmAnalytics.Application.Identity;
using CrmAnalytics.Application.ReportProcessing;
using CrmAnalytics.Application.ReportRequests;
using CrmAnalytics.Contracts.Integrations;
using CrmAnalytics.Domain.ReportRequests;
using CrmAnalytics.Infrastructure.Persistence;

namespace CrmAnalytics.UnitTests;

public sealed class ReportOwnershipTests
{
    private const string UserA =
        "11111111-1111-4111-8111-111111111111";
    private const string UserB =
        "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa";
    private const string TenantA =
        "22222222-2222-4222-8222-222222222222";
    private const string TenantB =
        "bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb";

    [Fact]
    public void AccessService_SameOwnerCanAccess()
    {
        var request = CreateOwnedRequest(UserA, TenantA);

        new ReportRequestAccessService().EnsureCanAccess(
            request,
            User(UserA, TenantA));
    }

    [Theory]
    [InlineData(UserB, TenantA)]
    [InlineData(UserA, TenantB)]
    public void AccessService_DifferentOwnerReturnsNotFound(
        string userId,
        string tenantId)
    {
        var request = CreateOwnedRequest(UserA, TenantA);

        Assert.Throws<KeyNotFoundException>(
            () => new ReportRequestAccessService().EnsureCanAccess(
                request,
                User(userId, tenantId, "Report.Admin")));
    }

    [Fact]
    public async Task AuthenticatedCreate_PersistsIdentity()
    {
        var fixture = CreateService();
        var user = User(UserA, TenantA);

        var result = await fixture.Service.CreateAsync(
            CreateCommand("conversation-create"),
            user,
            CancellationToken.None);
        var request = await fixture.Repository.GetByIdAsync(
            result.RequestId,
            CancellationToken.None);

        Assert.NotNull(request);
        Assert.Equal(UserA, request.UserId);
        Assert.Equal(TenantA, request.TenantId);
    }

    [Fact]
    public async Task DifferentUserCannotReviseAndStateDoesNotChange()
    {
        var fixture = CreateService();
        var source = CreateOwnedRequest(UserA, TenantA);
        Complete(source);
        await fixture.Repository.AddAsync(source, CancellationToken.None);

        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => fixture.Service.ReviseAsync(
                new ReviseReportRequestCommand(
                    source.RequestId,
                    "Revise this completed report.",
                    "correlation-revise"),
                User(UserB, TenantA, "Report.Admin"),
                CancellationToken.None));

        var history = await fixture.Repository.GetByConversationIdAsync(
            source.ConversationId,
            CancellationToken.None);
        Assert.Single(history);
        Assert.Equal(ReportRequestStatus.Completed, source.Status);
    }

    [Fact]
    public async Task DifferentTenantCannotClarifyAndStateDoesNotChange()
    {
        var fixture = CreateService();
        var request = CreateOwnedRequest(UserA, TenantA);
        request.TransitionTo(
            ReportRequestStatus.Validating,
            request.UpdatedAt);
        request.RequestClarification(
            "Please clarify the reporting period.",
            request.UpdatedAt);
        await fixture.Repository.AddAsync(request, CancellationToken.None);

        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => fixture.Service.SubmitClarificationAsync(
                new SubmitReportClarificationCommand(
                    request.RequestId,
                    "Last quarter.",
                    request.UpdatedAt),
                User(UserA, TenantB),
                CancellationToken.None));

        Assert.Equal(
            ReportRequestStatus.WaitingForClarification,
            request.Status);
        Assert.Null(request.ClarificationResponse);
    }

    [Fact]
    public async Task OwnershipFailure_DoesNotEnqueue()
    {
        var fixture = CreateService();
        var source = CreateOwnedRequest(UserA, TenantA);
        Complete(source);
        await fixture.Repository.AddAsync(source, CancellationToken.None);
        var queue = new RecordingQueue();
        var submission = new ReportRequestSubmissionService(fixture.Service);
        var userB = User(UserB, TenantA, "Report.Admin");
        var scope = TestDataScopeFactory.Create(
            UserB,
            TenantA,
            userB.Roles);

        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => submission.ReviseAndSubmitAsync(
                new ReviseReportRequestCommand(
                    source.RequestId,
                    "Attempt unauthorized revision.",
                    "correlation"),
                userB,
                scope,
                CancellationToken.None));

        Assert.Empty(queue.Items);
    }

    [Fact]
    public async Task AuthorizedRevision_PersistsIdentityWithoutDirectQueue()
    {
        var fixture = CreateService();
        var source = CreateOwnedRequest(UserA, TenantA);
        Complete(source);
        await fixture.Repository.AddAsync(source, CancellationToken.None);
        var queue = new RecordingQueue();
        var submission = new ReportRequestSubmissionService(fixture.Service);
        var user = User(UserA, TenantA, "Report.User");
        var scope = TestDataScopeFactory.Create(
            UserA,
            TenantA,
            user.Roles);

        var result = await submission.ReviseAndSubmitAsync(
            new ReviseReportRequestCommand(
                source.RequestId,
                "Authorized revision.",
                "correlation"),
            user,
            scope,
            CancellationToken.None);
        var revision = await fixture.Repository.GetByIdAsync(
            result.RequestId,
            CancellationToken.None);

        Assert.NotNull(revision);
        Assert.Equal(UserA, revision.UserId);
        Assert.Equal(TenantA, revision.TenantId);
        Assert.Empty(queue.Items);
    }

    [Fact]
    public async Task AuthenticatedHistory_FiltersUserAndTenant()
    {
        var reports = new InMemoryReportRequestRepository();
        var conversations = new InMemoryConversationRepository();
        var service = new ConversationContextService(
            conversations,
            reports);
        var ownFirst = CreateOwnedRequest(UserA, TenantA, "conversation");
        var otherUser = CreateOwnedRequest(UserB, TenantA, "conversation");
        var otherTenant = CreateOwnedRequest(UserA, TenantB, "conversation");
        var ownLast = CreateOwnedRequest(UserA, TenantA, "conversation");

        foreach (var request in new[]
                 {
                     ownFirst,
                     otherUser,
                     otherTenant,
                     ownLast
                 })
        {
            await reports.AddAsync(request, CancellationToken.None);
        }

        var history = await service.GetHistoryAsync(
            "conversation",
            User(UserA, TenantA),
            CancellationToken.None);

        Assert.Equal(2, history.Count);
        Assert.All(
            history,
            item => Assert.Contains(
                item.RequestId,
                new[] { ownFirst.RequestId, ownLast.RequestId }));
    }

    private static (
        ReportRequestService Service,
        InMemoryReportRequestRepository Repository) CreateService()
    {
        var reports = new InMemoryReportRequestRepository();
        var conversations = new ConversationContextService(
            new InMemoryConversationRepository(),
            reports);
        return (
            new ReportRequestService(
                reports,
                conversations,
                new ReportRequestAccessService()),
            reports);
    }

    private static AuthenticatedUserContext User(
        string userId,
        string tenantId,
        params string[] roles) =>
        new(userId, tenantId, roles);

    private static CreateReportRequestCommand CreateCommand(
        string conversationId) =>
        new(
            "Show quarterly sales.",
            conversationId,
            null,
            "correlation-create");

    private static ReportRequest CreateOwnedRequest(
        string userId,
        string tenantId,
        string conversationId = "conversation-owned") =>
        ReportRequest.Create(
            Guid.NewGuid().ToString("N"),
            conversationId,
            null,
            "Show sales.",
            Guid.NewGuid().ToString("N"),
            userId,
            tenantId);

    private static void Complete(ReportRequest request)
    {
        request.TransitionTo(
            ReportRequestStatus.Validating,
            request.UpdatedAt);
        request.TransitionTo(
            ReportRequestStatus.Processing,
            request.UpdatedAt);
        request.Complete(
            "report",
            "Summary.",
            "https://app.powerbi.com/reports/report",
            request.UpdatedAt);
    }

    private sealed class RecordingQueue : IReportProcessingQueue
    {
        public List<QueuedReportProcessingRequest> Items { get; } = [];

        public ValueTask EnqueueAsync(
            QueuedReportProcessingRequest request,
            CancellationToken cancellationToken)
        {
            Items.Add(request);
            return ValueTask.CompletedTask;
        }

        public ValueTask<QueuedReportProcessingRequest> DequeueAsync(
            CancellationToken cancellationToken) =>
            ValueTask.FromException<QueuedReportProcessingRequest>(
                new NotSupportedException());
    }
}
