using CrmAnalytics.Api.Authentication;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace CrmAnalytics.UnitTests;

public sealed class AuthenticationOptionsValidatorTests
{
    private const string UserId =
        "11111111-1111-4111-8111-111111111111";
    private const string TenantId =
        "22222222-2222-4222-8222-222222222222";
    private const string ClientId =
        "33333333-3333-4333-8333-333333333333";

    [Fact]
    public void DevelopmentMode_DevelopmentEnvironment_IsValid()
    {
        var result = Validate(
            ValidDevelopmentOptions(),
            Environments.Development,
            ValidAzureAd());

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void DevelopmentMode_ProductionEnvironment_IsRejected()
    {
        var result = Validate(
            ValidDevelopmentOptions(),
            Environments.Production,
            ValidAzureAd());

        Assert.True(result.Failed);
    }

    [Fact]
    public void DevelopmentMode_EmptyUserId_IsRejected()
    {
        var options = ValidDevelopmentOptions();
        options.Development.UserId = "";

        Assert.True(Validate(
            options,
            Environments.Development,
            ValidAzureAd()).Failed);
    }

    [Fact]
    public void DevelopmentMode_InvalidTenantId_IsRejected()
    {
        var options = ValidDevelopmentOptions();
        options.Development.TenantId = "tenant";

        Assert.True(Validate(
            options,
            Environments.Development,
            ValidAzureAd()).Failed);
    }

    [Fact]
    public void EntraMode_EmptyTenantId_IsRejected()
    {
        var azureAd = ValidAzureAd();
        azureAd.TenantId = "";

        Assert.True(Validate(
            ValidEntraOptions(),
            Environments.Production,
            azureAd).Failed);
    }

    [Theory]
    [InlineData("common")]
    [InlineData("organizations")]
    [InlineData("consumers")]
    public void EntraMode_MultiTenantAlias_IsRejected(string tenantId)
    {
        var azureAd = ValidAzureAd();
        azureAd.TenantId = tenantId;

        Assert.True(Validate(
            ValidEntraOptions(),
            Environments.Production,
            azureAd).Failed);
    }

    [Fact]
    public void EntraMode_InvalidClientId_IsRejected()
    {
        var azureAd = ValidAzureAd();
        azureAd.ClientId = "client";

        Assert.True(Validate(
            ValidEntraOptions(),
            Environments.Production,
            azureAd).Failed);
    }

    [Fact]
    public void EntraMode_HttpInstance_IsRejected()
    {
        var azureAd = ValidAzureAd();
        azureAd.Instance = "http://login.microsoftonline.com/";

        Assert.True(Validate(
            ValidEntraOptions(),
            Environments.Production,
            azureAd).Failed);
    }

    [Theory]
    [InlineData("Entra", "Production")]
    [InlineData("Development", "Development")]
    public void EmptyRequiredScope_IsRejected(
        string mode,
        string environment)
    {
        var options = mode == AuthenticationModes.Entra
            ? ValidEntraOptions()
            : ValidDevelopmentOptions();
        options.RequiredScope = " ";

        Assert.True(Validate(options, environment, ValidAzureAd()).Failed);
    }

    private static ValidateOptionsResult Validate(
        CrmAnalyticsAuthenticationOptions options,
        string environment,
        AzureAdOptions azureAd)
    {
        var validator = new CrmAnalyticsAuthenticationOptionsValidator(
            new TestHostEnvironment { EnvironmentName = environment },
            Options.Create(azureAd));
        return validator.Validate(null, options);
    }

    private static CrmAnalyticsAuthenticationOptions
        ValidDevelopmentOptions() =>
        new()
        {
            Mode = AuthenticationModes.Development,
            RequiredScope = "access_as_user",
            Development = new DevelopmentIdentityOptions
            {
                UserId = UserId,
                TenantId = TenantId,
                Roles = ["Report.User"]
            }
        };

    private static CrmAnalyticsAuthenticationOptions ValidEntraOptions() =>
        new()
        {
            Mode = AuthenticationModes.Entra,
            RequiredScope = "access_as_user"
        };

    private static AzureAdOptions ValidAzureAd() =>
        new()
        {
            Instance = "https://login.microsoftonline.com/",
            TenantId = TenantId,
            ClientId = ClientId,
            Audience = $"api://{ClientId}"
        };

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } =
            Environments.Development;

        public string ApplicationName { get; set; } = "Tests";

        public string ContentRootPath { get; set; } = ".";

        public IFileProvider ContentRootFileProvider { get; set; } =
            new NullFileProvider();
    }
}
