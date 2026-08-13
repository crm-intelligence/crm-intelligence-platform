using CrmAnalytics.Infrastructure.Notifications;
using CrmAnalytics.Teams.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace CrmAnalytics.UnitTests;

public sealed class NotificationOptionsValidatorTests
{
    [Fact]
    public void BackendDisabled_AllowsEmptyApiKey()
    {
        var validator = new TeamsNotificationOptionsValidator(
            new TestHostEnvironment(Environments.Production));

        var result = validator.Validate(
            null,
            new TeamsNotificationOptions
            {
                Enabled = false,
                ApiKey = string.Empty
            });

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void BackendEnabled_RequiresApiKey()
    {
        var validator = new TeamsNotificationOptionsValidator(
            new TestHostEnvironment(Environments.Development));

        var result = validator.Validate(
            null,
            new TeamsNotificationOptions
            {
                Enabled = true,
                BaseUrl = "http://localhost:3978",
                ApiKey = string.Empty
            });

        Assert.True(result.Failed);
    }

    [Fact]
    public void BackendProduction_RequiresHttps()
    {
        var validator = new TeamsNotificationOptionsValidator(
            new TestHostEnvironment(Environments.Production));

        var result = validator.Validate(
            null,
            new TeamsNotificationOptions
            {
                Enabled = true,
                BaseUrl = "http://teams.example",
                ApiKey = "secret"
            });

        Assert.True(result.Failed);
    }

    [Fact]
    public void BackendRanges_AreValidated()
    {
        var validator = new TeamsNotificationOptionsValidator(
            new TestHostEnvironment(Environments.Development));

        var result = validator.Validate(
            null,
            new TeamsNotificationOptions
            {
                Enabled = false,
                TimeoutSeconds = 61,
                TargetNotReadyRetryCount = 11,
                TargetNotReadyInitialDelayMilliseconds = 9
            });

        Assert.True(result.Failed);
        Assert.Equal(3, result.Failures.Count());
    }

    [Fact]
    public void TeamsEndpointDisabled_AllowsEmptyApiKey()
    {
        var validator =
            new ReportNotificationEndpointOptionsValidator();

        var result = validator.Validate(
            null,
            new ReportNotificationEndpointOptions
            {
                Enabled = false,
                ApiKey = string.Empty
            });

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void TeamsEndpointEnabled_RequiresApiKey()
    {
        var validator =
            new ReportNotificationEndpointOptionsValidator();

        var result = validator.Validate(
            null,
            new ReportNotificationEndpointOptions
            {
                Enabled = true,
                ApiKey = " "
            });

        Assert.True(result.Failed);
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public TestHostEnvironment(string environmentName)
        {
            EnvironmentName = environmentName;
        }

        public string EnvironmentName { get; set; }

        public string ApplicationName { get; set; } = "Tests";

        public string ContentRootPath { get; set; } =
            AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } =
            new NullFileProvider();
    }
}
