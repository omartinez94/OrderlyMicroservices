# Code-Style Iteration — Orderly Microservices

> **Plan version**: `v2.0` — updated 2026-08-27. Gap-analysis pass: fixed 3 fabricated editorconfig keys, corrected braceless count (36→46), fixed CI `-warnaserror` list, added `BuildingBlocks.Dev.Tests` canary, renumbered phases to execution order, converted Unix commands to PowerShell, added `check-code-style.ps1` integration, added Marten validation procedure, decided test-var policy upfront, accounted for `Orderly.DevMCP.Server`.
> **Strategy**: phased mechanical-then-manual cascade. Direct-on-main, no feature branch (per user decision).
> **Scope**: full solution, all 30 .csproj / 921 .cs files (excluding `obj/`, `bin/`, `**/Migrations/**/*.cs`, `*.Designer.cs`).

---

## Context

`AGENTS.md` (repo root, lines 17–23) mandates six C# style rules:

1. Explicit typed locals instead of `var` when possible. Exceptions: `using var`, `out var`, anonymous types, tuple deconstruction, and LINQ queries are permitted to use `var`.
2. `is not null` / `is null` over `!= null` / `== null`.
3. C# pattern matching (switch expressions, property patterns) where it improves clarity.
4. Guard clauses / early returns to avoid deep nesting.
5. Always use curly braces for `if`, `for`, and `foreach`.
6. Prefer `string.Empty` / `string.IsNullOrEmpty(...)` over empty-string comparisons.

The conventions are already idiomatic in `BuildingBlocks`, `Identity.API`, and `Basket.API` (e.g. `Services/Identity/Identity.API/Services/ClaimsTransformer.cs:17` uses `if (user is null)`, line 51 uses `string.IsNullOrEmpty`, line 62 uses `is not null`). Today's commit `a5fd82c refactor(identity): use explicit types and string class methods in ClaimsTransformer` is the recent precedent.

**Problem**: those rules are documented but only enforced at IDE-suggestion severity in `orderly-microservices/.editorconfig`. StyleCop.Analyzers 1.2.0-beta.556 is wired in `Directory.Build.props` but `TreatWarningsAsErrors=false`, so the build is silent on style drift. The pre-commit hook + `scripts/phase-guard.ps1` only run `dotnet format whitespace` (whitespace gate), not the var / null / pattern / brace / string-empty rules.

**Outcome**: every rule from AGENTS.md enforced and followed across the solution. Build + tests + format gate green. New CI workflow prevents regressions.

---

## Baseline measurements (audit, 2026-08-27)

Excluding `obj/`, `bin/`, `**/Migrations/**/*.cs`. Numbers from a regex-based audit; cross-checked by sampling.

| Metric | Count |
|---|---:|
| `.cs` files | **921** |
| `var <name>` local declarations | **4,001** |
| `!= null` | **20** |
| `== null` | **45** |
| Braceless `if` (single-line) | **39** |
| Braceless `else` (single-line) | **1** |
| Braceless `if` (multiline) | **4** |
| Braceless `if/else` (multiline) | **2** |
| Braceless `for`/`foreach`/`while` | **0** |
| Real `== ""` / `!= ""` empty-string equality | **0** (1 comment-only hit) |
| Estimated total violations to fix | **~4,112** |

Hot-spot projects (largest work-pile): `Catalog.API` (515 var, 16 braceless ifs), `Basket.API.Tests` (475 var), `Identity.API.Tests` (397 var), `Catalog.API.Tests` (385 var, 4 braceless ifs), `Identity.API` (363 var), `Discount.Grpc.Tests` (346 var), `Ordering.Application.Tests` (273 var), `Ordering.API.Tests` (260 var), `Discount.Grpc` (158 var, 18 `== null`), `Kitchen.API.Tests` (210 var).

`BuildingBlocks.Dev\BuildingBlocks.Dev.csproj` AND `BuildingBlocks.Dev.Tests\BuildingBlocks.Dev.Tests.csproj` are canaries: both override `TreatWarningsAsErrors=true`. Phase 0's promotion to `:warning` will turn style violations into build errors in BOTH projects. A temporary per-directory `.editorconfig` override downgrades `var` rules to `:suggestion` for these two projects until Phase 3 PR #3.1 lands (see Phase 0 edits).

---

## Phasing — mechanical cascade, then manual

**Order is braces → null → var → manual**. Doing braces and null first means reviewers' cognitive load stays concentrated on the biggest pile (var) at the end. Doing BuildingBlocks.Dev first in Phase 3 means the canary proves the gate works before the cascade.

**PR strategy: per-phase-across-projects** (one PR per phase touching every project). The mechanical phases are homogeneous ("LHS must equal the inferred type"), so splitting per-project produces review fatigue without any incremental safety. Phase 4 stays per-project (manual review per handler).

