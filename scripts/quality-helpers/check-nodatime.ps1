<#
.SYNOPSIS
    NodaTime convention checker — bans raw BCL wall-clock reads in the Orderly
    .NET 10 microservices solution.

.DESCRIPTION
    AGENTS.md mandates NodaTime (`Instant`, `LocalDate`, ...) over the BCL
    date/time types. This scanner enforces that mandate with a lightweight
    line-based parser — no MSBuild, no Roslyn workspace — so it runs in ~2s and
    is safe to put in the -Quick path of the quality gate (plan §0.2).

    Two severity tiers:

      1. ALWAYS-BANNED — local-time and time-zone APIs. These are wrong in every
         context, including tests, because they make behaviour depend on the
         machine's regional settings:
             DateTime.Now, DateTime.Today, DateTimeOffset.Now, TimeZoneInfo

      2. PRODUCTION-BANNED — UTC wall-clock reads. Correct but untestable: they
         bypass the injected TimeProvider / NodaTime IClock, so time cannot be
         frozen in a test:
             DateTime.UtcNow, DateTimeOffset.UtcNow
         Test projects are exempt (a test asserting "roughly now" is legitimate).

    Excluded from BOTH tiers (see $PathExcludes):
      * **/Migrations/** — EF Core and OpenIddict scaffold DateTime columns.
        ~294 of the repo's ~400 BCL date/time hits live here and are generated.
      * obj/, bin/, *.Designer.cs, *.g.cs, *.generated.cs — generated code.
      * Comment-only lines and block comments — a prose mention of DateTime.Now
        (including this file's own header) is not a violation.

    Files with a legitimate, reviewed reason to read the wall clock are listed
    in $AllowedWallClockFiles with a written rationale, keyed by repo-relative
    path so the entry survives edits to the file.

    Phase 2 of .agents/plan/platform/QUALITY_GATE_ENHANCEMENT_PLAN.md —
    Architecture, NodaTime & MediatR Conventions (plan §6.3 / §9 Phase 2).

.PARAMETER Path
    Root to scan. Defaults to the orderly-microservices/ source tree.

.PARAMETER IncludeTests
    Also apply the production-banned tier to test projects. Off by default.

.PARAMETER ListAllowed
    Print the allowlist with its rationales and exit 0 without scanning.
    Useful when reviewing whether an exemption is still justified.

.EXAMPLE
    pwsh ./scripts/quality-helpers/check-nodatime.ps1
    # Fails the gate on any banned date/time API outside the allowlist.

.EXAMPLE
    pwsh ./scripts/quality-helpers/check-nodatime.ps1 -ListAllowed
    # Review the exemptions without running a scan.

.NOTES
    Exit codes:
        0 — no banned date/time usage outside the allowlist
        1 — one or more violations
        2 — internal error (scan root missing)

    Style follows scripts/quality-helpers/find-secrets.ps1:
    [CmdletBinding()], $ErrorActionPreference='Stop', block-comment header,
    [check-nodatime] tag prefix on every Write-Host.
#>

[CmdletBinding()]
param(
    [string]$Path,
    [switch]$IncludeTests,
    [switch]$ListAllowed
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# ----------------------------------------------------------------
# Constants
# ----------------------------------------------------------------

$script:Tag = '[check-nodatime]'

# Tier 1 — banned everywhere, including test projects.
# Local-time / time-zone APIs make behaviour depend on machine settings.
$script:AlwaysBanned = @(
    @{ Name = 'DateTime.Now';       Pattern = '\bDateTime\.Now\b';       Fix = 'inject TimeProvider and use clock.GetUtcNow(), or NodaTime SystemClock.Instance.GetCurrentInstant()' },
    @{ Name = 'DateTime.Today';     Pattern = '\bDateTime\.Today\b';     Fix = 'use LocalDate.FromDateTime(clock.GetUtcNow().UtcDateTime) or an injected IClock' },
    @{ Name = 'DateTimeOffset.Now'; Pattern = '\bDateTimeOffset\.Now\b'; Fix = 'use clock.GetUtcNow() (TimeProvider) — never local time' },
    @{ Name = 'TimeZoneInfo';       Pattern = '\bTimeZoneInfo\b';        Fix = 'use NodaTime DateTimeZoneProviders.Tzdb and the Restaurant.TimeZone column' }
)

# Tier 2 — banned in production code only. Correct, but untestable.
$script:ProductionBanned = @(
    @{ Name = 'DateTime.UtcNow';       Pattern = '\bDateTime\.UtcNow\b';       Fix = 'inject TimeProvider (clock.GetUtcNow().UtcDateTime) or use SystemClock.Instance.GetCurrentInstant()' },
    @{ Name = 'DateTimeOffset.UtcNow'; Pattern = '\bDateTimeOffset\.UtcNow\b'; Fix = 'inject TimeProvider and call clock.GetUtcNow()' }
)

# Path fragments that are never scanned. Matched case-insensitively against the
# repo-relative path with forward slashes.
$script:PathExcludes = @(
    '/obj/', '/bin/', '/Migrations/', '/Generated Files/', '/node_modules/', '/.git/'
)

# Filename suffixes that indicate generated code.
$script:GeneratedSuffixes = @(
    '.Designer.cs', '.g.cs', '.generated.cs', 'ModelSnapshot.cs'
)

# Production files exempted from the Tier-2 (UtcNow) rule, each with the reason
# the exemption is justified. Keys are repo-relative paths with forward slashes.
#
# > [!CAUTION]
# > Adding an entry here means "NodaTime genuinely cannot express this".
# > Prefer injecting TimeProvider over adding an exemption.
$script:AllowedWallClockFiles = @{
    'orderly-microservices/BuildingBlocks.Persistence/MigratorHostedService.cs' =
        'Measures elapsed migration duration for a log line, not domain time. Monotonic-ish wall-clock delta; no NodaTime equivalent adds value here.'

    'orderly-microservices/Services/Discount/Discount.Grpc/Messaging/Outbox/BrokerHealthState.cs' =
        'Stores a Unix-millisecond timestamp in a long so it can be updated atomically with Interlocked for broker back-off. A NodaTime Instant is a struct and cannot be written atomically.'

    'orderly-microservices/Services/Discount/Discount.Grpc/Models/ProcessedInboundevent.cs' =
        'ConsumedAt is an EF Core column declared as DateTime (SQLite inbox-dedup table). Reshaping it to Instant requires a migration; tracked for a later persistence phase.'

    'orderly-microservices/Services/Discount/Discount.Grpc/Messaging/EventHandlers/InboundEventDedup.cs' =
        'Writes the ProcessedInboundEvent.ConsumedAt DateTime column above; must match that column type.'

    'orderly-microservices/Services/Identity/Identity.API/Services/AuditLogger.cs' =
        'AuditLog.Timestamp is DateTimeOffset because ASP.NET Core Identity entity shapes (IdentityUser<Guid>.LockoutEnd et al.) use DateTimeOffset throughout Identity.API. Converting the whole Identity temporal surface is its own migration.'

    'orderly-microservices/Services/Identity/Identity.API/Data/DataSeeder.cs' =
        'Seeds ApplicationUser.CreatedAt, a DateTimeOffset column inherited from the ASP.NET Identity entity shape. Same rationale as AuditLogger.cs.'

    'orderly-microservices/Services/Identity/Identity.API/Features/Auth/Register/RegisterCommand.cs' =
        'Sets ApplicationUser.CreatedAt (DateTimeOffset, ASP.NET Identity shape). Same rationale as AuditLogger.cs.'

    'orderly-microservices/Services/Identity/Identity.API/Features/Users/CreateUser/CreateUserCommand.cs' =
        'Sets ApplicationUser.CreatedAt (DateTimeOffset, ASP.NET Identity shape). Same rationale as AuditLogger.cs.'
}

# ----------------------------------------------------------------
# Helpers
# ----------------------------------------------------------------

function Get-PrunedSourceFiles {
    <#
    .SYNOPSIS
        Recursively enumerates *.cs, skipping whole directory subtrees that can
        never contain hand-written source.
    .DESCRIPTION
        Get-ChildItem -Recurse walks bin/, obj/ and node_modules/ and then
        post-filters them, which dominates the runtime on this repo. Pruning at
        the directory level with the BCL enumerator is an order of magnitude
        faster and, because Migrations/ is pruned here too, it also removes the
        ~294 generated EF/OpenIddict hits before they are ever read.
    #>
    param([string]$Root, [string[]]$PruneDirs)

    $results = New-Object System.Collections.Generic.List[string]
    $stack = New-Object System.Collections.Generic.Stack[string]
    $stack.Push($Root)

    while ($stack.Count -gt 0) {
        $dir = $stack.Pop()
        try {
            foreach ($sub in [System.IO.Directory]::EnumerateDirectories($dir)) {
                if ($PruneDirs -contains [System.IO.Path]::GetFileName($sub)) { continue }
                $stack.Push($sub)
            }
            foreach ($file in [System.IO.Directory]::EnumerateFiles($dir, '*.cs')) {
                $results.Add($file)
            }
        } catch {
            continue
        }
    }

    return $results
}

function Get-RelativePath {
    <#
    .SYNOPSIS
        Repo-relative path with forward slashes, for stable comparison and output.
    #>
    param([string]$FullName, [string]$RepoRoot)

    if ($FullName.StartsWith($RepoRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
        return $FullName.Substring($RepoRoot.Length).TrimStart('\', '/').Replace('\', '/')
    }
    return $FullName.Replace('\', '/')
}

function Test-ShouldScanFile {
    <#
    .SYNOPSIS
        Returns $true if a repo-relative .cs path is in scope for the scan.
    #>
    param([string]$RelPath)

    $probe = "/$RelPath"
    foreach ($excl in $script:PathExcludes) {
        if ($probe -like "*$excl*") { return $false }
    }
    foreach ($suffix in $script:GeneratedSuffixes) {
        if ($RelPath.EndsWith($suffix, [System.StringComparison]::OrdinalIgnoreCase)) { return $false }
    }
    return $true
}

function Test-IsTestFile {
    <#
    .SYNOPSIS
        Returns $true when the path sits inside a test project.
    .DESCRIPTION
        Matches a directory segment ending in '.Tests' (Catalog.API.Tests,
        BuildingBlocks.Dev.Tests, ...) — the convention used throughout the repo.
    #>
    param([string]$RelPath)
    return $RelPath -match '(^|/)[^/]*\.Tests?(/|$)'
}

function Remove-CommentedCode {
    <#
    .SYNOPSIS
        Blanks out comment content so prose mentions of banned APIs don't trip
        the scanner.
    .DESCRIPTION
        Returns the input lines with block-comment bodies and line-comment tails
        replaced by empty strings. Line numbering is preserved (each input line
        maps to exactly one output line) so findings still report accurately.

        This is deliberately simple — it does not model string literals or
        verbatim strings. The failure mode is a false NEGATIVE on the pathological
        case of a banned identifier inside a string containing "//", which is
        an acceptable trade for keeping the checker dependency-free.
    #>
    param([string[]]$Lines)

    $inBlock = $false
    $result = New-Object string[] $Lines.Length

    for ($i = 0; $i -lt $Lines.Length; $i++) {
        $line = $Lines[$i]

        if ($inBlock) {
            $end = $line.IndexOf('*/')
            if ($end -ge 0) {
                $line = $line.Substring($end + 2)
                $inBlock = $false
            } else {
                $result[$i] = ''
                continue
            }
        }

        # Strip a trailing // comment (covers /// xmldoc too).
        $slash = $line.IndexOf('//')
        if ($slash -ge 0) { $line = $line.Substring(0, $slash) }

        # Strip inline /* ... */ blocks, and open an unterminated one.
        while ($true) {
            $start = $line.IndexOf('/*')
            if ($start -lt 0) { break }
            $end = $line.IndexOf('*/', $start + 2)
            if ($end -ge 0) {
                $line = $line.Substring(0, $start) + $line.Substring($end + 2)
            } else {
                $line = $line.Substring(0, $start)
                $inBlock = $true
                break
            }
        }

        $result[$i] = $line
    }

    return $result
}

