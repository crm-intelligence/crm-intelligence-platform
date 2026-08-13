using System.Security.Cryptography;
using System.Text;

namespace CrmAnalytics.Infrastructure.Integrations;

public sealed record EvaluationBuildParityResult(
    bool IsMatch,
    string ExpectedFingerprint,
    string RepositoryFingerprint,
    string? FailureKind);

public static class EvaluationBuildParity
{
    private static readonly HashSet<string> IncludedExtensions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ".cs", ".csproj", ".json", ".props", ".targets", ".ps1"
        };

    private static readonly string[] IncludedRoots =
    [
        "src",
        "tools/CrmAnalytics.OllamaSmoke"
    ];

    public static EvaluationBuildParityResult Verify(
        string repositoryRoot,
        string expectedFingerprint)
    {
        var actual = ComputeRepositoryFingerprint(repositoryRoot);
        return new EvaluationBuildParityResult(
            string.Equals(expectedFingerprint, actual, StringComparison.Ordinal),
            expectedFingerprint,
            actual,
            string.Equals(expectedFingerprint, actual, StringComparison.Ordinal)
                ? null : "StaleEvaluationBinary");
    }

    public static string ComputeRepositoryFingerprint(string repositoryRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        var root = Path.GetFullPath(repositoryRoot);
        var manifest = IncludedRoots
            .Select(relative => Path.Combine(root,
                relative.Replace('/', Path.DirectorySeparatorChar)))
            .Where(Directory.Exists)
            .SelectMany(directory => Directory.EnumerateFiles(
                directory, "*", SearchOption.AllDirectories))
            .Where(path => IncludedExtensions.Contains(Path.GetExtension(path)))
            .Where(path => !HasBuildSegment(path))
            .Select(path => new
            {
                Relative = Path.GetRelativePath(root, path).Replace('\\', '/'),
                Hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))
                    .ToLowerInvariant()
            })
            .OrderBy(item => item.Relative, StringComparer.Ordinal)
            .Select(item => $"{item.Relative}|{item.Hash}");
        var content = string.Join('\n', manifest);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content)))
            .ToLowerInvariant();
    }

    public static string FindRepositoryRoot(string startPath)
    {
        var directory = new DirectoryInfo(Path.GetFullPath(startPath));
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "CrmAnalytics.slnx"))
                && Directory.Exists(Path.Combine(directory.FullName, "src"))
                && Directory.Exists(Path.Combine(directory.FullName,
                    "tools", "CrmAnalytics.OllamaSmoke")))
            {
                return directory.FullName;
            }
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException(
            "Evaluation repository root could not be located.");
    }

    private static bool HasBuildSegment(string path)
    {
        var segments = path.Split(Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);
        return segments.Any(segment => segment.Equals("bin",
                StringComparison.OrdinalIgnoreCase)
            || segment.Equals("obj", StringComparison.OrdinalIgnoreCase));
    }
}
