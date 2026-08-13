using Crm.Analytics.Sql.Contracts;
using Crm.Analytics.Sql.IntegrationTests.Sqlite;

namespace Crm.Analytics.Sql.IntegrationTests.Execution;

/// <summary>
/// Satir limitinin gercekten uygulandigini ve metrik degerlerinin dogru
/// oldugunu kanitlar.
/// </summary>
/// <remarks>
/// Beklenen sayilar yerel fixture uzerinde olculdu. Para karsilastirmalari
/// TOLERANSLI: <c>REAL</c> toplaminin son bitleri sorgu planina gore
/// degisebilir, tam esitlik gereksiz kirilganlik uretir.
/// </remarks>
[Collection(OlistDatabaseCollection.Name)]
public sealed class RowLimitAndMetricTests(OlistDatabaseFixture fixture)
{
    private const double KurusToleransi = 0.01;

    [OlistDatabaseFact]
    public void Satir_limiti_gercekten_kesiyor()
    {
        // Taban: SP'de 47.449 satir var, yani 5000'lik limit gercek bir kesme
        // yapiyor. Bu taban olmadan "limit calisti" ile "veri zaten azdi"
        // ayirt edilemez.
        var spSatirSayisi = fixture.ScalarLong(
            "SELECT COUNT(*) FROM vw_sales WHERE customer_state = 'SP'");

        Assert.Equal(47_449, spSatirSayisi);

        // customer_city kullaniliyor, order_id DEGIL: order_id identityColumns
        // icinde oldugu icin MinCellSizeCheck GR012 ile reddeder ve sorgu
        // veritabanina hic ulasmaz.
        var result = GuardrailHarness.RunGuardrail(
            "SELECT customer_city FROM mart.vw_sales",
            UserDataScope.ForRegions("SP"));

        Assert.Equal(GuardrailDecision.Accepted, result.Decision);

        var rows = fixture.QueryRunner.Execute(result);

        Assert.Equal(OlistCatalog.AllowList.MaxRows, rows.Count);
        Assert.Equal(5000, rows.Count);
    }

    [OlistDatabaseFact]
    public void Kullanicinin_yuksek_limiti_dusurulur()
    {
        var result = GuardrailHarness.RunGuardrail(
            "SELECT TOP 100000 customer_city FROM mart.vw_sales",
            UserDataScope.ForRegions("SP"));

        Assert.Equal(GuardrailDecision.Accepted, result.Decision);
        Assert.Equal(5000, fixture.QueryRunner.Execute(result).Count);
    }

    [OlistDatabaseFact]
    public void Kullanicinin_dusuk_limiti_korunur()
    {
        // Limit yalnizca DUSURUCU olabilir; kullanici daha azini istiyorsa
        // istegi korunur.
        var result = GuardrailHarness.RunGuardrail(
            "SELECT TOP 10 customer_city FROM mart.vw_sales",
            UserDataScope.ForRegions("SP"));

        Assert.Equal(GuardrailDecision.Accepted, result.Decision);
        Assert.Equal(10, fixture.QueryRunner.Execute(result).Count);
    }

    [OlistDatabaseFact]
    public void Satis_tutari_elle_hesaplanan_degerle_esleşir()
    {
        // Bu test, paylasilan bozuk fixture'daki 2x sismisligin geri gelmesini
        // yakalayan REGRESYON BEKCISI. Bozuk dosyada vw_sales 225.300 satir
        // donuyor ve her tutar tam iki katina cikiyordu.
        var result = GuardrailHarness.RunGuardrail(
            """
            SELECT customer_state, SUM(price) AS toplam
            FROM mart.vw_sales
            WHERE order_purchase_timestamp >= '2018-01-01'
              AND order_purchase_timestamp <= '2018-12-31'
            GROUP BY customer_state
            """,
            UserDataScope.ForRegions("SP"));

        Assert.Equal(GuardrailDecision.Accepted, result.Decision);

        var rows = fixture.QueryRunner.Execute(result);
        var toplam = Convert.ToDouble(rows.Single()["toplam"], System.Globalization.CultureInfo.InvariantCulture);

        Assert.Equal(2_975_757.23, toplam, KurusToleransi);
    }

    [OlistDatabaseFact]
    public void Siparis_sayisi_ve_kalem_sayisi_ayni_sey_degil()
    {
        // Metric Catalog'un approvalNote'unda uyardigi karisiklik:
        // order_count (DISTINCT order_id) ile item_count (COUNT(*)) farklidir.
        // Gercek veriyle nicelendiriyoruz.
        const string Filtre =
            "WHERE customer_state = 'SP' " +
            "AND order_purchase_timestamp >= '2018-01-01' " +
            "AND order_purchase_timestamp <= '2018-12-31'";

        var siparisSayisi = fixture.ScalarLong(
            $"SELECT COUNT(DISTINCT order_id) FROM vw_sales {Filtre}");
        var kalemSayisi = fixture.ScalarLong(
            $"SELECT COUNT(*) FROM vw_sales {Filtre}");

        Assert.Equal(23_741, siparisSayisi);
        Assert.Equal(27_279, kalemSayisi);
        Assert.True(
            kalemSayisi > siparisSayisi,
            "Kalem sayisi siparis sayisindan buyuk olmali; aksi halde bir siparisin " +
            "birden fazla kalemi olabilecegi varsayimi bozulmus demektir.");
    }

