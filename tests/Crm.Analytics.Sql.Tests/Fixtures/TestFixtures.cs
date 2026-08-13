using System.Reflection;

namespace Crm.Analytics.Sql.Tests.Fixtures;

/// <summary>
/// Test fixture dosyalarina erisim. Dosyalar gomulu kaynak olarak tasinir; bin klasoru
/// kopyalama davranisina veya calisma dizinine bagimli test yazmiyoruz.
/// </summary>
internal static class TestFixtures
{
    private const string AllowListName = "Crm.Analytics.Sql.Tests.Fixtures.allowlist.test.json";

    public static string ReadAllowListJson() => Read(AllowListName);

    private static string Read(string resourceName)
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException(
                $"Fixture bulunamadi: {resourceName}. " +
                $"Mevcut kaynaklar: {string.Join(", ", assembly.GetManifestResourceNames())}");

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
