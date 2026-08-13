using Crm.Analytics.Sql.IntegrationTests.Sqlite;

namespace Crm.Analytics.Sql.IntegrationTests.Execution;

/// <summary>
/// Fixture'in veri degismezleri. Diger testlerin beklenen degerleri bunlara
/// dayandigi icin once bunlar dogrulanir.
/// </summary>
/// <remarks>
/// Bu testler ayni zamanda <c>olist_views.contract.sql</c> sonundaki "DE'DEN EK
/// TALEPLER" maddelerinin cevaplarini SABITLER: bir veri seti yenilemesi bu
/// cevaplari degistirirse test kirmizyya doner ve sessiz kalmaz.
/// </remarks>
[Collection(OlistDatabaseCollection.Name)]
public sealed class FixtureInvariantTests(OlistDatabaseFixture fixture)
{
    [OlistDatabaseFact]
    public void Gorunum_satir_sayilari_beklenen_degerlerde()
    {
        // Bozuk paylasilan fixture'da bu degerler 225.300 / 103.886 / (yok) idi.
        Assert.Equal(112_650, fixture.ScalarLong("SELECT COUNT(*) FROM vw_sales"));
        Assert.Equal(103_886, fixture.ScalarLong("SELECT COUNT(*) FROM vw_payment"));
        Assert.Equal(95_539, fixture.ScalarLong("SELECT COUNT(*) FROM vw_customer_rfm"));
    }

    [OlistDatabaseFact]
    public void Orders_tablosunda_ciftlenme_yok()
    {
        // Bu isin baslama nedeni: paylasilan dosyada her siparis satiri iki kez
        // vardi (198.882 satir / 99.441 tekil) ve hicbir sey sikayet etmiyordu.
        var satir = fixture.ScalarLong(
            "SELECT COUNT(*) FROM cleaned_olist_orders_dataset");
        var tekil = fixture.ScalarLong(
            "SELECT COUNT(DISTINCT order_id) FROM cleaned_olist_orders_dataset");

        Assert.Equal(99_441, satir);
        Assert.Equal(satir, tekil);
    }

    [OlistDatabaseFact]
    public void Kapsam_kolonunda_NULL_yok()
    {
        // Sozlesme madde 3'un cevabi. Kapsam filtresi
        // "customer_state IN (...)" biciminde ve NULL satirlari DISARIDA
        // BIRAKIR; NULL olmadigi icin sessiz veri kaybi yok.
        var nullSayisi = fixture.ScalarLong(
            """
            SELECT COUNT(*) FROM cleaned_olist_customers_dataset
            WHERE customer_state IS NULL OR TRIM(customer_state) = ''
            """);

        Assert.Equal(0, nullSayisi);
        Assert.Equal(27, fixture.ScalarLong("SELECT COUNT(DISTINCT customer_state) FROM vw_sales"));
    }

    [OlistDatabaseFact]
    public void Musteri_kimligi_siparis_basina_uretiliyor()
    {
        // Sozlesmede "DOGRULANMADI" olarak isaretlenen cikarimin cevabi:
        // customer_id gercek musteri DEGIL, siparis basina uretiliyor.
        // Gercek kisiyi customer_unique_id temsil eder.
        var customerId = fixture.ScalarLong(
            "SELECT COUNT(*) FROM cleaned_olist_customers_dataset");
        var tekilKisi = fixture.ScalarLong(
            "SELECT COUNT(DISTINCT customer_unique_id) FROM cleaned_olist_customers_dataset");
        var cokSiparisliKisi = fixture.ScalarLong(
            """
            SELECT COUNT(*) FROM (
                SELECT customer_unique_id
                FROM cleaned_olist_customers_dataset
                GROUP BY customer_unique_id
                HAVING COUNT(*) > 1
            )
            """);

        Assert.Equal(99_441, customerId);
        Assert.Equal(96_096, tekilKisi);
        Assert.Equal(2_997, cokSiparisliKisi);

        Assert.True(
            tekilKisi < customerId,
            "customer_unique_id tekrar etmiyorsa RFM'in Frequency metrigi anlamsizlasir.");
    }

