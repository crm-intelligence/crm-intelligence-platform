[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory)]
    [string] $KeyVaultName,

    [Parameter(Mandatory)]
    [string] $ApplicationDbValueFile,

    [Parameter(Mandatory)]
    [string] $QueryDwhValueFile,

    [string] $QueryOltpValueFile,

    [Parameter(Mandatory)]
    [string] $InternalApiKeyValueFile,

    [Parameter(Mandatory)]
    [string] $TeamsClientSecretValueFile,

    [switch] $Apply
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$secretFiles = [ordered]@{
    'crm-analytics-application-db' = $ApplicationDbValueFile
    'crm-analytics-query-dwh' = $QueryDwhValueFile
    'crm-analytics-internal-api-key' = $InternalApiKeyValueFile
    'crm-analytics-teams-client-secret' = $TeamsClientSecretValueFile
}

if (-not [string]::IsNullOrWhiteSpace($QueryOltpValueFile)) {
    $secretFiles['crm-analytics-query-oltp'] = $QueryOltpValueFile
}

$repositoryRoot = [IO.Path]::GetFullPath(
    (Join-Path $PSScriptRoot '..\..'))
$resolvedFiles = [ordered]@{}

foreach ($entry in $secretFiles.GetEnumerator()) {
    $resolvedPath = (Resolve-Path -LiteralPath $entry.Value).Path
    $relativePath = [IO.Path]::GetRelativePath(
        $repositoryRoot,
        $resolvedPath)
    $isInsideRepository = -not $relativePath.StartsWith(
        '..',
        [StringComparison]::Ordinal) -and
        -not [IO.Path]::IsPathRooted($relativePath)

    if ($isInsideRepository) {
        throw "Secret value files must be outside the repository: $resolvedPath"
    }

    $resolvedFiles[$entry.Key] = $resolvedPath
}

Write-Host "Key Vault: $KeyVaultName"
Write-Host 'Secret names to prepare:'
$resolvedFiles.Keys | ForEach-Object { Write-Host "  - $_" }

if (-not $Apply) {
    Write-Host 'Preview only. No Azure command was run.'
    Write-Host 'Review the names, then rerun with -Apply (and optionally -WhatIf).'
    return
}

foreach ($entry in $resolvedFiles.GetEnumerator()) {
    if ($PSCmdlet.ShouldProcess(
            "$KeyVaultName/$($entry.Key)",
            'Create a new Key Vault secret version from an external file')) {
        & az keyvault secret set `
            --vault-name $KeyVaultName `
            --name $entry.Key `
            --file $entry.Value `
            --encoding utf-8 `
            --output none

        if ($LASTEXITCODE -ne 0) {
            throw "Azure CLI failed while setting secret '$($entry.Key)'."
        }
    }
}

Write-Host 'Secret preparation completed. No secret values were printed.'
