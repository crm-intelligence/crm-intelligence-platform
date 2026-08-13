using System.Data;
using CrmAnalytics.Application.SqlProduction;
using Microsoft.Data.SqlClient;

namespace CrmAnalytics.Infrastructure.QueryExecution;

public sealed class SqlQueryExecutionParameterBinder
    : IQueryExecutionParameterBinder
{
    public void Bind(
        SqlCommand command,
        IReadOnlyCollection<SqlExecutionParameter> parameters,
        SqlDataSource source)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(parameters);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var definition in parameters)
        {
            ArgumentNullException.ThrowIfNull(definition);
            if (!IsValidName(definition.Name) || !names.Add(definition.Name))
            {
                throw new QueryExecutionPermanentException(
                    QueryExecutionErrorCodes.Failed,
                    source,
                    TimeSpan.Zero);
            }

            command.Parameters.Add(CreateParameter(definition, source));
        }
    }

    private static SqlParameter CreateParameter(
        SqlExecutionParameter definition,
        SqlDataSource source)
    {
        var parameter = new SqlParameter { ParameterName = definition.Name };
        switch (definition.Kind)
        {
            case SqlExecutionParameterKind.Text:
                parameter.SqlDbType = definition.IsUnicode
                    ? SqlDbType.NVarChar
                    : SqlDbType.VarChar;
                parameter.Size = GetTextSize(definition, source);
                parameter.Value = definition.Value ?? DBNull.Value;
                break;
            case SqlExecutionParameterKind.Integer:
                parameter.SqlDbType = definition.Value is long
                    ? SqlDbType.BigInt
                    : SqlDbType.Int;
                parameter.Value = definition.Value switch
                {
                    null => DBNull.Value,
                    byte value => (int)value,
                    short value => (int)value,
                    int value => value,
                    long value => value,
                    _ => throw UnsupportedParameter(source)
                };
                break;
            case SqlExecutionParameterKind.Decimal:
                parameter.SqlDbType = SqlDbType.Decimal;
                parameter.Precision = definition.Precision ?? 38;
                parameter.Scale = definition.Scale ?? 18;
                parameter.Value = definition.Value switch
                {
                    null => DBNull.Value,
                    decimal value => value,
                    _ => throw UnsupportedParameter(source)
                };
                break;
            case SqlExecutionParameterKind.Boolean:
                parameter.SqlDbType = SqlDbType.Bit;
                parameter.Value = definition.Value switch
                {
                    null => DBNull.Value,
                    bool value => value,
                    _ => throw UnsupportedParameter(source)
                };
                break;
            case SqlExecutionParameterKind.Date:
                parameter.SqlDbType = SqlDbType.Date;
                parameter.Value = definition.Value switch
                {
                    null => DBNull.Value,
                    DateOnly value => value.ToDateTime(TimeOnly.MinValue),
                    DateTime value => value.Date,
                    _ => throw UnsupportedParameter(source)
                };
                break;
            default:
                throw UnsupportedParameter(source);
        }

        return parameter;
    }

    private static int GetTextSize(
        SqlExecutionParameter definition,
        SqlDataSource source)
    {
        if (definition.Value is not null and not string)
        {
            throw UnsupportedParameter(source);
        }

        var size = definition.Size
            ?? Math.Max(1, (definition.Value as string)?.Length ?? 1);
        if (size is <= 0 or > 100000)
        {
            throw UnsupportedParameter(source);
        }

        return size;
    }

    private static bool IsValidName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)
            || name.Length is < 2 or > 128
            || name[0] != '@'
            || !(char.IsAsciiLetter(name[1]) || name[1] == '_'))
        {
            return false;
        }

        return name.Skip(2).All(character =>
            char.IsAsciiLetterOrDigit(character) || character == '_');
    }

    private static QueryExecutionPermanentException UnsupportedParameter(
        SqlDataSource source) =>
        new(
            QueryExecutionErrorCodes.Failed,
            source,
            TimeSpan.Zero,
            new NotSupportedException(
                "The query parameter type is not supported."));
}
