using CrmAnalytics.Domain.ReportRequests;

namespace CrmAnalytics.UnitTests;

public sealed class ReportClarificationDomainTests
{
    [Fact]
    public void SubmitClarificationResponse_ValidResponse_IsTrimmed()
    {
        var request = CreateWaitingRequest();
        var submittedAt = request.UpdatedAt.AddMinutes(1);

        request.SubmitClarificationResponse(
            "  2026 yılının ilk çeyreği  ",
            submittedAt);

        Assert.Equal(
            "2026 yılının ilk çeyreği",
            request.ClarificationResponse);
        Assert.Equal(
            ReportRequestStatus.WaitingForClarification,
            request.Status);
        Assert.Equal(submittedAt, request.UpdatedAt);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("ab")]
    public void SubmitClarificationResponse_InvalidShortResponse_PreservesState(
        string response)
    {
        var request = CreateWaitingRequest();
        var updatedAt = request.UpdatedAt;

        Assert.ThrowsAny<ArgumentException>(
            () => request.SubmitClarificationResponse(
                response,
                updatedAt.AddMinutes(1)));

        Assert.Null(request.ClarificationResponse);
        Assert.Equal(updatedAt, request.UpdatedAt);
    }

    [Fact]
    public void SubmitClarificationResponse_OverMaximum_PreservesState()
    {
        var request = CreateWaitingRequest();
        var updatedAt = request.UpdatedAt;

        Assert.Throws<ArgumentException>(
            () => request.SubmitClarificationResponse(
                new string('a', 2001),
                updatedAt.AddMinutes(1)));

        Assert.Null(request.ClarificationResponse);
        Assert.Equal(updatedAt, request.UpdatedAt);
    }

    [Fact]
    public void SubmitClarificationResponse_NonUtcOrEarlierTime_IsRejected()
    {
        var request = CreateWaitingRequest();

        Assert.Throws<ArgumentException>(
            () => request.SubmitClarificationResponse(
                "Geçerli cevap",
                request.UpdatedAt.ToOffset(TimeSpan.FromHours(3))));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => request.SubmitClarificationResponse(
                "Geçerli cevap",
                request.UpdatedAt.AddTicks(-1)));
        Assert.Null(request.ClarificationResponse);
    }

    [Fact]
    public void SubmitClarificationResponse_InvalidStatus_IsRejected()
    {
        var request = ReportRequest.Create(
            "request-1",
            "conversation-1",
            null,
            "Original prompt",
            "correlation-1");

        Assert.Throws<InvalidOperationException>(
            () => request.SubmitClarificationResponse(
                "Geçerli cevap",
                request.UpdatedAt.AddMinutes(1)));
        Assert.Null(request.ClarificationResponse);
    }

    [Fact]
    public void SubmitClarificationResponse_SecondResponse_IsRejected()
    {
        var request = CreateWaitingRequest();
        request.SubmitClarificationResponse(
            "İlk cevap",
            request.UpdatedAt.AddMinutes(1));
        var updatedAt = request.UpdatedAt;

        Assert.Throws<InvalidOperationException>(
            () => request.SubmitClarificationResponse(
                "İkinci cevap",
                updatedAt.AddMinutes(1)));

        Assert.Equal("İlk cevap", request.ClarificationResponse);
        Assert.Equal(updatedAt, request.UpdatedAt);
    }

    [Fact]
    public void NewClarificationQuestion_ClearsPreviousResponse()
    {
        var request = CreateWaitingRequest();
        request.SubmitClarificationResponse(
            "İlk cevap",
            request.UpdatedAt.AddMinutes(1));
        request.TransitionTo(
            ReportRequestStatus.Validating,
            request.UpdatedAt.AddMinutes(1));

        request.RequestClarification(
            "Yeni soru?",
            request.UpdatedAt.AddMinutes(1));

        Assert.Null(request.ClarificationResponse);
    }

    [Fact]
    public void CompleteAndFail_ClearResponse()
    {
        var completed = CreateWaitingRequest();
        completed.SubmitClarificationResponse(
            "Geçerli cevap",
            completed.UpdatedAt.AddMinutes(1));
        completed.TransitionTo(
            ReportRequestStatus.Validating,
            completed.UpdatedAt.AddMinutes(1));
        completed.TransitionTo(
            ReportRequestStatus.Processing,
            completed.UpdatedAt.AddMinutes(1));
        completed.Complete(
            "report-1",
            "Summary",
            "https://app.powerbi.com/report/1",
            completed.UpdatedAt.AddMinutes(1));

        var failed = CreateWaitingRequest();
        failed.SubmitClarificationResponse(
            "Geçerli cevap",
            failed.UpdatedAt.AddMinutes(1));
        failed.Fail(
            "FAILED",
            "Safe failure",
            failed.UpdatedAt.AddMinutes(1));

        Assert.Null(completed.ClarificationResponse);
        Assert.Null(failed.ClarificationResponse);
    }

    private static ReportRequest CreateWaitingRequest()
    {
        var request = ReportRequest.Create(
            Guid.NewGuid().ToString("N"),
            "conversation-1",
            null,
            "Original prompt",
            "correlation-1");
        request.TransitionTo(
            ReportRequestStatus.Validating,
            request.UpdatedAt.AddMinutes(1));
        request.RequestClarification(
            "Hangi dönem?",
            request.UpdatedAt.AddMinutes(1));
        return request;
    }
}
