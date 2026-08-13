using Crm.Analytics.Sql.Contracts;
using Crm.Analytics.Sql.Parsing;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Crm.Analytics.Sql.Tests.Contracts;

/// <summary>
/// Sozlesme seviyesindeki guvenlik invariantlari. Bunlar pipeline'dan bagimsiz olarak
/// gecerlidir: bir kodlama hatasi bile gecersiz bir "kabul" nesnesi uretemez.
/// </summary>
public class SecurityInvariantTests
{
    private static readonly IReadOnlyList<CheckResult> AllPassed =
        [CheckResult.Pass(GuardrailCheckName.SelectOnly)];

    [Fact]
    public void Kapsam_filtresi_olmadan_kabul_sonucu_uretilemez()
    {
        Assert.Throws<ArgumentException>(() => GuardrailResult.Accepted(
            sql: "SELECT region FROM vw_sales",
            parameters: [],
            appliedScopeFilter: "   ",
            source: DataSource.Dwh,
            queryTimeoutSeconds: 30,
            parserVersion: "Sql150",
            checks: AllPassed));
    }

    [Fact]
    public void Bos_SQL_ile_kabul_sonucu_uretilemez()
    {
        Assert.Throws<ArgumentException>(() => GuardrailResult.Accepted(
            sql: "",
            parameters: [],
            appliedScopeFilter: "region IN (@p0)",
            source: DataSource.Dwh,
            queryTimeoutSeconds: 30,
            parserVersion: "Sql150",
            checks: AllPassed));
    }

    [Fact]
    public void Basarisiz_veya_atlanmis_kontrol_varken_kabul_sonucu_uretilemez()
    {
        // Atlanan kontrolun "gecti" sayilmasi, guvenlik kanitini yaniltici hale getirir.
        IReadOnlyList<CheckResult> withSkipped =
        [
            CheckResult.Pass(GuardrailCheckName.SelectOnly),
            CheckResult.Skip(GuardrailCheckName.ScopeFilterInjection)
        ];

        Assert.Throws<InvalidOperationException>(() => GuardrailResult.Accepted(
            sql: "SELECT region FROM vw_sales",
            parameters: [],
            appliedScopeFilter: "region IN (@p0)",
            source: DataSource.Dwh,
            queryTimeoutSeconds: 30,
            parserVersion: "Sql150",
            checks: withSkipped));
    }

    [Fact]
    public void Gerekce_kodu_olmadan_ret_uretilemez()
    {
        Assert.Throws<ArgumentException>(() =>
            GuardrailResult.Rejected(ReasonCode.None, AllPassed));
    }

    [Fact]
    public void Netlestirme_yalnizca_CL_kodlariyla_uretilebilir()
    {
        Assert.Throws<ArgumentException>(() =>
            GuardrailResult.NeedsClarification(ReasonCode.GR003, AllPassed));
    }

    [Fact]
    public void Her_ret_kodunun_kullanici_mesaji_tanimli()
    {
        // Mesaji olmayan bir kod, kullaniciya bos veya teknik metin gitmesi demektir.
        var codes = Enum.GetValues<ReasonCode>().Where(code => code != ReasonCode.None);

        foreach (var code in codes)
        {
            Assert.Contains(code, ReasonCodeMessages.DefinedCodes);
        }
    }

    [Fact]
    public void Kullanici_mesajlari_ic_detay_sizdirmaz()
    {
        // Ret mesajlari sema kesfi icin oracle'a donusmemeli: tablo/kolon adi, SQL parcasi
        // veya teknik terim icermemeli.
        string[] forbiddenFragments = ["vw_", "SELECT", "JOIN", "allow-list", "AST", "sys."];

        foreach (var code in ReasonCodeMessages.DefinedCodes)
        {
            var message = ReasonCodeMessages.For(code);

            foreach (var fragment in forbiddenFragments)
            {
                Assert.DoesNotContain(fragment, message, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    [Fact]
    public void Cozumlenemeyen_kapsam_sinirsiz_kapsama_donusmez()
    {
        // Fail-closed'in kalbi: yetkisi belirlenemeyen kullanici her seyi goremez.
        Assert.False(UserDataScope.Unresolved.IsResolvable);
        Assert.False(UserDataScope.Unresolved.IsUnrestricted);
        Assert.Empty(UserDataScope.Unresolved.ValuesFor(UserDataScope.RegionDimension));
    }

    [Fact]
    public void Bos_deger_listesiyle_kapsam_cozumlenemeyen_sayilir()
    {
        var scope = UserDataScope.ForRegions();

        Assert.False(scope.IsResolvable);
        Assert.False(scope.IsUnrestricted);
    }

    [Fact]
    public void Sinirsiz_kapsam_yalnizca_acik_kararla_olusur()
    {
        Assert.True(UserDataScope.Unrestricted.IsUnrestricted);
        Assert.True(UserDataScope.Unrestricted.IsResolvable);
    }

    [Fact]
    public void Kapsam_degerleri_buyuk_kucuk_harf_duyarsiz_okunur()
    {
        var scope = UserDataScope.ForRegions("Marmara", "Ege");

        Assert.Equal(2, scope.ValuesFor("REGION").Count);
    }

    [Theory]
    [InlineData(SqlVersion.Sql170)]
    [InlineData(SqlVersion.Sql130)]
    [InlineData(SqlVersion.Sql80)]
    public void Desteklenmeyen_parser_surumu_sessizce_varsayilana_dusmez(SqlVersion version)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TSqlParserFactory(version));
    }

    [Fact]
    public void Varsayilan_parser_surumu_SQL_Server_2019()
    {
        Assert.Equal("Sql150", new TSqlParserFactory().VersionName);
    }
}
