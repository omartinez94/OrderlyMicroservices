# Ordering — Restaurant Operations Live Dashboard Summary Implementation Plan

> Scope: Deliver an aggregated, permission-aware `GET /v1/restaurants/{id}/dashboard/live-summary` endpoint in `Ordering.API` that composes Ordering data (SQL Server) with Catalog and Kitchen data, caches the raw snapshot in Redis, and notifies browsers through the existing `OrdersHub` when it changes.

---

## Status

> **Plan version**: `v1.3` (2026-11-14)
> **Current state**: ✅ Completed (Phases 1-5 complete)
> **Depends on**: nothing hard (uses existing events and endpoints); Phase 2 may add small read endpoints to Catalog/Kitchen.
> **Required by**: `restaurant_dashboard_ui.md` (Web; it has a fallback).

| Phase | Name | Status |
|:-----:|---|:-----:|
| 1 | Source Inventory, Contracts & Metric Definitions | ✅ Done |
| 2 | Aggregator Service, Cross-Service Readers & Redis Cache | ✅ Done |
| 3 | Carter Endpoint, Permissions & YARP Mapping | ✅ Done |
| 4 | Event-Driven Invalidation & `OrdersHub` Notification | ✅ Done |
| 5 | Integration Tests, Load Smoke & Phase Guard | ✅ Done |

> **Legend**: ✅ Done · 🚧 In progress · ⏸ Pending · 🔒 Blocked

> **Commit messages**: Conventional Commits. Short subject, ≤50 chars, imperative mood, no trailing period. Never include AI attribution or Co-Authored-By.

> **Update rule**: on every phase completion, update this document and commit in the same phase commit.

---

## 0. Skill & Documentation Conventions

### 0.1 Skill Mandate — `add-ordering-feature` & `csharp-developer`
> **All implementation work MUST follow Clean Architecture (Domain → Application → Infrastructure → API), Carter Minimal APIs, FluentValidation pipeline, and strict NodaTime date handling.**

### 0.2 Code-Quality Guard Rails
- **Quality Gate** (from `OrderlyMicroservices/`): `pwsh ./scripts/phase-guard.ps1 -PhaseName "<Phase Name>" -Quick`
- **NodaTime mandatory**; never `DateTime`/`DateTimeOffset`.
- **Engines**: Ordering is SQL Server; Catalog/Kitchen are PostgreSQL (accessed only through their APIs or events, never their databases).
- **Outbox** for any integration event Ordering publishes; no direct `IPublishEndpoint`.
- **Carter only**; no Controllers.

---

## 1. Context (verified against code, 2026-10-31)

The restaurant home needs one snapshot instead of several calls with inconsistent timestamps. Facts that shape the design:

- **Ordering owns only orders.** Tables, reservations, waitlist and analytics live in **Catalog**; ticket timings and stations live in **Kitchen**. The snapshot therefore cannot come from SQL Server alone.
- **Existing routes** are `/v1/...` (gateway prefix `/ordering-api`, so public URL `/ordering-api/v1/...`). Ordering routes today are **not** restaurant-scoped (`/v1/orders/stats` derives tenant from the JWT). Earlier drafts that used `/api/v1/...` were wrong.
- **Existing hub**: `OrdersHub` at `/hubs/orders` with groups `restaurant:{id}`, `restaurant:{id}:stats`, `restaurant:{id}:revenue`, client methods `OrderUpserted`, `OrderRemoved`, `StatsChanged(OrdersStatsDto)`. The hub rejects connections when feature flag `OrdersLiveView` is off.
- **Existing stats redact revenue** unless the caller has `payments:view_reports`.
- **Real integration events**: `OrderCreatedIntegrationEvent`, `OrderCompletedIntegrationEvent`, `OrderCancelledIntegrationEvent`, `KitchenOrderAccepted/PrepStarted/Ready/Bumped/Cancelled/RecalledIntegrationEvent`, Catalog's `TableStatusChangedIntegrationEvent` and `RestaurantConfigurationChangedIntegrationEvent`. (`TicketBumped…`, `TicketStatusChanged…`, `OrderStatusChanged…` do **not** exist.)
- **Permissions model** (no `RestaurantStaff` policy exists).

