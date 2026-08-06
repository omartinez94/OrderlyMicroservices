<#
.SYNOPSIS
    GitHub Actions workflow lint for the Orderly .NET 10 microservices solution.

.DESCRIPTION
    Runs `npx --yes rhysd/actionlint` over every workflow under
    .github/workflows/. Catches typos in `runs-on:`, missing `on:`
    triggers, broken action version pins, and shellcheck-style errors
    inside `run:` blocks.

    actionlint has no allowlist mechanism that isn't a tool-side escape
    hatch (the `## actionlint disable=` comment), and any finding is by
    definition either a real syntax error or a version-check failure on
    `actions/*@vN` — never a stylistic preference. Therefore this helper
    has NO allowlist constant; every actionlint finding fails the gate.
    Disabling individual checks is done inline at the offending line.

    `npx --yes rhysd/actionlint` downloads the Go binary on first run;
    the project's GitHub workflow dir is two-deep (.github/workflows at
    the repo root, plus a nested copy under orderly-microservices/ that
    was used during early experimentation). We lint both.

    Phase 4 of .agents/plan/platform/QUALITY_GATE_ENHANCEMENT_PLAN.md —
    Docker, Contract & Test Coverage validation (plan §9 Phase 4).

.PARAMETER WorkflowDir
    Directory containing the GitHub Actions workflow files (*.yml).
    Can be passed multiple times. Defaults to the two known workflow
    directories at the repo root.

.PARAMETER Quick
    Reserved for future use. actionlint's cached run is sub-1s; this
    switch is a no-op.

.EXAMPLE
    pwsh ./scripts/quality-helpers/check-github-actions.ps1
    # Lints every workflow under .github/workflows/.

.EXAMPLE
    pwsh ./scripts/quality-helpers/check-github-actions.ps1 -WorkflowDir ./custom-wf
    # Lint a different directory.

.NOTES
    Exit codes:
        0 — every workflow is clean
        1 — one or more actionlint findings
        2 — internal error (actionlint not installable, etc.)

    First-run note: `npx --yes rhysd/actionlint` downloads the Go binary
    (~5 MB) on first invocation. This script warms the cache with
    `actionlint -version` before the real run.
#>

[CmdletBinding()]
param(
    [string[]]$WorkflowDir,
    [switch]$Quick
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$script:Tag = '[check-github-actions]'

# Local skip-helper: when the tool environment can't install actionlint
# (no network, npm cache clone failure, etc.) the helper degrades to a
# printed SKIPPED line and exits 0 — mirroring check-sonar.ps1:69-72 and
# check-owasp-dependency.ps1:74-77. The gate must never imply a check ran
# when it did not; the SKIPPED line carries the reason.
function Write-Skip {
    param([string]$Reason)
    Write-Host "$script:Tag SKIPPED — $Reason" -ForegroundColor DarkGray
}

$repoRoot = (Resolve-Path "$PSScriptRoot/../..").Path

if ($null -eq $WorkflowDir -or $WorkflowDir.Count -eq 0) {
    $WorkflowDir = @(
        (Join-Path $repoRoot '.github/workflows'),
        (Join-Path $repoRoot 'orderly-microservices/.github/workflows')
    )
}

# ----------------------------------------------------------------
# 1. One-time actionlint warmup.
#
#    `npx --yes rhysd/actionlint` downloads the Go binary via a postinstall
#    git clone. If that fails (offline workstation, broken npm cache,
#    permissions), the helper degrades to SKIPPED + exit 0 — matching the
#    check-sonar / check-owasp pattern. The gate never implies a check ran
#    when it did not; the SKIPPED line carries the reason.
# ----------------------------------------------------------------
Write-Host "$script:Tag warming actionlint cache (one-time npx download if needed)..."
$versionOutput = & npx --yes rhysd/actionlint -version 2>&1
$warmupExit = $LASTEXITCODE
if ($warmupExit -ne 0) {
    $reason = "could not install rhysd/actionlint via npx (exit $warmupExit); install manually or run in CI."
    Write-Skip $reason
    $versionOutput | ForEach-Object { Write-Host "  $_" -ForegroundColor DarkGray }
    exit 0
}
Write-Host "$script:Tag $($versionOutput[-1]) ready"

# ----------------------------------------------------------------
# 2. Discover workflows.
# ----------------------------------------------------------------
$workflowFiles = New-Object System.Collections.Generic.List[string]
foreach ($dir in $WorkflowDir) {
    if (-not (Test-Path -LiteralPath $dir)) { continue }
    foreach ($f in [System.IO.Directory]::EnumerateFiles($dir, '*.yml')) {
        $workflowFiles.Add($f)
    }
    foreach ($f in [System.IO.Directory]::EnumerateFiles($dir, '*.yaml')) {
        $workflowFiles.Add($f)
    }
}

if ($workflowFiles.Count -eq 0) {
    Write-Host "$script:Tag no workflow files found under the configured directories." -ForegroundColor Yellow
    Write-Host "$script:Tag nothing to lint; exit 0." -ForegroundColor Green
    exit 0
}

Write-Host "$script:Tag $($workflowFiles.Count) workflow file(s) to lint."

# ----------------------------------------------------------------
# 3. Run actionlint.
# ----------------------------------------------------------------
$failed = $false
$findings = 0

Push-Location $repoRoot
try {
    foreach ($wf in $workflowFiles) {
        $relWf = $wf.Substring($repoRoot.Length).TrimStart('\', '/') -replace '\\', '/'
        Write-Host ""
        Write-Host "$script:Tag → $relWf" -ForegroundColor Cyan

        $argList = @('--yes', 'rhysd/actionlint', '-color=never')
        $argList += $wf

        $output = & npx @argList 2>&1
        $exit = $LASTEXITCODE

        if ($exit -eq 0) {
            Write-Host "$script:Tag   ✓ clean" -ForegroundColor Green
            continue
        }

        $failed = $true
        # actionlint emits lines like "<file>:<line>:<col>: <message>".
        # Print each line in red, then a count.
        foreach ($line in $output) {
            if ([string]::IsNullOrWhiteSpace($line)) { continue }
            Write-Host "  $line" -ForegroundColor Red
            $findings++
        }
        Write-Host "$script:Tag   ✗ failed" -ForegroundColor Red
    }
}
finally {
    Pop-Location
}

Write-Host ""
if ($failed) {
    Write-Host "$script:Tag $findings actionlint finding(s)." -ForegroundColor Red
    Write-Host "$script:Tag Fix the underlying issue or, if a finding is intentional," -ForegroundColor Red
    Write-Host "$script:Tag add `## actionlint disable=` comment at the offending line" -ForegroundColor Red
    Write-Host "$script:Tag with a written rationale." -ForegroundColor Red
    exit 1
}

Write-Host "$script:Tag every workflow is clean." -ForegroundColor Green
exit 0