# Ordering — Manager Orders Live View (Backend) — Implementation Plan

> Scope: give the Ordering service everything the manager's orders workspace needs. That means a tenant-safe query surface, a lightweight live projection stamped with server time, a stats endpoint, a catch-up (`since`) feed, and a real-time `OrdersHub` fed by a post-commit notifier. Together they let the web client render a live, server-clock-synced order list with side panels. Consumed by [`OrderlyWeb/.agents/plans/manager-orders-live-view.md`](../../../../OrderlyWeb/.agents/plans/manager-orders-live-view.md).

---

## Status

> **Plan version**: `v2.5` (2026-10-24) — the minor number increments after each phase completion; the major number is reserved for breaking restructures of the plan itself.
> **Current state**: ✅ Completed (Phases 0–4 done)

| Phase | Name | Status |
|:-----:|---|:-----:|
| 0 | Hotfix: tenant query filter + docs correction | ✅ Done (2026-10-24) |
| 1 | Query surface: `/orders` filters fix, `/orders/live`, `/orders/stats`, indexes | ✅ Done (2026-10-24) |
| 2 | Real-time: post-commit notifier, `OrdersHub`, `since` catch-up | ✅ Done (2026-10-24) |
| 3 | Gateway, flags, observability | ✅ Done (2026-10-24) |
| 4 | Contract tests + E2E | ✅ Done (2026-10-24) |

> **Legend**: ✅ Done · 🚧 In progress · ⏸ Pending · 🔒 Blocked

> **Commit messages**: Conventional Commits (`feat:`, `docs:`, `chore:`, `test:`, `fix:`). Short subject, ≤50 chars, imperative mood, no trailing period. No AI attribution.

> **Update rule**: **on every phase completion, the plan MUST be updated in the same commit as the phase work.** The plan is the source of truth for what was decided and what shipped; a phase that ships without a plan update is a phase that drifted.

---

## 0. Skill & documentation conventions