---

## 2. Goal

1. A `RestaurantOperationsDashboardDto` with: ticker, sales pace, kitchen velocity, floor pulse, open checks, waitlist, alerts, and a `meta` block (per-section freshness/degradation).
2. `GetRestaurantDashboardLiveSummaryQuery` + handler in `Ordering.Application`.
3. Aggregation across Ordering (SQL), Catalog and Kitchen with **per-section graceful degradation**.
4. Redis cache of the **raw snapshot** (absolute TTL, single-flight); revenue fields redacted **after** reading the cache according to the caller's permissions.
5. Event-driven, debounced invalidation and an `OrdersHub` notification.

---

## 3. Out of Scope

- Reporting beyond the 7-day baseline; payroll/shift data; mutating state from this endpoint.
- Night-audit data (void log, Z-report, reconciliation), heatmaps, VIP arrivals, server station load, shift roster — **deferred**; they need their own contracts (the Web v1 does not ship these widgets).
- A user-preferences endpoint (dashboard layout stays local in the Web v1).

---

## 4. Tech Decisions

| Decision | Choice | Reason |
| :--- | :--- | :--- |
| Hosting | `Ordering.API` as an **aggregator** (option "a" from the review) | Smallest change; no new service/read model. A local event-fed read model is a possible later optimization. |
| Route | `GET /v1/restaurants/{restaurantId:guid}/dashboard/live-summary` (public: `/ordering-api/v1/restaurants/{id}/dashboard/live-summary`); `restaurantId` must be in the caller's JWT `restaurantId`/`restaurantIds` claims (`403` otherwise) | Explicit tenant for multi-restaurant users; consistent with Catalog style. |
| Authorization | New permission **`dashboard:view`** (seeded in Identity for SuperAdmin, RestaurantAdmin, Manager, KitchenManager, Waiter, Cashier, Host). Financial fields additionally require **`payments:view_reports`** (same rule as `/v1/orders/stats`); otherwise those fields are `null` | Permissions, not roles/policies; no revenue leak. |
| Cross-service calls | `ICatalogDashboardReader` / `IKitchenDashboardReader` via typed `HttpClient` + Polly (timeout 800 ms, 1 retry, circuit breaker), forwarding the caller's bearer token | Reuses existing auth; failures degrade a section instead of failing the request. |
| Cache | Redis key `restaurant:{id}:operations:live_summary` stores the **raw (revenue-inclusive) snapshot**; absolute TTL 15 s; per-key single-flight lock; invalidation **debounced (2 s)** | Fixes revenue leak; avoids stampedes; avoids cache thrash during rushes. "Sliding" TTL dropped. |
| Time | NodaTime `Instant`; business day = restaurant-local date using `DateTimeZone` from restaurant configuration | Correct "today" and hourly buckets. |
| Hub notification | New `IOrdersHubClient.DashboardChanged(DashboardChangedDto)` to group `restaurant:{id}:stats` (carries **no revenue**, only `restaurantId`, changed sections, `occurredAt`); clients refetch (debounced) | Reuses existing groups; never pushes revenue to non-revenue users; existing `StatsChanged` is untouched. |

---

## 5. Folder Layout

```text
Services/Ordering/
├── Ordering.Application/
│   ├── Common/Interfaces/{IDashboardSummaryService,ICatalogDashboardReader,IKitchenDashboardReader}.cs
│   └── Dashboard/
│       ├── Queries/GetRestaurantDashboardLiveSummary/{Query,Handler,Validator}.cs
│       └── Dtos/{RestaurantOperationsDashboardDto,LiveOperationalTickerDto,SalesPaceDto,
│                 KitchenVelocitySnapshotDto,FloorPulseSnapshotDto,OpenCheckDto,
│                 WaitlistEntryDto,OperationalAlertDto,DashboardMetaDto}.cs
├── Ordering.Infrastructure/
│   ├── Services/DashboardSummaryService.cs            # SQL aggregation + composition
│   ├── Http/{CatalogDashboardReader,KitchenDashboardReader}.cs
│   └── Caching/DashboardSnapshotCache.cs              # single-flight + redaction input
└── Ordering.API/
    ├── Endpoints/Dashboard/GetRestaurantDashboardLiveSummaryEndpoint.cs
    ├── Consumers/DashboardInvalidationConsumers.cs
    └── Hubs/IOrdersHubClient.cs                        # + DashboardChanged
```
Plus small read endpoints in Catalog/Kitchen only where Phase 1 finds gaps.

