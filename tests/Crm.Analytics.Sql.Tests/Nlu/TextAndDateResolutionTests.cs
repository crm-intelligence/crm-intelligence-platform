using Crm.Analytics.Sql.Contracts;
using Crm.Analytics.Sql.Nlu;

namespace Crm.Analytics.Sql.Tests.Nlu;

public class TurkishTextNormalizerTests
{
    [Theory]
    [InlineData("Satış Tutarı", "satis tutari")]
    [InlineData("EYALETE GÖRE", "eyalete gore")]
    [InlineData("Müşteri Ödemesi", "musteri odemesi")]
    [InlineData("çeyreklik", "ceyreklik")]
    [InlineData("Ağustos", "agustos")]
    public void Aksanli_metin_katalog_formuna_indirilir(string input, string expected)
    {
        Assert.Equal(expected, TurkishTextNormalizer.Normalize(input));
    }

    [Fact]
    public void Buyuk_I_harfi_birlesik_nokta_uretmez()
    {
        // ToLowerInvariant("İ") "i" + U+0307 uretir ve "i" ile esit degildir. Karakter
        // haritasi kucultmeden once uygulanmazsa "İSTANBUL" sessizce eslesmezdi.
        var normalized = TurkishTextNormalizer.Normalize("İSTANBUL");

        Assert.Equal("istanbul", normalized);
        Assert.Equal(8, normalized.Length);
    }

    [Fact]
    public void Noktalama_kelimenin_basinda_ve_sonunda_kirpilir()
    {
        var tokens = TurkishTextNormalizer.Tokenize("(satış), göre:");

        Assert.Equal(["satis", "gore"], tokens.Select(token => token.Normalized));
    }

    [Fact]
    public void Tarih_isaretleri_kelime_icinde_korunur()
    {
        // Tire atilirsa "2018-01-15" uc ayri sayiya bolunur ve tarih olarak taninamazdi.
        var tokens = TurkishTextNormalizer.Tokenize("2018-01-15 tarihinde");

        Assert.Equal("2018-01-15", tokens[0].Normalized);
    }

    [Fact]
    public void Ham_hal_korunur_cozumlenemeyen_terim_kullaniciya_boyle_gosterilir()
    {
        var tokens = TurkishTextNormalizer.Tokenize("Kâr Marjı");

        Assert.Equal("Kâr", tokens[0].Raw);
        Assert.Equal("kar", tokens[0].Normalized);
    }
}

public class RelativeDateResolverTests
{
    // Referans gun sabit: cozumleme sistem saatinden okunursa test yarin kirilirdi.
    private static readonly DateOnly Today = new(2026, 7, 30);

    [Fact]
    public void Bu_yil_yil_basindan_bugune()
    {
        var match = Resolve("bu yıl satış");

        Assert.NotNull(match);
        Assert.Equal(DateRangeKind.Relative, match.Range.Kind);
        Assert.Equal("this_year", match.Range.RelativeExpression);
        Assert.Equal(new DateOnly(2026, 1, 1), match.Range.From);
        Assert.Equal(Today, match.Range.To);
    }

    [Fact]
    public void Gecen_yil_onceki_yilin_tamami()
    {
        var match = Resolve("geçen yıl");

        Assert.NotNull(match);
        Assert.Equal(new DateOnly(2025, 1, 1), match.Range.From);
        Assert.Equal(new DateOnly(2025, 12, 31), match.Range.To);
    }

    [Fact]
    public void Gecen_ceyrek_onceki_tam_ceyrek()
    {
        // 2026-07-30 ucuncu ceyrekte; onceki tam ceyrek Nisan-Haziran.
        var match = Resolve("geçen çeyrek");

        Assert.NotNull(match);
        Assert.Equal("last_quarter", match.Range.RelativeExpression);
        Assert.Equal(new DateOnly(2026, 4, 1), match.Range.From);
        Assert.Equal(new DateOnly(2026, 6, 30), match.Range.To);
    }

    [Fact]
    public void Son_ceyrek_de_gecen_ceyrek_gibi_cozulur()
    {
        Assert.Equal("last_quarter", Resolve("son çeyrek")!.Range.RelativeExpression);
    }

