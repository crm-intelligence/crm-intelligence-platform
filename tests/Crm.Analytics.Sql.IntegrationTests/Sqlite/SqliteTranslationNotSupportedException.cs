namespace Crm.Analytics.Sql.IntegrationTests.Sqlite;

/// <summary>
/// Guardrail'in urettigi T-SQL, SQLite'a guvenle cevrilemedigi zaman atilir.
/// </summary>
/// <remarks>
/// <para>
/// Bu istisnanin varlik sebebi: cevirici tanimadigi bir yapiyi OLDUGU GIBI
/// gecirmemeli. Gecirirse SQLite ya hata verir (iyi durum) ya da farkli
/// anlamda calisir (kotu durum) — ikinci halde test yesil kalir ama sayi
/// yanlistir. Ustelik boyle bir cevirici, guardrail'in gercek bir hatasini
/// "SQLite uyumsuzlugu" gibi maskeleyebilir.
/// </para>
/// <para>
/// Bu yuzden harness'ta hicbir yerde bu istisna yakalanip "orijinal SQL'e geri
/// don" yapilmaz. Musamahakar bir cevirici, cevirici olmamasindan daha kotudur.
/// </para>
/// </remarks>
internal sealed class SqliteTranslationNotSupportedException : Exception
{
    public SqliteTranslationNotSupportedException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Mesaji standart bicimde kurar: neyin cevrilemedigi, NEDEN, ve kural
    /// eklenecekse hangi dosyanin degistirilecegi.
    /// </summary>
    public static SqliteTranslationNotSupportedException For(
        string construct,
        string reason,
        string whereToFix = "Sqlite/SqliteDialectTranslator.cs")
    {
        return new SqliteTranslationNotSupportedException(
            $"{construct} SQLite'a cevrilemedi: {reason} " +
            $"Kural eklenecekse: {whereToFix}");
    }
}
