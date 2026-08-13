using CrmAnalytics.Teams.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace CrmAnalytics.UnitTests;

public sealed class TeamsHostOptionsValidatorTests
{
    [Fact]
    public void Production_WithCompleteCredentials_IsAccepted()
    {
        var result = CreateValidator(Environments.Production).Validate(
            null,
            CompleteOptions());

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Production_WithoutClientSecret_IsRejected()
    {
        var result = CreateValidator(Environments.Production).Validate(
            null,
            new TeamsHostOptions
            {
                ClientId = "11111111-1111-4111-8111-111111111111",
                TenantId = "22222222-2222-4222-8222-222222222222",
                AppType = "SingleTenant"
            });

        Assert.True(result.Failed);
        Assert.Contains(
            result.Failures,
            failure => failure.Contains(
                "Teams:ClientSecret",
                StringComparison.Ordinal));
    }

    [Fact]
    public void Production_WithNonSingleTenantAppType_IsRejected()
    {
        var options = new TeamsHostOptions
        {
            ClientId = "11111111-1111-4111-8111-111111111111",
            TenantId = "22222222-2222-4222-8222-222222222222",
            AppType = "MultiTenant",
            ClientSecret = "test-only-secret"
        };

        var result = CreateValidator(Environments.Production).Validate(
            null,
            options);

        Assert.True(result.Failed);
        Assert.Contains(
            result.Failures,
            failure => failure.Contains(
                "Teams:AppType",
                StringComparison.Ordinal));
    }

    [Fact]
    public void Development_WithoutCredentials_IsAccepted()
    {
        var result = CreateValidator(Environments.Development).Validate(
            null,
            new TeamsHostOptions());

        Assert.True(result.Succeeded);
    }

    private static TeamsHostOptionsValidator CreateValidator(
        string environmentName) =>
        new(new TestHostEnvironment
        {
            EnvironmentName = environmentName
        });

    private static TeamsHostOptions CompleteOptions() =>
        new()
        {
            ClientId = "11111111-1111-4111-8111-111111111111",
            TenantId = "22222222-2222-4222-8222-222222222222",
            AppType = "SingleTenant",
            ClientSecret = "test-only-secret"
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
