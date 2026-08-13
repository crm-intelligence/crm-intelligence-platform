using Crm.Analytics.Sql.Parsing;
using Microsoft.Data.Sqlite;

namespace Crm.Analytics.Sql.IntegrationTests.Sqlite;

/// <summary>
/// Test koleksiyonu boyunca tek bir salt okunur baglanti paylasan fixture.
/// </summary>
public sealed class OlistDatabaseFixture : IDisposable
{
    private readonly Lazy<SqliteConnection> connection;

    public OlistDatabaseFixture()
    {
        // Lazy: fixture eksikse ve testler atlanacaksa baglanti hic acilmasin.
        connection = new Lazy<SqliteConnection>(OlistTestDatabase.OpenReadOnly);
        Runner = new Lazy<GuardrailQueryRunner>(
            () => new GuardrailQueryRunner(Connection, new TSqlParserFactory()));
    }

    public SqliteConnection Connection => connection.Value;

    private Lazy<GuardrailQueryRunner> Runner { get; }

    /// <summary>
    /// Guardrail cikitisini gercek veride calistiran kosucu. Sinif xunit'in
    /// sart kostugu icin <c>public</c>, uye ise <c>internal</c>: kosucu tipi
    /// harness'in ic detayi.
    /// </summary>
    internal GuardrailQueryRunner QueryRunner => Runner.Value;

    /// <summary>
    /// Guardrail'dan GECMEYEN, dogrudan referans sorgusu. Yalnizca testlerin
    /// beklenen degerlerini dogrulamak icin — karsilastirma tabani uretir.
    /// </summary>
    public long ScalarLong(string sql)
    {
        using var command = Connection.CreateCommand();
        command.CommandText = sql;

        return Convert.ToInt64(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
    }

    public double ScalarDouble(string sql)
    {
        using var command = Connection.CreateCommand();
        command.CommandText = sql;

        return Convert.ToDouble(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
    }

    public void Dispose()
    {
        if (connection.IsValueCreated)
        {
            connection.Value.Dispose();
        }
    }
}

/// <summary>
/// Fixture'i paylasan koleksiyon tanimi.
/// </summary>
[CollectionDefinition(Name)]
public sealed class OlistDatabaseCollection : ICollectionFixture<OlistDatabaseFixture>
{
    public const string Name = "olist-sqlite";
}