    [Fact]
    public void Bu_ceyrek_ceyrek_basindan_bugune()
    {
        var match = Resolve("bu çeyrek");

        Assert.NotNull(match);
        Assert.Equal(new DateOnly(2026, 7, 1), match.Range.From);
        Assert.Equal(Today, match.Range.To);
    }

    [Fact]
    public void Gecen_ay_onceki_ayin_tamami()
    {
        var match = Resolve("geçen ay");

        Assert.NotNull(match);
        Assert.Equal(new DateOnly(2026, 6, 1), match.Range.From);
        Assert.Equal(new DateOnly(2026, 6, 30), match.Range.To);
    }

    [Fact]
    public void Son_30_gun_bugun_dahil_30_gun()
    {
        var match = Resolve("son 30 gün");

        Assert.NotNull(match);
        Assert.Equal("last_30_days", match.Range.RelativeExpression);
        Assert.Equal(new DateOnly(2026, 7, 1), match.Range.From);
        Assert.Equal(Today, match.Range.To);
        Assert.Equal(30, match.Range.To!.Value.DayNumber - match.Range.From!.Value.DayNumber + 1);
    }

    [Theory]
    [InlineData("bugun", "today", 2026, 7, 30, 2026, 7, 30)]
    [InlineData("dun", "yesterday", 2026, 7, 29, 2026, 7, 29)]
    [InlineData("bu hafta", "last_7_days", 2026, 7, 24, 2026, 7, 30)]
    [InlineData("gecen hafta", "previous_7_days", 2026, 7, 17, 2026, 7, 23)]
    [InlineData("bu ay", "this_month", 2026, 7, 1, 2026, 7, 30)]
    [InlineData("son 3 hafta", "last_21_days", 2026, 7, 10, 2026, 7, 30)]
    [InlineData("son 3 ay", "last_3_months", 2026, 5, 1, 2026, 7, 30)]
    public void Generic_relative_date_contract_is_authoritative(
        string prompt,
        string expression,
        int fromYear,
        int fromMonth,
        int fromDay,
        int toYear,
        int toMonth,
        int toDay)
    {
        var match = Resolve(prompt);

        Assert.NotNull(match);
        Assert.Equal(expression, match.Range.RelativeExpression);
        Assert.Equal(new DateOnly(fromYear, fromMonth, fromDay), match.Range.From);
        Assert.Equal(new DateOnly(toYear, toMonth, toDay), match.Range.To);
    }

    [Theory]
    [InlineData("bu sene", "this_year", 2026, 1, 1, 2026, 7, 30)]
    [InlineData("gecen sene", "last_year", 2025, 1, 1, 2025, 12, 31)]
    [InlineData("son 2 yil", "last_2_years", 2024, 7, 31, 2026, 7, 30)]
    [InlineData("son iki sene", "last_2_years", 2024, 7, 31, 2026, 7, 30)]
    [InlineData("son otuz gun", "last_30_days", 2026, 7, 1, 2026, 7, 30)]
    public void Generic_yil_ve_sene_varyasyonlari_cozulur(
        string prompt,
        string expression,
        int fromYear,
        int fromMonth,
        int fromDay,
        int toYear,
        int toMonth,
        int toDay)
    {
        var match = Resolve(prompt);

        Assert.NotNull(match);
        Assert.Equal(expression, match.Range.RelativeExpression);
        Assert.Equal(new DateOnly(fromYear, fromMonth, fromDay), match.Range.From);
        Assert.Equal(new DateOnly(toYear, toMonth, toDay), match.Range.To);
    }

    [Fact]
    public void Iki_iso_tarih_mutlak_aralik_verir()
    {
        var match = Resolve("2018-01-01 ile 2018-03-31 arası satış");

        Assert.NotNull(match);
        Assert.Equal(DateRangeKind.Absolute, match.Range.Kind);
        Assert.Null(match.Range.RelativeExpression);
        Assert.Equal(new DateOnly(2018, 1, 1), match.Range.From);
        Assert.Equal(new DateOnly(2018, 3, 31), match.Range.To);
    }

    [Theory]
    [InlineData("2018/01/01 - 2018/03/31")]
    [InlineData("01.01.2018 ile 31.03.2018")]
    public void Supported_absolute_range_contract_accepts_reviewed_formats(
        string prompt)
    {
        var match = Resolve(prompt);

        Assert.NotNull(match);
        Assert.Equal(DateRangeKind.Absolute, match.Range.Kind);
        Assert.Equal(new DateOnly(2018, 1, 1), match.Range.From);
        Assert.Equal(new DateOnly(2018, 3, 31), match.Range.To);
    }

