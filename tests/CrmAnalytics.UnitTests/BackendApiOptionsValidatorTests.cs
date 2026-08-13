using CrmAnalytics.Teams.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace CrmAnalytics.UnitTests;

public sealed class BackendApiOptionsValidatorTests
{
    [Fact]
    public void ValidHttpsUrl_IsAccepted()
    {
        var result = CreateValidator(Environments.Production).Validate(
            null,
            CreateOptions("https://api.example.com", 15));

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void InvalidUrl_IsRejected()
    {
        var result = CreateValidator(Environments.Development).Validate(
            null,
            CreateOptions("not-a-url", 15));

        Assert.True(result.Failed);
    }

    [Fact]
    public void HttpUrl_InProduction_IsRejected()
    {
        var result = CreateValidator(Environments.Production).Validate(
            null,
            CreateOptions("http://api.example.com", 15));

        Assert.True(result.Failed);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(61)]
    public void TimeoutOutsideAllowedRange_IsRejected(int timeoutSeconds)
    {
        var result = CreateValidator(Environments.Development).Validate(
            null,
            CreateOptions("https://api.example.com", timeoutSeconds));

        Assert.True(result.Failed);
    }

    private static BackendApiOptionsValidator CreateValidator(
        string environmentName)
    {
        return new BackendApiOptionsValidator(
            new TestHostEnvironment(environmentName));
    }

    private static BackendApiOptions CreateOptions(
        string baseUrl,
        int timeoutSeconds)
    {
        return new BackendApiOptions
        {
            BaseUrl = baseUrl,
            TimeoutSeconds = timeoutSeconds
        };
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public TestHostEnvironment(string environmentName)
        {
            EnvironmentName = environmentName;
        }

        public string EnvironmentName { get; set; }

        public string ApplicationName { get; set; } =
            "CrmAnalytics.UnitTests";

        public string ContentRootPath { get; set; } =
            AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } =
            new NullFileProvider();
    }
}
