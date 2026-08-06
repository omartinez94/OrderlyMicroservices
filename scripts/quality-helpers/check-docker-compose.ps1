<#
.SYNOPSIS
    Compose healthcheck gate for the Orderly .NET 10 microservices solution.

.DESCRIPTION
    Parses orderly-microservices/docker-compose.yml and asserts that every
    declared service carries a healthcheck: block with the keys docker
    compose requires for `depends_on: condition: service_healthy` to work:

        healthcheck.test     (non-empty list or scalar string)
        healthcheck.interval (matches ^\d+(\.\d+)?(s|m)$)
        healthcheck.timeout  (matches ^\d+(\.\d+)?(s|m)$)
        healthcheck.retries  (integer >= 1)

    healthcheck.start_period is recommended but not required.

    The YAML parser is hand-rolled — no Python / yq dependency, matching the
    dependency-free philosophy of every other helper in scripts/quality-helpers/.
    Only enough YAML is recognised to walk services and their immediate property
    keys. Multi-document compose files are supported by resetting the service
    tracker on each `^---$` line.

    The override files (docker-compose.override.dev.yml,
    docker-compose.override.prod.yml) are NOT scanned. Audited 2026-08-05 —
    neither declares a healthcheck: block, only environment/ports/volumes/restart/
    container_name/depends_on. The base file is the single source of truth for
    the contract; merging would add edge cases without adding enforcement.

    Phase 4 of .agents/plan/platform/QUALITY_GATE_ENHANCEMENT_PLAN.md —
    Docker, Contract & Test Coverage validation (plan §9 Phase 4).

.PARAMETER Path
    Path to docker-compose.yml. Defaults to the repo's base file.

.PARAMETER ListMissing
    Print the services that lack healthchecks and exit 0 without failing.
    Useful as a one-shot diagnostic before a phase that adds the missing blocks.

.EXAMPLE
    pwsh ./scripts/quality-helpers/check-docker-compose.ps1
    # Fails the gate if any service in the base compose file lacks healthcheck.

.EXAMPLE
    pwsh ./scripts/quality-helpers/check-docker-compose.ps1 -ListMissing
    # Prints services without healthchecks, exits 0.

.NOTES
    Exit codes:
        0 — every service has a complete healthcheck block
        1 — one or more services missing healthchecks or invalid values
        2 — internal error (file missing or unparseable)

    Style follows scripts/quality-helpers/find-secrets.ps1:
    [CmdletBinding()], $ErrorActionPreference='Stop', block-comment header,
    [check-docker-compose] tag prefix on every Write-Host.
#>

