[CmdletBinding()]
param(
    [string]$WorkspaceId = $env:FABRIC_WORKSPACE_ID,
    [string]$ItemId = $env:FABRIC_ITEM_ID,
    [string]$JobType = $env:FABRIC_JOB_TYPE
)

$parsed = [guid]::Empty
if (-not [guid]::TryParse($WorkspaceId, [ref]$parsed) -or
    -not [guid]::TryParse($ItemId, [ref]$parsed) -or
    [string]::IsNullOrWhiteSpace($JobType)) {
    Write-Output 'BLOCKED: valid FABRIC_WORKSPACE_ID, FABRIC_ITEM_ID, and FABRIC_JOB_TYPE are required.'
    exit 2
}

Write-Output 'BLOCKED: IDs are structurally valid, but no approved result-staging and job-parameter contract exists; the Fabric job was not triggered.'
exit 2
