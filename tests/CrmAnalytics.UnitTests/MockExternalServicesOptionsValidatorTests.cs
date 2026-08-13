using CrmAnalytics.Infrastructure.Integrations;

namespace CrmAnalytics.UnitTests;

public sealed class MockExternalServicesOptionsValidatorTests
{
    private readonly MockExternalServicesOptionsValidator _validator = new();

    [Theory]
    [InlineData(-1)]
    [InlineData(5001)]
    public void Validate_InvalidDelay_ReturnsFailure(int delayMilliseconds)
    {
        var options = CreateValidOptions();
        options.DelayMilliseconds = delayMilliseconds;

        var result = _validator.Validate(name: null, options);

        Assert.True(result.Failed);
    }

    [Fact]
    public void Validate_HttpPowerBiBaseUrl_ReturnsFailure()
    {
        var options = CreateValidOptions();
        options.PowerBiBaseUrl = "http://app.powerbi.com/reports";

        var result = _validator.Validate(name: null, options);

        Assert.True(result.Failed);
    }

    [Fact]
    public void Validate_ValidHttpsOptions_ReturnsSuccess()
    {
        var result = _validator.Validate(
            name: null,
            CreateValidOptions());

        Assert.True(result.Succeeded);
    }

    private static MockExternalServicesOptions CreateValidOptions()
    {
        return new MockExternalServicesOptions
        {
            DelayMilliseconds = 50,
            PowerBiBaseUrl =
                "https://app.powerbi.com/demo/reports"
        };
    }
}
