using CrmAnalytics.Domain.ReportRequests;

namespace CrmAnalytics.UnitTests;

public sealed class ReportRequestTests
{
    public static TheoryData<
        ReportRequestStatus,
        ReportRequestStatus> AllowedTransitions =>
        new()
        {
            { ReportRequestStatus.Received, ReportRequestStatus.Validating },
            { ReportRequestStatus.Received, ReportRequestStatus.Failed },
            { ReportRequestStatus.Validating, ReportRequestStatus.Queued },
            { ReportRequestStatus.Validating, ReportRequestStatus.Processing },
            {
                ReportRequestStatus.Validating,
                ReportRequestStatus.WaitingForClarification
            },
            { ReportRequestStatus.Validating, ReportRequestStatus.Failed },
            { ReportRequestStatus.Validating, ReportRequestStatus.Rejected },
            { ReportRequestStatus.Queued, ReportRequestStatus.Running },
            { ReportRequestStatus.Queued, ReportRequestStatus.Failed },
            {
                ReportRequestStatus.Processing,
                ReportRequestStatus.WaitingForClarification
            },
            { ReportRequestStatus.Processing, ReportRequestStatus.Completed },
            { ReportRequestStatus.Processing, ReportRequestStatus.Failed },
            { ReportRequestStatus.Processing, ReportRequestStatus.Rejected },
            {
                ReportRequestStatus.Running,
                ReportRequestStatus.WaitingForClarification
            },
            { ReportRequestStatus.Running, ReportRequestStatus.Completed },
            { ReportRequestStatus.Running, ReportRequestStatus.Failed },
            { ReportRequestStatus.Running, ReportRequestStatus.Rejected },
            {
                ReportRequestStatus.WaitingForClarification,
                ReportRequestStatus.Validating
            },
            {
                ReportRequestStatus.WaitingForClarification,
                ReportRequestStatus.Processing
            },
            {
                ReportRequestStatus.WaitingForClarification,
                ReportRequestStatus.Failed
            }
        };

    public static TheoryData<
        ReportRequestStatus,
        ReportRequestStatus> RejectedTransitions
    {
        get
        {
            var rejectedTransitions = new TheoryData<
                ReportRequestStatus,
                ReportRequestStatus>();

            foreach (var currentStatus in Enum.GetValues<ReportRequestStatus>())
            {
                foreach (var newStatus in Enum.GetValues<ReportRequestStatus>())
                {
                    if (!IsAllowed(currentStatus, newStatus))
                    {
                        rejectedTransitions.Add(currentStatus, newStatus);
                    }
                }
            }

            return rejectedTransitions;
        }
    }

    [Fact]
    public void Create_ValidRequest_StartsInReceivedWithMatchingTimestamps()
    {
        var reportRequest = CreateReportRequest();

        Assert.Equal(ReportRequestStatus.Received, reportRequest.Status);
        Assert.Equal(reportRequest.CreatedAt, reportRequest.UpdatedAt);
    }

    [Theory]
    [MemberData(nameof(AllowedTransitions))]
    public void TransitionTo_AllowedTransition_UpdatesStatusAndTimestamp(
        ReportRequestStatus currentStatus,
        ReportRequestStatus newStatus)
    {
        var reportRequest = CreateInStatus(currentStatus);
        var transitionedAt = reportRequest.UpdatedAt.AddMinutes(1);

        reportRequest.TransitionTo(newStatus, transitionedAt);

        Assert.Equal(newStatus, reportRequest.Status);
        Assert.Equal(transitionedAt, reportRequest.UpdatedAt);
    }

    [Theory]
    [MemberData(nameof(RejectedTransitions))]
    public void TransitionTo_RejectedTransition_PreservesStatusAndTimestamp(
        ReportRequestStatus currentStatus,
        ReportRequestStatus newStatus)
    {
        var reportRequest = CreateInStatus(currentStatus);
        var originalStatus = reportRequest.Status;
        var originalUpdatedAt = reportRequest.UpdatedAt;

        Assert.Throws<InvalidOperationException>(
            () => reportRequest.TransitionTo(
                newStatus,
                originalUpdatedAt.AddMinutes(1)));

        Assert.Equal(originalStatus, reportRequest.Status);
        Assert.Equal(originalUpdatedAt, reportRequest.UpdatedAt);
    }

