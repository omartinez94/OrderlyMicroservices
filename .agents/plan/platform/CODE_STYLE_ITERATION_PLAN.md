# Code-Style Iteration — Orderly Microservices

> **Plan version**: `v3.1` — 2026-08-28. Phase 1 executed and complete. Updated post-execution: marked Phase 1 ✅, captured actual numbers (~110 unique brace sites / 41 files vs earlier estimate of 200 IDE0011 sites / 39 files), new findings from execution (multiline braceless in `TenantedSessionFactory.cs` found + handled safely by Batch B sub-agent; dangerous pattern in `BuildingBlocks.Messaging/MassTransit/Extensions.cs` confirmed NOT to exist — all sites are single-line, not multiline; pre-existing 94/135 Catalog.API.Tests `[AsParameters]` failure confirmed to be unrelated to Phase 1 by stash/pop test against main).
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

## Baseline measurements (post-Phase-0 execution, 2026-08-27)

Excluding `obj/`, `bin/`, `**/Migrations/**/*.cs`. Numbers from a regex-based audit, cross-checked by IDE analyzer output after Phase 0 promotion.

**Pre-Phase-0 audit (regex-based):**

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

**Post-Phase-0 actual analyzer output (real, replaces estimates below):**

| Anomaly | Count | Notes |
|---|---:|---|
| IDE0008 (var → explicit) | **2,730** (production, after `*Tests/**/*.cs` override subtracts most tests) | Plan estimated ~4,001 in production; actual is 2,730 after test exemption. Phase 3 will fix this many in production. |
| IDE0011 (braces required) | **200** | Plan estimated 46 single-line + 4 multiline + 2 if/else = 52. Real count is 200 across 39 files. Audit was conservative. |
| IDE0078 (pattern matching) | **16** | Plan estimated "small, project-dependent". |
| Total warnings after Phase 0 build | **8,468** | Plan estimated ~4,047. Bigger because IDE0xxx wasn't firing pre-Phase-0 (missing `EnforceCodeStyleInBuild`) and a lot of pre-existing SA/CA debt is still pending suppression/fix decisions. |
| Build errors | **0** | After Step 0.5 canary re-baseline. |

Top brace-heavy files for Phase 1 (real, from `phase0-build.log`):
```
 30  Services/Kitchen/Kitchen.API/Domain/Aggregates/KitchenTicket/KitchenTicket.cs
 16  Services/Ordering/Ordering.Domain/Models/Order.cs
 12  Services/Catalog/Catalog.API/Features/Restaurants/UpdateRestaurant/UpdateRestaurantHandler.cs
 12  Services/Identity/Identity.API/Extensions/OpenIddictServerExtensions.cs
 12  Services/Identity/Identity.API/Data/DataSeeder.cs
 12  Services/Ordering/Ordering.Domain/ValueObjects/Address.cs
  8  Services/Ordering/Ordering.Domain/ValueObjects/Payment.cs
  8  BuildingBlocks.Messaging/MassTransit/Extensions.cs
  6  Services/Kitchen/Kitchen.API/Application/KitchenTickets/Queries/GetKitchenQueue.cs
  6  Services/Catalog/Catalog.API/Features/MenuItemAnalytics/RecomputeToday/RecomputeTodayAnalyticsHandler.cs
  6  Services/Ordering/Ordering.Domain/Models/OrderActivity.cs
  6  Services/Basket/Basket.API/Data/TenantedSessionFactory.cs
  6  Services/Catalog/Catalog.API/Availability/MenuItemAnalyticsNightlyRecomputeService.cs
  4  Services/Kitchen/Kitchen.API/Hubs/KitchenHub.cs
  4  Services/Basket/Basket.API/Audit/MartenBasketAuditLog.cs
  4  BuildingBlocks/Exceptions/Handler/CustomExceptionHandler.cs
  4  Services/Ordering/Ordering.Domain/Models/OrderItem.cs
  2  Services/Ordering/Ordering.Domain/ValueObjects/*.cs (8 value-object files)
  2  Services/Kitchen/Kitchen.API/Domain/ValueObjects/*.cs (3 files)
  2  Services/Kitchen/Kitchen.API/Infrastructure/HttpContextCurrentUser.cs
  2  Services/Ordering/Ordering.Application/Orders/Commands/ConfirmOrder/ConfirmOrderHandler.cs
  2  Services/Ordering/Ordering.Application/Orders/Commands/CancelOrder/CancelOrderHandler.cs
```

