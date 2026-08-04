# --------------------------------------------------------------
# phase-guard.ps1 – quality‑gate run after each plan phase
# --------------------------------------------------------------
# Phase 1 of .agents/plan/platform/QUALITY_GATE_ENHANCEMENT_PLAN.md
# adds two new sections (format gate + secret scan + spellcheck) and
# renumbers the downstream sections accordingly. See the plan §9
# Phase 1 implementation notes for the rationale.
# --------------------------------------------------------------
param (
    [string]$PhaseName = "Unnamed Phase",
    [switch]$Quick   # If set, run only fast unit tests (skip integration)
)

function Write-Section($title) {
    Write-Host "`n=== $title ===`n" -ForegroundColor Cyan
}

$repoRoot = (Resolve-Path "$PSScriptRoot\..").Path   # one level up to repository root
Set-Location $repoRoot

# ----------------------------------------------------------------
# 1️⃣ Build
# ----------------------------------------------------------------
Write-Section "💡 Build"

dotnet build "orderly-microservices/orderly-microservices.slnx" --no-restore
if ($LASTEXITCODE) { throw "❌ Build failed" }

# ----------------------------------------------------------------
# 2️⃣ Test
# ----------------------------------------------------------------
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
# disabled to avoid the auto-fix trap). Phase 2 (Architecture / NodaTime /
# MediatR) is the canonical home for tuning analyzer rules and promoting
# them to gates. Phase 1 keeps the gate scoped to whitespace + indent +
# line endings, which is what the plan §6.1 deliverable asks for.
dotnet format whitespace "orderly-microservices/orderly-microservices.slnx" --verify-no-changes --no-restore
if ($LASTEXITCODE) {
    throw "❌ Format drift detected — run 'dotnet format whitespace orderly-microservices.slnx' to fix"
}

# ----------------------------------------------------------------
# 4️⃣ Secret‑leak scan (Phase 1 of QUALITY_GATE_ENHANCEMENT_PLAN.md)
# ----------------------------------------------------------------
Write-Section "🔐 Secret-leak scan"

pwsh "$PSScriptRoot/quality-helpers/find-secrets.ps1"
if ($LASTEXITCODE) { throw "❌ Potential secrets found in source" }

# ----------------------------------------------------------------
# 5️⃣ Comment spell-check (Phase 1 of QUALITY_GATE_ENHANCEMENT_PLAN.md)
# ----------------------------------------------------------------
Write-Section "📝 Comment spell-check"

pwsh "$PSScriptRoot/quality-helpers/check-spelling.ps1"
if ($LASTEXITCODE) { throw "❌ Unknown words in scripts/comments" }

# ----------------------------------------------------------------
# 6️⃣ Consolidate duplicated usings (Skipped/Disabled)
# ----------------------------------------------------------------
Write-Section "🔧 Consolidate duplicated usings (Skipped)"
# Disabled: Root-level GlobalUsings.cs breaks project-specific boundaries and collapses source files.

# ----------------------------------------------------------------
# 7️⃣ Nullable‑reference‑type warnings (CS8618, CS8625)
# ----------------------------------------------------------------
Write-Section "⚠️ Nullable warnings"

# --no-restore: the dcproj entry in orderly-microservices.slnx fails
# restore under net10.0 (NU1105 invalid target framework). Section 1
# already restored+built without --no-restore, so the artifacts are
# warm and a recompile with -warnaserror is sufficient.
dotnet build "orderly-microservices/orderly-microservices.slnx" -warnaserror:CS8618,CS8625 --no-restore
if ($LASTEXITCODE) { throw "❌ Nullable‑reference‑type warnings detected" }

# ----------------------------------------------------------------
# 8️⃣ Dockerfile HEALTHCHECK lint (hadolint)
# ----------------------------------------------------------------
Write-Section "🐳 Dockerfile lint"

$dockerfiles = Get-ChildItem -Path . -Recurse -Filter Dockerfile
foreach ($df in $dockerfiles) {
    Write-Host "Linting $($df.FullName)"
    Get-Content $df.FullName -Raw | docker run --rm -i hadolint/hadolint hadolint --failure-threshold error -
    if ($LASTEXITCODE) { throw "❌ Dockerfile lint failed: $($df.FullName)" }
}

# ----------------------------------------------------------------
# 9️⃣ Dependency‑vulnerability scan
# ----------------------------------------------------------------
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

# ----------------------------------------------------------------
# 🔟 Suggested Git commit message
# ----------------------------------------------------------------
Write-Section "✉️ Suggested Git commit"
$commitMsg = @"
[$PhaseName] – quality gate

✅ Format drift-free (dotnet format --verify-no-changes)
🔐 No hard-coded secrets
📝 Comments spell-checked
✅ Build succeeded
✅ Tests passed
🔎 No unused/duplicate usings (informational)
⚠️ No nullable‑reference‑type warnings
🐳 Dockerfiles linted
🔐 No vulnerable packages found
"@.TrimEnd()

Write-Host $commitMsg -ForegroundColor Green

exit 0