# ----------------------------------------------------------------
# Main
# ----------------------------------------------------------------

$repoRoot = (Resolve-Path "$PSScriptRoot/../..").Path

if ($ListAllowed) {
    Write-Host "$script:Tag $($script:AllowedWallClockFiles.Count) file(s) exempt from the UtcNow rule:`n"
    foreach ($key in ($script:AllowedWallClockFiles.Keys | Sort-Object)) {
        Write-Host "  $key" -ForegroundColor Yellow
        Write-Host "      $($script:AllowedWallClockFiles[$key])"
    }
    exit 0
}

if ([string]::IsNullOrWhiteSpace($Path)) {
    $Path = Join-Path $repoRoot 'orderly-microservices'
}
if (-not (Test-Path -LiteralPath $Path)) {
    Write-Host "$script:Tag scan root not found: $Path" -ForegroundColor Red
    exit 2
}

$scanRoot = (Resolve-Path -LiteralPath $Path).Path
Write-Host "$script:Tag repo: $repoRoot"
Write-Host "$script:Tag scan: $scanRoot"

$violations = New-Object System.Collections.Generic.List[object]
$exemptHits = 0
$scanned = 0
$skipped = 0

$sourceFiles = Get-PrunedSourceFiles -Root $scanRoot -PruneDirs @(
    'bin', 'obj', 'node_modules', '.git', '.vs', 'Migrations', 'Generated Files'
)

