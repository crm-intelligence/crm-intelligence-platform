using System.Data;
using Microsoft.Data.SqlClient;

namespace CrmAnalytics.DataScopeProvisioner;

public sealed class SqlDataScopeStore : IDataScopeStore
{
    private const string ReadSql = """
        SELECT
            (SELECT COUNT_BIG(*)
             FROM [crm].[UserDataAccessAssignments]
             WHERE [TenantId] = @tenantId AND [UserId] = @userId),
            (SELECT TOP (1) [IsActive]
             FROM [crm].[UserDataAccessAssignments]
             WHERE [TenantId] = @tenantId AND [UserId] = @userId),
            (SELECT TOP (1) [AllowAllRegions]
             FROM [crm].[UserDataAccessAssignments]
             WHERE [TenantId] = @tenantId AND [UserId] = @userId),
            (SELECT TOP (1) [AllowAllStores]
             FROM [crm].[UserDataAccessAssignments]
             WHERE [TenantId] = @tenantId AND [UserId] = @userId),
            (SELECT COUNT_BIG(*)
             FROM [crm].[UserDataAccessRegions]
             WHERE [TenantId] = @tenantId AND [UserId] = @userId),
            (SELECT COUNT_BIG(*)
             FROM [crm].[UserDataAccessStores]
             WHERE [TenantId] = @tenantId AND [UserId] = @userId);
        """;

    private const string LockedReadSql = """
        SELECT [IsActive], [AllowAllRegions], [AllowAllStores]
        FROM [crm].[UserDataAccessAssignments] WITH (UPDLOCK, HOLDLOCK)
        WHERE [TenantId] = @tenantId AND [UserId] = @userId;

        SELECT COUNT_BIG(*)
        FROM [crm].[UserDataAccessRegions] WITH (UPDLOCK, HOLDLOCK)
        WHERE [TenantId] = @tenantId AND [UserId] = @userId;

        SELECT COUNT_BIG(*)
        FROM [crm].[UserDataAccessStores] WITH (UPDLOCK, HOLDLOCK)
        WHERE [TenantId] = @tenantId AND [UserId] = @userId;
        """;

    private const string InsertSql = """
        INSERT INTO [crm].[UserDataAccessAssignments]
            ([TenantId], [UserId], [AllowAllRegions], [AllowAllStores],
             [IsActive], [CreatedAt], [UpdatedAt])
        VALUES
            (@tenantId, @userId, CAST(1 AS bit), CAST(1 AS bit),
             CAST(1 AS bit), @now, @now);
        """;

    private const string UpdateSql = """
        UPDATE [crm].[UserDataAccessAssignments]
        SET [AllowAllRegions] = CAST(1 AS bit),
            [AllowAllStores] = CAST(1 AS bit),
            [IsActive] = CAST(1 AS bit),
            [UpdatedAt] = @now
        WHERE [TenantId] = @tenantId AND [UserId] = @userId;
        """;

    private const string DeleteChildrenSql = """
        DELETE FROM [crm].[UserDataAccessRegions]
        WHERE [TenantId] = @tenantId AND [UserId] = @userId;

        DELETE FROM [crm].[UserDataAccessStores]
        WHERE [TenantId] = @tenantId AND [UserId] = @userId;
        """;

    private readonly SqlConnection connection;

    public SqlDataScopeStore(
        string server,
        string database,
        Guid managedIdentityClientId)
    {
        var builder = new SqlConnectionStringBuilder
        {
            DataSource = server,
            InitialCatalog = database,
            Authentication = SqlAuthenticationMethod.ActiveDirectoryManagedIdentity,
            UserID = managedIdentityClientId.ToString("D"),
            Encrypt = true,
            TrustServerCertificate = false,
            ConnectTimeout = 30,
            ApplicationName = "CrmAnalytics.DataScopeProvisioner"
        };
        connection = new SqlConnection(builder.ConnectionString);
    }

    public async Task<ScopeState> ReadAsync(
        ScopeKey target,
        CancellationToken cancellationToken)
    {
        await EnsureOpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = ReadSql;
        AddTargetParameters(command, target);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new DataException("Target-scoped state query returned no result.");
        }

        return new ScopeState(
            reader.GetInt64(0),
            !reader.IsDBNull(1) && reader.GetBoolean(1),
            !reader.IsDBNull(2) && reader.GetBoolean(2),
            !reader.IsDBNull(3) && reader.GetBoolean(3),
            reader.GetInt64(4),
            reader.GetInt64(5));
    }

    public async Task<MutationResult> ApplyUnrestrictedAsync(
        ScopeKey target,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await EnsureOpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        try
        {
            var lockedState = await ReadLockedAsync(target, transaction, cancellationToken);
            if (lockedState.IsValidUnrestricted)
            {
                await transaction.CommitAsync(cancellationToken);
                return new MutationResult(MutationDecision.NoOp, lockedState);
            }

            if (lockedState.AssignmentCount > 1)
            {
                throw new DataException("Composite-key invariant failed for target scope.");
            }

            var decision = lockedState.AssignmentCount == 0
                ? MutationDecision.Insert
                : MutationDecision.Update;
            await ExecuteMutationAsync(
                decision == MutationDecision.Insert ? InsertSql : UpdateSql,
                target,
                now,
                transaction,
                cancellationToken);
            await ExecuteMutationAsync(
                DeleteChildrenSql,
                target,
                now: null,
                transaction,
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new MutationResult(decision, lockedState);
        }
        catch
        {
            try
            {
                await transaction.RollbackAsync(CancellationToken.None);
            }
            catch
            {
                // Preserve the original failure and still return a non-zero process code.
            }

            throw;
        }
    }

    public async ValueTask DisposeAsync() => await connection.DisposeAsync();

    private async Task EnsureOpenAsync(CancellationToken cancellationToken)
    {
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }
    }

    private async Task<ScopeState> ReadLockedAsync(
        ScopeKey target,
        SqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = LockedReadSql;
        AddTargetParameters(command, target);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var assignmentCount = 0L;
        var isActive = false;
        var allowAllRegions = false;
        var allowAllStores = false;
        if (await reader.ReadAsync(cancellationToken))
        {
            assignmentCount = 1;
            isActive = reader.GetBoolean(0);
            allowAllRegions = reader.GetBoolean(1);
            allowAllStores = reader.GetBoolean(2);
            if (await reader.ReadAsync(cancellationToken))
            {
                assignmentCount = 2;
            }
        }

        await reader.NextResultAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        var regionCount = reader.GetInt64(0);
        await reader.NextResultAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        var storeCount = reader.GetInt64(0);
        return new ScopeState(
            assignmentCount,
            isActive,
            allowAllRegions,
            allowAllStores,
            regionCount,
            storeCount);
    }

    private async Task ExecuteMutationAsync(
        string sql,
        ScopeKey target,
        DateTimeOffset? now,
        SqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        AddTargetParameters(command, target);
        if (now.HasValue)
        {
            command.Parameters.Add(
                new SqlParameter("@now", SqlDbType.DateTimeOffset)
                {
                    Value = now.Value
                });
        }

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void AddTargetParameters(SqlCommand command, ScopeKey target)
    {
        command.Parameters.Add(
            new SqlParameter("@tenantId", SqlDbType.NVarChar, 36)
            {
                Value = target.TenantId.ToString("D")
            });
        command.Parameters.Add(
            new SqlParameter("@userId", SqlDbType.NVarChar, 36)
            {
                Value = target.UserId.ToString("D")
            });
    }
}
