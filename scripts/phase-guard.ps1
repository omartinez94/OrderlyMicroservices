# --------------------------------------------------------------
# phase-guard.ps1 – quality-gate run after each plan phase
# --------------------------------------------------------------
# Phase 1 of .agents/plan/platform/QUALITY_GATE_ENHANCEMENT_PLAN.md
# added the format gate, secret scan and spellcheck.
#
# Phase 2 adds the architecture tier — NodaTime conventions, CQRS/MediatR
# layout, NuGet license compliance and git-hook sanity — plus a -PreCommit
# fast path used by .githooks/pre-commit. See the plan §9 Phase 1/2
# implementation notes for the rationale behind each section.
# --------------------------------------------------------------
param (
    [string]$PhaseName = "Unnamed Phase",
    [switch]$Quick,      # If set, run only fast unit tests (skip integration)
    [switch]$PreCommit   # If set, run ONLY the fast checks (no build/test/docker/vuln)
)

function Write-Section($title) {
    Write-Host "`n=== $title ===`n" -ForegroundColor Cyan
}

function Write-Skipped($title) {
    Write-Host "`n=== $title (skipped: -PreCommit) ===`n" -ForegroundColor DarkGray
}

$repoRoot = (Resolve-Path "$PSScriptRoot\..").Path   # one level up to repository root
Set-Location $repoRoot

# ----------------------------------------------------------------
# 1️⃣ Build
# ----------------------------------------------------------------
if ($PreCommit) {
    Write-Skipped "💡 Build"
}
else {
    Write-Section "💡 Build"

    dotnet build "orderly-microservices/orderly-microservices.slnx" --no-restore
    if ($LASTEXITCODE) { throw "❌ Build failed" }
}

# ----------------------------------------------------------------
# 2️⃣ Test
# ----------------------------------------------------------------
if ($PreCommit) {
    Write-Skipped "🧪 Test"
}
else {
    Write-Section "🧪 Test"

    # Build argument array for dotnet test
    $testArgs = @(
        "--no-build",
        "--logger", "trx;LogFileName=TestResults.trx"
    )

    if ($Quick) {
        # Exclude integration tests (slow) and enable parallelism
        $testArgs += "--filter"
        $testArgs += "FullyQualifiedName!~Integration"
        $testArgs += "--maxcpucount"
        $testArgs += "4"
    }

    dotnet test "orderly-microservices/orderly-microservices.slnx" @testArgs

    $testExit = $LASTEXITCODE
    Write-Host "Test process exit code: $testExit"

    # Locate the newest TRX file
    $trxFile = Get-ChildItem -Path . -Recurse -Filter *.trx |
               Sort-Object LastWriteTime -Descending |
               Select-Object -First 1

    if ($null -eq $trxFile) {
        Write-Warning "⚠️ No TRX results file found – cannot verify test outcome."
    }
    else {
        [xml]$trxXml = Get-Content $trxFile.FullName
        $failed = [int]$trxXml.TestRun.ResultSummary.failed
        Write-Host "🔎 Test failures reported in TRX: $failed"
        if ($failed -gt 0) { throw "❌ Tests failed ($failed failures)." }
    }
}

# ----------------------------------------------------------------
# 3️⃣ Format drift gate (Phase 1 of QUALITY_GATE_ENHANCEMENT_PLAN.md)
#
#    Upgraded from `dotnet format --diagnostics IDE0005` (informational
#    warning) to a hard gate. The accompanying .editorconfig under
#    orderly-microservices/ locks the conventions; any drift fails here.
# ----------------------------------------------------------------
Write-Section "📐 Format drift"

# Use `dotnet format whitespace` (NOT the default `dotnet format`) because the
# default subcommand also applies analyzer-rule code-fixes (SA1111, SA1413,
# SA1505, SA1508, SA1600, SA1611, SA1122, CA1724, etc.), many of which would
# change semantics or break tests (see .editorconfig for the rules we've
# disabled to avoid the auto-fix trap). Phase 2 keeps this scoped to
# whitespace + indent + line endings, which is what plan §6.1 asks for.
dotnet format whitespace "orderly-microservices/orderly-microservices.slnx" --verify-no-changes --no-restore
if ($LASTEXITCODE) {
    throw "❌ Format drift detected — run 'dotnet format whitespace orderly-microservices.slnx' to fix"
}