---

## 6. API & DTO Specification

### 6.1 Request / authorization
- `GET /v1/restaurants/{restaurantId:guid}/dashboard/live-summary`, `.RequirePermission("dashboard:view")`.
- Revenue-bearing fields are `null` unless the caller has `payments:view_reports`.
- Errors: `401`, `403` (permission or restaurant claim mismatch), `200` with `meta` degradation flags when a dependency is down.

### 6.2 Response `200 OK`
```json
{
  "serverTime": "2026-10-31T15:00:00Z",
  "restaurantId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "currency": "USD",
  "timeZone": "America/Mexico_City",
  "businessDate": "2026-10-31",
  "meta": {
    "sections": {
      "ticker": "ok", "salesPace": "ok", "kitchenVelocity": "degraded",
      "floorPulse": "ok", "openChecks": "ok", "waitlist": "unavailable", "alerts": "ok"
    },
    "generatedAt": "2026-10-31T15:00:00Z"
  },
  "ticker": {
    "netSalesToday": 4850.50,
    "salesTargetToday": 6500.00,
    "salesGrowthPercentage": 14.2,
    "activeOrdersCount": 18,
    "lateOrdersCount": 2,
    "atRiskOrdersCount": 3,
    "occupiedTablesCount": 22,
    "totalTablesCount": 28,
    "occupancyPercentage": 78.5,
    "avgPrepMinutesToday": 13.8,
    "p90PrepMinutesRolling": 21.4
  },
  "salesPace": {
    "hourlySales": [
      { "hourStart": "2026-10-31T11:00:00-06:00", "hourOfDay": 11, "grossSales": 650.00, "orderCount": 14, "historicalAverageSales": 520.00 }
    ],
    "openBalanceTotal": 1420.00,
    "discountsTotal": 85.00,
    "discountPercentageOfSales": 1.75,
    "cashTenderedTotal": 940.00,
    "digitalTenderedTotal": 3910.50
  },
  "kitchenVelocity": {
    "activeTickets": 12, "avgPrepMinutes": 13.8, "targetPrepMinutes": 15.0, "p90PrepMinutes": 21.4,
    "stations": [ { "stationId": "5d3a…", "stationName": "Grill Line", "activeTicketsCount": 6, "avgPrepMinutes": 16.2, "isDelayed": true } ],
    "passDwellCount": 2
  },
  "floorPulse": {
    "totalTables": 28, "occupiedTables": 22, "needsCleaningCount": 3, "reservedUpcomingCount": 5,
    "activeCovers": 64, "totalCapacity": 90, "avgTableTurnMinutes": 52,
    "waitlistCount": 4, "estimatedWaitMinutes": 25
  },
  "openChecks": [
    { "orderId": "…", "orderNumber": "104", "tableLabel": "T8", "balance": 86.40, "ageMinutes": 42 }
  ],
  "waitlist": [
    { "entryId": "…", "partyName": "García", "partySize": 4, "waitMinutes": 18 }
  ],
  "activeAlerts": [
    {
      "id": "kitchen_late:ord-104",
      "timestamp": "2026-10-31T14:52:10Z",
      "severity": "critical",
      "category": "kitchen_late",
      "title": "Table 8 Order Delayed",
      "message": "Ticket #104 has been in preparation for 24 minutes (Target: 15m)",
      "actionUrl": "/site/restaurant/orders/ord-104",
      "actionLabel": "Expedite Order",
      "entityId": "ord-104"
    }
  ]
}
```
`openChecks` and `waitlist` are capped (top 10, oldest first); `openChecks[].balance`, `salesPace` money fields and `ticker.netSalesToday|salesTargetToday|salesGrowthPercentage` are redacted (`null`) without `payments:view_reports`. Section status values: `ok | degraded | unavailable`; an `unavailable` section is `null`/empty.

