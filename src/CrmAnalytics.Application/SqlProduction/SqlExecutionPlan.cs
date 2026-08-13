namespace CrmAnalytics.Application.SqlProduction;

public sealed class SqlExecutionPlan
{
    public const int MaximumCommandTimeoutSeconds = 3600;

    public SqlExecutionPlan(
        SqlDataSource source,
        string sql,
        IReadOnlyCollection<SqlExecutionParameter> parameters,
        string appliedScopeFilter,
        int commandTimeoutSeconds,
        SqlResultShapeMetadata? resultShape,
        string? verifiedPhysicalObject = null,
        int? rowLimit = null)
    {
        if (!Enum.IsDefined(source) || source == SqlDataSource.Unknown)
        {
            throw new ArgumentOutOfRangeException(nameof(source));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(sql);
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentException.ThrowIfNullOrWhiteSpace(appliedScopeFilter);

        if (commandTimeoutSeconds is <= 0
            or > MaximumCommandTimeoutSeconds)
        {
            throw new ArgumentOutOfRangeException(
                nameof(commandTimeoutSeconds),
                commandTimeoutSeconds,
                $"The command timeout must be between 1 and "
                    + $"{MaximumCommandTimeoutSeconds} seconds.");
        }

        if (rowLimit is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(rowLimit));
        }

        Source = source;
        Sql = sql;
        Parameters = Array.AsReadOnly(parameters.ToArray());
        AppliedScopeFilter = appliedScopeFilter;
        CommandTimeoutSeconds = commandTimeoutSeconds;
        ResultShape = resultShape;
        VerifiedPhysicalObject = string.IsNullOrWhiteSpace(verifiedPhysicalObject)
            ? null
            : verifiedPhysicalObject.Trim();
        RowLimit = rowLimit;
    }

    public SqlDataSource Source { get; }

    public string Sql { get; }

    public IReadOnlyCollection<SqlExecutionParameter> Parameters { get; }

    public string AppliedScopeFilter { get; }

    public int CommandTimeoutSeconds { get; }

    public SqlResultShapeMetadata? ResultShape { get; }

    public string? VerifiedPhysicalObject { get; }

    public int? RowLimit { get; }

    public override string ToString() =>
        "SqlExecutionPlan { Sensitive data redacted }";
}
