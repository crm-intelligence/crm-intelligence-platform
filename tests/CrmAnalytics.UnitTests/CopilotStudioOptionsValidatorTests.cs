using CrmAnalytics.Teams.Configuration;
using Microsoft.Extensions.Options;

namespace CrmAnalytics.UnitTests;

public sealed class CopilotStudioOptionsValidatorTests
{
    private readonly CopilotStudioOptionsValidator _validator = new();

    [Fact]
    public void Disabled_AllowsEmptyDirectLineConfiguration()
    {
        var result = _validator.Validate(
            Options.DefaultName,
            new CopilotStudioOptions
            {
                Enabled = false,
                RequestTimeoutSeconds = 30
            });

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Enabled_RequiresHttpsDirectLineBaseUriSecretAndAgentName()
    {
        var result = _validator.Validate(
            Options.DefaultName,
            new CopilotStudioOptions
            {
                Enabled = true,
                AgentName = "",
                RequestTimeoutSeconds = 30
            });

        Assert.True(result.Failed);
        Assert.Contains(
            result.Failures,
            failure => failure.Contains(
                "DirectLineBaseUri",
                StringComparison.Ordinal));
        Assert.Contains(
            result.Failures,
            failure => failure.Contains(
                "DirectLineSecret",
                StringComparison.Ordinal));
        Assert.Contains(
            result.Failures,
            failure => failure.Contains(
                "AgentName",
                StringComparison.Ordinal));
    }

    [Fact]
    public void Enabled_AcceptsCompleteConfiguration()
    {
        var result = _validator.Validate(
            Options.DefaultName,
            new CopilotStudioOptions
            {
                Enabled = true,
                DirectLineBaseUri =
                    "https://europe.directline.botframework.com",
                DirectLineSecret = "test-direct-line-secret",
                AgentName = "CRM Planner",
                RequestTimeoutSeconds = 30
            });

        Assert.True(result.Succeeded);
    }

    [Theory]
    [InlineData("http://europe.directline.botframework.com")]
    [InlineData("https://europe.directline.botframework.com/v3/directline")]
    [InlineData("https://user@europe.directline.botframework.com")]
    [InlineData("not-a-uri")]
    public void Enabled_RejectsUnsafeDirectLineBaseUri(string baseUri)
    {
        var result = _validator.Validate(
            Options.DefaultName,
            new CopilotStudioOptions
            {
                Enabled = true,
                DirectLineBaseUri = baseUri,
                DirectLineSecret = "test-direct-line-secret",
                AgentName = "CRM Planner",
                RequestTimeoutSeconds = 30
            });

        Assert.True(result.Failed);
        Assert.Contains(
            result.Failures,
            failure => failure.Contains("HTTPS origin", StringComparison.Ordinal));
    }
}
