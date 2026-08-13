using System.Text.Json;
using Crm.Analytics.Sql.Contracts;

namespace Crm.Analytics.Sql.Tests.Contracts;

/// <summary>
/// Sema dosyasi ile C# modelinin ortustugunu dogrular.
/// </summary>
/// <remarks>
/// Onceki test seti yalnizca <b>enum degerlerini</b> karsilastiriyordu; semaya uygun bir
/// JSON'un gercekten modele cozumlenebildigini hicbir test kontrol etmiyordu. Bu bosluk
/// gercek bir uyumsuzlugu gizledi: sema <c>filter.value</c> (tekil) tanimliyordu, model
/// <c>Values</c> (liste) bekliyordu. Backend semaya gore JSON uretirse cozumleme
/// <b>basarisiz olurdu</b>.
/// </remarks>
public class SchemaModelAlignmentTests
{
    /// <summary>Semaya uygun, tum alanlari dolu ornek talep.</summary>
    private const string SchemaCompliantJson = """
    {
      "requestId": "req_01HX",
      "conversationId": "conv_8f2",
      "previousRequestId": "req_01HW",
      "intent": "single_value",
      "metrics": ["item_sales"],
      "dimensions": ["customer_state"],
      "filters": [
        {
          "field": "product_category",
          "op": "not_eq",
          "values": [{ "kind": "text", "raw": "electronics" }]
        }
      ],
      "dateRange": {
        "kind": "absolute",
        "relativeExpression": null,
        "from": "2018-01-01",
        "to": "2018-03-31"
      },
      "grain": "month",
      "limit": 500,
      "scenarioKey": "sales_by_region",
      "confidence": 0.86,
      "unresolvedTerms": []
    }
    """;

    [Fact]
    public void Semaya_uygun_JSON_modele_cozumlenebilir()
    {
        var request = CanonicalRequestSerializer.Deserialize(SchemaCompliantJson);

        Assert.Equal("req_01HX", request.RequestId);
        Assert.Equal(RequestIntent.SingleValue, request.Intent);
        Assert.Equal(TimeGrain.Month, request.Grain);
        Assert.Equal(["item_sales"], request.Metrics);
        Assert.Equal(new DateOnly(2018, 1, 1), request.DateRange.From);
        Assert.Equal(DateRangeKind.Absolute, request.DateRange.Kind);

        var filter = Assert.Single(request.Filters);
        Assert.Equal("product_category", filter.Field);
        Assert.Equal(FilterOperator.NotEq, filter.Op);
        Assert.Equal("electronics", Assert.Single(filter.Values).Raw);
        Assert.Equal(FilterValueKind.Text, filter.Values[0].Kind);
    }

    [Fact]
    public void Model_JSON_a_cevrilip_geri_okunabilir()
    {
        // Karsilastirma JSON METNI uzerinden yapilir: record esitligi koleksiyon uyelerinde
        // referans karsilastirir, dolayisiyla iceriği ayni iki nesne "farkli" gorunurdu.
        var once = CanonicalRequestSerializer.Serialize(
            CanonicalRequestSerializer.Deserialize(SchemaCompliantJson));

        var twice = CanonicalRequestSerializer.Serialize(
            CanonicalRequestSerializer.Deserialize(once));

        Assert.Equal(once, twice);
    }

    [Fact]
    public void Uretilen_JSON_alan_adlari_semadakilerle_ortusur()
    {
        // Model semada tanimsiz bir alan uretirse Backend'in dogrulamasi kirilir.
        var schemaProperties = SchemaProperties();

        using var document = JsonDocument.Parse(
            CanonicalRequestSerializer.Serialize(
                CanonicalRequestSerializer.Deserialize(SchemaCompliantJson)));

        var produced = document.RootElement.EnumerateObject().Select(p => p.Name).ToArray();

        Assert.All(produced, name =>
            Assert.Contains(name, schemaProperties));
    }

    [Fact]
    public void Semadaki_zorunlu_alanlarin_tamami_modelde_var()
    {
        using var schema = JsonDocument.Parse(ContractResources.ReadCanonicalRequestSchema());

        var required = schema.RootElement.GetProperty("required")
            .EnumerateArray().Select(v => v.GetString()!).ToArray();

        using var produced = JsonDocument.Parse(
            CanonicalRequestSerializer.Serialize(
                CanonicalRequestSerializer.Deserialize(SchemaCompliantJson)));

        var names = produced.RootElement.EnumerateObject().Select(p => p.Name).ToArray();

        Assert.All(required, field => Assert.Contains(field, names));
    }

    [Fact]
    public void Semadaki_tarih_araligi_turleri_DateRangeKind_ile_ortusur()
    {
        using var schema = JsonDocument.Parse(ContractResources.ReadCanonicalRequestSchema());

        var schemaValues = schema.RootElement
            .GetProperty("$defs").GetProperty("dateRange")
            .GetProperty("properties").GetProperty("kind").GetProperty("enum")
            .EnumerateArray().Select(v => v.GetString()!)
            .OrderBy(v => v, StringComparer.Ordinal);

        var enumValues = Enum.GetNames<DateRangeKind>()
            .Select(JsonNamingPolicy.SnakeCaseLower.ConvertName)
            .OrderBy(v => v, StringComparer.Ordinal);

        Assert.Equal(enumValues, schemaValues);
    }

    [Fact]
    public void Semadaki_filtre_deger_tipleri_FilterValueKind_ile_ortusur()
    {
        using var schema = JsonDocument.Parse(ContractResources.ReadCanonicalRequestSchema());

        var schemaValues = schema.RootElement
            .GetProperty("$defs").GetProperty("filterLiteral")
            .GetProperty("properties").GetProperty("kind").GetProperty("enum")
            .EnumerateArray().Select(v => v.GetString()!)
            .OrderBy(v => v, StringComparer.Ordinal);

        var enumValues = Enum.GetNames<FilterValueKind>()
            .Select(JsonNamingPolicy.SnakeCaseLower.ConvertName)
            .OrderBy(v => v, StringComparer.Ordinal);

        Assert.Equal(enumValues, schemaValues);
    }

    [Fact]
    public void Uygulanamaz_tarih_araligi_semada_kendi_turune_sahiptir()
    {
        // "kind=absolute ama tarih yok" kombinasyonu semanin niyetini ihlal ederdi: sema
        // absolute icin from/to zorunlu kilar. Uygulanamaz aralik ucuncu bir durumdur.
        using var document = JsonDocument.Parse(
            JsonSerializer.Serialize(DateRangeSpec.NotApplicable, CanonicalRequestSerializer.Options));

        Assert.Equal("not_applicable", document.RootElement.GetProperty("kind").GetString());
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("from").ValueKind);
    }

    [Fact]
    public void Tanimsiz_alan_sessizce_yok_sayilmaz()
    {
        // Sessiz dusurme, Backend'in gonderdigi bir filtrenin kaybolmasi anlamina gelirdi.
        const string withUnknownField = """
        {
          "requestId": "req_1", "conversationId": "conv_1", "intent": "list",
          "metrics": [], "dateRange": { "kind": "not_applicable" },
          "userScope": "SP"
        }
        """;

        Assert.Throws<JsonException>(() => CanonicalRequestSerializer.Deserialize(withUnknownField));
    }

    private static string[] SchemaProperties()
    {
        using var schema = JsonDocument.Parse(ContractResources.ReadCanonicalRequestSchema());

        return [.. schema.RootElement.GetProperty("properties")
            .EnumerateObject().Select(p => p.Name)];
    }
}