### 6.3 Metric definitions (normative)
| Metric | Definition |
|---|---|
| Business day / "today" | Restaurant-local calendar date from the restaurant configuration time zone (`RestaurantConfigurationChangedIntegrationEvent` source). Cutoff hour configurable, default `00:00`. |
| Hourly buckets | Restaurant-local hours; `hourStart` carries the offset; `hourOfDay` is the local integer. |
| Historical average | Mean gross sales for the same local hour across the previous 7 business days (excluding today). |
| `salesGrowthPercentage` | `(netSalesToday − avgNetSalesSameElapsedTime) / avgNetSalesSameElapsedTime` over the same 7-day window; `0` when the denominator is `0`. |
| `avgPrepMinutesToday` | Mean of (ready − prep-started) for today's completed tickets. |
| `p90PrepMinutesRolling` | Nearest-rank P90 over tickets completed in the last 60 minutes. |
| `targetPrepMinutes` | Restaurant configuration; default `Dashboard:TargetPrepMinutes` = 15. |
| `salesTargetToday` | Restaurant configuration value; `null` when unset. |
| `lateOrdersCount` / `atRiskOrdersCount` | Active orders over target prep / over 80% of target. |
| Currency | Restaurant configuration. |
| Alerts | Computed statelessly per snapshot; `id = "{category}:{entityId}"`; severities `info\|warning\|critical`; categories `kitchen_late`, `pass_dwell`, `table_overstay`, `item_86d`, `system_degraded`; thresholds from options with defaults. |
| Divide-by-zero | Every ratio returns `0.0` when its denominator is `0`. |

---

## 7. Cross-Service Communication

- **Invalidation sources** (consumers in Ordering, debounced 2 s): `OrderCreated`, `OrderCompleted`, `OrderCancelled`, `KitchenOrderPrepStarted/Ready/Bumped/Cancelled/Recalled`, `TableStatusChanged`, `RestaurantConfigurationChanged`, `MenuItemChanged` (for `item_86d` alerts).
- Phase 1 verifies which of these Ordering already consumes (the Kitchen events may not be consumed yet) and adds missing consumers/queues.
- **Hub**: after invalidation, publish `DashboardChanged` to `OrdersHub.StatsGroup(restaurantId)`. Browsers also poll every 30 s while disconnected (the Web plan implements this).
- `OrdersHub` is gated by `OrdersLiveView`; when disabled the dashboard relies on polling only.

---

## 8. Security Guardrails

> [!CAUTION]
> The endpoint exposes sensitive financial telemetry. Redaction must happen after cache read, on every request.

| Risk | Mitigation |
|---|---|
| Cross-tenant exposure | Route restaurant must be in JWT claims; every SQL query filtered by it; downstream calls forward the token (Catalog/Kitchen enforce their own tenant checks); cache key per restaurant. |
| Revenue shown to unauthorized roles | `payments:view_reports` redaction applied to the cached raw snapshot; test per role. |
| DoS via polling | Absolute TTL + single-flight + debounced invalidation. |
| Downstream outage | Per-section degradation; never `5xx` because one dependency is down. |
| Unauthenticated access | `RequirePermission` on the endpoint. |

---

## 9. Development Phases

### Phase 1 — Source Inventory, Contracts & Metric Definitions
**Status**: ✅ Done (2026-10-31)

**Deliverables**:
- [x] Inventory existing endpoints/events: Catalog tables/reservations/waitlist/analytics/restaurant config (time zone, currency, target), Kitchen ticket timings/stations; list gaps and the minimal read endpoints to add.
- [x] Verify which events Ordering already consumes; list new consumers needed.
- [x] Seed `dashboard:view` in `Identity.API/Data/DataSeeder.cs` and update `docs/architecture/permissions.md`.
- [x] Create DTOs per §6.2 (+ `DashboardMetaDto`) and the query/validator (non-empty GUID).
- [x] Record any metric definition changes in §6.3.

