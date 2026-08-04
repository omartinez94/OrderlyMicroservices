<#
.SYNOPSIS
    Local secret scanner for the Orderly .NET 10 microservices solution.

.DESCRIPTION
    Walks the repository and flags high-signal secret patterns
    (AWS access keys, GitHub PATs, OpenAI keys, Slack tokens, static JWTs,
    private key bodies). Path-based excludes avoid scanning known
    fixture files (appsettings*.json, docker-compose.override.*.yml,
    .env.example, *.Tests/**, test_e2e_auth.ps1). Known dev-credential
    placeholders are allow-listed inline so the scanner only flags NEW
    or production-shaped credentials.

    Phase 1 of .agents/plan/platform/QUALITY_GATE_ENHANCEMENT_PLAN.md —
    Formatting, Style & Secret Scanning. Invoked by scripts/phase-guard.ps1
    between the format gate (section 3) and the comment spell-check (section 5).

.PARAMETER Quick
    Reserved for future use. In Phase 1 the scanner is already fast
    (< 5 s for the current repo), so this switch is a no-op.

.PARAMETER ExtraIncludePaths
    Additional paths to scan (forward-slash globs relative to the repo
    root). Useful when CI wants to scan a focused subset.

.EXAMPLE
    pwsh ./scripts/quality-helpers/find-secrets.ps1
    # Scans the entire repository.

.EXAMPLE
    pwsh ./scripts/quality-helpers/find-secrets.ps1 -ExtraIncludePaths 'orderly-microservices/Services/Identity/Identity.API'
    # Adds Identity.API to the scan (in addition to the default walk).

.NOTES
    Exit codes:
        0 — no high-signal secrets found
        1 — one or more high-signal secrets detected
        2 — internal error (file walk failed, etc.)

    Style follows orderly-microservices/scripts/generate-basket-openapi.ps1:
    [CmdletBinding()], $ErrorActionPreference='Stop', block-comment header,
    [find-secrets] tag prefix on every Write-Host.
#>

[CmdletBinding()]
param(
    [switch]$Quick,
    [string[]]$ExtraIncludePaths = @()
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# ----------------------------------------------------------------
# Constants
# ----------------------------------------------------------------

$script:Tag = '[find-secrets]'

# Path-based excludes — files matching these globs are NEVER scanned.
$script:PathExcludes = @(
    '.git', 'node_modules', 'bin', 'obj', '.vs', '.vscode',
    'Generated Files', '.agents/notes',
    'appsettings.*.Local.json'
)

# Path-based includes — only files matching one of these globs are scanned.
# Everything else is skipped wholesale (the high-signal allowlist below
# only applies to files that ARE scanned).
$script:PathIncludes = @(
    '*.cs', '*.json', '*.yml', '*.yaml',
    '*.csproj', '*.props', '*.targets',
    '*.md', '*.ps1',
    '.env.example', '*.http', '*.rest'
)

# Files / paths that are exempt from scanning because they contain known
# dev/test fixtures. Matched against the relative path from repo root.
$script:KnownFixturePaths = @(
    '*/Tests/*', '*/.Tests/*', '*/.Dev.Tests/*',
    'test_e2e_auth.ps1',
    'appsettings.json', 'appsettings.Development.json',
    'docker-compose.override.dev.yml', 'docker-compose.override.prod.yml',
    '.env.example',
    '*.gitignore',
    'phase-guard.ps1',
    'scripts/phase-guard.ps1'
)

# High-signal regex patterns. A hit ALWAYS flags (the allowlist below
# does NOT override these). Each entry has a name and a regex.
$script:HighSignalPatterns = @(
    @{ Name = 'AWS Access Key';     Pattern = 'AKIA[0-9A-Z]{16}' },
    @{ Name = 'GitHub PAT';         Pattern = 'gh[pousr]_[A-Za-z0-9]{36,}' },
    @{ Name = 'OpenAI API Key';     Pattern = 'sk-[A-Za-z0-9]{32,}' },
    @{ Name = 'Slack Token';        Pattern = 'xox[baprs]-[0-9a-zA-Z-]+' },
    @{ Name = 'Static JWT';         Pattern = 'eyJ[A-Za-z0-9_-]{10,}\.eyJ[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]+' },
    @{ Name = 'Private Key Body';   Pattern = '-----BEGIN (RSA |EC |OPENSSH |DSA |ENCRYPTED |)PRIVATE KEY-----\s*\n[\s\S]{50,}' }
)

# Known dev/test credential placeholders that the allowlist recognises.
# These are matched against the FULL MATCH of a high-signal finding.
# If the matched substring equals one of these (or matches one of the
# wildcards below), the finding is suppressed.
$script:KnownDevCreds = @(
    'postgres', 'guest', 'YrPsswrd123456789', 'password123', 'redisdev',
    'changeit-please', 'replace-me-with-a-dev-only-*',
    'dev-only-shared-secret-*', 'test-pwd-12345',
    'YourStrong!Passw0rd', 'Admin@123456', 'weak', 'P@ssword1!'
)

# Subset of HighSignalPatterns that ONLY fire if the surrounding context
# is suspicious — e.g. the matched value is not just a placeholder.
# Applied to: AWS / GitHub / OpenAI / Slack / Static JWT.
$script:ContextAwarePatterns = @(
    'AWS Access Key', 'GitHub PAT', 'OpenAI API Key',
    'Slack Token', 'Static JWT'
)

# ----------------------------------------------------------------
# Helpers
# ----------------------------------------------------------------

function Test-ShouldScanPath {
    <#
    .SYNOPSIS
        Returns $true if the given relative path is in scope for scanning.
    #>
    param([string]$RelPath)

    foreach ($excl in $script:PathExcludes) {
        if ($RelPath -like "*${excl}*") { return $false }
    }
    foreach ($fixture in $script:KnownFixturePaths) {
        if ($RelPath -like $fixture) { return $false }
    }
    $leaf = Split-Path -Leaf $RelPath
    foreach ($inc in $script:PathIncludes) {
        if ($leaf -like $inc) { return $true }
    }
    return $false
}

function Test-IsAllowListed {
    <#
    .SYNOPSIS
        Returns $true if a finding's matched value matches a known dev-cred.
    #>
    param([string]$MatchValue)

    foreach ($cred in $script:KnownDevCreds) {
        if ($MatchValue -eq $cred) { return $true }
        if ($cred -like '*\*' -and $MatchValue -like $cred) { return $true }
    }
    return $false
}

function Test-IsYmlDefault {
    <#
    .SYNOPSIS
        Returns $true if the match is the default-clause of a ${VAR:-default}
        substitution in a .yml file.
    #>
    param(
        [string]$RelPath,
        [string]$Line
    )

    if ($RelPath -notlike '*.y*ml') { return $false }
    # Match strings of the form ${SOME_VAR:-some-value} where the value is the
    # candidate. The pattern looks for an unbraced `${VAR:-default}` substring.
    if ($Line -match '\$\{[A-Za-z_][A-Za-z0-9_]*:-([^\}]+)\}') {
        $default = $Matches[1]
        if ($Line -match [regex]::Escape($default)) {
            # Heuristic: if the candidate appears INSIDE a ${...:-...} default
            # clause AND equals the default value, suppress.
            if ($default -match '^(postgres|guest|YrPsswrd123456789|password123|redisdev|changeit-please|replace-me-with-a-dev-only-.+|dev-only-shared-secret-.+|test-pwd-12345|YourStrong!Passw0rd|Admin@123456|weak|P@ssword1!)$') {
                return $true
            }
        }
    }
    return $false
}

function Format-Finding {
    <#
    .SYNOPSIS
        Format a finding for stdout. Returns "relpath:line:name" lines.
    #>
    param(
        [string]$RelPath,
        [int]$LineNumber,
        [string]$Name,
        [string]$MatchValue
    )
    $truncated = if ($MatchValue.Length -gt 60) { $MatchValue.Substring(0, 57) + '...' } else { $MatchValue }
    return ("{0}:{1}:{2}  match=[{3}]" -f $RelPath, $LineNumber, $Name, $truncated)
}

# ----------------------------------------------------------------
# Main
# ----------------------------------------------------------------

$repoRoot = (Resolve-Path "$PSScriptRoot/../..").Path
Write-Host "$script:Tag repo: $repoRoot"


# Apply extra include paths (relative to repo root) to the default walk.
# Absolute paths are accepted as-is; relative paths are joined to the repo root.
$scanRoots = @($repoRoot)
foreach ($p in $ExtraIncludePaths) {
    if ([System.IO.Path]::IsPathRooted($p)) {
        $resolved = $p
    } else {
        $resolved = Join-Path $repoRoot $p
    }
    if (Test-Path -LiteralPath $resolved) {
        $scanRoots += (Resolve-Path -LiteralPath $resolved).Path
    } else {
        Write-Warning "$script:Tag ExtraIncludePath '$p' does not exist; skipping."
    }
}

$findings = New-Object System.Collections.Generic.List[string]
$fileCount = 0
$scannedCount = 0
$skippedCount = 0

foreach ($root in $scanRoots) {
    if (-not (Test-Path $root)) { continue }
    Get-ChildItem -Path $root -Recurse -File -Force -ErrorAction SilentlyContinue | ForEach-Object {
        $file = $_
        $fileCount++
        $relPath = if ($file.FullName.StartsWith($repoRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
            $file.FullName.Substring($repoRoot.Length).TrimStart('\', '/')
        } else {
            # Out-of-tree file (e.g. -ExtraIncludePaths on a temp file). Use the
            # full path so Test-ShouldScanPath still has something to compare.
            $file.FullName
        }
        if (-not (Test-ShouldScanPath -RelPath $relPath)) {
            $skippedCount++
            return
        }
        $scannedCount++

        $content = Get-Content -LiteralPath $file.FullName -Raw -ErrorAction SilentlyContinue
        if ($null -eq $content) { return }

        $lines = $content -split "`n"
        for ($i = 0; $i -lt $lines.Length; $i++) {
            $lineNum = $i + 1
            $line = $lines[$i]
            foreach ($pattern in $script:HighSignalPatterns) {
                $regexMatches = [regex]::Matches($line, $pattern.Pattern)
                foreach ($m in $regexMatches) {
                    $matchValue = $m.Value
                    if ($pattern.Name -in $script:ContextAwarePatterns) {
                        # Suppress if the matched value is a known dev cred.
                        if (Test-IsAllowListed -MatchValue $matchValue) { continue }
                        # Suppress if the match is inside a ${VAR:-default} yml clause.
                        if (Test-IsYmlDefault -RelPath $relPath -Line $line) { continue }
                    }
                    $findings.Add((Format-Finding -RelPath $relPath -LineNumber $lineNum -Name $pattern.Name -MatchValue $matchValue))
                }
            }
        }
    }
}

Write-Host "$script:Tag scanned $scannedCount of $fileCount files (skipped $skippedCount)"

if ($findings.Count -gt 0) {
    Write-Host "$script:Tag $($findings.Count) potential secret(s) found:" -ForegroundColor Red
    foreach ($f in $findings) {
        Write-Host "  $f" -ForegroundColor Red
    }
    Write-Host "$script:Tag see .agents/plan/platform/QUALITY_GATE_ENHANCEMENT_PLAN.md §6.2 for the allowlist rationale." -ForegroundColor Red
    exit 1
}

Write-Host "$script:Tag no high-signal secrets detected." -ForegroundColor Green
exit 0
