<#
.SYNOPSIS
    NuGet license-compliance checker for the Orderly .NET 10 microservices solution.

.DESCRIPTION
    Enumerates every <PackageReference> across the repository's .csproj / .props
    files, resolves each package's license from the LOCAL NuGet cache
    (~/.nuget/packages/<id>/<version>/<id>.nuspec), and classifies it.

    Resolution order for a package's license:
      1. <license type="expression"> — an SPDX expression, used verbatim.
      2. <license type="file">       — the referenced license file is text-sniffed
                                       for a well-known preamble ("The MIT License",
                                       "Apache License, Version 2.0", ...). If the
                                       sniff is inconclusive the package must appear
                                       in $AcknowledgedNonPermissive below.
      3. <licenseUrl>                — legacy metadata; mapped via $LicenseUrlMap.

    Classification:
      * Permissive  ($PermissiveSpdx)      -> pass
      * Acknowledged ($AcknowledgedNonPermissive) -> pass with a warning; each entry
                                              carries a written rationale so the
                                              legal position is reviewable in-tree.
      * Anything else                      -> FAIL

    The check is fully OFFLINE — it never contacts nuget.org, so it adds ~1s to the
    quality gate and works behind a firewall. The trade-off is that a package which
    has never been restored on this machine reports as NOT-CACHED; run
    `dotnet restore` first (phase-guard.ps1 section 1 already does).

    Phase 2 of .agents/plan/platform/QUALITY_GATE_ENHANCEMENT_PLAN.md —
    Architecture, NodaTime & MediatR Conventions (plan §6.3 / §9 Phase 2).

.PARAMETER PackagesRoot
    Override the NuGet global-packages folder. Defaults to $env:NUGET_PACKAGES
    when set, else ~/.nuget/packages.

.PARAMETER ListAll
    Print every package and its resolved license, not just the failures.

.EXAMPLE
    pwsh ./scripts/quality-helpers/check-licensing.ps1
    # Fails the gate if any dependency has an unapproved license.

.EXAMPLE
    pwsh ./scripts/quality-helpers/check-licensing.ps1 -ListAll
    # Full inventory — useful when producing an attribution/NOTICE file.

.NOTES
    Exit codes:
        0 — every dependency is permissive or explicitly acknowledged
        1 — one or more dependencies have an unapproved / unresolvable license
        2 — internal error (no packages found, cache root missing, etc.)

    Style follows scripts/quality-helpers/find-secrets.ps1:
    [CmdletBinding()], $ErrorActionPreference='Stop', block-comment header,
    [check-licensing] tag prefix on every Write-Host.
#>