$sourceFiles | ForEach-Object {
    $relPath = Get-RelativePath -FullName $_ -RepoRoot $repoRoot

    if (-not (Test-ShouldScanFile -RelPath $relPath)) {
        $skipped++
        return
    }
    $scanned++

    $raw = Get-Content -LiteralPath $_ -ErrorAction SilentlyContinue
    if ($null -eq $raw) { return }
    $lines = Remove-CommentedCode -Lines @($raw)

    $isTest = Test-IsTestFile -RelPath $relPath
    $isAllowed = $script:AllowedWallClockFiles.ContainsKey($relPath)

    # Tier 2 applies to production files only (unless -IncludeTests), and never
    # to files carrying a reviewed exemption.
    $rules = @($script:AlwaysBanned)
    if ((-not $isTest) -or $IncludeTests) {
        if ($isAllowed) {
            $exemptHits++
        } else {
            $rules += $script:ProductionBanned
        }
    }

    for ($i = 0; $i -lt $lines.Length; $i++) {
        $line = $lines[$i]
        if ([string]::IsNullOrWhiteSpace($line)) { continue }

        foreach ($rule in $rules) {
            if ($line -match $rule.Pattern) {
                $violations.Add([pscustomobject]@{
                    Path = $relPath
                    Line = $i + 1
                    Rule = $rule.Name
                    Fix  = $rule.Fix
                    Text = $line.Trim()
                })
            }
        }
    }
}

