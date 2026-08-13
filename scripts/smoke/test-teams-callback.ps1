[CmdletBinding()]
param([string]$BaseUrl = $env:CRM_TEAMS_BASE_URL)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($BaseUrl)) {
    Write-Output 'BLOCKED: CRM_TEAMS_BASE_URL is not set.'
    exit 2
}

$health = Invoke-RestMethod -Method Get -Uri ($BaseUrl.TrimEnd('/') + '/health/ready')
if ($health.status -ne 'Healthy') { throw 'FAIL: Teams readiness is unhealthy.' }
try {
    Invoke-WebRequest -Method Post `
        -Uri ($BaseUrl.TrimEnd('/') + '/api/internal/report-notifications') `
        -ContentType 'application/json' -Body '{}' | Out-Null
    throw 'FAIL: unauthenticated callback was accepted.'
}
catch {
    if ($_.Exception.Response.StatusCode.value__ -ne 401) { throw }
}
Write-Output 'PASS: Teams is ready and its callback rejects unauthenticated requests.'
