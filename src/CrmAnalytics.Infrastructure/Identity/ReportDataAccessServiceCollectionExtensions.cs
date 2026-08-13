using CrmAnalytics.Application.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace CrmAnalytics.Infrastructure.Identity;

public static class ReportDataAccessServiceCollectionExtensions
{
    public static IServiceCollection AddReportDataAccess(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        services.AddOptions<ReportDataAccessOptions>()
            .Bind(configuration.GetSection(ReportDataAccessOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<ReportDataAccessOptions>>(
            new ReportDataAccessOptionsValidator(configuration, environment));

        var provider = configuration[
            $"{ReportDataAccessOptions.SectionName}:Provider"];
        if (string.Equals(
            provider,
            ReportDataAccessOptions.SqlServerProvider,
            StringComparison.OrdinalIgnoreCase))
        {
            services.AddScoped<IUserDataAccessAssignmentStore,
                SqlUserDataAccessAssignmentStore>();
        }
        else
        {
            services.AddScoped<IUserDataAccessAssignmentStore,
                ConfigurationUserDataAccessAssignmentStore>();
        }

        services.AddScoped<IUserDataScopeResolver,
            CurrentUserDataScopeResolver>();
        return services;
    }
}
