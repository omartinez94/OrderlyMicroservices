<#
.SYNOPSIS
    Spell-check wrapper around cspell for the Orderly .NET 10 microservices solution.

.DESCRIPTION
    Runs `npx --yes cspell` with the project's cspell.json config. Default scope
    is `scripts/**/*.ps1` and `scripts/**/*.md` only — the new scripts added
    by Phase 1 of the quality-gate plan. Use -IncludeCs to also check the
    Services/*.cs and BuildingBlocks*/**/*.cs trees (off by default in Phase 1
    because the dictionary is not yet tuned to the existing xmldoc vocabulary).

    Phase 1 of .agents/plan/platform/QUALITY_GATE_ENHANCEMENT_PLAN.md —
    Formatting, Style & Secret Scanning. Invoked by scripts/phase-guard.ps1
    immediately after the secret scan (section 4) and before the existing
    nullable-warnings gate (section 7).

.PARAMETER Quick
    Reserved for future use. In Phase 1 cspell is fast enough on the default
    scope that this switch is a no-op.

.PARAMETER IncludeCs
    Extend the cspell scope to .cs files (Services/**/*.cs + BuildingBlocks*/**/*.cs).
    Off by default in Phase 1.

.EXAMPLE
    pwsh ./scripts/quality-helpers/check-spelling.ps1
    # Spell-checks scripts/**/*.ps1 and scripts/**/*.md only.

.EXAMPLE
    pwsh ./scripts/quality-helpers/check-spelling.ps1 -IncludeCs
    # Also spell-checks the .cs source trees defined in cspell.json.

.NOTES
    Exit codes:
        0 — no unknown words
        1 — one or more unknown words detected
        2 — internal error (cspell not installable, etc.)

    First-run note: `npx --yes cspell` downloads cspell (~50 MB) on first
    invocation. This script warms the cache with `cspell --version` before
    the real run so the user-visible output is the cspell report, not a
    download progress bar.
#>

[CmdletBinding()]
param(
    [switch]$Quick,
    [switch]$IncludeCs
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$script:Tag = '[check-spelling]'

$repoRoot = (Resolve-Path "$PSScriptRoot/../..").Path
Write-Host "$script:Tag repo: $repoRoot"


# ----------------------------------------------------------------
# 1. One-time cspell warmup.
#
#    `npx --yes cspell` will fetch cspell from npm on first invocation.
#    Doing a no-op `cspell --version` first warms the cache so the real
#    run shows only the spell-check output, not a download progress bar.
# ----------------------------------------------------------------
Write-Host "$script:Tag warming cspell cache (one-time npx download if needed)..."
# Use the call operator (&) so Windows PATHEXT picks the right binary/shim
# (npx is shipped as npx.cmd / npx.ps1 on Windows; Start-Process -FilePath 'npx'
# tries to launch the un-suffixed name and fails with "%1 is not a valid Win32").
$versionOutput = & npx --yes cspell --version 2>&1
if ($LASTEXITCODE -ne 0) {
    Write-Host "$script:Tag failed to install cspell (exit $LASTEXITCODE)" -ForegroundColor Red
    $versionOutput | ForEach-Object { Write-Host "  $_" }
    exit 2
}
Write-Host "$script:Tag cspell $($versionOutput[-1]) ready"

# ----------------------------------------------------------------
# 2. Build the file list.
# ----------------------------------------------------------------
$files = @(
    'scripts/**/*.ps1',
    'scripts/**/*.md'
)
if ($IncludeCs) {
    $files += @(
        'orderly-microservices/Services/**/*.cs',
        'orderly-microservices/BuildingBlocks*/**/*.cs'
    )
}

# ----------------------------------------------------------------
# 3. Run cspell.
# ----------------------------------------------------------------
$argList = @('--yes', 'cspell', '--config', 'cspell.json',
    '--no-progress', '--no-summary', '--unique',
    '--exclude-code')
$argList += $files

Write-Host "$script:Tag running: npx $($argList -join ' ')"
Push-Location $repoRoot
try {
    $cspellOutput = & npx @argList 2>&1
    $cspellExit = $LASTEXITCODE
} finally {
    Pop-Location
}

if ($cspellExit -ne 0) {
    Write-Host "$script:Tag cspell reported unknown words:" -ForegroundColor Red
    $cspellOutput | ForEach-Object { Write-Host "  $_" }
    Write-Host "$script:Tag add project-specific terms to cspell.json ignoreWords." -ForegroundColor Yellow
    exit 1
}

Write-Host "$script:Tag no unknown words." -ForegroundColor Green
exit 0