    [Fact]
    public void TransitionTo_EarlierThanUpdatedAt_ThrowsAndPreservesState()
    {
        var reportRequest = CreateReportRequest();
        var originalStatus = reportRequest.Status;
        var originalUpdatedAt = reportRequest.UpdatedAt;

        Assert.Throws<ArgumentOutOfRangeException>(
            () => reportRequest.TransitionTo(
                ReportRequestStatus.Validating,
                originalUpdatedAt.AddTicks(-1)));

        Assert.Equal(originalStatus, reportRequest.Status);
        Assert.Equal(originalUpdatedAt, reportRequest.UpdatedAt);
    }

    [Fact]
    public void TransitionTo_NonUtcTimestamp_ThrowsAndPreservesState()
    {
        var reportRequest = CreateReportRequest();
        var originalStatus = reportRequest.Status;
        var originalUpdatedAt = reportRequest.UpdatedAt;
        var nonUtcTimestamp = originalUpdatedAt.ToOffset(
            TimeSpan.FromHours(3));

        Assert.Throws<ArgumentException>(
            () => reportRequest.TransitionTo(
                ReportRequestStatus.Validating,
                nonUtcTimestamp));

        Assert.Equal(originalStatus, reportRequest.Status);
        Assert.Equal(originalUpdatedAt, reportRequest.UpdatedAt);
    }

    [Fact]
    public void TransitionTo_UndefinedStatus_ThrowsAndPreservesState()
    {
        var reportRequest = CreateReportRequest();
        var originalStatus = reportRequest.Status;
        var originalUpdatedAt = reportRequest.UpdatedAt;
        var undefinedStatus = (ReportRequestStatus)int.MaxValue;

        Assert.Throws<ArgumentOutOfRangeException>(
            () => reportRequest.TransitionTo(
                undefinedStatus,
                originalUpdatedAt.AddMinutes(1)));

        Assert.Equal(originalStatus, reportRequest.Status);
        Assert.Equal(originalUpdatedAt, reportRequest.UpdatedAt);
    }

    [Fact]
    public void Complete_ProcessingRequestWithValidDetails_Completes()
    {
        var reportRequest = CreateInStatus(ReportRequestStatus.Processing);
        var completedAt = reportRequest.UpdatedAt.AddMinutes(1);

        reportRequest.Complete(
            "report-1",
            "CRM report summary.",
            "https://app.powerbi.com/reports/report-1",
            completedAt);

        Assert.Equal(ReportRequestStatus.Completed, reportRequest.Status);
        Assert.Equal(completedAt, reportRequest.UpdatedAt);
    }

    [Fact]
    public void Complete_RunningRequestWithValidDetails_Completes()
    {
        var reportRequest = CreateInStatus(ReportRequestStatus.Running);

        reportRequest.Complete(
            "report-1",
            "CRM report summary.",
            "https://app.powerbi.com/reports/report-1",
            reportRequest.UpdatedAt.AddMinutes(1));

        Assert.Equal(ReportRequestStatus.Completed, reportRequest.Status);
    }

    [Fact]
    public void Complete_ValidDetails_AssignsResultFields()
    {
        var reportRequest = CreateInStatus(ReportRequestStatus.Processing);

        reportRequest.Complete(
            "report-1",
            "CRM report summary.",
            "https://app.powerbi.com/reports/report-1",
            reportRequest.UpdatedAt.AddMinutes(1));

        Assert.Equal("report-1", reportRequest.ReportId);
        Assert.Equal("CRM report summary.", reportRequest.Summary);
        Assert.Equal(
            "https://app.powerbi.com/reports/report-1",
            reportRequest.PowerBiUrl);
    }

    [Fact]
    public void Complete_ValidDetails_ClearsErrorFields()
    {
        var reportRequest = CreateInStatus(ReportRequestStatus.Processing);

        reportRequest.Complete(
            "report-1",
            "CRM report summary.",
            "https://app.powerbi.com/reports/report-1",
            reportRequest.UpdatedAt.AddMinutes(1));

        Assert.Null(reportRequest.ErrorCode);
        Assert.Null(reportRequest.ErrorMessage);
    }

    [Fact]
    public void Complete_NonHttpsUrl_Throws()
    {
        var reportRequest = CreateInStatus(ReportRequestStatus.Processing);

        Assert.Throws<ArgumentException>(
            () => reportRequest.Complete(
                "report-1",
                "CRM report summary.",
                "http://app.powerbi.com/reports/report-1",
                reportRequest.UpdatedAt.AddMinutes(1)));
    }

