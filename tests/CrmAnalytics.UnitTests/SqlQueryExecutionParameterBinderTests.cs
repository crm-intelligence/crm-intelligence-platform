using System.Data;
using CrmAnalytics.Application.SqlProduction;
using CrmAnalytics.Infrastructure.QueryExecution;
using Microsoft.Data.SqlClient;

namespace CrmAnalytics.UnitTests;

public sealed class SqlQueryExecutionParameterBinderTests
{
    private readonly SqlQueryExecutionParameterBinder _binder = new();

    [Theory]
    [InlineData(true, SqlDbType.NVarChar)]
    [InlineData(false, SqlDbType.VarChar)]
    public void Text_UsesExplicitCharacterType(
        bool unicode,
        SqlDbType expected)
    {
        var command = CreateCommand();
        _binder.Bind(command,
        [
            new SqlExecutionParameter(
                "@text", SqlExecutionParameterKind.Text,
                "value", unicode, Size: 42)
        ], SqlDataSource.Dwh);

        var parameter = Assert.IsType<SqlParameter>(command.Parameters[0]);
        Assert.Equal(expected, parameter.SqlDbType);
        Assert.Equal(42, parameter.Size);
        Assert.Equal("SELECT @text", command.CommandText);
    }

    [Fact]
    public void ScalarKinds_UseExplicitSqlTypesAndFacets()
    {
        var command = CreateCommand();
        _binder.Bind(command,
        [
            new("@integer", SqlExecutionParameterKind.Integer, 42, false),
            new("@long", SqlExecutionParameterKind.Integer, 42L, false),
            new("@decimal", SqlExecutionParameterKind.Decimal, 12.34m,
                false, Precision: 12, Scale: 2),
            new("@flag", SqlExecutionParameterKind.Boolean, true, false),
            new("@date", SqlExecutionParameterKind.Date,
                new DateOnly(2026, 7, 31), false)
        ], SqlDataSource.Dwh);

        Assert.Equal(SqlDbType.Int, command.Parameters[0].SqlDbType);
        Assert.Equal(SqlDbType.BigInt, command.Parameters[1].SqlDbType);
        Assert.Equal(SqlDbType.Decimal, command.Parameters[2].SqlDbType);
        Assert.Equal((byte)12, command.Parameters[2].Precision);
        Assert.Equal((byte)2, command.Parameters[2].Scale);
        Assert.Equal(SqlDbType.Bit, command.Parameters[3].SqlDbType);
        Assert.Equal(SqlDbType.Date, command.Parameters[4].SqlDbType);
    }

    [Fact]
    public void Null_UsesDbNull()
    {
        var command = CreateCommand();
        _binder.Bind(command,
        [
            new("@value", SqlExecutionParameterKind.Text,
                null, true, Size: 10)
        ], SqlDataSource.Dwh);

        Assert.Same(DBNull.Value, command.Parameters[0].Value);
    }

    [Theory]
    [InlineData("value")]
    [InlineData("@")]
    [InlineData("@1invalid")]
    [InlineData("@invalid-name")]
    public void InvalidName_IsRejectedWithoutLeakingValue(string name)
    {
        var command = CreateCommand();
        var exception = Assert.Throws<QueryExecutionPermanentException>(
            () => _binder.Bind(command,
            [
                new(name, SqlExecutionParameterKind.Text,
                    "secret-value", true)
            ], SqlDataSource.Dwh));

        Assert.DoesNotContain("secret-value", exception.ToString());
    }

    [Fact]
    public void DuplicateName_IsRejectedCaseInsensitively()
    {
        var command = CreateCommand();

        Assert.Throws<QueryExecutionPermanentException>(
            () => _binder.Bind(command,
            [
                new("@region", SqlExecutionParameterKind.Text,
                    "TR", true),
                new("@REGION", SqlExecutionParameterKind.Text,
                    "US", true)
            ], SqlDataSource.Dwh));
    }

    [Fact]
    public void UnknownKind_IsRejectedAndSqlIsNotModified()
    {
        var command = CreateCommand();
        var definition = new SqlExecutionParameter(
            "@value",
            (SqlExecutionParameterKind)999,
            "secret-value",
            true);

        var exception = Assert.Throws<QueryExecutionPermanentException>(
            () => _binder.Bind(
                command, [definition], SqlDataSource.Dwh));

        Assert.Equal("SELECT @text", command.CommandText);
        Assert.DoesNotContain("secret-value", exception.ToString());
    }

    private static SqlCommand CreateCommand() => new()
    {
        CommandText = "SELECT @text"
    };
}