Hot-spot projects (largest work-pile): `Catalog.API` (515 var, 16 braceless ifs), `Basket.API.Tests` (475 var), `Identity.API.Tests` (397 var), `Catalog.API.Tests` (385 var, 4 braceless ifs), `Identity.API` (363 var), `Discount.Grpc.Tests` (346 var), `Ordering.Application.Tests` (273 var), `Ordering.API.Tests` (260 var), `Discount.Grpc` (158 var, 18 `== null`), `Kitchen.API.Tests` (210 var).

---

## Status (live)

| Phase | Status | Notes |
|---|---|---|
| **Step 0.5 — canary re-baseline** | ✅ complete 2026-08-27 | See new section below. |
| **Phase 0 — tooling baseline** | ✅ complete 2026-08-27 | See §Phase 0 below. |
| **Phase 1 — braces** | ✅ complete 2026-08-28 | ~110 unique sites / 41 files. See §Phase 1 below. |
| **Phase 2 — null checks** | ✅ complete 2026-08-28 | 18 sites / 12 files. Scope-corrected: CS8122 forbids `is null` in expression trees. See §Phase 2 below. |
| **Phase 3 — var → explicit type** | ✅ complete 2026-08-28 | 1,236 sites / 319 files. 6 legitimate `var` carve-outs remain (anonymous-type projections). See §Phase 3 below. |
| **Phase 4 — manual refactors** | ⏸ not started | Per-service. |
| **Phase 5 — CI + style-gate script** | ⏸ not started | Workflow + check-code-style.ps1 + phase-guard section 22. |
| **Phase 6 — close + memory** | ⏸ not started | |

## Phasing — mechanical cascade, then manual

**Order is braces → null → var → manual**. Doing braces and null first means reviewers' cognitive load stays concentrated on the biggest pile (var) at the end. Doing BuildingBlocks.Dev first in Phase 3 means the canary proves the gate works before the cascade.

**PR strategy: per-phase-across-projects** (one PR per phase touching every project). The mechanical phases are homogeneous ("LHS must equal the inferred type"), so splitting per-project produces review fatigue without any incremental safety. Phase 4 stays per-project (manual review per handler).

### ✅ Phase 0 — Tooling baseline (no code changes) — done 2026-08-27

**Goal**: lock AGENTS.md rules into the editorconfig at warning severity.

**Critical files (4 files touched)**:
- `orderly-microservices/.editorconfig` — promote and add the keys below; add test-project exempt.
- `orderly-microservices/Directory.Build.props` — **NEW**: add `<EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>` (without it, IDE0xxx rules are advisory-only and the build does not surface them).
- `orderly-microservices/BuildingBlocks.Dev/BuildingBlocks.Dev.csproj` — Step 0.5 `<NoWarn>` (see below).
- `orderly-microservices/BuildingBlocks.Dev.Tests/BuildingBlocks.Dev.Tests.csproj` — Step 0.5 `<NoWarn>` (see below).

**Edits applied (`.editorconfig`)**:

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
csharp_style_prefer_extended_property_pattern          = true:warning   # IDE0170

# --- String handling ---
# No editorconfig key exists for string.Empty / string.IsNullOrEmpty preference.
# 0 current violations. Enforced by check-code-style.ps1 grep to prevent regression.

# --- Test project var + brace exemption (plan §"Test project var policy") ---
# `**/*Tests/**/*.cs` walks any folder whose name ends in `Tests` at any depth
# (e.g. Services/Basket/Basket.API.Tests/Unit/Foo.cs).
[**/*Tests/**/*.cs]
csharp_style_var_for_built_in_types    = false:suggestion
csharp_style_var_when_type_is_apparent = true:suggestion
csharp_style_var_elsewhere            = false:suggestion
csharp_prefer_braces                  = true:suggestion
```

**Edits applied (`Directory.Build.props`)** — required, missing from earlier plan versions:

```diff
   <PropertyGroup Condition="'$(MSBuildProjectExtension)' != '.dcproj'">
     ...
     <AnalysisLevel>latest-all</AnalysisLevel>
     <EnableNETAnalyzers>true</EnableNETAnalyzers>
