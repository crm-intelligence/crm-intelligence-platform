using System.Text.Json;
using System.Text.Json.Serialization;

namespace Crm.Analytics.Sql.Catalog;

/// <summary>
/// Metric Catalog dosyasini yukler ve yapisal olarak dogrular.
/// </summary>
/// <remarks>
/// SQL ifadelerinin parse dogrulamasi burada YAPILMAZ; o, T-SQL parser gerektirdigi icin
/// <see cref="CatalogValidator"/>'in isidir. Ayrim, yukleyicinin parser bagimliligi
/// tasimamasi ve izole test edilebilmesi icin korunur.
/// </remarks>
public static class MetricCatalogLoader
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        // Bilinmeyen alan reddedilir: katalogdaki bir yazim hatasinin sessizce yok sayilmasi,
        // ornegin "approvalStatus" yerine "approvalState" yazilmasi, onaylanmamis bir KPI'nin
        // uretime sizmasi anlamina gelirdi.
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        AllowTrailingCommas = false,
        ReadCommentHandling = JsonCommentHandling.Disallow
    };

    private static readonly string[] ValidApprovalStatuses =
        ["documented", "designPending", "businessApprovalPending"];

    private static readonly string[] ValidUseCaseStatuses =
        ["ready", "partial", "blocked", "impossible"];

    private static readonly string[] ValidValueTypes =
        ["text", "integer", "decimal", "boolean", "date"];

    public static MetricCatalogDocument FromJson(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        MetricCatalogDocument? document;
        try
        {
            document = JsonSerializer.Deserialize<MetricCatalogDocument>(json, SerializerOptions);
        }
        catch (JsonException exception)
        {
            throw new CatalogValidationException($"Metric Catalog okunamadi: {exception.Message}");
        }

        if (document is null)
        {
            throw new CatalogValidationException("Metric Catalog bos.");
        }

        Validate(document);
        return document;
    }

    private static void Validate(MetricCatalogDocument document)
    {
        var errors = new List<string>();

        foreach (var (key, metric) in document.Metrics)
        {
            if (!ValidApprovalStatuses.Contains(metric.ApprovalStatus, StringComparer.Ordinal))
            {
                errors.Add($"'{key}' metriginde gecersiz approvalStatus: '{metric.ApprovalStatus}'.");
            }

            // Ifadesi olmayan bir metrik "hazir" gorunmemeli; Query Builder calisma aninda
            // patlamak yerine burada engellenir.
            if (!metric.IsUsable && metric.ApprovalStatus != "designPending")
            {
                errors.Add(
                    $"'{key}' metriginin ifadesi yok ancak approvalStatus '{metric.ApprovalStatus}'. " +
                    "Ifadesiz metrik designPending olmalidir.");
            }

            foreach (var dependency in metric.DependsOn.Where(dependency => !document.Metrics.ContainsKey(dependency)))
            {
                errors.Add($"'{key}' metrigi tanimsiz bir metrige bagli: '{dependency}'.");
            }
        }

        foreach (var (key, dimension) in document.Dimensions)
        {
            if (!ValidValueTypes.Contains(dimension.ValueType, StringComparer.Ordinal))
            {
                errors.Add($"'{key}' boyutunda gecersiz valueType: '{dimension.ValueType}'.");
            }
        }

        ValidateUseCases(document, errors);

        if (errors.Count > 0)
        {
            throw new CatalogValidationException(
                "Metric Catalog gecersiz:" + System.Environment.NewLine +
                string.Join(System.Environment.NewLine, errors.Select(error => "  - " + error)));
        }
    }

    private static void ValidateUseCases(MetricCatalogDocument document, List<string> errors)
    {
        foreach (var (key, useCase) in document.UseCases)
        {
            if (!ValidUseCaseStatuses.Contains(useCase.Status, StringComparer.Ordinal))
            {
                errors.Add($"'{key}' use case'inde gecersiz status: '{useCase.Status}'.");
            }

            // Engelli bir use case gerekcesini tasimak zorunda: gerekcesiz engel, engelin
            // sprint icinde surpriz olarak ortaya cikmasi demektir.
            if (useCase.Status is "blocked" or "impossible"
                && string.IsNullOrWhiteSpace(useCase.BlockedReason))
            {
                errors.Add($"'{key}' use case'i '{useCase.Status}' ancak blockedReason bos.");
            }

            foreach (var metric in useCase.Metrics.Where(metric => !document.Metrics.ContainsKey(metric)))
            {
                errors.Add($"'{key}' use case'i tanimsiz metrige referans veriyor: '{metric}'.");
            }

            foreach (var dimension in useCase.Dimensions.Where(dimension => !document.Dimensions.ContainsKey(dimension)))
            {
                errors.Add($"'{key}' use case'i tanimsiz boyuta referans veriyor: '{dimension}'.");
            }
        }
    }
}
