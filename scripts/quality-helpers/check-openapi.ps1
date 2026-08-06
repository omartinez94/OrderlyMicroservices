<#
.SYNOPSIS
    OpenAPI contract lint for the Orderly .NET 10 microservices solution.

.DESCRIPTION
    Runs `npx --yes @stoplight/spectral-cli` against every committed OpenAPI
    spec under orderly-microservices/docs/api/, using the ruleset shipped at
    orderly-microservices/.spectral.yaml. Spectral extends the base `spectral:oas`
    ruleset and the project's only override is `oas3-api-servers: off` — the
    committed specs deliberately omit a `servers:` block because deployment
    targets are added at deploy time, not at code-emit time.

    Five services emit /openapi/v1.json (Catalog/Ordering/Kitchen/Identity/
    Discount.Grpc) and one (Basket) exposes /swagger/v1/swagger.json via
    Swashbuckle. The five committed specs in docs/api/ are the contract the
    gate enforces — runtime emitters are checked separately by
    .github/workflows/openapi-smoke.yml.

    The `--fail-severity error` flag is load-bearing. Spectral's default
    exit code is 0 unless an `error`-severity finding is emitted; without
    this flag the gate would silently accept `warn` and `info` findings.

    Phase 4 of .agents/plan/platform/QUALITY_GATE_ENHANCEMENT_PLAN.md —
    Docker, Contract & Test Coverage validation (plan §9 Phase 4).

.PARAMETER SpecDir
    Directory containing the committed OpenAPI specs (*.json / *.yaml).
    Defaults to orderly-microservices/docs/api/.

.PARAMETER Ruleset
    Path to the Spectral ruleset. Defaults to orderly-microservices/.spectral.yaml.

.PARAMETER Quick
    Reserved for future use. Spectral's cached run is sub-3s for the 5-spec
    corpus; this switch is a no-op.

.EXAMPLE
    pwsh ./scripts/quality-helpers/check-openapi.ps1
    # Lints every spec under orderly-microservices/docs/api/.

.EXAMPLE
    pwsh ./scripts/quality-helpers/check-openapi.ps1 -SpecDir ./docs/api
    # Override the spec directory.

.NOTES
    Exit codes:
        0 — every spec is clean
        1 — one or more specs have error-severity findings
        2 — internal error (spectral not installable, etc.)

    First-run note: `npx --yes @stoplight/spectral-cli` downloads Spectral
    (~10 MB) on first invocation. This script warms the cache with
    `spectral --version` before the real run.
#>