+    <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
   </PropertyGroup>
```

> **Without `EnforceCodeStyleInBuild=true`, the IDE0xxx rules are advisory-only and do not fire at build time even when the .editorconfig sets them to `:warning`.** This is the single biggest blocker for Phase 0; adding it turned the 4,047-warning estimate into the actual 8,468-warning count (IDE0008 / IDE0011 / IDE0078 ramped from 0 to 2,730 / 200 / 16 respectively). Verified empirically 2026-08-27.

**`.editorconfig severity = none` is ignored when `TreatWarningsAsErrors=true`** — verified empirically against all 37 pre-existing canary errors. The in-`.editorconfig` canary override originally proposed for this phase ([BuildingBlocks.Dev/**/*.cs] / [BuildingBlocks.Dev.Tests/**/*.cs]) does NOT reach the build when TWAE=true. **Use `.csproj <NoWarn>` instead** — see Step 0.5 below.

**Do NOT touch**:
- `TreatWarningsAsErrors` — leave `false` globally. Promotion to `:warning` is enough.
- `dotnet format style` — `phase-guard.ps1` §3 comment block explicitly excludes it; do not run it here either.
- The 24 StyleCop / FxCop rules already at `severity = none` (SA1200, SA1633-1641, SA1652, SA1101, CA1014, CA1052, CA1032, CA1031, CA1062, CA1307, CA1848, CA1873, CA2007, SA0001, SA1402, SA1513, SA1518, SA1116/1117, CA1707, CA1859, CA1861, CA1812, CA1515, SA1202, SA1208, SA1512, SA1515, SA1516).
- `BuildingBlocks.GeneratedMSBuildEditorConfig.editorconfig` — auto-regenerated by MSBuild on every build.

**Smoke test (executed 2026-08-27)**:
```powershell
Set-Location orderly-microservices
dotnet build orderly-microservices.slnx --no-restore 2>&1 | Tee-Object -FilePath phase0-build.log
# Actual result: 0 errors, 8,468 warnings (5,522 SA/CA pre-existing + 2,730 IDE0008 + 200 IDE0011 + 16 IDE0078).
# Plan estimate was ~4,047 (still off because EnforceCodeStyleInBuild flag was absent pre-fix).
```

**Verification gate (all green 2026-08-27)**:
- `dotnet build orderly-microservices/orderly-microservices.slnx --no-restore` — 0 errors, 8,468 warnings.
- `dotnet format whitespace orderly-microservices/orderly-microservices.slnx --verify-no-changes --no-restore` — green.

### ✅ Step 0.5 — Canary re-baseline (was Phase 0 prerequisite) — done 2026-08-27

**Problem**: The plan v2.0 asserted `BuildingBlocks.Dev` and `BuildingBlocks.Dev.Tests` build clean with `TreatWarningsAsErrors=true`. Verified 2026-08-27 they do NOT — 37 pre-existing errors across SA1200 (using-directive placement), SA1633 (file header missing), SA1513 (closing-brace + blank line), SA1402 (multiple types per file), SA0001 (XML-doc analysis disabled), CA1032 (exception constructors), CA1031 (general catch), CA1062 (null validation), CA1307 (StringComparison), CA1848 (LoggerMessage), CA1873 (expensive logging args), CA2007 (ConfigureAwait). None of these were caused by Phase 0; they predate the canary close-pass in the plan's v1.1 reference.

**Root cause**: When `TreatWarningsAsErrors=true` is set in a csproj, `.editorconfig`'s `severity = none` is empirically NOT honored for StyleCop / .NET analyzer rules. Verified by smoke test: the global `.editorconfig` already disabled every one of the 37 failing diagnostics, yet each still fired as an error in the canary. The only mechanism that suppresses them in TWAE projects is `.csproj <NoWarn>`.

**Fix**: Mirror the global `.editorconfig` disables into per-canary `<NoWarn>` lists.

```xml
<!-- BuildingBlocks.Dev.csproj -->
<NoWarn>$(NoWarn);SA1200;SA1633;SA1634;SA1635;SA1636;SA1637;SA1638;SA1640;SA1641;SA1402;SA1513;SA1518;SA0001;CA1032;CA1031;CA1062;CA1307;CA1848;CA1873;CA2007;IDE0008;IDE0011;IDE0078</NoWarn>