[CmdletBinding()]
param(
    [string]$PackagesRoot,
    [switch]$ListAll
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# ----------------------------------------------------------------
# Constants
# ----------------------------------------------------------------

$script:Tag = '[check-licensing]'

# SPDX identifiers considered permissive enough to ship without review.
# PostgreSQL is the PostgreSQL License (a BSD/MIT-style permissive licence used
# by Npgsql). MS-PL is the Microsoft Public License.
$script:PermissiveSpdx = @(
    'MIT', 'MIT-0', '0BSD', 'ISC', 'Unlicense',
    'Apache-2.0',
    'BSD-2-Clause', 'BSD-3-Clause',
    'PostgreSQL',
    'MS-PL'
)

# Packages whose license is NOT permissive but whose use has been reviewed and
# accepted. Each entry MUST carry a rationale — this table is the in-tree record
# of the project's legal position, so a reviewer can audit it without leaving the
# repo. Keys are package ids (case-insensitive); they apply to ALL versions.
#
# > [!CAUTION]
# > Adding an entry here is a legal decision, not a technical one. Do not add a
# > package just to make the gate green.
$script:AcknowledgedNonPermissive = @{
    'MediatR'          = 'RPL-1.5 or commercial (Lucky Penny Software). Used as an unmodified binary dependency for CQRS dispatch per AGENTS.md; no MediatR source is modified or redistributed. Revisit if Orderly is ever distributed as a binary product rather than operated as a service.'
    'Hangfire.AspNetCore' = 'LGPL-3.0 or commercial (Hangfire OU), multi-licensed. Consumed as an unmodified binary; LGPL dynamic-linking terms are satisfied.'
    'Hangfire.PostgreSql' = 'LGPL-3.0 (community storage provider for Hangfire). Same dynamic-linking rationale as Hangfire.AspNetCore.'
    'Microsoft.VisualStudio.Azure.Containers.Tools.Targets' = 'Microsoft EULA, proprietary. Build-time-only MSBuild targets for docker-compose tooling; never redistributed in a container image or NuGet package.'
}

# Legacy <licenseUrl> values mapped to SPDX. Only needed for packages that
# predate the <license> element.
$script:LicenseUrlMap = @{
    'https://opensource.org/licenses/MIT'          = 'MIT'
    'https://licenses.nuget.org/MIT'               = 'MIT'
    'https://licenses.nuget.org/Apache-2.0'        = 'Apache-2.0'
    'https://www.apache.org/licenses/LICENSE-2.0'  = 'Apache-2.0'
}

# Text-sniff signatures for <license type="file"> packages. First match wins.
$script:LicenseTextSignatures = @(
    @{ Spdx = 'MIT';          Pattern = 'The MIT License|Permission is hereby granted, free of charge' },
    @{ Spdx = 'Apache-2.0';   Pattern = 'Apache License\s*,?\s*Version 2\.0' },
    @{ Spdx = 'BSD-3-Clause'; Pattern = 'Redistributions in binary form must reproduce' },
    @{ Spdx = 'MS-PL';        Pattern = 'Microsoft Public License' }
)

# ----------------------------------------------------------------
# Helpers
# ----------------------------------------------------------------

function Get-PrunedFiles {
    <#
    .SYNOPSIS
        Recursively enumerates files matching $Extensions, skipping whole
        directory subtrees named in $PruneDirs.
    .DESCRIPTION
        Get-ChildItem -Recurse -Include walks every directory (bin/, obj/,
        node_modules/, .git/) and post-filters, which costs ~70s on this repo.
        Pruning at the directory level with the BCL enumerator brings the same
        walk down to well under a second.
    #>
    param(
        [string]$Root,
        [string[]]$Extensions,
        [string[]]$PruneDirs
    )

    $results = New-Object System.Collections.Generic.List[string]
    $stack = New-Object System.Collections.Generic.Stack[string]
    $stack.Push($Root)

    while ($stack.Count -gt 0) {
        $dir = $stack.Pop()

        try {
            foreach ($sub in [System.IO.Directory]::EnumerateDirectories($dir)) {
                $name = [System.IO.Path]::GetFileName($sub)
                if ($PruneDirs -contains $name) { continue }
                $stack.Push($sub)
            }
            foreach ($ext in $Extensions) {
                foreach ($file in [System.IO.Directory]::EnumerateFiles($dir, $ext)) {
                    $results.Add($file)
                }
            }
        } catch {
            # Unreadable directory (permissions, junction loop) — skip it.
            continue
        }
    }

    return $results
}

function Get-PackageReferences {
    <#
    .SYNOPSIS
        Returns a de-duplicated list of [pscustomobject]@{ Id; Version } for every
        <PackageReference> in the repository.
    .DESCRIPTION
        Handles both the attribute form (Version="x") and the child-element form
        (<Version>x</Version>). PackageReference entries with no version at all
        (central package management, or an analyzer inherited from a .props) are
        reported with a $null version and resolved against whatever single version
        exists in the cache.
    #>
    param([string]$RepoRoot)

    $seen = @{}
    $files = Get-PrunedFiles -Root $RepoRoot `
        -Extensions @('*.csproj', '*.props', '*.targets') `
        -PruneDirs @('bin', 'obj', 'node_modules', '.git', '.vs')

    foreach ($path in $files) {
        try {
            [xml]$xml = Get-Content -LiteralPath $path -Raw
        } catch {
            Write-Warning "$script:Tag could not parse ${path}: $($_.Exception.Message)"
            continue
        }
        foreach ($node in $xml.SelectNodes('//PackageReference')) {
            $id = $node.GetAttribute('Include')
            if ([string]::IsNullOrWhiteSpace($id)) { $id = $node.GetAttribute('Update') }
            if ([string]::IsNullOrWhiteSpace($id)) { continue }

            $version = $node.GetAttribute('Version')
            if ([string]::IsNullOrWhiteSpace($version)) {
                $child = $node.SelectSingleNode('Version')
                if ($null -ne $child) { $version = $child.InnerText }
            }
            if ([string]::IsNullOrWhiteSpace($version)) { $version = $null }

            $key = '{0}|{1}' -f $id.ToLowerInvariant(), $version
            if ($seen.ContainsKey($key)) { continue }
            $seen[$key] = $true

            [pscustomobject]@{ Id = $id; Version = $version }
        }
    }
}

function Resolve-NuspecPath {
    <#
    .SYNOPSIS
        Locates the .nuspec for a package id/version inside the global cache.
        Returns $null when the package has not been restored on this machine.
    #>
    param(
        [string]$PackagesRoot,
        [string]$Id,
        [string]$Version
    )

    $lower = $Id.ToLowerInvariant()
    $pkgDir = Join-Path $PackagesRoot $lower
    if (-not (Test-Path -LiteralPath $pkgDir)) { return $null }

    if ([string]::IsNullOrWhiteSpace($Version)) {
        # No version pinned in the project file — fall back to the only cached
        # version if unambiguous, else the highest-sorting one.
        $versionDir = Get-ChildItem -LiteralPath $pkgDir -Directory -ErrorAction SilentlyContinue |
            Sort-Object Name | Select-Object -Last 1
        if ($null -eq $versionDir) { return $null }
        $verPath = $versionDir.FullName
    } else {
        $verPath = Join-Path $pkgDir $Version
        if (-not (Test-Path -LiteralPath $verPath)) { return $null }
    }

    $nuspec = Join-Path $verPath "$lower.nuspec"
    if (Test-Path -LiteralPath $nuspec) { return $nuspec }
    return $null
}

function Get-LicenseFromNuspec {
    <#
    .SYNOPSIS
        Resolves a package's license to an SPDX-ish string.
    .DESCRIPTION
        Returns a [pscustomobject] with Spdx (the resolved identifier, or $null)
        and Source (how it was resolved: 'expression', 'file-sniff', 'url', or a
        diagnostic such as 'file-unrecognised').
    #>
    param([string]$NuspecPath)

    try {
        [xml]$xml = Get-Content -LiteralPath $NuspecPath -Raw
    } catch {
        return [pscustomobject]@{ Spdx = $null; Source = 'nuspec-unparsable' }
    }

    $metadata = $xml.package.metadata
    $licenseNode = $metadata.SelectSingleNode('*[local-name()="license"]')

    if ($null -ne $licenseNode) {
        $type = $licenseNode.GetAttribute('type')

        if ($type -eq 'expression') {
            return [pscustomobject]@{ Spdx = $licenseNode.InnerText.Trim(); Source = 'expression' }
        }

        if ($type -eq 'file') {
            $licenseFile = Join-Path (Split-Path -Parent $NuspecPath) $licenseNode.InnerText
            if (Test-Path -LiteralPath $licenseFile) {
                # Read a bounded prefix — license preambles are always at the top,
                # and some LICENSE files are hundreds of KB.
                $head = (Get-Content -LiteralPath $licenseFile -TotalCount 40 -ErrorAction SilentlyContinue) -join "`n"
                foreach ($sig in $script:LicenseTextSignatures) {
                    if ($head -match $sig.Pattern) {
                        return [pscustomobject]@{ Spdx = $sig.Spdx; Source = 'file-sniff' }
                    }
                }
            }
            return [pscustomobject]@{ Spdx = $null; Source = 'file-unrecognised' }
        }
    }

    $urlNode = $metadata.SelectSingleNode('*[local-name()="licenseUrl"]')
    if ($null -ne $urlNode) {
        $url = $urlNode.InnerText.Trim()
        if ($script:LicenseUrlMap.ContainsKey($url)) {
            return [pscustomobject]@{ Spdx = $script:LicenseUrlMap[$url]; Source = 'url' }
        }
        return [pscustomobject]@{ Spdx = $null; Source = "url-unmapped:$url" }
    }

    return [pscustomobject]@{ Spdx = $null; Source = 'no-license-metadata' }
}

function Test-IsPermissive {
    <#
    .SYNOPSIS
        Returns $true when an SPDX expression consists only of permissive terms.
    .DESCRIPTION
        Handles simple compound expressions ("MIT OR Apache-2.0",
        "Apache-2.0 WITH LLVM-exception") by requiring every OR-alternative to be
        permissive. An AND expression is only permissive if BOTH sides are.
    #>
    param([string]$Spdx)

    if ([string]::IsNullOrWhiteSpace($Spdx)) { return $false }

    # Strip WITH-exception clauses and parentheses, then split on AND/OR.
    $normalised = $Spdx -replace '\s+WITH\s+[A-Za-z0-9\.\-]+', '' -replace '[()]', ''
    # @() is load-bearing: a single-term expression makes the pipeline emit a
    # scalar, and Set-StrictMode -Version Latest rejects .Count on it.
    $terms = @($normalised -split '\s+(?:AND|OR)\s+' | ForEach-Object { $_.Trim() } | Where-Object { $_ })

    if ($terms.Count -eq 0) { return $false }
    foreach ($term in $terms) {
        if ($script:PermissiveSpdx -notcontains $term) { return $false }
    }
    return $true
}

# ----------------------------------------------------------------
# Main
# ----------------------------------------------------------------

$repoRoot = (Resolve-Path "$PSScriptRoot/../..").Path

if ([string]::IsNullOrWhiteSpace($PackagesRoot)) {
    $PackagesRoot = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path $HOME '.nuget/packages' }
}