[CmdletBinding()]
param(
    [string]$Path,
    [switch]$ListMissing
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# ----------------------------------------------------------------
# Constants
# ----------------------------------------------------------------

$script:Tag = '[check-docker-compose]'

# Validation regex for interval / timeout values: "10s", "1.5m", "30s" etc.
$script:DurationPattern = '^\d+(\.\d+)?(s|m)$'

# > [!CAUTION]
# > Entries here mean "this service is allowed to be without a healthcheck".
# > Phase 4 ships with an empty allowlist by design — the 7 services that
# > lacked healthchecks were fixed in the SAME commit (Hard-fail AND fix
# > policy). Adding an entry here without fixing the underlying service is
# > the exact regression the gate exists to prevent.
$script:AllowedWithoutHealthcheck = @{
    # Currently empty. Future entries go here with a written rationale —
    # e.g. a job-runner service that is `restart: on-failure` and exits 0.
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

function Get-ComposeServices {
    <#
    .SYNOPSIS
        Walks a docker-compose YAML file and returns one record per service
        with the service name and its detected healthcheck sub-block.

    .DESCRIPTION
        Hand-rolled scanner. Indent-based: top-level keys live at column 0
        (or 2 inside `services:`), service properties at +2, healthcheck
        sub-keys at +2 again. Anything else is treated as opaque property
        content and not validated.

        Recognises:
            ^services:                       -> enter services scope
            ^---$                            -> reset document
            ^(\s*)([A-Za-z0-9._-]+):\s*$     -> service name or property key
            healthcheck:                     -> start collecting sub-keys
    #>
    param([string]$ComposePath)

    $lines = Get-Content -LiteralPath $ComposePath
    $results = New-Object System.Collections.Generic.List[object]

    # Track the current container (the key under which service names live).
    # We need two levels because the YAML is `services:` -> service-name -> props.
    $topKey = $null       # '' before any top-level key, then 'services'
    $currentService = $null
    $inHealthcheck = $false
    $hcIndent = -1
    $currentHealthcheck = $null
    # When a healthcheck sub-key (notably `test:`) starts with an empty value
    # and the value continues on subsequent lines as a YAML block list
    # (`- "CMD-SHELL"`, `- '/opt/mssql-tools*/bin/sqlcmd ...'`), we need to
    # accumulate those list items into the original key. Tracks which key
    # the next deeper-indent list items belong to.
    $pendingListKey = $null

    for ($i = 0; $i -lt $lines.Length; $i++) {
        $line = $lines[$i]
        $trimmed = $line.TrimEnd()

        # Skip blanks and comments
        if ($trimmed -eq '' -or $trimmed.StartsWith('#')) { continue }

        # Document separator resets all state.
        if ($trimmed -match '^---\s*$') {
            $topKey = $null
            $currentService = $null
            $inHealthcheck = $false
            continue
        }

        # Compute indent (leading spaces).
        if ($line -match '^( *)([^ ].*)$') {
            $indent = $matches[1].Length
            $content = $matches[2]
        }
        else {
            continue
        }

        # A `key:` line at column 0 is a top-level key.
        if ($indent -eq 0 -and $content -match '^([A-Za-z0-9._-]+):\s*(.*)$') {
            $key = $matches[1]
            $topKey = $key
            $currentService = $null
            $inHealthcheck = $false
            continue
        }

        # Inside `services:`, a key at indent 2 is a service name.
        if ($topKey -eq 'services' -and $indent -eq 2 -and $content -match '^([A-Za-z0-9._-]+):\s*(.*)$') {
            $currentService = $matches[1]
            $inHealthcheck = $false
            $currentHealthcheck = [ordered]@{
                test        = $null
                interval    = $null
                timeout     = $null
                retries     = $null
                start_period = $null
            }
            $results.Add([pscustomobject]@{
                Name        = $currentService
                Line        = $i + 1
                Healthcheck = $currentHealthcheck
            })
            continue
        }

        # Inside a service, `healthcheck:` at indent +2 opens the sub-block.
        if ($null -ne $currentService -and $indent -eq 4 -and $content -match '^healthcheck:\s*$') {
            $inHealthcheck = $true
            $hcIndent = 6
            continue
        }

        # Inside a healthcheck sub-block, keys at the recorded indent populate the record.
        if ($inHealthcheck -and $indent -eq $hcIndent -and $content -match '^([A-Za-z0-9_]+):\s*(.*)$') {
            $key = $matches[1]
            $value = $matches[2].Trim()
            $pendingListKey = $null
            if ($currentHealthcheck.Contains($key)) {
                # Strip wrapping quotes if present.
                if ($value.StartsWith('"') -and $value.EndsWith('"')) {
                    $value = $value.Substring(1, $value.Length - 2)
                }
                elseif ($value.StartsWith("'") -and $value.EndsWith("'")) {
                    $value = $value.Substring(1, $value.Length - 2)
                }
                $currentHealthcheck[$key] = $value
                # If the value is empty, the real content is on the next
                # lines as a block list (`- "..."`).
                if ($value -eq '') { $pendingListKey = $key }
            }
            continue
        }

        # Continuation lines for a pending block-list key (`- "item"` at
        # deeper indent than $hcIndent). Accumulates into the pending key.
        if ($inHealthcheck -and $null -ne $pendingListKey -and $indent -gt $hcIndent -and $content -match '^\s*-\s*(.*)$') {
            $item = $matches[1].Trim()
            if ($item.StartsWith('"') -and $item.EndsWith('"')) {
                $item = $item.Substring(1, $item.Length - 2)
            }
            elseif ($item.StartsWith("'") -and $item.EndsWith("'")) {
                $item = $item.Substring(1, $item.Length - 2)
            }
            $existing = $currentHealthcheck[$pendingListKey]
            if ([string]::IsNullOrEmpty($existing)) {
                $currentHealthcheck[$pendingListKey] = '[' + $item
            }
            else {
                $currentHealthcheck[$pendingListKey] = $existing + ',' + $item
            }
            continue
        }

        # Closing bracket for a pending block-list: when we see a sibling
        # healthcheck key at the recorded indent (or the block ends), close
        # the open '[' we accumulated above.
        if ($inHealthcheck -and $null -ne $pendingListKey) {
            $existing = $currentHealthcheck[$pendingListKey]
            if (-not [string]::IsNullOrEmpty($existing) -and $existing.StartsWith('[') -and -not $existing.EndsWith(']')) {
                $currentHealthcheck[$pendingListKey] = $existing + ']'
            }
            $pendingListKey = $null
        }

        # A property at indent 4 that is NOT healthcheck closes the healthcheck sub-block.
        if ($inHealthcheck -and $indent -le 4 -and $content -match '^[A-Za-z0-9_]+:') {
            $inHealthcheck = $false
            $pendingListKey = $null
        }
    }

    return $results
}

function Test-HealthcheckBlock {
    <#
    .SYNOPSIS
        Validates a healthcheck sub-block. Returns an array of error strings
        (empty if valid).
    #>
    param(
        [string]$ServiceName,
        [hashtable]$Healthcheck
    )

    $errors = New-Object System.Collections.Generic.List[string]

    if ($null -eq $Healthcheck -or $Healthcheck.test -eq $null -or $Healthcheck.test -eq '') {
        $errors.Add("$ServiceName : missing healthcheck.test")
        return $errors
    }

    if ($Healthcheck.test.StartsWith('[') -and $Healthcheck.test.EndsWith(']')) {
        # List form: ["CMD", "curl", ...] — non-empty after trim is enough.
        $inner = $Healthcheck.test.Trim('[', ']').Trim()
        if ($inner -eq '') {
            $errors.Add("$ServiceName : healthcheck.test is an empty list")
        }
    }
    elseif ($Healthcheck.test.StartsWith('"') -or $Healthcheck.test.StartsWith("'")) {
        # quoted scalar: ""..."" — empty is invalid
        $inner = $Healthcheck.test.Trim('"', "'")
        if ($inner -eq '') {
            $errors.Add("$ServiceName : healthcheck.test is an empty string")
        }
    }

    if ($Healthcheck.interval -notmatch $script:DurationPattern) {
        $errors.Add("$ServiceName : healthcheck.interval '$($Healthcheck.interval)' must match ^\d+(\.\d+)?(s|m)$")
    }
    if ($Healthcheck.timeout -notmatch $script:DurationPattern) {
        $errors.Add("$ServiceName : healthcheck.timeout '$($Healthcheck.timeout)' must match ^\d+(\.\d+)?(s|m)$")
    }

    if ($null -eq $Healthcheck.retries -or $Healthcheck.retries -eq '') {
        $errors.Add("$ServiceName : missing healthcheck.retries")
    }
    else {
        $retriesInt = 0
        if (-not [int]::TryParse($Healthcheck.retries, [ref]$retriesInt) -or $retriesInt -lt 1) {
            $errors.Add("$ServiceName : healthcheck.retries '$($Healthcheck.retries)' must be an integer >= 1")
        }
    }

    return $errors
}

# ----------------------------------------------------------------
# Main
# ----------------------------------------------------------------

if ([string]::IsNullOrWhiteSpace($Path)) {
    $Path = Join-Path $PSScriptRoot '../../orderly-microservices/docker-compose.yml'
}

if (-not (Test-Path -LiteralPath $Path)) {
    Write-Host "$script:Tag cannot find compose file: $Path" -ForegroundColor Red
    exit 2
}

$relPath = Get-RelativePath -FullPath $Path
Write-Host "$script:Tag scanning $relPath" -ForegroundColor Cyan

try {
    $services = @(Get-ComposeServices -ComposePath $Path)
}
catch {
    Write-Host "$script:Tag parse error: $($_.Exception.Message)" -ForegroundColor Red
    exit 2
}

if ($services.Count -eq 0) {
    Write-Host "$script:Tag no services discovered — is this a docker-compose file?" -ForegroundColor Red
    exit 2
}

Write-Host "$script:Tag $($services.Count) services discovered." -ForegroundColor Cyan

$missing = New-Object System.Collections.Generic.List[object]
$invalid = New-Object System.Collections.Generic.List[object]

foreach ($svc in $services) {
    if ($script:AllowedWithoutHealthcheck.ContainsKey($svc.Name)) {
        continue
    }
    $hc = $svc.Healthcheck
    if ($null -eq $hc -or [string]::IsNullOrWhiteSpace($hc.test)) {
        $missing.Add($svc)
        continue
    }
    $errs = @(Test-HealthcheckBlock -ServiceName $svc.Name -Healthcheck $hc)
    foreach ($e in $errs) { $invalid.Add([pscustomobject]@{ Service = $svc.Name; Error = $e }) }
}

if ($ListMissing) {
    if ($missing.Count -gt 0) {
        Write-Host "$script:Tag services without healthchecks:" -ForegroundColor Yellow
        foreach ($svc in $missing) {
            Write-Host "  x $($svc.Name) (line $($svc.Line))" -ForegroundColor Yellow
        }
    }
    if ($invalid.Count -gt 0) {
        Write-Host "$script:Tag services with malformed healthchecks:" -ForegroundColor Yellow
        foreach ($i in $invalid) {
            Write-Host "  x $($i.Service): $($i.Error)" -ForegroundColor Yellow
        }
    }
    if ($missing.Count -eq 0 -and $invalid.Count -eq 0) {
        Write-Host "$script:Tag every service has a complete healthcheck." -ForegroundColor Green
    }
    exit 0
}

$fail = $false

if ($missing.Count -gt 0) {
    $fail = $true
    Write-Host ''
    Write-Host "$script:Tag $($missing.Count) service(s) missing healthcheck block:" -ForegroundColor Red
    foreach ($svc in ($missing | Sort-Object Name)) {
        Write-Host "  x $($svc.Name) (line $($svc.Line))" -ForegroundColor Red
    }
    Write-Host ''
    Write-Host "$script:Tag Phase 4 mandates a healthcheck on every service so" -ForegroundColor Red
    Write-Host "$script:Tag `depends_on: condition: service_healthy` works at boot." -ForegroundColor Red
    Write-Host "$script:Tag Add the block, or — if a service cannot be healthchecked —" -ForegroundColor Red
    Write-Host "$script:Tag add its name to `$AllowedWithoutHealthcheck WITH a rationale." -ForegroundColor Red
}

if ($invalid.Count -gt 0) {
    $fail = $true
    Write-Host ''
    Write-Host "$script:Tag $($invalid.Count) healthcheck block(s) malformed:" -ForegroundColor Red
    foreach ($i in ($invalid | Sort-Object Service)) {
        Write-Host "  x $($i.Service): $($i.Error)" -ForegroundColor Red
    }
}

if ($fail) {
    exit 1
}

Write-Host ''
Write-Host "$script:Tag every service has a complete healthcheck block." -ForegroundColor Green
exit 0