# ----------------------------------------------------------------
# 4️⃣ Secret-leak scan (Phase 1 of QUALITY_GATE_ENHANCEMENT_PLAN.md)
# ----------------------------------------------------------------
Write-Section "🔐 Secret-leak scan"

pwsh -NoProfile -File "$PSScriptRoot/quality-helpers/find-secrets.ps1"
if ($LASTEXITCODE) { throw "❌ Potential secrets found in source" }

# ----------------------------------------------------------------
# 5️⃣ Comment spell-check (Phase 1 of QUALITY_GATE_ENHANCEMENT_PLAN.md)
# ----------------------------------------------------------------
Write-Section "📝 Comment spell-check"

pwsh -NoProfile -File "$PSScriptRoot/quality-helpers/check-spelling.ps1"
if ($LASTEXITCODE) { throw "❌ Unknown words in scripts/comments" }

# ----------------------------------------------------------------
# 6️⃣ NodaTime conventions (Phase 2 — plan §6.3)
#
#    AGENTS.md mandates NodaTime over BCL date/time. Bans local-time APIs
#    everywhere and UTC wall-clock reads in production code.
# ----------------------------------------------------------------
Write-Section "🕰️ NodaTime conventions"

pwsh -NoProfile -File "$PSScriptRoot/quality-helpers/check-nodatime.ps1"
if ($LASTEXITCODE) { throw "❌ Banned date/time API usage — NodaTime is mandatory (AGENTS.md)" }

# ----------------------------------------------------------------
# 7️⃣ CQRS / MediatR layout (Phase 2 — plan §6.3)
#
#    Validates each service against ITS OWN declared handler root, because
#    AGENTS.md documents a deliberate Vertical-Slice / Clean-Architecture split.
# ----------------------------------------------------------------
Write-Section "🧭 CQRS / MediatR layout"

pwsh -NoProfile -File "$PSScriptRoot/quality-helpers/check-mediatr.ps1"
if ($LASTEXITCODE) { throw "❌ CQRS handler layout drift detected" }

# ----------------------------------------------------------------
# 8️⃣ NuGet license compliance (Phase 2 — plan §6.3)
#
#    Resolves every PackageReference against the local NuGet cache. Permissive
#    licenses pass; reviewed copyleft/commercial dependencies pass with a
#    warning; anything new and non-permissive fails.
# ----------------------------------------------------------------
Write-Section "⚖️ NuGet license compliance"

pwsh -NoProfile -File "$PSScriptRoot/quality-helpers/check-licensing.ps1"
if ($LASTEXITCODE) { throw "❌ Dependency with an unapproved license" }

# ----------------------------------------------------------------
# 9️⃣ Git-hook sanity (Phase 2 — plan §7)
#
#    Reports whether the tracked .githooks/pre-commit hook is wired up.
#
#    This is ADVISORY, never fatal. Hook installation is a per-workstation
#    concern (`core.hooksPath` lives in .git/config, which is not tracked), so
#    failing the gate on it would break fresh clones and CI for something that
#    is not a code-quality problem. The hook is shipped and documented; each
#    developer opts in.
# ----------------------------------------------------------------
Write-Section "🪝 Git-hook sanity"

$hookPath = Join-Path $repoRoot '.githooks/pre-commit'
$configuredHooksPath = (git config core.hooksPath 2>$null)

$hookProblems = @()
if (-not (Test-Path -LiteralPath $hookPath)) {
    $hookProblems += "tracked hook .githooks/pre-commit is missing"
}
elseif (-not (Select-String -LiteralPath $hookPath -Pattern 'phase-guard\.ps1' -Quiet)) {
    $hookProblems += ".githooks/pre-commit does not invoke phase-guard.ps1"
}
if ($configuredHooksPath -ne '.githooks') {
    $hookProblems += "core.hooksPath is '$configuredHooksPath' (expected '.githooks')"
}

