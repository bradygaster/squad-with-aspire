#Requires -Version 7.2
<#
.SYNOPSIS
  Canary smoke test for checkout-api per SEC-CHK-008 R6 / GATE-CO-06b-canary.

.DESCRIPTION
  Polls Application Insights / Log Analytics for canary samples in the configured
  window. Fails (nonzero exit) if sample count is insufficient OR p99 latency
  exceeds floor. Wired into .github/workflows/checkout-canary-promote.yml by
  review-deployment squad (commit 6070f3f).

.PARAMETER WorkspaceId
  Log Analytics workspace GUID. Required.

.PARAMETER Stage
  Canary stage label, e.g. "canary-1", "canary-10", "canary-50", "prod-100".
  Required. Matched against custom dim deploymentStage.

.PARAMETER MinSamples
  Minimum request samples required in window. Default 40.

.PARAMETER WindowMinutes
  Trailing time window (minutes). Default 10.

.PARAMETER P99FloorMs
  p99 latency floor in milliseconds. Failure if observed p99 exceeds this.
  Default 1000 (= FloorMs(800) + buffer(200) per SEC-CHK-008 R6).

.EXIT CODES
  0 = pass (sample count met AND p99 within floor)
  1 = insufficient samples
  2 = p99 latency regression
  3 = Log Analytics query failure
  4 = invalid input

.NOTES
  Skips dark stage (no live traffic). Caller (workflow) gates that.
  Fail-closed by design — any unexpected condition triggers rollback.
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$WorkspaceId,
    [Parameter(Mandatory)][string]$Stage,
    [int]$MinSamples = 40,
    [int]$WindowMinutes = 10,
    [int]$P99FloorMs = 1000
)

$ErrorActionPreference = 'Stop'

function Write-Result {
    param([string]$Level, [string]$Message)
    $ts = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")
    Write-Host "[$ts][$Level] $Message"
}

if ([string]::IsNullOrWhiteSpace($WorkspaceId)) {
    Write-Result "ERROR" "WorkspaceId is empty."
    exit 4
}
if ([string]::IsNullOrWhiteSpace($Stage)) {
    Write-Result "ERROR" "Stage is empty."
    exit 4
}
if ($MinSamples -lt 1 -or $WindowMinutes -lt 1 -or $P99FloorMs -lt 1) {
    Write-Result "ERROR" "Numeric parameters must be positive integers."
    exit 4
}

Write-Result "INFO" "Canary smoke start. Stage=$Stage Workspace=$WorkspaceId MinSamples=$MinSamples WindowMinutes=$WindowMinutes P99FloorMs=$P99FloorMs"

# KQL: checkout-api requests in window, scoped to stage. p99 in ms.
$kql = @"
requests
| where timestamp > ago(${WindowMinutes}m)
| where cloud_RoleName == 'checkout-api'
| where customDimensions.deploymentStage == '$Stage'
| where url has '/api/checkout/' or url has '/api/cart/'
| summarize SampleCount = count(), P99Ms = percentile(duration, 99)
"@

Write-Result "INFO" "Querying Log Analytics..."
Write-Verbose $kql

try {
    Import-Module Az.OperationalInsights -ErrorAction Stop | Out-Null
} catch {
    Write-Result "ERROR" "Az.OperationalInsights module not available: $($_.Exception.Message)"
    exit 3
}

try {
    $result = Invoke-AzOperationalInsightsQuery -WorkspaceId $WorkspaceId -Query $kql -ErrorAction Stop
} catch {
    Write-Result "ERROR" "Log Analytics query failed: $($_.Exception.Message)"
    exit 3
}

if ($null -eq $result -or $null -eq $result.Results -or $result.Results.Count -eq 0) {
    Write-Result "ERROR" "Query returned no result rows. Treating as insufficient signal (fail-closed)."
    exit 1
}

$row = $result.Results[0]
$sampleCount = [int]($row.SampleCount)
$p99Raw = $row.P99Ms

if ([string]::IsNullOrWhiteSpace($p99Raw)) {
    Write-Result "ERROR" "P99Ms field empty in query result (likely zero samples). SampleCount=$sampleCount"
    exit 1
}
$p99 = [double]$p99Raw

Write-Result "INFO" "Observed: SampleCount=$sampleCount P99Ms=$([math]::Round($p99,2))"

if ($sampleCount -lt $MinSamples) {
    Write-Result "FAIL" "Insufficient samples: $sampleCount < $MinSamples. Triggering rollback (exit 1)."
    exit 1
}

if ($p99 -gt $P99FloorMs) {
    Write-Result "FAIL" "p99 latency regression: $([math]::Round($p99,2))ms > ${P99FloorMs}ms floor. Triggering rollback (exit 2)."
    exit 2
}

Write-Result "PASS" "Canary smoke ok. Stage=$Stage SampleCount=$sampleCount P99Ms=$([math]::Round($p99,2)) (floor ${P99FloorMs}ms)."
exit 0