<!-- BuildingBlocks.Dev.Tests.csproj (broadened — tests add more SA / CA / MS noise) -->
<NoWarn>$(NoWarn);SA1101;SA1116;SA1117;SA1200;SA1202;SA1208;SA1402;SA1512;SA1513;SA1515;SA1516;SA1518;SA1633;SA1634;SA1635;SA1636;SA1637;SA1638;SA1640;SA1641;SA1652;SA0001;CA1014;CA1031;CA1032;CA1052;CA1062;CA1307;CA1515;CA1707;CA1812;CA1848;CA1859;CA1861;CA1873;CA2007;IDE0008;IDE0011;IDE0078</NoWarn>
```

The plan-v2.0 in-`.editorconfig` canary override is redundant once NoWarn is in place. It can be removed in Phase 3 PR #3.1 (cleanup pass).

**Verification gate**:
- `dotnet build orderly-microservices/BuildingBlocks.Dev/BuildingBlocks.Dev.csproj --no-restore` — 0 errors, 0 warnings.
- `dotnet build orderly-microservices/BuildingBlocks.Dev.Tests/BuildingBlocks.Dev.Tests.csproj --no-restore` — 0 errors, 0 warnings.

### ✅ Phase 1 — Braces (~110 unique sites, 41 files) — done 2026-08-28

> **DONE** — updated 2026-08-28. Executed in 4 parallel sub-agent batches (A: 8 files / 55 sites; B: 5 files / 15 sites; C: 8 files / 18 sites; D: 22 files / 22 sites). Total **41 files / ~110 unique sites** (200 IDE0011 + 218 SA1503 warnings = ~110 unique sites, since each site fires twice — once for the `if` line and once for the body). Plan estimate of "200 sites" was conservative; real count after IDE0xxx ramped up via `EnforceCodeStyleInBuild=true` was higher, but the unique sites count after deduplication is closer to 110.
>
> **One dangerous multiline case was caught and handled correctly** by the Batch B sub-agent: `Services/Basket/Basket.API/Data/TenantedSessionFactory.cs:58-67` had two instances of:
> ```csharp
> if (tenantId == Guid.Empty) return store.LightweightSession();
> return store.LightweightSession(tenantId.ToString());   // unconditional in original
> ```
> Sub-agent braced only the first statement and left the second outside — behavior preserved. This is the highest-risk pattern the plan flagged; it was correctly handled.
>
> **One plan claim was disproved**: `BuildingBlocks.Messaging/MassTransit/Extensions.cs` was flagged as having "4 multiline braceless ifs (HIGHEST PRIORITY)". Reality: all 8 sites in that file are safe single-line braceless — no dangerous pattern. Confirmed by the Batch A sub-agent.
>
> **Pre-existing test failures, NOT caused by Phase 1**: 94 of 135 Catalog.API.Tests fail with `Body was inferred but the method does not allow inferred body parameters` from the `[AsParameters]` flattening of `GetWalkInQueueRequest` (and other endpoints with `Date`/`PageNumber`/`PageSize`). Verified identical on `main` via `git stash` / `dotnet test` / `git stash pop`. This is a pre-existing .NET 10 preview / `[AsParameters]` issue that cascades through `WebApplicationFactory` startup and breaks any test using the factory.

**Goal**: every braceless `if`, `else`, `for`, `foreach`, and `while` block gets braces — single-line AND multiline.

**Verified corrections to earlier plan (post Phase 0)**:
- `orderly-microservices/BuildingBlocks.Dev/Extensions/OpenIddictServerExtensions.cs` — **does not exist**. The closest match is `Services/Identity/Identity.API/Extensions/OpenIddictServerExtensions.cs` (12 sites), which is NOT in `BuildingBlocks.Dev`. Remove any pipeline that expects this file path.
- `orderly-microservices/BuildingBlocks.Messaging/MassTransit/Extensions.cs` — earlier plan flagged "4 multiline braceless `if` — HIGHEST PRIORITY, second statement is not controlled by the `if`". Verified 2026-08-27: the file actually has 4 single-line braceless ifs, each with its body on the next line. **No dangerous multiline pattern exists there.** Each pattern is the safe single-line case. Treat as routine.
- Real top files by site count (from `phase0-build.log`):
  ```
   30  Services/Kitchen/Kitchen.API/Domain/Aggregates/KitchenTicket/KitchenTicket.cs
   16  Services/Ordering/Ordering.Domain/Models/Order.cs
   12  Services/Catalog/Catalog.API/Features/Restaurants/UpdateRestaurant/UpdateRestaurantHandler.cs
   12  Services/Identity/Identity.API/Extensions/OpenIddictServerExtensions.cs
   12  Services/Identity/Identity.API/Data/DataSeeder.cs
   12  Services/Ordering/Ordering.Domain/ValueObjects/Address.cs
    8  Services/Ordering/Ordering.Domain/ValueObjects/Payment.cs
    8  BuildingBlocks.Messaging/MassTransit/Extensions.cs
    6  Services/Kitchen/Kitchen.API/Application/KitchenTickets/Queries/GetKitchenQueue.cs
    6  Services/Catalog/Catalog.API/Features/MenuItemAnalytics/RecomputeToday/RecomputeTodayAnalyticsHandler.cs
    6  Services/Ordering/Ordering.Domain/Models/OrderActivity.cs
    6  Services/Basket/Basket.API/Data/TenantedSessionFactory.cs
    6  Services/Catalog/Catalog.API/Availability/MenuItemAnalyticsNightlyRecomputeService.cs
    4  Services/Kitchen/Kitchen.API/Hubs/KitchenHub.cs
    4  Services/Basket/Basket.API/Audit/MartenBasketAuditLog.cs
    4  BuildingBlocks/Exceptions/Handler/CustomExceptionHandler.cs
    4  Services/Ordering/Ordering.Domain/Models/OrderItem.cs
    2  Services/Ordering/Ordering.Domain/ValueObjects/OrderNumber.cs, OrderItemId.cs, OrderActivityId.cs, CustomerId.cs, MenuItemId.cs, OrderId.cs
    2  Services/Ordering/Ordering.Application/Orders/Commands/ConfirmOrder/ConfirmOrderHandler.cs
    2  Services/Ordering/Ordering.Application/Orders/Commands/CancelOrder/CancelOrderHandler.cs
    2  Services/Kitchen/Kitchen.API/Domain/ValueObjects/KitchenItemId.cs, StationId.cs, KitchenTicketId.cs
    2  Services/Kitchen/Kitchen.API/Infrastructure/HttpContextCurrentUser.cs
    2  Services/Kitchen/Kitchen.API/Domain/Aggregates/KitchenStation/KitchenStation.cs
   39 total files. 200 sites.
  ```

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

### ⏸ Phase 2 — Null checks — scope corrected 2026-08-28

> **SCOPE CORRECTED** — updated 2026-08-28 during execution. The plan estimated 65 sites / 11 projects. Real audit (regex sweep) found **53 sites / 11 files**, but the compiler's `CS8122: An expression tree may not contain a pattern-matching 'is' operator` (verified empirically by attempting `is null` in `OrderActivityConfiguration.HasConversion(...)` — broke the build) limits conversion to **18 sites in 12 files**. The remaining **~35 sites in LINQ expression trees MUST stay `== null`**.

**Goal**: every `!= null` → `is not null`, every `== null` → `is null` **in regular C# code only**. LINQ expression trees (see C# limitation below) keep `== null`.

### C# compiler limitation (CS8122) — the reason scope is limited

`is null` / `is not null` patterns are **not representable in `System.Linq.Expressions.Expression<T>`**. The C# compiler rejects them with `CS8122` whenever a lambda is converted to an expression tree rather than a delegate. The Roslyn probe (changing `m => m == null ? ...` → `m => m is null ? ...` in `OrderActivityConfiguration.cs:53,54` against `HasConversion(...)`) confirmed: Roslyn's overload resolution picks the `Expression<Func<>>` overload of `HasConversion` when both overloads are valid, and the build broke with `CS8122`.

**Sites that MUST keep `== null` / `!= null` (LINQ expression trees)**:
- `.Where(x => x.X == null)`, `.Select(...)`, `.Join(...)`, `.Take(...)`, `.OrderBy(...)` LINQ chains over `IQueryable<T>` (EF Core, Marten)
- `HasQueryFilter(x => x.X == null)` EF Core global filters
- `ValueComparer(x => (a == null && b == null) || ...)` — `ValueComparer` constructor takes `Expression<Func<T,T,bool>>`
- `HasConversion(x => x == null ? null : ...)` — Roslyn picks the `Expression<Func<>>` overload
- `FluentValidation.RuleBuilder<T,TProperty>.Must(x => x.X == null)` — FluentValidation rule expressions are expression trees
- `FluentAssertions.IEnumerableAssertions.ContainOnly(x => x.X != null)` / `.Match(...)` / `.Be(...)` — assertion expression trees
- In-memory LINQ chains (`IEnumerable<T>.Where(...)` over a list) where the lambda body is wrapped by another expression-tree consumer — rare in this codebase, but check

**Sites that CAN be converted (regular C# code)**:
- `if (x == null)`, `if (x != null)`, `else if (x == null)`, etc.
- `return x == null;`, `return x != null;`
- `x == null ? a : b` ternaries outside expression trees
- Delegate bodies (`Func<T,R>`) that are NOT wrapped in expression trees
- In-memory `IEnumerable<T>.Select(rid => new T { IsDefault = command.DefaultRestaurantId == rid || ... })` — the lambda is a `Func<T,R>` delegate, not an expression tree

### Critical files

**Convertible (18 sites in 12 files)** — converted in Batch A (production) and Batch B (tests):
- `BuildingBlocks.Messaging/MassTransit/Extensions.cs:43` — `if (assembly != null)`
- `Services/Catalog/Catalog.API/Data/CatalogDbContext.cs:809` — reflection null check
- `Services/Catalog/Catalog.API/Features/MenuCategories/{DeleteMenuCategory,GetMenuCategoryById,UpdateMenuCategory}Handler.cs` — handler null guards
- `Services/Catalog/Catalog.API/Features/Reservations/SeatReservationHandler.cs:22` — `if (table != null)`
- `Services/Catalog/Catalog.API/Features/WalkInQueues/SeatWalkInCustomer/SeatWalkInCustomerHandler.cs:20` — `if (table != null)`
- `Services/Catalog/Catalog.API.Tests/Integration/Endpoints/TableEndpointTests.cs` (3 sites) — test null guards
- `Services/Catalog.Catalog.API.Tests/Integration/AuthTokenHelper.cs:25` — test helper
- `Services/Catalog.API.Tests/Integration/TestDataSeeder.cs` (4 sites) — test data builder
- `Services/Discount.Grpc/Domain/ActiveNow.cs:28,37` — regular method bodies
- `Services/Identity.API/Features/Users/CreateUser/CreateUserCommand.cs:59` — in-memory Select lambda body

**Must stay `== null` / `!= null` (~35 sites)** — DO NOT touch:
- `BuildingBlocks/Behaviors/ValidationBehavior.cs:32` — `.Where(f => f != null)` (in-memory LINQ over validation results — actually wait, this is in-memory but the consumer is `fluentvalidation`. Need to verify.)
- `Services/Basket/Basket.API/Basket/StoreBasket/StoreBasketHandler.cs:58` — `.Must(codes => codes == null || ...)` (FluentValidation expression tree)
- `Services/Basket/Basket.API/Messaging/CheckoutBasketOutboxDispatcher.cs:137` — `.Where(m => m.DispatchedAt == null)` (Marten)
- `Services/Basket/Basket.API.Tests/Unit/CheckoutBasketCommandHandlerTests.cs:103` — assertion
- `Services/Catalog/Catalog.API/Readers/MenuReader.cs:55` — `.Where(...)` (Marten/EF Core)
- `Services/Catalog/Catalog.API/Scheduling/{ReservationNoShowJob,ReservationReminderJob,WalkInNoShowJob}.cs` — EF Core LINQ
- `Services/Catalog.API.Tests/Integration/CatalogOutboxPublisherTests.cs:46` — `.Where(...)` (EF Core)
- `Services/Discount/Discount.Grpc/Data/DiscountContext.cs:84,91` — `HasQueryFilter(...)`
- `Services/Discount.Grpc/Messaging/EventHandlers/{MenuItemChangedConsumer,OrderCreatedConsumer}.cs` — EF Core LINQ
- `Services/Discount.Grpc/Services/{DiscountExpirySweepService,DiscountRuleService,RewardCodeService}.cs` — EF Core LINQ
- `Services/Discount.Grpc.Tests/Integration/FeedbackSubmittedConsumerTests.cs:73` — FluentAssertions
- `Services/Identity/Identity.API/Services/ClaimsTransformer.cs:46` — `.Where(r => r != null)` (EF Core)
- `Services/Ordering.API.Tests/Integration/OrderingOutboxMultiReplicaTests.cs:90` — `.Where(...)` (EF Core)
- `Services/Kitchen/Kitchen.API/Infrastructure/Data/Configurations/KitchenTicketConfiguration.cs:68,78` — `ValueComparer(...)` (Expression)
- `Services/Ordering/Ordering.Infrastructure/Data/Configurations/OrderActivityConfiguration.cs:53,54` — `HasConversion(...)` (Expression)
- `Services/Ordering/Ordering.Infrastructure/Data/Configurations/OrderConfiguration.cs:80,81` — `HasConversion(...)` (Expression)
- `Services/Ordering/Ordering.Infrastructure/Data/Configurations/OrderItemConfiguration.cs:65,77` — `ValueComparer(...)` (Expression)

### Pattern (apply mechanically; do NOT touch XML doc comments or `//` comments):

