using System.Text.Json;
using System.Text.Json.Serialization;

namespace Crm.Analytics.Sql.Contracts;

/// <summary>
/// <see cref="CanonicalRequest"/> icin <b>tek</b> serialize sozlesmesi.
/// </summary>
/// <remarks>
/// <para>
/// Neden gerekli: Backend, Canonical Request'i konusma deposunda saklar ve takip sorusunda
/// geri verir (bkz. ENTEGRASYON.md §7). Serialize ayarlari iki tarafta ayri tanimlanirsa
/// <c>single_value</c> ile <c>singleValue</c> gibi bir fark, gecerli bir talebin sessizce
/// farkli yorumlanmasina yol acar. Bu yuzden ayarlar burada bir kez tanimlanir.
/// </para>
/// <para>
/// <b>Iki ayri adlandirma kurali bilincli:</b> alan adlari camelCase (<c>requestId</c>),
/// enum degerleri snake_case (<c>single_value</c>, <c>not_eq</c>). Sema dosyasi
/// (<c>canonical_request.schema.json</c>) bu ikiliyi kullanir; kod ile sema arasindaki
/// ortusme <c>SchemaModelAlignmentTests</c> tarafindan dogrulanir.
/// </para>
/// <para>
/// <b>Null alanlar yazilir</b> (atlanmaz): sema <c>kind=absolute</c> icin <c>from</c>/<c>to</c>
/// alanlarinin <i>varligini</i> zorunlu kilar. Null'lari atlayan bir ayar, gecerli bir nesneyi
/// sema dogrulamasindan gecmeyen bir JSON'a cevirirdi.
/// </para>
/// </remarks>
public static class CanonicalRequestSerializer
{
    public static JsonSerializerOptions Options { get; } = Create();

    public static string Serialize(CanonicalRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return JsonSerializer.Serialize(request, Options);
    }

    /// <summary>
    /// JSON'dan talebi okur. Tanimsiz bir alan <b>hata</b> uretir: sessizce yok saymak,
    /// Backend'in gonderdigi bir filtrenin veya tarih araliginin kaybolmasi anlamina gelirdi.
    /// </summary>
    public static CanonicalRequest Deserialize(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        return JsonSerializer.Deserialize<CanonicalRequest>(json, Options)
            ?? throw new JsonException("Canonical Request JSON'u null cozumlendi.");
    }

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = false,
            // Tanimsiz alan sessizce dusurulmez.
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            // Null yazilir: semadaki "required" alan varligini bekler.
            DefaultIgnoreCondition = JsonIgnoreCondition.Never
        };

        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower));

        return options;
    }
}
