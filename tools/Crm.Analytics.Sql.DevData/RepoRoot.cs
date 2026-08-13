namespace Crm.Analytics.Sql.DevData;

/// <summary>
/// Repo kokunu bulur. Arac hangi calisma dizininden cagrilirsa cagrilsin ayni
/// sonucu uretmesi icin dizin tahminine degil, cozum dosyasinin varligina bakar.
/// </summary>
internal static class RepoRoot
{
    private const string SolutionFileName = "CrmAnalytics.slnx";

    /// <exception cref="DirectoryNotFoundException">
    /// Repo koku bulunamazsa.
    /// </exception>
    public static string Locate()
    {
        // Once calisma dizini (dotnet run genelde repo kokunden cagrilir),
        // sonra assembly konumu (bin/Debug/... altindan yukari yurunur).
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            var directory = new DirectoryInfo(start);

            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory.FullName, SolutionFileName)))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }
        }

        throw new DirectoryNotFoundException(
            $"Repo koku bulunamadi: yukari dogru hicbir dizinde {SolutionFileName} yok. " +
            "Araci repo icinden cagirin veya hedef yolu argument olarak verin.");
    }
}
