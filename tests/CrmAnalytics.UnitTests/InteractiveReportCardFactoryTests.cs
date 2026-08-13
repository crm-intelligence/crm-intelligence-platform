using CrmAnalytics.Contracts.Notifications;
using CrmAnalytics.Teams.Notifications;
using Microsoft.Teams.Cards;

namespace CrmAnalytics.UnitTests;

public sealed class InteractiveReportCardFactoryTests
{
    private readonly ReportNotificationCardFactory _factory = new();

    [Fact]
    public void Completed_ContainsRevisionInputAndExecuteAction()
    {
        var card = _factory.Create(Create(
            ReportNotificationStatus.Completed));

        var input = Assert.Single(
            card.Card.Body!.OfType<TextInput>());
        var action = Assert.Single(
            card.Card.Actions!.OfType<ExecuteAction>());
        var data = Assert.IsType<SubmitActionData>(action.Data!.Value);

        Assert.Equal("revisionPrompt", input.Id);
        Assert.True(input.IsMultiline);
        Assert.Equal(2000, input.MaxLength);
        Assert.Equal("revise-report", action.Verb);
        Assert.True(action.AssociatedInputs!.IsAuto);
        Assert.Contains("requestId", data.NonSchemaProperties.Keys);
        Assert.Contains("actionToken", data.NonSchemaProperties.Keys);
        Assert.DoesNotContain(
            "conversationId",
            data.NonSchemaProperties.Keys);
    }

    [Fact]
    public void Waiting_ContainsClarificationInputAndExecuteAction()
    {
        var card = _factory.Create(Create(
            ReportNotificationStatus.WaitingForClarification));

        var input = Assert.Single(
            card.Card.Body!.OfType<TextInput>());
        var action = Assert.Single(
            card.Card.Actions!.OfType<ExecuteAction>());

        Assert.Equal("clarificationResponse", input.Id);
        Assert.True(input.IsMultiline);
        Assert.Equal(2000, input.MaxLength);
        Assert.Equal("submit-clarification", action.Verb);
        Assert.True(action.AssociatedInputs!.IsAuto);
    }

    [Fact]
    public void Failed_DoesNotContainInteractiveControls()
    {
        var card = _factory.Create(Create(
            ReportNotificationStatus.Failed));

        Assert.Empty(card.Card.Body!.OfType<TextInput>());
        Assert.Empty(card.Card.Actions!.OfType<ExecuteAction>());
    }

    private static ReportStatusNotificationRequest Create(
        ReportNotificationStatus status) =>
        new()
        {
            RequestId = "request-1",
            Status = status,
            UpdatedAt = new DateTimeOffset(
                2026,
                7,
                30,
                10,
                0,
                0,
                TimeSpan.Zero),
            CorrelationId = "correlation-1",
            Summary = "Summary",
            ClarificationQuestion = "Hangi dönem?"
        };
}