[CmdletBinding()]
param(
    [string]$SpecDir,
    [string]$Ruleset,
    [switch]$Quick
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$script:Tag = '[check-openapi]'

$repoRoot = (Resolve-Path "$PSScriptRoot/../..").Path

if ([string]::IsNullOrWhiteSpace($SpecDir)) {
    $SpecDir = Join-Path $repoRoot 'orderly-microservices/docs/api'
}
if ([string]::IsNullOrWhiteSpace($Ruleset)) {
    $Ruleset = Join-Path $repoRoot 'orderly-microservices/.spectral.yaml'
}

# > [!CAUTION]
# > Each entry here is a deliberate ruleset suppression — i.e. we accept
# > a finding from Spectral as a known false positive. Add only when the
# > underlying constraint is real (e.g. the spec deliberately omits a
# > field because it's added at deploy time). Never add a suppression to
# > make the gate green without understanding the rule.
$script:AllowedOpenApiFindings = @{
    'oas3-api-servers' =
        'Committed docs/api/* specs deliberately omit a servers: block — ' +
        'the runtime deployment targets are added by the deployment playbook, ' +
        'not at code-emit time. Re-enable the rule when the deployment playbook ' +
        'lands a real server list per environment.'
}

# ----------------------------------------------------------------
# 1. One-time Spectral warmup.
# ----------------------------------------------------------------
Write-Host "$script:Tag warming Spectral cache (one-time npx download if needed)..."
$versionOutput = & npx --yes @stoplight/spectral-cli --version 2>&1
if ($LASTEXITCODE -ne 0) {
    Write-Host "$script:Tag failed to install @stoplight/spectral-cli (exit $LASTEXITCODE)" -ForegroundColor Red
    $versionOutput | ForEach-Object { Write-Host "  $_" }
    exit 2
}
Write-Host "$script:Tag $($versionOutput[-1]) ready"

# ----------------------------------------------------------------
# 2. Discover specs.
# ----------------------------------------------------------------
if (-not (Test-Path -LiteralPath $SpecDir)) {
    Write-Host "$script:Tag spec directory not found: $SpecDir" -ForegroundColor Yellow
    Write-Host "$script:Tag nothing to lint; exit 0." -ForegroundColor Green
    exit 0
}

if (-not (Test-Path -LiteralPath $Ruleset)) {
    Write-Host "$script:Tag ruleset not found: $Ruleset" -ForegroundColor Red
    exit 2
}

# Enumerate .json and .yaml/.yml files. Use BCL enumerator (matches
# check-nodatime.ps1:146-180) so we prune nothing — docs/api is small.
$specs = New-Object System.Collections.Generic.List[string]
foreach ($ext in @('*.json', '*.yaml', '*.yml')) {
    foreach ($f in [System.IO.Directory]::EnumerateFiles($SpecDir, $ext)) {
        $specs.Add($f)
    }
}

if ($specs.Count -eq 0) {
    Write-Host "$script:Tag no specs found under $SpecDir" -ForegroundColor Yellow
    Write-Host "$script:Tag nothing to lint; exit 0." -ForegroundColor Green
    exit 0
}

Write-Host "$script:Tag $($specs.Count) spec(s) to lint."

# ----------------------------------------------------------------
# 3. Run Spectral per spec.
# ----------------------------------------------------------------
$failedCount = 0
$allowlistHits = 0

Push-Location $repoRoot
try {
    foreach ($spec in $specs) {
        $relSpec = $spec.Substring($repoRoot.Length).TrimStart('\', '/') -replace '\\', '/'
        Write-Host ""
        Write-Host "$script:Tag → $relSpec" -ForegroundColor Cyan

        # --fail-severity error is the load-bearing flag: Spectral's default
        # exit is 0 unless an `error`-severity finding is emitted.
        $spectralOutput = & npx --yes @stoplight/spectral-cli lint `
            --ruleset $Ruleset `
            --fail-severity error `
            --format stylish `
            $spec 2>&1
        $spectralExit = $LASTEXITCODE

        if ($spectralExit -eq 0) {
            Write-Host "$script:Tag   ✓ clean" -ForegroundColor Green
            continue
        }

        # Parse Spectral output for allowlisted rules. If the only findings
        # are rules in $AllowedOpenApiFindings, treat as clean; otherwise fail.
        $errors = @($spectralOutput | Where-Object { $_ -match '^\s*\d+\s+(error|warn|info)\s+' })
        $realErrors = 0
        foreach ($e in $errors) {
            $ruleId = ($e -replace '^\s*\d+\s+(error|warn|info)\s+([\w-]+).*', '$2')
            if ($script:AllowedOpenApiFindings.ContainsKey($ruleId)) {
                $allowlistHits++
                Write-Host "$script:Tag   ~ suppressed: $ruleId" -ForegroundColor DarkGray
            }
            else {
                $realErrors++
                Write-Host "  $_" -ForegroundColor Red
            }
        }

        if ($realErrors -gt 0) {
            $failedCount++
            Write-Host "$script:Tag   ✗ $realErrors real finding(s)" -ForegroundColor Red
        }
        else {
            Write-Host "$script:Tag   ✓ clean (after allowlist)" -ForegroundColor Green
        }
    }
}
finally {
    Pop-Location
}

Write-Host ""
if ($allowlistHits -gt 0) {
    Write-Host "$script:Tag $allowlistHits finding(s) suppressed by allowlist." -ForegroundColor DarkGray
    Write-Host "$script:Tag review `$AllowedOpenApiFindings periodically — every entry is technical debt." -ForegroundColor DarkGray
}

if ($failedCount -gt 0) {
    Write-Host "$script:Tag $failedCount spec(s) failed lint." -ForegroundColor Red
    exit 1
}

Write-Host "$script:Tag every spec is clean." -ForegroundColor Green
exit 0