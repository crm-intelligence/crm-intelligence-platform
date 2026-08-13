using System.Globalization;
using System.Reflection;
using Crm.Analytics.Sql.DevData;
using Microsoft.Data.Sqlite;

// ===========================================================================
// Yerel SQLite fixture uretici
// ===========================================================================
//
// Neden var: Azure SQL ortami hazir olana kadar guardrail'dan gecen SQL'i
// gercek veriye karsi calistirabilmek icin yerel bir veritabani gerekiyor.
// Paylasilan bir ikili dosyaya guvenmek yerine, repoda versiyonlanan temiz
// CSV'lerden HER SEFERINDE SIFIRDAN uretiyoruz.
//
// Bunun somut gerekcesi var: elle paylasilan crm_dev.db dosyasinda
// cleaned_olist_orders_dataset tablosu iki kez yuklenmisti (198.882 satir /
// 99.441 tekil order_id). Sonuc sessizdi — hicbir hata vermeden vw_sales
// 225.300 satir dondurdu ve tum satis metrikleri tam 2 kat sisti.
// Bu arac ayni hatanin tekrarlanmasini iki mekanizmayla engeller:
//   1) DB her calismada silinip yeniden olusturulur (var olana INSERT yapmaz),
//   2) yukleme sonrasi satir sayilari sozlesmedeki degerlere karsi dogrulanir
//      ve uyusmazlikta arac HATA VERIP CIKAR.
//
// Kullanim:
//   dotnet run --project tools/Crm.Analytics.Sql.DevData
//   dotnet run --project tools/Crm.Analytics.Sql.DevData -- <hedef-db-yolu>
// ===========================================================================

const string SchemaVersion = "1";

try
{
    var repoRoot = RepoRoot.Locate();
    var datasetDirectory = Path.Combine(repoRoot, "data", "fixtures", "olist");
    var databasePath = args.Length > 0
        ? Path.GetFullPath(args[0])
        : Path.Combine(repoRoot, "crm_dev.db");

    Console.WriteLine($"Repo koku      : {repoRoot}");
    Console.WriteLine($"Kaynak CSV'ler : {datasetDirectory}");
    Console.WriteLine($"Hedef veritabani: {databasePath}");
    Console.WriteLine();

    DeleteExistingDatabase(databasePath);

    var connectionString = new SqliteConnectionStringBuilder
    {
        DataSource = databasePath,
        Mode = SqliteOpenMode.ReadWriteCreate
    }.ToString();

    using var connection = new SqliteConnection(connectionString);
    connection.Open();

    // Yukleme suresince dayaniklilik gerekmiyor: dosya bir hata durumunda
    // bastan uretilecek turetilebilir bir cikti.
    Execute(connection, "PRAGMA journal_mode = OFF;");
    Execute(connection, "PRAGMA synchronous = OFF;");

    var loadedCounts = LoadTables(connection, datasetDirectory);
    CreateViews(connection);
    CreateIndexes(connection);
    var viewCounts = WriteBuildReceipt(connection, loadedCounts);

    Execute(connection, "PRAGMA optimize;");

    Report(loadedCounts, viewCounts);
    Console.WriteLine();
    Console.WriteLine("Fixture hazir. Dogrulamalarin tamami gecti.");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine();
    Console.Error.WriteLine($"HATA: {ex.Message}");
    Console.Error.WriteLine();
    Console.Error.WriteLine("Fixture uretilemedi. Yarim kalmis bir veritabani ile");
    Console.Error.WriteLine("devam edilmemesi icin islem durduruldu.");
    return 1;
}

// ---------------------------------------------------------------------------

static void DeleteExistingDatabase(string databasePath)
{
    // -wal ve -shm dosyalari da silinir: eski bir journal yeni veritabanina
    // karisirsa tespiti zor tutarsizliklar dogar.
    foreach (var suffix in new[] { "", "-wal", "-shm" })
    {
        var path = databasePath + suffix;

        if (File.Exists(path))
        {
            File.Delete(path);
            Console.WriteLine($"Silindi: {Path.GetFileName(path)}");
        }
    }
}

static Dictionary<string, int> LoadTables(SqliteConnection connection, string datasetDirectory)
{
    var counts = new Dictionary<string, int>(StringComparer.Ordinal);

    foreach (var spec in OlistTables.All)
    {
        var csvPath = Path.Combine(datasetDirectory, spec.CsvFileName);

        if (!File.Exists(csvPath))
        {
            throw new FileNotFoundException(
                $"Kaynak CSV bulunamadi: {csvPath}", csvPath);
        }

        Execute(connection, spec.CreateTableSql());

        var inserted = BulkInsert(connection, spec, csvPath);

        // Sozlesmedeki satir sayisina karsi dogrulama. Bu kontrol, paylasilan
        // dosyadaki ciftlenmeyi yakalayacak olan kontroldur.
        if (inserted != spec.ExpectedRowCount)
        {
            throw new InvalidDataException(
                $"{spec.TableName}: {spec.ExpectedRowCount:N0} satir bekleniyordu, " +
                $"{inserted:N0} yuklendi. Kaynak CSV degistiyse " +
                "TableSpec.cs icindeki ExpectedRowCount ve " +
                "src/Crm.Analytics.Sql/Catalog/olist_views.contract.sql birlikte " +
                "guncellenmelidir.");
        }

        counts[spec.TableName] = inserted;
        Console.WriteLine($"Yuklendi: {spec.TableName,-40} {inserted,8:N0} satir");
    }

    return counts;
}

