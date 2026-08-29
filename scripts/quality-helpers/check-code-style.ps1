# --------------------------------------------------------------
# check-code-style.ps1 — local mirror of the style-gate CI grep
# --------------------------------------------------------------
# Enforces the rules that IDE analyzers cannot (== null / != null,
# empty-string comparisons, braceless control flow). Companion to
# the analyzers job in .github/workflows/style-gate.yml — both
# must pass for the gate to be green. Run from repo root:
#
#   pwsh scripts/quality-helpers/check-code-style.ps1
#
# Wired into scripts/phase-guard.ps1 §22 (Phase 5 of
# .agents/plan/platform/CODE_STYLE_ITERATION_PLAN.md).
# --------------------------------------------------------------

$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path "$PSScriptRoot\..\..").Path
Set-Location $repoRoot

$searchRoot = Join-Path $repoRoot 'orderly-microservices'

# Filters shared with the CI null-check job. Each pattern is a
# PowerShell -match regex; the helper below tests them with
# -notmatch in a single call so we don't need to escape alternation.
$excludes = @(
    '[\\/]obj[\\/]',
    '[\\/]bin[\\/]',
    '[\\/]\.git[\\/]',
    'Migrations[\\/]',
    '\.Designer\.cs$',
    '\.g\.cs$',
    # Test-project exemption per .editorconfig ([**/*Tests/**/*.cs]).
    # csharp_style_var_* and csharp_prefer_braces are :suggestion there.
    '\.Tests[\\/]',
    # Tripwire file — intentionally violates IDE0008/IDE0011/SA1503 to
    # prove the analyzer gate works. Guarded by `#if STYLE_GATE_TEST` so
    # it does not compile into the normal build. The exclusion is
    # belt-and-suspenders in case someone forgets the `#if`.
    'StyleRegressionMarker\.cs$',
    # EF Core LINQ expression-tree carve-outs (CS8122 — pattern-matching
    # is not allowed inside Expression<Func<>>). The plan §Phase 2
    # explicitly documents these as MUST-KEEP-==-NULL sites. Files
    # listed here are the production-code carriers of EF Core LINQ
    # (`IQueryable<T>.Where`, `.Select`, `.OrderBy`, `HasQueryFilter`,
    # `HasConversion`, `ValueComparer`) plus Marten LINQ and FluentValidation
    # `.Must(...)`/`RuleFor(...)` expression trees. New expression-tree
    # sites must be added here AND documented in the plan.
    'OrderActivityConfiguration\.cs$',
    'OrderConfiguration\.cs$',
    'OrderItemConfiguration\.cs$',
    'KitchenTicketConfiguration\.cs$',
    'DiscountContext\.cs$',
    'MenuItemChangedConsumer\.cs$',
    'OrderCreatedConsumer\.cs$',
    'FeedbackSubmittedConsumer\.cs$',
    'DiscountRuleService\.cs$',
    'DiscountExpirySweepService\.cs$',
    'RewardCodeService\.cs$',
    'CheckoutBasketOutboxDispatcher\.cs$',
    'StoreBasketHandler\.cs$',
    'ClaimsTransformer\.cs$',
    'ValidationBehavior\.cs$',
    'MenuReader\.cs$',
    'ReservationNoShowJob\.cs$',
    'ReservationReminderJob\.cs$',
    'WalkInNoShowJob\.cs$'
)
$excludePattern = ($excludes -join '|')
$filter = { $_.FullName -notmatch $excludePattern }

$violations = @()

# -- 1. == null / != null outside expression trees -----------------------
# EF Core LINQ expression trees cannot use is null (CS8122). The plan
# documents the surviving ~35 sites (Phase 2 carve-out). The grep below
# is intentionally loose — it flags any hit and the operator reviews.
$nullHits = Get-ChildItem -Path $searchRoot -Recurse -Include '*.cs' |
    Where-Object $filter |
    Select-String -Pattern '(\!\=|\=\=)\s*null' |
    Where-Object { $_.Line -notmatch '^\s*//' -and $_.Line -notmatch '^\s*///' }
if ($nullHits) {
    foreach ($h in $nullHits) {
        $violations += "[== null / != null] $($h.Path):$($h.LineNumber) $($h.Line.Trim())"
    }
}

# -- 2. empty-string comparisons -----------------------------------------
# AGENTS.md rule 6: prefer string.IsNullOrEmpty / string.Empty / IsNullOrWhiteSpace.
$emptyHits = Get-ChildItem -Path $searchRoot -Recurse -Include '*.cs' |
    Where-Object $filter |
    Select-String -Pattern '(\=\=|\!\=)\s*""' |
    Where-Object { $_.Line -notmatch '^\s*//' -and $_.Line -notmatch '^\s*///' }
if ($emptyHits) {
    foreach ($h in $emptyHits) {
        $violations += "[empty-string] $($h.Path):$($h.LineNumber) $($h.Line.Trim())"
    }
}

# -- 3. braceless control flow -------------------------------------------
# AGENTS.md rule 5: always use braces for if / for / foreach / while.
# Pattern matches single-line braceless only: control keyword + (...) +
# non-`{` body + `;` at end of line. Multi-line `if (cond)` followed by
# `{` on the next line is NOT braceless and does not match.
$bracelessHits = Get-ChildItem -Path $searchRoot -Recurse -Include '*.cs' |
    Where-Object $filter |
    Select-String -Pattern '^\s*(if|else if|for|foreach|while)\s*\(.*\)[^{]*;\s*$' |
    Where-Object { $_.Line -notmatch '^\s*//' -and $_.Line -notmatch '^\s*///' }
if ($bracelessHits) {
    foreach ($h in $bracelessHits) {
        $violations += "[braceless] $($h.Path):$($h.LineNumber) $($h.Line.Trim())"
    }
}

if ($violations.Count -gt 0) {
    Write-Host "❌ Code-style violations found ($($violations.Count)):" -ForegroundColor Red
    foreach ($v in $violations) {
        Write-Host "  $v" -ForegroundColor Red
    }
    exit 1
}

Write-Host "✅ Code-style gate clean (no == null, empty-string, or braceless sites)" -ForegroundColor Green
exit 0
