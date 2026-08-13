param(
    [Parameter(Mandatory = $true)][string]$RepositoryRoot,
    [Parameter(Mandatory = $true)][string]$OutputFile,
    [Parameter(Mandatory = $true)][string]$BuildConfiguration
)

$ErrorActionPreference = 'Stop'
$RepositoryRoot = (Resolve-Path -LiteralPath $RepositoryRoot).Path.TrimEnd('\', '/')
$extensions = @('.cs', '.csproj', '.json', '.props', '.targets', '.ps1')
$roots = @(
    'src',
    'tools/CrmAnalytics.OllamaSmoke'
)

$manifest = foreach ($relativeRoot in $roots) {
    $absoluteRoot = Join-Path $RepositoryRoot $relativeRoot
    if (-not (Test-Path -LiteralPath $absoluteRoot -PathType Container)) {
        continue
    }

    Get-ChildItem -LiteralPath $absoluteRoot -File -Recurse |
        Where-Object {
            $extensions -contains $_.Extension -and
            $_.FullName -notmatch '[\\/](bin|obj)[\\/]'
        } |
        ForEach-Object {
            $relative = $_.FullName.Substring($RepositoryRoot.Length + 1).Replace('\', '/')
            $hash = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
            "$relative|$hash"
        }
}

$manifest = [string[]]$manifest
[Array]::Sort($manifest, [StringComparer]::Ordinal)
$manifestText = $manifest -join "`n"
$bytes = [Text.Encoding]::UTF8.GetBytes($manifestText)
$sha = [Security.Cryptography.SHA256]::Create()
try {
    $fingerprint = [BitConverter]::ToString($sha.ComputeHash($bytes)).Replace('-', '').ToLowerInvariant()
}
finally {
    $sha.Dispose()
}

$timestamp = [DateTime]::UtcNow.ToString('O')
$source = @"
namespace CrmAnalytics.OllamaSmoke;

internal static class GeneratedEvaluationBuild
{
    internal const string RepositoryFingerprint = "$fingerprint";
    internal const string BuildTimestampUtc = "$timestamp";
    internal const string Configuration = "$BuildConfiguration";
}
"@

$directory = Split-Path -Parent $OutputFile
New-Item -ItemType Directory -Force -Path $directory | Out-Null
[IO.File]::WriteAllText($OutputFile, $source, [Text.UTF8Encoding]::new($false))