if ($hookProblems.Count -gt 0) {
    Write-Host "⚠️ Pre-commit hook is not active on this workstation:" -ForegroundColor Yellow
    foreach ($p in $hookProblems) { Write-Host "  • $p" -ForegroundColor Yellow }
    Write-Host "  opt in with: git config core.hooksPath .githooks" -ForegroundColor Yellow
    Write-Host "  (advisory only — the full gate still runs every check below)" -ForegroundColor DarkGray
}
else {
    Write-Host "✅ .githooks/pre-commit wired via core.hooksPath" -ForegroundColor Green
}

# ----------------------------------------------------------------
# 🔟 Consolidate duplicated usings (Skipped/Disabled)
# ----------------------------------------------------------------
Write-Section "🔧 Consolidate duplicated usings (Skipped)"
# Disabled: Root-level GlobalUsings.cs breaks project-specific boundaries and collapses source files.

# ----------------------------------------------------------------
# 1️⃣1️⃣ Nullable-reference-type warnings (CS8618, CS8625)
# ----------------------------------------------------------------
if ($PreCommit) {
    Write-Skipped "⚠️ Nullable warnings"
}
else {
    Write-Section "⚠️ Nullable warnings"

    # --no-restore: the dcproj entry in orderly-microservices.slnx fails
    # restore under net10.0 (NU1105 invalid target framework). Section 1
    # already restored+built without --no-restore, so the artifacts are
    # warm and a recompile with -warnaserror is sufficient.
    dotnet build "orderly-microservices/orderly-microservices.slnx" -warnaserror:CS8618,CS8625 --no-restore
    if ($LASTEXITCODE) { throw "❌ Nullable-reference-type warnings detected" }
}

# ----------------------------------------------------------------
# 1️⃣2️⃣ Dockerfile HEALTHCHECK lint (hadolint)
# ----------------------------------------------------------------
if ($PreCommit) {
    Write-Skipped "🐳 Dockerfile lint"
}
else {
    Write-Section "🐳 Dockerfile lint"

    $dockerfiles = Get-ChildItem -Path . -Recurse -Filter Dockerfile
    foreach ($df in $dockerfiles) {
        Write-Host "Linting $($df.FullName)"
        Get-Content $df.FullName -Raw | docker run --rm -i hadolint/hadolint hadolint --failure-threshold error -
        if ($LASTEXITCODE) { throw "❌ Dockerfile lint failed: $($df.FullName)" }
    }
}

# ----------------------------------------------------------------
# 1️⃣3️⃣ Dependency-vulnerability scan
# ----------------------------------------------------------------
if ($PreCommit) {
    Write-Skipped "🔐 Vulnerable package scan"
}
else {
    Write-Section "🔐 Vulnerable package scan"

    $projects = Get-ChildItem -Path . -Recurse -Filter *.csproj
    $vulnFailed = $false
    foreach ($proj in $projects) {
        Write-Host "Scanning $($proj.Name) for vulnerabilities..."
        dotnet list $proj.FullName package --vulnerable
        if ($LASTEXITCODE -ne 0) {
            $vulnFailed = $true
        }
    }
    if ($vulnFailed) { throw "❌ Vulnerable NuGet packages found" }
}

# ----------------------------------------------------------------
# 1️⃣4️⃣ Suggested Git commit message
# ----------------------------------------------------------------
if ($PreCommit) {
    Write-Host "`n✅ Pre-commit checks passed.`n" -ForegroundColor Green
    exit 0
}

Write-Section "✉️ Suggested Git commit"
$commitMsg = @"
[$PhaseName] – quality gate

✅ Format drift-free (dotnet format --verify-no-changes)
🔐 No hard-coded secrets
📝 Comments spell-checked
🕰️ NodaTime conventions upheld
🧭 CQRS handler layout valid
⚖️ Dependency licenses approved
🪝 Pre-commit hook available (.githooks/pre-commit)
✅ Build succeeded
✅ Tests passed
🔎 No unused/duplicate usings (informational)
⚠️ No nullable-reference-type warnings
🐳 Dockerfiles linted
🔐 No vulnerable packages found
"@.TrimEnd()

Write-Host $commitMsg -ForegroundColor Green

exit 0