### Phase 0 — Tooling baseline (no code changes)

**Goal**: lock AGENTS.md rules into the editorconfig at warning severity.

**Critical files**:
- `orderly-microservices/.editorconfig` — promote and add the keys below.

**Edits**:

```ini
# --- Style rules promoted per AGENTS.md (was :suggestion; now :warning) ---
csharp_style_var_for_built_in_types             = false:warning
csharp_style_var_when_type_is_apparent          = false:warning   # was true:suggestion — align with AGENTS.md
csharp_style_var_elsewhere                      = false:warning
csharp_prefer_braces                            = true:warning

# --- Null checks ---
# IDE0041 only covers ReferenceEquals(x, null) → x is null.
# There is NO editorconfig key that enforces == null → is null or != null → is not null.
# That rule is enforced by scripts/quality-helpers/check-code-style.ps1 (new, Phase 5).
dotnet_style_prefer_is_null_check_over_reference_equality_method = true:warning   # IDE0041

# --- Pattern matching rules (real keys, with IDE rule IDs) ---
csharp_style_pattern_matching_over_is_with_cast_check  = true:warning   # IDE0020
csharp_style_pattern_matching_over_as_with_null_check  = true:warning   # IDE0019
csharp_style_prefer_pattern_matching                   = true:warning   # IDE0078
csharp_style_prefer_not_pattern                        = true:warning   # IDE0083
csharp_style_prefer_extended_property_pattern           = true:warning   # IDE0170

# --- String handling ---
# No editorconfig key exists for string.Empty / string.IsNullOrEmpty preference.
# 0 current violations. Enforced by check-code-style.ps1 grep to prevent regression.
```