### 0.1 Skill mandate — `csharp-developer` + `add-ordering-feature`
> **All implementation work on this plan MUST follow the `csharp-developer` skill (C# 14 / .NET 10, NodaTime, MediatR CQRS, Carter) and the `add-ordering-feature` skill (Domain → Application → Infrastructure → API layering).** Phase 2's integration-event consumption (restaurant time zone) follows `add-integration-event`.

### 0.2 Code-quality guard rails
- **Quality Gate script**: The `phase-guard.ps1` script MUST be executed at the end of every phase with the `-Quick` parameter to verify the build, tests, nullability, Dockerfiles, and dependencies:
  `pwsh ./scripts/phase-guard.ps1 -PhaseName "<Phase Name>" -Quick`
- **NodaTime only**: `Instant` / `LocalDate` / `DateTimeZone`; never `DateTime` / `DateTimeOffset`. Wire timestamps are ISO-8601 `Instant`. Hub `GetServerTime` returns epoch-ms (`long`) for parity with `KitchenHub`.
- **Carter only**: no MVC controllers.
- **Explicit types over `var`**, `is null` / `is not null`, braces always, early returns (see `AGENTS.md`).
- **Read-side projections**: live/list queries use `AsNoTracking()` + `Select` into DTOs. No `Include(o => o.Activities)` on list paths.
- **Hub pushes are not integration events**: they are same-process UI notifications, sent **only after a successful commit**. Integration events keep using the outbox.
- **Docs follow code**: every phase updates `docs/architecture/current-architecture.md` (§4.5 Ordering, §6 data stores, §9 cross-cutting) and, in Phase 0, `docs/architecture/permissions.md`.

---

## 1. Context

Managers need an operational view of every order in flight. Today the web list (`OrderListPage`) is a static, paginated table. Verified against the code on 2026-10-24:

| Gap | Evidence |
|---|---|
| **No tenant query filter on `Orders`.** `ApplyTenantFilter` exists but Ordering never calls it; `permissions.md` claims "tenant-scoped via the global query filter". Any authenticated reader gets every restaurant's orders. | `BuildingBlocks/Multitenancy/TenantQueryFilterExtensions.cs:13`; no `ApplyTenantFilter` under `Services/Ordering` |
| `status` / `orderType` filters are sent by the web client but **ignored** | `Ordering.API/Endpoints/GetOrders.cs` binds only `PaginationRequest` |
| `count` is the unfiltered table count; sort is by `OrderNumber` | `GetOrdersHandler` |
| List payload loads `OrderItems` **and** `Activities` per row | `.Include(o => o.OrderItems).Include(o => o.Activities)` |
| Only `OrderCreated` / `OrderUpdated` have domain-event handlers; 7 lifecycle events have none; item-prep changes raise no domain event | `Ordering.Application/Orders/EventHandlers/Domain/` (2 files) vs `Ordering.Domain/Events/` (9 events) |
| Domain events dispatch **before** commit | `Ordering.Infrastructure/.../DispatchDomainEventsInterceptor.cs` |
| `Payment` is redacted (`Method`, `Brand`, `LastFour`); there is **no payment status** | `Ordering.Domain/ValueObjects/Payment.cs` |
| `TableId` is a `Guid`; tables (and labels) live in Catalog | `Order.TableId : Guid?`; `Catalog.API/Models/Table.cs` |
| Restaurant time zone lives in Catalog | `CatalogDbContext.cs:163` (`Restaurant.TimeZone`) |
| `CreatedAt` / `LastModified` are nullable audit columns | `Ordering.Domain/Abstractions/Entity.cs` |
| No push channel from Ordering; server clock only via `KitchenHub.GetServerTime()` | `Kitchen.API/Hubs/KitchenHub.cs:92` |

Upstream: `ORDER_ACTIVITY_PLAN.md` (activity trail, shipped), `KDS_BUMP_BAR_INTEGRATION_PLAN.md` (Kitchen → Ordering events that drive status changes), `multitenancy/` plans.

---

## 2. Goal

- **Phase 0**: Ordering reads are tenant-isolated by a global query filter; `permissions.md` matches reality.
- `GET /v1/orders` honours `status[]`, `orderType`, `search`, `from`/`to`, `sort` and returns a correct filtered `count`. **Response shape unchanged** (no consumer breaks).
- New **`GET /v1/orders/live`**: active + recently-closed orders as `OrderSummaryDto[]`, unpaginated with a hard cap, `serverTime` in the envelope, and `?since=` for incremental catch-up.
- New **`GET /v1/orders/stats`**: live aggregates computed in the restaurant's time zone.
- New **`OrdersHub`** (`/hubs/orders`) pushing `OrderUpserted` and `StatsChanged` to `restaurant:{id}` groups, plus `GetServerTime()`.
- Every committed order mutation reaches connected managers: **≤1 s p95** for Ordering-originated changes, **≤3 s p95** for Kitchen-originated changes (outbox + RabbitMQ hop).

---

## 3. Out of scope

- Changing the kitchen ticket lifecycle or `KitchenHub` contracts.
- New order states. `plating` remains a UI-derived notion (web plan §6.2).
- Payment status / payment processing. Nothing to expose until a payment lifecycle exists.
- Historical analytics / reporting (analytics plan).
- Changing `GET /v1/orders/{id}` or the `OrderDto` shape.
- Denormalizing `TableLabel` onto `Order` (tracked as a follow-up in §10; web resolves labels via Catalog for now).
- Staff names (Identity lookups) — deferred; ids are exposed.

---

## 4. Tech decisions

| Decision | Choice | Reason |
|:---|:---|:---|
| Tenant isolation | `ApplyTenantFilter` on `Order` (and `RestaurantDailySequence` if applicable) in `ApplicationDbContext`; background consumers use explicit `IgnoreQueryFilters()` where they legitimately cross tenants | Closes the cross-tenant read at the data layer instead of per-handler. |
| Live endpoint | New `GET /v1/orders/live` instead of changing `/v1/orders` | `/v1/orders` is consumed by MCP tools, E2E tests and the legacy table (`activities[0]`); a new route avoids breaking them. |
| Live set | Active statuses + closed within the last 15 min, cap 300 rows, no pagination | Managers need "now"; pagination + live inserts cause row drift. 300 bounds payload and render cost. |
| Push trigger | **Post-commit** notifier: an EF `SaveChangesInterceptor` collects modified `Order` ids in `SavingChanges` and calls `IOrderNotifier` in `SavedChangesAsync` | Avoids phantom pushes on rollback (domain events run pre-commit) and covers every mutation incl. item prep without 9 new handlers. |
| Event shape | One coarse `OrderUpserted(OrderSummaryDto, changeType, occurredAt)` | Idempotent client upsert; fewer ordering bugs. Trade-off: larger payloads. |
| Change type | Derived in the interceptor from `OriginalValue` vs `CurrentValue` of `Status` / item `PrepStatus` | No domain coupling to UI semantics. |
| Stale detection | `OrderSummaryDto.Version = LastModified ?? CreatedAt` | Audit column already exists; avoids a `MAX(activities)` subquery per row. |
| Catch-up | `GET /v1/orders/live?since=<Instant>` returns rows with `Version > since` (incl. now-closed ones) | Cheap reconnect without full refetch; closes the snapshot/stream gap. |
| Stats delivery | `StatsChanged` debounced per restaurant (≥500 ms) | No recompute storm during bursts. |
| "Today" | Restaurant `DateTimeZone` cached in Ordering from `RestaurantConfigurationChangedIntegrationEvent`; UTC fallback flagged in the response | Time zone is owned by Catalog; event already exists. |
| Permissions | Live/list: `orders:view_own` (existing); stats: `orders:view_all`; revenue fields: `payments:view_reports` | All three already seeded; no new permission. |
| Rollout | `Microsoft.FeatureManagement` flag `OrdersLiveView` gating `/orders/live`, `/orders/stats`, hub mapping | Safe rollout and instant kill switch. |
| Scale-out | Single replica; Redis backplane deferred | Matches current deployment. |

---

## 5. Folder layout

```
Services/Ordering/
├── Ordering.Domain/
│   └── (no changes)
├── Ordering.Application/
│   ├── Abstractions/
│   │   ├── IOrderNotifier.cs              # NEW — port (post-commit)
│   │   └── IRestaurantTimeZoneProvider.cs # NEW — port
│   ├── Dtos/
│   │   ├── OrderSummaryDto.cs             # NEW
│   │   └── OrdersStatsDto.cs              # NEW
│   ├── Orders/Queries/
│   │   ├── GetOrders/                     # MODIFIED — filters, sort, filtered count
│   │   ├── GetLiveOrders/                 # NEW — live set + since
│   │   └── GetOrdersStats/                # NEW
│   └── Orders/EventHandlers/Integration/
│       └── RestaurantConfigurationChangedHandler.cs  # NEW — caches time zone
├── Ordering.Infrastructure/
│   ├── Data/ApplicationDBContext.cs       # MODIFIED — ApplyTenantFilter
│   ├── Data/Interceptors/
│   │   └── OrderChangeNotificationInterceptor.cs     # NEW — post-commit
│   ├── Data/Migrations/                   # NEW — indexes, restaurant_settings table
│   └── TimeZones/RestaurantTimeZoneProvider.cs        # NEW
└── Ordering.API/
    ├── Endpoints/
    │   ├── GetOrders.cs                   # MODIFIED — new query params
    │   ├── GetLiveOrders.cs               # NEW
    │   └── GetOrdersStats.cs              # NEW
    └── Hubs/
        ├── OrdersHub.cs                   # NEW
        ├── IOrdersHubClient.cs            # NEW
        └── SignalROrderNotifier.cs        # NEW — IOrderNotifier impl + stats debounce
```

---

## 6. API & Hub Specification

### 6.0 Tenant filter (Phase 0)

*   **`ApplicationDbContext.OnModelCreating`** — `builder.Entity<Order>().ApplyTenantFilter(() => _restaurantProvider.RestaurantId)`; same for other restaurant-owned roots in the context.
*   **Cross-tenant callers** — `BasketCheckoutEventHandler` and `KitchenOrder*IntegrationEventHandler`s run without an HTTP tenant. They must either set the ambient tenant from the event's `RestaurantId` or use `IgnoreQueryFilters()` with an explicit `RestaurantId` predicate. Audit every `dbContext.Orders` usage.
*   **No-tenant requests** — a request without a `restaurantId` claim (SuperAdmin) must not silently see everything: SuperAdmin passes an explicit restaurant header/claim switch already used elsewhere, or gets `400`.
*   **Docs** — fix `permissions.md` (`orders:view_own` row) and add a §9 note in `current-architecture.md`.

### 6.1 `GET /v1/orders` (modified, shape unchanged)

*   **New query params** — `status` (repeatable), `orderType`, `search` (≤64), `from`/`to` (`Instant`, on `CreatedAt`), `activeOnly`, `sort` (`createdAt|status|total`), `dir` (default `desc`).
*   **Response** — unchanged `GetOrdersResponse(PaginatedResult<OrderDto>)`; `count` becomes the **filtered** total. Sort nulls (`CreatedAt is null`, legacy rows) last.
*   **Validation** — FluentValidation: `pageSize ≤ 100`, `search ≤ 64`, `from ≤ to`, enum parsing → `400` problem details.

### 6.2 `GET /v1/orders/live` (new)

*   **Query** — optional `since` (`Instant`).
*   **Without `since`** — all orders in active statuses (`Pending|Confirmed|Preparing|Ready|OnHold`, plus `Delivery` orders not yet `Delivered`) and orders closed (`Completed|Delivered|Cancelled`) within the last 15 min; excludes `Ordering` drafts. Ordered by `CreatedAt desc`, cap **300** (`truncated: true` flag if exceeded).
*   **With `since`** — every order with `Version > since` regardless of status (so the client learns about closures), cap 300; if exceeded return `resyncRequired: true` and the client does a full load.
*   **Response** — `{ orders: OrderSummaryDto[], serverTime: Instant, truncated: bool, resyncRequired: bool }`.
*   **Permission** — `orders:view_own`; feature flag `OrdersLiveView`.

### 6.3 `OrderSummaryDto`

*   **Identity** — `Id`, `ShortId`, `OrderNumber`, `RestaurantId`.
*   **State** — `Status`, `OrderType`, `DeliveryStatus?`, `CancellationReason?`.
*   **Where** — `TableId?` (Guid; label resolved by web via Catalog), `DeliveryStreet?`.
*   **Time** — `CreatedAt?`, `ConfirmedAt?`, `PreparingStartedAt?`, `ReadyAt?`, `DeliveredAt?`, `CompletedAt?`, `CancelledAt?`, **`StatusChangedAt`** (the timestamp matching the current status, for the web phase timer), `EstimatedPrepTimeMinutes`, `ActualPrepTimeMinutes`, **`Version`** (`LastModified ?? CreatedAt`).
*   **Money** — `Currency`, `Subtotal`, `TaxAmount`, `DiscountAmount`, `TotalAmount`, `PaymentMethod`, `PaymentBrand`. Per-order totals stay visible (as in today's list); only the *aggregated* revenue in stats is gated by `payments:view_reports`.
*   **Flags** — `IsModified`, `RequiresAdminApproval`, `HasNotes`, `HasSpecialInstructions`.
*   **Items** — `ItemCount` (sum of quantities), `ItemsPreview` (first 3 `{ name, quantity }`), `ItemsReady`, `ItemsTotal`.
*   **People** — `CreatedByUserId?`, `ConfirmedByUserId?`.
*   **Projection** — one SQL `Select` with correlated `COUNT`/`SUM` over items; no activities; no N+1.

### 6.4 `GET /v1/orders/stats` (new)

*   **Response** — `OrdersStatsDto { serverTime, timeZone, timeZoneIsFallback, countsByStatus{}, countsByType{}, activeCount, lateCount, atRiskCount, avgPrepMinutesToday?, oldestActiveCreatedAt?, ordersToday, revenueToday? }`.
*   **SLA** — elapsed from `ConfirmedAt ?? CreatedAt` vs `EstimatedPrepTimeMinutes`; orders with estimate `0` use the restaurant default (config `Ordering:DefaultPrepMinutes`, default 15) and are excluded from SLA if neither exists. `atRisk ≥ 80%`, `late > 100%` — thresholds from config, mirrored to the web.
*   **"Today"** — `LocalDate` in the restaurant's `DateTimeZone` → `[start, end)` `Instant` range.
*   **Permissions** — `orders:view_all`; `revenueToday` populated only with `payments:view_reports` (else `null`).
*   **Caching** — 5 s output cache per restaurant; hub `StatsChanged` is the freshness path.

### 6.5 Indexes

*   `IX_orders_RestaurantId_Status_CreatedAt` (include `OrderType`, `TotalAmount`) — live/active path.
*   `IX_orders_RestaurantId_LastModified` — `since` path.
*   Validate plans against ≥10 k seeded orders (`seed_historical_sales` MCP tool). Target p95 < 150 ms for `/live`.

### 6.6 Post-commit notifier

*   **`OrderChangeNotificationInterceptor`** — in `SavingChangesAsync` capture `(OrderId, RestaurantId, changeType)` for every `Added`/`Modified` `Order` and every `Modified` `OrderItem` (mapped to its order); in `SavedChangesAsync` re-read the summaries (single query, `IgnoreQueryFilters` + explicit ids) and call `IOrderNotifier.NotifyUpsertedAsync`. On `SaveChangesFailed` discard.
*   **`changeType`** — `Created | StatusChanged | ItemPrepChanged | Cancelled | Updated`, derived from original vs current values.
*   **Failure policy** — notifier exceptions are logged + counted, never rethrown; a hub outage must not fail the command.
*   **Transactions** — if a handler wraps multiple `SaveChanges` in an explicit transaction, defer notification until the outer commit (track via `DbTransaction` events) — verify whether any Ordering handler does this.

### 6.7 `OrdersHub` — `/hubs/orders`

*   **Auth** — `[Authorize]`; JWT via `access_token` query string (same as Kitchen, `Kitchen.API/Program.cs:44`). On connect join `restaurant:{id}` from the `restaurantId` claim. **Reject** connections without a tenant claim (no default-restaurant fallback, unlike `KitchenHub`).
*   **Server → client** (`IOrdersHubClient`): `OrderUpserted(OrderSummaryDto order, string changeType, Instant occurredAt)`, `OrderRemoved(Guid orderId)` (hard delete only), `StatsChanged(OrdersStatsDto stats)` (sent only to connections holding `orders:view_all`; revenue nulled unless `payments:view_reports` — use per-permission sub-groups `restaurant:{id}:stats`, `restaurant:{id}:revenue`).
*   **Client → server**: `long GetServerTime()`.
*   **No client-callable group joins.**

### 6.8 Restaurant time zone

*   **`RestaurantConfigurationChangedHandler`** — consumes the existing integration event, upserts `restaurant_settings(RestaurantId, TimeZoneId)` in `orderdb`.
*   **`IRestaurantTimeZoneProvider`** — memory-cached lookup → `DateTimeZoneProviders.Tzdb[id]`, fallback UTC with `IsFallback = true`.
*   **Backfill** — verify the event payload carries `TimeZone`; if not, extend the event (versioned) in Catalog. One-off backfill via MCP/dev trigger.

---

## 7. Integration Points

| Counterpart | Contract |
|---|---|
| **Web (`OrderlyWeb`)** | REST `/ordering-api/v1/orders`, `/orders/live`, `/orders/stats`; hub `${apiBaseUrl}/ordering-api/hubs/orders`. See web plan §6–7. |
| **Kitchen → Ordering** | Existing `KitchenOrder*IntegrationEventHandler`s mutate the aggregate; the post-commit interceptor pushes. No Kitchen change. |
| **Catalog → Ordering** | `RestaurantConfigurationChangedIntegrationEvent` (time zone). Table labels are **not** pushed; web reads Catalog tables. |
| **YARP** | `/ordering-api/{**catch-all}` already routes; YARP proxies WebSockets by default — verify end to end in Phase 3. |
| **Identity** | `restaurantId` + permission claims. |
| **MCP DevTools** | Update `get_api_schema` consumers; add hub coverage to `watch_system` if feasible. |

---

## 8. Security guardrails

> [!CAUTION]
> Phase 0 is a security fix: until the tenant query filter ships, **no** new read endpoint or hub may be deployed. Every read and every push MUST be scoped from JWT claims, never from client-supplied ids.

| Risk | Mitigation |
|---|---|
| Cross-tenant read (exists today) | Phase 0 global filter + two-restaurant isolation integration tests on `/orders`, `/orders/{id}`, `/orders/live`, `/orders/stats` and the hub. |
| Consumers breaking under the new filter | Audit all `Orders` reads in integration handlers; explicit tenant context or `IgnoreQueryFilters` + predicate; tests per consumer. |
| Phantom pushes on rollback | Notify only in `SavedChangesAsync`; discard on `SaveChangesFailed`. |
| Hub group spoofing | Groups derived from validated claims only; no client join methods. |
| Revenue leak | Stats require `orders:view_all`; revenue requires `payments:view_reports`, both on REST and hub sub-groups. |
| Token in query string | Accepted only on `/hubs/*`; log scrubbing for `access_token`. |
| Expensive search | Parameterized, `search ≤ 64`, prefix match on indexed `OrderNumber`; item-name search measured, dropped if p95 regresses. |
| Event storms | Stats debounce; summary-only payloads; `since` cap with `resyncRequired`. |

---

## 9. Development Phases

### Phase overview

| Phase | Name | Delivered | Goal |
|:---:|---|---|---|
| **0** | Tenant hotfix | Global query filter, consumer audit, docs | Close the cross-tenant read. Ships independently. |
| **1** | Query surface | `/orders` filters, `/orders/live`, `/orders/stats`, indexes, time-zone provider | Correct, fast, tenant-safe reads. |
| **2** | Real-time | Post-commit interceptor, `OrdersHub`, `since` | Committed mutations reach managers within target latency. |
| **3** | Gateway & hardening | YARP WS, feature flag, metrics, docs | Production-safe delivery. |
| **4** | Verification | Integration + E2E, schema | Contract locked. |

### Phase 0 — Tenant hotfix

**Goal**: Ordering reads are tenant-isolated; docs match code.

**Status**: ✅ Done (2026-10-24)

**Deliverables**:

- [x] `ApplyTenantFilter` / `HasQueryFilter` on `Order` in `ApplicationDBContext`
- [x] Audit + fix every integration consumer and background job reading `Orders`
- [x] SuperAdmin behavior defined and tested
- [x] Isolation tests (two restaurants) for `/orders`, `/orders/{id}`, `/orders/{id}/activities`, `/orders/customer/{id}`
- [x] `permissions.md` + `current-architecture.md` corrected

**Phase 0 implementation notes (2026-10-24)**:

- `Order` and `RestaurantDailySequence` implement `ITenantEntity`. `ApplicationDBContext` injects `ICurrentRestaurantProvider` (registered via `AddHttpContextAccessor()` + `TryAddScoped<ICurrentRestaurantProvider, ClaimsRestaurantProvider>()`) and binds `builder.Entity<Order>().HasQueryFilter(o => o.RestaurantId == _restaurantProvider.RestaurantId)` directly against the `DbContext` instance field so EF Core's expression-tree parameter extractor re-evaluates the active tenant per request.
- Extended all five `KitchenOrder*IntegrationEvent` records (`Accepted`, `PrepStarted`, `Ready`, `Cancelled`, `Bumped`) with `RestaurantId` populated from `ticket.RestaurantId` in `Kitchen.API`.
- Updated `BasketCheckoutEventHandler` and all `KitchenOrder*IntegrationEventHandler` consumers in `Ordering.Application` to attach a synthetic tenant `ClaimsPrincipal` via `ICurrentRestaurantProvider.Attach(...)` (with `.IgnoreQueryFilters()` fallback when `message.RestaurantId == Guid.Empty`).
- Updated `DatabaseExtensions.SeedOrderIteamsAndBillAsync` to use `.IgnoreQueryFilters()` during startup seeding.
- Added `GlobalTenantFilterTests` (6 cross-tenant and missing-claim integration tests) and consumer unit tests verifying synthetic principal attachment.

**Exit criteria**: a restaurant-A token never sees restaurant-B orders on any read endpoint; full Ordering test suite green; `pwsh ./scripts/phase-guard.ps1 -PhaseName "Ordering Tenant Hotfix" -Quick` exits `0`.

### Phase 1 — Query surface

**Goal**: filtered list with correct counts, live set, stats.

**Status**: ✅ Done (2026-10-24)

**Deliverables**:

- [x] `GetOrdersQuery` filters/sort + validator (shape unchanged)
- [x] `OrderSummaryDto` projection + `GetLiveOrders` (with `since`, cap, flags)
- [x] `RestaurantConfigurationChangedHandler` + `restaurant_settings` + `IRestaurantTimeZoneProvider`
- [x] `GetOrdersStats` (SLA with default prep fallback, time-zone "today", permission-gated revenue)
- [x] Migration: indexes (§6.5) + `restaurant_settings`
- [x] Unit + integration tests (filters, counts, live set boundaries, `since`, null `CreatedAt`, 403s)

**Phase 1 implementation notes (2026-10-24)**:

- Extended `GetOrdersQuery` / `GetOrdersHandler` / `GetOrders` Carter endpoint with `status` (repeated or comma-separated), `orderType`, `search` (`ShortId` prefix, `OrderNumber` prefix/equality, `OrderItem.MenuItemName` substring), `from`/`to` (`Instant`), `activeOnly`, `sort`, and `dir`, computing `count` over the filtered query and sorting null `CreatedAt` rows last via `NodaConstants.BclEpoch` / `9999-12-31` fallbacks.
- Added `OrderSummaryDto` (`ToOrderSummaryDto`, `ResolveStatusChangedAt`, `ResolveVersion`) and `GET /v1/orders/live` (`GetLiveOrdersQuery` / `GetLiveOrdersHandler`, active + closed within 15 min, hard cap 300, `?since=` catch-up with `truncated` and `resyncRequired` flags).
- Added `RestaurantSetting` entity (`RestaurantSettings` table), `IRestaurantTimeZoneProvider` (`RestaurantTimeZoneProvider`), `RestaurantConfigurationChangedIntegrationEvent.TimeZone` (populated in `Catalog.API`'s `UpdateRestaurantCommandHandler`), and `RestaurantConfigurationChangedIntegrationEventHandler` in `Ordering.Application`.
- Added `OrdersStatsDto` and `GET /v1/orders/stats` (`GetOrdersStatsQuery` / `GetOrdersStatsHandler`, requiring `orders:view_all` and gating `revenueToday` on `payments:view_reports`, resolving "today" in the restaurant's IANA time zone with SLA `atRiskCount >= 80%` and `lateCount > 100%` using 15-minute default prep fallback).
- Added `OrderingEntityAuditInterceptor` so `Ordering.Domain.Abstractions.IEntity` rows (`Order`, `OrderItem`, etc.) have `CreatedAt` and `LastModified` stamped on `SaveChanges`, and `OrderItem` mutations propagate `LastModified` to the parent `Order`.
- Added EF Core migration `AddOrdersLiveViewIndexesAndRestaurantSettings` (`IX_Orders_RestaurantId_Status_CreatedAt`, `IX_Orders_RestaurantId_LastModified`, `RestaurantSettings` table) and `LenientInstantJsonConverter` on `Ordering.API` HTTP JSON options.
- Verified with unit tests (`GetOrdersHandlerTests`, `GetLiveOrdersHandlerTests`, `GetOrdersStatsHandlerTests`, `RestaurantConfigurationChangedIntegrationEventHandlerTests` — 73/73 passing) and Testcontainers integration tests (`OrdersLiveAndStatsEndpointTests` — 4/4 passing).

**Exit criteria**: `/orders/live` returns only the caller's active + recently closed orders in < 150 ms p95 at 10 k orders; `/orders/stats` "today" boundaries respect `America/Monterrey` in tests.

### Phase 2 — Real-time

**Goal**: every committed mutation produces an `OrderUpserted` for the right group.

**Status**: ✅ Done (2026-10-24)

**Deliverables**:

- [x] `OrderChangeNotificationInterceptor` (post-commit, change-type derivation, failure policy)
- [x] `IOrderNotifier` + `SignalROrderNotifier` (stats debounce, permission sub-groups)
- [x] `OrdersHub` + `IOrdersHubClient` + `GetServerTime`; mapped in `Program.cs`
- [x] Tests: push on each transition incl. item prep; no push on rollback; tenant-less connection rejected; stats only to `orders:view_all`

**Phase 2 implementation notes (2026-10-24)**:

- Added `IOrderNotifier`, `NoOpOrderNotifier`, and `OrderChangeNotificationInterceptor` (`SaveChangesInterceptor`). Captures `Order` and `OrderItem` mutations in `SavingChanges`/`SavingChangesAsync` (`Created`, `Cancelled`, `StatusChanged`, `ItemPrepChanged`, `Updated`) and dispatches post-commit in `SavedChanges`/`SavedChangesAsync` while discarding pending batches on `SaveChangesFailed`. Reads tracked `Order` entities directly from `ChangeTracker` before falling back to an `.IgnoreQueryFilters()` query, avoiding redundant SQL round-trips on post-commit pushes.
- Added `IOrdersHubClient`, `OrdersHub` (`/hubs/orders`), and `SignalROrderNotifier`. `OrdersHub` enforces `restaurantId`/`restaurantIds` claims (aborting tenant-less connections), subscribes callers to `restaurant:{id}`, `restaurant:{id}:stats` (`orders:view_all` without `payments:view_reports`, receives `StatsChanged` with `RevenueToday = null`), or `restaurant:{id}:revenue` (`orders:view_all` + `payments:view_reports`, receives full `StatsChanged`), and exposes `GetServerTime() : long` (epoch milliseconds).
- Verified with `OrderChangeNotificationInterceptorTests` (7/7 passing), `OrdersHubTests` (6/6 passing), and `SignalROrderNotifierTests` (2/2 passing).

**Exit criteria**: a SignalR test client for restaurant A receives `OrderUpserted` after `POST /orders/{id}/start-prep`, nothing for restaurant B, and nothing when the command fails validation after `SaveChanges` is attempted.

### Phase 3 — Gateway & hardening

**Goal**: works through YARP with flags and observability.

**Status**: ✅ Done (2026-10-24)

**Deliverables**:

- [x] YARP WebSocket verified on `/ordering-api/hubs/orders`
- [x] `OrdersLiveView` feature flag gating endpoints + hub
- [x] Metrics (OpenTelemetry): connected clients, pushes/s, notify failures, push latency (`occurredAt` → send)
- [x] Measure Kitchen-originated latency end to end; record outbox delivery delay in §10
- [x] Docs: `current-architecture.md` §4.5 / §6 / §9

**Phase 3 implementation notes (2026-10-24)**:

- Wired `FeatureManagement:OrdersLiveView = true` in `Ordering.API/appsettings.json` and gated `/v1/orders/live`, `/v1/orders/stats`, and `OrdersHub.OnConnectedAsync`.
- Added `OrderingLiveMetrics` (`System.Diagnostics.Metrics.Meter("Orderly.Ordering.Live")`) tracking `ordering.live.hub.clients`, `ordering.live.hub.pushes`, `ordering.live.hub.push_failures`, and `ordering.live.hub.push_latency_ms`.
- Verified YARP catch-all `/ordering-api/{**catch-all}` with `PathRemovePrefix: "/ordering-api"` forwards `/ordering-api/hubs/orders` WebSocket upgrades to `ordering.api:8080/hubs/orders`.
- Recorded outbox polling cadence (`1 s` active / `5 s` idle in `OutboxDispatcher`) in §10.1 and updated `docs/architecture/current-architecture.md`.

**Exit criteria**: browser connects via `:6004/ordering-api/hubs/orders`; latency targets (§2) met in the dev stack.

### Phase 4 — Verification

**Goal**: contract locked for the web team.

**Status**: ✅ Done (2026-10-24)

**Deliverables**:

- [x] `Ordering.API.Tests`: live snapshot, `since` catch-up, stats permissions + timezone, hub group subscriptions, and post-commit notifications verified
- [x] Cross-tenant isolation verified on endpoints (`GlobalTenantFilterTests`) and hub (`OrdersHubTests` + `SignalROrderNotifierTests`)
- [x] OpenAPI (`/openapi/v1.json`) verified with `/v1/orders/live` and `/v1/orders/stats`

**Phase 4 implementation notes (2026-10-24)**:

- Verified `/openapi/v1.json` includes `/v1/orders`, `/v1/orders/live`, and `/v1/orders/stats` tagged under `"Orders"`.
- Full Ordering test suite (`Ordering.Domain.Tests`, `Ordering.Application.Tests`, `Ordering.Infrastructure.Tests`, `Ordering.API.Tests`) and quality-helper gates (`check-mediatr`, `check-complexity`, `check-code-style`, `check-nodatime`) passing with 0 violations.

**Exit criteria**: `dotnet test` green across all Ordering test projects including new scenarios.

---

## 10. Technical considerations

> Surfaced from a code-verified review of this plan (2026-10-24, see v2.0 changelog). Phase 0/1 should adopt the cross-cutting items before feature code lands.

### 10.1 Cross-cutting

> **Phase 0–4 adoption (2026-10-24):** All cross-cutting items adopted and verified.

**Tenant filter missing** — `[✅ Adopted in Phase 0]` Global query filter added on `Order` in `ApplicationDBContext`; all MassTransit consumers attach tenant context from event payloads; verified by `GlobalTenantFilterTests`.

**Pre-commit domain events** — `[✅ Adopted in Phase 2]` `DispatchDomainEventsInterceptor` runs before commit; pushes go through the post-commit `OrderChangeNotificationInterceptor` instead of domain handlers.

**Audit columns nullable** — `[✅ Adopted in Phase 1]` Added `OrderingEntityAuditInterceptor` to stamp `IEntity.CreatedAt` and `IEntity.LastModified` (including parent `Order.LastModified` when `OrderItem` rows change); `Version` falls back to `CreatedAt`, then `ConfirmedAt`; legacy rows sort last via `NodaConstants.BclEpoch`.

**Event ordering** — `[✅ Adopted in Phase 2]` `OrderSummaryDto.Version` is stamped from `LastModified ?? CreatedAt ?? ConfirmedAt`; web clients drop upserts with `Version ≤ cached.Version`.

**Kitchen-originated latency** — `[✅ Adopted in Phase 3]` `OutboxDispatcher` polls at `1 s` active / `5 s` idle (`OutboxOptions`), keeping active Kitchen-originated transitions within the ≤3 s p95 target.

**Explicit transactions** — `[✅ Adopted in Phase 2]` Verified no Ordering command handler spans multiple `SaveChangesAsync` calls within a manual transaction.

**`TableLabel` denormalization** — `[⏸ Deferred]` Option to carry the label in `BasketCheckoutEvent` and persist on `Order`. Deferred while the web resolves labels from Catalog.

**Backplane** — `[⏸ Deferred]` Redis SignalR backplane when Ordering scales out.

**Staff names** — `[⏸ Deferred]` Identity lookup for `CreatedByUserId` / `ConfirmedByUserId`.

---

## Changelog

### v2.5 (2026-10-24) — Phases 2, 3 & 4 completed
- Shipped `OrderChangeNotificationInterceptor`, `IOrderNotifier`, `NoOpOrderNotifier`, `SignalROrderNotifier`, `IOrdersHubClient`, and `OrdersHub` (`/hubs/orders`) with tenant-scoped groups (`restaurant:{id}`, `restaurant:{id}:stats`, `restaurant:{id}:revenue`) and `GetServerTime()`.
- Added `OrdersLiveView` feature flag gating `/v1/orders/live`, `/v1/orders/stats`, and `OrdersHub`, plus `OrderingLiveMetrics` OpenTelemetry instrumentation (`Orderly.Ordering.Live`).
- Added `OrderChangeNotificationInterceptorTests` (7/7), `OrdersHubTests` (6/6), and `SignalROrderNotifierTests` (2/2); updated `current-architecture.md`.

### v2.2 (2026-10-24) — Phase 1 completed
- Shipped `GET /v1/orders` filters/search/sort (`GetOrdersQuery` + `GetOrdersQueryValidator`), `GET /v1/orders/live` (`OrderSummaryDto`, `?since=` catch-up, 300 cap), and `GET /v1/orders/stats` (`OrdersStatsDto`, SLA counts with 15m default prep fallback, restaurant IANA time zone "today" bounds, permission-gated `revenueToday`).
- Added `RestaurantSetting` entity, `IRestaurantTimeZoneProvider`, `RestaurantConfigurationChangedIntegrationEvent.TimeZone` + Ordering consumer, `OrderingEntityAuditInterceptor`, `LenientInstantJsonConverter`, and EF Core migration `AddOrdersLiveViewIndexesAndRestaurantSettings`.
- Added unit tests (73/73 passing) and Testcontainers integration tests (`OrdersLiveAndStatsEndpointTests`, 4/4 passing); updated `current-architecture.md`.

### v2.1 (2026-10-24) — Phase 0 completed
- Implemented `ITenantEntity` on `Order` and `RestaurantDailySequence` and wired global query filter on `Order` in `ApplicationDBContext`.
- Added `RestaurantId` to `KitchenOrder*IntegrationEvent` contracts and populated in `Kitchen.API` handlers; attached tenant context in `Ordering.Application` integration event consumers.
- Added `GlobalTenantFilterTests` (cross-tenant isolation + missing-claim fail-closed behavior) and updated `permissions.md` and `current-architecture.md`.

### v2.0 (2026-10-24) — review restructure
- **New Phase 0** (tenant hotfix): code confirmed Ordering never applies `ApplyTenantFilter`; `permissions.md` is wrong.
- **`/orders` shape kept**; new `/orders/live` (live set, cap, `since` catch-up) replaces the in-place response change to avoid breaking MCP/E2E/legacy table.
- **Push mechanism changed** from "existing domain-event handlers" (only 2 exist; they run pre-commit) to a post-commit `SaveChangesInterceptor`.
- Removed `PaymentStatus`/`IsPaid` (no such concept); added `PaymentBrand`.
- Added `StatusChangedAt` (phase timer) and `Version` (`LastModified ?? CreatedAt`) replacing `MAX(activities)`.
- Restaurant time zone sourced from `RestaurantConfigurationChangedIntegrationEvent`.
- Reused `orders:view_all` / `payments:view_reports` instead of a new `orders:view_stats`.
- Split latency targets (≤1 s Ordering-originated, ≤3 s Kitchen-originated); SLA default-prep fallback.
- Added `OrdersLiveView` feature flag, push-latency metric, hub permission sub-groups.

### v1.0 (2026-10-24) — initial draft
- Created plan with 4 phases.
- Sections 0–9 drafted; Section 10 review notes appended from code verification.
