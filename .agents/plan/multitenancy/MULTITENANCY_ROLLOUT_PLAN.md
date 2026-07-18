# Multi-Tenancy — Implementation Plan

> Scope: finish wiring tenant isolation across every service in `orderly-microservices/`. Tenant == Restaurant, shared database per service + `RestaurantId` column (logical isolation, not physical). Two of six services (Discount, Basket) are already shipped; this plan covers the remaining four (Kitchen, Ordering, Catalog, Identity) plus the BuildingBlocks-side ergonomics, the migration story for entities that don't yet carry `RestaurantId`, and the cross-cutting guard rails.

---

## Status

> **Plan version**: `v1.0` (2026-07-18) — `1` increments after each phase completion; `2` is reserved for breaking restructures of the plan itself.
> **Current state**: ⏸ Not started

| Phase | Name | Status |
|:-----:|---|:-----:|
| 0 | BuildingBlocks ergonomics + Catalog migrations catch-up | ⏸ Pending |
| 1 | Kitchen pilot | 🔒 Blocked (by Phase 0) |
| 2 | Ordering denormalization | 🔒 Blocked (by Phase 1) |
| 3 | Catalog adoption | 🔒 Blocked (by Phase 2) |
| 4 | Test contract codification | 🔒 Blocked (by Phase 1) |
| 5 | Identity cleanup (int→Guid + null provider) | 🔒 Blocked (by Phase 2) |

> **Legend**: ✅ Done · 🚧 In progress · ⏸ Pending · 🔒 Blocked

> **Commit messages**: Conventional Commits (`feat:`, `docs:`, `chore:`, `test:`, `fix:`). Short subject, ≤50 chars, imperative mood, no trailing period.

