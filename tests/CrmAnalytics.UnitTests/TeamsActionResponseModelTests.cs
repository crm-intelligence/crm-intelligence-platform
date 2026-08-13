using System.Text.Json;
using Microsoft.Teams.Api;
using Microsoft.Teams.Api.AdaptiveCards;
using Microsoft.Teams.Cards;
using AdaptiveCardVersion = Microsoft.Teams.Cards.Version;

namespace CrmAnalytics.UnitTests;

public sealed class TeamsActionResponseModelTests
{
    [Fact]
    public void ReplacementCard_UsesSdkActionResponseShape()
    {
        var card = new AdaptiveCard(
            new TextBlock("Talebiniz alındı"))
        {
            Schema =
                "http://adaptivecards.io/schemas/adaptive-card.json",
            Version = new AdaptiveCardVersion("1.5")
        };
        var response = new ActionResponse(ContentType.AdaptiveCard)
        {
            StatusCode = 200,
            Value = card
        };

        using var document = JsonDocument.Parse(
            JsonSerializer.Serialize(response));

        Assert.Equal(
            200,
            document.RootElement.GetProperty("statusCode").GetInt32());
        Assert.Equal(
            "application/vnd.microsoft.card.adaptive",
            document.RootElement.GetProperty("type").GetString());
        Assert.Equal(
            "AdaptiveCard",
            document.RootElement
                .GetProperty("value")
                .GetProperty("type")
                .GetString());
    }
}