Write-Host "$script:Tag repo:  $repoRoot"
Write-Host "$script:Tag cache: $PackagesRoot"

if (-not (Test-Path -LiteralPath $PackagesRoot)) {
    Write-Host "$script:Tag NuGet global-packages folder not found: $PackagesRoot" -ForegroundColor Red
    Write-Host "$script:Tag run 'dotnet restore' first, or pass -PackagesRoot." -ForegroundColor Red
    exit 2
}

$packages = @(Get-PackageReferences -RepoRoot $repoRoot)
if ($packages.Count -eq 0) {
    Write-Host "$script:Tag no PackageReference entries found — nothing to check." -ForegroundColor Red
    exit 2
}

$violations = New-Object System.Collections.Generic.List[string]
$acknowledged = New-Object System.Collections.Generic.List[string]
$inventory = New-Object System.Collections.Generic.List[object]

foreach ($pkg in ($packages | Sort-Object Id, Version)) {
    $display = if ($pkg.Version) { "$($pkg.Id) $($pkg.Version)" } else { "$($pkg.Id) (unversioned)" }

    # An explicit acknowledgement short-circuits resolution: the rationale in the
    # table is the decision of record, regardless of what the nuspec says.
    $ackKey = $script:AcknowledgedNonPermissive.Keys |
        Where-Object { $_ -ieq $pkg.Id } | Select-Object -First 1
    if ($ackKey) {
        $acknowledged.Add("$display — $($script:AcknowledgedNonPermissive[$ackKey])")
        $inventory.Add([pscustomobject]@{ Package = $display; License = 'ACKNOWLEDGED'; Source = 'override' })
        continue
    }

    $nuspec = Resolve-NuspecPath -PackagesRoot $PackagesRoot -Id $pkg.Id -Version $pkg.Version
    if ($null -eq $nuspec) {
        $violations.Add("$display — NOT CACHED (run 'dotnet restore'; cannot verify license offline)")
        $inventory.Add([pscustomobject]@{ Package = $display; License = 'NOT-CACHED'; Source = 'n/a' })
        continue
    }

    $license = Get-LicenseFromNuspec -NuspecPath $nuspec
    $inventory.Add([pscustomobject]@{
        Package = $display
        License = if ($license.Spdx) { $license.Spdx } else { 'UNRESOLVED' }
        Source  = $license.Source
    })

    if (Test-IsPermissive -Spdx $license.Spdx) { continue }

    if ($license.Spdx) {
        $violations.Add("$display — '$($license.Spdx)' is not in the permissive allowlist (resolved via $($license.Source))")
    } else {
        $violations.Add("$display — license could not be resolved ($($license.Source))")
    }
}

Write-Host "$script:Tag inspected $($packages.Count) unique package reference(s)."

if ($ListAll) {
    Write-Host ''
    $inventory | Sort-Object Package | Format-Table -AutoSize | Out-String -Width 200 | Write-Host
}

if ($acknowledged.Count -gt 0) {
    Write-Host ''
    Write-Host "$script:Tag $($acknowledged.Count) non-permissive dependency/ies accepted by explicit review:" -ForegroundColor Yellow
    foreach ($a in $acknowledged) { Write-Host "  ! $a" -ForegroundColor Yellow }
}

if ($violations.Count -gt 0) {
    Write-Host ''
    Write-Host "$script:Tag $($violations.Count) dependency/ies with unapproved licenses:" -ForegroundColor Red
    foreach ($v in $violations) { Write-Host "  x $v" -ForegroundColor Red }
    Write-Host ''
    Write-Host "$script:Tag either replace the dependency, or add it to `$AcknowledgedNonPermissive" -ForegroundColor Red
    Write-Host "$script:Tag in this script WITH a written rationale. See plan §6.3 / §8." -ForegroundColor Red
    exit 1
}

Write-Host ''
Write-Host "$script:Tag all dependencies are permissive or explicitly acknowledged." -ForegroundColor Green
exit 0
