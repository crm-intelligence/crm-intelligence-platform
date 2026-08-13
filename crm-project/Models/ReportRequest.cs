using System.ComponentModel.DataAnnotations;

namespace crm_project.Models;

/// <summary>
/// <c>POST /api/reports</c> gövdesi.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="CanonicalRequest"/>'ten kasten ayrı bir tip. En önemli fark: burada
/// <b>Region alanı yok</b>. Kullanıcının veri kapsamı token'dan türetilir
/// (<see cref="Services.ClaimsDataScopeResolver"/>); istemcinin gönderdiği bir bölge
/// değeri yetki kararına giremez.
/// </para>
/// <para>
/// <c>TargetTable</c> da yok: hangi objelere erişilebileceğine allow-list kataloğu karar
/// verir, istemci değil.
/// </para>
/// </remarks>
public sealed class ReportRequest
{
    /// <summary>Kullanıcının serbest metni (Teams'ten gelen talep).</summary>
    [Required(AllowEmptyStrings = false)]
    public string Prompt { get; set; } = string.Empty;

    /// <summary>
    /// Konuşma kimliği. Takip sorusu zincirini Backend yönetir; SQL üretim servisi
    /// durumsuzdur ve konuşma durumunu saklamaz.
    /// </summary>
    [Required(AllowEmptyStrings = false)]
    public string ConversationId { get; set; } = string.Empty;
}
