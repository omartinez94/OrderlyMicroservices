<#
.SYNOPSIS
    CQRS / MediatR layout validator for the Orderly .NET 10 microservices solution.

.DESCRIPTION
    Enforces two conventions on every MediatR / MassTransit handler:

      1. LOCATION — a handler must live under one of the "handler roots" declared
         for its project in $HandlerRoots below.

         The roots are per-project ON PURPOSE. AGENTS.md documents a deliberate
         architectural split: Catalog.API and Basket.API use Vertical Slice
         (Features/ or Basket/), while Ordering uses Clean Architecture
         (Ordering.Application/Orders/Commands/...). A single repo-wide
         "everything under Features/" rule would flag 33 handlers that are exactly
         where the documented architecture wants them. This check therefore
         validates each service against ITS OWN declared convention, so it catches
         real drift (a handler dropped into Infrastructure/) without fighting the
         architecture.

      2. NAMESPACE — the declared namespace must mirror the folder path:
             <ProjectName>/Features/Brands/CreateBrand/CreateBrandHandler.cs
                -> namespace <ProjectName>.Features.Brands.CreateBrand
         No project in the solution overrides <RootNamespace>, so the project
         file name is the root namespace.

    A "handler" is any type whose base list mentions IRequestHandler<>,
    INotificationHandler<>, ICommandHandler<>, IQueryHandler<> (the BuildingBlocks
    CQRS markers) or IConsumer<> (MassTransit).

    Test projects (*.Tests) are skipped — test fakes and in-line stub handlers
    are not part of the production layout contract.

    Phase 2 of .agents/plan/platform/QUALITY_GATE_ENHANCEMENT_PLAN.md —
    Architecture, NodaTime & MediatR Conventions (plan §6.3 / §9 Phase 2).

.PARAMETER Path
    Root to scan. Defaults to the orderly-microservices/ source tree.

.PARAMETER ListHandlers
    Print every handler discovered with its project, folder and namespace, then
    exit 0. Useful when onboarding a new service and deciding its handler root.

.EXAMPLE
    pwsh ./scripts/quality-helpers/check-mediatr.ps1
    # Fails the gate on a handler outside its service's declared root, or on a
    # namespace that does not mirror its folder.

.EXAMPLE
    pwsh ./scripts/quality-helpers/check-mediatr.ps1 -ListHandlers
    # Inventory of all handlers and their locations.

.NOTES
    Exit codes:
        0 — every handler satisfies both conventions
        1 — one or more layout violations
        2 — internal error (scan root missing)

    Style follows scripts/quality-helpers/find-secrets.ps1:
    [CmdletBinding()], $ErrorActionPreference='Stop', block-comment header,
    [check-mediatr] tag prefix on every Write-Host.
#>