    [Fact]
    public void Complete_EmptyReportId_Throws()
    {
        var reportRequest = CreateInStatus(ReportRequestStatus.Processing);

        Assert.Throws<ArgumentException>(
            () => reportRequest.Complete(
                " ",
                "CRM report summary.",
                "https://app.powerbi.com/reports/report-1",
                reportRequest.UpdatedAt.AddMinutes(1)));
    }

    [Fact]
    public void Complete_EmptySummary_Throws()
    {
        var reportRequest = CreateInStatus(ReportRequestStatus.Processing);

        Assert.Throws<ArgumentException>(
            () => reportRequest.Complete(
                "report-1",
                " ",
                "https://app.powerbi.com/reports/report-1",
                reportRequest.UpdatedAt.AddMinutes(1)));
    }

    [Fact]
    public void Complete_ReceivedRequest_Throws()
    {
        var reportRequest = CreateReportRequest();

        Assert.Throws<InvalidOperationException>(
            () => reportRequest.Complete(
                "report-1",
                "CRM report summary.",
                "https://app.powerbi.com/reports/report-1",
                reportRequest.UpdatedAt.AddMinutes(1)));
    }

    [Fact]
    public void Complete_FailedOperation_PreservesAllState()
    {
        var reportRequest = CreateReportRequest();
        reportRequest.Fail(
            "QUERY_FAILED",
            "The report could not be generated.",
            reportRequest.UpdatedAt.AddMinutes(1));
        var originalUpdatedAt = reportRequest.UpdatedAt;

        Assert.Throws<InvalidOperationException>(
            () => reportRequest.Complete(
                "report-1",
                "CRM report summary.",
                "https://app.powerbi.com/reports/report-1",
                originalUpdatedAt.AddMinutes(1)));

        Assert.Equal(ReportRequestStatus.Failed, reportRequest.Status);
        Assert.Equal(originalUpdatedAt, reportRequest.UpdatedAt);
        Assert.Null(reportRequest.ReportId);
        Assert.Null(reportRequest.Summary);
        Assert.Null(reportRequest.PowerBiUrl);
        Assert.Equal("QUERY_FAILED", reportRequest.ErrorCode);
        Assert.Equal(
            "The report could not be generated.",
            reportRequest.ErrorMessage);
    }

    [Fact]
    public void Fail_ReceivedRequest_Fails()
    {
        var reportRequest = CreateReportRequest();
        var failedAt = reportRequest.UpdatedAt.AddMinutes(1);

        reportRequest.Fail(
            "QUERY_FAILED",
            "The report could not be generated.",
            failedAt);

        Assert.Equal(ReportRequestStatus.Failed, reportRequest.Status);
        Assert.Equal(failedAt, reportRequest.UpdatedAt);
    }

    [Fact]
    public void Fail_ProcessingRequest_Fails()
    {
        var reportRequest = CreateInStatus(ReportRequestStatus.Processing);

        reportRequest.Fail(
            "QUERY_FAILED",
            "The report could not be generated.",
            reportRequest.UpdatedAt.AddMinutes(1));

        Assert.Equal(ReportRequestStatus.Failed, reportRequest.Status);
    }

    [Fact]
    public void Fail_ValidDetails_AssignsErrorFields()
    {
        var reportRequest = CreateReportRequest();

        reportRequest.Fail(
            "QUERY_SERVICE_UNAVAILABLE",
            "The report service is temporarily unavailable.",
            reportRequest.UpdatedAt.AddMinutes(1));

        Assert.Equal(
            "QUERY_SERVICE_UNAVAILABLE",
            reportRequest.ErrorCode);
        Assert.Equal(
            "The report service is temporarily unavailable.",
            reportRequest.ErrorMessage);
    }

    [Fact]
    public void Fail_ValidDetails_ClearsResultFields()
    {
        var reportRequest = CreateInStatus(ReportRequestStatus.Processing);

        reportRequest.Fail(
            "QUERY_FAILED",
            "The report could not be generated.",
            reportRequest.UpdatedAt.AddMinutes(1));

        Assert.Null(reportRequest.ReportId);
        Assert.Null(reportRequest.Summary);
        Assert.Null(reportRequest.PowerBiUrl);
    }

    [Theory]
    [InlineData("query_failed")]
    [InlineData("QUERY-FAILED")]
    [InlineData("QUERY FAILED")]
    public void Fail_InvalidErrorCode_Throws(string errorCode)
    {
        var reportRequest = CreateReportRequest();

        Assert.Throws<ArgumentException>(
            () => reportRequest.Fail(
                errorCode,
                "The report could not be generated.",
                reportRequest.UpdatedAt.AddMinutes(1)));
    }