static int BulkInsert(SqliteConnection connection, TableSpec spec, string csvPath)
{
    using var transaction = connection.BeginTransaction();
    using var command = connection.CreateCommand();

    command.Transaction = transaction;
    command.CommandText = spec.InsertSql();

    // Parametreler bir kez olusturulur, her satirda yalnizca degerleri degisir.
    var parameters = new SqliteParameter[spec.Columns.Count];

    for (var i = 0; i < spec.Columns.Count; i++)
    {
        parameters[i] = command.Parameters.Add($"@p{i}", SqliteTypeOf(spec.Columns[i]));
    }

    var rowCount = 0;

    foreach (var fields in CsvReader.ReadRows(csvPath, spec))
    {
        for (var i = 0; i < parameters.Length; i++)
        {
            parameters[i].Value = Convert(fields[i], spec.Columns[i], spec, rowCount + 2);
        }

        command.ExecuteNonQuery();
        rowCount++;
    }

    transaction.Commit();
    return rowCount;
}

static SqliteType SqliteTypeOf(ColumnSpec column) => column.SqliteType switch
{
    "TEXT" => SqliteType.Text,
    "INTEGER" => SqliteType.Integer,
    "REAL" => SqliteType.Real,
    // default dali bilincli olarak hata atar: yeni bir tip eklendiginde
    // sessizce yanlis baglamak, sorgu sonucunu fark edilmeden degistirir.
    _ => throw new NotSupportedException($"Bilinmeyen SQLite tipi: {column.SqliteType}")
};

static object Convert(string raw, ColumnSpec column, TableSpec spec, int lineNumber)
{
    if (raw.Length == 0)
    {
        return DBNull.Value;
    }

    // CSV sayilari daima InvariantCulture'dir (nokta ondalik ayirici).
    // CurrentCulture kullanilmasi Turkce yerel ayarda "158.8" degerini 1588
    // olarak okurdu — sessiz ve buyuk bir tutar hatasi.
    switch (column.SqliteType)
    {
        case "TEXT":
            return raw;

        case "INTEGER":
            if (!long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i))
            {
                throw new InvalidDataException(
                    $"{spec.CsvFileName}:{lineNumber} — '{column.Name}' tam sayi " +
                    $"olmali, '{raw}' bulundu.");
            }

            return i;

        case "REAL":
            if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
            {
                throw new InvalidDataException(
                    $"{spec.CsvFileName}:{lineNumber} — '{column.Name}' ondalik sayi " +
                    $"olmali, '{raw}' bulundu.");
            }

            return d;

        default:
            throw new NotSupportedException($"Bilinmeyen SQLite tipi: {column.SqliteType}");
    }
}

static void CreateViews(SqliteConnection connection)
{
    var assembly = Assembly.GetExecutingAssembly();
    var resourceName = assembly.GetManifestResourceNames()
        .Single(n => n.EndsWith("sqlite_views.sql", StringComparison.Ordinal));

    using var stream = assembly.GetManifestResourceStream(resourceName)
        ?? throw new InvalidOperationException("sqlite_views.sql gomulu kaynagi okunamadi.");
    using var reader = new StreamReader(stream);

    // GO bir SQL komutu degil, batch ayiricidir — SQLite tanimaz, atiyoruz.
    // Dosyada bulunma sebebi: sozlesme dosyasiyla ayni bicimde T-SQL olarak
    // ayristirilabilmesi (sapma testi iki dosyayi ayni ayristiriciyla okuyor).
    var batches = new List<string>();
    var current = new List<string>();

    foreach (var line in reader.ReadToEnd().Split('\n'))
    {
        if (line.Trim().Equals("GO", StringComparison.OrdinalIgnoreCase))
        {
            AddBatch(batches, current);
            current = [];
        }
        else
        {
            current.Add(line);
        }
    }

    AddBatch(batches, current);

    foreach (var batch in batches)
    {
        Execute(connection, batch);
    }

    Console.WriteLine();
    Console.WriteLine($"Gorunumler olusturuldu ({batches.Count}): vw_sales, vw_customer_rfm, vw_payment");
}

/// <summary>
/// Yalnizca yorum ve bosluktan olusan batch'leri atlayarak listeye ekler.
/// </summary>
static void AddBatch(List<string> batches, List<string> lines)
{
    var hasStatement = lines.Any(line =>
    {
        var trimmed = line.Trim();
        return trimmed.Length > 0 && !trimmed.StartsWith("--", StringComparison.Ordinal);
    });

    if (hasStatement)
    {
        batches.Add(string.Join('\n', lines));
    }
}

