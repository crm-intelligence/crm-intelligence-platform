using Crm.Analytics.Sql.IntegrationTests.Sqlite;
using Crm.Analytics.Sql.Parsing;

namespace Crm.Analytics.Sql.IntegrationTests.Translation;

/// <summary>
/// Cevirici altin testleri. Veritabani gerektirmez: ceviri saf bir
/// metin -> metin donusumu.
/// </summary>
public sealed class SqliteDialectTranslatorTests
{
    private static string Translate(string tsql) =>
        new SqliteDialectTranslator(new TSqlParserFactory()).Translate(tsql);

    private static SqliteTranslationNotSupportedException Refuse(string tsql) =>
        Assert.Throws<SqliteTranslationNotSupportedException>(() => Translate(tsql));

    [Fact]
    public void Production_fiziksel_view_yerel_logical_fixture_viewina_eslenir()
    {
        var sql = Translate(
            "SELECT TOP (10) customer_state FROM mart.vw_sales");

        Assert.Contains("FROM vw_sales", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("mart.vw_sales", sql, StringComparison.Ordinal);
    }

    // -- TOP -> LIMIT -------------------------------------------------------

    [Fact]
    public void Top_limit_e_cevrilir()
    {
        var sql = Translate("SELECT TOP (5000) customer_city FROM vw_sales");

        Assert.Contains("LIMIT 5000", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("TOP", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Limit_order_by_dan_sonra_gelir()
    {
        // SQLite LIMIT'in ORDER BY'dan SONRA gelmesini sart kosar.
        var sql = Translate(
            "SELECT TOP (10) customer_state, SUM(price) AS t FROM vw_sales " +
            "GROUP BY customer_state ORDER BY SUM(price) DESC");

        var orderByIndex = sql.IndexOf("ORDER BY", StringComparison.OrdinalIgnoreCase);
        var limitIndex = sql.IndexOf("LIMIT", StringComparison.OrdinalIgnoreCase);

        Assert.True(orderByIndex >= 0, "ORDER BY kayboldu.");
        Assert.True(limitIndex > orderByIndex, "LIMIT, ORDER BY'dan once geliyor.");
    }

    [Fact]
    public void Top_yoksa_limit_eklenmez()
    {
        var sql = Translate("SELECT customer_city FROM vw_sales");

        Assert.DoesNotContain("LIMIT", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Yuzde_bazli_top_reddedilir()
    {
        // Guardrail'in RowLimitInjector'i bunu normalize etmeliydi; normalize
        // edilmemis bir deger guardrail'in degistigini gosterir ve harness bunu
        // ortmemelidir.
        var ex = Refuse("SELECT TOP 50 PERCENT customer_city FROM vw_sales");

        Assert.Contains("PERCENT", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void With_ties_reddedilir()
    {
        var ex = Refuse(
            "SELECT TOP (10) WITH TIES customer_city FROM vw_sales ORDER BY customer_city");

        Assert.Contains("WITH TIES", ex.Message, StringComparison.Ordinal);
    }

    // -- Fonksiyon yeniden adlandirma ---------------------------------------

    [Fact]
    public void Isnull_coalesce_e_cevrilir()
    {
        // ISNULL SQLite'ta tokenizer anahtar sozcugudur; UDF ile karsilanamaz.
        var sql = Translate("SELECT ISNULL(product_category, 'yok') AS k FROM vw_sales");

        Assert.Contains("COALESCE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ISNULL", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Count_big_count_a_cevrilir()
    {
        var sql = Translate("SELECT COUNT_BIG(*) AS n FROM vw_sales");

        Assert.Contains("COUNT(*)", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("COUNT_BIG", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("YEAR")]
    [InlineData("MONTH")]
    [InlineData("DAY")]
    [InlineData("LEN")]
    public void Udf_ile_karsilanan_fonksiyonlar_oldugu_gibi_kalir(string function)
    {
        // Bu fonksiyonlar SQL'de degistirilmez; baglanti uzerinde UDF olarak
        // kaydedilir. Calistirilan metnin guardrail cikitisina birebir yakin
        // kalmasi bilincli.
        var sql = Translate($"SELECT {function}(order_purchase_timestamp) AS v FROM vw_sales");

        Assert.Contains(function, sql, StringComparison.OrdinalIgnoreCase);
    }

    // -- DATEPART argumani --------------------------------------------------

    [Fact]
    public void Datepart_argumani_metin_sabitine_cevrilir()
    {
        // ScriptDom 'quarter' ifadesini KOLON REFERANSI olarak ayristirir;
        // SQLite oldugu gibi alirsa "no such column: quarter" hatasi verir.
        var sql = Translate(
            "SELECT DATEPART(quarter, order_purchase_timestamp) AS c FROM vw_sales");

        Assert.Contains("'quarter'", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Datepart_grup_ve_select_tutarli_kalir()
    {
        // Kritik: DeterministicQueryBuilder SELECT ve GROUP BY'da AYNI ifade
        // ornegini paylasir. Yerinde mutasyon kullanildigi icin iki taraf birlikte
        // guncellenmeli; ayrisirsa SQLite kabul eder ve sessizce yanlis gruplar.
        var sql = Translate(
            "SELECT DATEPART(quarter, order_purchase_timestamp) AS c, SUM(price) AS t " +
            "FROM vw_sales GROUP BY DATEPART(quarter, order_purchase_timestamp)");

        var occurrences = sql.Split("'quarter'").Length - 1;

        Assert.Equal(2, occurrences);
    }

    [Theory]
    [InlineData("week")]
    [InlineData("weekday")]
    public void Deterministik_olmayan_tarih_parcalari_reddedilir(string part)
    {
        // T-SQL sonucu DATEFIRST oturum ayarina baglidir; "dogru" bir cevirisi yok.
        var ex = Refuse(
            $"SELECT DATEPART({part}, order_purchase_timestamp) AS c FROM vw_sales");

        Assert.Contains("DATEFIRST", ex.Message, StringComparison.Ordinal);
    }

    // -- Reddedilen yapilar -------------------------------------------------

    [Fact]
    public void Allow_list_disi_fonksiyon_reddedilir()
    {
        var ex = Refuse("SELECT GETDATE() AS n FROM vw_sales");

        Assert.Contains("GETDATE", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Bilesik_sorgu_reddedilir()
    {
        // Bilincli olarak ertelenmis GERCEK bosluk: guardrail limiti her kola
        // ekler, SQLite bilesik kolda LIMIT'e izin vermez.
        var ex = Refuse(
            "SELECT customer_city FROM vw_sales UNION SELECT customer_city FROM vw_payment");

        Assert.Contains("Bilesik sorgu", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Sema_onekli_ad_reddedilir()
    {
        var ex = Refuse("SELECT customer_city FROM dbo.vw_sales");

        Assert.Contains("Cok parcali", ex.Message, StringComparison.Ordinal);
    }

    // -- Belirlenimcilik ----------------------------------------------------

    [Fact]
    public void Ayni_girdi_ayni_cikti_uretir()
    {
        // Gizli sayac veya durum yok: harness'in kendisi belirlenimci olmali,
        // aksi halde altin testler guvenilmez olur.
        const string Sql = "SELECT TOP (5000) DATEPART(quarter, order_purchase_timestamp) AS c, " +
                           "ISNULL(product_category, 'yok') AS k FROM vw_sales";

        Assert.Equal(Translate(Sql), Translate(Sql));
    }

    [Fact]
    public void Parametre_adlari_korunur()
    {
        // Microsoft.Data.Sqlite @ parametrelerini destekler; degistirmeye gerek yok.
        // Degistirilirse guardrail'in urettigi SqlParameterSpec adlariyla eslesmez.
        var sql = Translate(
            "SELECT customer_city FROM vw_sales WHERE customer_state IN (@scope0, @scope1)");

        Assert.Contains("@scope0", sql, StringComparison.Ordinal);
        Assert.Contains("@scope1", sql, StringComparison.Ordinal);
    }
}