    [Fact]
    public void Ters_yazilmis_tarihler_duzeltilir()
    {
        // Duzeltilmezse From > To olur ve sorgu her zaman bos sonuc donerdi.
        var match = Resolve("2018-03-31 ve 2018-01-01");

        Assert.Equal(new DateOnly(2018, 1, 1), match!.Range.From);
        Assert.Equal(new DateOnly(2018, 3, 31), match.Range.To);
    }

    [Fact]
    public void Ay_adi_ve_yil_o_ayin_tamami()
    {
        var match = Resolve("Ocak 2018 satışı");

        Assert.NotNull(match);
        Assert.Equal(new DateOnly(2018, 1, 1), match.Range.From);
        Assert.Equal(new DateOnly(2018, 1, 31), match.Range.To);
    }

    [Fact]
    public void Subat_artik_yil_dogru_hesaplanir()
    {
        var match = Resolve("Şubat 2016");

        Assert.Equal(new DateOnly(2016, 2, 29), match!.Range.To);
    }

    [Fact]
    public void Yalniz_yil_o_yilin_tamami()
    {
        var match = Resolve("2017 satış tutarı");

        Assert.NotNull(match);
        Assert.Equal(new DateOnly(2017, 1, 1), match.Range.From);
        Assert.Equal(new DateOnly(2017, 12, 31), match.Range.To);
    }

    [Fact]
    public void Ay_yil_kalibi_yalniz_yil_kalibindan_once_denenir()
    {
        // "ocak 2018" ifadesi yalniz-yil kalibina da uyar. Sira yanlis olsa ayin tamami
        // yerine yilin tamami donerdi ve kullanici sessizce 12 kat fazla veri gorurdu.
        var match = Resolve("ocak 2018");

        Assert.Equal(new DateOnly(2018, 1, 31), match!.Range.To);
    }

    [Theory]
    [InlineData("2018 mayıd ayında", 5)]
    [InlineData("2018 mayiz ayında", 5)]
    [InlineData("2018 ağustso ayında", 8)]
    [InlineData("2018 eylül ayında", 9)]
    [InlineData("2018 EYLÜL ayında", 9)]
    [InlineData("2018 şubat ayında", 2)]
    [InlineData("2018 subat ayında", 2)]
    public void Ay_adlari_unicode_casing_ve_tek_typo_toleransiyla_cozulur(
        string prompt,
        int expectedMonth)
    {
        var match = Resolve(prompt);

        Assert.NotNull(match);
        Assert.Equal(new DateOnly(2018, expectedMonth, 1), match.Range.From);
        Assert.Equal(expectedMonth, match.Range.To!.Value.Month);
    }

    [Fact]
    public void Tarih_ifadesi_yoksa_null_doner()
    {
        // Varsayilan aralik uydurulmaz: tarihsiz talep CL002 ile netlestirmeye gider.
        Assert.Null(Resolve("satış tutarı eyalete göre"));
    }

    [Fact]
    public void Gorece_ifadede_de_mutlak_tarihler_yazilir()
    {
        // Gorece ifade SQL'e gomulmez; parametreye baglanacak kesin tarihler burada hesaplanir.
        var match = Resolve("bu ay");

        Assert.NotNull(match);
        Assert.NotNull(match.Range.From);
        Assert.NotNull(match.Range.To);
        Assert.NotNull(match.Range.RelativeExpression);
    }

    [Fact]
    public void Ifadenin_kapladigi_kelime_araligi_bildirilir()
    {
        // Parser bu araligi "tuketildi" olarak isaretler; yoksa tarih kelimeleri
        // cozumlenemeyen terim sayilir ve guven skoru yanlis dusuk cikardi.
        var match = Resolve("bu yıl satış");

        Assert.Equal(0, match!.TokenStart);
        Assert.Equal(2, match.TokenCount);
    }

    private static DateRangeMatch? Resolve(string prompt) =>
        RelativeDateResolver.Resolve(TurkishTextNormalizer.Tokenize(prompt), Today);
}
