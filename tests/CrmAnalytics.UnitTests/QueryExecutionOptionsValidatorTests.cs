using CrmAnalytics.Application.SqlProduction;
using CrmAnalytics.Infrastructure.QueryExecution;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace CrmAnalytics.UnitTests;

public sealed class QueryExecutionOptionsValidatorTests
{
    [Fact]
    public void Mock_IsAllowedInDevelopmentAndRejectedInProduction()
    {
        var options = new QueryExecutionOptions
        {
            Provider = QueryExecutionProviders.Mock
        };

        Assert.True(Validate(options, Environments.Development).Succeeded);
        Assert.True(Validate(options, Environments.Production).Failed);
    }

    [Fact]
    public void SqlClient_RequiresAnEnabledSource()
    {
        var options = CreateValidOptions();
        options.Dwh.Enabled = false;

        Assert.True(Validate(options, Environments.Development).Failed);
    }

    [Fact]
    public void Production_RejectsConnectionStringAuthentication()
    {
        var options = CreateValidOptions();
        options.Dwh.AuthenticationMode =
            QueryAuthenticationModes.ConnectionString;

        Assert.True(Validate(
            options,
            Environments.Production,
            LocalConnection()).Failed);
    }

    [Fact]
    public void EnabledManagedIdentity_RequiresNamedConnection()
    {
        var options = CreateValidOptions();

        Assert.True(Validate(options, Environments.Production).Failed);
    }

    [Theory]
    [InlineData(0, 100, 2048)]
    [InlineData(301, 100, 2048)]
    [InlineData(30, 0, 2048)]
    [InlineData(30, 100, 100)]
    public void InvalidLimits_AreRejected(
        int timeout,
        int rows,
        long bytes)
    {
        var options = CreateValidOptions();
        options.Dwh.CommandTimeoutCeilingSeconds = timeout;
        options.Dwh.MaxRows = rows;
        options.Dwh.MaxResultBytes = bytes;

        Assert.True(Validate(
            options,
            Environments.Development,
            LocalConnection()).Failed);
    }

    [Theory]
    [InlineData("Encrypt=False;TrustServerCertificate=False;Authentication=Active Directory Managed Identity")]
    [InlineData("Encrypt=True;TrustServerCertificate=True;Authentication=Active Directory Managed Identity")]
    [InlineData("Encrypt=True;TrustServerCertificate=False;User ID=sa;Password=secret")]
    public void Production_RejectsUnsafeConnectionProperties(
        string properties)
    {
        var options = CreateValidOptions();
        var connection = $"Server=tcp:example.database.windows.net;Database=warehouse;{properties}";

        Assert.True(Validate(
            options,
            Environments.Production,
            connection).Failed);
    }

    [Fact]
    public void DwhAndOltp_AreBoundAndRoutedSeparately()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["QueryExecution:Provider"] = "SqlClient",
                ["QueryExecution:Dwh:MaxRows"] = "10000",
                ["QueryExecution:Oltp:MaxRows"] = "1000",
                ["QueryExecution:Dwh:ConnectionStringName"] = "QueryDwh",
                ["QueryExecution:Oltp:ConnectionStringName"] = "QueryOltp"
            })
            .Build();
        var options = configuration.GetSection("QueryExecution")
            .Get<QueryExecutionOptions>();

        Assert.NotNull(options);
        Assert.Equal(10000, options.GetSource(SqlDataSource.Dwh).MaxRows);
        Assert.Equal(1000, options.GetSource(SqlDataSource.Oltp).MaxRows);
        Assert.Equal("QueryDwh", options.Dwh.ConnectionStringName);
        Assert.Equal("QueryOltp", options.Oltp.ConnectionStringName);
        Assert.Throws<QueryExecutionPermanentException>(
            () => options.GetSource(SqlDataSource.Unknown));
    }

    [Fact]
    public void Production_AcceptsSecureManagedIdentityConnection()
    {
        Assert.True(Validate(
            CreateValidOptions(),
            Environments.Production,
            ManagedIdentityConnection()).Succeeded);
    }

    [Fact]
    public void Production_AcceptsEnabledOltpWithSeparateSecureConnection()
    {
        var options = CreateValidOptions();
        options.Oltp.Enabled = true;

        Assert.True(Validate(
            options,
            Environments.Production,
            ManagedIdentityConnection("warehouse"),
            ManagedIdentityConnection("operational")).Succeeded);
    }

    [Fact]
    public void Production_EnabledOltpFailsFastWithoutItsConnection()
    {
        var options = CreateValidOptions();
        options.Oltp.Enabled = true;

        Assert.True(Validate(
            options,
            Environments.Production,
            ManagedIdentityConnection("warehouse")).Failed);
    }

    private static Microsoft.Extensions.Options.ValidateOptionsResult Validate(
        QueryExecutionOptions options,
        string environment,
        string? connection = null,
        string? oltpConnection = null)
    {
        var values = new Dictionary<string, string?>();
        if (connection is not null)
        {
            values["ConnectionStrings:QueryDwh"] = connection;
        }

        if (oltpConnection is not null)
        {
            values["ConnectionStrings:QueryOltp"] = oltpConnection;
        }

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
        return new QueryExecutionOptionsValidator(
                configuration,
                new TestHostEnvironment(environment))
            .Validate(null, options);
    }

    private static QueryExecutionOptions CreateValidOptions() => new()
    {
        Provider = QueryExecutionProviders.SqlClient,
        Dwh = new QuerySourceOptions
        {
            Enabled = true,
            AuthenticationMode = QueryAuthenticationModes.ManagedIdentity,
            ConnectionStringName = "QueryDwh",
            CommandTimeoutCeilingSeconds = 120,
            MaxRows = 10000,
            MaxColumns = 100,
            MaxCellCharacters = 10000,
            MaxResultBytes = 10485760,
            MaxRetryCount = 2,
            RetryBaseDelayMilliseconds = 500
        },
        Oltp = new QuerySourceOptions
        {
            Enabled = false,
            AuthenticationMode = QueryAuthenticationModes.ManagedIdentity,
            ConnectionStringName = "QueryOltp",
            CommandTimeoutCeilingSeconds = 15,
            MaxRows = 1000,
            MaxColumns = 50,
            MaxCellCharacters = 4000,
            MaxResultBytes = 2097152,
            MaxRetryCount = 1,
            RetryBaseDelayMilliseconds = 250
        }
    };

    private static string ManagedIdentityConnection(
        string database = "warehouse") =>
        $"Server=tcp:example.database.windows.net;Database={database};"
        + "Encrypt=True;TrustServerCertificate=False;"
        + "Persist Security Info=False;"
        + "Authentication=Active Directory Managed Identity;"
        + "Application Intent=ReadOnly";

    private static string LocalConnection() =>
        "Server=(localdb)\\mssqllocaldb;Database=test;"
        + "Integrated Security=True;Encrypt=True;TrustServerCertificate=True";

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public TestHostEnvironment(string environmentName)
        {
            EnvironmentName = environmentName;
        }

        public string EnvironmentName { get; set; }
        public string ApplicationName { get; set; } = "Tests";
        public string ContentRootPath { get; set; } = string.Empty;
        public IFileProvider ContentRootFileProvider { get; set; } =
            new NullFileProvider();
    }
}
