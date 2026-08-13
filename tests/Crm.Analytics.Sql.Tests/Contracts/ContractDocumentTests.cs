using System.Text.Json;
using Crm.Analytics.Sql.Contracts;

namespace Crm.Analytics.Sql.Tests.Contracts;

/// <summary>
/// Gun 1 ciktisinin dogrulanmasi: Canonical Request semasi ve Metric Catalog iskeleti
/// gecerli, eksiksiz ve kendi kurallarina uygun mu.
/// </summary>
public class ContractDocumentTests
{
    [Fact]
    public void Canonical_request_semasi_gecerli_json()
    {
        using var document = JsonDocument.Parse(ContractResources.ReadCanonicalRequestSchema());

        var root = document.RootElement;
        Assert.Equal(JsonValueKind.Object, root.ValueKind);
        Assert.True(root.TryGetProperty("$id", out _));
        Assert.Equal("object", root.GetProperty("type").GetString());
    }

    [Fact]
    public void Sema_yetki_kapsamini_tasiyan_bir_alan_TANIMLAMAZ()
    {
        // Kirmizi cizgi: kullanicinin veri kapsami bu sozlesmeye girmez. Girerse NL2SQL
        // prompt'una sizma yolu acilir; kapsam SQL'e yalnizca guardrail tarafindan eklenir.
        var schema = ContractResources.ReadCanonicalRequestSchema();
        using var document = JsonDocument.Parse(schema);

        var propertyNames = document.RootElement.GetProperty("properties")
            .EnumerateObject()
            .Select(property => property.Name)
            .ToArray();

        Assert.DoesNotContain("userScope", propertyNames);
        Assert.DoesNotContain("scopeRegions", propertyNames);
        Assert.DoesNotContain("userId", propertyNames);
    }

    [Fact]
    public void Sema_ek_alanlara_izin_vermez()
    {
        // additionalProperties=false olmadan Backend sessizce taninmayan alan gonderebilir
        // ve biz bunu yok sayabiliriz. Sozlesme ihlali sessiz kalmamali.
        using var document = JsonDocument.Parse(ContractResources.ReadCanonicalRequestSchema());

        Assert.False(document.RootElement.GetProperty("additionalProperties").GetBoolean());
    }

    [Fact]
    public void Semadaki_intent_degerleri_RequestIntent_enum_uyeleriyle_birebir_ortusur()
    {
        // Sema ile C# tipinin ayrisması, Backend'in gonderdigi gecerli bir talebin bizde
        // sessizce farkli yorumlanmasi anlamina gelir.
        using var document = JsonDocument.Parse(ContractResources.ReadCanonicalRequestSchema());

        var schemaValues = document.RootElement
            .GetProperty("properties").GetProperty("intent").GetProperty("enum")
            .EnumerateArray()
            .Select(value => value.GetString()!)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

        var enumValues = Enum.GetNames<RequestIntent>()
            .Select(ToSnakeCase)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(enumValues, schemaValues);
    }

    [Fact]
    public void Semadaki_filtre_operatorleri_FilterOperator_enum_uyeleriyle_birebir_ortusur()
    {
        using var document = JsonDocument.Parse(ContractResources.ReadCanonicalRequestSchema());

        var schemaValues = document.RootElement
            .GetProperty("$defs").GetProperty("filter")
            .GetProperty("properties").GetProperty("op").GetProperty("enum")
            .EnumerateArray()
            .Select(value => value.GetString()!)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

        var enumValues = Enum.GetNames<FilterOperator>()
            .Select(ToSnakeCase)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(enumValues, schemaValues);
    }

    [Fact]
    public void Ornek_metric_catalog_gecerli_json_ve_uretim_dosyasi_olmadigini_isaretler()
    {
        using var document = JsonDocument.Parse(ContractResources.ReadSampleMetricCatalog());

        var status = document.RootElement.GetProperty("_meta").GetProperty("status").GetString();
        Assert.Contains("SAMPLE", status, StringComparison.Ordinal);
    }

    [Fact]
    public void Katalogdaki_her_metrigin_onay_durumu_bildirilmis()
    {
        // "Emin olmadigin bir seyi TODO ile gecme" kurali: onaylanmamis bir KPI sessizce
        // uretime sizmamali. approvalStatus zorunlu.
        using var document = JsonDocument.Parse(ContractResources.ReadSampleMetricCatalog());

        foreach (var metric in document.RootElement.GetProperty("metrics").EnumerateObject())
        {
            Assert.True(
                metric.Value.TryGetProperty("approvalStatus", out var status),
                $"'{metric.Name}' metriginde approvalStatus alani yok.");

            Assert.Contains(status.GetString(), new[] { "documented", "designPending", "businessApprovalPending" });
        }
    }

    [Fact]
    public void Ifadesi_olmayan_metrik_designPending_olarak_isaretlenmis()
    {
        // growth_rate tek bir SQL ifadesiyle tanimlanamaz. Ifadesiz bir metrigin "hazir"
        // gorunmesi, Query Builder'in calisma aninda patlamasi demektir.
        using var document = JsonDocument.Parse(ContractResources.ReadSampleMetricCatalog());

        foreach (var metric in document.RootElement.GetProperty("metrics").EnumerateObject())
        {
            var hasExpression = metric.Value.TryGetProperty("expression", out var expression)
                && expression.ValueKind == JsonValueKind.String;

            if (!hasExpression)
            {
                Assert.Equal("designPending", metric.Value.GetProperty("approvalStatus").GetString());
            }
        }
    }

    [Fact]
    public void Engellenen_use_case_gerekcesini_ve_kimden_ne_bekledigini_bildirir()
    {
        // Segmentasyon ve kampanya senaryolari ornek allow-list ile kurulamiyor. Bu bilgi
        // dosyada gerekcesiz durursa, engel Gun 6'da surpriz olarak ortaya cikar.
        using var document = JsonDocument.Parse(ContractResources.ReadSampleMetricCatalog());

        var blocked = document.RootElement.GetProperty("useCases").EnumerateObject()
            .Where(useCase => useCase.Value.GetProperty("status").GetString() == "blocked")
            .ToArray();

        Assert.NotEmpty(blocked);

        foreach (var useCase in blocked)
        {
            Assert.True(useCase.Value.TryGetProperty("blockedReason", out var reason));
            Assert.False(string.IsNullOrWhiteSpace(reason.GetString()));
            Assert.True(useCase.Value.TryGetProperty("needsFromDataEngineer", out _));
        }
    }

    [Fact]
    public void Katalogdaki_ifadeler_tablo_aliasi_kullanmaz()
    {
        // Alias sozlesmesi: expression'da alias YOK. Dokumandaki 'f.' ile negatif test
        // orneklerindeki 'v.' celiskisini katalogda sabitlemek yerine, alias'i Query Builder
        // AST uzerinde niteler. Katalogda alias gorulurse bu karar bozulmus demektir.
        using var document = JsonDocument.Parse(ContractResources.ReadSampleMetricCatalog());

        foreach (var metric in document.RootElement.GetProperty("metrics").EnumerateObject())
        {
            if (!metric.Value.TryGetProperty("expression", out var expression)
                || expression.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var text = expression.GetString()!;
            Assert.DoesNotContain("f.", text, StringComparison.Ordinal);
            Assert.DoesNotContain("v.", text, StringComparison.Ordinal);
        }
    }

    private static string ToSnakeCase(string pascalCase) =>
        string.Concat(pascalCase.Select((character, index) =>
            char.IsUpper(character) && index > 0
                ? "_" + char.ToLowerInvariant(character)
                : char.ToLowerInvariant(character).ToString()));
}
