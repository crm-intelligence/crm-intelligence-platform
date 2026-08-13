using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace CrmAnalytics.IntegrationTests;

public sealed class AuthenticationStartupValidationTests
{
    [Fact]
    public void ProductionEnvironment_DevelopmentMode_FailsOnStartup()
    {
        using var factory = new ProductionDevelopmentAuthFactory();

        Assert.Throws<OptionsValidationException>(
            () => factory.CreateClient());
    }

    private sealed class ProductionDevelopmentAuthFactory
        : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Production");
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["Persistence:Provider"] = "SqlServer",
                        ["ConnectionStrings:CrmAnalytics"] =
                            "Server=localhost;"
                            + "Database=CrmAnalyticsStartupValidation;"
                            + "Integrated Security=True;"
                            + "TrustServerCertificate=True",
                        ["CrmAnalyticsAuthentication:Mode"] =
                            "Development",
                        ["CrmAnalyticsAuthentication:RequiredScope"] =
                            "access_as_user",
                        ["CrmAnalyticsAuthentication:Development:UserId"] =
                            "11111111-1111-4111-8111-111111111111",
                        ["CrmAnalyticsAuthentication:Development:TenantId"] =
                            "22222222-2222-4222-8222-222222222222",
                        ["CrmAnalyticsAuthentication:Development:Roles:0"] =
                            "Report.User"
                    });
            });
        }
    }
}
