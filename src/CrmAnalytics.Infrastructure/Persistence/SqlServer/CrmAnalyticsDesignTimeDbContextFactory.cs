using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace CrmAnalytics.Infrastructure.Persistence.SqlServer;

public sealed class CrmAnalyticsDesignTimeDbContextFactory
    : IDesignTimeDbContextFactory<CrmAnalyticsDbContext>
{
    private const string MigrationsConnectionEnvironmentVariable =
        "CRM_ANALYTICS_MIGRATIONS_CONNECTION";

    public CrmAnalyticsDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable(
            MigrationsConnectionEnvironmentVariable);

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            connectionString =
                "Server=(localdb)\\mssqllocaldb;"
                + "Database=CrmAnalyticsMigrations;"
                + "Trusted_Connection=True;"
                + "TrustServerCertificate=True";
        }

        var options = new DbContextOptionsBuilder<
                CrmAnalyticsDbContext>()
            .UseSqlServer(
                connectionString,
                sqlServer => sqlServer.MigrationsAssembly(
                    typeof(CrmAnalyticsDbContext).Assembly.FullName))
            .Options;

        return new CrmAnalyticsDbContext(options);
    }
}
