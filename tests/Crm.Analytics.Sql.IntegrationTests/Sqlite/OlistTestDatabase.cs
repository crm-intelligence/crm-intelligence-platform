using Microsoft.Data.Sqlite;

namespace Crm.Analytics.Sql.IntegrationTests.Sqlite;

/// <summary>
/// Yerel SQLite fixture'ini bulur ve kullanilabilirligini siniflandirir.
/// </summary>
/// <remarks>
/// <para><b>Asimetri bilincli ve kritik:</b></para>
/// <list type="bullet">
///   <item>
///     Fixture <b>yoksa</b> -> testler ATLANIR. NLU tarafinda calisan bir
///     gelistiricinin 60 MB'lik bir dosyayi uretmeye zorlanmasi gereksiz.
///   </item>
///   <item>
///     Fixture <b>varsa ama yanlissa</b> -> testler BASARISIZ olur, atlanmaz.
///     Eski veya bozuk bir fixture ile yesil test almak, bu isi gerekli kilan
///     hatanin tam kendisidir: paylasilan crm_dev.db dosyasinda orders tablosu
///     iki kez yuklenmisti ve hicbir sey sikayet etmiyordu.
///   </item>
/// </list>
/// </remarks>
internal static class OlistTestDatabase
{
    /// <summary>
    /// Fixture uretecinin yazdigi surum. <c>Crm.Analytics.Sql.DevData</c>
    /// icindeki deger ile ayni olmak zorunda; gorunumler degistiginde ikisi
    /// birlikte artirilir.
    /// </summary>
    private const string ExpectedSchemaVersion = "1";

    private const string PathEnvironmentVariable = "CRM_OLIST_SQLITE_DB";

    /// <summary>
    /// Ayarlandiginda, fixture eksikse testler atlanmaz — BASARISIZ olur.
    /// CI icin zorunlu: aksi halde bozuk bir kurulum tum entegrasyon setini
    /// sessizce "atlandi"ya cevirir ve yesil CI hicbir sey ifade etmez.
    /// </summary>
    private const string RequireEnvironmentVariable = "CRM_REQUIRE_SQLITE_DB";

    private static readonly Lazy<Resolution> resolution = new(Resolve);

    /// <summary>
    /// Testin atlanma nedeni; <c>null</c> ise fixture kullanilabilir.
    /// </summary>
    public static string? SkipReason => resolution.Value.SkipReason;

    public static string DatabasePath => resolution.Value.Path;

    /// <summary>
    /// Salt okunur baglanti acar ve T-SQL uyumluluk fonksiyonlarini kaydeder.
    /// </summary>
    /// <remarks>
    /// <c>Mode=ReadOnly</c> bilincli: bir testin paylasilan fixture'i
    /// degistirmesi yapisal olarak imkansiz hale gelir ve <c>-wal</c>/<c>-shm</c>
    /// dosyalari olusmaz.
    /// </remarks>
    public static SqliteConnection OpenReadOnly()
    {
        var current = resolution.Value;

        if (current.SkipReason is not null)
        {
            throw new InvalidOperationException(
                $"Fixture kullanilamaz: {current.SkipReason}");
        }

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = current.Path,
            Mode = SqliteOpenMode.ReadOnly
        }.ToString();

        var connection = new SqliteConnection(connectionString);
        connection.Open();

        TSqlCompatibilityFunctions.Register(connection);
        VerifySchemaVersion(connection);

        return connection;
    }

    // -----------------------------------------------------------------------

    private static Resolution Resolve()
    {
        var path = Environment.GetEnvironmentVariable(PathEnvironmentVariable);

        if (string.IsNullOrWhiteSpace(path))
        {
            path = Path.Combine(LocateRepoRoot(), "crm_dev.db");
        }

        path = Path.GetFullPath(path);

        if (File.Exists(path))
        {
            return new Resolution(path, SkipReason: null);
        }

        var required = Environment.GetEnvironmentVariable(RequireEnvironmentVariable) == "1";

        if (required)
        {
            // Atlamak yerine acikca patlat: cagiran taraf fixture'in
            // bulunmasini SART kosmus.
            throw new FileNotFoundException(
                $"{RequireEnvironmentVariable}=1 ayarli ama fixture bulunamadi: {path}. " +
                "Uretmek icin: dotnet run --project tools/Crm.Analytics.Sql.DevData",
                path);
        }

        return new Resolution(
            path,
            $"Yerel SQLite fixture'i yok ({Path.GetFileName(path)}). " +
            "Uretmek icin: dotnet run --project tools/Crm.Analytics.Sql.DevData");
    }

    private static void VerifySchemaVersion(SqliteConnection connection)
    {
        string? version = null;

        try
        {
            using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT value FROM _harness_build_info WHERE key = 'schema_version';";
            version = command.ExecuteScalar() as string;
        }
        catch (SqliteException)
        {
            // Tablo yok: dosya bu araçla uretilmemis.
        }

        if (version is null)
        {
            throw new InvalidOperationException(
                $"Fixture'da _harness_build_info tablosu yok: {DatabasePath}. " +
                "Bu dosya Crm.Analytics.Sql.DevData ile uretilmemis (ornegin elle " +
                "paylasilmis bir kopya). Elle paylasilan kopyalarda dogrulanmamis " +
                "veri hatalari bulundu, bu yuzden kabul edilmiyor. " +
                "Uretmek icin: dotnet run --project tools/Crm.Analytics.Sql.DevData");
        }

        if (version != ExpectedSchemaVersion)
        {
            throw new InvalidOperationException(
                $"Fixture surumu uyusmuyor: beklenen '{ExpectedSchemaVersion}', " +
                $"bulunan '{version}'. Gorunumler degismis olabilir. " +
                "Yeniden uretmek icin: dotnet run --project tools/Crm.Analytics.Sql.DevData");
        }
    }

    private static string LocateRepoRoot()
    {
        foreach (var start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            var directory = new DirectoryInfo(start);

            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "CrmAnalytics.slnx")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }
        }

        throw new DirectoryNotFoundException(
            "Repo koku bulunamadi (CrmAnalytics.slnx aranmisti). " +
            $"Fixture yolunu {PathEnvironmentVariable} ile verebilirsiniz.");
    }

    private sealed record Resolution(string Path, string? SkipReason);
}
