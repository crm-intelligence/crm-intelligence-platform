using Crm.Analytics.Sql.IntegrationTests.Sqlite;

namespace Crm.Analytics.Sql.IntegrationTests.Translation;

/// <summary>
/// Harness'in en onemli fail-loud mekanizmasi.
/// </summary>
/// <remarks>
/// Uretim allow-list'ine yeni bir fonksiyon eklenip harness'a karsiligi
/// eklenmezse bu test kirmizyya doner. Boylece varsayilan davranis
/// "sessizce cevrilmemis" olmaktan cikip "acik hata" haline gelir.
/// Veritabani gerektirmez: fixture'i olmayan gelistiricide de calisir.
/// </remarks>
public sealed class FunctionCoverageTests
{
    [Fact]
    public void Allow_list_fonksiyonlarinin_tamami_harness_tarafindan_biliniyor()
    {
        var allowed = OlistCatalog.AllowList.AllowedFunctions;

        var unmapped = allowed
            .Where(f => !TSqlFunctionSets.AllKnown.Contains(f))
            .ToList();

        Assert.True(
            unmapped.Count == 0,
            $"Allow-list'te olup harness'ta karsiligi olmayan fonksiyon(lar): " +
            $"{string.Join(", ", unmapped)}. " +
            "Sqlite/TSqlFunctionSets.cs icindeki Native / Renamed / UserDefined " +
            "kumelerinden birine eklenmelidir.");
    }

    [Fact]
    public void Harness_allow_list_disinda_fonksiyon_tanimlamiyor()
    {
        var allowed = OlistCatalog.AllowList.AllowedFunctions
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var extra = TSqlFunctionSets.AllKnown
            .Where(f => !allowed.Contains(f))
            .ToList();

        // Harness'in allow-list'ten FAZLA fonksiyon tanimasi da bir sorundur:
        // guardrail'in reddettigi bir fonksiyonu harness'in calistirabilmesi,
        // testlerin uretimde mumkun olmayan bir yolu dogrulamasi demektir.
        Assert.True(
            extra.Count == 0,
            $"Harness'ta olup allow-list'te olmayan fonksiyon(lar): " +
            $"{string.Join(", ", extra)}. Guardrail bunlari reddedecegi icin " +
            "harness'in bilmesi yanlis guven verir.");
    }

    [Fact]
    public void Uc_kume_ortusmuyor()
    {
        // Bir fonksiyon hem yerlesik hem UDF olarak isaretlenirse hangi yolun
        // calistigini bilemeyiz.
        Assert.Empty(TSqlFunctionSets.Native.Intersect(TSqlFunctionSets.UserDefined, StringComparer.OrdinalIgnoreCase));
        Assert.Empty(TSqlFunctionSets.Native.Intersect(TSqlFunctionSets.Renamed.Keys, StringComparer.OrdinalIgnoreCase));
        Assert.Empty(TSqlFunctionSets.UserDefined.Intersect(TSqlFunctionSets.Renamed.Keys, StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public void Yeniden_adlandirma_hedefleri_sqlite_tarafinda_calistirilabilir()
    {
        foreach (var (source, target) in TSqlFunctionSets.Renamed)
        {
            Assert.True(
                TSqlFunctionSets.ExecutableAfterTranslation.Contains(target),
                $"{source} -> {target} yeniden adlandirmasinin hedefi SQLite'ta " +
                "calistirilabilir kumede degil.");
        }
    }
}
