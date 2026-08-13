using CrmAnalytics.Application.SqlProduction;
using CrmAnalytics.Contracts.Integrations;
using CrmAnalytics.Domain.ReportRequests;

namespace CrmAnalytics.UnitTests;

public sealed class SqlProductionClientRequestFactoryTests
{
    private static readonly string UserId =
        Guid.Parse("11111111-1111-1111-1111-111111111111")
            .ToString("D");
    private static readonly string TenantId =
        Guid.Parse("22222222-2222-2222-2222-222222222222")
            .ToString("D");

    [Fact]
    public void FirstRequest_UsesOriginalPromptReferenceDateAndNoPrevious()
    {
        var report = CreateRequest("request-1", null);
        var scope = CreateScope();

        var request = SqlProductionClientRequestFactory.Create(
            report,
            null,
            scope);

        Assert.Equal(report.Prompt, request.Prompt);
        Assert.Equal(report.ReferenceDate, request.Today);
        Assert.Null(request.PreviousCanonicalRequestJson);
        Assert.Same(scope, request.UserDataScope);
        Assert.DoesNotContain("SP", request.Prompt);
        Assert.Equal(SqlDataSource.Unknown, request.Source);
    }

    [Fact]
    public void Revision_SameOwnerUsesPreviousCanonical()
    {
        var previous = CreateRequest("previous-1", null);
        previous.TransitionTo(
            ReportRequestStatus.Validating,
            previous.UpdatedAt.AddMinutes(1));
        previous.RecordCanonicalRequest(
            "{\"request\":\"previous\"}",
            previous.UpdatedAt.AddMinutes(1));
        var revision = CreateRequest("revision-1", previous.RequestId);

        var request = SqlProductionClientRequestFactory.Create(
            revision,
            previous,
            CreateScope());

        Assert.Equal(revision.Prompt, request.Prompt);
        Assert.Equal(
            previous.CanonicalRequestJson,
            request.PreviousCanonicalRequestJson);
        Assert.Equal(revision.ReferenceDate, request.Today);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void Revision_DifferentOwnerDoesNotUsePreviousCanonical(
        bool differentUser,
        bool differentTenant)
    {
        var previous = ReportRequest.Create(
            "previous-1",
            "conversation-1",
            null,
            "Önceki talep",
            "correlation-1",
            differentUser ? Guid.NewGuid().ToString("D") : UserId,
            differentTenant ? Guid.NewGuid().ToString("D") : TenantId);
        previous.TransitionTo(
            ReportRequestStatus.Validating,
            previous.UpdatedAt.AddMinutes(1));
        previous.RecordCanonicalRequest(
            "{\"request\":\"previous\"}",
            previous.UpdatedAt.AddMinutes(1));
        var revision = CreateRequest("revision-1", previous.RequestId);

        var request = SqlProductionClientRequestFactory.Create(
            revision,
            previous,
            CreateScope());

        Assert.Null(request.PreviousCanonicalRequestJson);
    }

    [Fact]
    public void Clarification_UsesDeltaAndCurrentCanonicalWithoutJoiningPrompt()
    {
        var report = CreateRequest("request-1", null);
        report.TransitionTo(
            ReportRequestStatus.Validating,
            report.UpdatedAt.AddMinutes(1));
        report.RecordCanonicalRequest(
            "{\"request\":\"current\"}",
            report.UpdatedAt.AddMinutes(1));
        report.RequestClarification(
            "Hangi dönem?",
            report.UpdatedAt.AddMinutes(1));
        report.SubmitClarificationResponse(
            "2018 yılının ilk çeyreği",
            report.UpdatedAt.AddMinutes(1));
        var referenceDate = report.ReferenceDate;
        var originalPrompt = report.Prompt;

        var request = SqlProductionClientRequestFactory.Create(
            report,
            null,
            CreateScope());

        Assert.Equal(
            "2018 yılının ilk çeyreği",
            request.Prompt);
        Assert.Equal(
            "{\"request\":\"current\"}",
            request.PreviousCanonicalRequestJson);
        Assert.DoesNotContain(originalPrompt, request.Prompt);
        Assert.Equal(originalPrompt, report.Prompt);
        Assert.Equal(referenceDate, report.ReferenceDate);
        Assert.Equal(referenceDate, request.Today);
        Assert.Equal(originalPrompt, request.OriginalPrompt);
        Assert.Equal(report.ClarificationQuestion, request.ClarificationQuestion);
        Assert.Equal(report.ClarificationResponse, request.ClarificationAnswer);
    }

    [Fact]
    public void Clarification_WithoutCanonicalReparsesOriginalAndAnswer()
    {
        var report = CreateRequest("request-1", null);
        report.TransitionTo(
            ReportRequestStatus.Validating,
            report.UpdatedAt.AddMinutes(1));
        report.RequestClarification(
            "Hangi dönem?",
            report.UpdatedAt.AddMinutes(1));
        report.SubmitClarificationResponse(
            "2018 yılı",
            report.UpdatedAt.AddMinutes(1));

        var request = SqlProductionClientRequestFactory.Create(
            report,
            null,
            CreateScope());

        Assert.Equal(
            string.Join(Environment.NewLine,
                report.Prompt, report.ClarificationResponse),
            request.Prompt);
        Assert.Null(request.PreviousCanonicalRequestJson);
        Assert.Equal(report.ReferenceDate, request.Today);
        Assert.Equal(report.Prompt, request.OriginalPrompt);
        Assert.Equal(report.ClarificationQuestion, request.ClarificationQuestion);
        Assert.Equal(report.ClarificationResponse, request.ClarificationAnswer);
    }

    private static ReportRequest CreateRequest(
        string requestId,
        string? previousRequestId) =>
        ReportRequest.Create(
            requestId,
            "conversation-1",
            previousRequestId,
            "2018 satış raporu",
            "correlation-1",
            UserId,
            TenantId);

    private static UserDataScope CreateScope() =>
        new()
        {
            UserId = UserId,
            TenantId = TenantId,
            AllowAllRegions = false,
            AllowAllStores = true,
            AllowedRegions = ["SP"]
        };
}
