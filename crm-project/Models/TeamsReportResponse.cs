using Crm.Analytics.Sql.Contracts;
using Crm.Analytics.Sql.Routing;
using Crm.Analytics.Sql.Service;

namespace crm_project.Models;

/// <summary>
/// Teams'e donen zenginlestirilmis yanit.
/// </summary>
/// <remarks>
/// <para>
/// Ham <see cref="SqlProductionResponse"/> Teams'e dogrudan verilmiyor. Iki sebep:
/// </para>
/// <list type="bullet">
/// <item>
/// Yanit, kart olarak <b>gosterilebilir</b> olmali: baslik, mesaj ve gorsel ipucu ayri
/// alanlar. Teams tarafinin karar agacini yeniden kurmasi gerekmemeli.
/// </item>
/// <item>
/// Ic teshis bilgisi disa cikmamali. <see cref="CheckResult.Detail"/> alanlari kullaniciya
/// gosterilmek icin degil; sema kesfi icin oracle olusturur. Bu tipe hic tasinmiyor.
/// </item>
/// </list>
/// </remarks>
public sealed record TeamsReportResponse
{
    public required string RequestId { get; init; }

    public required string ConversationId { get; init; }

    /// <summary>Backend durum modeli (<c>ENTEGRASYON.md</c> §6) karsiligi.</summary>
    public required string Status { get; init; }

    /// <summary>Kart basligi.</summary>
    public required string Title { get; init; }

    /// <summary>
    /// Kullaniciya gosterilecek mesaj. Ret ve netlestirmede dolu; sema bilgisi icermez.
    /// </summary>
    public string? Message { get; init; }

    /// <summary>Gerekce kodu (<c>GR001</c>–<c>GR015</c>, <c>CL001</c>–<c>CL002</c>).</summary>
    public string? ReasonCode { get; init; }

    /// <summary>
    /// Gorsel onerisi. Yalnizca kabul edilen talepte dolu — reddedilen bir talebe gorsel
    /// eslemek Teams tarafinda "rapor hazirlanabilir" izlenimi yaratirdi.
    /// </summary>
    public TeamsVisualHint? Visual { get; init; }

    /// <summary>Onaylanan sorgunun ozeti. Yalnizca kabul edilen talepte dolu.</summary>
    public TeamsQuerySummary? Query { get; init; }

    /// <summary>
    /// Netlestirme gerektiginde kullaniciya sunulacak somut oneriler. Cozumlenemeyen
    /// terimlerden uretilir; bos liste "oneri yok" demektir, uydurma oneri uretilmez.
    /// </summary>
    public IReadOnlyList<string> Suggestions { get; init; } = [];

    public static TeamsReportResponse From(SqlProductionResponse response, string conversationId)
    {
        ArgumentNullException.ThrowIfNull(response);

        return new TeamsReportResponse
        {
            RequestId = response.RequestId,
            ConversationId = conversationId,
            Status = MapStatus(response.Decision),
            Title = BuildTitle(response),
            Message = response.UserMessage,
            ReasonCode = response.ReasonCode == Crm.Analytics.Sql.Contracts.ReasonCode.None
                ? null
                : response.ReasonCode.ToString(),
            Visual = TeamsVisualHint.From(response.ResultShape),
            Query = TeamsQuerySummary.From(response),
            Suggestions = BuildSuggestions(response)
        };
    }

    /// <summary>
    /// <c>Accepted</c> icin <c>Completed</c> DEGIL <c>ReadyToRun</c> donuyor: bu serviste
    /// calistirma katmani yok, sorgu henuz kosmadi. <c>Completed</c> demek, veri dondugu
    /// izlenimi yaratirdi.
    /// </summary>
    private static string MapStatus(GuardrailDecision decision) => decision switch
    {
        GuardrailDecision.Accepted => "ReadyToRun",
        GuardrailDecision.NeedsClarification => "NeedsClarification",
        GuardrailDecision.Rejected => "Rejected",
        _ => "Rejected"
    };

