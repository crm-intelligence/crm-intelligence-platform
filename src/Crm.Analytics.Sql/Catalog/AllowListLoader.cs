using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Crm.Analytics.Sql.Contracts;

namespace Crm.Analytics.Sql.Catalog;

/// <summary>
/// Allow-list dosyasini yukler ve <b>yapisal olarak</b> dogrular. Gecersiz dosya ile
/// uygulama baslamaz.
/// </summary>
/// <remarks>
/// Relationship contract'i serbest SQL kosulu tasimaz. Logical source, approved kolon,
/// cardinality, runtime ve JOIN type alanlari parser veya veritabani kesfi gerektirmeden
/// yukleme aninda dogrulanir.
/// </remarks>
public static partial class AllowListLoader
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        // Bilinmeyen alan REDDEDILIR: "deniedColumn" gibi bir yazim hatasinin sessizce bos
        // listeye donusmesi, PII kontrolunun tamamen devre disi kalmasi anlamina gelirdi.
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        AllowTrailingCommas = false,
        ReadCommentHandling = JsonCommentHandling.Disallow
    };

    static AllowListLoader()
    {
        SerializerOptions.Converters.Add(
            new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
    }

    public static AllowListDocument FromJson(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        AllowListDocument? document;
        try
        {
            document = JsonSerializer.Deserialize<AllowListDocument>(json, SerializerOptions);
        }
        catch (JsonException exception)
        {
            throw new CatalogValidationException($"Allow-list okunamadi: {exception.Message}");
        }

        if (document is null)
        {
            throw new CatalogValidationException("Allow-list bos.");
        }

        Validate(document);
        return document;
    }

    private static void Validate(AllowListDocument document)
    {
        var errors = new List<string>();

        if (document.Objects.Count == 0)
        {
            errors.Add("En az bir izinli obje tanimlanmalidir; bos allow-list her sorguyu reddeder.");
        }

        ValidateLimits(document, errors);

        foreach (var (objectName, allowed) in document.Objects)
        {
            ValidateObject(document, objectName, allowed, errors);
        }

        ValidatePhysicalNames(document, errors);
        ValidateScenarioObjects(document, errors);
        ValidateRelationshipIds(document, errors);

        ValidateIdentityColumns(document, errors);

        if (errors.Count > 0)
        {
            throw new CatalogValidationException(
                "Allow-list gecersiz:" + System.Environment.NewLine +
                string.Join(System.Environment.NewLine, errors.Select(error => "  - " + error)));
        }
    }

    private static void ValidateLimits(AllowListDocument document, List<string> errors)
    {
        if (document.MaxJoins < 0)
        {
            errors.Add($"maxJoins negatif olamaz: {document.MaxJoins}");
        }

        if (document.MaxRows <= 0)
        {
            errors.Add($"maxRows pozitif olmalidir: {document.MaxRows}");
        }

        if (document.DefaultRows <= 0 || document.DefaultRows > document.MaxRows)
        {
            errors.Add(
                $"defaultRows 1 ile maxRows ({document.MaxRows}) arasinda olmalidir: {document.DefaultRows}");
        }

        if (document.QueryTimeoutSeconds <= 0)
        {
            errors.Add($"queryTimeoutSeconds pozitif olmalidir: {document.QueryTimeoutSeconds}");
        }

        if (document.MaxDateRangeDays <= 0)
        {
            errors.Add($"maxDateRangeDays pozitif olmalidir: {document.MaxDateRangeDays}");
        }

        if (document.MaxInputLength <= 0)
        {
            errors.Add($"maxInputLength pozitif olmalidir: {document.MaxInputLength}");
        }

        if (document.MinCellSize < 1)
        {
            errors.Add($"minCellSize en az 1 olmalidir: {document.MinCellSize}");
        }
    }

    private static void ValidateObject(
        AllowListDocument document,
        string objectName,
        AllowedObject allowed,
        List<string> errors)
    {
        // Obje ve kolon adlari AST'ye yazilacak. Allow-list'e "[x]; DROP TABLE y" gibi bir ad
        // yazilmasi, guardrail'in kendi eliyle enjeksiyon uretmesi demek olurdu.
        if (!IsValidObjectName(objectName))
        {
            errors.Add($"Gecersiz obje adi: '{objectName}'. Yalnizca [sema.]ad bicimi kabul edilir.");
        }

        if (allowed.Columns.Count == 0)
        {
            errors.Add($"'{objectName}' icin izinli kolon listesi bos.");
        }

        foreach (var column in allowed.Columns.Where(column => !IsValidIdentifier(column)))
        {
            errors.Add($"'{objectName}' icinde gecersiz kolon adi: '{column}'.");
        }

        foreach (var column in allowed.StableOrderColumns)
        {
            if (!IsValidIdentifier(column) || !allowed.HasColumn(column))
            {
                errors.Add(
                    $"'{objectName}' stableOrderColumns degeri izinli bir kolon olmalidir: '{column}'.");
            }
        }

        // Ayni kolonun hem izinli hem yasakli olmasi belirsizlik yaratir. Hangisinin kazandigini
        // varsaymak yerine yapilandirma hatasi olarak reddediyoruz.
        foreach (var conflicting in allowed.Columns.Where(document.IsDeniedColumn))
        {
            errors.Add(
                $"'{objectName}.{conflicting}' hem izinli kolon listesinde hem deniedColumns'da. " +
                "Belirsizlik birakilmaz; kolonu izinli listeden cikarin.");
        }

        ValidateScopeColumn(objectName, allowed, errors);
        ValidateJoinPaths(document, objectName, allowed, errors);
    }

    private static void ValidatePhysicalNames(
        AllowListDocument document,
        List<string> errors)
    {
        foreach (var (logicalName, allowed) in document.Objects)
        {
            if (allowed.PhysicalName is null)
            {
                continue;
            }

            if (!IsValidObjectName(allowed.PhysicalName)
                || !allowed.PhysicalName.Contains('.', StringComparison.Ordinal))
            {
                errors.Add(
                    $"'{logicalName}' physicalName degeri schema-qualified ve sabit bir obje adi olmalidir: '{allowed.PhysicalName}'.");
            }
        }

        foreach (var duplicate in document.Objects
            .Select(entry => entry.Value.PhysicalName ?? entry.Key)
            .GroupBy(name => name, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1))
        {
            errors.Add($"Ayni fiziksel obje birden fazla logical ada baglanamaz: '{duplicate.Key}'.");
        }
    }

    private static void ValidateScenarioObjects(
        AllowListDocument document,
        List<string> errors)
    {
        foreach (var (objectName, scenarioObject) in document.ScenarioObjects)
        {
            if (!IsValidObjectName(objectName)
                || !objectName.Contains('.', StringComparison.Ordinal))
            {
                errors.Add($"Ozel scenario view'i schema-qualified olmalidir: '{objectName}'.");
            }

            if (document.HasSqlObject(objectName))
            {
                errors.Add($"'{objectName}' hem genel hem ozel scenario yuzeyinde tanimli.");
            }

            if (scenarioObject.Contracts.Count == 0
                || scenarioObject.Contracts.Any(contract =>
                    string.IsNullOrWhiteSpace(contract)))
            {
                errors.Add($"'{objectName}' en az bir acik scenario/metric contract'i tasimalidir.");
            }
        }
    }

    private static void ValidateScopeColumn(string objectName, AllowedObject allowed, List<string> errors)
    {
        if (allowed.IsScopeExempt)
        {
            if (allowed.ScopeColumn is not null)
            {
                errors.Add(
                    $"'{objectName}' hem scopeExempt hem scopeColumn tanimliyor. " +
                    "Muafiyet ile kapsam kolonu birlikte kullanilamaz.");
            }

            return;
        }

        if (string.IsNullOrWhiteSpace(allowed.ScopeColumn))
        {
            // Sessiz muafiyet yok: kapsam kolonu olmayan bir obje acikca scopeExempt
            // isaretlenmelidir. Aksi halde filtresiz sorgulanabilir bir obje olusur.
            errors.Add(
                $"'{objectName}' icin scopeColumn tanimli degil. Kapsam filtresi uygulanamayacak " +
                "objeler acikca scopeExempt=true olarak isaretlenmelidir.");
            return;
        }

        if (!allowed.HasColumn(allowed.ScopeColumn))
        {
            errors.Add(
                $"'{objectName}' icin scopeColumn '{allowed.ScopeColumn}' izinli kolon listesinde yok. " +
                "Kapsam filtresi var olmayan bir kolona uygulanamaz.");
        }
    }

    private static void ValidateJoinPaths(
        AllowListDocument document,
        string objectName,
        AllowedObject allowed,
        List<string> errors)
    {
        foreach (var joinPath in allowed.JoinPaths)
        {
            var target = document.FindObject(joinPath.To);
            if (target is null)
            {
                errors.Add(
                    $"'{objectName}' icin tanimli JOIN hedefi '{joinPath.To}' allow-list'te yok.");
                continue;
            }

            if (!IsValidRelationshipId(joinPath.Id))
            {
                errors.Add($"'{objectName}' relationship id gecersiz: '{joinPath.Id}'.");
            }

            if (!IsValidIdentifier(joinPath.LeftColumn)
                || !allowed.HasColumn(joinPath.LeftColumn))
            {
                errors.Add(
                    $"'{joinPath.Id}' relationship sol JOIN kolonu '{objectName}.{joinPath.LeftColumn}' izinli degil.");
            }

            if (!IsValidIdentifier(joinPath.RightColumn)
                || !target.HasColumn(joinPath.RightColumn))
            {
                errors.Add(
                    $"'{joinPath.Id}' relationship sag JOIN kolonu '{joinPath.To}.{joinPath.RightColumn}' izinli degil.");
            }

            if (joinPath.LeftRuntime != joinPath.RightRuntime)
            {
                errors.Add(
                    $"'{joinPath.Id}' relationship runtime sinirini asiyor: " +
                    $"'{joinPath.LeftRuntime}' -> '{joinPath.RightRuntime}'.");
            }

            if (joinPath.AllowedJoinTypes.Count == 0)
            {
                errors.Add($"'{joinPath.Id}' relationship en az bir izinli JOIN tipi tasimalidir.");
            }

            if (joinPath.AllowedJoinTypes.Count != joinPath.AllowedJoinTypes.Distinct().Count())
            {
                errors.Add($"'{joinPath.Id}' relationship JOIN tipleri benzersiz olmalidir.");
            }
        }

        var duplicateTargets = allowed.JoinPaths
            .GroupBy(joinPath => joinPath.To, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key);

        foreach (var duplicate in duplicateTargets)
        {
            errors.Add(
                $"'{objectName}' -> '{duplicate}' icin birden fazla JOIN yolu tanimli. " +
                "Hangisinin kullanilacagi belirsiz kalmamalidir.");
        }
    }

    private static void ValidateRelationshipIds(
        AllowListDocument document,
        List<string> errors)
    {
        foreach (var duplicate in document.Objects.Values
            .SelectMany(allowed => allowed.JoinPaths)
            .GroupBy(path => path.Id, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1))
        {
            errors.Add($"Relationship id benzersiz degil: '{duplicate.Key}'.");
        }
    }

    private static void ValidateIdentityColumns(AllowListDocument document, List<string> errors)
    {
        foreach (var column in document.IdentityColumns.Where(column => !IsValidIdentifier(column)))
        {
            errors.Add($"Gecersiz kimlik kolonu adi: '{column}'.");
        }

        foreach (var column in document.DeniedColumns.Where(column => !IsValidIdentifier(column)))
        {
            errors.Add($"Gecersiz yasakli kolon adi: '{column}'.");
        }
    }

    private static bool IsValidIdentifier(string value) =>
        !string.IsNullOrWhiteSpace(value) && IdentifierPattern().IsMatch(value);

    private static bool IsValidObjectName(string value) =>
        !string.IsNullOrWhiteSpace(value) && ObjectNamePattern().IsMatch(value);

    internal static bool IsValidRelationshipId(string value) =>
        !string.IsNullOrWhiteSpace(value) && RelationshipIdPattern().IsMatch(value);

    // Kasitli olarak dar: yalnizca duz identifier. Koseli parantez, tirnak, bosluk, noktali
    // virgul ve cok parcali ad (sema disinda) kabul edilmez.
    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex IdentifierPattern();

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]{0,127}(\\.[A-Za-z_][A-Za-z0-9_]{0,127})?$", RegexOptions.CultureInvariant)]
    private static partial Regex ObjectNamePattern();

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_.-]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex RelationshipIdPattern();
}