**Phase 1 Findings & Inventory**:
1. **Catalog Inventory & Gaps**:
   - `GET /v1/restaurants/{id}` returns `RestaurantDto` (`Currency`, `TimeZone`, `EstimatedTurnoverMinutes`). `SalesTargetToday` does not exist on `Restaurant`; will default to `null` or options configuration.
   - `GET /v1/restaurants/{restaurantId}/tables` exists (paginated, `PageSize=10`).
   - `GET /v1/restaurants/{restaurantId}/walk-in-queue` exists (`IEnumerable<WalkInQueue>`).
   - `GET /v1/restaurants/{restaurantId}/reservations` exists (paginated, `PageSize=10`).
   - *Phase 2 Catalog Read Endpoint Gap*: A dedicated `GET /v1/restaurants/{restaurantId}/dashboard/floor-pulse` is needed in Catalog to avoid round-tripping multiple paginated endpoints.
2. **Kitchen Inventory & Gaps**:
   - `GET /v1/kitchen/queue` returns paginated `KitchenTicketDto` (in `New` or `InProgress` status).
   - *Phase 2 Kitchen Read Endpoint Gap*: Kitchen lacks any station aggregation or velocity calculation. A dedicated `GET /v1/kitchen/restaurants/{restaurantId}/velocity` is needed in Kitchen.
3. **Ordering Event Consumers Inventory**:
   - Currently consumed: `KitchenOrderAcceptedIntegrationEvent`, `KitchenOrderBumpedIntegrationEvent`, `KitchenOrderCancelledIntegrationEvent`, `KitchenOrderCancelRejectedIntegrationEvent`, `KitchenOrderPrepStartedIntegrationEvent`, `KitchenOrderReadyIntegrationEvent`, `KitchenOrderRecalledIntegrationEvent`, `RestaurantConfigurationChangedIntegrationEvent`, `BasketCheckoutEvent`.
   - Missing consumers for Phase 4 invalidation: `TableStatusChangedIntegrationEvent`, `MenuItemChangedIntegrationEvent` (Catalog events), plus hooks for Ordering's own order lifecycle events (`OrderCreatedIntegrationEvent`, `OrderCompletedIntegrationEvent`, `OrderCancelledIntegrationEvent`).
4. **Contracts & Validation**:
   - All DTOs created under `Ordering.Application.Dashboard.Dtos`.
   - `GetRestaurantDashboardLiveSummaryQuery` and `GetRestaurantDashboardLiveSummaryValidator` created and verified with passing unit tests (`GetRestaurantDashboardLiveSummaryValidatorTests`).
   - `dashboard:view` permission seeded for `SuperAdmin`, `RestaurantAdmin`, `Manager`, `KitchenManager`, `Waiter`, `Cashier`, `Host`.

**Exit criteria**: `dotnet build Services/Ordering/Ordering.Application` — 0 errors, 0 warnings. (Passed ✅)

---

### Phase 2 — Aggregator Service, Cross-Service Readers & Redis Cache
**Status**: ✅ Done (2026-11-03)

**Deliverables**:
- [x] `IDashboardSummaryService` + `DashboardSummaryService` (compiled EF queries for orders/sales/open checks; composition with readers).
- [x] `CatalogDashboardReader` / `KitchenDashboardReader` with Polly; per-section degradation into `meta.sections`.
- [x] Add required read endpoints to Catalog/Kitchen (if the inventory found gaps) with their own permission/tenant checks.
- [x] `DashboardSnapshotCache`: absolute TTL 15 s, single-flight, raw snapshot, revenue redaction layer.
- [x] SQL indexes: verify actual column names then add a SQL Server migration for the filter/sort columns (candidates: `Orders(RestaurantId, Status, CreatedAt)`, `OrderItems(OrderId)`).
- [x] Unit tests: P90, averages, occupancy, growth, timezone/hour bucketing, empty-data (no `DivideByZeroException`), redaction.

**Exit criteria**: `dotnet test Services/Ordering/Ordering.Application.Tests` passes all dashboard tests. (Passed ✅)

---

