[CmdletBinding()]
param([string]$BaseUrl = $env:CRM_API_BASE_URL)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($BaseUrl)) {
    Write-Output 'BLOCKED: CRM_API_BASE_URL is not set.'
    exit 2
}

foreach ($path in @('/health/live', '/health/ready')) {
    $response = Invoke-RestMethod -Method Get -Uri ($BaseUrl.TrimEnd('/') + $path)
    if ($response.status -ne 'Healthy') {
        throw "FAIL: $path did not report Healthy."
    }
}
Write-Output 'PASS: API liveness and readiness are healthy.'