[CmdletBinding()]
param(
    [string]$Path,
    [switch]$ListHandlers
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# ----------------------------------------------------------------
# Constants
# ----------------------------------------------------------------

$script:Tag = '[check-mediatr]'

# Interfaces whose presence in a type's base list makes it a "handler".
$script:HandlerInterfaces = @(
    'IRequestHandler', 'INotificationHandler',
    'ICommandHandler', 'IQueryHandler',
    'IConsumer'
)

# Per-project handler roots — the FIRST path segment(s) under the project
# directory where handlers are allowed to live. See the .DESCRIPTION for why
# this is per-project rather than a single repo-wide rule.
#
# 'Messaging' recurs across services: it is the established home for MassTransit
# integration-event consumers (Basket, Catalog and Discount all have a
# Messaging/ tree). Services built on Clean Architecture instead nest their
# consumers under the application root (Ordering -> Orders/EventHandlers/
# Integration, Kitchen -> Application/EventHandlers/Integration), so they need
# no separate entry.
#
# Adding a new service? Add its root here; an unconfigured project with handlers
# is reported so a new service cannot silently invent its own layout.
$script:HandlerRoots = @{
    'Catalog.API'          = @('Features', 'Application', 'Messaging')
    'Identity.API'         = @('Features')
    'Ordering.Application' = @('Orders')
    'Kitchen.API'          = @('Application')
    'Basket.API'           = @('Basket', 'Endpoints', 'Messaging')
    'Discount.Grpc'        = @('Messaging')
}

# Files exempt from the NAMESPACE rule, with the reason. Keyed by repo-relative
# path with forward slashes.
$script:AllowedNamespaceMismatch = @{
    'orderly-microservices/Services/Basket/Basket.API/Endpoints/AdminCartEndpoints.cs' =
        'Declares Basket.API.Basket.AdminCarts while sitting in Endpoints/. Intentional: the admin cart handlers belong to the Basket feature family and consume types from Basket.API.Basket.StoreBasket; the file is grouped with the other endpoint modules for discoverability. Renaming the namespace would split the feature across two namespaces for no benefit.'
}

# Path fragments never scanned.
$script:PathExcludes = @(
    '/obj/', '/bin/', '/Migrations/', '/Generated Files/', '/node_modules/', '/.git/'
)

# ----------------------------------------------------------------
# Helpers
# ----------------------------------------------------------------

function Get-PrunedSourceFiles {
    <#
    .SYNOPSIS
        Recursively enumerates *.cs, skipping subtrees that cannot contain
        hand-written handlers.
    .DESCRIPTION
        Directory-level pruning with the BCL enumerator rather than
        Get-ChildItem -Recurse + post-filter; the latter walks bin/ and obj/ on
        every run and dominates the checker's runtime.
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

function Remove-CommentedCode {
    <#
    .SYNOPSIS
        Blanks comment content so commented-out handlers are not parsed.
    .DESCRIPTION
        Operates on the whole file text and preserves newlines so that offsets
        still map to sensible line numbers.
    #>
    param([string]$Text)

    # Block comments -> equivalent run of newlines (keeps line numbering).
    $Text = [regex]::Replace($Text, '/\*[\s\S]*?\*/', {
        param($m)
        $newlines = ([regex]::Matches($m.Value, "`n")).Count
        "`n" * $newlines
    })

    # Line comments -> stripped to end of line.
    $Text = [regex]::Replace($Text, '//[^\r\n]*', '')

    return $Text
}

function Get-DeclaredNamespace {
    <#
    .SYNOPSIS
        Returns the file's declared namespace (file-scoped or block), or $null.
    #>
    param([string]$Text)

    $m = [regex]::Match($Text, '(?m)^\s*namespace\s+([A-Za-z0-9_.]+)\s*[;{]')
    if ($m.Success) { return $m.Groups[1].Value }
    return $null
}

function Get-HandlerTypes {
    <#
    .SYNOPSIS
        Returns the names of types in the file whose base list mentions a
        handler interface.
    .DESCRIPTION
        Finds each type declaration, then inspects the text between the type name
        and the opening brace (which contains the primary-constructor parameter
        list, the base list, and any generic constraints). A handler interface
        appearing there means the type implements it.

        This is a text heuristic, not a Roslyn parse — chosen per plan §0.2,
        which requires the convention checks to avoid heavyweight MSBuild/Roslyn
        invocation so the gate stays fast.
    #>
    param([string]$Text)

    $found = New-Object System.Collections.Generic.List[string]
    $pattern = '(?m)^\s*(?:(?:public|internal|private|protected|sealed|abstract|static|partial|file)\s+)*(?:class|record|struct)\s+(\w+)'

    foreach ($m in [regex]::Matches($Text, $pattern)) {
        $typeName = $m.Groups[1].Value
        $start = $m.Index + $m.Length

        $brace = $Text.IndexOf('{', $start)
        # A record can be declared without a body: `record Foo(int X) : IBar;`
        $semi = $Text.IndexOf(';', $start)
        if ($brace -lt 0 -and $semi -lt 0) { continue }
        $end = if ($brace -lt 0) { $semi } elseif ($semi -ge 0 -and $semi -lt $brace) { $semi } else { $brace }

        $header = $Text.Substring($start, $end - $start)
        if ($header -notmatch ':') { continue }

        foreach ($iface in $script:HandlerInterfaces) {
            if ($header -match "\b$iface\s*<") {
                $found.Add($typeName)
                break
            }
        }
    }

    return $found
}

function Get-OwningProject {
    <#
    .SYNOPSIS
        Walks up from a file to the nearest .csproj. Returns a [pscustomobject]
        with Name and Directory, or $null when the file is outside a project.
    #>
    param([string]$FilePath)

    $dir = Split-Path -Parent $FilePath
    while ($dir -and (Test-Path -LiteralPath $dir)) {
        $proj = Get-ChildItem -LiteralPath $dir -Filter '*.csproj' -File -ErrorAction SilentlyContinue |
            Select-Object -First 1
        if ($null -ne $proj) {
            return [pscustomobject]@{
                Name      = [System.IO.Path]::GetFileNameWithoutExtension($proj.Name)
                Directory = $dir
            }
        }
        $parent = Split-Path -Parent $dir
        if ($parent -eq $dir) { break }
        $dir = $parent
    }
    return $null
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
Write-Host "$script:Tag scan: $scanRoot"

$locationViolations  = New-Object System.Collections.Generic.List[object]
$namespaceViolations = New-Object System.Collections.Generic.List[object]
$unconfigured        = New-Object System.Collections.Generic.List[object]
$inventory           = New-Object System.Collections.Generic.List[object]
$handlerFileCount    = 0

$sourceFiles = Get-PrunedSourceFiles -Root $scanRoot -PruneDirs @(
    'bin', 'obj', 'node_modules', '.git', '.vs', 'Migrations', 'Generated Files'
)

# Cache project lookups — Get-OwningProject walks up the tree and hits the disk
# for each directory, and hundreds of files share the same project.
$script:ProjectCache = @{}

$sourceFiles | ForEach-Object {
    $fullName = $_
    $relPath = Get-RelativePath -FullName $fullName -RepoRoot $repoRoot

    $probe = "/$relPath"
    $excluded = $false
    foreach ($excl in $script:PathExcludes) {
        if ($probe -like "*$excl*") { $excluded = $true; break }
    }
    if ($excluded) { return }

    $dirKey = Split-Path -Parent $fullName
    if (-not $script:ProjectCache.ContainsKey($dirKey)) {
        $script:ProjectCache[$dirKey] = Get-OwningProject -FilePath $fullName
    }
    $project = $script:ProjectCache[$dirKey]

    if ($null -eq $project) { return }
    # Test projects are not part of the production layout contract.
    if ($project.Name -match '\.Tests?$') { return }

    $raw = Get-Content -LiteralPath $fullName -Raw -ErrorAction SilentlyContinue
    if ([string]::IsNullOrWhiteSpace($raw)) { return }

    # Cheap pre-filter: skip files that cannot possibly declare a handler.
    $anyIface = $false
    foreach ($iface in $script:HandlerInterfaces) {
        if ($raw.Contains($iface)) { $anyIface = $true; break }
    }
    if (-not $anyIface) { return }

    $text = Remove-CommentedCode -Text $raw
    # @() is load-bearing: PowerShell unrolls the returned List, so a no-match
    # result arrives as $null and .Count would throw under Set-StrictMode.
    $handlers = @(Get-HandlerTypes -Text $text)
    if ($handlers.Count -eq 0) { return }

    $handlerFileCount++

    # Folder of the file relative to its project directory.
    $projRelDir = (Split-Path -Parent $fullName).Substring($project.Directory.Length).TrimStart('\', '/').Replace('\', '/')
    $segments = @($projRelDir -split '/' | Where-Object { $_ })

    $declaredNs = Get-DeclaredNamespace -Text $text
    $expectedNs = if ($segments.Count -gt 0) {
        "$($project.Name)." + ($segments -join '.')
    } else {
        $project.Name
    }

    $inventory.Add([pscustomobject]@{
        Project   = $project.Name
        Path      = $relPath
        Handlers  = ($handlers -join ', ')
        Namespace = $declaredNs
    })

    # --- Rule 1: location ---------------------------------------------------
    if ($script:HandlerRoots.ContainsKey($project.Name)) {
        $roots = $script:HandlerRoots[$project.Name]
        $actualRoot = if ($segments.Count -gt 0) { $segments[0] } else { '<project root>' }
        if ($roots -notcontains $actualRoot) {
            $locationViolations.Add([pscustomobject]@{
                Path     = $relPath
                Project  = $project.Name
                Actual   = $actualRoot
                Allowed  = ($roots -join ', ')
                Handlers = ($handlers -join ', ')
            })
        }
    } else {
        $unconfigured.Add([pscustomobject]@{
            Path     = $relPath
            Project  = $project.Name
            Handlers = ($handlers -join ', ')
        })
    }

    # --- Rule 2: namespace mirrors folder -----------------------------------
    if (-not $script:AllowedNamespaceMismatch.ContainsKey($relPath)) {
        if ($declaredNs -and $declaredNs -ne $expectedNs) {
            $namespaceViolations.Add([pscustomobject]@{
                Path     = $relPath
                Declared = $declaredNs
                Expected = $expectedNs
            })
        }
    }
}

Write-Host "$script:Tag found $handlerFileCount file(s) declaring handlers across $($script:HandlerRoots.Count) configured project(s)."

if ($ListHandlers) {
    Write-Host ''
    $inventory | Sort-Object Project, Path | Format-Table -AutoSize | Out-String -Width 240 | Write-Host
    exit 0
}

$failed = $false

if ($locationViolations.Count -gt 0) {
    $failed = $true
    Write-Host ''
    Write-Host "$script:Tag $($locationViolations.Count) handler(s) outside their project's declared root:" -ForegroundColor Red
    foreach ($v in ($locationViolations | Sort-Object Path)) {
        Write-Host "  x $($v.Path)" -ForegroundColor Red
        Write-Host "      handlers: $($v.Handlers)"
        Write-Host "      found under '$($v.Actual)/' but $($v.Project) allows: $($v.Allowed)" -ForegroundColor DarkGray
    }
}

if ($namespaceViolations.Count -gt 0) {
    $failed = $true
    Write-Host ''
    Write-Host "$script:Tag $($namespaceViolations.Count) handler file(s) whose namespace does not mirror its folder:" -ForegroundColor Red
    foreach ($v in ($namespaceViolations | Sort-Object Path)) {
        Write-Host "  x $($v.Path)" -ForegroundColor Red
        Write-Host "      declared: $($v.Declared)"
        Write-Host "      expected: $($v.Expected)" -ForegroundColor DarkGray
    }
}

if ($unconfigured.Count -gt 0) {
    $failed = $true
    Write-Host ''
    Write-Host "$script:Tag $($unconfigured.Count) handler file(s) in project(s) with no declared handler root:" -ForegroundColor Red
    foreach ($v in ($unconfigured | Sort-Object Project, Path)) {
        Write-Host "  x [$($v.Project)] $($v.Path)  ($($v.Handlers))" -ForegroundColor Red
    }
    Write-Host "$script:Tag add the project to `$HandlerRoots in this script to declare its convention." -ForegroundColor Red
}

if ($failed) {
    Write-Host ''
    Write-Host "$script:Tag see plan §6.3 — CQRS layout is per-service by design (AGENTS.md)." -ForegroundColor Red
    exit 1
}

Write-Host ''
Write-Host "$script:Tag all handlers satisfy their project's layout convention." -ForegroundColor Green
exit 0