    [OlistDatabaseFact]
    public void Yil_kirilimi_udf_ile_dogru_hesaplaniyor()
    {
        // YEAR() bir UDF olarak kayitli; bu test hem degerin dogrulugunu hem de
        // GROUP BY icinde paylasilan ifade dugumunun tutarli kaldigini kanitlar.
        var result = GuardrailHarness.RunGuardrail(
            """
            SELECT YEAR(order_purchase_timestamp) AS yil, SUM(price) AS toplam
            FROM mart.vw_sales
            GROUP BY YEAR(order_purchase_timestamp)
            """,
            UserDataScope.ForRegions("SP"));

        Assert.Equal(GuardrailDecision.Accepted, result.Decision);

        var rows = fixture.QueryRunner.Execute(result);

        // Veri 2016-09 ile 2018-10 arasinda: uc yil beklenir, dordunculuk yok.
        var yillar = rows
            .Select(r => Convert.ToInt32(r["yil"], System.Globalization.CultureInfo.InvariantCulture))
            .OrderBy(y => y)
            .ToList();

        Assert.Equal([2016, 2017, 2018], yillar);

        // Yillarin toplami, SP'nin genel toplamina esit olmali (kayip grup yok).
        var yilToplamlari = rows.Sum(r =>
            Convert.ToDouble(r["toplam"], System.Globalization.CultureInfo.InvariantCulture));
        var genelToplam = fixture.ScalarDouble(
            "SELECT SUM(price) FROM vw_sales WHERE customer_state = 'SP'");

        Assert.Equal(genelToplam, yilToplamlari, KurusToleransi);
    }

    [OlistDatabaseFact]
    public void Ceyrek_kirilimi_datepart_ile_dogru_hesaplaniyor()
    {
        // DATEPART(quarter, ...) iki asamayi birlikte sinar: ilk argumanin
        // metin sabitine cevrilmesi (yoksa "no such column: quarter") ve UDF'in
        // dogru ceyregi dondurmesi.
        var result = GuardrailHarness.RunGuardrail(
            """
            SELECT DATEPART(quarter, order_purchase_timestamp) AS ceyrek, SUM(price) AS toplam
            FROM mart.vw_sales
            WHERE order_purchase_timestamp >= '2018-01-01'
              AND order_purchase_timestamp <= '2018-12-31'
            GROUP BY DATEPART(quarter, order_purchase_timestamp)
            """,
            UserDataScope.ForRegions("SP"));

        Assert.Equal(GuardrailDecision.Accepted, result.Decision);

        var rows = fixture.QueryRunner.Execute(result);

        var ceyrekler = rows
            .Select(r => Convert.ToInt32(r["ceyrek"], System.Globalization.CultureInfo.InvariantCulture))
            .OrderBy(q => q)
            .ToList();

        // Q4 YOK ve bu dogru: vw_sales 2018-09-03'te bitiyor. Ham orders
        // tablosu 2018-10-17'ye kadar gidiyor ama 775 siparisin hic kalemi
        // olmadigi icin son ~6 hafta satis gorunumune girmiyor.
        // Bu satir ayni zamanda "uydurma grup uretilmiyor" kanitidir.
        Assert.Equal([1, 2, 3], ceyrekler);

        // Ceyrek bazinda dogrulanmis degerler.
        var ceyrekBazinda = rows.ToDictionary(
            r => Convert.ToInt32(r["ceyrek"], System.Globalization.CultureInfo.InvariantCulture),
            r => Convert.ToDouble(r["toplam"], System.Globalization.CultureInfo.InvariantCulture));

        Assert.Equal(1_073_432.26, ceyrekBazinda[1], KurusToleransi);
        Assert.Equal(1_179_247.29, ceyrekBazinda[2], KurusToleransi);
        Assert.Equal(723_077.68, ceyrekBazinda[3], KurusToleransi);

        // Ceyreklerin toplami 2018 SP toplamina esit: kayip veya uydurma grup yok.
        var ceyrekToplami = rows.Sum(r =>
            Convert.ToDouble(r["toplam"], System.Globalization.CultureInfo.InvariantCulture));

        Assert.Equal(2_975_757.23, ceyrekToplami, KurusToleransi);
    }
}
