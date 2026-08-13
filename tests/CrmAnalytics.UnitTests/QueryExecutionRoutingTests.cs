using System.Collections;
using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Reflection;
using CrmAnalytics.Application.Abstractions.Integrations;
using CrmAnalytics.Application.SqlProduction;
using CrmAnalytics.Infrastructure.QueryExecution;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CrmAnalytics.UnitTests;

public sealed class QueryExecutionRoutingTests
{
    [Fact]
    public void Plan_RejectsUnknownSource()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CreatePlan(
            SqlDataSource.Unknown));
        Assert.Throws<ArgumentOutOfRangeException>(() => CreatePlan(
            (SqlDataSource)999));
    }

    [Theory]
    [InlineData(SqlDataSource.Dwh)]
    [InlineData(SqlDataSource.Oltp)]
    public async Task MockProvider_PreservesExplicitSource(
        SqlDataSource source)
    {
        IQueryExecutionClient client = new MockQueryExecutionClient(
            TimeProvider.System,
            new QueryResultReferenceFactory());

        var result = await client.ExecuteAsync(
            CreatePlan(source),
            CancellationToken.None);

        Assert.Equal(source, result.Source);
        Assert.Empty(result.Rows);
        Assert.Empty(result.Columns);
    }

    [Theory]
    [InlineData(SqlDataSource.Dwh)]
    [InlineData(SqlDataSource.Oltp)]
    public async Task SqlClient_RoutesOnlyToPlanSourceWithoutFallback(
        SqlDataSource source)
    {
        var factory = new RecordingFailClosedConnectionFactory();
        var client = CreateClient(factory, CreateOptions());

        var exception = await Assert.ThrowsAsync<
            QueryExecutionPermanentException>(() => client.ExecuteAsync(
                CreatePlan(source),
                CancellationToken.None));

        Assert.Equal(QueryExecutionErrorCodes.AccessDenied,
            exception.ErrorCode);
        Assert.Equal([source], factory.Sources);
    }

    [Fact]
    public async Task DisabledSource_FailsBeforeOpeningAnotherSource()
    {
        var options = CreateOptions();
        options.Oltp.Enabled = false;
        var factory = new RecordingFailClosedConnectionFactory();
        var client = CreateClient(factory, options);

        var exception = await Assert.ThrowsAsync<
            QueryExecutionPermanentException>(() => client.ExecuteAsync(
                CreatePlan(SqlDataSource.Oltp),
                CancellationToken.None));

        Assert.Equal(QueryExecutionErrorCodes.SourceDisabled,
            exception.ErrorCode);
        Assert.Empty(factory.Sources);
    }

    [Fact]
    public async Task Oltp_BracketedCanonicalObjectReachesOnlyOltpConnection()
    {
        var factory = new RecordingFailClosedConnectionFactory();
        var client = CreateClient(factory, CreateOptions());

        var exception = await Assert.ThrowsAsync<QueryExecutionPermanentException>(() =>
            client.ExecuteAsync(
                CreatePlan(
                    SqlDataSource.Oltp,
                    "SELECT order_id FROM [dbo].[vw_operational_orders]"),
                CancellationToken.None));

        Assert.Equal(QueryExecutionErrorCodes.AccessDenied, exception.ErrorCode);
        Assert.Equal([SqlDataSource.Oltp], factory.Sources);
    }

    [Theory]
    [InlineData("dbo.customers")]
    [InlineData("dbo.products")]
    [InlineData("dbo.orders")]
    [InlineData("dbo.order_items")]
    [InlineData("dbo.order_payments")]
    public async Task Oltp_BaseTablesAreRejectedBeforeConnection(
        string objectName)
    {
        var factory = new RecordingFailClosedConnectionFactory();
        var client = CreateClient(factory, CreateOptions());
        var plan = CreatePlan(
            SqlDataSource.Oltp,
            $"SELECT 1 FROM {objectName}");

        var exception = await Assert.ThrowsAsync<
            QueryExecutionPermanentException>(() => client.ExecuteAsync(
                plan,
                CancellationToken.None));

        Assert.Equal(QueryExecutionErrorCodes.Failed,
            exception.ErrorCode);
        Assert.Empty(factory.Sources);
    }

    [Theory]
    [InlineData("vw_operational_orders")]
    [InlineData("dwh.vw_operational_orders")]
    [InlineData("mart.vw_operational_orders")]
    [InlineData("mart.vw_sales")]
    [InlineData("database.dbo.vw_operational_orders")]
    [InlineData("dbo.vw_operati\u043enal_orders")]
    public async Task Oltp_UnknownCrossSourceOrNonCanonicalObjectsAreRejected(
        string objectName)
    {
        var factory = new RecordingFailClosedConnectionFactory();
        var client = CreateClient(factory, CreateOptions());

        await Assert.ThrowsAsync<QueryExecutionPermanentException>(() =>
            client.ExecuteAsync(
                CreatePlan(SqlDataSource.Oltp, $"SELECT 1 FROM {objectName}"),
                CancellationToken.None));

        Assert.Empty(factory.Sources);
    }

    [Theory]
    [InlineData("mart.vw_sales")]
    [InlineData("mart.vw_customer_rfm")]
    [InlineData("mart.vw_payment")]
    [InlineData("mart.vw_sales_detail")]
    [InlineData("mart.vw_monthly_sales")]
    [InlineData("mart.vw_sales_by_region")]
    [InlineData("mart.vw_sales_by_category")]
    [InlineData("mart.vw_customer_rfm_segmented")]
    [InlineData("mart.vw_payment_summary")]
    public async Task Dwh_ContractViewsReachOnlyDwhConnection(string objectName)
    {
        var factory = new RecordingFailClosedConnectionFactory();
        var client = CreateClient(factory, CreateOptions());

        var exception = await Assert.ThrowsAsync<
            QueryExecutionPermanentException>(() => client.ExecuteAsync(
                CreatePlan(SqlDataSource.Dwh, $"SELECT 1 FROM {objectName}"),
                CancellationToken.None));

        Assert.Equal(QueryExecutionErrorCodes.AccessDenied, exception.ErrorCode);
        Assert.Equal([SqlDataSource.Dwh], factory.Sources);
    }

    [Theory]
    [InlineData("vw_sales")]
    [InlineData("dwh.dim_customer")]
    [InlineData("dwh.dim_product")]
    [InlineData("dwh.dim_date")]
    [InlineData("dwh.fact_sales")]
    [InlineData("mart.not_allowed")]
    [InlineData("dbo.vw_operational_orders")]
    [InlineData("[dwh].[fact_sales] -- mart.vw_sales")]
    [InlineData("[mart].[vw_sales]]; DROP TABLE dwh.fact_sales--]")]
    public async Task Dwh_UnknownOrUnqualifiedObjectsAreRejectedBeforeConnection(
        string objectName)
    {
        var factory = new RecordingFailClosedConnectionFactory();
        var client = CreateClient(factory, CreateOptions());

        var exception = await Assert.ThrowsAsync<
            QueryExecutionPermanentException>(() => client.ExecuteAsync(
                CreatePlan(SqlDataSource.Dwh, $"SELECT 1 FROM {objectName}"),
                CancellationToken.None));

        Assert.Equal(QueryExecutionErrorCodes.Failed, exception.ErrorCode);
        Assert.Empty(factory.Sources);
    }

    [Theory]
    [InlineData("INSERT INTO mart.vw_sales DEFAULT VALUES")]
    [InlineData("UPDATE mart.vw_sales SET order_id = order_id")]
    [InlineData("DELETE FROM mart.vw_sales")]
    [InlineData("MERGE mart.vw_sales AS target USING mart.vw_sales AS source ON 1 = 0 WHEN MATCHED THEN DELETE;")]
    [InlineData("EXEC mart.vw_sales")]
    [InlineData("EXECUTE mart.vw_sales")]
    [InlineData("CREATE TABLE mart.disallowed (id int)")]
    [InlineData("ALTER TABLE mart.vw_sales ADD id int")]
    [InlineData("DROP VIEW mart.vw_sales")]
    [InlineData("TRUNCATE TABLE mart.vw_sales")]
    [InlineData("SELECT 1 AS id INTO mart.disallowed FROM mart.vw_sales")]
    [InlineData("SELECT 1 FROM mart.vw_sales; SELECT 1 FROM mart.vw_sales")]
    public async Task Dwh_NonSelectOrMultiStatementSqlIsRejectedBeforeConnection(
        string sql)
    {
        var factory = new RecordingFailClosedConnectionFactory();
        var client = CreateClient(factory, CreateOptions());

        var exception = await Assert.ThrowsAsync<
            QueryExecutionPermanentException>(() => client.ExecuteAsync(
                CreatePlan(SqlDataSource.Dwh, sql),
                CancellationToken.None));

        Assert.Equal(QueryExecutionErrorCodes.Failed, exception.ErrorCode);
        Assert.Empty(factory.Sources);
    }

    [Theory]
    [InlineData("INSERT INTO dbo.vw_operational_orders DEFAULT VALUES")]
    [InlineData("UPDATE dbo.vw_operational_orders SET order_id = order_id")]
    [InlineData("DELETE FROM dbo.vw_operational_orders")]
    [InlineData("MERGE dbo.vw_operational_orders AS target USING dbo.vw_operational_orders AS source ON 1 = 0 WHEN NOT MATCHED THEN INSERT DEFAULT VALUES;")]
    [InlineData("EXEC dbo.vw_operational_orders")]
    [InlineData("CREATE TABLE dbo.disallowed (id int)")]
    [InlineData("SELECT 1 AS id INTO dbo.disallowed FROM dbo.vw_operational_orders")]
    public async Task Oltp_NonSelectStatementsAreRejectedBeforeConnection(
        string sql)
    {
        var factory = new RecordingFailClosedConnectionFactory();
        var client = CreateClient(factory, CreateOptions());

        var exception = await Assert.ThrowsAsync<
            QueryExecutionPermanentException>(() => client.ExecuteAsync(
                CreatePlan(SqlDataSource.Oltp, sql),
                CancellationToken.None));

        Assert.Equal(QueryExecutionErrorCodes.Failed,
            exception.ErrorCode);
        Assert.Empty(factory.Sources);
    }

    [Fact]
    public async Task TimeoutAboveSourceCeiling_FailsClosed()
    {
        var factory = new RecordingFailClosedConnectionFactory();
        var client = CreateClient(factory, CreateOptions());
        var plan = new SqlExecutionPlan(
            SqlDataSource.Oltp,
            "SELECT 1",
            [],
            "store_id = @store",
            16,
            null);

        var exception = await Assert.ThrowsAsync<
            QueryExecutionPermanentException>(() => client.ExecuteAsync(
                plan,
                CancellationToken.None));

        Assert.Equal(QueryExecutionErrorCodes.Failed, exception.ErrorCode);
        Assert.Empty(factory.Sources);
    }

    [Fact]
    public async Task TransientConnectionFailure_UsesBoundedAttempts()
    {
        var factory = new AttemptFailureConnectionFactory(
            SqlQueryFailureKind.Transient);
        var client = CreateClient(factory, CreateOptions());

        var exception = await Assert.ThrowsAsync<
            QueryExecutionTransientException>(() => client.ExecuteAsync(
                CreatePlan(SqlDataSource.Dwh),
                CancellationToken.None));

        Assert.Equal(QueryExecutionErrorCodes.Transient,
            exception.ErrorCode);
        Assert.Equal(3, factory.CallCount);
        Assert.All(factory.Sources,
            source => Assert.Equal(SqlDataSource.Dwh, source));
    }

    [Fact]
    public void NumberZeroClass20_IsTransient()
    {
        var exception = CreateSqlException(0, 20);

        Assert.Equal(
            SqlQueryFailureKind.Transient,
            SqlQueryFailureClassifier.Classify(exception));
    }

    [Fact]
    public void NumberZeroWithAnotherClass_IsPermanent()
    {
        var exception = CreateSqlException(0, 19);

        Assert.Equal(
            SqlQueryFailureKind.Permanent,
            SqlQueryFailureClassifier.Classify(exception));
    }

    [Fact]
    public void UnknownNumberWithClass20_IsPermanent()
    {
        var exception = CreateSqlException(50000, 20);

        Assert.Equal(
            SqlQueryFailureKind.Permanent,
            SqlQueryFailureClassifier.Classify(exception));
    }

    [Theory]
    [InlineData(229)]
    [InlineData(916)]
    [InlineData(18456)]
    public void AccessAndAuthenticationErrors_RemainAccessDenied(int number)
    {
        var exception = CreateSqlException(number, 20);

        Assert.Equal(
            SqlQueryFailureKind.AccessDenied,
            SqlQueryFailureClassifier.Classify(exception));
    }

    [Fact]
    public async Task NumberZeroClass20BeforeReader_RetriesAndSucceeds()
    {
        var factory = new ClosedConnectionFactory();
        var sqlException = CreateSqlException(0, 20);
        var executeAttempts = 0;
        var client = CreateClient(
            factory,
            CreateOptions(),
            executeReaderAsync: (_, _, _) =>
            {
                executeAttempts++;
                if (executeAttempts == 1)
                {
                    return Task.FromException<DbDataReader>(sqlException);
                }

                var table = new DataTable();
                return Task.FromResult<DbDataReader>(
                    table.CreateDataReader());
            });

        var result = await client.ExecuteAsync(
            CreatePlan(SqlDataSource.Dwh),
            CancellationToken.None);

        Assert.Empty(result.Columns);
        Assert.Empty(result.Rows);
        Assert.Equal(2, executeAttempts);
        Assert.Equal(2, factory.CallCount);
    }

    [Fact]
    public async Task NumberZeroClass20BeforeReader_ExhaustsRetriesAsTransient()
    {
        var factory = new ClosedConnectionFactory();
        var sqlException = CreateSqlException(0, 20);
        var executeAttempts = 0;
        var client = CreateClient(
            factory,
            CreateOptions(),
            executeReaderAsync: (_, _, _) =>
            {
                executeAttempts++;
                return Task.FromException<DbDataReader>(sqlException);
            });

        var exception = await Assert.ThrowsAsync<
            QueryExecutionTransientException>(() => client.ExecuteAsync(
                CreatePlan(SqlDataSource.Dwh),
                CancellationToken.None));

        Assert.Equal(QueryExecutionErrorCodes.Transient, exception.ErrorCode);
        Assert.Equal(3, executeAttempts);
        Assert.Equal(3, factory.CallCount);
    }

    [Fact]
    public async Task NumberZeroClass20AfterReaderStarted_DoesNotRetry()
    {
        var factory = new ClosedConnectionFactory();
        var sqlException = CreateSqlException(0, 20);
        var executeAttempts = 0;
        var client = CreateClient(
            factory,
            CreateOptions(),
            executeReaderAsync: (_, _, _) =>
            {
                executeAttempts++;
                return Task.FromResult<DbDataReader>(
                    new ThrowingFieldCountDataReader(sqlException));
            });

        var exception = await Assert.ThrowsAsync<
            QueryExecutionPermanentException>(() => client.ExecuteAsync(
                CreatePlan(SqlDataSource.Dwh),
                CancellationToken.None));

        Assert.Equal(QueryExecutionErrorCodes.Failed, exception.ErrorCode);
        Assert.Equal(1, executeAttempts);
        Assert.Equal(1, factory.CallCount);
    }

    [Theory]
    [InlineData(SqlQueryFailureKind.AccessDenied,
        QueryExecutionErrorCodes.AccessDenied)]
    [InlineData(SqlQueryFailureKind.Timeout,
        QueryExecutionErrorCodes.Timeout)]
    [InlineData(SqlQueryFailureKind.Permanent,
        QueryExecutionErrorCodes.Failed)]
    public async Task NonTransientConnectionFailure_DoesNotRetry(
        SqlQueryFailureKind failureKind,
        string expectedCode)
    {
        var factory = new AttemptFailureConnectionFactory(failureKind);
        var client = CreateClient(factory, CreateOptions());

        var thrown = await Record.ExceptionAsync(
            () => client.ExecuteAsync(
                CreatePlan(SqlDataSource.Dwh),
                CancellationToken.None));
        var exception = Assert.IsAssignableFrom<QueryExecutionException>(
            thrown);

        Assert.Equal(expectedCode, exception.ErrorCode);
        Assert.Equal(1, factory.CallCount);
    }

    [Fact]
    public async Task Cancellation_DoesNotRetryOrBecomeFailed()
    {
        var factory = new CancelingConnectionFactory();
        var client = CreateClient(factory, CreateOptions());

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => client.ExecuteAsync(
                CreatePlan(SqlDataSource.Dwh),
                CancellationToken.None));

        Assert.Equal(1, factory.CallCount);
    }

    [Fact]
    public async Task DwhFailedConnection_LogsOnlySafeExceptionTelemetry()
    {
        var logger = new RecordingLogger<SqlClientQueryExecutionClient>();
        var sensitive = "sql=SELECT secret;connection=Server=secret;token=secret;user=secret";
        var factory = new FailedConnectionFactory(
            new SqlQueryConnectionAttemptException(
                SqlQueryFailureKind.Permanent,
                new InvalidOperationException(sensitive)));
        var client = CreateClient(factory, CreateOptions(), logger: logger);

        await Assert.ThrowsAsync<QueryExecutionPermanentException>(() =>
            client.ExecuteAsync(
                CreatePlan(SqlDataSource.Dwh),
                CancellationToken.None));

        var message = Assert.Single(logger.Messages);
        Assert.Contains("SqlQueryConnectionAttemptException", message);
        Assert.Contains("InvalidOperationException", message);
        Assert.Contains("Stage: connection-open", message);
        Assert.Contains("Retryable: False", message);
        Assert.DoesNotContain(sensitive, message);
        Assert.DoesNotContain("SELECT secret", message);
        Assert.DoesNotContain("Server=secret", message);
    }

    [Fact]
    public async Task DwhFailedCommandCreation_LogsCommandCreateStage()
    {
        var logger = new RecordingLogger<SqlClientQueryExecutionClient>();
        var client = CreateClient(
            new ClosedConnectionFactory(),
            CreateOptions(),
            new ThrowingParameterBinder(),
            logger);

        await Assert.ThrowsAsync<QueryExecutionPermanentException>(() =>
            client.ExecuteAsync(
                CreatePlan(SqlDataSource.Dwh),
                CancellationToken.None));

        var message = Assert.Single(logger.Messages);
        Assert.Contains("InvalidOperationException", message);
        Assert.Contains("Stage: command-create", message);
        Assert.DoesNotContain("mart.vw_sales", message);
        Assert.DoesNotContain("parameter=user-data", message);
    }

    [Fact]
    public async Task DwhFailedExecute_LogsExecuteStage()
    {
        var logger = new RecordingLogger<SqlClientQueryExecutionClient>();
        var client = CreateClient(
            new ClosedConnectionFactory(),
            CreateOptions(),
            logger: logger);

        await Assert.ThrowsAsync<QueryExecutionPermanentException>(() =>
            client.ExecuteAsync(
                CreatePlan(SqlDataSource.Dwh),
                CancellationToken.None));

        var message = Assert.Single(logger.Messages);
        Assert.Contains("InvalidOperationException", message);
        Assert.Contains("Stage: execute", message);
        Assert.DoesNotContain("mart.vw_sales", message);
    }

    private static SqlClientQueryExecutionClient CreateClient(
        ISqlQueryConnectionFactory factory,
        QueryExecutionOptions options,
        IQueryExecutionParameterBinder? parameterBinder = null,
        ILogger<SqlClientQueryExecutionClient>? logger = null,
        Func<SqlCommand, CommandBehavior, CancellationToken,
            Task<DbDataReader>>? executeReaderAsync = null)
    {
        var binder = parameterBinder
            ?? new SqlQueryExecutionParameterBinder();
        var actualLogger = logger
            ?? NullLogger<SqlClientQueryExecutionClient>.Instance;
        if (executeReaderAsync is null)
        {
            return new SqlClientQueryExecutionClient(
                factory,
                binder,
                new QueryResultMaterializer(),
                new QueryResultReferenceFactory(),
                Options.Create(options),
                TimeProvider.System,
                actualLogger);
        }

        return new SqlClientQueryExecutionClient(
            factory,
            binder,
            new QueryResultMaterializer(),
            new QueryResultReferenceFactory(),
            Options.Create(options),
            TimeProvider.System,
            actualLogger,
            executeReaderAsync);
    }

    private static SqlException CreateSqlException(
        int number,
        byte errorClass)
    {
        var error = (SqlError)Activator.CreateInstance(
            typeof(SqlError),
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            args:
            [
                number,
                (byte)0,
                errorClass,
                "server",
                "transport failure",
                "procedure",
                1,
                0,
                null
            ],
            culture: CultureInfo.InvariantCulture)!;
        var errors = (SqlErrorCollection)Activator.CreateInstance(
            typeof(SqlErrorCollection),
            nonPublic: true)!;
        typeof(SqlErrorCollection).GetMethod(
            "Add",
            BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(errors, [error]);
        var createException = typeof(SqlException)
            .GetMethods(BindingFlags.Static | BindingFlags.NonPublic)
            .Single(method =>
            {
                if (method.Name != "CreateException")
                    return false;
                var parameters = method.GetParameters();
                return parameters.Length == 2
                    && parameters[0].ParameterType
                        == typeof(SqlErrorCollection)
                    && parameters[1].ParameterType == typeof(string);
            });
        var exception = (SqlException)createException.Invoke(
            null,
            [errors, "16.0.0"])!;

        Assert.Equal(number, exception.Number);
        Assert.Equal(errorClass, exception.Class);
        return exception;
    }

    private static SqlExecutionPlan CreatePlan(
        SqlDataSource source,
        string? sql = null) => new(
        source,
        sql ?? (source == SqlDataSource.Oltp
            ? "SELECT 1 FROM dbo.vw_operational_orders"
            : "SELECT 1 FROM mart.vw_sales"),
        [],
        "store_id = @store",
        source == SqlDataSource.Oltp ? 15 : 30,
        null);

    private static QueryExecutionOptions CreateOptions() => new()
    {
        Provider = QueryExecutionProviders.SqlClient,
        Dwh = CreateSource(120),
        Oltp = CreateSource(15)
    };

    private static QuerySourceOptions CreateSource(int timeout) => new()
    {
        Enabled = true,
        CommandTimeoutCeilingSeconds = timeout,
        MaxRows = 100,
        MaxColumns = 20,
        MaxCellCharacters = 1000,
        MaxResultBytes = 1024 * 1024,
        MaxRetryCount = 2,
        RetryBaseDelayMilliseconds = 50
    };

    private sealed class RecordingFailClosedConnectionFactory
        : ISqlQueryConnectionFactory
    {
        public List<SqlDataSource> Sources { get; } = [];

        public ValueTask<SqlConnection> CreateOpenConnectionAsync(
            SqlDataSource source,
            CancellationToken cancellationToken)
        {
            Sources.Add(source);
            throw new QueryExecutionPermanentException(
                QueryExecutionErrorCodes.AccessDenied,
                source,
                TimeSpan.FromMilliseconds(1));
        }
    }

    private sealed class AttemptFailureConnectionFactory
        : ISqlQueryConnectionFactory
    {
        private readonly SqlQueryFailureKind _kind;

        public AttemptFailureConnectionFactory(SqlQueryFailureKind kind)
        {
            _kind = kind;
        }

        public int CallCount { get; private set; }
        public List<SqlDataSource> Sources { get; } = [];

        public ValueTask<SqlConnection> CreateOpenConnectionAsync(
            SqlDataSource source,
            CancellationToken cancellationToken)
        {
            CallCount++;
            Sources.Add(source);
            throw new SqlQueryConnectionAttemptException(_kind);
        }
    }

    private sealed class CancelingConnectionFactory
        : ISqlQueryConnectionFactory
    {
        public int CallCount { get; private set; }

        public ValueTask<SqlConnection> CreateOpenConnectionAsync(
            SqlDataSource source,
            CancellationToken cancellationToken)
        {
            CallCount++;
            throw new OperationCanceledException(cancellationToken);
        }
    }

    private sealed class FailedConnectionFactory(Exception exception)
        : ISqlQueryConnectionFactory
    {
        public ValueTask<SqlConnection> CreateOpenConnectionAsync(
            SqlDataSource source,
            CancellationToken cancellationToken) => throw exception;
    }

    private sealed class ClosedConnectionFactory
        : ISqlQueryConnectionFactory
    {
        public int CallCount { get; private set; }

        public ValueTask<SqlConnection> CreateOpenConnectionAsync(
            SqlDataSource source,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return ValueTask.FromResult(new SqlConnection());
        }
    }

    private sealed class ThrowingFieldCountDataReader(SqlException exception)
        : DbDataReader
    {
        public override int FieldCount => throw exception;
        public override bool HasRows => false;
        public override bool IsClosed => false;
        public override int RecordsAffected => 0;
        public override int Depth => 0;
        public override object this[int ordinal] =>
            throw new NotSupportedException();
        public override object this[string name] =>
            throw new NotSupportedException();

        public override void Close()
        {
        }

        public override DataTable? GetSchemaTable() => null;
        public override bool NextResult() => false;
        public override bool Read() => false;
        public override bool GetBoolean(int ordinal) =>
            throw new NotSupportedException();
        public override byte GetByte(int ordinal) =>
            throw new NotSupportedException();
        public override long GetBytes(
            int ordinal,
            long dataOffset,
            byte[]? buffer,
            int bufferOffset,
            int length) => throw new NotSupportedException();
        public override char GetChar(int ordinal) =>
            throw new NotSupportedException();
        public override long GetChars(
            int ordinal,
            long dataOffset,
            char[]? buffer,
            int bufferOffset,
            int length) => throw new NotSupportedException();
        public override string GetDataTypeName(int ordinal) =>
            throw new NotSupportedException();
        public override DateTime GetDateTime(int ordinal) =>
            throw new NotSupportedException();
        public override decimal GetDecimal(int ordinal) =>
            throw new NotSupportedException();
        public override double GetDouble(int ordinal) =>
            throw new NotSupportedException();
        public override IEnumerator GetEnumerator() =>
            throw new NotSupportedException();
        public override Type GetFieldType(int ordinal) =>
            throw new NotSupportedException();
        public override float GetFloat(int ordinal) =>
            throw new NotSupportedException();
        public override Guid GetGuid(int ordinal) =>
            throw new NotSupportedException();
        public override short GetInt16(int ordinal) =>
            throw new NotSupportedException();
        public override int GetInt32(int ordinal) =>
            throw new NotSupportedException();
        public override long GetInt64(int ordinal) =>
            throw new NotSupportedException();
        public override string GetName(int ordinal) =>
            throw new NotSupportedException();
        public override int GetOrdinal(string name) =>
            throw new NotSupportedException();
        public override string GetString(int ordinal) =>
            throw new NotSupportedException();
        public override object GetValue(int ordinal) =>
            throw new NotSupportedException();
        public override int GetValues(object[] values) =>
            throw new NotSupportedException();
        public override bool IsDBNull(int ordinal) =>
            throw new NotSupportedException();
    }

    private sealed class ThrowingParameterBinder
        : IQueryExecutionParameterBinder
    {
        public void Bind(
            SqlCommand command,
            IReadOnlyCollection<SqlExecutionParameter> parameters,
            SqlDataSource source) =>
            throw new InvalidOperationException("parameter=user-data");
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Messages.Add(formatter(state, exception));
    }
}
