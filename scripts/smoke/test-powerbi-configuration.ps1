[CmdletBinding()]
param(
    [string]$WorkspaceId = $env:POWERBI_WORKSPACE_ID,
    [string]$ReportId = $env:POWERBI_REPORT_ID,
    [string]$AccessToken = $env:POWERBI_ACCESS_TOKEN
)

$ErrorActionPreference = 'Stop'
$parsed = [guid]::Empty
if (-not [guid]::TryParse($WorkspaceId, [ref]$parsed) -or
    -not [guid]::TryParse($ReportId, [ref]$parsed)) {
    Write-Output 'BLOCKED: valid POWERBI_WORKSPACE_ID and POWERBI_REPORT_ID are required.'
    exit 2
}
if ([string]::IsNullOrWhiteSpace($AccessToken)) {
    Write-Output 'SKIPPED: IDs are structurally valid; POWERBI_ACCESS_TOKEN is absent, so no real API test ran.'
    exit 3
}

$uri = "https://api.powerbi.com/v1.0/myorg/groups/$WorkspaceId/reports/$ReportId"
$report = Invoke-RestMethod -Method Get -Uri $uri -Headers @{ Authorization = "Bearer $AccessToken" }
$safe = [uri]::IsWellFormedUriString($report.webUrl, [UriKind]::Absolute) -and
    ([uri]$report.webUrl).Scheme -eq 'https' -and ([uri]$report.webUrl).Host -eq 'app.powerbi.com'
if (-not $safe -or $report.webUrl -match '(?i)(access_token|token=|sig=)') {
    throw 'FAIL: Power BI returned an unsafe webUrl.'
}
Write-Output 'PASS: configured Power BI report returned a safe webUrl.'