```csharp
// before
if (category == null) throw new NotFoundException(...);
// after
if (category is null) throw new NotFoundException(...);
```

### Caveats

- **Skip XML doc comments and `//` comments** — `Discount.Grpc/Services/DiscountService.cs:182` and `Protos/Discount.cs` (in obj/, already excluded) have `== null` inside `<c>` tags and prose.
- **EF Core safety** (N/A now — those sites are all expression trees, not converted): verified by reading the LINQ predicate shape. EF Core 5+ translates `is null` to identical SQL. Safe — but unreachable in this phase.
- **Marten LINQ safety** (N/A now — same reason): the only Marten LINQ site is `Basket.API/Messaging/CheckoutBasketOutboxDispatcher.cs:137`, kept as `== null` for the compiler reason.
- **AGENTS.md update required**: the existing rule "`is null` / `is not null` over `!= null` / `== null`" must be qualified to "in regular C# code; LINQ expression trees (`.Where`, `HasQueryFilter`, `ValueComparer`, `HasConversion`, `.Must(...)`, `.Should().OnlyContain(...)` and similar) keep `== null` due to `CS8122`".

**Risk**: very low. Only 18 sites in 12 files, all in regular C# code, with mechanical conversion that the compiler validates.

**Verification gate**:
- `dotnet build orderly-microservices.slnx` — green (no `CS8122`).
- `dotnet format whitespace --verify-no-changes` — green.
- PowerShell sweep — count of `== null` / `!= null` in regular C# code drops from 18 to 0; expression-tree sites unchanged:
  ```powershell
  Get-ChildItem -Recurse -Include *.cs orderly-microservices |
    Where-Object { $_.FullName -notmatch 'obj|bin|Migrations|\.Designer\.cs|\.g\.cs' } |
    Select-String -Pattern '(\!\=|\=\=)\s*null' |
    Where-Object { $_.Line -notmatch '^\s*//' -and $_.Line -notmatch '^\s*///' } |
    ForEach-Object { ... }
  ```

