using Crm.Analytics.Sql.Contracts;
using Crm.Analytics.Sql.IntegrationTests.Sqlite;
using Crm.Analytics.Sql.Routing;

namespace Crm.Analytics.Sql.IntegrationTests.Execution;

/// <summary>
/// Gorsel onerisinin gercek sonuc kumesiyle tutarli oldugunu kanitlar.
/// </summary>
/// <remarks>
/// <para>
/// <c>ResultShapeClassifier</c> sonuc setine degil <b>talebe</b> bakar; oneri sorgu
/// kosmadan uretilir. Bu bilincli bir tasarim (BI rapor sayfasini onceden hazirlayabilsin
/// diye) ama beraberinde bir risk getirir: oneri, donen veriyle ilgisiz olabilir ve bunu
/// hicbir birim testi yakalamaz.
/// </para>
/// <para>
/// Birim testleri (<c>Routing/ResultShapeClassifierTests</c>) karar tablosunun kendisini
/// dogruluyor. Burada dogrulanan farkli bir sey: <c>MetricCount + DimensionCount</c>
/// gercekten donen kolon sayisiyla ayni mi, ve KPI karti onerildiginde sonuc gercekten
/// tek satir mi. Yani oneri bir <b>iddia</b> olarak ele alinip veriye karsi sinaniyor.
/// </para>
/// </remarks>
[Collection(OlistDatabaseCollection.Name)]
public sealed class ResultShapeContractTests(OlistDatabaseFixture fixture)
{
    /// <summary>
    /// Karar tablosunun dort kolu: kirilimsiz, tek kategorik, tek zaman, iki kirilim.
    /// </summary>
    public static TheoryData<string, string, VisualType> GorselOnerileri() => new()
    {
        { "kirilim yok", "2018 sipariş sayısı", VisualType.KpiCard },
        { "tek kategorik kirilim", "2018 satış tutarını eyalete göre göster", VisualType.BarChart },
        { "tek zaman kirilimi", "2018 satış tutarını tarihe göre göster", VisualType.LineChart },
        { "iki kirilim", "2018 satış tutarını eyalete ve kategoriye göre göster", VisualType.Matrix }
    };

    [OlistDatabaseTheory]
    [MemberData(nameof(GorselOnerileri))]
    public void Onerilen_gorsel_donen_sonuc_kumesiyle_tutarli(
        string senaryo,
        string prompt,
        VisualType beklenenGorsel)
    {
        var response = GuardrailHarness.Produce(prompt, UserDataScope.ForRegions("SP", "RJ"));

        Assert.Equal(GuardrailDecision.Accepted, response.Decision);

        // Accepted bir yanitta oneri alani BOS BIRAKILAMAZ: BI tarafi bu alani
        // okuyacak, null gelirse gorsel secimi sessizce varsayilana duserdi.
        var shape = response.ResultShape;
        Assert.NotNull(shape);

        Assert.Equal(beklenenGorsel, shape.SuggestedVisual);

        // Gerekce bos gelemez: "neden bar degil cizgi" tartismasini kesen tek alan bu.
        Assert.False(string.IsNullOrWhiteSpace(shape.Rationale), senaryo);

        var rows = fixture.QueryRunner.Execute(response);
        Assert.NotEmpty(rows);

        // Asil iddia: sonuc kumesi olcum + kirilim sayisi kadar kolon dondurur.
        // Sinif sonuc setini hic gormedigi icin bu bagin kopmasi ancak burada
        // farkedilir.
        Assert.Equal(
            shape.MetricCount + shape.DimensionCount,
            rows[0].Count);

        // Kirilim yoksa toplam tek satirdir; KPI karti onerisi bunu varsayar.
        if (shape.SuggestedVisual == VisualType.KpiCard)
        {
            Assert.Single(rows);
        }
    }

    [OlistDatabaseFact]
    public void Zaman_kirilimi_gercekten_tarih_kolonu_dondurur()
    {
        // HasTimeDimension katalogdan okunuyor; donen kolonun gercekten tarih
        // olup olmadigina bakilmiyor. Katalog ile gorunum ayrisirsa cizgi grafik
        // onerisi tarih olmayan bir kolona verilir.
        var response = GuardrailHarness.Produce(
            "2018 satış tutarını tarihe göre göster",
            UserDataScope.ForRegions("SP", "RJ"));

        Assert.Equal(GuardrailDecision.Accepted, response.Decision);
        Assert.True(response.ResultShape!.HasTimeDimension);

        var rows = fixture.QueryRunner.Execute(response);

        Assert.All(rows, row =>
        {
            var deger = row["order_purchase_timestamp"];
            Assert.NotNull(deger);

            // Fixture tarihleri metin olarak tutuyor (SQLite'ta tarih tipi yok);
            // onemli olan degerin tarih olarak COZULEBILIR olmasi.
            Assert.True(
                DateTime.TryParse(
                    Convert.ToString(deger, System.Globalization.CultureInfo.InvariantCulture),
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None,
                    out _),
                $"Zaman kirilimi tarih olarak cozulemedi: {deger}");
        });
    }

    [OlistDatabaseFact]
    public void Reddedilen_talepte_gorsel_onerisi_uretilmez()
    {
        // Reddedilen bir talebe gorsel onerisi eslemek, BI tarafinda "rapor
        // hazirlanabilir" izlenimi yaratirdi. Alan null kalmali.
        var response = GuardrailHarness.Produce(
            "2018 satış tutarını eyalete göre göster",
            UserDataScope.Unresolved);

        Assert.NotEqual(GuardrailDecision.Accepted, response.Decision);
        Assert.Null(response.ResultShape);
    }
}
