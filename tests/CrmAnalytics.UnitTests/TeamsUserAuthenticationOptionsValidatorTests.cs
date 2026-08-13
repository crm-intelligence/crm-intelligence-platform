using CrmAnalytics.Teams.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace CrmAnalytics.UnitTests;

public sealed class TeamsUserAuthenticationOptionsValidatorTests
{
    [Fact]
    public void DevelopmentMode_InDevelopment_IsAccepted()
    {
        var result = CreateValidator(
            Environments.Development,
            skipAuth: true).Validate(
                null,
                DevelopmentOptions());

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void DevelopmentMode_InProduction_IsRejected()
    {
        var result = CreateValidator(
            Environments.Production,
            skipAuth: false).Validate(
                null,
                DevelopmentOptions());

        Assert.True(result.Failed);
    }

    [Fact]
    public void EntraMode_WithoutConnectionName_IsRejected()
    {
        var result = CreateValidator(
            Environments.Development,
            skipAuth: false).Validate(
                null,
                new TeamsUserAuthenticationOptions
                {
                    Mode = TeamsUserAuthenticationMode.Entra
                });

        Assert.True(result.Failed);
    }

    [Fact]
    public void EntraMode_WithSkipAuth_IsRejected()
    {
        var result = CreateValidator(
            Environments.Production,
            skipAuth: true).Validate(
                null,
                EntraOptions());

        Assert.True(result.Failed);
    }

    [Fact]
    public void EntraMode_WithConnectionAndAuthEnabled_IsAccepted()
    {
        var result = CreateValidator(
            Environments.Production,
            skipAuth: false).Validate(
                null,
                EntraOptions());

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void UnknownMode_IsRejected()
    {
        var result = CreateValidator(
            Environments.Development,
            skipAuth: false).Validate(
                null,
                new TeamsUserAuthenticationOptions
                {
                    Mode = (TeamsUserAuthenticationMode)42,
                    OAuthConnectionName = "connection"
                });

        Assert.True(result.Failed);
    }

    private static TeamsUserAuthenticationOptionsValidator
        CreateValidator(
            string environmentName,
            bool skipAuth) =>
        new(
            new TestHostEnvironment
            {
                EnvironmentName = environmentName
            },
            Options.Create(new TeamsHostOptions
            {
                SkipAuth = skipAuth
            }));

    private static TeamsUserAuthenticationOptions DevelopmentOptions() =>
        new()
        {
            Mode = TeamsUserAuthenticationMode.Development
        };

    private static TeamsUserAuthenticationOptions EntraOptions() =>
        new()
        {
            Mode = TeamsUserAuthenticationMode.Entra,
            OAuthConnectionName = "crm-analytics-sso"
        };

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } =
            Environments.Development;

        public string ApplicationName { get; set; } = "UnitTests";

        public string ContentRootPath { get; set; } =
            Directory.GetCurrentDirectory();

        public IFileProvider ContentRootFileProvider { get; set; } =
            new NullFileProvider();
    }
}
