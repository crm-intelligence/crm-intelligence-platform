using CrmAnalytics.Teams.Notifications;
using Microsoft.Teams.Cards;

namespace CrmAnalytics.UnitTests;

public sealed class TeamsProactiveNotificationSenderTests
{
    [Fact]
    public async Task SendCardAsync_ForwardsTargetCardAndCancellationToken()
    {
        var sdkSender = new RecordingSdkCardSender();
        var sender = new TeamsProactiveNotificationSender(sdkSender);
        var adaptiveCard = new AdaptiveCard(
            new TextBlock("Rapor kartı"));
        var card = new ReportNotificationCard(adaptiveCard);
        using var cancellationSource =
            new CancellationTokenSource();

        await sender.SendCardAsync(
            "  conversation-42  ",
            card,
            cancellationSource.Token);

        Assert.Equal("conversation-42", sdkSender.ConversationId);
        Assert.Same(adaptiveCard, sdkSender.Card);
        Assert.Equal(
            cancellationSource.Token,
            sdkSender.CancellationToken);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task SendCardAsync_EmptyConversationIdIsRejected(
        string? conversationId)
    {
        var sender = new TeamsProactiveNotificationSender(
            new RecordingSdkCardSender());
        var card = new ReportNotificationCard(new AdaptiveCard());

        await Assert.ThrowsAnyAsync<ArgumentException>(
            () => sender.SendCardAsync(
                conversationId!,
                card,
                CancellationToken.None));
    }

    [Fact]
    public async Task SendCardAsync_NullCardIsRejected()
    {
        var sender = new TeamsProactiveNotificationSender(
            new RecordingSdkCardSender());

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => sender.SendCardAsync(
                "conversation-42",
                null!,
                CancellationToken.None));
    }

    [Fact]
    public async Task SendCardAsync_ChartFailureSendsTextFallback()
    {
        var sdkSender = new FailsFirstSdkCardSender();
        var sender = new TeamsProactiveNotificationSender(sdkSender);
        var chartCard = new AdaptiveCard(new TextBlock("chart"));
        var textCard = new AdaptiveCard(new TextBlock("fallback"));

        await sender.SendCardAsync(
            "conversation-42",
            new ReportNotificationCard(chartCard, textCard),
            CancellationToken.None);

        Assert.Equal(2, sdkSender.AttemptCount);
        Assert.Same(textCard, sdkSender.LastCard);
    }

    private sealed class RecordingSdkCardSender
        : ITeamsSdkCardSender
    {
        public string? ConversationId { get; private set; }

        public AdaptiveCard? Card { get; private set; }

        public CancellationToken CancellationToken { get; private set; }

        public Task SendCardAsync(
            string conversationId,
            AdaptiveCard card,
            CancellationToken cancellationToken)
        {
            ConversationId = conversationId;
            Card = card;
            CancellationToken = cancellationToken;
            return Task.CompletedTask;
        }
    }

    private sealed class FailsFirstSdkCardSender : ITeamsSdkCardSender
    {
        public int AttemptCount { get; private set; }
        public AdaptiveCard? LastCard { get; private set; }

        public Task SendCardAsync(
            string conversationId,
            AdaptiveCard card,
            CancellationToken cancellationToken)
        {
            AttemptCount++;
            LastCard = card;
            return AttemptCount == 1
                ? Task.FromException(new InvalidOperationException(
                    "render rejected"))
                : Task.CompletedTask;
        }
    }
}
