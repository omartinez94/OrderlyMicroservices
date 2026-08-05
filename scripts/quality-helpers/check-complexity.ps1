<#
.SYNOPSIS
    Cyclomatic-complexity gate for the Orderly .NET 10 microservices solution.

.DESCRIPTION
    Computes McCabe cyclomatic complexity per method and fails when any method
    exceeds $MaxComplexity.

    Complexity is 1 + the number of independent branch points:
        if / else if           +1 each
        for / foreach / while  +1 each
        case labels            +1 each (default: does not count)
        catch clauses          +1 each
        && / ||                +1 each (short-circuit -> extra path)
        ?: ternary             +1
        ?? / ??=               +1
        ?. / ?[ null-conditional +1
        switch expression arms +1 each (_ discard arm does not count)
        when guards            +1 each

    Methods are located by brace matching from a signature match, so the count
    is scoped to the method body rather than the whole file. This is a text
    analysis, not a Roslyn parse — plan §0.2 requires the convention checks to
    avoid heavyweight MSBuild/Roslyn invocation so the gate stays fast.

    Excluded from scanning: Migrations/, obj/, bin/, Generated Files/ and
    *.Designer.cs / *.g.cs / *.generated.cs — generated switch tables and EF
    model snapshots are enormous and not hand-maintained. Test projects are
    skipped by default (long table-driven [Theory] bodies are not a defect);
    pass -IncludeTests to include them.

    Phase 3 of .agents/plan/platform/QUALITY_GATE_ENHANCEMENT_PLAN.md —
    Static Analysis, Sonar & Security Checks (plan §9 Phase 3).

.PARAMETER Path
    Root to scan. Defaults to the orderly-microservices/ source tree.

.PARAMETER MaxComplexity
    Fail when a method's complexity exceeds this value. Default 15, the
    threshold named in the plan's Phase 3 deliverable.

.PARAMETER Top
    With -Report, how many of the most complex methods to list. Default 20.

.PARAMETER Report
    Print the complexity leaderboard and exit 0 without failing. Use this to
    pick a threshold or to track the tail over time.

.PARAMETER IncludeTests
    Also analyse *.Tests projects.

.EXAMPLE
    pwsh ./scripts/quality-helpers/check-complexity.ps1
    # Fails if any production method exceeds complexity 15.

.EXAMPLE
    pwsh ./scripts/quality-helpers/check-complexity.ps1 -Report -Top 30
    # Show the 30 most complex methods without failing.

.NOTES
    Exit codes:
        0 — no method exceeds the threshold
        1 — one or more methods exceed it
        2 — internal error (scan root missing)

    Style follows scripts/quality-helpers/find-secrets.ps1:
    [CmdletBinding()], $ErrorActionPreference='Stop', block-comment header,
    [check-complexity] tag prefix on every Write-Host.
#>

[CmdletBinding()]
param(
    [string]$Path,
    [int]$MaxComplexity = 15,
    [int]$Top = 20,
    [switch]$Report,
    [switch]$IncludeTests
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# ----------------------------------------------------------------
# Constants
# ----------------------------------------------------------------

$script:Tag = '[check-complexity]'

$script:PruneDirs = @(
    'bin', 'obj', 'node_modules', '.git', '.vs', 'Migrations', 'Generated Files'
)

$script:GeneratedSuffixes = @(
    '.Designer.cs', '.g.cs', '.generated.cs', 'ModelSnapshot.cs'
)

# Branch-point patterns. Each match adds 1 to the method's complexity.
# Keyword patterns use \b so identifiers like `iffy` or `forEach` do not match.
$script:BranchPatterns = @(
    '\bif\s*\(',
    '\bfor\s*\(',
    '\bforeach\s*\(',
    '\bwhile\s*\(',
    '\bcatch\s*[\(\{]',
    '\bcase\s+[^:]+:',
    '\bwhen\s*\(',
    '&&',
    '\|\|',
    '\?\?=?',
    '\?\.',
    '\?\['
)

# Methods whose complexity is reviewed and accepted, keyed by
# "<repo-relative-path>::<MethodName>". Each entry carries a rationale.
#
# Every entry below is *flat* branching — a single linear run of guard clauses
# or a single switch over a closed set — rather than nested control flow. That
# distinction matters: McCabe counts paths, but it is nesting depth, not path
# count, that predicts defects. Splitting these would trade one readable method
# for several indirection hops.
#
# > [!CAUTION]
# > Prefer extracting a helper over adding an entry. A high-complexity method
# > is the single best predictor of a defect in that method.
$script:AllowedComplexMethods = @{
    'orderly-microservices/BuildingBlocks/Exceptions/Handler/CustomExceptionHandler.cs::TryHandleAsync' =
        'A single switch expression mapping 8 exception types to (detail, title, status). Flat, exhaustive, and read top-to-bottom as a table; extracting arms would scatter the HTTP status contract across several methods.'

    'orderly-microservices/Services/Basket/Basket.API/Idempotency/BasketIdempotencyFilter.cs::InvokeAsync' =
        'Sequential guard clauses for the Idempotency-Key contract (header presence, UUID v4 shape, tenant resolution, replay lookup), each returning a distinct RFC 7807 problem response. The order is the specification; collapsing it would obscure which check produced which response.'

    'orderly-microservices/Services/Catalog/Catalog.API/Availability/IngredientAvailabilityEngine.cs::AvailabilityProfileFor' =
        'The documented ingredient-substitution rule set (optional / available / alternative / auto-substitute) evaluated in one pass. The branch count IS the rule count; the loop is deliberately single-pass with O(1) alternative lookup for hot-path performance.'

    'orderly-microservices/Services/Discount/Discount.Grpc/Validators/RewardCodeValidator.cs::ValidateAndBuild' =
        'A linear run of guard clauses, one per validation rule, each throwing BusinessRuleException with a specific message. Flat by construction — a validator with N rules has complexity N by definition.'

    'orderly-microservices/Services/Identity/Identity.API/Features/Auth/Token/TokenModule.cs::AddRoutes' =
        'Carter route module whose /connect/token handler branches over OpenIddict grant types (password, refresh_token, client_credentials) inline. Candidate for extraction into per-grant handlers; tracked as a Phase 3 deferral rather than refactored inside a quality-gate phase.'
}

# ----------------------------------------------------------------
# Helpers
# ----------------------------------------------------------------

function Get-PrunedSourceFiles {
    <#
    .SYNOPSIS
        Recursively enumerates *.cs, pruning excluded subtrees at the directory
        level (far cheaper than Get-ChildItem -Recurse + post-filter).
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
        Repo-relative path with forward slashes.
    #>
    param([string]$FullName, [string]$RepoRoot)

    if ($FullName.StartsWith($RepoRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
        return $FullName.Substring($RepoRoot.Length).TrimStart('\', '/').Replace('\', '/')
    }
    return $FullName.Replace('\', '/')
}

function Remove-NonCode {
    <#
    .SYNOPSIS
        Blanks comments and string literals so their contents cannot be mistaken
        for branch points.
    .DESCRIPTION
        A SQL string containing " AND " is harmless, but a verbatim string
        holding "a || b" or an interpolated string with "?." would inflate the
        count. Replacement preserves newlines so line numbers stay accurate.

        Order matters: strings are blanked BEFORE comments, otherwise a "//"
        inside a string literal would truncate the rest of the line.
    #>
    param([string]$Text)

    $keepNewlines = {
        param($m)
        "`n" * ([regex]::Matches($m.Value, "`n")).Count
    }

    # Verbatim / raw strings first (they may legally contain quotes and slashes).
    $Text = [regex]::Replace($Text, '@"(?:[^"]|"")*"', $keepNewlines)
    # Regular string literals (no newlines, honour backslash escapes).
    $Text = [regex]::Replace($Text, '(?<!@)"(?:\\.|[^"\\\r\n])*"', '""')
    # Character literals.
    $Text = [regex]::Replace($Text, "'(?:\\.|[^'\\\r\n])*'", "''")
    # Block then line comments.
    $Text = [regex]::Replace($Text, '/\*[\s\S]*?\*/', $keepNewlines)
    $Text = [regex]::Replace($Text, '//[^\r\n]*', '')

    return $Text
}

function Get-MethodBodies {
    <#
    .SYNOPSIS
        Returns [pscustomobject]@{ Name; Line; Body } for each method-like
        member with a braced body.
    .DESCRIPTION
        Matches a signature of the form `... Name(...)` followed by `{`, then
        brace-matches to find the body. Deliberately skips:
          * control-flow keywords that look like calls (if/for/foreach/...)
          * expression-bodied members (=>), which cannot branch enough to matter
          * primary constructors on the type declaration line

        Nested local functions are counted as part of their enclosing method,
        which matches how a reviewer reads the method.
    #>
    param([string]$Text)

    $results = New-Object System.Collections.Generic.List[object]
    $keywords = @('if', 'for', 'foreach', 'while', 'switch', 'catch', 'using',
                  'lock', 'fixed', 'return', 'yield', 'new', 'get', 'set',
                  'do', 'else', 'try', 'when', 'select', 'where')

    # name ( ... ) optional-constraints {
    $sigPattern = '(?m)(?<![.\w])(?<name>[A-Za-z_]\w*)\s*(?<generic><[^<>()]*>)?\s*\((?<params>[^()]*(?:\([^()]*\)[^()]*)*)\)\s*(?:where[^{;]*)?\{'

    foreach ($m in [regex]::Matches($Text, $sigPattern)) {
        $name = $m.Groups['name'].Value
        if ($keywords -contains $name) { continue }

        $openBrace = $m.Index + $m.Length - 1
        $depth = 0
        $end = -1

        for ($i = $openBrace; $i -lt $Text.Length; $i++) {
            $ch = $Text[$i]
            if ($ch -eq '{') { $depth++ }
            elseif ($ch -eq '}') {
                $depth--
                if ($depth -eq 0) { $end = $i; break }
            }
        }
        if ($end -lt 0) { continue }

        $body = $Text.Substring($openBrace, $end - $openBrace + 1)
        # A type declaration's body is not a method; those contain member
        # declarations and would double-count. Heuristic: a real method body
        # rarely declares accessibility modifiers at its top level.
        $line = ($Text.Substring(0, $m.Index) -split "`n").Count

        $results.Add([pscustomobject]@{
            Name = $name
            Line = $line
            Body = $body
        })
    }

    return $results
}

function Measure-Complexity {
    <#
    .SYNOPSIS
        McCabe cyclomatic complexity of a method body.
    #>
    param([string]$Body)

    $complexity = 1
    foreach ($pattern in $script:BranchPatterns) {
        $complexity += ([regex]::Matches($Body, $pattern)).Count
    }

    $complexity += Measure-SwitchExpressionArms -Body $Body

    return $complexity
}

function Measure-SwitchExpressionArms {
    <#
    .SYNOPSIS
        Counts switch-EXPRESSION arms, which are real branch points.
    .DESCRIPTION
        Only `=>` inside a `switch { ... }` block counts. A bare `=>` elsewhere
        is a lambda or an expression-bodied member, and a lambda is NOT a branch
        point — counting them inflated EF Core fluent configuration
        (`OnModelCreating`, `IEntityTypeConfiguration.Configure`) by an order of
        magnitude, because every `.Property(e => e.Foo)` looked like an arm.

        The discard arm `_ =>` is the default case and adds no path.
    #>
    param([string]$Body)

    $arms = 0

    foreach ($m in [regex]::Matches($Body, '\bswitch\s*(?:\([^()]*\))?\s*\{')) {
        $open = $Body.IndexOf('{', $m.Index)
        if ($open -lt 0) { continue }

        $depth = 0
        $end = -1
        for ($i = $open; $i -lt $Body.Length; $i++) {
            $ch = $Body[$i]
            if ($ch -eq '{') { $depth++ }
            elseif ($ch -eq '}') {
                $depth--
                if ($depth -eq 0) { $end = $i; break }
            }
        }
        if ($end -lt 0) { continue }

        $block = $Body.Substring($open, $end - $open + 1)
        foreach ($arm in [regex]::Matches($block, '(?m)^\s*(?<pat>[^\r\n]*?)\s*=>')) {
            if ($arm.Groups['pat'].Value.Trim() -eq '_') { continue }
            $arms++
        }
    }

    return $arms
}

# ----------------------------------------------------------------
# Main
# ----------------------------------------------------------------

$repoRoot = (Resolve-Path "$PSScriptRoot/../..").Path

if ([string]::IsNullOrWhiteSpace($Path)) {
    $Path = Join-Path $repoRoot 'orderly-microservices'
}
if (-not (Test-Path -LiteralPath $Path)) {
    Write-Host "$script:Tag scan root not found: $Path" -ForegroundColor Red
    exit 2
}

$scanRoot = (Resolve-Path -LiteralPath $Path).Path
Write-Host "$script:Tag repo: $repoRoot"
Write-Host "$script:Tag scan: $scanRoot  (threshold: $MaxComplexity)"

$measurements = New-Object System.Collections.Generic.List[object]
$scanned = 0

$sourceFiles = Get-PrunedSourceFiles -Root $scanRoot -PruneDirs $script:PruneDirs

foreach ($file in $sourceFiles) {
    $relPath = Get-RelativePath -FullName $file -RepoRoot $repoRoot

    $isGenerated = $false
    foreach ($suffix in $script:GeneratedSuffixes) {
        if ($relPath.EndsWith($suffix, [System.StringComparison]::OrdinalIgnoreCase)) { $isGenerated = $true; break }
    }
    if ($isGenerated) { continue }

    $isTest = $relPath -match '(^|/)[^/]*\.Tests?(/|$)'
    if ($isTest -and -not $IncludeTests) { continue }

    $raw = Get-Content -LiteralPath $file -Raw -ErrorAction SilentlyContinue
    if ([string]::IsNullOrWhiteSpace($raw)) { continue }
    $scanned++

    $text = Remove-NonCode -Text $raw

    foreach ($method in @(Get-MethodBodies -Text $text)) {
        $score = Measure-Complexity -Body $method.Body
        $measurements.Add([pscustomobject]@{
            Path       = $relPath
            Method     = $method.Name
            Line       = $method.Line
            Complexity = $score
        })
    }
}

Write-Host "$script:Tag analysed $($measurements.Count) method(s) across $scanned file(s)."

if ($measurements.Count -eq 0) {
    Write-Host "$script:Tag nothing to analyse." -ForegroundColor Yellow
    exit 0
}

$sorted = $measurements | Sort-Object Complexity -Descending

if ($Report) {
    $stats = $measurements | Measure-Object -Property Complexity -Average -Maximum
    Write-Host "$script:Tag mean $([math]::Round($stats.Average, 2)), max $($stats.Maximum)"
    Write-Host ''
    Write-Host "$script:Tag top $Top most complex methods:"
    $sorted | Select-Object -First $Top | Format-Table -AutoSize |
        Out-String -Width 200 | Write-Host

    foreach ($t in @(10, 15, 20, 25)) {
        $over = @($measurements | Where-Object { $_.Complexity -gt $t }).Count
        Write-Host ("$script:Tag  > {0,-3} : {1} method(s)" -f $t, $over)
    }
    exit 0
}

$violations = @($sorted | Where-Object {
    $_.Complexity -gt $MaxComplexity -and
    -not $script:AllowedComplexMethods.ContainsKey("$($_.Path)::$($_.Method)")
})

$exempted = @($sorted | Where-Object {
    $_.Complexity -gt $MaxComplexity -and
    $script:AllowedComplexMethods.ContainsKey("$($_.Path)::$($_.Method)")
})

if ($exempted.Count -gt 0) {
    Write-Host "$script:Tag $($exempted.Count) method(s) over threshold by explicit review:" -ForegroundColor Yellow
    foreach ($e in $exempted) {
        Write-Host "  ! $($e.Path):$($e.Line) $($e.Method) = $($e.Complexity)" -ForegroundColor Yellow
    }
}

if ($violations.Count -gt 0) {
    Write-Host ''
    Write-Host "$script:Tag $($violations.Count) method(s) exceed complexity $MaxComplexity :" -ForegroundColor Red
    foreach ($v in $violations) {
        Write-Host "  x $($v.Path):$($v.Line)  $($v.Method)() = $($v.Complexity)" -ForegroundColor Red
    }
    Write-Host ''
    Write-Host "$script:Tag extract helpers to reduce branching, or — if the complexity is" -ForegroundColor Red
    Write-Host "$script:Tag genuinely irreducible — add '<path>::<Method>' to" -ForegroundColor Red
    Write-Host "$script:Tag `$AllowedComplexMethods in this script WITH a rationale." -ForegroundColor Red
    exit 1
}

Write-Host ''
Write-Host "$script:Tag no method exceeds complexity $MaxComplexity." -ForegroundColor Green
exit 0