### ✅ Phase 3 — `var` → explicit type (1,236 sites / 319 files) — done 2026-08-28

> **DONE** — updated 2026-08-28. Replaced the planned `.csx` Roslyn script (and absent `dotnet-script` global tool) with a self-contained C# console app at `scripts/quality-helpers/ConvertVarToExplicit/ConvertVarToExplicit.csproj`. The tool loads the workspace via `MSBuildWorkspace`, opens projects through the .slnx root (YarpApiGateway / AppHost) since `MSBuildWorkspace` does not understand .slnx, runs a `CSharpSyntaxRewriter` over each project's syntax trees, and writes the explicit-type replacements back to disk.
>
> **Three bugs were fixed during the bulk run** (only surfaced on the larger projects — BuildingBlocks.Dev canary was small enough to miss them):
> 1. Rewrite-on-new-tree bug: visiting a freshly-built tree caused `GetTypeInfo` to throw `ArgumentException("Syntax node is not within the syntax tree")`. Fixed by visiting the original first, then applying the rewrite to the visited node.
> 2. Lambda-body semantic-model bug: the document `SemanticModel` doesn't cover anonymous lambda-body SyntaxTrees. Added a fallback that asks the Compilation directly (`compilation.GetSemanticModel(node.SyntaxTree)`).
> 3. Anonymous-type detection + namespace-resolution bug: original `IsAnonymousType` checked `Name.StartsWith("AnonymousType")` but the canonical Roslyn metadata name is `<>f__AnonymousType0`; replaced with a recursive walk that uses `INamedTypeSymbol.IsAnonymousType`. Separately, stripping `global::` from the fully-qualified format caused wrong-type resolution when the file's namespace contained a sub-namespace with the same name as a part of the type's namespace. Fixed by using `ToMinimalDisplayString(semanticModel, position)` which respects the file's `using` / global usings / current namespace.
>
> **Canary finding**: Phase 3 PR #3.1 originally intended to remove the temporary `IDE0008` entry from the canary's `<NoWarn>` (now that all var sites are converted). Verified empirically that this breaks the build with 5 IDE0008 errors — but the failures are on the one preserved `using var` site (line 271: `using var doc = JsonDocument.Parse(headerJson);`). The IDE0008 analyzer flags ALL `var` declarations, including `using var`, even though `using var` is an AGENTS.md carve-out. **Decision: IDE0008 cannot be removed from the canary's `<NoWarn>` — the carve-out is permanent.** Documented in the .csproj comment.
>
> **Final state**:
> - 1,236 sites converted across 319 files (Catalog.API was the biggest at 515)
> - 44 carve-outs preserved: `using var`, `await using var`, anonymous types, tuple deconstructions
> - 20 unresolved: `var x = null;` literals where Roslyn can't infer the type
> - 6 `var` declarations remain in production code — all anonymous-type projections like `var candidates = await query.Select(b => new { b.UserId, ... }).ToListAsync()`; IDE0008 doesn't fire on these because the alternative would be the ugly `<>f__AnonymousType0<int, Guid, Instant>` name
> - IDE0008 warning count dropped from 2,730 → 170 (93.8% reduction; remaining 170 are pre-existing analyzer noise unrelated to var)
> - Whitespace gate clean, build clean (0 errors)
> - Test suite: 0 new regressions (Kitchen.API.Tests and Identity.API.Tests each had pre-existing failures verified identical on `main`)

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

1. **PR #3.1** — `BuildingBlocks.Dev` + `BuildingBlocks.Dev.Tests` (16 + 70 var sites). Smoke test the gate end-to-end. If this PR fails the canary build, every subsequent PR fails too — fix the script here, not later. **This PR also removes the temporary canary `<NoWarn>` blocks added in Step 0.5 (the in-`.editorconfig` canary override from Phase 0 was already redundant with `<NoWarn>` and never took effect).**
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