> **Update rule**: **on every phase completion, the plan MUST be updated in the same pair of commits as the phase work (a code commit + a plan commit — see [How to use this plan](#how-to-use-this-plan)).** The plan is the source of truth for what was decided and what shipped; a phase that ships without a plan update is a phase that drifted.

---

## 0. Skill & documentation conventions

### 0.1 Skill mandate — `csharp-developer`
> **All implementation work on this plan MUST invoke the `csharp-developer` skill** (base directory `.claude/skills/csharp-developer`, invoked as `/csharp-developer` in Claude Code). The skill is the source of truth for C# 12+ / .NET 10 idiom, async patterns, EF Core / Marten usage, ASP.NET Core + Carter, MediatR CQRS, xUnit + FluentAssertions test scaffolding, and the project's "MUST DO / MUST NOT DO" guard rails (nullable enabled, primary constructors, async/await with `CancellationToken`, `Result<T>` for error paths, no blocking calls, DTO mapping for API responses).

Companion reference files (loaded on demand per the skill's table): `modern-csharp.md`, `aspnet-core.md`, `entity-framework.md`, `performance.md` (only if a phase lands a perf-sensitive hot path — none in this plan).

> **EF Core checkpoint:** after any code change that mutates the schema (Phase 0 Catalog migrations catch-up; Phase 1 Kitchen filter; Phase 2 Ordering denormalization; Phase 5 Identity type fix), the implementer runs `dotnet ef migrations add <Name>` per the project's `--startup-project` rule (Ordering: `--startup-project Ordering.API`; Catalog/Basket/Discount/Identity/Kitchen: see memory `ordering-ef-migration-startup-project.md` for dev-DB passwords + ports). Reviews the generated migration for unintended drops. For Phase 2's denormalization migration, the backfill `UPDATE` must be hand-authored in `migrationBuilder.Sql(...)` — EF cannot infer it.

The skill is *additional* to whatever other skills are relevant (e.g. `csharp-xunit` for test scaffolding; `dotnet-best-practices` for the project-wide guard rails). It is **not** a substitute for the plan; the plan wins where they disagree.

### 0.2 Code-quality guard rails

This plan **inherits the project-wide guard rails from `CATALOG_SERVICE_PLAN.md §0.3` verbatim**. Mirror-references drift silently; copy-into-context is verbose but drift-proof. The Catalog plan is the authoritative source; if Catalog §0.3 changes, this section changes in lockstep on the next multitenancy phase commit.

Multitenancy-specific overrides layered on top of the catalog-copied bullets:

- **`ICurrentRestaurantProvider` is the only allowed source of `Guid RestaurantId` in a request scope.** No controller, handler, or repository may re-read the JWT or the `UserRestaurant` table directly. The provider is the single source of truth. (Enforced by code review on every PR.)
- **Global query filters are mandatory for every `ITenantEntity`.** A repository that hand-writes a `Where(RestaurantId == ...)` clause is acceptable only if it ALSO calls `.IgnoreQueryFilters()` for a documented cross-tenant operation (e.g. Discount's expiry sweep). The combination is the only valid reason to bypass the filter.
- **`Guid.Empty` is a fail-secure sentinel.** When the provider can't resolve a tenant, it returns `Guid.Empty`; the filter then matches no rows. Never write `Guid.Empty == some.RestaurantId`-bypassing logic.
- **Bus consumers MUST use `provider.Attach(syntheticPrincipal)` before any DbContext call.** Synthetic principals are built with `ClaimsPrincipalBuilder.WithRestaurant(rid)` (per `DISCOUNT_SERVICE_PLAN.md §0.1 #10`). A consumer that forgets the attach reads `Guid.Empty` and silently writes nothing — the worst possible failure mode. Cover with a unit test per consumer.
- **No DbContext in a service that does not register `ICurrentRestaurantProvider`.** Phase 5 introduces the `NullCurrentRestaurantProvider` for Identity so the DI graph compiles uniformly; every other service uses `ClaimsRestaurantProvider` against `IHttpContextAccessor`.
- **Tests for every new filter**: a `GlobalTenantFilterTests` class per relational service. The pattern lives in `Discount.Tests` and `Basket.Tests` — copy-paste-modify. Phase 4 lifts the pattern into a shared contract.
- **No `FromSqlRaw` in tenant-scoped queries without explicit `WHERE RestaurantId = @rid` clause.** The EF filter does not apply to raw SQL; bypass is silent.

#### 0.2.1 Global usings (project-specific)

After Phase 0, every adopter service's `GlobalUsings.cs` gains:

```csharp
global using BuildingBlocks.Multitenancy;
global using BuildingBlocks.Behaviors;  // for TenantGuardBehavior in Phase 1+
```

The "2+ files" promotion rule from CATALOG_SERVICE_PLAN §0.3.12 applies.

---

## 1. Context

The codebase already signals that multi-tenancy was always intended, but the rollout stopped at two of six services:

- **BuildingBlocks primitives exist and are correct.** `BuildingBlocks/Multitenancy/` ships `ITenantEntity` (Guid `RestaurantId`), `ICurrentRestaurantProvider` + `ClaimsRestaurantProvider` (HTTP claim reader with `AsyncLocal<ClaimsPrincipal?>` Pattern 2 support for bus consumers), and `TenantQueryFilterExtensions.ApplyTenantFilter<T>(getRestaurantId)`. All four files are referenced by `DISCOUNT_SERVICE_PLAN.md` and `BASKET_SERVICE_PLAN.md` as the canonical primitives.
- **The JWT already carries a `restaurantId` claim.** `Services/Identity/Identity.API/Services/ClaimsTransformer.cs:47` stamps the user's `IsDefault` restaurant (or first) on every token. The accessor `BuildingBlocks/Authorization/JwtClaimExtensions.cs:13` reads it.
- **Most aggregates already have a `RestaurantId` column.** `Order.RestaurantId` (Ordering), `KitchenTicket.RestaurantId` (Kitchen), `Coupon.RestaurantId`, `RewardCode.RestaurantId`, `DiscountRule.RestaurantId` (Discount), every Catalog relational entity except `Brand`. The schema is already tenant-correct.
- **Two of six services are wired.** Discount (full EF filter + ITenantEntity on 3 entities) and Basket (Marten `MultiTenanted()` + per-tenant DBs + repo-layer guard) are the only complete adopters. Catalog, Ordering, Kitchen, Identity have no `HasQueryFilter` for tenant.
- **Caches and bus are already tenant-aware.** Catalog's `CacheKeys.Menu(rid)` etc. namespace by restaurant. Every integration event in `BuildingBlocks.Messaging/Events/` carries `RestaurantId` on the wire.
- **Two blocking drifts remain:**
  - `Identity/Models/UserRestaurant.cs:7` — `RestaurantId : int`. Everywhere else is `Guid`. Documented in `docs/architecture/db_relational_model.md §137-148`.
  - `Catalog.API/Data/Migrations/` is **empty**. Catalog's relational schema is bootstrapped only by Marten's `ApplyAllDatabaseChangesOnStartup()`. The filter cannot land until a real EF migration exists.

Reference plans: `DISCOUNT_SERVICE_PLAN.md` (Pattern A adopter; gold standard for the EF filter + ITenantEntity pattern), `BASKET_SERVICE_PLAN.md` (Marten-style adopter; gold standard for the defense-in-depth guard), `KITCHEN_SERVICE_PLAN.md` (Phase 1 pilot).

---

## 2. Goal

By the end of Phase 5:

1. A JWT carrying `restaurantId = A` and one carrying `restaurantId = B` cannot read each other's data in any service.
2. A bus consumer firing with a `RestaurantId`-only event payload (no HTTP context) reads/writes the right tenant's data via `provider.Attach(syntheticPrincipal)`.
3. A `Guid.Empty` provider (no claim, no scope) returns zero rows from every tenant-scoped query.
4. The `int→Guid` type drift in Identity is closed.
5. The plan-vs-code drift report (`docs/architecture/db_model_drift_report.md`) shows no tenant-scope gaps.

Concrete deliverables:

- `BuildingBlocks/Multitenancy/ApplyTenantFilters` extension that walks every `ITenantEntity` in the model and registers a global filter.
- `BuildingBlocks/Multitenancy/NullCurrentRestaurantProvider` for Identity's DI graph.
- `BuildingBlocks/Behaviors/TenantGuardBehavior` — MediatR pipeline behavior asserting `ITenantScopedRequest.RestaurantId == provider.RestaurantId`.
- `BuildingBlocks.Multitenancy.Tests/GlobalTenantFilterContract` — abstract test contract with 7 test methods; concrete services derive from it.
- Per-service adoption in Kitchen, Ordering, Catalog (`ITenantEntity` marker + `OnModelCreating` filter + `ICurrentRestaurantProvider` registration + per-service tests).
- Identity `int→Guid` schema migration with backfill.
- Empty Catalog `Data/Migrations/` populated with `InitialSchema`.

---

## 3. Out of scope

- **DB-per-tenant** (one database per restaurant per service). Revived only when the abort condition in §10.6 is met.
- **Cross-tenant data sharing or analytics rollups** (e.g. "total sales across all my restaurants for brand X"). Per-tenant filter intentionally blocks it; brand analytics is a separate BI problem.
- **Tenant onboarding flow.** A restaurant self-signup or admin-create flow is its own service plan. The `restaurantId` claim gets stamped regardless of how the restaurant came to exist.
- **Multi-region or data-residency routing.** Today's setup is single-region.
- **Re-keying the gateway rate limiter by tenant.** Today's per-user partition (`User.Identity?.Name ?? Host`) is adequate until a real abuse vector emerges.
- **`Brand`-level filter** alongside `Restaurant`. Catalog has `BrandId` on `Restaurant`; brand-as-tenant is a future layering (different scope from this plan).

---

## 4. Tech decisions

| # | Decision | Choice | Reason |
|:---|:---|:---|:---|
| 1 | Tenant granularity | **Restaurant** | Already settled at the JWT-issuance layer (`ClaimsTransformer.cs:47`). Reversing invalidates every consumer-side tenant-scope assumption. |
| 2 | Isolation strategy | **Shared DB per service + `RestaurantId` column** | DB-per-tenant costs ~weeks of work (connection-string factory, per-tenant migration tooling, connection-pool math) without buying anything the query-filter approach doesn't already give. |
| 3 | BuildingBlocks surface | **`ModelBuilder.ApplyTenantFilters(getRestaurantId)` extension** walking every `ITenantEntity` | Today every adopter writes its own `OnModelCreating` walk (Discount inlines at `DiscountContext.cs:92-101`). One-call extension removes ~30 lines of boilerplate per DbContext. |
| 4 | Defense-in-depth layer | **`TenantGuardBehavior<TRequest>` MediatR pipeline behavior** mirroring Basket's `BasketIdentityGuardBehavior` | A forgotten `Where(RestaurantId == ...)` is the canonical leak vector. The behavior catches it at the request boundary. Complements the global filter — does not replace it. |
| 5 | Migration of entities without `RestaurantId` | **Denormalized `RestaurantId` column on `OrderItem`, `OrderBill`, `Customer`, `OrderActivity`** | Query-time join on `Order` misses the global-filter benefit and leaves the table vulnerable to direct `DbSet<OrderItem>` queries. Denormalization + filter is the same pattern Discount uses. |
| 6 | Identity tenant model | **`NullCurrentRestaurantProvider` returning `Guid.Empty`; Identity repositories never implement `ITenantEntity`** | Identity *issues* tenant context, it does not *consume* it. Forcing tenant filters on Identity's user-table would break admin tooling. |
| 7 | `UserRestaurant.RestaurantId : int → Guid` drift | **Fix in Phase 5**: change column type, ship migration with explicit `ALTER COLUMN` on Postgres | Drift blocks every cross-service join that flows through Identity. Fixing once removes a class of future bug. |
| 8 | Test pattern | **Per-service `GlobalTenantFilterTests` integration suite** (xUnit + Testcontainers Postgres + FluentAssertions); one test per CRUD verb + bus scope + null provider | The filter is invisible; without tests, regressions slip in. Pattern is established in `Discount.Tests` + `Basket.Tests`; ship three more. |
| 9 | Gateway rate-limit re-keying | **Defer**; today's per-user partition is adequate | Don't ship infrastructure for a hypothetical load profile. |
| 10 | Cache keying | **No change**; Catalog's `CacheKeys.Menu(rid)` convention stays; new caches namespace by `restaurantId` | Convention is proven. No reason to change it. |
| 11 | Event payload shape | **No change**; every integration event already carries `RestaurantId`; consumers use Pattern 2 attach | Wire shape is settled. |
| 12 | Rollout ordering | **Phase 1 Kitchen pilot (smallest) → Phase 2 Ordering (biggest domain work) → Phase 3 Catalog (biggest surface, mechanical) → Phase 5 Identity (cleanup)** | Each phase proves the pattern for the next. |

---

## 5. Folder layout

The plan touches files across the four adopter services plus BuildingBlocks. No new top-level directories.

```
orderly-microservices/
├── BuildingBlocks/
│   ├── Multitenancy/
│   │   ├── ITenantEntity.cs                          (unchanged)
│   │   ├── ICurrentRestaurantProvider.cs             (unchanged)
│   │   ├── ClaimsRestaurantProvider.cs               (unchanged)
│   │   ├── TenantQueryFilterExtensions.cs            [Phase 0] add ApplyTenantFilters(getRestaurantId)
│   │   └── NullCurrentRestaurantProvider.cs          [Phase 5] new file
│   ├── Behaviors/
│   │   └── TenantGuardBehavior.cs                    [Phase 1] new file (template); copies in Phases 2/3
│   └── Multitenancy.Tests/                           [Phase 0 + 4] new project
│       ├── ApplyTenantFiltersTests.cs                [Phase 0]
│       ├── NullCurrentRestaurantProviderTests.cs     [Phase 5]
│       └── Integration/GlobalTenantFilterContract.cs [Phase 4] abstract test contract
├── Services/
│   ├── Catalog/Catalog.API/
│   │   ├── Data/Migrations/                          [Phase 0] populate InitialSchema
│   │   ├── Data/CatalogDbContext.cs                  [Phase 3] widen ctor + ApplyTenantFilters
│   │   ├── Models/*.cs                               [Phase 3] ~20 entities add ITenantEntity
│   │   ├── Behaviors/TenantGuardBehavior.cs          [Phase 3] copy from Phase 1
│   │   ├── Program.cs                                [Phase 0] register provider
│   │   └── Catalog.API.Tests/GlobalTenantFilterTests.cs [Phase 3]
│   ├── Ordering/
│   │   ├── Ordering.Domain/Models/{OrderItem,OrderBill,Customer,OrderActivity}.cs [Phase 2] add RestaurantId
│   │   ├── Ordering.Infrastructure/Data/ApplicationDBContext.cs  [Phase 2] ApplyTenantFilters
│   │   ├── Ordering.Infrastructure/Data/Migrations/  [Phase 2] AddRestaurantIdToOrderChildren + backfill
│   │   ├── Ordering.Application/*/EventHandlers/     [Phase 2] Pass Order.RestaurantId into constructors
│   │   ├── Ordering.API/Behaviors/TenantGuardBehavior.cs  [Phase 2]
│   │   ├── Ordering.API/Program.cs                   [Phase 2] register provider + wire guard
│   │   ├── Ordering.Infrastructure/Consumers/        [Phase 2] Pattern 2 attach per consumer
│   │   └── Ordering.{Domain,Infrastructure}.Tests/GlobalTenantFilterTests.cs [Phase 2]
│   ├── Kitchen/Kitchen.API/
│   │   ├── Domain/Aggregates/KitchenTicket/KitchenTicket.cs  [Phase 1] add ITenantEntity
│   │   ├── Domain/Aggregates/KitchenStation/KitchenStation.cs  [Phase 1] add ITenantEntity
│   │   ├── Infrastructure/Data/KitchenDbContext.cs    [Phase 1] widen ctor + ApplyTenantFilters
│   │   ├── Infrastructure/Data/Migrations/           [Phase 1] ApplyTenantFilter (empty body)
│   │   ├── Behaviors/TenantGuardBehavior.cs          [Phase 1]
│   │   ├── Program.cs                                [Phase 1] register provider + wire guard
│   │   └── Kitchen.API.Tests/GlobalTenantFilterTests.cs [Phase 1]
│   └── Identity/Identity.API/
│       ├── Models/UserRestaurant.cs                  [Phase 5] int → Guid
│       ├── Data/Migrations/                          [Phase 5] ConvertUserRestaurantRestaurantIdToGuid
│       └── Program.cs                                [Phase 5] register NullCurrentRestaurantProvider
└── docs/architecture/current-architecture.md         [every phase] Doc-update scope per §9
```

---

## 6. Specification

The contracts the implementer acts on. One subsection per group of related items.

### 6.1 BuildingBlocks extensions

- **`BuildingBlocks/Multitenancy/ApplyTenantFilters(getRestaurantId)` extension** — `ModelBuilder.ApplyTenantFilters(this ModelBuilder modelBuilder, Func<Guid> getRestaurantId)`. Walks `modelBuilder.Model.GetEntityTypes()` for any `ITenantEntity`. For each, registers `entity.HasQueryFilter(e => e.RestaurantId == getRestaurantId())`. Throws `InvalidOperationException` if an `ITenantEntity` already has a `HasQueryFilter` (adopter must use the single-entity `ApplyTenantFilter<T>` for composed filters, as Discount does for `!IsDeleted && RestaurantId == ...`). No-op on models with no `ITenantEntity`.
- **`BuildingBlocks/Multitenancy/NullCurrentRestaurantProvider`** — implementation of `ICurrentRestaurantProvider`. `RestaurantId` always returns `Guid.Empty`. `Attach(principal)` returns `EmptyScope.Instance` (a no-op `IDisposable`). Used by Identity.
- **`BuildingBlocks/Behaviors/TenantGuardBehavior<TRequest, TResponse>`** — MediatR pipeline behavior. When `TRequest : ITenantScopedRequest` and `scoped.RestaurantId is { } rid`, asserts `rid == tenant.RestaurantId`; mismatch throws `ForbiddenException("Cross-tenant access blocked by TenantGuardBehavior.")`. No-op for requests that don't implement `ITenantScopedRequest`.
- **`BuildingBlocks/Multitenancy/ITenantScopedRequest`** — marker interface with `Guid? RestaurantId { get; }`. DTOs that carry a tenant-aligned field opt in.

### 6.2 Per-service adoption contract

For every service (Kitchen, Ordering, Catalog) the implementer ships:

1. **Aggregate marker** — every entity that already has a `RestaurantId : Guid` column gains `: , ITenantEntity`. One line per entity.
2. **DbContext widening** — `DbContext` constructor adds `ICurrentRestaurantProvider restaurantProvider` parameter. `OnModelCreating` calls `modelBuilder.ApplyTenantFilters(() => _restaurantProvider.RestaurantId)` (or per-entity `ApplyTenantFilter<T>` where composition with an existing filter is needed).
3. **DI registration** — `services.AddHttpContextAccessor(); services.AddSingleton<ICurrentRestaurantProvider, ClaimsRestaurantProvider>();` in `Program.cs`.
4. **Behavior wire-up** — register `TenantGuardBehavior<,>` in the MediatR pipeline (after `LoggingBehavior`, before `ValidationBehavior` — mirrors Basket).
5. **Migration** — `dotnet ef migrations add ApplyTenantFilter --startup-project <Service>.API`. Empty body; documents the change in migrations history.

For Ordering (Phase 2), step 1 is preceded by step 1a: add `RestaurantId : Guid` (set in constructor from parent `Order.RestaurantId`) to `OrderItem`, `OrderBill`, `Customer`, `OrderActivity`. New migration `AddRestaurantIdToOrderChildren` with hand-authored backfill SQL.

### 6.3 Global query filter registration shape

```csharp
// Kitchen / Catalog / Ordering — single-entity walks (Discount's pattern, inline):
modelBuilder.Entity<KitchenTicket>().HasQueryFilter(t =>
    t.RestaurantId == _restaurantProvider.RestaurantId);

// Ordering — composed with existing !IsDeleted (Discount's pattern):
modelBuilder.Entity<Order>().HasQueryFilter(o =>
    !o.IsDeleted && o.RestaurantId == _restaurantProvider.RestaurantId);

// Catalog (Phase 3) — uniform walk via extension:
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    base.OnModelCreating(modelBuilder);
    // ... existing Catalog configurations ...
    modelBuilder.ApplyTenantFilters(() => _restaurantProvider.RestaurantId);
}
```

### 6.4 Bus consumer Pattern 2 (synthetic claims) contract

Every `IConsumer<TIntegrationEvent>` that touches a relational DbContext must:

```csharp
public class OrderCompletedConsumer(
    ApplicationDBContext db,
    ICurrentRestaurantProvider tenant) : IConsumer<OrderCompletedIntegrationEvent>
{
    public async Task Consume(ConsumeContext<OrderCompletedIntegrationEvent> ctx)
    {
        var synthetic = new ClaimsPrincipalBuilder()
            .WithRestaurant(ctx.Message.RestaurantId)
            .WithPermission("order:read")
            .Build();
        using var _ = tenant.Attach(synthetic);
        // ...db work, tenant filter resolves to ctx.Message.RestaurantId
    }
}
```

`ClaimsPrincipalBuilder` already exists in `BuildingBlocks/Authorization/ClaimsPrincipalBuilder.cs:46`. Failure to attach reads `Guid.Empty` and silently writes nothing — covered by a unit test per consumer.

### 6.5 Migration shape per service

| Phase | Service | Migration name | Body |
|:---|:---|:---|:---|
| 0 | Catalog | `InitialSchema` | Auto-generated; review for missing indexes (`MenuItems.RestaurantId` critical). |
| 1 | Kitchen | `ApplyTenantFilter` | Empty (filter is runtime). |
| 2 | Ordering | `AddRestaurantIdToOrderChildren` | Hand-authored backfill `UPDATE` joining `Orders`, then `NOT NULL` constraint. |
| 3 | Catalog | `ApplyTenantFilter` | Empty. |
| 5 | Identity | `ConvertUserRestaurantRestaurantIdToGuid` | Add temp `Guid` column; backfill from `int`; drop `int`; rename; recreate unique indexes. |

Per-project dev-DB passwords + ports: see memory `ordering-ef-migration-startup-project.md`.

### 6.6 Test contract

Every relational service ships a `GlobalTenantFilterTests` class with at minimum:

| # | Test | Asserts |
|:---|:---|:---|
| 1 | `Create_WithTenantA_RowIsReadableOnlyByTenantA` | Tenant A creates a row; Tenant A can read it; Tenant B cannot. |
| 2 | `GetById_FromTenantB_TenantAId_ReturnsNull` | Filter hides cross-tenant rows from `Find`/`FirstOrDefault`. |
| 3 | `List_FromTenantB_DoesNotIncludeTenantARows` | `ToList()` returns only caller's tenant's rows. |
| 4 | `Update_FromTenantB_TenantAId_AffectsZeroRows` | Cross-tenant update is a no-op. |
| 5 | `Delete_FromTenantB_TenantAId_AffectsZeroRows` | Cross-tenant delete is a no-op. |
| 6 | `BusConsumer_WithoutHttpContext_AttachScope_ReadsPayloadTenant` | Pattern 2 synthetic-claim path. |
| 7 | `NoTenantScope_ReturnsZeroRows` | `Guid.Empty` provider → filter matches nothing. |

Discount already has tests 1-5 in `Discount.Tests/GlobalTenantFilterTests`. Basket has tests 1-3 in `Basket.Tests/BasketIdentityGuardBehaviorTests`. Phase 4 lifts this into `BuildingBlocks.Multitenancy.Tests.Integration.GlobalTenantFilterContract` (abstract test contract with 7 `protected abstract` fixture hooks).

---

## 7. Cross-service integration

This plan is cross-service by definition; integration points must be enumerated.

### 7.1 Integration events (already tenant-aware on the wire)

| Event | Carries `RestaurantId`? | Consumer-side attach needed? |
|---|:---:|:---:|
| `BasketCheckoutEvent` (`BuildingBlocks.Messaging/Events/BasketCheckoutEvent.cs:6`) | ✅ | ✅ in Ordering consumer |
| `OrderCreatedIntegrationEvent` (`.../OrderCreatedIntegrationEvent.cs:12`) | ✅ | ✅ in Catalog consumer |
| `OrderCompletedIntegrationEvent` (`.../OrderCompletedIntegrationEvent.cs:21`) | ✅ | ✅ in Catalog consumer (analytics) |
| `FeedbackSubmittedIntegrationEvent` (`.../Catalog/FeedbackSubmittedIntegrationEvent.cs:16`) | ✅ | ✅ in Discount consumer |
| `MenuItemChangedIntegrationEvent` (`.../MenuItemChangedIntegrationEvent.cs`) | ✅ | ✅ in Discount consumer |
| `IngredientAvailabilityChangedIntegrationEvent` | ✅ | ✅ in Ordering consumer (if subscribed) |

Wire shape is settled — no event payload change in this plan. Consumer-side work is the Pattern 2 attach (per §6.4).

### 7.2 Gateway (`ApiGateway/YarpApiGateway/`)

Out of scope per Tech Decision #9. Today's behavior:

- Path-only routing; no JWT validation, no claim pass-through.
- Rate limiter partitions by `User.Identity?.Name ?? Host` (per-user when authenticated; per-host when anonymous).

A future tenant-aware rate-limit is a 5-line Yarp `Transform` that injects `restaurantId` into the partition key. **Not in this plan.**

### 7.3 Cache keying convention

Catalog already namespaces by `restaurantId` (`CacheKeys.Menu(rid)`, `CacheKeys.Ingredients(rid)`). Code-review checklist: any new `IDistributedCache` key MUST contain `{restaurantId}`. Enforced by Tech Decision #10.

### 7.4 Marten documents (Catalog)

Catalog documents (`OrderSnapshot`, `OrderModificationLog`, `OrderItemPriceAudit`, `NotificationLog`, `User`) are **out of the relational filter** (Phase 3 §5). Marten queries that read these documents MUST add `.Where(d => d.RestaurantId == restaurantId)` if the document carries the field. Audited in Phase 3 code review.

---

## 8. Security guardrails

> [!CAUTION]
> **A forgotten `Where(RestaurantId == ...)` is the canonical cross-tenant data leak vector.** Every guardrail in this section defends against it. A leak is treated as a P0 incident: revoke any long-lived caches, audit access logs, rotate any credentials the leaked data could expose.

| Risk | Mitigation |
|---|---|
| Hand-written query forgets `Where(RestaurantId == ...)` | EF `HasQueryFilter` makes the clause invisible to the developer — they cannot accidentally omit it. |
| `FromSqlRaw` bypasses EF filter | §0.2 guard rail: no `FromSqlRaw` in tenant-scoped queries without explicit `WHERE RestaurantId = @rid` clause. |
| Consumer forgets `provider.Attach(syntheticPrincipal)` | §0.2 guard rail: every consumer covered by a unit test. Failure mode is "no rows written" (fail-secure), not "wrong rows written" — but the silent failure is still a bug. |
| `Identity` accidentally gains a `HasQueryFilter` | §0.2 + Phase 5 §6.1: `NullCurrentRestaurantProvider` makes Identity's no-filter state explicit and named; `Identity.API.Tests` covers the contract. |
| `UserRestaurant.RestaurantId : int → Guid` migration corrupts prod data | Phase 5 §6.5: hand-authored migration with backfill; pre-migration orphan check on dev; prod-data validation before merge. |
| Tenant A obtains a JWT for tenant B | Identity-side authorization concern, **out of this plan**; covered by `Identity.API/Services/ClaimsTransformer.cs` and the permission-per-claim shape. |
| Bus payload spoofing (attacker publishes a fake `OrderCompletedIntegrationEvent` with arbitrary `RestaurantId`) | MassTransit transport security + signing — **out of this plan**; covered by the bus-infrastructure plan, not by tenant isolation. |

---

## 9. Development Phases

### Phase overview

| Phase | Name | Tool groups delivered | Goal |
|:---:|---|---|---|
| **0** | BuildingBlocks ergonomics + Catalog migrations catch-up | `ApplyTenantFilters` extension; Catalog `InitialSchema` migration | One-call filter adoption across services; Catalog has a real EF migration to live in. |
| **1** | Kitchen pilot | `KitchenTicket`, `KitchenStation` adopt `ITenantEntity`; `KitchenDbContext` filter; `TenantGuardBehavior` template; `Kitchen.API.Tests/GlobalTenantFilterTests` | Prove the pattern end-to-end on the smallest adopter (2 aggregates); unblock confident rollout to Ordering and Catalog. |
| **2** | Ordering denormalization | `OrderItem`, `OrderBill`, `Customer`, `OrderActivity` gain `RestaurantId`; `ApplicationDBContext` filter; backfill migration; per-consumer Pattern 2 attach; Ordering `GlobalTenantFilterTests` | Close the biggest domain gap; ship the denormalization pattern. |
| **3** | Catalog adoption | ~20 relational entities adopt `ITenantEntity`; `CatalogDbContext` filter; `Catalog.API.Tests/GlobalTenantFilterTests`; Marten doc audit | Cover the biggest surface; mechanical adoption. |
| **4** | Test contract codification | `BuildingBlocks.Multitenancy.Tests/Integration/GlobalTenantFilterContract` abstract test class | Lift the per-service test pattern into a shared contract. |
| **5** | Identity cleanup | `UserRestaurant.RestaurantId : int → Guid` migration; `NullCurrentRestaurantProvider`; `BuildingBlocks.Multitenancy.Tests/NullCurrentRestaurantProviderTests` | Close the type drift; make Identity's DI graph compile uniformly. |

### Phase 0 — BuildingBlocks ergonomics + Catalog migrations catch-up

**Goal**: ship the one-call `ApplyTenantFilters(getRestaurantId)` extension so adopters don't repeat Discount's inline walk. Catch up Catalog's empty `Migrations/` folder so the global filter has a migration to live in.

**Status**: ⏸ Pending

**Deliverables**:
- [ ] `BuildingBlocks/Multitenancy/TenantQueryFilterExtensions.ApplyTenantFilters(getRestaurantId)` extension shipped
- [ ] `BuildingBlocks/Multitenancy.Tests` project created (xUnit + FluentAssertions)
- [ ] `ApplyTenantFiltersTests`: `OnModelWithMixedEntities_AddsFilterToITenantEntityOnly`
- [ ] `ApplyTenantFiltersTests`: `OnEntityThatAlreadyHasFilter_ThrowsInvalidOperationException`
- [ ] `ApplyTenantFiltersTests`: `OnEmptyModel_DoesNotThrow`
- [ ] `Catalog.API/Data/Migrations/InitialSchema` migration generated + reviewed + applied to fresh `catalogdb`
- [ ] `Catalog.API/Program.cs` registers `IHttpContextAccessor` + `ICurrentRestaurantProvider`
- [ ] `docs/architecture/current-architecture.md` §2 + §4.2 updated per Status rule

**Exit criteria**: `dotnet ef migrations list --startup-project Catalog.API` shows `InitialSchema`; `dotnet test BuildingBlocks.Multitenancy.Tests` green; solution-wide `dotnet test` green.

### Phase 1 — Kitchen pilot

**Goal**: prove the pattern end-to-end on the smallest adopter (2 aggregates).

**Status**: ⏸ Pending (blocked by Phase 0)

**Deliverables**:
- [ ] `KitchenTicket` (`Kitchen.API/Domain/Aggregates/KitchenTicket/KitchenTicket.cs:36`) gains `: ITenantEntity`
- [ ] `KitchenStation` gains `: ITenantEntity`
- [ ] `KitchenDbContext` constructor widened to accept `ICurrentRestaurantProvider restaurantProvider`
- [ ] `KitchenDbContext.OnModelCreating` calls `ApplyTenantFilters(() => restaurantProvider.RestaurantId)`
- [ ] `Kitchen.API/Program.cs` registers `IHttpContextAccessor` + `ICurrentRestaurantProvider`
- [ ] `Kitchen.API/Behaviors/TenantGuardBehavior.cs` created (template)
- [ ] `Kitchen.API/Program.cs` MediatR pipeline wires `TenantGuardBehavior`
- [ ] `dotnet ef migrations add ApplyTenantFilter --startup-project Kitchen.API` — empty body, applied
- [ ] `Kitchen.API.Tests/GlobalTenantFilterTests` ships 7 tests (per §6.6)
- [ ] `docs/architecture/current-architecture.md` §4.5 + §6 + §9 updated

**Exit criteria**: manual smoke (log in as user of restaurant A → GET kitchen queue → only A's tickets; log in as user of restaurant B → different result set); `dotnet test Kitchen.API.Tests` green.

### Phase 2 — Ordering denormalization

**Goal**: add `RestaurantId` to `OrderItem`, `OrderBill`, `Customer`, `OrderActivity`; apply the global filter to all ordering entities; ship the backfill migration.

**Status**: ⏸ Pending (blocked by Phase 1)

**Deliverables**:
- [ ] `OrderItem`, `OrderBill`, `Customer`, `OrderActivity` gain `Guid RestaurantId { get; private set; }`
- [ ] Constructor pattern: each child entity's constructor takes the parent `Order` (or its `RestaurantId`) so `RestaurantId` is wired automatically
- [ ] `Ordering.Infrastructure/Data/Configurations/` registers `RestaurantId` column + index per entity
- [ ] `dotnet ef migrations add AddRestaurantIdToOrderChildren --startup-project Ordering.API` — hand-authored backfill in `Up()`:
  ```sql
  UPDATE oi SET oi.RestaurantId = o.RestaurantId
  FROM OrderItems oi
  INNER JOIN Orders o ON oi.OrderId = o.Id
  WHERE oi.RestaurantId = '00000000-0000-0000-0000-000000000000';
  -- repeat for OrderBills, Customers, OrderActivities
  -- then add NOT NULL constraint via migrationBuilder.Sql
  ```
- [ ] Pre-migration orphan-check script run against dev `orderdb` (returns 0 orphans)
- [ ] `ApplicationDBContext.OnModelCreating` calls `ApplyTenantFilters(() => _restaurantProvider.RestaurantId)` (or per-entity `ApplyTenantFilter<T>` for entities with existing `!IsDeleted`)
- [ ] `Ordering.API/Program.cs` registers provider + wires guard
- [ ] Every `IConsumer<TIntegrationEvent>` in `Ordering.Infrastructure/Consumers/` wraps the DbContext call in `using var _ = tenant.Attach(syntheticPrincipal)`
- [ ] `Ordering.Domain.Tests` + `Ordering.Infrastructure.Tests` ship `GlobalTenantFilterTests` (7 tests)
- [ ] `Ordering.Infrastructure.Tests/GlobalTenantFilterTests` adds 1 extra: `OrderItem_CreateWithoutParentRestaurantId_Throws`
- [ ] `docs/architecture/current-architecture.md` §4.1 + §6 + §11 updated; backfill SQL documented in §11

**Exit criteria**: migration applies cleanly to fresh DB and to dev DB (manual verify on `orderdb`); all existing ordering tests green (no regression); new `GlobalTenantFilterTests` green; existing `OrderItem` rows in dev DB all have non-empty `RestaurantId`.

### Phase 3 — Catalog adoption

**Goal**: apply the filter to every Catalog relational entity that has `RestaurantId`. ~20 entities, all already have the column.

**Status**: ⏸ Pending (blocked by Phase 2)

**Deliverables**:
- [ ] Each of `MenuItem`, `MenuCategory`, `MenuSubCategory`, `MenuItemVariation`, `ComboItem`, `Table`, `MergedTable`, `Ingredient`, `MenuItemIngredient`, `IngredientAlternative`, `PriceHistory`, `MenuItemAnalytics`, `OrderTimingAnalytics`, `Reservation`, `WalkInQueue`, `CustomerFeedback`, `BulkOrderUpload`, plus any menu-item variants gains `: ITenantEntity`
- [ ] `CatalogDbContext` constructor widened to accept `ICurrentRestaurantProvider restaurantProvider`
- [ ] `CatalogDbContext.OnModelCreating` calls `modelBuilder.ApplyTenantFilters(...)` (uniform walk) OR `ApplyTenantFilter<T>` per entity for the 4 menu entities that need `!IsDeleted` composition
- [ ] `Catalog.API/Behaviors/TenantGuardBehavior.cs` created (copy from Phase 1)
- [ ] `Catalog.API/Program.cs` wires `TenantGuardBehavior`
- [ ] `dotnet ef migrations add ApplyTenantFilter --startup-project Catalog.API` — empty body, applied
- [ ] Marten documents audit: every `IMartenQueryable<T>` query in `Catalog.API` that reads a document with `RestaurantId` carries an explicit `.Where(d => d.RestaurantId == restaurantId)` clause; deviations listed in implementation notes
- [ ] `Catalog.API.Tests/GlobalTenantFilterTests` ships 7 tests
- [ ] `docs/architecture/current-architecture.md` §4.2 + §6 + §9 updated

**Exit criteria**: `dotnet ef database update --startup-project Catalog.API` clean; all existing catalog tests green; `GlobalTenantFilterTests` green; manual smoke (log in as restaurant A → GET menu → only A's items).

### Phase 4 — Test contract codification

**Goal**: lift the `GlobalTenantFilterTests` pattern into a `BuildingBlocks.Multitenancy.Tests` helper so future services get it for free.

**Status**: ⏸ Pending (blocked by Phase 1)

**Deliverables**:
- [ ] `BuildingBlocks.Multitenancy.Tests/Integration/GlobalTenantFilterContract.cs` abstract test class with 7 `protected abstract` fixture hooks
- [ ] Each adopter (Kitchen, Ordering, Catalog) refactored to derive from the contract (test count unchanged, ~50% less code)
- [ ] `dotnet test BuildingBlocks.Multitenancy.Tests` green

**Exit criteria**: test class hierarchy refactored; no test count delta; `dotnet test` green across solution.

### Phase 5 — Identity cleanup

**Goal**: fix the `UserRestaurant.RestaurantId : int → Guid` drift; register `NullCurrentRestaurantProvider` so DI compiles uniformly across all six services.

**Status**: ⏸ Pending (blocked by Phase 2)

**Deliverables**:
- [ ] `Services/Identity/Identity.API/Models/UserRestaurant.cs:7` — `int RestaurantId` → `Guid RestaurantId`
- [ ] `BuildingBlocks/Multitenancy/NullCurrentRestaurantProvider.cs` created (returns `Guid.Empty`; `Attach` is no-op)
- [ ] `Services/Identity/Identity.API/Program.cs` registers `NullCurrentRestaurantProvider`
- [ ] `dotnet ef migrations add ConvertUserRestaurantRestaurantIdToGuid --startup-project Identity.API` — hand-authored:
  ```sql
  ALTER TABLE "UserRestaurants" ADD COLUMN "RestaurantId_Guid" uuid;
  -- backfill: sequential Guid.NewGuid() per row in dev; mapping script in prod
  UPDATE "UserRestaurants" SET "RestaurantId_Guid" = gen_random_uuid();
  ALTER TABLE "UserRestaurants" DROP CONSTRAINT "PK_UserRestaurants";  -- if composite
  ALTER TABLE "UserRestaurants" DROP COLUMN "RestaurantId";
  ALTER TABLE "UserRestaurants" RENAME COLUMN "RestaurantId_Guid" TO "RestaurantId";
  -- recreate unique indexes that referenced the old column
  ```
- [ ] Pre-migration data validation script run against dev `identitydb` (counts match; foreign-key references intact)
- [ ] `BuildingBlocks.Multitenancy.Tests/NullCurrentRestaurantProviderTests` shipped (2 tests per §10.7)
- [ ] `Identity.API.Tests` green; grep proves `UserRestaurant.RestaurantId : Guid` everywhere in the codebase
- [ ] `docs/architecture/current-architecture.md` §4.6 + §11 updated; migration window documented

**Exit criteria**: migration applies to dev `identitydb`; all Identity tests green; `dotnet ef migrations list --startup-project Identity.API` shows the conversion migration; grep proves zero remaining `int RestaurantId` references in `Identity.API/`.

---

## 10. Technical considerations

> Surfaced from the multi-tenant readiness assessment on 2026-07-18. Each item points at a concrete risk and (where useful) to the relevant reference doc. **Phase 0 adoption:** items marked `[P0 ✅]` are adopted before any feature code lands; items without that marker remain pending for the phase that introduces the corresponding code.

### 10.1 Cross-cutting

> **Phase 0 adoption (2026-07-18):** the cross-cutting items below are *part of* Phase 0 (BuildingBlocks ergonomics), not a separate phase. They ship in the same PR as the `ApplyTenantFilters` extension.

- **Single source of truth for `RestaurantId`** — `[P0 ✅]` `ICurrentRestaurantProvider` is the only allowed source. No controller/handler/repository re-reads the JWT or `UserRestaurant` table. Enforced by §0.2 code review.
- **Defense-in-depth at the MediatR pipeline** — `[P1 ✅]` `TenantGuardBehavior` catches hand-rolled query mistakes before they hit the DB. Complements (does not replace) the global filter.
- **`Guid.Empty` fail-secure default** — `[P0 ✅]` the provider returns `Guid.Empty` when no tenant can be resolved; the filter then matches no rows. The `NullCurrentRestaurantProvider` makes the failure mode explicit and named for Identity.
- **Marten documents (Catalog) opt out of the relational filter** — `[P3 ✅]` Marten docs are document-store entities; the relational filter does not apply. Convention + code review catches missing `.Where(d => d.RestaurantId == ...)` clauses.
- **Cache keying convention** — `[P0 ✅]` every new `IDistributedCache` key MUST contain `{restaurantId}`. Enforced by §0.2 + Tech Decision #10.
- **Gateway awareness deferred** — `[P0 ✅]` today's per-user partition is adequate; Tech Decision #9 records when to revisit.
- **DB-per-tenant as a future premium tier** — `[P0 ✅]` §10.6 documents the abort condition for reviving DB-per-tenant.

### 10.2 Phase 0 — BuildingBlocks + Catalog catch-up

- **[P0 ✅]** `ApplyTenantFilters` extension — single-call adoption for adopters with no existing filter composition needs.
- **[P0 ✅]** Catalog `InitialSchema` migration — populates the empty folder; first review surfaces missing indexes.
- **[P0 ✅]** Catalog provider registration — establishes the DI pattern for Phases 1-3.
- **[P0 ⚠]** `ApplyTenantFilters` does **not** compose with existing filters — adopters with `!IsDeleted` (Catalog's menu entities, Discount, Ordering) must use the single-entity `ApplyTenantFilter<T>` instead. Documented in §6.1; future improvement could add a composition overload.

### 10.3 Phase 1 — Kitchen pilot

- **[P1 ✅]** Two-aggregate adoption proves the pattern is mechanical, not architectural.
- **[P1 ⚠]** Kitchen's existing hand-written `Where(RestaurantId == ...)` clauses in repositories stay as defense-in-depth but are now redundant. Documented in the commit message; removal deferred to a follow-up to keep the pilot PR small.

### 10.4 Phase 2 — Ordering denormalization

- **[P2 ✅]** Constructor pattern `(parent Order, ...)` for child entities wires `RestaurantId` automatically. Static-analyzer-friendly.
- **[P2 ⚠]** Backfill migration is hand-authored SQL — EF cannot infer the join. The orphan-check script (`SELECT COUNT(*) FROM OrderItems LEFT JOIN Orders ... WHERE Orders.Id IS NULL`) is a prerequisite. Documented in §9 Phase 2.
- **[P2 ⚠]** Existing ordering tests that build `OrderItem` etc. without an `Order` parent will break. Updated as part of Phase 2; expected and tracked in the implementation notes.

### 10.5 Phase 3 — Catalog adoption

- **[P3 ✅]** Marten documents audit (§9 Phase 3 step) catches every query that needs an explicit `.Where(d => d.RestaurantId == ...)`. Out-of-pattern queries get a finding in the implementation notes.
- **[P3 ⚠]** `!IsDeleted` composition in Catalog means 4 menu entities use the single-entity `ApplyTenantFilter<T>` overload, not the uniform walk. Small inconsistency; documented.

### 10.6 Phase 4 — Test contract

- **[P4 ✅]** Abstract test class with 7 fixture hooks covers the test matrix once. Future services get the contract for free.

### 10.7 Phase 5 — Identity cleanup

- **[P5 ✅]** `NullCurrentRestaurantProvider` makes the Identity "no-tenant-filter" state explicit and named; reduces the risk of a future contributor adding a tenant filter to Identity by mistake.
- **[P5 ⚠]** `UserRestaurant.RestaurantId : int → Guid` migration is a schema break. The dev-data backfill uses sequential `Guid.NewGuid()` per row; prod data needs a manual mapping script (e.g. map restaurant `int` IDs to their `Guid` counterpart in the Catalog `Restaurants` table). Documented in §9 Phase 5.
- **[P5 ✅]** `BuildingBlocks.Multitenancy.Tests/NullCurrentRestaurantProviderTests`:
  - `RestaurantId_IsEmpty`
  - `Attach_DoesNotThrow`

### 10.8 DB-per-tenant abort condition

Revisit DB-per-tenant as a strategy **only** when at least one of the following holds:

1. **Compliance requires physical data isolation** — SOC2 / PCI-DSS / GDPR data-residency guarantees that a shared DB cannot provide. Simplest response is a "premium tier" DB-per-tenant for the demanding customer, not a wholesale flip.
2. **Tenant count exceeds 1000 with per-tenant connection-pool pressure** — `6N` connection math starts to bite Postgres `max_connections = 100`. Read replicas and connection pooling may be a cheaper fix.
3. **A specific tenant's data volume dominates shared-DB index size** — e.g. a chain with 10⁶ menu items. Tenant partitioning (logical) or a tenant-specific table move (physical) is more surgical than DB-per-tenant across the board.

If the abort condition is met, the work is bounded (~2 weeks):

1. New `IConnectionStringProvider` in BuildingBlocks with one impl per DB engine.
2. Replace every `AddDbContext<T>(sp => sp.UseXxx(GetConnectionString(...)))` with `AddDbContext<T>((sp, opt) => opt.UseXxx(sp.GetRequiredService<IConnectionStringProvider>().Resolve(sp.GetRequiredService<ICurrentRestaurantProvider>().RestaurantId)))`.
3. Per-tenant migration runner hosted service (or `Database.BeginDatabaseMigration` on first sight of a new tenant).
4. `Marten.CreateDatabasesForTenants` (already in Basket) lifted to BuildingBlocks as a reference pattern for the relational services.

**Not in this plan.**

### 10.9 Effort estimate

| Phase | Effort | Risk |
|---|---|---|
| Phase 0 — BuildingBlocks + Catalog catch-up | 0.5 day | Low |
| Phase 1 — Kitchen pilot | 1 day | Low |
| Phase 2 — Ordering denormalization | 2-3 days | Medium (backfill migration) |
| Phase 3 — Catalog adoption | 1.5 days | Low |
| Phase 4 — Test contract codification | 0.5 day | Low |
| Phase 5 — Identity cleanup | 1 day | Medium (schema break + backfill) |
| **Total** | **6-7 days** | **Medium** |

---

## How to use this plan

1. **Find the current phase** in the Status table above. Update its row to 🚧 In progress on the first commit of the phase.
2. **For each phase**, copy the "Phase N" subsection before starting work. After completion, append a new "Phase N implementation notes (DATE)" section using the template below.
3. **Commit messages** convention is in the Status section. The whole plan is the source of truth for what was decided — keep it current.
4. **Drift between the plan and the code is the bug class plans exist to prevent.** When implementation reveals the plan was wrong (schema different than expected, API behaves differently), update the plan *and* the code in the same PR.

### The phase-completion workflow

> **Every phase completion is two commits, not one.**

1. **Code commit** — the work itself (`feat: ...`). Do NOT touch the plan in this commit.
2. **Plan commit** — the plan update only (`docs: mark Phase N complete in multitenancy-rollout-plan`):
   - Bump `Plan version` from `v1.N-1` → `v1.N` in the Status section.
   - Mark the phase's `[ ]` → `[x]` on deliverables; update the Status table row.
   - Append a new `### Phase N implementation notes (DATE)` section below.
   - Update §10's "Phase N adoption" subnote to reflect what was actually adopted vs deferred.
   - Add a Changelog entry at the bottom.
   - **If you skip the plan commit, the phase is not done** — even if the code shipped. The next person to read the plan will not know what state it's in.

> Two commits keeps the diff reviewable: the code commit is just code, the plan commit is just documentation. Mixing them makes both harder to review and easier to forget.

### Phase implementation notes template

> Append a new "implementation notes" section after every phase is finished. The structure stays constant so readers can find the same information in every phase's notes.

**§6.X items — adopted in Phase N.**
- {{ITEM}} — `[{{STATUS — ✅ adopted, ⚠ deferred, ❌ rejected}}]` {{RESOLUTION_NOTE}}.

**Bugs found + fixed during implementation.**
- {{BUG_AND_FIX — one line per bug, named with the symptom not the root cause.}}.

**Deferred to a Phase N follow-up ({{SCOPE}}).**
- {{DEFERRED_ITEM — link to the follow-up doc / TODO file if it lives elsewhere.}}.

**Phase N verification ({{WITHOUT/WITH}} {{DEPENDENCY}}).**
- {{VERIFICATION_STEP — the command + expected output.}}.

**Files added.** {{LIST}}. **Files modified:** {{LIST}}.

### Plan versioning

Plans follow `vMAJOR.MINOR` semantics. The version lives in the Status section as the first line so it is the first thing a reader sees.

| Bump | When |
|---|---|
| **Minor** (`v1.0` → `v1.1`) | After each phase completion. Always paired with a Changelog entry. |
| **Major** (`v1.x` → `v2.0`) | When the plan itself is restructured: phase boundaries change, new phases added, or the goal/scope shifts significantly. Reflects that readers who knew the old plan should re-read. |
| **No bump for typos** | Fixing a typo or wording error doesn't need a version bump. The Changelog is for *meaningful* changes, not every commit. |

---

## Changelog

### v1.0 (2026-07-18) — initial draft
- Created plan with 6 phases (0-5).
- Sections 0–10 drafted; §10 cross-cutting + per-phase adoption notes populated.
- Restructured from a Discount-style plan to match `.agents/plan/_template.md` conventions (Status section early, Tech Decisions table, Folder Layout, Specification subsection, Phases with Goal/Status/Deliverables/Exit criteria, Technical Considerations, Changelog).
- 12 locked decisions; estimated 6-7 days total effort; Phase 2 carries the bulk of risk.
