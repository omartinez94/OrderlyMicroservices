<#
.SYNOPSIS
    Regenerates `orderly-microservices/docs/api/basket-api-v1.json` from
    the running Basket.API host's Swashbuckle generator.

.DESCRIPTION
    Phase 5.1 Commit 4 — re-runnable generator for the public OpenAPI
    artifact. The integration test
    (`Services/Basket/Basket.API.Tests/Integration/Endpoints/BasketSnapshotsTests.VerifyAllEndpoints`)
    serialises the SAME `ISwaggerProvider.GetSwagger("v1")` document via
    `OpenApiDocument.SerializeAsJsonAsync` — this script is the human
    counterpart for one-off regeneration without running the full test
    suite.

    The Basket API host must be running in `ASPNETCORE_ENVIRONMENT=Development`
    so `app.UseSwagger()` is reachable
    (Basket.API/Program.cs:504-508 gates UseSwagger on IsDevelopment()).

.PARAMETER Url
    Base URL of the running Basket host. Defaults to `http://localhost:6001`
    (the project's Basket HTTP port per docker-compose).

.PARAMETER OutputPath
    Path the pretty-printed JSON is written to. Defaults to
    `docs/api/basket-api-v1.json` relative to the repo root
    (`orderly-microservices/`).

.PARAMETER RepoRoot
    Path to the `orderly-microservices/` directory. Defaults to the script's
    sibling directory.

.EXAMPLE
    pwsh scripts/generate-basket-openapi.ps1
    # Regenerate docs/api/basket-api-v1.json from http://localhost:6001/swagger/v1/swagger.json

.EXAMPLE
    pwsh scripts/generate-basket-openapi.ps1 -Url http://localhost:5001 -OutputPath /tmp/basket.json

.NOTES
    Run from the `orderly-microservices/` directory. The script intentionally
    does NOT start the host itself — running the host requires Postgres,
    Redis, RabbitMQ, and a configured identity endpoint; that's the
    operator's responsibility (Docker Compose via `docker-compose up` or
    `dotnet run --project Services/Basket/Basket.API`).
#>
[CmdletBinding()]
param(
    [string]$Url = 'http://localhost:6001',

    [string]$OutputPath,

    [string]$RepoRoot
)

$ErrorActionPreference = 'Stop'

if (-not $RepoRoot) {
    $RepoRoot = Split-Path -Parent $PSScriptRoot
}
if (-not $OutputPath) {
    $OutputPath = Join-Path $RepoRoot 'docs/api/basket-api-v1.json'
}

$swaggerUrl = "$Url/swagger/v1/swagger.json"

Write-Host "[generate-basket-openapi] Fetching $swaggerUrl" -ForegroundColor Cyan
$response = Invoke-WebRequest -Uri $swaggerUrl -Method Get -UseBasicParsing

if ($response.StatusCode -ne 200) {
    throw "[generate-basket-openapi] HTTP $($response.StatusCode) from $swaggerUrl. Is the Basket host running in ASPNETCORE_ENVIRONMENT=Development?"
}

if ([string]::IsNullOrWhiteSpace($response.Content)) {
    throw "[generate-basket-openapi] Empty body from $swaggerUrl. The Swagger middleware may not be enabled."
}

# Pretty-print with a 100-deep depth (the OpenAPI document references
# recursive component schemas; the default 2-depth is too shallow and
# truncates the JSON tree). ConvertFromJson then ConvertToJson-Json
# normalises field order — the Verify snapshot uses the same
# OpenApiDocument.SerializeAsJsonAsync output, so any reordering here
# would cause the snapshot to fail.
$json = $response.Content | ConvertFrom-Json
$pretty = $json | ConvertTo-Json -Depth 100

$outputDir = Split-Path -Parent $OutputPath
if (-not (Test-Path $outputDir)) {
    New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
}

Set-Content -Path $OutputPath -Value $pretty -Encoding utf8NoBOM
Write-Host "[generate-basket-openapi] Wrote $OutputPath ($($pretty.Length) bytes)" -ForegroundColor Green

# Spot-check the path count so an empty document fails loud (instead of
# silently committing an empty {} artifact).
$pathCount = ($json.paths.PSObject.Properties | Measure-Object).Count
Write-Host "[generate-basket-openapi] OpenAPI document has $pathCount paths" -ForegroundColor Cyan
if ($pathCount -lt 1) {
    throw "[generate-basket-openapi] Empty OpenAPI document — no paths. Did the host start in Development env?"
}