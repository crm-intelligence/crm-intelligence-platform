[CmdletBinding()]
param(
    [string]$BaseUrl = $env:CRM_API_BASE_URL,
    [string]$Token = $env:CRM_API_TOKEN,
    [string]$Prompt = $env:CRM_SMOKE_PROMPT
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($BaseUrl) -or
    [string]::IsNullOrWhiteSpace($Token) -or
    [string]::IsNullOrWhiteSpace($Prompt)) {
    Write-Output 'BLOCKED: CRM_API_BASE_URL, CRM_API_TOKEN, and CRM_SMOKE_PROMPT are required.'
    exit 2
}

$headers = @{ Authorization = "Bearer $Token" }
$body = @{ prompt = $Prompt; conversationId = "smoke-$([guid]::NewGuid())" } |
    ConvertTo-Json
$created = Invoke-RestMethod -Method Post -Uri ($BaseUrl.TrimEnd('/') + '/api/report-requests') `
    -Headers $headers -ContentType 'application/json' -Body $body
if ([string]::IsNullOrWhiteSpace($created.requestId)) { throw 'FAIL: API returned no request ID.' }

$deadline = [DateTimeOffset]::UtcNow.AddMinutes(10)
do {
    Start-Sleep -Seconds 5
    $report = Invoke-RestMethod -Method Get `
        -Uri ($BaseUrl.TrimEnd('/') + '/api/report-requests/' + [uri]::EscapeDataString($created.requestId)) `
        -Headers $headers
    if ($report.status -in @('Completed', 'Failed', 'Rejected', 'WaitingForClarification')) { break }
} while ([DateTimeOffset]::UtcNow -lt $deadline)

if ($report.status -ne 'Completed') {
    throw "FAIL: report flow ended as '$($report.status)'; this is not reported as a pass."
}
if ($report.powerBiUrl -notmatch '^https://app\.powerbi\.com/') {
    throw 'FAIL: Completed report did not contain a safe Power BI web URL.'
}
if ($report.powerBiUrl -match '(?i)(access_token|token=|sig=)') {
    throw 'FAIL: Power BI URL appears to contain credential material.'
}
Write-Output "PASS: report $($created.requestId) completed with a safe Power BI URL."