**Canary override** (temporary — removed by Phase 3 PR #3.1):

Both `BuildingBlocks.Dev` and `BuildingBlocks.Dev.Tests` have `TreatWarningsAsErrors=true`. The promotion above turns var/brace warnings into build errors for those two projects. Add these sections at the bottom of `.editorconfig` to keep them building until Phase 3 fixes their var sites:

```ini
# --- Temporary: suppress var warnings in canary projects until Phase 3 PR #3.1 ---
# Remove this section after PR #3.1 lands.
[BuildingBlocks.Dev/**/*.cs]
csharp_style_var_for_built_in_types    = false:suggestion
csharp_style_var_when_type_is_apparent = false:suggestion
csharp_style_var_elsewhere            = false:suggestion

[BuildingBlocks.Dev.Tests/**/*.cs]
csharp_style_var_for_built_in_types    = false:suggestion
csharp_style_var_when_type_is_apparent = false:suggestion
csharp_style_var_elsewhere            = false:suggestion
```

**Do NOT touch**:
- `TreatWarningsAsErrors` — leave `false` globally. Promotion to `:warning` is enough.
- `dotnet format style` — `phase-guard.ps1` §3 comment block explicitly excludes it; do not run it here either.
- The 24 StyleCop / FxCop rules already at `severity = none` (SA1200, SA1633-1641, SA1652, SA1101, CA1014, CA1052, CA1032, CA1031, CA1062, CA1307, CA1848, CA1873, CA2007, SA0001, SA1402, SA1513, SA1518, SA1116/1117, CA1707, CA1859, CA1861, CA1812, CA1515, SA1202, SA1208, SA1512, SA1515, SA1516).
- `BuildingBlocks.GeneratedMSBuildEditorConfig.editorconfig` — auto-regenerated by MSBuild on every build.

**Smoke test** (mandatory before Phase 1):
```powershell
Set-Location orderly-microservices
dotnet build orderly-microservices.slnx --no-restore 2>&1 | Tee-Object -FilePath phase0-build.log
# Expect ~4,047+ warnings:
#   IDE0008 (var → explicit type)              ~4,001
#   IDE0011 / SA1503 (braces)                  ~46
#   IDE0020/IDE0019/IDE0078 (pattern matching)  small, project-dependent
#
# == null / != null will NOT appear as IDE warnings — no analyzer covers this.
# They are enforced by check-code-style.ps1 (Phase 5) instead.
```

`SA1503` (Braces must not be omitted) is already fired by StyleCop.Analyzers with default severity. The audit's 46 braceless sites will appear in the build output as SA1503 too. Phase 1 must address both IDE0011 and SA1503.

**Verification gate**:
- `pwsh scripts/phase-guard.ps1 -Quick` — green.
- `dotnet format whitespace orderly-microservices/orderly-microservices.slnx --verify-no-changes --no-restore` — green (whitespace not touched in this phase).

### Phase 1 — Braces (46 sites, 8 projects, one PR)

**Goal**: every braceless `if`, `else`, `for`, `foreach`, and `while` block gets braces — single-line AND multiline.

**Critical files** (representative; full list is the union of the 46 sites):
- `orderly-microservices/BuildingBlocks.Messaging/MassTransit/Extensions.cs` (4 multiline braceless `if` — **HIGHEST PRIORITY**, second statement is not controlled by the `if`)
- `orderly-microservices/BuildingBlocks.Dev/Extensions/OpenIddictServerExtensions.cs` (2 multiline braceless `if/else`)
- `orderly-microservices/Services/Catalog/Catalog.API/Features/Restaurants/UpdateRestaurant/UpdateRestaurantHandler.cs` (6 sites)
- `orderly-microservices/Services/Catalog/Catalog.API.Tests/Integration/TestDataSeeder.cs` (4 sites)
- `orderly-microservices/Services/Catalog/Catalog.API/Features/MenuItemAnalytics/RecomputeToday/RecomputeTodayAnalyticsHandler.cs` (3 sites)
- `orderly-microservices/Services/Catalog/Catalog.API/Availability/MenuItemAnalyticsNightlyRecomputeService.cs` (3 sites)
- `orderly-microservices/Services/Basket/Basket.API/Data/TenantedSessionFactory.cs` (3 sites)
- `orderly-microservices/Services/Ordering/Ordering.Application/...` (2 sites)
- `orderly-microservices/Services/Kitchen/Kitchen.API/Features/GetKitchenQueue/GetKitchenQueue.cs` (3 sites)
- Remaining single-site files across `BuildingBlocks`, `BuildingBlocks.Observability.Tests`, `Basket.API`, `Catalog.API.Tests`, `Kitchen.API`, `Ordering.Infrastructure`

**Pattern** (apply mechanically):
```csharp
// before (single-line)
if (row.X < 0) row.X = 0;
// after
if (row.X < 0)
{
    row.X = 0;
}

// before (multiline — dangerous, second statement is NOT controlled by the if)
if (condition)
    statement1;
    statement2;  // ← always executes, looks like it's inside the if
// after
if (condition)
{
    statement1;
}
statement2;  // ← now visually clear this is unconditional
```

**Risk**: zero for single-line sites (whitespace-only). **Low for multiline sites** — verify each multiline braceless block manually to confirm the second statement was indeed not intended to be inside the `if`. Run the full test suite after.

**Verification gate**:
- `dotnet build orderly-microservices.slnx` — IDE0011 / SA1503 sites gone from warning list.
- All test projects green.
- `dotnet format whitespace --verify-no-changes` — green.

### Phase 2 — Null checks (65 sites, 11 projects, one PR)

**Goal**: every `!= null` → `is not null`, every `== null` → `is null`.

**Critical files** (representative):
- `orderly-microservices/Services/Discount/Discount.Grpc/...` (18 `== null`, mostly inside `Where(c => c.DeletedAt == null ...)` — EF Core 9 + Npgsql translates `== null` and `is null` to identical SQL, semantically safe)
- `orderly-microservices/Services/Catalog/Catalog.API/Features/Reservations/SeatReservation/SeatReservationHandler.cs` (`table != null`)
- `orderly-microservices/Services/Catalog/Catalog.API.Tests/Integration/Endpoints/TableEndpointTests.cs` (3 `!= null`)
- `orderly-microservices/Services/Kitchen/Kitchen.API/Infrastructure/Data/Configurations/KitchenTicketConfiguration.cs` (2)
- `orderly-microservices/Services/Ordering/Ordering.Infrastructure/Data/Configurations/OrderItemConfiguration.cs` (2)
- `orderly-microservices/BuildingBlocks/Behaviors/ValidationBehavior.cs` (`f != null` inside a LINQ `.Where`)
- ~5 more in `BuildingBlocks/Authorization`, `BuildingBlocks.Messaging/MassTransit/Extensions.cs`

**Pattern** (apply mechanically; do NOT touch XML doc comments or `//` comments):
```csharp
// before
.Where(c => c.DeletedAt == null && c.ExpirationDate <= now)
// after
.Where(c => c.DeletedAt is null && c.ExpirationDate <= now)
```

**Caveats**:
- **Skip XML doc comments and `//` comments** — `Discount.Grpc/Services/DiscountService.cs:182` and `Protos/Discount.cs` (in obj/, already excluded) have `== null` inside `<c>` tags and prose. Manual review for any XML doc hit.
- **EF Core safety**: verified by reading 15 of the 18 Discount.Grpc sites. All are `c.DeletedAt == null` LINQ predicates. EF Core 5+ translates `is null` to the same SQL. Safe.
- **Marten DB validation** (run BEFORE bulk conversion in Catalog/Basket):
  1. Add a throwaway integration test in `Catalog.API.Tests`:
     ```csharp
     [Fact]
     public async Task MartenSupportsIsNullLinqTranslation()
     {
         await using IDocumentSession session = _store.LightweightSession();
         NpgsqlCommand command = session.Query<Restaurant>()
             .Where(r => r.DeletedAt is null).ToCommand();
         string sql = command.CommandText;
         Assert.Contains("IS NULL", sql, StringComparison.OrdinalIgnoreCase);
     }
     ```
  2. Verify the generated SQL produces `WHERE deleted_at IS NULL` (not a broken/empty predicate).
  3. If Marten fails: keep `== null` for Marten LINQ queries and add an inline comment `// Marten LINQ: == null required`.
- **One known inconsistency**: `BuildingBlocks/Entities/Interceptors/AuditableEntityInterceptor.cs:27` uses `if (context is null) return;` (already `is null`). Inconsistent with the codebase-wide `is not null` style elsewhere — no change needed, this is the *target* pattern. Use it as a model for the rest.

**Risk**: low. EF Core translation is safe, but Marten must be validated first.

**Verification gate**:
- `dotnet build orderly-microservices.slnx` — green.
- All test projects green.
- `dotnet format whitespace --verify-no-changes` — green.
- PowerShell sweep — 0 hits (excluding `obj/`, `bin/`, comments):
  ```powershell
  Get-ChildItem -Recurse -Include *.cs orderly-microservices |
    Where-Object { $_.FullName -notmatch 'obj|bin|Migrations|\.Designer\.cs' } |
    Select-String -Pattern '(\!\=|\=\=)\s*null' |
    Where-Object { $_.Line -notmatch '^\s*//' }
  ```

### Phase 3 — `var` → explicit type (4,001 sites, 30 projects, multiple PRs)

**Goal**: every `var <name>` becomes `<InferredType> <name>`, except anonymous types, `using var`, `await using var`, `out var`, tuple deconstructions, and LINQ `from`-query syntax.

**Strategy**: write a one-time Roslyn script (`scripts/quality-helpers/convert-var-to-explicit.csx`). `dotnet format style` does not have a safe var→explicit code-fix path — IDE0007 is the *inverse* direction. The plan §3 comment in `phase-guard.ps1` excludes `dotnet format style` for good reason.

**Critical files**:
- New: `scripts/quality-helpers/convert-var-to-explicit.csx` (Roslyn, ~150 lines)
- New: `scripts/quality-helpers/convert-var-to-explicit.ps1` (wrapper with `--dry-run` and `--verify` flags)
- All 30 .csproj under `orderly-microservices/` (excluding `obj/`, `bin/`, `**/Migrations/**/*.cs`, `**/*.Designer.cs`, `**/*.g.cs`, `**/*.AssemblyInfo.cs`)

**Script behaviour** (specification):
- Loads each .csproj via `MSBuildWorkspace`.
- For every `LocalDeclarationStatementSyntax`, `ForEachStatementSyntax`, `ForEachVariableStatementSyntax`, and `UsingStatementSyntax` where `var` is used:
  - Resolve type via `SemanticModel.GetTypeInfo(declaration.Type).Type`.
  - Format the type name (handles nullables `string?`, generics `List<Foo>`, tuples `(int, string)`, qualified names `Basket.API.Models.Basket`).
  - Skip if the declaration is `using var`, `await using var`, `out var`, anonymous, or tuple-deconstruct (`var (a, b) = ...`).
  - Skip if the type resolves to `null` (compiler couldn't infer).
  - Skip if the file is generated (`*.g.cs`, `*.Designer.cs`, `*.AssemblyInfo.cs`, `**/Migrations/**/*`).
- Writes back to disk, gated on `if (File.Exists && !IsGenerated(path))`.
- `--dry-run` emits the diff without writing.
- `--verify` runs the gate (`dotnet format whitespace --verify-no-changes`) before writing.

**Test project `var` policy** (decided upfront to bound scope):

~2,000 of 4,001 var sites (49%) are in `*.Tests` projects. Test readability favours `var` — e.g. `var result = await handler.Handle(query, CancellationToken.None);` is clearer than `GetRestaurantByIdResult result = await handler.Handle(query, CancellationToken.None);` in a test context.

**Decision: exempt test projects.** Add a permanent `[*Tests/**/*.cs]` `.editorconfig` override downgrading IDE0008 to `:suggestion`:

```ini
# Test projects: var is permitted for readability of test data builders and assertions.
[*Tests/**/*.cs]
csharp_style_var_for_built_in_types    = false:suggestion
csharp_style_var_when_type_is_apparent = true:suggestion
csharp_style_var_elsewhere            = false:suggestion
```

This cuts Phase 3 scope from ~4,001 to ~2,001 production-code sites. PR #3.10 is dropped.

**PR order within Phase 3** (BUILDINGBLOCKS.DEV FIRST — it's the canary with `TreatWarningsAsErrors=true`):

1. **PR #3.1** — `BuildingBlocks.Dev` + `BuildingBlocks.Dev.Tests` (16 + 70 var sites). Smoke test the gate end-to-end. If this PR fails the canary build, every subsequent PR fails too — fix the script here, not later. **This PR also removes the temporary canary `.editorconfig` override from Phase 0.**
2. **PR #3.2** — `BuildingBlocks`, `BuildingBlocks.Messaging`, `BuildingBlocks.Persistence`, `BuildingBlocks.Observability`, `BuildingBlocks.Tests`, `BuildingBlocks.Observability.Tests` (small projects, ~200 var sites combined).
3. **PR #3.3** — `ServiceDefaults`, `AppHost`, `ApiGateway/YarpApiGateway`, `Orderly.DevMCP.Server` (~10 var sites total).
4. **PR #3.4** — `Services/Discount/Discount.Grpc` (158 var sites) — biggest single-service pile, do it before tests pile up.
5. **PR #3.5** — `Services/Identity/Identity.API` (363 var sites) — biggest handler pile.
6. **PR #3.6** — `Services/Catalog/Catalog.API` (515 var sites) — biggest of all. May need to split into 2 sub-PRs if review load demands it.
7. **PR #3.7** — `Services/Kitchen/Kitchen.API` (15 var sites — small, batch with #3.8).
8. **PR #3.8** — `Services/Basket/Basket.API` (118 var sites).
9. **PR #3.9** — `Services/Ordering/Ordering.Domain`, `Ordering.Application`, `Ordering.Infrastructure`, `Ordering.API` (16 + 70 + 10 + 35 = 131 var sites).

**Carve-outs** (per the Plan agent's audit, ~15% of var sites must stay `var` - `AGENTS.md` explicitly carves these out):
- `using var scope = ...;`
- `await using var session = ...;`
- `out var id`
- Anonymous types: `var anon = new { Id = 1, Name = "x" };`
- Tuple deconstructions: `var (a, b) = (1, 2);`
- LINQ `from`-query syntax: `var q = from x in y select x;`

**Verification gate per PR**:
- `dotnet build orderly-microservices.slnx --no-restore` — green, IDE0008 warnings down by that PR's count.
- All test projects green.
- `dotnet format whitespace --verify-no-changes` — green.
- PowerShell sweep — count must drop monotonically per PR:
  ```powershell
  Get-ChildItem -Recurse -Include *.cs orderly-microservices |
    Where-Object { $_.FullName -notmatch 'obj|bin|Migrations|\.Designer\.cs|Tests' } |
    Select-String -Pattern '^\s*var\s+[A-Za-z_]' | Measure-Object
  ```

### Phase 4 — Manual refactor: guard clauses, pattern matching (per project)

**Goal**: convert deep `if`-nests to guard clauses; convert type-test chains to switch expressions where it improves clarity. The codebase already uses these idioms (see `BuildingBlocks/Exceptions/Handler/CustomExceptionHandler.cs:17-64` for the model switch expression, `:76` for the `is ValidationException validationException` pattern variable, `:87` for `is not null` guard). This phase EXTENDS the existing pattern. Implement property patterns (`if (obj is { Property: value })`) as well. Enforce `string.Empty` and `string.IsNullOrEmpty` checking on string properties where appropriate.

**Critical files** (representative; full list by handler review):
- `orderly-microservices/Services/Catalog/Catalog.API/Features/Restaurants/UpdateRestaurant/UpdateRestaurantHandler.cs` — 6 `if (restaurant.X != command.X) changedFields.Add(...);` → extract to `RecordChanges(restaurant, command)` helper or switch expression. Phase 1 already added braces; Phase 4 unwraps the semantic.
- `orderly-microservices/Services/Catalog/Catalog.API/Features/Restaurants/GetBrandsHandler.cs:15`, `GetTablesHandler.cs:15`, `GetReservationsHandler.cs:20` — 3 identical `if (pageSize > 50) pageSize = 50;` clamps → extract `ClampPageSize(int pageSize, int max) => pageSize > max ? max : pageSize;` extension method. Done in one PR across Catalog.
- `orderly-microservices/Services/Catalog/Catalog.API/Features/Restaurants/UpdateRestaurantEndpoint.cs:28` — `if (id != request.Id) return Results.BadRequest();` → guard-clause already; nothing to refactor (verify during review).
- `orderly-microservices/Services/Basket/Basket.API/Checkout/CheckoutBasketHandler.cs` (and similar handler files) — review each for `if (x is null) throw ...; if (y is null) throw ...;` chains → consolidate via `[NotNull]` annotations or `ArgumentNullException.ThrowIfNull(...)` (.NET 6+ API).

**PR strategy**: one PR per service (Catalog, Basket, Discount, Identity, Kitchen, Ordering). Manual review each.

**Risk**: medium. Guard-clause conversions can flip the order of validation side-effects. **Full test suite per PR** is mandatory.

**Verification gate per PR**:
- `dotnet build orderly-microservices.slnx` — green.
- All test projects green (especially integration tests that hit the affected endpoints).
- `dotnet format whitespace --verify-no-changes` — green.
- Manual review confirms `is null` / `is not null` and pattern variables replace type-test chains.

### Phase 5 — Verification + CI workflow + phase-guard integration

**Goal**: prove the gate works end-to-end and prevent future regression. Unified enforcement: same checks run locally (via `phase-guard.ps1`) and in CI (via GitHub Actions).

**Critical files**:
- New: `.github/workflows/style-gate.yml` (CI workflow — placed at repo root alongside existing `openapi-smoke.yml`)
- New: `scripts/quality-helpers/check-code-style.ps1` (grep-based enforcement of `== null`, `!= null`, braceless control flow, empty-string comparisons)
- Modify: `scripts/phase-guard.ps1` — add section 22 (code style)

**CI workflow** (`.github/workflows/style-gate.yml`):

```yaml
name: style gate
on:
  push:
    branches: [main]
    paths:
      - 'orderly-microservices/**'
      - '.editorconfig'
      - 'orderly-microservices/Directory.Build.props'
      - '.github/workflows/style-gate.yml'
  pull_request:
    branches: [main]
    paths:
      - 'orderly-microservices/**'
      - '.editorconfig'
      - 'orderly-microservices/Directory.Build.props'
      - '.github/workflows/style-gate.yml'
jobs:
  whitespace:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with: { dotnet-version: '10.0.x' }
      - run: cd orderly-microservices && dotnet format whitespace orderly-microservices.slnx --verify-no-changes --no-restore
  analyzers:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with: { dotnet-version: '10.0.x' }
      - run: cd orderly-microservices && dotnet build orderly-microservices.slnx -warnaserror:IDE0008,IDE0011,SA1503 --no-restore
        # IDE0008 = var → explicit type
        # IDE0011 = braces required
        # SA1503  = braces must not be omitted (StyleCop equivalent of IDE0011)
        #
        # NOT included and why:
        # - IDE0041: only covers ReferenceEquals → is null, NOT == null → is null
        # - IDE0021: "expression body for constructors" — not an AGENTS.md rule
  null-check:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - name: No == null or != null in source
        run: |
          HITS=$(grep -rnE '(\!\=|\=\=)\s*null' --include='*.cs' orderly-microservices/ \
            --exclude-dir=obj --exclude-dir=bin \
            | grep -v '^\s*//' | grep -v 'Migrations/' | grep -v '\.Designer\.cs' \
            | grep -v '\.g\.cs' | wc -l)
          if [ "$HITS" -gt 0 ]; then
            echo "❌ Found $HITS == null / != null sites:"
            grep -rnE '(\!\=|\=\=)\s*null' --include='*.cs' orderly-microservices/ \
              --exclude-dir=obj --exclude-dir=bin \
              | grep -v '^\s*//' | grep -v 'Migrations/' | grep -v '\.Designer\.cs' \
              | grep -v '\.g\.cs'
            exit 1
          fi
```

**Local enforcement** (`scripts/quality-helpers/check-code-style.ps1`):

PowerShell script that enforces the rules IDE analyzers cannot cover:
- `== null` / `!= null` → 0 hits expected (excluding comments, generated code)
- `== ""` / `!= ""` / `.Equals("")` → 0 hits expected
- Braceless `if` / `else` / `for` / `foreach` / `while` → 0 hits expected (belt-and-suspenders with the analyzer)

Wire into `phase-guard.ps1` as a new section:
```powershell
# ----------------------------------------------------------------
# 2️⃣2️⃣ Code style (AGENTS.md — null checks, braces, string handling)
# ----------------------------------------------------------------
Write-Section "🎨 Code style (AGENTS.md)"

pwsh -NoProfile -File "$PSScriptRoot/quality-helpers/check-code-style.ps1"
if ($LASTEXITCODE) { throw "❌ Code style violations found" }
```

**Do NOT run** `dotnet format style` in CI or locally. `phase-guard.ps1` §3 comment block documents the rationale.

**Regression check file** (mandatory):
- New: `BuildingBlocks.Tests/Style/StyleRegressionMarker.cs` (intentionally violates IDE0008 and IDE0011/SA1503; guarded by `#if STYLE_GATE_TEST` so it doesn't pollute daily IDE warnings). Verify it fails `dotnet build -warnaserror:IDE0008,IDE0011,SA1503` when the symbol is defined. Confirms the analyzer gate works.

**Final verification**:
- `dotnet build orderly-microservices.slnx` — clean.
- `dotnet test orderly-microservices.slnx` — all green.
- `dotnet format whitespace --verify-no-changes` — zero diff.
- `pwsh scripts/phase-guard.ps1 -Quick` — green (now includes section 22 code style).
- `pwsh scripts/quality-helpers/check-code-style.ps1` — green (0 null/brace/string violations).
- CI workflow green on a synthetic PR.

**Do NOT promote `TreatWarningsAsErrors=true` for all projects**. The CI workflow does the enforcement via `-warnaserror:...`. BuildingBlocks.Dev and BuildingBlocks.Dev.Tests keep their existing `TreatWarningsAsErrors=true`. Per-project promotion can come in a follow-up plan if the user wants stricter local enforcement.

### Phase 6 — Plan close + memory note

**Goal**: capture what shipped.

**Critical files**:
- New: `.agents/notes/code-style-iteration-closed.md` (project memory)
- Create: `MEMORY.md` (does not exist yet — one-line pointer to the new memory file)
- Update: `.agents/plan/platform/CODE_STYLE_ITERATION_PLAN.md` (mark all phases ✅, set version to v3.0)

**Edits**:
- Write the memory note in the same format as existing entries (see `.agents/notes/catalog_gap_analysis.md` for the precedent).
- One-line entry in `MEMORY.md`: `- [Code-style iteration closed 2026-MM-DD](code-style-iteration-closed.md) — short hook`.

**Do NOT**:
- Add follow-up commits unless asked (per project memory `never-commit-unless-asked.md`).
- Promote `TreatWarningsAsErrors=true` globally.
- Add tests that don't exist in the existing test suite.
- Run `dotnet format style` at any point.

---

## Verification (end-to-end, after all phases)

1. **Build**: `dotnet build orderly-microservices/orderly-microservices.slnx --no-restore` — clean.
2. **Tests**: `dotnet test orderly-microservices/orderly-microservices.slnx --no-restore` — all green.
3. **Format gate**: `dotnet format whitespace orderly-microservices/orderly-microservices.slnx --verify-no-changes --no-restore` — zero diff.
4. **Local quality gate**: `pwsh scripts/phase-guard.ps1 -Quick` — green (includes section 22 code style).
5. **Code style script**: `pwsh scripts/quality-helpers/check-code-style.ps1` — green (0 null/brace/string violations).
6. **Manual PowerShell sweeps** (each returns 0 hits, excluding `obj/`, `bin/`, `**/Migrations/**/*`, `*.Designer.cs`, comments):
   ```powershell
   $Filter = { $_.FullName -notmatch 'obj|bin|Migrations|\.Designer\.cs|\.g\.cs' }

   # == null / != null (expect 0)
   Get-ChildItem -Recurse -Include *.cs orderly-microservices | Where-Object $Filter |
     Select-String -Pattern '(\!\=|\=\=)\s*null' |
     Where-Object { $_.Line -notmatch '^\s*//' }

   # braceless if/else/for/foreach/while (expect 0)
   Get-ChildItem -Recurse -Include *.cs orderly-microservices | Where-Object $Filter |
     Select-String -Pattern '^\s*(if|else if|for|foreach|while)\s*\(.*\)\s*[a-zA-Z]'

   # var declarations in production code (expect carve-out count only)
   Get-ChildItem -Recurse -Include *.cs orderly-microservices |
     Where-Object { $_.FullName -notmatch 'obj|bin|Migrations|\.Designer\.cs|Tests' } |
     Select-String -Pattern '^\s*var\s+[A-Za-z_]'

   # empty string comparisons (expect 0)
   Get-ChildItem -Recurse -Include *.cs orderly-microservices | Where-Object $Filter |
     Select-String -Pattern '== ""|!= ""|\.Equals\(""\)'
   ```
7. **CI**: new `style-gate` workflow green on a synthetic PR against `main`.

---

## Critical files (summary)

| Path | Phase | Action |
|---|---|---|
| `orderly-microservices/.editorconfig` | 0 | Add pattern-matching + null-check keys, promote var keys, add canary overrides |
| `orderly-microservices/BuildingBlocks.Messaging/MassTransit/Extensions.cs` | 1 | 4 multiline braceless `if` (highest priority) |
| `orderly-microservices/BuildingBlocks.Dev/Extensions/OpenIddictServerExtensions.cs` | 1 | 2 multiline braceless `if/else` |
| `orderly-microservices/Services/Catalog/Catalog.API/Features/Restaurants/UpdateRestaurant/UpdateRestaurantHandler.cs` | 1, 4 | 6 braceless ifs + extract helper |
| `orderly-microservices/Services/Discount/Discount.Grpc/...` | 2 | 18 `== null` (EF Core safe) |
| `scripts/quality-helpers/convert-var-to-explicit.csx` | 3 | New — Roslyn script (~150 lines) |
| `scripts/quality-helpers/convert-var-to-explicit.ps1` | 3 | New — wrapper with `--dry-run` / `--verify` |
| `orderly-microservices/BuildingBlocks.Dev/...` | 3 | First PR — canary for TreatWarningsAsErrors |
| `orderly-microservices/Services/Catalog/Catalog.API/Features/Restaurants/GetBrandsHandler.cs` and 2 sibling files | 4 | Extract `ClampPageSize` extension |
| `.github/workflows/style-gate.yml` | 5 | New CI workflow (repo root) |
| `scripts/quality-helpers/check-code-style.ps1` | 5 | New — grep-based null/brace/string enforcement |
| `scripts/phase-guard.ps1` | 5 | Add section 22 (code style) |
| `orderly-microservices/BuildingBlocks.Tests/Style/StyleRegressionMarker.cs` | 5 | New regression check file |
| `.agents/notes/code-style-iteration-closed.md` | 6 | New project memory note |
| `MEMORY.md` | 6 | New project memory index |

(For Phase 3, the pattern "var x = ...;" → "<InferredType> x = ...;" applies to ~2,001 production-code sites across 30 .csproj. Apply the script; do not enumerate every file. Test projects are exempt.)

---

## Out of scope (defer)

- **Copyright headers / SA1633-SA1641** — already disabled in .editorconfig §10; separate refactor.
- **Whitespace / line-ending / file-encoding normalisation** — Phase 4 of Docker / Contract / Coverage plan, separately scoped.
- **NodaTime / MediatR convention enforcement** — already covered by Phase 2 of `QUALITY_GATE_ENHANCEMENT_PLAN.md`.
- **Test coverage thresholds** — Phase 4 of `QUALITY_GATE_ENHANCEMENT_PLAN.md`.
- **Promoting `TreatWarningsAsErrors=true` globally** — CI workflow handles enforcement; per-project promotion can come in a follow-up plan.
- **The 24 already-disabled StyleCop / FxCop rules** — out of scope; revisit per-rule in a later phase.
- **`.gitattributes` line-ending normalisation** — the plan touches many files; without `.gitattributes` there is a risk of mixed CRLF/LF. Defer to the whitespace-normalisation phase above.

## Risks and mitigations

- **BuildingBlocks.Dev + Dev.Tests canary**: both have `TreatWarningsAsErrors=true`. Phase 0 promotion to `:warning` will turn style violations into build errors in both projects. Mitigation: temporary per-directory editorconfig overrides downgrade to `:suggestion` until PR #3.1 fixes their var sites and removes the overrides.
- **Generated code**: `**/Migrations/**/*.cs`, `*.Designer.cs`, `*.g.cs`, `*.AssemblyInfo.cs`, `obj/`, `bin/` must be excluded by the Roslyn script. The audit already excludes these.
- **XML doc comments**: any `== null` / `!= null` inside `<c>` tags or `///` prose must NOT be rewritten. Filter the sed-style regex with a negative lookahead for `//` or `///` context. Manual review for any hit.
- **EF Core null translation**: verified safe on EF Core 9 + Npgsql. If a future project uses a different provider, re-verify before bulk conversion.
- **Marten DB constraints**: Marten's LINQ provider will be validated with a throwaway integration test (see Phase 2 caveats) before translating `is null` in the `Catalog` and `Basket` services. If the test fails, those LINQ queries keep `== null` with an inline comment.
- **`dotnet format style` auto-fix trap**: do not run it. The plan §3 comment in `phase-guard.ps1` documents why. The Roslyn script replaces its IDE0008 code-fix with a deterministic equivalent.
- **No IDE analyzer for `== null` → `is null`**: IDE0041 only covers `ReferenceEquals`. The `== null` rule is enforced by `check-code-style.ps1` (local) and the `null-check` CI job (GitHub Actions). Both use grep, not an analyzer.
- **Phase 4 manual refactors** are the only phase with real behavioural risk. Per-service PRs + full test suite per PR.
- **Test projects exempt from `var` enforcement**: `[*Tests/**/*.cs]` editorconfig override downgrades IDE0008 to `:suggestion`. This is a deliberate readability trade-off, not a loophole.
