namespace Crm.Analytics.Sql.IntegrationTests.Sqlite;

/// <summary>
/// Yerel SQLite fixture'i yoksa testi ATLAYAN <c>[Fact]</c>.
/// </summary>
/// <remarks>
/// <para>
/// Neden <c>Assert.Skip</c> kullanilmiyor: <c>xunit 2.9.3</c>'te
/// <c>xunit.assert</c> icinde ilgili tip bulunsa da CALISTIRMA MOTORU dinamik
/// atlamayi desteklemiyor — cagri BASARISIZ test olarak raporlanir. Bu yuzden
/// atlama karari kesif aninda, <c>Skip</c> alani doldurularak verilir.
/// </para>
/// <para>
/// Dikkat: bu yalnizca fixture'in YOKLUGUNU atlar. Fixture varsa ama yanlissa
/// <see cref="OlistTestDatabase"/> istisna atar ve test basarisiz olur.
/// </para>
/// </remarks>
public sealed class OlistDatabaseFactAttribute : FactAttribute
{
    public OlistDatabaseFactAttribute()
    {
        if (OlistTestDatabase.SkipReason is { } reason)
        {
            Skip = reason;
        }
    }
}

/// <summary>
/// <see cref="OlistDatabaseFactAttribute"/>'un <c>[Theory]</c> karsiligi.
/// </summary>
public sealed class OlistDatabaseTheoryAttribute : TheoryAttribute
{
    public OlistDatabaseTheoryAttribute()
    {
        if (OlistTestDatabase.SkipReason is { } reason)
        {
            Skip = reason;
        }
    }
}
