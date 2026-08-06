<#
.SYNOPSIS
    Cobertura coverage gate for the Orderly .NET 10 microservices solution.

.DESCRIPTION
    Parses every TestResults/**/coverage.cobertura.xml file (produced by
    `dotnet test --collect:"XPlat Code Coverage"` via coverlet.collector)
    and fails the gate when the aggregate line-rate falls below the
    threshold (default 0.80).

    Aggregate is computed as `sum(covered) / sum(valid)` across ALL files,
    NOT the mean of per-project rates. The difference matters when projects
    are uneven in size: a 99%/100-line project and a 1%/100,000-line project
    would naively pass at 50% mean; the aggregate correctly reports ~1%.

    Per-project rates are still printed as a leaderboard (worst first) so
    the next PR knows where to invest, but only the aggregate gates.

    The gate is HARD-SKIPPED under -Quick (and the section 19 wiring in
    phase-guard.ps1 short-circuits it under -PreCommit). Coverage parsing
    is meaningless when tests have not produced Cobertura files, and a
    stale `TestResults/` directory would give a misleading pass/fail.

    Phase 4 of .agents/plan/platform/QUALITY_GATE_ENHANCEMENT_PLAN.md —
    Docker, Contract & Test Coverage validation (plan §9 Phase 4).

.PARAMETER TestResultsRoot
    Root to search for coverage.cobertura.xml files. Defaults to the repo
    root (Coverlet emits under TestResults/<machine>/<run>/).

.PARAMETER MinimumLineRate
    Aggregate line-rate threshold. Default 0.80 (the value named in plan §6.4).

.PARAMETER ListProjects
    Print the per-project rate and the aggregate without failing. Useful as
    a one-shot diagnostic.

.PARAMETER Quick
    Reserved for future use. The phase-guard wiring short-circuits this
    helper under -Quick via its own Write-Skipped; the helper itself does
    NOT short-circuit (parsing is cheap) — but it does emit a SKIPPED line
    if invoked directly with -Quick, mirroring check-sonar.ps1's
    forward-compatibility pattern.

.EXAMPLE
    pwsh ./scripts/quality-helpers/check-coverage.ps1
    # Fails if aggregate line-rate < 0.80.

.EXAMPLE
    pwsh ./scripts/quality-helpers/check-coverage.ps1 -ListProjects
    # Print per-project rates + aggregate, exit 0.

.NOTES
    Exit codes:
        0 — aggregate line-rate meets threshold
        1 — aggregate line-rate is below threshold
        2 — internal error (no Cobertura files found, malformed XML, etc.)

    Style follows scripts/quality-helpers/find-secrets.ps1:
    [CmdletBinding()], $ErrorActionPreference='Stop', block-comment header,
    [check-coverage] tag prefix on every Write-Host.
#>

