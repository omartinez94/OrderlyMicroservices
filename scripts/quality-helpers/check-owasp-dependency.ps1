<#
.SYNOPSIS
    Conditional OWASP Dependency-Check runner (CVE scan of third-party packages).

.DESCRIPTION
    Runs the OWASP Dependency-Check CLI via its official Docker image against the
    repository, producing an HTML + JSON report and failing when a finding meets
    or exceeds $FailOnCvss.

    This complements — it does not replace — `dotnet list package --vulnerable`,
    which phase-guard already runs. That command only knows about advisories in
    the NuGet feed's own database; Dependency-Check matches file evidence against
    the full NVD, so it also catches vendored binaries and transitive natives.

    The scan is SKIPPED (exit 0, with the reason printed) when:
      * -Quick is set — a first run downloads the entire NVD database and takes
        tens of minutes, which is exactly what plan §10 says must stay out of the
        fast path.
      * Docker is unavailable.
      * $env:NVD_API_KEY is unset. Since 2023 the NVD rate-limits unauthenticated
        clients so aggressively that an un-keyed run takes hours and usually times
        out. Requiring the key makes the skip explicit instead of hanging the gate.

    The NVD database is cached in a named Docker volume ($DataVolume) so only the
    first run pays the download cost.

    Phase 3 of .agents/plan/platform/QUALITY_GATE_ENHANCEMENT_PLAN.md —
    Static Analysis, Sonar & Security Checks (plan §9 Phase 3).

.PARAMETER Quick
    Skip the scan. phase-guard.ps1 forwards its own -Quick switch here.

.PARAMETER FailOnCvss
    Fail when any finding has a CVSS score >= this value. Default 7.0 (High).

.PARAMETER OutputDirectory
    Where reports are written. Defaults to <repo>/artifacts/dependency-check.

.PARAMETER DataVolume
    Docker named volume caching the NVD database. Default 'orderly-dc-data'.

.EXAMPLE
    pwsh ./scripts/quality-helpers/check-owasp-dependency.ps1
    # Skips unless docker + NVD_API_KEY are present.

.EXAMPLE
    $env:NVD_API_KEY='...'
    pwsh ./scripts/quality-helpers/check-owasp-dependency.ps1 -FailOnCvss 9.0
    # Only fail on Critical findings.

.NOTES
    Exit codes:
        0 — no finding at/above the threshold, or the scan was skipped
        1 — a finding met the threshold, or the scanner errored

    Get a free NVD API key at https://nvd.nist.gov/developers/request-an-api-key

    Style follows scripts/quality-helpers/find-secrets.ps1.
#>

[CmdletBinding()]
param(
    [switch]$Quick,
    [double]$FailOnCvss = 7.0,
    [string]$OutputDirectory,
    [string]$DataVolume = 'orderly-dc-data'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$script:Tag = '[check-owasp]'

function Write-Skip {
    param([string]$Reason)
    Write-Host "$script:Tag SKIPPED — $Reason" -ForegroundColor DarkGray
}

$repoRoot = (Resolve-Path "$PSScriptRoot/../..").Path
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $repoRoot 'artifacts/dependency-check'
}

# ----------------------------------------------------------------
# Preconditions
# ----------------------------------------------------------------

if ($Quick) {
    Write-Skip 'running with -Quick; the NVD sync is far too slow for the fast path (plan §10).'
    exit 0
}

$docker = Get-Command 'docker' -ErrorAction SilentlyContinue
if ($null -eq $docker) {
    Write-Skip 'docker not found on PATH.'
    exit 0
}

& docker info *> $null
if ($LASTEXITCODE -ne 0) {
    Write-Skip 'docker daemon is not running.'
    exit 0
}

if ([string]::IsNullOrWhiteSpace($env:NVD_API_KEY)) {
    Write-Skip 'NVD_API_KEY is not set — an un-keyed NVD sync is rate-limited to the point of hanging. Request a free key at https://nvd.nist.gov/developers/request-an-api-key'
    exit 0
}

# ----------------------------------------------------------------
# Run
# ----------------------------------------------------------------

New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null

Write-Host "$script:Tag scanning $repoRoot"
Write-Host "$script:Tag reports -> $OutputDirectory"
Write-Host "$script:Tag failing at CVSS >= $FailOnCvss"
Write-Host "$script:Tag (first run downloads the NVD database; subsequent runs reuse volume '$DataVolume')"

# --disableAssembly: the .NET assembly analyzer needs a runtime inside the
# container that the image does not ship; without it every scan logs a noisy
# recoverable error. NuGet/packages.config/MSBuild analyzers still run.
& docker run --rm `
    -e "NVD_API_KEY=$env:NVD_API_KEY" `
    -v "${repoRoot}:/src:ro" `
    -v "${DataVolume}:/usr/share/dependency-check/data" `
    -v "${OutputDirectory}:/report" `
    owasp/dependency-check:latest `
    --scan /src `
    --project 'Orderly' `
    --out /report `
    --format 'HTML' `
    --format 'JSON' `
    --nvdApiKey "$env:NVD_API_KEY" `
    --failOnCVSS $FailOnCvss `
    --disableAssembly

$scanExit = $LASTEXITCODE

if ($scanExit -eq 0) {
    Write-Host "$script:Tag no findings at or above CVSS $FailOnCvss." -ForegroundColor Green
    exit 0
}

# Dependency-Check exits with the --failOnCVSS code (default 1) when a finding
# meets the threshold, and with other non-zero codes on internal errors. Both
# are worth failing the gate, but the message should distinguish them.
$jsonReport = Join-Path $OutputDirectory 'dependency-check-report.json'
if (Test-Path -LiteralPath $jsonReport) {
    Write-Host "$script:Tag findings at or above CVSS $FailOnCvss — see:" -ForegroundColor Red
    Write-Host "$script:Tag   $(Join-Path $OutputDirectory 'dependency-check-report.html')" -ForegroundColor Red
} else {
    Write-Host "$script:Tag scanner exited $scanExit without producing a report (internal error)." -ForegroundColor Red
}

exit 1