Write-Host "$script:Tag scanned $scanned .cs file(s); $skipped skipped by filename filter, generated/Migrations subtrees pruned before read"
if ($exemptHits -gt 0) {
    Write-Host "$script:Tag $exemptHits file(s) exempt from the UtcNow rule — run with -ListAllowed to review." -ForegroundColor Yellow
}

if ($violations.Count -gt 0) {
    Write-Host ''
    Write-Host "$script:Tag $($violations.Count) banned date/time usage(s):" -ForegroundColor Red
    foreach ($v in ($violations | Sort-Object Path, Line)) {
        Write-Host "  x $($v.Path):$($v.Line)  $($v.Rule)" -ForegroundColor Red
        Write-Host "      $($v.Text)"
        Write-Host "      fix: $($v.Fix)" -ForegroundColor DarkGray
    }
    Write-Host ''
    Write-Host "$script:Tag AGENTS.md mandates NodaTime (Instant, LocalDate) over BCL date/time." -ForegroundColor Red
    Write-Host "$script:Tag If a usage is genuinely unavoidable, add the file to" -ForegroundColor Red
    Write-Host "$script:Tag `$AllowedWallClockFiles in this script WITH a rationale." -ForegroundColor Red
    exit 1
}

Write-Host ''
Write-Host "$script:Tag no banned date/time usage found." -ForegroundColor Green
exit 0