[CmdletBinding()]
param(
    [string]$TestResultsRoot,
    [double]$MinimumLineRate = 0.80,
    [switch]$ListProjects,
    [switch]$Quick
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$script:Tag = '[check-coverage]'

# Local skip-helper: the heavy helpers (check-sonar.ps1:69-72 and
# check-owasp-dependency.ps1:74-77) define their own private Write-Skip
# to signal "the tool environment doesn't support this run" — distinct
# from phase-guard's Write-Skipped ("the gate mode short-circuits this").
function Write-Skip {
    param([string]$Reason)
    Write-Host "$script:Tag SKIPPED — $Reason" -ForegroundColor DarkGray
}

# > [!CAUTION]
# > Exemptions here mean "this project is allowed to be below 80% line
# > coverage". Prefer writing tests over adding an exemption — every
# > entry here is technical debt the next reader will inherit.
$script:AllowedCoverageExemptions = @{
    # Currently empty. The 12 test projects are expected to clear 80%
    # with their existing test suites. If a future project lands below,
    # add its repo-relative csproj path here WITH a written rationale.
}

# ----------------------------------------------------------------
# Helpers
# ----------------------------------------------------------------

function Get-RelativePath {
    <#
    .SYNOPSIS
        Repo-relative path with forward slashes.
    #>
    param([string]$FullPath)

    $repoRoot = (Resolve-Path "$PSScriptRoot/../..").Path
    $full = (Resolve-Path -LiteralPath $FullPath).Path
    $rel = $full.Substring($repoRoot.Length).TrimStart('\', '/') -replace '\\', '/'
    return $rel
}

function Get-CoverageFiles {
    <#
    .SYNOPSIS
        Walk $TestResultsRoot and return every coverage.cobertura.xml.
        Prunes bin/, obj/, node_modules/ at the directory level to avoid
        the 29,000-file post-filter trap documented in check-nodatime.ps1:146-180.
    #>
    param([string]$Root)

    $results = New-Object System.Collections.Generic.List[string]
    $stack = New-Object System.Collections.Generic.Stack[string]
    $stack.Push($Root)
    $pruneDirs = @('bin', 'obj', 'node_modules', '.git', '.vs', '.agents')

    while ($stack.Count -gt 0) {
        $dir = $stack.Pop()
        try {
            foreach ($sub in [System.IO.Directory]::EnumerateDirectories($dir)) {
                $name = [System.IO.Path]::GetFileName($sub)
                if ($pruneDirs -contains $name) { continue }
                $stack.Push($sub)
            }
            foreach ($f in [System.IO.Directory]::EnumerateFiles($dir, 'coverage.cobertura.xml')) {
                $results.Add($f)
            }
        } catch {
            continue
        }
    }

    return $results
}

function Get-CoverageSummary {
    <#
    .SYNOPSIS
        Parse a Cobertura XML file and return its top-level line-rate metrics.
    #>
    param([string]$Path)

    [xml]$xml = Get-Content -LiteralPath $Path -Raw
    $root = $xml.coverage
    if ($null -eq $root) {
        throw "no <coverage> root in $Path"
    }

    [double]$rate = 0.0
    if ($root.'line-rate') { $rate = [double]$root.'line-rate' }
    [int]$covered = 0
    if ($root.'lines-covered') { $covered = [int]$root.'lines-covered' }
    [int]$valid = 0
    if ($root.'lines-valid') { $valid = [int]$root.'lines-valid' }
    [int]$branchesCovered = 0
    if ($root.'branches-covered') { $branchesCovered = [int]$root.'branches-covered' }
    [int]$branchesValid = 0
    if ($root.'branches-valid') { $branchesValid = [int]$root.'branches-valid' }

    # Try to extract the owning project from the package/source paths.
    # Coverlet emits per-package <package name="...">; the project that
    # ran the tests is reflected in the file path (TestResults/<project>/...)
    # or, failing that, the first <package> name.
    $project = $null
    $rel = Get-RelativePath -FullPath $Path
    if ($rel -match '^(.+?)/TestResults/') {
        $project = $matches[1]
    }
    elseif ($xml.SelectNodes('//package').Count -gt 0) {
        $project = $xml.SelectNodes('//package')[0].name
    }

    return [pscustomobject]@{
        Project         = $project
        Path            = $rel
        LineRate        = $rate
        LinesCovered    = $covered
        LinesValid      = $valid
        BranchesCovered = $branchesCovered
        BranchesValid   = $branchesValid
    }
}

# ----------------------------------------------------------------
# Main
# ----------------------------------------------------------------

if ([string]::IsNullOrWhiteSpace($TestResultsRoot)) {
    $TestResultsRoot = (Resolve-Path "$PSScriptRoot/../..").Path
}

if (-not (Test-Path -LiteralPath $TestResultsRoot)) {
    Write-Host "$script:Tag root not found: $TestResultsRoot" -ForegroundColor Red
    exit 2
}

if ($Quick) {
    Write-Skip "running with -Quick; coverage parsing requires a fresh `dotnet test` run (phase-guard forwards -Quick)."
    exit 0
}

$repoRoot = (Resolve-Path "$PSScriptRoot/../..").Path
Write-Host "$script:Tag root: $repoRoot"
Write-Host "$script:Tag threshold: $MinimumLineRate"

try {
    $files = @(Get-CoverageFiles -Root $TestResultsRoot)
}
catch {
    Write-Host "$script:Tag walk failed: $($_.Exception.Message)" -ForegroundColor Red
    exit 2
}

if ($files.Count -eq 0) {
    Write-Host "$script:Tag no coverage.cobertura.xml files under $TestResultsRoot" -ForegroundColor Yellow
    Write-Host "$script:Tag Phase 4 requires tests to have been run with --collect:`"XPlat Code Coverage`"." -ForegroundColor Yellow
    Write-Host "$script:Tag (coverlet.collector is wired in all 12 *.Tests.csproj; the workflows use the flag.)" -ForegroundColor Yellow
    exit 2
}

Write-Host "$script:Tag $($files.Count) coverage file(s) discovered."

$summaries = New-Object System.Collections.Generic.List[object]
$parseErrors = New-Object System.Collections.Generic.List[string]

foreach ($f in $files) {
    try {
        $summaries.Add((Get-CoverageSummary -Path $f))
    }
    catch {
        $parseErrors.Add("$f`: $($_.Exception.Message)")
    }
}

if ($parseErrors.Count -gt 0) {
    Write-Host "$script:Tag $($parseErrors.Count) file(s) could not be parsed:" -ForegroundColor Red
    foreach ($e in $parseErrors) { Write-Host "  x $e" -ForegroundColor Red }
    exit 2
}

# Aggregate = sum(covered) / sum(valid). NOT mean of per-file rates.
$totalCovered = ($summaries | Measure-Object -Property LinesCovered -Sum).Sum
$totalValid   = ($summaries | Measure-Object -Property LinesValid   -Sum).Sum
$totalBranchesCovered = ($summaries | Measure-Object -Property BranchesCovered -Sum).Sum
$totalBranchesValid   = ($summaries | Measure-Object -Property BranchesValid   -Sum).Sum

if ($totalValid -eq 0) {
    Write-Host "$script:Tag aggregate denominator is zero — coverage report is empty." -ForegroundColor Red
    exit 2
}

$aggregate = [double]$totalCovered / [double]$totalValid
$branchAggregate = 0.0
if ($totalBranchesValid -gt 0) {
    $branchAggregate = [double]$totalBranchesCovered / [double]$totalBranchesValid
}

# Print per-project leaderboard (worst first).
$ranked = $summaries | Sort-Object LineRate

Write-Host ""
Write-Host "$script:Tag per-project line-rate (worst first):" -ForegroundColor Cyan
foreach ($s in $ranked) {
    $pct = [math]::Round($s.LineRate * 100, 1)
    $marker = '✓'
    $color = 'Green'
    if ($s.LineRate -lt $MinimumLineRate) { $marker = '✗'; $color = 'Red' }
    Write-Host ("  {0} {1,5}%  {2}  ({3}/{4})" -f $marker, $pct, $s.Project, $s.LinesCovered, $s.LinesValid) -ForegroundColor $color
}

Write-Host ""
$aggPct = [math]::Round($aggregate * 100, 1)
$branchPct = [math]::Round($branchAggregate * 100, 1)
Write-Host "$script:Tag aggregate line-rate: $aggPct%  ($totalCovered / $totalValid lines)" -ForegroundColor Cyan
Write-Host "$script:Tag aggregate branch-rate: $branchPct%  ($totalBranchesCovered / $totalBranchesValid branches)" -ForegroundColor Cyan

if ($ListProjects) {
    exit 0
}

if ($aggregate -lt $MinimumLineRate) {
    Write-Host ""
    Write-Host "$script:Tag aggregate line-rate $([math]::Round($aggregate, 4)) < threshold $MinimumLineRate" -ForegroundColor Red
    Write-Host "$script:Tag add tests for the worst-covered project (printed above) or" -ForegroundColor Red
    Write-Host "$script:Tag — if the gap is structural — add the project's path to" -ForegroundColor Red
    Write-Host "$script:Tag `$AllowedCoverageExemptions WITH a written rationale." -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "$script:Tag aggregate line-rate meets threshold." -ForegroundColor Green
exit 0