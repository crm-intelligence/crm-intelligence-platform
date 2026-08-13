namespace Crm.Analytics.Sql.DevData;

/// <summary>
/// Bir kaynak CSV ile hedef SQLite tablosu arasindaki sozlesme.
/// </summary>
/// <param name="TableName">Hedef tablo adi.</param>
/// <param name="CsvFileName">
/// <c>data/fixtures/olist/</c> altindaki dosya adi.
/// </param>
/// <param name="Columns">
/// Kolon adi ve SQLite tipi, CSV'deki sira ile AYNI olmak zorunda.
/// Yukleme sirasinda CSV basligi bu listeye karsi dogrulanir.
/// </param>
/// <param name="ExpectedRowCount">
/// Sozlesmede (<c>src/Crm.Analytics.Sql/Catalog/olist_views.contract.sql</c>, satir 24-35)
/// yazili beklenen satir sayisi. Yukleme sonrasi bu degere karsi dogrulanir;
/// uyusmazlikta arac hata verip cikar.
/// </param>
internal sealed record TableSpec(
    string TableName,
    string CsvFileName,
    IReadOnlyList<ColumnSpec> Columns,
    int ExpectedRowCount)
{
    /// <summary>
    /// <c>CREATE TABLE</c> ifadesini uretir.
    /// </summary>
    public string CreateTableSql()
    {
        var columnList = string.Join(
            ",\n    ",
            Columns.Select(c => $"{c.Name} {c.SqliteType}"));

        return $"CREATE TABLE {TableName} (\n    {columnList}\n);";
    }

    /// <summary>
    /// Parametreli <c>INSERT</c> ifadesini uretir. Parametre adlari kolon
    /// adlariyla ayni sirada <c>@p0..@pN</c> olarak uretilir.
    /// </summary>
    public string InsertSql()
    {
        var columnList = string.Join(", ", Columns.Select(c => c.Name));
        var parameterList = string.Join(", ", Columns.Select((_, i) => $"@p{i}"));

        return $"INSERT INTO {TableName} ({columnList}) VALUES ({parameterList});";
    }
}

/// <summary>Kolon adi ve SQLite tipi.</summary>
internal sealed record ColumnSpec(string Name, string SqliteType);

/// <summary>
/// Bes kaynak tablonun kanonik tanimi. Kolon adlari, siralari ve beklenen satir
/// sayilari <c>olist_views.contract.sql</c> ile birebir ayni tutulmalidir.
/// </summary>
internal static class OlistTables
{
    public static IReadOnlyList<TableSpec> All { get; } =
    [
        new TableSpec(
            "cleaned_olist_customers_dataset",
            "cleaned_olist_customers_dataset.csv",
            [
                new ColumnSpec("customer_id", "TEXT"),
                new ColumnSpec("customer_unique_id", "TEXT"),
                new ColumnSpec("customer_state", "TEXT"),
                new ColumnSpec("customer_city", "TEXT")
            ],
            ExpectedRowCount: 99_441),

        new TableSpec(
            "cleaned_olist_orders_dataset",
            "cleaned_olist_orders_dataset.csv",
            [
                new ColumnSpec("order_id", "TEXT"),
                new ColumnSpec("customer_id", "TEXT"),
                new ColumnSpec("order_status", "TEXT"),
                // ISO 8601 metin (YYYY-MM-DD HH:MM:SS). Metin olarak saklanmasi
                // bilincli: bu bicimde sozluksel karsilastirma kronolojik
                // karsilastirma ile ayni sonucu verir, tarih araligi filtreleri
                // ek donusum gerektirmez.
                new ColumnSpec("order_purchase_timestamp", "TEXT")
            ],
            ExpectedRowCount: 99_441),

        new TableSpec(
            "cleaned_olist_order_items_dataset",
            "cleaned_olist_order_items_dataset.csv",
            [
                new ColumnSpec("order_id", "TEXT"),
                new ColumnSpec("order_item_id", "INTEGER"),
                new ColumnSpec("product_id", "TEXT"),
                new ColumnSpec("price", "REAL"),
                new ColumnSpec("freight_value", "REAL")
            ],
            ExpectedRowCount: 112_650),

        new TableSpec(
            "cleaned_olist_order_payments_dataset",
            "cleaned_olist_order_payments_dataset.csv",
            [
                new ColumnSpec("order_id", "TEXT"),
                new ColumnSpec("payment_type", "TEXT"),
                new ColumnSpec("payment_installments", "INTEGER"),
                new ColumnSpec("payment_value", "REAL")
            ],
            ExpectedRowCount: 103_886),

        new TableSpec(
            "cleaned_olist_products_dataset",
            "cleaned_olist_products_dataset.csv",
            [
                new ColumnSpec("product_id", "TEXT"),
                new ColumnSpec("product_category_name_english", "TEXT")
            ],
            ExpectedRowCount: 32_951)
    ];
}