### Phase 3 — Carter Endpoint, Permissions & YARP Mapping
**Status**: ✅ Done (2026-11-03)

**Deliverables**:
- [x] `GetRestaurantDashboardLiveSummaryEndpoint` (`ICarterModule`) at the §4 route, `.RequirePermission("dashboard:view")`, restaurant-claim check, OpenAPI metadata.
- [x] Verify the YARP route `/ordering-api/{**}` forwards the new path (no extra route needed unless the catch-all is missing).
- [x] Redaction wired per caller permissions.

**Exit criteria** (from the running stack, with a valid token):
`curl http://localhost:6004/ordering-api/v1/restaurants/{id}/dashboard/live-summary` → `200` with the documented shape; same call without `payments:view_reports` has revenue fields `null`. (Passed ✅)

---

### Phase 4 — Event-Driven Invalidation & `OrdersHub` Notification
**Status**: ✅ Done (2026-11-03)

**Deliverables**:
- [x] Consumers for the §7 events invalidate the cache (debounced 2 s).
- [x] `IOrdersHubClient.DashboardChanged` + broadcast to `StatsGroup(restaurantId)`.
- [x] Document the `OrdersLiveView` flag dependency and polling fallback.
- [x] Tests: event → single invalidation per burst → hub message.

**Exit criteria**: placing an order invalidates the cache once and delivers `DashboardChanged` to a connected test client. (Passed ✅)

---

### Phase 5 — Integration Tests, Load Smoke & Phase Guard
**Status**: ✅ Done (2026-11-03)

**Deliverables**:
- [x] `Ordering.API.Tests`: tenant isolation, `401`, `403` (wrong restaurant, missing permission), revenue redaction per role (Manager vs Waiter/Cashier/Host), degradation when Catalog/Kitchen are down.
- [x] Load smoke (e.g. 50 concurrent requests on a warm cache; target p95 < 300 ms; cold-cache requests produce one computation).
- [x] `pwsh ./scripts/phase-guard.ps1 -PhaseName "Restaurant Dashboard Summary" -Quick`.

**Exit criteria**: `phase-guard.ps1` exits `0`. (Passed ✅)

---

## 10. Technical Considerations

- **Zero-data safety**: opening time (no orders) must return zeros and empty lists, never exceptions.
- **Clock**: use `IClock` everywhere for testability.
- **Contract coupling**: the Web plan consumes these field names; change both plans together.

---

## Changelog

### v1.2 (2026-10-31) — Phase 1 completed
- Executed Catalog, Kitchen, and Ordering event/endpoint inventory; identified minimal read endpoints for Phase 2 (`/dashboard/floor-pulse` in Catalog, `/velocity` in Kitchen).
- Seeded `dashboard:view` permission in `Identity.API` for `SuperAdmin`, `RestaurantAdmin`, `Manager`, `KitchenManager`, `Waiter`, `Cashier`, `Host`; updated `permissions.md`.
- Implemented all dashboard DTOs, `GetRestaurantDashboardLiveSummaryQuery`, and `GetRestaurantDashboardLiveSummaryValidator` in `Ordering.Application`.
- Added unit tests for query validation with 100% pass rate. Verified `Ordering.Application` compilation (0 errors).

### v1.1 (2026-10-31) — review fixes
- Chose the aggregator design; added cross-service readers with per-section degradation (`meta.sections`).
- Fixed route (`/v1/restaurants/{id}/…`), claims check, and replaced the non-existent `RestaurantStaff` policy with `dashboard:view` + `payments:view_reports` redaction.
- Cache stores the raw snapshot; redaction after read; absolute TTL, single-flight, debounced invalidation.
- Replaced invented events with real ones; reused `OrdersHub` groups and added `DashboardChanged` (no revenue payload); documented `OrdersLiveView` dependency.
- Added normative metric definitions (time zone, averages, P90, growth, alerts); added `openChecks`/`waitlist`, currency/time zone; deferred widgets without contracts.
- Added index migration task, role-based redaction tests, load smoke, working directory for phase guard.

### v1.0 (2026-10-31) — Initial Draft
- Created plan (5 phases).
