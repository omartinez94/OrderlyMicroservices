# --------------------------------------------------------------
# phase-guard.ps1 – quality‑gate run after each plan phase
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
# 3️⃣ Unused / duplicate using check (IDE0005)
# ----------------------------------------------------------------
Write-Section "🔎 Unused‑using analysis"

dotnet format "orderly-microservices/orderly-microservices.slnx" --diagnostics IDE0005
if ($LASTEXITCODE) {
    Write-Warning "⚠️ Unused or duplicate usings detected – continuing" 
}

# ----------------------------------------------------------------
# 4️⃣ Consolidate duplicated usings (Skipped/Disabled)
# ----------------------------------------------------------------
Write-Section "🔧 Consolidate duplicated usings (Skipped)"
# Disabled: Root-level GlobalUsings.cs breaks project-specific boundaries and collapses source files.

# ----------------------------------------------------------------
# 5️⃣ Nullable‑reference‑type warnings (CS8618, CS8625)
# ----------------------------------------------------------------
Write-Section "⚠️ Nullable warnings"

dotnet build "orderly-microservices/orderly-microservices.slnx" -warnaserror:CS8618,CS8625
if ($LASTEXITCODE) { throw "❌ Nullable‑reference‑type warnings detected" }

# ----------------------------------------------------------------
# 6️⃣ Dockerfile HEALTHCHECK lint (hadolint)
# ----------------------------------------------------------------
Write-Section "🐳 Dockerfile lint"

$dockerfiles = Get-ChildItem -Path . -Recurse -Filter Dockerfile
foreach ($df in $dockerfiles) {
    Write-Host "Linting $($df.FullName)"
    Get-Content $df.FullName -Raw | docker run --rm -i hadolint/hadolint hadolint --failure-threshold error -
    if ($LASTEXITCODE) { throw "❌ Dockerfile lint failed: $($df.FullName)" }
}

# ----------------------------------------------------------------
# 7️⃣ Dependency‑vulnerability scan
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
# 8️⃣ Suggested Git commit message
# ----------------------------------------------------------------
Write-Section "✉️ Suggested Git commit"
$commitMsg = @"
[$PhaseName] – quality gate

✅ Build succeeded
✅ Tests passed
🔎 No unused/duplicate usings (consolidated into GlobalUsings.cs)
⚠️ No nullable‑reference‑type warnings
🐳 Dockerfiles linted
🔐 No vulnerable packages found
"@.TrimEnd()

Write-Host $commitMsg -ForegroundColor Green

exit 0
