using System.Text;
using CrmAnalytics.Api.Authentication;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace CrmAnalytics.UnitTests;

public sealed class DevelopmentAuthenticationHandlerTests
{
    private const string UserId =
        "11111111-1111-4111-8111-111111111111";
    private const string TenantId =
        "22222222-2222-4222-8222-222222222222";

    [Fact]
    public async Task Authenticate_ProducesConfiguredUserPrincipal()
    {
        await using var provider = CreateProvider();
        var context = new DefaultHttpContext
        {
            RequestServices = provider
        };
        context.Request.Headers["X-User-Id"] =
            "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa";
        context.Request.Headers["X-Tenant-Id"] =
            "bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb";
        context.Request.Body = new MemoryStream(
            Encoding.UTF8.GetBytes(
                """{"userId":"cccccccc-cccc-4ccc-8ccc-cccccccccccc"}"""));

        var result = await context.AuthenticateAsync(
            DevelopmentAuthenticationDefaults.AuthenticationScheme);

        Assert.True(result.Succeeded);
        Assert.True(result.Principal?.Identity?.IsAuthenticated);
        Assert.Equal(
            UserId,
            result.Principal?.FindFirst("oid")?.Value);
        Assert.Equal(
            TenantId,
            result.Principal?.FindFirst("tid")?.Value);
        Assert.Equal(
            "access_as_user",
            result.Principal?.FindFirst("scp")?.Value);
        Assert.Equal(
            "user",
            result.Principal?.FindFirst("idtyp")?.Value);
        Assert.NotNull(result.Principal);
        Assert.Equal(
            ["Report.User", "Sales"],
            result.Principal.FindAll("roles")
                .Select(claim => claim.Value)
                .ToArray());
    }

    private static ServiceProvider CreateProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOptions();
        services.Configure<CrmAnalyticsAuthenticationOptions>(options =>
        {
            options.Mode = AuthenticationModes.Development;
            options.RequiredScope = "access_as_user";
            options.Development = new DevelopmentIdentityOptions
            {
                UserId = UserId,
                TenantId = TenantId,
                Roles = ["Report.User", "Sales"]
            };
        });
        services.AddSingleton<IHostEnvironment>(
            new TestHostEnvironment());
        services
            .AddAuthentication(
                DevelopmentAuthenticationDefaults.AuthenticationScheme)
            .AddScheme<
                AuthenticationSchemeOptions,
                DevelopmentAuthenticationHandler>(
                DevelopmentAuthenticationDefaults.AuthenticationScheme,
                _ => { });
        return services.BuildServiceProvider();
    }

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