    [Fact]
    public void Fail_CompletedRequest_Throws()
    {
        var reportRequest = CreateInStatus(ReportRequestStatus.Processing);
        reportRequest.Complete(
            "report-1",
            "CRM report summary.",
            "https://app.powerbi.com/reports/report-1",
            reportRequest.UpdatedAt.AddMinutes(1));

        Assert.Throws<InvalidOperationException>(
            () => reportRequest.Fail(
                "QUERY_FAILED",
                "The report could not be generated.",
                reportRequest.UpdatedAt.AddMinutes(1)));
    }

    [Fact]
    public void Fail_FailedOperation_PreservesAllState()
    {
        var reportRequest = CreateInStatus(ReportRequestStatus.Processing);
        reportRequest.Complete(
            "report-1",
            "CRM report summary.",
            "https://app.powerbi.com/reports/report-1",
            reportRequest.UpdatedAt.AddMinutes(1));
        var originalUpdatedAt = reportRequest.UpdatedAt;

        Assert.Throws<InvalidOperationException>(
            () => reportRequest.Fail(
                "QUERY_FAILED",
                "The report could not be generated.",
                originalUpdatedAt.AddMinutes(1)));

        Assert.Equal(ReportRequestStatus.Completed, reportRequest.Status);
        Assert.Equal(originalUpdatedAt, reportRequest.UpdatedAt);
        Assert.Equal("report-1", reportRequest.ReportId);
        Assert.Equal("CRM report summary.", reportRequest.Summary);
        Assert.Equal(
            "https://app.powerbi.com/reports/report-1",
            reportRequest.PowerBiUrl);
        Assert.Null(reportRequest.ErrorCode);
        Assert.Null(reportRequest.ErrorMessage);
    }