static void CreateIndexes(SqliteConnection connection)
{
    // Gorunumler join-agir; ham dosyada hic index yoktu. Index'ler sonucu
    // degistirmez, yalnizca test suresini kisaltir.
    string[] indexes =
    [
        "CREATE INDEX ix_orders_order_id     ON cleaned_olist_orders_dataset(order_id);",
        "CREATE INDEX ix_orders_customer_id  ON cleaned_olist_orders_dataset(customer_id);",
        "CREATE INDEX ix_items_order_id      ON cleaned_olist_order_items_dataset(order_id);",
        "CREATE INDEX ix_items_product_id    ON cleaned_olist_order_items_dataset(product_id);",
        "CREATE INDEX ix_payments_order_id   ON cleaned_olist_order_payments_dataset(order_id);",
        "CREATE UNIQUE INDEX ux_customers_id ON cleaned_olist_customers_dataset(customer_id);",
        "CREATE UNIQUE INDEX ux_products_id  ON cleaned_olist_products_dataset(product_id);"
    ];

    foreach (var sql in indexes)
    {
        Execute(connection, sql);
    }

    Console.WriteLine($"Index olusturuldu: {indexes.Length} adet");
}

static Dictionary<string, int> WriteBuildReceipt(
    SqliteConnection connection,
    Dictionary<string, int> tableCounts)
{
    // Gorunum satir sayilari — bu degerler paylasilan bozuk dosyada
    // 225.300 / 103.886 / (yok) idi. Beklenen degerler temiz CSV'lerden
    // hesaplandi ve testler bunlara guvenir.
    var expectedViewCounts = new Dictionary<string, int>(StringComparer.Ordinal)
    {
        ["vw_sales"] = 112_650,
        ["vw_payment"] = 103_886,
        ["vw_customer_rfm"] = 95_539
    };

    var actualViewCounts = new Dictionary<string, int>(StringComparer.Ordinal);

    foreach (var (view, expected) in expectedViewCounts)
    {
        var actual = ScalarInt(connection, $"SELECT COUNT(*) FROM {view};");
        actualViewCounts[view] = actual;

        if (actual != expected)
        {
            throw new InvalidDataException(
                $"{view}: {expected:N0} satir bekleniyordu, {actual:N0} bulundu. " +
                "Gorunum tanimi veya kaynak veri degismis olabilir.");
        }
    }

    // Veri kalitesi degismezleri: bunlar sozlesmenin DE'ye sordugu acik
    // sorularin cevabidir ve testler tarafindan da dogrulanir.
    var nullStates = ScalarInt(connection,
        """
        SELECT COUNT(*) FROM cleaned_olist_customers_dataset
        WHERE customer_state IS NULL OR TRIM(customer_state) = '';
        """);

    if (nullStates != 0)
    {
        throw new InvalidDataException(
            $"customer_state icinde {nullStates:N0} NULL/bos deger var. Kapsam " +
            "filtresi 'customer_state IN (...)' bu satirlari sessizce disarida " +
            "birakir; sozlesmenin 3. maddesi bu durumu acikca yasaklıyor.");
    }

    // schema_version: testler bu degeri koddaki sabite karsi dogrular.
    // Eslesmezse test ATLANMAZ, BASARISIZ OLUR — eski/bozuk bir fixture ile
    // yesil test almanin onlenmesi icin.
    Execute(connection,
        """
        CREATE TABLE _harness_build_info (
            key   TEXT PRIMARY KEY,
            value TEXT NOT NULL
        );
        """);

    using var command = connection.CreateCommand();
    command.CommandText =
        """
        INSERT INTO _harness_build_info (key, value) VALUES
            ('schema_version', @version),
            ('view_contract',  'src/Crm.Analytics.Sql/Catalog/olist_views.contract.sql'),
            ('built_by',       'tools/Crm.Analytics.Sql.DevData');
        """;
    command.Parameters.AddWithValue("@version", SchemaVersion);
    command.ExecuteNonQuery();

    // Zaman damgasi BILINCLI olarak yazilmiyor: ayni CSV'lerden uretilen iki
    // fixture'in mantiksal olarak ayni olmasi isteniyor.

    Console.WriteLine($"Denetim izi yazildi: schema_version = {SchemaVersion}");
    return actualViewCounts;
}

static void Report(Dictionary<string, int> tableCounts, Dictionary<string, int> viewCounts)
{
    Console.WriteLine();
    Console.WriteLine("Ozet");
    Console.WriteLine("----");

    foreach (var (name, count) in tableCounts)
    {
        Console.WriteLine($"  {name,-40} {count,8:N0}");
    }

    foreach (var (name, count) in viewCounts)
    {
        Console.WriteLine($"  {name,-40} {count,8:N0}  (gorunum)");
    }
}

static void Execute(SqliteConnection connection, string sql)
{
    using var command = connection.CreateCommand();
    command.CommandText = sql;
    command.ExecuteNonQuery();
}

static int ScalarInt(SqliteConnection connection, string sql)
{
    using var command = connection.CreateCommand();
    command.CommandText = sql;

    return System.Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
}