    private static string BuildTitle(SqlProductionResponse response) => response.Decision switch
    {
        GuardrailDecision.Accepted =>
            response.ResultShape is null
                ? "Rapor sorgusu hazir"
                : $"Rapor sorgusu hazir — {DescribeVisual(response.ResultShape.SuggestedVisual)}",
        GuardrailDecision.NeedsClarification => "Talebi netlestirmek gerekiyor",
        _ => "Talep calistirilamadi"
    };

    private static string DescribeVisual(VisualType visual) => visual switch
    {
        VisualType.KpiCard => "KPI karti",
        VisualType.LineChart => "cizgi grafik",
        VisualType.BarChart => "bar grafik",
        VisualType.Matrix => "matris",
        VisualType.Table => "tablo",
        _ => visual.ToString()
    };

    private static IReadOnlyList<string> BuildSuggestions(SqlProductionResponse response)
    {
        if (response.Decision != GuardrailDecision.NeedsClarification)
        {
            return [];
        }

        // Cozumlenemeyen terim varsa kullaniciya onu isaret etmek en ise yarar oneri.
        // Terim yoksa (ornegin tarih araligi eksik) genel bir yonlendirme veriliyor;
        // uydurma metrik veya kirilim ONERILMEZ -- katalogda olmayan bir sey teklif etmek
        // kullaniciyi var olmayan bir rapora yonlendirirdi.
        if (response.UnresolvedTerms.Count > 0)
        {
            return
            [
                .. response.UnresolvedTerms.Select(term =>
                    $"'{term}' ifadesini rapor sozlugundeki bir olcum veya kirilimla degistirin.")
            ];
        }

        return ["Talebe bir tarih araligi ekleyin (ornek: 2018 veya 2018-01-01 ile 2018-03-31)."];
    }
}

/// <summary>Sonuc yapisindan uretilen gorsel ipucu.</summary>
public sealed record TeamsVisualHint
{
    public required string Type { get; init; }

    /// <summary>Onerinin gerekcesi. BI/Teams tarafi oneriyi kabul etmek zorunda degil.</summary>
    public required string Rationale { get; init; }

    public required int MetricCount { get; init; }

    public required int DimensionCount { get; init; }

    public required bool HasTimeDimension { get; init; }

    public static TeamsVisualHint? From(ResultShape? shape) => shape is null
        ? null
        : new TeamsVisualHint
        {
            Type = shape.SuggestedVisual.ToString(),
            Rationale = shape.Rationale,
            MetricCount = shape.MetricCount,
            DimensionCount = shape.DimensionCount,
            HasTimeDimension = shape.HasTimeDimension
        };
}

/// <summary>
/// Onaylanan sorgunun Teams'e gosterilebilir ozeti.
/// </summary>
/// <remarks>
/// Parametre <b>adlari</b> var, <b>degerleri</b> yok. Degerler kullanici verisi; guardrail'in
/// PII kontrollerini uygularken ayni veriyi yanitta disa vermek celiskili olurdu.
/// </remarks>
public sealed record TeamsQuerySummary
{
    public required string Sql { get; init; }

    /// <summary>Uygulanan kapsam filtresi — kapsamin gercekten uygulandiginin kaniti.</summary>
    public required string AppliedScopeFilter { get; init; }

    public required IReadOnlyList<string> ParameterNames { get; init; }

    public int? CommandTimeoutSeconds { get; init; }

    /// <summary>Gercekten dogrulanan kontrol sayisi. Atlanan kontrol "gecti" sayilmaz.</summary>
    public required int VerifiedCheckCount { get; init; }

    public static TeamsQuerySummary? From(SqlProductionResponse response)
    {
        // Ret ve netlestirmede Sql null gelir; sorgu ozeti de uretilmez.
        if (response.Decision != GuardrailDecision.Accepted || response.Sql is null)
        {
            return null;
        }

        return new TeamsQuerySummary
        {
            Sql = response.Sql,
            AppliedScopeFilter = response.AppliedScopeFilter ?? string.Empty,
            ParameterNames = [.. response.Parameters.Select(parameter => parameter.Name)],
            CommandTimeoutSeconds = response.CommandTimeoutSeconds,
            VerifiedCheckCount = response.Checks.Count(check => check.Outcome == CheckOutcome.Passed)
        };
    }
}
