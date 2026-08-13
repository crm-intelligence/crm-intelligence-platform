using System.Security.Claims;
using Crm.Analytics.Sql.Contracts;

namespace crm_project.Services;

/// <summary>
/// JWT claim'lerinden kullanicinin veri kapsamini cozumler.
/// </summary>
/// <remarks>
/// <para>
/// SQL uretim katmani kapsami <b>uretmez</b>, aldigi kapsami SQL'e zorla uygular
/// (bkz. <c>Crm.Analytics.Sql/ENTEGRASYON.md</c> §4). Kullanici -> kapsam eslemesi
/// Backend'in sorumlulugu; bu sinif o eslemenin tek yeri.
/// </para>
/// <para>
/// <b>Fail-closed.</b> Cozumlenemeyen her durum <see cref="UserDataScope.Unresolved"/>
/// doner ve guardrail bunu <see cref="ReasonCode.GR007"/> ile reddeder. Bos kapsamin
/// "kisit yok" sayilmasi en tehlikeli hata olurdu: yetkisi belirlenemeyen kullanici
/// her seyi gorurdu.
/// </para>
/// <para>
/// Mevcut <c>/api/requests</c> ucundaki satir ici RLS kontrolu bu konuda fail-OPEN:
/// <c>if (!isAdmin &amp;&amp; isRegionManager &amp;&amp; ...)</c> kosulu, iki rolden de
/// olmayan bir kullanicinin (ornegin hic rol claim'i tasimayan) bolge kontrolunu
/// tamamen atlamasina izin veriyor. Bu sinif ayni hatayi tekrarlamiyor.
/// </para>
/// </remarks>
public sealed class ClaimsDataScopeResolver
{
    /// <summary>Bolge claim'i. Namespace'siz, duz ad — token'i ureten tarafla eslesir.</summary>
    public const string RegionClaimType = "region";

    /// <summary>Entra ID uygulama rolleri claim'i (ClaimTypes.Role'a ek olarak).</summary>
    public const string RolesClaimType = "roles";

    public const string AdminRole = "Admin";
    public const string RegionManagerRole = "RegionManager";

    /// <summary>
    /// Admin kullanicilarin token'inda bolge yerine gecen isaret deger. Gercek bir bolge
    /// kodu degil, bu yuzden kapsam degeri olarak asla kullanilmaz.
    /// </summary>
    public const string WildcardRegionValue = "ALL";

    public UserDataScope Resolve(ClaimsPrincipal? user)
    {
        // Kimlik dogrulanmadiysa kapsam konusulamaz.
        if (user?.Identity?.IsAuthenticated != true)
        {
            return UserDataScope.Unresolved;
        }

        var roles = ReadRoles(user);

        // Sinirsiz kapsam yalnizca ACIK bir rol karariyla verilir; claim'in yoklugu veya
        // taninmayan bir rol bu bayragi asla true yapmaz.
        if (roles.Contains(AdminRole, StringComparer.OrdinalIgnoreCase))
        {
            return UserDataScope.Unrestricted;
        }

        if (!roles.Contains(RegionManagerRole, StringComparer.OrdinalIgnoreCase))
        {
            // Taninmayan veya eksik rol: kapsam belirlenemez.
            return UserDataScope.Unresolved;
        }

        var regions = user
            .FindAll(RegionClaimType)
            .Select(claim => claim.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            // "ALL" degerini bolge kodu saymiyoruz. Aksi halde RegionManager rolundeki bir
            // kullanici, token'inda region=ALL tasiyarak kapsam filtresini "ALL" eyaletine
            // cevirir ve bu filtre hicbir satira uymadigi icin sessiz bos sonuc dondururdu;
            // daha kotusu, ilerde "ALL" ozel olarak yorumlanirsa yetki yukseltmesi olurdu.
            .Where(value => !string.Equals(value, WildcardRegionValue, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        // ForRegions bos diziyle Unresolved dondurur; yine de niyeti aciklikta tutuyoruz.
        return regions.Length == 0
            ? UserDataScope.Unresolved
            : UserDataScope.ForRegions(regions);
    }

    /// <summary>
    /// Rolleri iki claim tipinden birlestirir. <c>FindFirst</c> degil <c>FindAll</c>
    /// kullaniliyor: coklu rol tasiyan bir token'da ilk claim'e bakmak, Admin rolunu
    /// siralamaya bagli olarak kaciririrdi.
    /// </summary>
    private static string[] ReadRoles(ClaimsPrincipal user) =>
    [
        .. user.FindAll(ClaimTypes.Role).Select(claim => claim.Value),
        .. user.FindAll(RolesClaimType).Select(claim => claim.Value)
    ];
}
