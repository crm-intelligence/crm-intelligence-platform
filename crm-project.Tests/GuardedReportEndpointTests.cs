using System.Net;
using System.Net.Http.Json;
using Crm.Analytics.Sql.Service;
using crm_project.Models;
using Microsoft.Extensions.DependencyInjection;

namespace crm_project.Tests;

/// <summary>
/// <c>POST /api/reports</c> ucunun ve SQL uretim katmani DI kaydinin testleri.
/// </summary>
/// <remarks>
/// Bu paket sorguyu CALISTIRMAZ — calistirma katmani yok. Burada dogrulanan sey
/// uygulamanin kutuphaneyi dogru kurdugu ve kararlari Teams formatina dogru tasidigi.
/// Guardrail'in kendi davranisi <c>Crm.Analytics.Sql.Tests</c> (414 test) ve uretilen
/// SQL'in gercekten kosabildigi <c>Crm.Analytics.Sql.IntegrationTests</c> (68 test)
/// altinda dogrulaniyor.
/// </remarks>
public class GuardedReportEndpointTests(TestWebApplicationFactory factory)
    : IClassFixture<TestWebApplicationFactory>
{
    /// <summary>
    /// Olist veri setinde eyalet kodlari Brezilya eyaletleridir. Uygulamanin mevcut
    /// RLS testleri TR/EU kullaniyor; katalogla ortak bir deger degil. Bu testler
    /// gercek katalog degerlerini kullaniyor.
    /// </summary>
    private const string OlistRegion = "SP";

    private const string GecerliTalep = "2018 satış tutarını eyalete göre göster";

    [Fact]
    public void Di_kaydi_sql_uretim_servisini_cozumleyebiliyor()
    {
        // "Calisan DI kaydi"nin gercek testi bu: katalog ve allow-list dogrulamasi
        // KURULUM aninda yapiliyor, ilk istekte degil. Hatali bir katalog satiri
        // servisi cozumlerken patlar.
        using var scope = factory.Services.CreateScope();

        var service = scope.ServiceProvider.GetRequiredService<ISqlProductionService>();

        Assert.NotNull(service);
    }

    [Fact]
    public async Task Gecerli_talep_calistirilabilir_sorgu_ve_gorsel_onerisi_dondurur()
    {
        var client = CreateClient("sp-manager", "RegionManager", OlistRegion);

        var response = await client.PostAsJsonAsync("/api/reports", Talep(GecerliTalep));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await ReadAsync(response);

        Assert.Equal("ReadyToRun", body.Status);
        Assert.NotNull(body.Query);
        Assert.NotNull(body.Visual);

        // Tek kategorik kirilim -> bar. Oneri gerekcesiyle birlikte geliyor.
        Assert.Equal("BarChart", body.Visual.Type);
        Assert.False(string.IsNullOrWhiteSpace(body.Visual.Rationale));

        // Kapsamin gercekten uygulandiginin kaniti; bos olamaz.
        Assert.False(string.IsNullOrWhiteSpace(body.Query.AppliedScopeFilter));

        // Filtre kapsam kolonu uzerinden kuruluyor ve deger PARAMETRE olarak gecuyor.
        // Eyalet kodunun filtre metninde ARANMAMASI bilincli: "SP" burada gorunurse
        // deger SQL metnine gomulmus olurdu (LiteralParameterization ihlali).
        Assert.Contains("customer_state", body.Query.AppliedScopeFilter, StringComparison.Ordinal);
        Assert.Contains("@scope0", body.Query.AppliedScopeFilter, StringComparison.Ordinal);
        Assert.DoesNotContain(OlistRegion, body.Query.AppliedScopeFilter, StringComparison.Ordinal);

        Assert.Equal(16, body.Query.VerifiedCheckCount);
    }

    [Fact]
    public async Task Yanitta_parametre_adlari_var_degerleri_yok()
    {
        var client = CreateClient("sp-manager", "RegionManager", OlistRegion);

        var response = await client.PostAsJsonAsync("/api/reports", Talep(GecerliTalep));
        var body = await ReadAsync(response);

        Assert.NotNull(body.Query);
        Assert.NotEmpty(body.Query.ParameterNames);

        // Ham JSON uzerinden bakiyoruz: tipe eslemek, siziyor olsa bile gizleyebilirdi.
        var json = await response.Content.ReadAsStringAsync();

        // Tarih ve eyalet kodu deger olarak SQL metninde veya yanitta gecmemeli.
        Assert.DoesNotContain("2018-01-01", json, StringComparison.Ordinal);

        // Parametre ADLARI ise gecmeli.
        Assert.Contains("@f0", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Belirsiz_talep_netlestirme_ister_ve_sorgu_uretmez()
    {
        var client = CreateClient("sp-manager", "RegionManager", OlistRegion);

        // Katalogda karsiligi olmayan metrik: uydurma bir olcume baglanmamali.
        var response = await client.PostAsJsonAsync("/api/reports", Talep("2018 kâr marjı nedir"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await ReadAsync(response);

        Assert.Equal("NeedsClarification", body.Status);

        // Sorgu ozeti uretilmemeli; yoksa Teams tarafi "rapor hazir" sanardi.
        Assert.Null(body.Query);
        Assert.Null(body.Visual);

        Assert.False(string.IsNullOrWhiteSpace(body.Message));
        Assert.NotEmpty(body.Suggestions);
    }

    [Fact]
    public async Task Kapsam_cozumlenemeyen_kullanici_reddedilir()
    {
        // FAIL-CLOSED KANITI: rol claim'i olmayan kullanici. Mevcut /api/requests
        // ucundaki satir ici RLS kontrolu boyle bir kullaniciyi bolge karsilastirmasindan
        // muaf tutuyor; bu uc GR007 ile reddediyor.
        var client = CreateClient("rolsuz-kullanici", role: null, region: OlistRegion);

        var response = await client.PostAsJsonAsync("/api/reports", Talep(GecerliTalep));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        var body = await ReadAsync(response);

        Assert.Equal("Rejected", body.Status);
        Assert.Equal("GR007", body.ReasonCode);
        Assert.Null(body.Query);
    }

    [Fact]
    public async Task RegionManager_ALL_degeriyle_sinirsiz_kapsam_kazanmaz()
    {
        // "ALL" gercek bir eyalet kodu degil. RegionManager rolundeki bir kullanici
        // token'ina region=ALL koyarak sinirsiz kapsam elde edememeli.
        var client = CreateClient("sahte-admin", "RegionManager", "ALL");

        var response = await client.PostAsJsonAsync("/api/reports", Talep(GecerliTalep));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        var body = await ReadAsync(response);

        Assert.Equal("GR007", body.ReasonCode);
    }

    [Fact]
    public async Task Admin_sinirsiz_kapsamla_sorgu_alir()
    {
        var client = CreateClient("admin-user", "Admin", "ALL");

        var response = await client.PostAsJsonAsync("/api/reports", Talep(GecerliTalep));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await ReadAsync(response);

        Assert.Equal("ReadyToRun", body.Status);
        Assert.NotNull(body.Query);

        // Sinirsiz kapsamda da alan bos kalmiyor: muafiyet gerekcesi yaziliyor,
        // boylece "kapsam uygulanmadi" ile "kapsam kisitlanmadi" ayirt edilebilir.
        Assert.False(string.IsNullOrWhiteSpace(body.Query.AppliedScopeFilter));
    }

    [Fact]
    public async Task Kimlik_dogrulanmadan_erisilemez()
    {
        // X-Test-User yok -> TestAuthHandler basarisiz doner.
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/reports", Talep(GecerliTalep));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Mevcut_api_requests_ucu_bozulmadi()
    {
        // Yeni uc eklenirken BE'nin ucunun davranisi degismedigini ayrica dogruluyoruz:
        // ayni istek, ayni sonuc.
        var client = CreateClient("tr-manager", "RegionManager", "TR");

        var response = await client.PostAsJsonAsync(
            "/api/requests",
            new CanonicalRequest
            {
                RequestId = "regresyon-001",
                Prompt = "Satış raporunu göster",
                UseCase = "sales_report",
                UserId = "test-user",
                Source = "crm",
                TargetTable = "sales",
                Region = "TR",
                Parameters = new Dictionary<string, object>()
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static ReportRequest Talep(string prompt) => new()
    {
        Prompt = prompt,
        ConversationId = "conv-test-001"
    };

    private static async Task<TeamsReportResponse> ReadAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadFromJsonAsync<TeamsReportResponse>();

        Assert.NotNull(body);

        return body;
    }

    private HttpClient CreateClient(string userId, string? role, string? region)
    {
        var client = factory.CreateClient();

        client.DefaultRequestHeaders.Add("X-Test-User", userId);

        if (role is not null)
        {
            client.DefaultRequestHeaders.Add("X-Test-Role", role);
        }

        if (region is not null)
        {
            client.DefaultRequestHeaders.Add("X-Test-Region", region);
        }

        return client;
    }
}