    [OlistDatabaseFact]
    public void Kategorisiz_kalemler_acik_etiket_aliyor()
    {
        // Sozlesmenin COALESCE savunmasi LATENT DEGIL, AKTIF:
        // 610 urunun kategorisi bos ve bunlardan 1.603 kalem geliyor.
        // NULL birakilsa bu kalemler kategori kirilimlarinda sessizce kaybolurdu.
        var bosKategoriliUrun = fixture.ScalarLong(
            """
            SELECT COUNT(*) FROM cleaned_olist_products_dataset
            WHERE product_category_name_english IS NULL
               OR TRIM(product_category_name_english) = ''
            """);

        var uncategorizedKalem = fixture.ScalarLong(
            "SELECT COUNT(*) FROM vw_sales WHERE product_category = 'uncategorized'");

        Assert.Equal(610, bosKategoriliUrun);
        Assert.Equal(1_603, uncategorizedKalem);

        // NULL kategori KALMAMALI: COALESCE her satiri etiketlemis olmali.
        Assert.Equal(0, fixture.ScalarLong(
            "SELECT COUNT(*) FROM vw_sales WHERE product_category IS NULL"));
    }

    [OlistDatabaseFact]
    public void Kalemi_olmayan_siparisler_satis_gorunumune_girmiyor()
    {
        // Onemli ve kolay gozden kacan veri gercegi: 775 siparisin hic kalemi
        // yok. Bu yuzden vw_sales 2018-09-03'te biterken ham orders tablosu
        // 2018-10-17'ye kadar gidiyor.
        //
        // BI tarafinda "siparis sayisi" metriginin hangi kaynaktan
        // hesaplandigina gore 775 fark cikar; bu mutabakat farkinin bilinmesi
        // gerekiyor.
        var kalemsizSiparis = fixture.ScalarLong(
            """
            SELECT COUNT(*) FROM cleaned_olist_orders_dataset o
            LEFT JOIN cleaned_olist_order_items_dataset i ON o.order_id = i.order_id
            WHERE i.order_id IS NULL
            """);

        Assert.Equal(775, kalemsizSiparis);

        Assert.Equal(
            "2018-10-17 17:30:18",
            ScalarText("SELECT MAX(order_purchase_timestamp) FROM cleaned_olist_orders_dataset"));

        Assert.Equal(
            "2018-09-03 09:06:57",
            ScalarText("SELECT MAX(order_purchase_timestamp) FROM vw_sales"));
    }

    [OlistDatabaseFact]
    public void Tarih_kolonu_metin_ve_ISO_biciminde()
    {
        // Yuk tasiyan bir degismez: tarih araligi filtreleri sozluksel
        // karsilastirmaya dayaniyor. Kolon Julian sayisina cevrilse harness
        // uretimle sessizce ayrisirdi.
        Assert.Equal("text", ScalarText(
            "SELECT DISTINCT typeof(order_purchase_timestamp) FROM vw_sales"));

        Assert.Equal(0, fixture.ScalarLong(
            """
            SELECT COUNT(*) FROM vw_sales
            WHERE order_purchase_timestamp
                NOT GLOB '[0-9][0-9][0-9][0-9]-[0-9][0-9]-[0-9][0-9] [0-9][0-9]:[0-9][0-9]:[0-9][0-9]'
            """));
    }

    [OlistDatabaseFact]
    public void Sqlite_surumu_gerekli_fonksiyonlari_iceriyor()
    {
        // CONCAT 3.44+, CEILING (ceil) 3.35+ ile geliyor. Bu fonksiyonlar
        // allow-list'te "yerlesik" olarak isaretli; motor eski olsa isaretleme
        // yanlis olurdu.
        var surum = ScalarText("SELECT sqlite_version()")!;
        var parcalar = surum.Split('.');
        var major = int.Parse(parcalar[0], System.Globalization.CultureInfo.InvariantCulture);
        var minor = int.Parse(parcalar[1], System.Globalization.CultureInfo.InvariantCulture);

        Assert.True(
            major > 3 || (major == 3 && minor >= 44),
            $"SQLite {surum} cok eski: CONCAT icin 3.44+ gerekiyor.");
    }

    private string? ScalarText(string sql)
    {
        using var command = fixture.Connection.CreateCommand();
        command.CommandText = sql;

        return command.ExecuteScalar() as string;
    }
}