    [Fact]
    public void RequestClarification_ValidQuestion_StoresTrimmedQuestion()
    {
        var reportRequest = CreateInStatus(ReportRequestStatus.Validating);
        var requestedAt = reportRequest.UpdatedAt.AddMinutes(1);

        reportRequest.RequestClarification(
            "  Hangi tarih aralığı kullanılmalı?  ",
            requestedAt);

        Assert.Equal(
            ReportRequestStatus.WaitingForClarification,
            reportRequest.Status);
        Assert.Equal(requestedAt, reportRequest.UpdatedAt);
        Assert.Equal(
            "Hangi tarih aralığı kullanılmalı?",
            reportRequest.ClarificationQuestion);
        Assert.Null(reportRequest.ErrorCode);
        Assert.Null(reportRequest.ErrorMessage);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void RequestClarification_EmptyQuestion_RejectsAndPreservesState(
        string question)
    {
        var reportRequest = CreateInStatus(ReportRequestStatus.Validating);
        var originalUpdatedAt = reportRequest.UpdatedAt;

        Assert.Throws<ArgumentException>(
            () => reportRequest.RequestClarification(
                question,
                originalUpdatedAt.AddMinutes(1)));

        Assert.Equal(ReportRequestStatus.Validating, reportRequest.Status);
        Assert.Equal(originalUpdatedAt, reportRequest.UpdatedAt);
        Assert.Null(reportRequest.ClarificationQuestion);
    }

    [Fact]
    public void RequestClarification_QuestionOverMaximum_Rejects()
    {
        var reportRequest = CreateInStatus(ReportRequestStatus.Validating);

        Assert.Throws<ArgumentException>(
            () => reportRequest.RequestClarification(
                new string('a', 1001),
                reportRequest.UpdatedAt.AddMinutes(1)));

        Assert.Equal(ReportRequestStatus.Validating, reportRequest.Status);
        Assert.Null(reportRequest.ClarificationQuestion);
    }

    [Fact]
    public void RequestClarification_InvalidStatus_PreservesAllState()
    {
        var reportRequest = CreateReportRequest();
        var originalUpdatedAt = reportRequest.UpdatedAt;

        Assert.Throws<InvalidOperationException>(
            () => reportRequest.RequestClarification(
                "Hangi tarih aralığı kullanılmalı?",
                originalUpdatedAt.AddMinutes(1)));

        Assert.Equal(ReportRequestStatus.Received, reportRequest.Status);
        Assert.Equal(originalUpdatedAt, reportRequest.UpdatedAt);
        Assert.Null(reportRequest.ClarificationQuestion);
    }

    [Fact]
    public void TransitionFromClarificationToProcessing_ClearsQuestion()
    {
        var reportRequest = CreateInStatus(ReportRequestStatus.Validating);
        reportRequest.RequestClarification(
            "Hangi tarih aralığı kullanılmalı?",
            reportRequest.UpdatedAt.AddMinutes(1));

        reportRequest.TransitionTo(
            ReportRequestStatus.Processing,
            reportRequest.UpdatedAt.AddMinutes(1));

        Assert.Null(reportRequest.ClarificationQuestion);
    }

    [Fact]
    public void Complete_AfterClarification_ClearsQuestion()
    {
        var reportRequest = CreateInStatus(ReportRequestStatus.Validating);
        reportRequest.RequestClarification(
            "Hangi tarih aralığı kullanılmalı?",
            reportRequest.UpdatedAt.AddMinutes(1));
        reportRequest.TransitionTo(
            ReportRequestStatus.Processing,
            reportRequest.UpdatedAt.AddMinutes(1));

        reportRequest.Complete(
            "report-1",
            "Summary.",
            "https://app.powerbi.com/reports/report-1",
            reportRequest.UpdatedAt.AddMinutes(1));

        Assert.Null(reportRequest.ClarificationQuestion);
    }

    [Fact]
    public void Fail_ClarificationRequest_ClearsQuestion()
    {
        var reportRequest = CreateInStatus(ReportRequestStatus.Validating);
        reportRequest.RequestClarification(
            "Hangi tarih aralığı kullanılmalı?",
            reportRequest.UpdatedAt.AddMinutes(1));

        reportRequest.Fail(
            "CLARIFICATION_FAILED",
            "Clarification failed.",
            reportRequest.UpdatedAt.AddMinutes(1));

        Assert.Null(reportRequest.ClarificationQuestion);
    }

    private static ReportRequest CreateReportRequest()
    {
        return ReportRequest.Create(
            requestId: Guid.NewGuid().ToString("N"),
            conversationId: "conversation-1",
            previousRequestId: null,
            prompt: "Create a CRM report.",
            correlationId: "correlation-1");
    }

    private static ReportRequest CreateInStatus(
        ReportRequestStatus targetStatus)
    {
        var reportRequest = CreateReportRequest();

        foreach (var nextStatus in PathFromReceived(targetStatus))
        {
            reportRequest.TransitionTo(
                nextStatus,
                reportRequest.UpdatedAt.AddMinutes(1));
        }

        return reportRequest;
    }

    private static IReadOnlyList<ReportRequestStatus> PathFromReceived(
        ReportRequestStatus targetStatus)
    {
        return targetStatus switch
        {
            ReportRequestStatus.Received => [],
            ReportRequestStatus.Validating =>
                [ReportRequestStatus.Validating],
            ReportRequestStatus.Queued =>
                [
                    ReportRequestStatus.Validating,
                    ReportRequestStatus.Queued
                ],
            ReportRequestStatus.Processing =>
                [
                    ReportRequestStatus.Validating,
                    ReportRequestStatus.Processing
                ],
            ReportRequestStatus.Running =>
                [
                    ReportRequestStatus.Validating,
                    ReportRequestStatus.Queued,
                    ReportRequestStatus.Running
                ],
            ReportRequestStatus.WaitingForClarification =>
                [
                    ReportRequestStatus.Validating,
                    ReportRequestStatus.WaitingForClarification
                ],
            ReportRequestStatus.Completed =>
                [
                    ReportRequestStatus.Validating,
                    ReportRequestStatus.Processing,
                    ReportRequestStatus.Completed
                ],
            ReportRequestStatus.Failed =>
                [ReportRequestStatus.Failed],
            ReportRequestStatus.Rejected =>
                [
                    ReportRequestStatus.Validating,
                    ReportRequestStatus.Rejected
                ],
            _ => throw new ArgumentOutOfRangeException(
                nameof(targetStatus),
                targetStatus,
                "The report request status is not defined.")
        };
    }

    private static bool IsAllowed(
        ReportRequestStatus currentStatus,
        ReportRequestStatus newStatus)
    {
        return AllowedTransitions.Any(
            transition =>
                transition[0] is ReportRequestStatus allowedCurrentStatus
                && transition[1] is ReportRequestStatus allowedNewStatus
                && allowedCurrentStatus == currentStatus
                && allowedNewStatus == newStatus);
    }
}
