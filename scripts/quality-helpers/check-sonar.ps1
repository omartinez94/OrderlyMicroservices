<#
.SYNOPSIS
    Conditional SonarQube / SonarCloud analysis runner.

.DESCRIPTION
    Runs a client-side `dotnet-sonarscanner` analysis when — and only when — the
    environment is fully configured for it. Plan §3 puts hosting or provisioning
    a SonarQube server explicitly OUT OF SCOPE, so this script never attempts to
    start one; it is the client half only.

    The analysis is SKIPPED (exit 0, with the reason printed) when any of these
    is missing:
      * $env:SONAR_HOST_URL   — the server to report to
      * $env:SONAR_TOKEN      — the auth token
      * the `dotnet-sonarscanner` tool on PATH or in the local tool manifest

    That makes it inert on a developer workstation with no Sonar server and
    active in CI once the secrets are provided, which is the intended split.

    A real run is EXPENSIVE: sonarscanner requires `begin` -> full rebuild ->
    `end`, because it collects Roslyn output during compilation. It therefore
    honours -Quick by skipping entirely (plan §10: heavy checks must not run in
    the fast path).

    Phase 3 of .agents/plan/platform/QUALITY_GATE_ENHANCEMENT_PLAN.md —
    Static Analysis, Sonar & Security Checks (plan §9 Phase 3).

.PARAMETER Quick
    Skip the analysis. phase-guard.ps1 forwards its own -Quick switch here.

.PARAMETER ProjectKey
    Sonar project key. Defaults to $env:SONAR_PROJECT_KEY, then 'orderly'.

.PARAMETER Solution
    Solution to analyse. Defaults to orderly-microservices/orderly-microservices.slnx.

.EXAMPLE
    pwsh ./scripts/quality-helpers/check-sonar.ps1
    # Skips with a reason unless SONAR_HOST_URL + SONAR_TOKEN + scanner exist.

.EXAMPLE
    $env:SONAR_HOST_URL='https://sonarcloud.io'; $env:SONAR_TOKEN='...'
    pwsh ./scripts/quality-helpers/check-sonar.ps1
    # Runs begin -> rebuild -> end.

.NOTES
    Exit codes:
        0 — analysis passed, or was skipped because it is not configured
        1 — analysis ran and failed (quality gate failed, or scanner errored)

    Install the scanner with:
        dotnet tool install --global dotnet-sonarscanner

    Style follows scripts/quality-helpers/find-secrets.ps1.
#>

[CmdletBinding()]
param(
    [switch]$Quick,
    [string]$ProjectKey,
    [string]$Solution
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$script:Tag = '[check-sonar]'

function Write-Skip {
    param([string]$Reason)
    Write-Host "$script:Tag SKIPPED — $Reason" -ForegroundColor DarkGray
}

$repoRoot = (Resolve-Path "$PSScriptRoot/../..").Path

if ([string]::IsNullOrWhiteSpace($Solution)) {
    $Solution = Join-Path $repoRoot 'orderly-microservices/orderly-microservices.slnx'
}
if ([string]::IsNullOrWhiteSpace($ProjectKey)) {
    $ProjectKey = if ($env:SONAR_PROJECT_KEY) { $env:SONAR_PROJECT_KEY } else { 'orderly' }
}

# ----------------------------------------------------------------
# Preconditions — every one of these is a legitimate reason to skip.
# ----------------------------------------------------------------

if ($Quick) {
    Write-Skip 'running with -Quick; Sonar requires a full rebuild (plan §10).'
    exit 0
}

if ([string]::IsNullOrWhiteSpace($env:SONAR_HOST_URL)) {
    Write-Skip 'SONAR_HOST_URL is not set (plan §3: hosting a Sonar server is out of scope).'
    exit 0
}

if ([string]::IsNullOrWhiteSpace($env:SONAR_TOKEN)) {
    Write-Skip 'SONAR_TOKEN is not set.'
    exit 0
}

$scanner = Get-Command 'dotnet-sonarscanner' -ErrorAction SilentlyContinue
if ($null -eq $scanner) {
    # The tool may also be available as a local tool.
    $localTool = & dotnet tool run dotnet-sonarscanner --version 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Skip "dotnet-sonarscanner not found. Install with 'dotnet tool install --global dotnet-sonarscanner'."
        exit 0
    }
}

# ----------------------------------------------------------------
# Run
# ----------------------------------------------------------------

Write-Host "$script:Tag host:    $env:SONAR_HOST_URL"
Write-Host "$script:Tag project: $ProjectKey"

Push-Location $repoRoot
try {
    Write-Host "$script:Tag begin..."
    & dotnet sonarscanner begin `
        /k:"$ProjectKey" `
        /d:sonar.host.url="$env:SONAR_HOST_URL" `
        /d:sonar.token="$env:SONAR_TOKEN" `
        /d:sonar.cs.opencover.reportsPaths="**/coverage.opencover.xml" `
        /d:sonar.scanner.scanAll=false
    if ($LASTEXITCODE) { throw 'sonarscanner begin failed' }

    # Sonar needs a real compilation to observe; --no-incremental guarantees
    # every project is actually recompiled under the scanner's MSBuild hooks.
    Write-Host "$script:Tag rebuilding under scanner..."
    & dotnet build "$Solution" --no-incremental --no-restore
    if ($LASTEXITCODE) { throw 'build under sonarscanner failed' }

    Write-Host "$script:Tag end (uploading + waiting for quality gate)..."
    & dotnet sonarscanner end /d:sonar.token="$env:SONAR_TOKEN"
    if ($LASTEXITCODE) { throw 'sonarscanner end failed (quality gate may have failed)' }
}
catch {
    Write-Host "$script:Tag $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}
finally {
    Pop-Location
}

Write-Host "$script:Tag analysis complete." -ForegroundColor Green
exit 0
