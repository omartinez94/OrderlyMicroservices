# Ordering Reservation / Booking — Implementation Plan

> Scope: move `Reservation` + `WalkInQueue` + `Table` + `MergedTable` aggregates from `Catalog.API` to `Ordering.API`; provide a unified **Booking service API** surface that consolidates reservation + walk-in queue + table-assignment flows under `/api/v1/booking/*`. Re-host the three Catalog Hangfire jobs (`ReservationReminderJob`, `ReservationNoShowJob`, `WalkInNoShowJob`) in Ordering; re-issue `ReservationReminderDueIntegrationEvent` + `TableStatusChangedIntegrationEvent` from the new publisher home. **BulkOrderUpload stays in Catalog** per the original `CATALOG_SERVICE_PLAN.md §7.6.0` decision. Database engine switch: source `catalogdb` (Postgres) → target `orderdb` (MSSQL).

---

## Status

> **Plan version**: `v1.0` (2026-07-18) — `MINOR` increments per phase completion; `MAJOR` is reserved for breaking restructures of the plan itself.
> **Current state**: ⏸ Not started (Phase 1 is the next action).

| Phase | Name | Status |
|:-----:|---|:-----:|
| 1 | Aggregate models + state machines | ⏸ Pending |
| 2 | Database migration (Postgres catalogdb → MSSQL orderdb) | 🔒 Blocked (by Phase 1) |
| 3 | API endpoints (Carter; move + Booking namespace) | 🔒 Blocked (by Phase 2) |
| 4 | Hangfire jobs re-host (3 jobs) | 🔒 Blocked (by Phase 3) |
| 5 | Integration event publishers move | 🔒 Blocked (by Phase 4) |
| 6 | Booking API unification + Carter module | 🔒 Blocked (by Phase 5) |
| 7 | Dual-write window + cutover | 🔒 Blocked (by Phase 6) |
| 8 | Catalog cleanup (delete source) | 🔒 Blocked (by Phase 7) |

> **Legend**: ✅ Done · 🚧 In progress · ⏸ Pending · 🔒 Blocked

> **Commit messages**: Conventional Commits (`feat:`, `docs:`, `chore:`, `test:`, `fix:`). Short subject, ≤50 chars, imperative mood, no trailing period.

> **Update rule**: **on every phase completion, the plan MUST be updated in the same pair of commits as the phase work (a code commit + a plan commit — see [How to use this plan](#how-to-use-this-plan)).** The plan is the source of truth for what was decided and what shipped.

---

## 0. Skill & documentation conventions

### 0.1 Skill mandate — `csharp-developer`
> **All implementation work on this plan MUST invoke the `csharp-developer` skill** (base directory `.claude/skills/csharp-developer`, invoked as `/csharp-expert` in Claude Code). The skill is the source of truth for C# 12+ / .NET 10 idiom, async patterns, EF Core + Marten usage, ASP.NET Core + Carter, MassTransit outbox patterns, xUnit + Testcontainers test scaffolding, and the project's "MUST DO / MUST NOT DO" guard rails (nullable enabled, primary constructors, async/await with `CancellationToken`, `Result<T>` for error paths, no blocking calls, DTO mapping for API responses).

Companion reference files (loaded on demand per the skill's table): `modern-csharp.md`, `aspnet-core.md`, `entity-framework.md`, `performance.md` (only if a phase lands a perf-sensitive hot path — Phase 4's Hangfire tick handler may qualify).

> **EF Core checkpoint:** after any code change that mutates the schema (Phase 2 schema conversion; Phase 3 endpoint DTOs adding FK columns; Phase 8 Catalog deletions), the implementer runs `dotnet ef migrations add <Name>` per the project's `--startup-project` rule (Ordering: `--startup-project Ordering.API`; see memory `ordering-ef-migration-startup-project.md` for dev-DB passwords + ports). Reviews the generated migration for unintended drops. **Phase 2's MSSQL migration is hand-authored** — EF cannot infer the cross-engine schema conversion from Postgres `text`/`jsonb`/`uuid`/`timestamp` to MSSQL `nvarchar(MAX)`/`nvarchar(MAX)`/`uniqueidentifier`/`datetime2`.

The skill is *additional* to whatever other skills are relevant (e.g. `csharp-xunit` for test scaffolding; `dotnet-best-practices` for the project-wide guard rails; `api-design-principles` for the Booking API endpoint shape). It is **not** a substitute for the plan; the plan wins where they disagree.

### 0.2 Code-quality guard rails

This plan **inherits the project-wide guard rails from `CATALOG_SERVICE_PLAN.md §0.3` verbatim** (the source-side guard rails are authoritative for the aggregates being moved). The Ordering-side plan (`ORDER_ACTIVITY_PLAN.md`) provides additional patterns the new aggregates must respect (e.g. `OrderActivity` for state transitions; `CorrelationContext` ambient).

Reservation-specific overrides layered on top of the catalog-copied bullets:

- **Aggregate base = `AuditableEntity<TId>`** (mirrors the source-side base class). NOT `Aggregate<TId>` — Reservation/WalkInQueue/Table don't dispatch domain events today and refactoring to event-sourced aggregates is out of scope.
- **State machines use guarded methods, not raw status assignment.** Every transition is a method on the aggregate (`Confirm()`, `Cancel()`, `Seat()`, `NoShow()`); illegal transitions throw `InvalidReservationStateTransitionException`. This mirrors the `Order` aggregate pattern (per `KITCHEN_FOLLOWUP_PLAN.md §F`).
- **`AuditableEntityInterceptor` stamps `CreatedBy`/`LastModifiedBy` on save.** Already wired in `Ordering.Infrastructure/Interceptors/`; the new aggregates pick it up automatically.
- **Outbox-mediated publishes.** Every integration event is staged via `IOutboxPublisher` after `SaveChangesAsync` and drained by `OrderingOutboxDispatcher`. The existing `OrderingOutboxMultiReplicaTests` + `OrderingOutboxDeadLetterTests` cover the dispatcher contract.
- **No `FromSqlRaw` in tenant-scoped queries.** Per `MULTITENANCY_ROLLOUT_PLAN.md §0.2`.
- **`ICurrentRestaurantProvider` is the only allowed source of `Guid RestaurantId` in a request scope.** Per `MULTITENANCY_ROLLOUT_PLAN.md §0.2`. The new aggregates do NOT implement `ITenantEntity` directly in this plan — `MULTITENANCY_ROLLOUT_PLAN.md Phase 2` owns the Ordering tenant-scope adoption (see Tech Decision #10).

#### 0.2.1 Global usings (project-specific)

After Phase 1, every new aggregate file's namespace + the Ordering global usings gain:

```csharp
// Ordering.Domain
global using Ordering.Domain.Enums;       // for ReservationStatus, WalkInQueueStatus, TableStatus

// Ordering.Application
global using Ordering.Application.Abstractions;  // for ICurrentRestaurantProvider when ITenantEntity adoption lands

// Ordering.Infrastructure
global using Ordering.Infrastructure.Interceptors;
```

The "2+ files" promotion rule from CATALOG_SERVICE_PLAN §0.3.12 applies.

---

## 1. Context

The Orderly platform's reservation + walk-in + table-assignment flows live in `Catalog.API` today. They were placed there during the initial green-field design because the menu + tables are co-located in Catalog's restaurant-management context. Six months in, this ownership is causing friction:

- **Reservation aggregate has a cross-service dependency on Order placement** (`Reservation.TableId` is consumed by `Order.TableId` in Ordering) but lives in Catalog. Today the join is `Guid`-only with no FK constraint; cross-service writes happen via `TableStatusChangedIntegrationEvent` (Catalog → Ordering consumer).
- **Walk-in queue has a real-time dependency on Table availability** — same cross-service pattern.
- **Three Hangfire jobs in Catalog** (`ReservationReminderJob`, `ReservationNoShowJob`, `WalkInNoShowJob`) issue `ReservationReminderDueIntegrationEvent` for the future Notification consumer. They're hosted in Catalog's Hangfire schema (Postgres).
- **Two of Catalog's integration events** are reservation-domain events (`ReservationReminderDueIntegrationEvent`, `TableStatusChangedIntegrationEvent`) but they're emitted by the menu-management service, which surprises new contributors.

The natural ownership for this domain is **Ordering** — the service that already places orders, holds tables during order creation, and consumes `TableStatusChangedIntegrationEvent` to coordinate reservation + order placement. The Booking API consolidation unifies the three flows under a single endpoint family (`/api/v1/booking/*`).

Reference plans:
- `CATALOG_SERVICE_PLAN.md` — closed 2026-07-18; Phase 6.0 + 6.1 relocated here.
- `ORDER_ACTIVITY_PLAN.md` — closed 2026-07-16; the `OrderActivity` pattern is the model for state-transition history on Reservation.
- `KITCHEN_INTEGRATION_PLAN.md` — closed 2026-07-16; the `Order` aggregate's guarded state-transition methods are the model for `Reservation` / `WalkInQueue` state machines.
- `MULTITENANCY_ROLLOUT_PLAN.md` — not started; Phase 2 covers Ordering's `ITenantEntity` adoption. The new aggregates here must NOT block on that — see Tech Decision #10.

---

## 2. Goal

1. **Reservation / WalkInQueue / Table / MergedTable aggregates live in Ordering.** The 19 source-side endpoints (Reservations 6 + WalkInQueues 5 + Tables 5 + MergedTables 3) are moved and exposed under `/api/v1/booking/*`.
2. **Booking service API is a unified surface.** A single Carter module owns reservation + walk-in + table-assignment routes; `POST /api/v1/booking/reservations`, `POST /api/v1/booking/walk-ins`, `GET /api/v1/booking/tables`, etc.
3. **Hangfire jobs re-host in Ordering.** A new `hangfire` schema in `orderdb` (or a separate Hangfire database — see Tech Decision #5) hosts the three migrated jobs.
4. **Integration events re-issued from Ordering.** `ReservationReminderDueIntegrationEvent` + `TableStatusChangedIntegrationEvent` are published by Ordering; consumer compatibility is preserved (identical payload shape, no `SchemaVersion` bump).
5. **Database migration lands cleanly.** Cross-engine Postgres → MSSQL schema conversion + data backfill in a single offline cutover window (or zero-downtime dual-write window — see Tech Decision #4).
6. **Catalog source deletion.** After Phase 7 cutover, Phase 8 deletes the 19 source endpoints + the 4 model files + the 3 Hangfire jobs + drops the 4 source tables from `catalogdb`.

By the end of Phase 8:
- `Catalog.API` no longer exposes `/reservations`, `/walk-in-queues`, `/tables`, `/merged-tables` endpoints.
- `Ordering.API` exposes `/api/v1/booking/*` (19 endpoints).
- `ReservationReminderDueIntegrationEvent` + `TableStatusChangedIntegrationEvent` are emitted only by Ordering.
- The 4 tables (`reservations`, `walk_in_queues`, `tables`, `merged_tables`) exist only in `orderdb`.

---

## 3. Out of scope

- **Multi-tenancy filter adoption on the new aggregates.** `MULTITENANCY_ROLLOUT_PLAN.md Phase 2` owns the Ordering `ITenantEntity` adoption. The new aggregates carry `RestaurantId` (already do today) but do NOT implement `ITenantEntity` in this plan. Documented as a hand-off; tracked in §10.1.
- **Deposit / prepayment handling** on reservations. Tracked as a follow-up plan after Phase 8 lands.
- **SMS / email reminders for reservations** — this is Notification v1's job (`NOTIFICATION_SERVICE_PLAN.md` is the destination for `ReservationReminderDueIntegrationEvent`).
- **Floor plan drag-drop UI** — frontend work is out of repo per existing plans.
- **Per-restaurant booking policies** (lead time, party-size caps, cancellation grace). Out of scope; future plan.
- **BulkOrderUpload move.** Stays in Catalog per user choice + original §7.6.0 decision.
- **CustomerFeedback + NotificationLog move.** Tracked in `NOTIFICATION_SERVICE_PLAN.md` (relocated from CATALOG_SERVICE_PLAN §6.1).
- **Frontend migration to `/ordering-api/booking/*` paths.** Frontend is in a separate project; YARP gateway preserves the old `/catalog-api/reservations/*` paths via a one-time rewrite rule (Phase 7).

---

## 4. Tech decisions

| # | Decision | Choice | Reason |
|:---|:---|:---|:---|
| 1 | Scope | **Migration with Booking API** — move aggregates + unified endpoint surface | User choice (2026-07-18); preserves existing behavior, consolidates the API. |
| 2 | Tables owner | **Move with Reservation to Ordering** | User choice; consolidates reservation ↔ table in the same DbContext; eliminates cross-service `TableStatusChanged` race. |
| 3 | BulkOrderUpload | **Stays in Catalog** | User choice + original `CATALOG_SERVICE_PLAN.md §7.6.0` decision. |
| 4 | DB engine migration strategy | **Pure migration with offline cutover window** | Cross-engine Postgres → MSSQL has subtle differences (`text`/`jsonb`/`uuid`/`timestamp` ↔ `nvarchar(MAX)`/`nvarchar(MAX)`/`uniqueidentifier`/`datetime2`); a dual-write window adds correctness risk without proportional benefit at the target scale. The offline window is 30-60 min; managed via a feature flag flip. |
| 5 | Hangfire storage | **New Hangfire schema in `orderdb`** | Colocates reservation-domain state with its scheduling; matches the Catalog pattern (`hangfire` schema in `catalogdb`). One schema per service is the project convention. |
| 6 | YARP route migration | **Dual-route window (7 days)** | Gateway serves both `/catalog-api/reservations/*` (rewrite to `/ordering-api/booking/reservations/*`) and the new `/ordering-api/booking/*` paths during the window. Catalog source deletions land at window end. Frontend migrates on its own schedule. |
| 7 | Frontend integration | **Frontend migrates to `/ordering-api/booking/*` directly** | Backend does NOT proxy (no backward-compat shim in Catalog); the gateway rewrite covers the migration window. Frontend is out-of-repo per existing plans. |
| 8 | Integration event payload | **Identical payload shape (no `SchemaVersion` bump)** | Consumer compatibility: Notification v1's `ReservationReminderDueIntegrationEvent` consumer + Ordering's `TableStatusChangedIntegrationEvent` consumer continue to work unchanged. Avoids a wire-shape change for a service-side move. |
| 9 | State-machine enhancements | **Pure migration; no new domain behavior** | Booking API = unified endpoint surface, not new state transitions. Out-of-scope items (deposits, policies) become a follow-up plan after Phase 8. |
| 10 | Multi-tenancy adoption | **Defer to `MULTITENANCY_ROLLOUT_PLAN.md Phase 2`** | Decouples the moves; RESERVATION_PLAN ships regardless of multitenancy progress. The new aggregates carry `RestaurantId` (already do today); the filter adoption is a separate PR. Documented in §10.1. |
| 11 | Aggregate base | **`AuditableEntity<TId>`** (matches source-side base) | Mirrors the source-side model files; preserves audit columns (`CreatedBy`, `CreatedAt`, `LastModifiedBy`, `LastModifiedAt`) that are already populated by `AuditableEntityInterceptor`. Refactoring to event-sourced `Aggregate<TId>` is out of scope. |
| 12 | Outbox publisher | **Move publishers to Ordering's outbox** | Colocated with the domain that owns the data; matches the existing Ordering outbox pattern (`OrderingOutboxPublisher` + `OrderingOutboxDispatcher`). |

---

## 5. Folder layout

The plan touches files across two services (Catalog source deletions + Ordering additions). No new top-level directories.

```
orderly-microservices/
├── Services/
│   ├── Catalog/Catalog.API/                          [Phase 8] source deletion
│   │   ├── Models/{Reservation,WalkInQueue,Table,MergedTable}.cs   DELETE
│   │   ├── Features/Reservations/                    DELETE all 12 files
│   │   ├── Features/WalkInQueues/                    DELETE all 10 files
│   │   ├── Features/Tables/                          DELETE all 10 files
│   │   ├── Features/MergedTables/                    DELETE all 6 files
│   │   ├── Scheduling/{ReservationReminderJob,ReservationNoShowJob,WalkInNoShowJob}.cs  DELETE
│   │   ├── Exceptions/{ReservationNotFoundException,WalkInQueueNotFoundException,TableNotFoundException,MergedTableNotFoundException}.cs  DELETE
│   │   ├── Data/Migrations/                          [Phase 8] drop tables in Down() of new migration
│   │   └── Program.cs                                [Phase 8] remove Carter module + Hangfire job registrations
│   └── Ordering/
│       ├── Ordering.Domain/
│       │   ├── Models/Reservation/                   [Phase 1] NEW
│       │   │   ├── Reservation.cs                    [Phase 1] move + state methods
│       │   │   ├── ReservationStatus.cs              [Phase 1] enum
│       │   │   └── Exceptions/InvalidReservationStateTransitionException.cs  [Phase 1]
│       │   ├── Models/WalkInQueue/                   [Phase 1] NEW
│       │   │   ├── WalkInQueue.cs                    [Phase 1] move + state methods
│       │   │   ├── WalkInQueueStatus.cs              [Phase 1] enum
│       │   │   └── Exceptions/InvalidWalkInStateTransitionException.cs  [Phase 1]
│       │   ├── Models/Table/                         [Phase 1] NEW
│       │   │   ├── Table.cs                          [Phase 1] move
│       │   │   └── TableStatus.cs                    [Phase 1] enum
│       │   └── Models/MergedTable/                   [Phase 1] NEW
│       │       └── MergedTable.cs                    [Phase 1] move
│       ├── Ordering.Application/
│       │   ├── Features/Booking/                     [Phase 3 + 6] NEW
│       │   │   ├── Reservations/                     [Phase 3] move 6 endpoints from Catalog
│       │   │   │   ├── CreateReservation/
│       │   │   │   ├── GetReservations/
│       │   │   │   ├── GetReservationById/
│       │   │   │   ├── ConfirmReservation/
│       │   │   │   ├── CancelReservation/
│       │   │   │   └── SeatReservation/
│       │   │   ├── WalkIns/                          [Phase 3] move 5 endpoints from Catalog
│       │   │   │   ├── AddToWalkInQueue/
│       │   │   │   ├── GetWalkInQueue/
│       │   │   │   ├── NotifyWalkInCustomer/
│       │   │   │   ├── RemoveFromQueue/
│       │   │   │   └── SeatWalkInCustomer/
│       │   │   ├── Tables/                           [Phase 3] move 5 endpoints from Catalog
│       │   │   │   ├── CreateTable/
│       │   │   │   ├── DeleteTable/
│       │   │   │   ├── GetTableById/
│       │   │   │   ├── GetTables/
│       │   │   │   └── UpdateTable/
│       │   │   ├── MergedTables/                     [Phase 3] move 3 endpoints from Catalog
│       │   │   │   ├── MergeTables/
│       │   │   │   ├── SplitTables/
│       │   │   │   └── GetMergedTables/
│       │   │   └── BookingModule.cs                  [Phase 6] Carter module registration
│       │   └── Common/Mappings/                      [Phase 1] NEW
│       │       └── ReservationMappings.cs            [Phase 1] Mapster config
│       └── Ordering.Infrastructure/
│           ├── Data/Configurations/                  [Phase 1] NEW
│           │   ├── ReservationConfiguration.cs
│           │   ├── WalkInQueueConfiguration.cs
│           │   ├── TableConfiguration.cs
│           │   └── MergedTableConfiguration.cs
│           ├── Data/Migrations/                      [Phase 2] NEW
│           │   ├── 20260718xxxxxx_AddReservationsToOrdering.cs   [Phase 2] hand-authored schema conversion
│           │   └── 20260718xxxxxx_BackfillReservationData.cs    [Phase 2] backfill from catalogdb
│           ├── Scheduling/                           [Phase 4] NEW
│           │   ├── ReservationReminderJob.cs
│           │   ├── ReservationNoShowJob.cs
│           │   └── WalkInNoShowJob.cs
│           ├── Hangfire/                             [Phase 4] NEW
│           │   └── OrderingHangfireSchema.cs         [Phase 4] hangfire schema setup in orderdb
│           └── Messaging/                            [Phase 5] NEW
│               ├── Publishers/ReservationReminderDuePublisher.cs
│               └── Publishers/TableStatusChangedPublisher.cs
├── ApiGateway/YarpApiGateway/
│   └── appsettings.json                              [Phase 7] add /catalog-api/{reservations,walk-in-queues,tables,merged-tables}/** rewrite to /ordering-api/booking/**
└── docs/architecture/current-architecture.md         [every phase] Doc-update scope per §9
```

---

## 6. Specification

The contracts the implementer acts on. One subsection per group of related items.

### 6.1 Aggregate models + state machines

**Reservation** — moves from `Catalog.API.Models.Reservation : AuditableEntity<Guid>` to `Ordering.Domain.Models.Reservation.Reservation : AuditableEntity<Guid>`. Columns preserved: `Id`, `RestaurantId`, `CustomerEmail`, `CustomerName`, `CustomerPhone`, `Notes`, `PartySize`, `ReminderSent`, `RequiresApproval`, `ReservationDate` (NodaTime `LocalDate`), `ReservationNumber`, `ReservationTime` (NodaTime `LocalTime`), `SpecialRequests`, `Status`, `ApprovedAt`, `ApprovedByUserId`, `CancelledAt`, `CompletedAt`, `ConfirmedAt`, `CreatedByUserId`, `ReminderSentAt`, `SeatedAt`, `TableId`, plus audit columns from `AuditableEntity` (`CreatedAt`, `CreatedBy`, `LastModifiedAt`, `LastModifiedBy`).

State machine (guarded methods on the aggregate):

| Current state | Method | Allowed next states |
|---|---|---|
| `Pending` | `Confirm()` | `Confirmed` |
| `Pending` | `Cancel()` | `Cancelled` |
| `Confirmed` | `Seat(tableId)` | `Seated` |
| `Confirmed` | `Cancel()` | `Cancelled` |
| `Confirmed` | `NoShow()` | `NoShow` |
| `Seated` | `Complete()` | `Completed` |

Illegal transitions throw `InvalidReservationStateTransitionException`.

**WalkInQueue** — moves from `Catalog.API.Models.WalkInQueue : AuditableEntity<int>` to `Ordering.Domain.Models.WalkInQueue.WalkInQueue : AuditableEntity<int>`. Columns preserved (per `Models/WalkInQueue.cs`).

State machine:

| Current state | Method | Allowed next states |
|---|---|---|
| `Waiting` | `Notify(estimatedWaitMinutes)` | `Notified` |
| `Waiting` | `Seat(tableId)` | `Seated` |
| `Waiting` | `Leave()` | `Cancelled` |
| `Notified` | `Seat(tableId)` | `Seated` |
| `Notified` | `NoShow()` | `NoShow` |
| `Notified` | `Leave()` | `Cancelled` |

**Table** — moves from `Catalog.API.Models.Table : AuditableEntity<Guid>` to `Ordering.Domain.Models.Table.Table : AuditableEntity<Guid>`. No state machine (Table is configuration-style data; status transitions are driven by Reservation/WalkInQueue events).

**MergedTable** — moves from `Catalog.API.Models.MergedTable : Entity<Guid>` to `Ordering.Domain.Models.MergedTable.MergedTable : Entity<Guid>`. Columns: `Id`, `ParentTableId`, `ChildTableId`, `IsActive`, `MergedAt`, `SplitAt`.

### 6.2 Database migration (Postgres catalogdb → MSSQL orderdb)

**Schema conversion** — hand-authored `migrationBuilder.Sql(...)` per column type:

| Postgres (`catalogdb`) | MSSQL (`orderdb`) |
|---|---|
| `uuid` | `uniqueidentifier` |
| `text` | `nvarchar(MAX)` |
| `jsonb` (none on these tables) | n/a |
| `timestamp with time zone` | `datetime2(7)` |
| `date` (NodaTime `LocalDate`) | `date` |
| `time without time zone` (NodaTime `LocalTime`) | `time(7)` |
| `boolean` | `bit` |
| `integer` | `int` |
| `bigint` | `bigint` |

**Backfill** — second migration `BackfillReservationData` reads from `catalogdb` (via `OpenConnection` on a separate connection string) and `INSERT`s into the new `orderdb` tables. Run via a dedicated console job (or a manual ops script) during the Phase 7 cutover window.

**Cutover sequence** (Phase 7):
1. Feature flag `Ordering:AcceptBookingWrites=true` on dev; existing `/catalog-api/*` writes continue.
2. Backfill job runs; data parity verified by row counts + sample-comparison script.
3. `Ordering:AcceptBookingWrites=true` on staging + prod; both backends accept writes for 7 days.
4. Frontend migrates to `/ordering-api/booking/*` during the window.
5. Day 7: flip `Catalog:BookingEndpointsEnabled=false`; Catalog endpoints return 410 Gone.
6. Day 8+: Phase 8 deletes source models + drops source tables.

### 6.3 API endpoints (Carter; Booking namespace)

19 endpoints move from Catalog to Ordering under a single Carter module. Endpoint paths change:

| Source (Catalog) | Target (Ordering) |
|---|---|
| `POST /api/v1/reservations` | `POST /api/v1/booking/reservations` |
| `GET /api/v1/reservations` | `GET /api/v1/booking/reservations` |
| `GET /api/v1/reservations/{id}` | `GET /api/v1/booking/reservations/{id}` |
| `POST /api/v1/reservations/{id}/confirm` | `POST /api/v1/booking/reservations/{id}/confirm` |
| `POST /api/v1/reservations/{id}/cancel` | `POST /api/v1/booking/reservations/{id}/cancel` |
| `POST /api/v1/reservations/{id}/seat` | `POST /api/v1/booking/reservations/{id}/seat` |
| `POST /api/v1/walk-in-queues` | `POST /api/v1/booking/walk-ins` |
| `GET /api/v1/walk-in-queues` | `GET /api/v1/booking/walk-ins` |
| `POST /api/v1/walk-in-queues/{id}/notify` | `POST /api/v1/booking/walk-ins/{id}/notify` |
| `POST /api/v1/walk-in-queues/{id}/seat` | `POST /api/v1/booking/walk-ins/{id}/seat` |
| `DELETE /api/v1/walk-in-queues/{id}` | `DELETE /api/v1/booking/walk-ins/{id}` |
| `POST /api/v1/tables` | `POST /api/v1/booking/tables` |
| `GET /api/v1/tables` | `GET /api/v1/booking/tables` |
| `GET /api/v1/tables/{id}` | `GET /api/v1/booking/tables/{id}` |
| `PUT /api/v1/tables/{id}` | `PUT /api/v1/booking/tables/{id}` |
| `DELETE /api/v1/tables/{id}` | `DELETE /api/v1/booking/tables/{id}` |
| `POST /api/v1/merged-tables/merge` | `POST /api/v1/booking/merged-tables/merge` |
| `POST /api/v1/merged-tables/split` | `POST /api/v1/booking/merged-tables/split` |
| `GET /api/v1/merged-tables` | `GET /api/v1/booking/merged-tables` |

Permissions (mirrors Catalog):

| Endpoint | Permission |
|---|---|
| All reservation endpoints | `reservation:create` / `reservation:read` / `reservation:confirm` / `reservation:cancel` / `reservation:seat` |
| All walk-in endpoints | `walkin:create` / `walkin:read` / `walkin:notify` / `walkin:seat` / `walkin:remove` |
| All table endpoints | `table:create` / `table:read` / `table:update` / `table:delete` |
| All merged-table endpoints | `table:merge` / `table:split` |

### 6.4 Hangfire jobs re-host (3 jobs)

`Ordering.Hangfire` schema in `orderdb` (MSSQL). Three jobs migrate:

| Job | Cron | Source file | Target file |
|---|---|---|---|
| `ReservationReminderJob` | Every 5 min | `Catalog.API/Scheduling/ReservationReminderJob.cs` | `Ordering.Infrastructure/Scheduling/ReservationReminderJob.cs` |
| `ReservationNoShowJob` | Every 1 min | `Catalog.API/Scheduling/ReservationNoShowJob.cs` | `Ordering.Infrastructure/Scheduling/ReservationNoShowJob.cs` |
| `WalkInNoShowJob` | Every 1 min | `Catalog.API/Scheduling/WalkInNoShowJob.cs` | `Ordering.Infrastructure/Scheduling/WalkInNoShowJob.cs` |

Gated by `FeatureManagement__OrderingScheduledJobs` (default `false`); same self-gating pattern as `CatalogScheduledJobs`. `Ordering:Hangfire` config section with `[Range]` validation for `MaxRowsPerTick` and `WorkerCount`. Per-job cron expressions configurable.

`HangfireAdminOnlyFilter` (`IDashboardAuthorizationFilter`) restricts the dashboard to JWT `Admin` / `Manager` role claims. Dashboard mounted at `/ordering-api/hangfire`.

### 6.5 Integration event publisher move

Two events migrate publishers from Catalog to Ordering. Payload shapes unchanged.

| Event | Source publisher | Target publisher | Consumers |
|---|---|---|---|
| `ReservationReminderDueIntegrationEvent` | `Catalog.API/.../ReservationReminderJob.cs` (via outbox) | `Ordering.Infrastructure/Messaging/Publishers/ReservationReminderDuePublisher.cs` | `Notification.API` (future per `NOTIFICATION_SERVICE_PLAN.md`) |
| `TableStatusChangedIntegrationEvent` | `Catalog.API/Messaging/.../TableStatusChangedPublisher.cs` | `Ordering.Infrastructure/Messaging/Publishers/TableStatusChangedPublisher.cs` | `Ordering.API` (existing; per current `TableStatusChangedIntegrationEventHandler`) |

Both publish via `IOutboxPublisher` after `SaveChangesAsync`; drained by `OrderingOutboxDispatcher` (already exists, used by Order). Per Tech Decision #8, no `SchemaVersion` bump.

### 6.6 Booking API Carter module

`Ordering.Application/Features/Booking/BookingModule.cs` registers the 19 endpoints. Module name `Booking`; route prefix `/api/v1/booking`. Permissions applied per §6.3 table.

Module structure (matches the existing Ordering Carter module pattern):

```csharp
public sealed class BookingModule : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var reservations = app.MapGroup("/api/v1/booking/reservations").RequireAuthorization();
        reservations.MapPost("/", CreateReservation.Handler);
        reservations.MapGet("/", GetReservations.Handler);
        reservations.MapGet("/{id:guid}", GetReservationById.Handler);
        reservations.MapPost("/{id:guid}/confirm", ConfirmReservation.Handler);
        reservations.MapPost("/{id:guid}/cancel", CancelReservation.Handler);
        reservations.MapPost("/{id:guid}/seat", SeatReservation.Handler);

        var walkIns = app.MapGroup("/api/v1/booking/walk-ins").RequireAuthorization();
        walkIns.MapPost("/", AddToWalkInQueue.Handler);
        walkIns.MapGet("/", GetWalkInQueue.Handler);
        walkIns.MapPost("/{id:int}/notify", NotifyWalkInCustomer.Handler);
        walkIns.MapPost("/{id:int}/seat", SeatWalkInCustomer.Handler);
        walkIns.MapDelete("/{id:int}", RemoveFromQueue.Handler);

        var tables = app.MapGroup("/api/v1/booking/tables").RequireAuthorization();
        tables.MapPost("/", CreateTable.Handler);
        tables.MapGet("/", GetTables.Handler);
        tables.MapGet("/{id:guid}", GetTableById.Handler);
        tables.MapPut("/{id:guid}", UpdateTable.Handler);
        tables.MapDelete("/{id:guid}", DeleteTable.Handler);

        var merged = app.MapGroup("/api/v1/booking/merged-tables").RequireAuthorization();
        merged.MapPost("/merge", MergeTables.Handler);
        merged.MapPost("/split", SplitTables.Handler);
        merged.MapGet("/", GetMergedTables.Handler);
    }
}
```

### 6.7 Test contract

Per endpoint: xUnit unit test on the handler (with `NSubstitute` for `IApplicationDbContext`); integration test against a real MSSQL via Testcontainers (mirrors `OrderingOutboxMultiReplicaTests` pattern). Per state machine: a `[Theory]` of happy-path + illegal-transition rows.

| # | Test | Asserts |
|:---|:---|:---|
| 1 | `CreateReservation_ValidInput_PersistsAndReturnsGuid` | Reservation saved; response carries `Id`. |
| 2 | `ConfirmReservation_FromPending_Succeeds` | State transition OK; `ConfirmedAt` stamped. |
| 3 | `ConfirmReservation_FromConfirmed_Throws` | Illegal transition throws. |
| 4 | `CancelReservation_FromPendingOrConfirmed_Succeeds` | Both source states OK; `CancelledAt` stamped. |
| 5 | `SeatReservation_FromConfirmed_Succeeds` | `SeatedAt` + `TableId` set. |
| 6 | `NoShowReservation_FromConfirmed_StampsAndFreesTable` | `TableId` cleared; event emitted. |
| 7 | `WalkInNotify_FromWaiting_Succeeds` | `NotifiedAt` stamped; `ReservationReminderDueIntegrationEvent` not emitted (it's a reservation event). |
| 8 | `ReservationReminderJob_ReminderWindow_EmitsEvent` | Reservation 55-65 min from now → event in outbox. |
| 9 | `ReservationNoShowJob_PastWindow_MarksNoShow` | `Confirmed` + 15 min past + no `SeatedAt` → `NoShow` + table freed. |
| 10 | `WalkInNoShowJob_ExpiredResponse_MarksNoShow` | `Notified` + 10 min past → `NoShow` + table freed. |
| 11 | `BookingEndpoint_WithoutPermission_Returns403` | All endpoints covered (per §6.3 permissions table). |
| 12 | `CrossTenant_Request_FilteredOut` | After `MULTITENANCY_ROLLOUT_PLAN.md Phase 2` lands; defer to that plan. |

---

## 7. Cross-service integration

This plan re-issues two existing integration events; consumer compatibility is the goal.

### 7.1 Event publisher migration

| Event | Old publisher | New publisher | Wire change? |
|---|---|---|:---:|
| `ReservationReminderDueIntegrationEvent` | `Catalog.API/Scheduling/ReservationReminderJob.cs` (outbox) | `Ordering.Infrastructure/Messaging/Publishers/ReservationReminderDuePublisher.cs` (outbox) | ❌ No |
| `TableStatusChangedIntegrationEvent` | `Catalog.API/Messaging/.../TableStatusChangedPublisher.cs` (outbox) | `Ordering.Infrastructure/Messaging/Publishers/TableStatusChangedPublisher.cs` (outbox) | ❌ No |

### 7.2 Event consumer impact

- **`ReservationReminderDueIntegrationEvent`** — No active consumer today. Future consumer is `Notification.API` per `NOTIFICATION_SERVICE_PLAN.md`. Once Phase 5 lands, the wire shape is identical; the future Notification consumer is unaffected.
- **`TableStatusChangedIntegrationEvent`** — Consumed today by `Ordering.API/Consumers/TableStatusChangedIntegrationEventHandler.cs`. Wire shape unchanged; consumer continues to work. **However**, post-move, Ordering emits the event for its own domain changes (which it previously consumed); the consumer-side handler may now see events it effectively published. The handler is idempotent on `(TableId, NewStatus)`, so a self-emit is a no-op. Verified in Phase 5 testing.

### 7.3 YARP gateway route migration (Phase 7)

`ApiGateway/YarpApiGateway/appsettings.json` adds rewrite rules for the 7-day dual-route window:

```json
{
  "ReverseProxy": {
    "Routes": {
      "catalog-reservations-legacy": {
        "ClusterId": "ordering-cluster",
        "Match": { "Path": "/catalog-api/reservations/{**catch-all}" },
        "Transforms": [
          { "PathRemovePrefix": "/catalog-api" },
          { "PathPrefix": "/api/v1/booking/reservations" }
        ]
      },
      "catalog-walk-ins-legacy": {
        "ClusterId": "ordering-cluster",
        "Match": { "Path": "/catalog-api/walk-in-queues/{**catch-all}" },
        "Transforms": [
          { "PathRemovePrefix": "/catalog-api" },
          { "PathPrefix": "/api/v1/booking/walk-ins" }
        ]
      },
      "catalog-tables-legacy": {
        "ClusterId": "ordering-cluster",
        "Match": { "Path": "/catalog-api/tables/{**catch-all}" },
        "Transforms": [
          { "PathRemovePrefix": "/catalog-api" },
          { "PathPrefix": "/api/v1/booking/tables" }
        ]
      },
      "catalog-merged-tables-legacy": {
        "ClusterId": "ordering-cluster",
        "Match": { "Path": "/catalog-api/merged-tables/{**catch-all}" },
        "Transforms": [
          { "PathRemovePrefix": "/catalog-api" },
          { "PathPrefix": "/api/v1/booking/merged-tables" }
        ]
      }
    }
  }
}
```

Routes removed in Phase 8 after the 7-day window.

### 7.4 MassTransit + outbox

Ordering's existing outbox infrastructure (`OrderingOutboxPublisher` + `OrderingOutboxDispatcher`) is reused. No new outbox wiring. The reservation publishers use the same `IOutboxPublisher.StageAsync(...)` pattern as the Order publishers. Existing `OrderingOutboxMultiReplicaTests` + `OrderingOutboxDeadLetterTests` cover the contract.

---

## 8. Security guardrails

> [!CAUTION]
> **The 7-day dual-route window is the highest-risk surface.** During the window, two backend paths serve the same logical operation. A bug in the legacy rewrite (e.g., a missed path prefix) could silently route writes to the wrong endpoint. Mitigation: the Phase 7 acceptance criteria include a path-coverage matrix that exercises every source path and verifies it lands on the correct Ordering endpoint.

| Risk | Mitigation |
|---|---|
| Cross-engine schema conversion loses data | Hand-authored `migrationBuilder.Sql(...)` per column; reviewed against source-side row counts + sample comparison. Phase 2 acceptance test compares 1% random sample row-by-row. |
| Dual-write race during cutover window | Tech Decision #4 picks **pure migration with offline cutover**, not dual-write. Single backend accepts writes per the cutover sequence in §6.2. |
| Existing `TableStatusChangedIntegrationEvent` consumer sees self-emitted events | Handler is idempotent on `(TableId, NewStatus)`; self-emit is a no-op. Verified in Phase 5 testing. |
| Cross-tenant data leak | Deferred to `MULTITENANCY_ROLLOUT_PLAN.md Phase 2` per Tech Decision #10. The new aggregates carry `RestaurantId` already; the filter is a follow-up. |
| Forbidden-state writes via raw SQL | No `FromSqlRaw` on the new aggregates per §0.2. EF Core audit columns are auto-stamped. |
| Hangfire job double-fires during the migration window | Old Catalog jobs are disabled before new Ordering jobs fire; toggle order is `Catalog:ScheduledJobs=false` → `Ordering:ScheduledJobs=true` in the cutover sequence. |
| YARP rewrite rule misses a path | Phase 7 acceptance matrix covers every source path; CI lint fails on any unmatched `/catalog-api/{reservations,walk-in-queues,tables,merged-tables}/*` request. |
| Frontend breaks when the gateway rewrite is removed (Phase 8) | Frontend team is on the hook for the migration per Tech Decision #7; the 7-day window is the grace period. |

---

## 9. Development Phases

### Phase overview

| Phase | Name | Tool groups delivered | Goal |
|:---:|---|---|---|
| **1** | Aggregate models + state machines | `Reservation`, `WalkInQueue`, `Table`, `MergedTable` aggregates in `Ordering.Domain`; state machines; exceptions | Move 4 source models + add guarded state-transition methods. |
| **2** | Database migration | 2 EF migrations (schema + backfill); backfill console job | Schema conversion from Postgres → MSSQL; data backfill. |
| **3** | API endpoints (Carter; move) | 19 endpoints under `/api/v1/booking/*`; permission attributes; DTOs; handler unit tests | Move endpoints verbatim; new Booking namespace. |
| **4** | Hangfire jobs re-host | 3 jobs in `Ordering.Infrastructure/Scheduling/`; Hangfire schema in `orderdb`; dashboard | Move scheduled jobs to Ordering. |
| **5** | Integration event publishers move | 2 publishers in `Ordering.Infrastructure/Messaging/Publishers/`; consumer compatibility tests | Re-issue `ReservationReminderDue` + `TableStatusChanged` from Ordering. |
| **6** | Booking API unification | `BookingModule.cs` Carter module; unified endpoint surface; Booking DTO set | Single coherent endpoint family. |
| **7** | Dual-route window + cutover | YARP rewrite rules; backfill job; feature flags; frontend migration window | 7-day transition with both backends serving. |
| **8** | Catalog cleanup | Delete 4 model files; 38 endpoint files; 3 Hangfire job files; drop 4 source tables | Final source deletion; plan closes. |

### Phase 1 — Aggregate models + state machines

**Goal**: move the 4 source models to `Ordering.Domain` with guarded state-transition methods.

**Status**: ⏸ Pending

**Deliverables**:
- [ ] `Ordering.Domain/Models/Reservation/Reservation.cs` created with all columns preserved
- [ ] `Ordering.Domain/Models/Reservation/ReservationStatus.cs` (enum: Pending, Confirmed, Seated, Completed, Cancelled, NoShow)
- [ ] `Ordering.Domain/Models/Reservation/Exceptions/InvalidReservationStateTransitionException.cs`
- [ ] `Reservation.Confirm()`, `Cancel()`, `Seat(tableId)`, `NoShow()`, `Complete()` methods
- [ ] `Ordering.Domain/Models/WalkInQueue/WalkInQueue.cs` created
- [ ] `Ordering.Domain/Models/WalkInQueue/WalkInQueueStatus.cs` (enum: Waiting, Notified, Seated, Cancelled, NoShow)
- [ ] `Ordering.Domain/Models/WalkInQueue/Exceptions/InvalidWalkInStateTransitionException.cs`
- [ ] `WalkInQueue.Notify()`, `Seat(tableId)`, `Leave()`, `NoShow()` methods
- [ ] `Ordering.Domain/Models/Table/Table.cs` created
- [ ] `Ordering.Domain/Models/Table/TableStatus.cs` (enum: Available, Occupied, Reserved, Cleaning, NeedsAttention)
- [ ] `Ordering.Domain/Models/MergedTable/MergedTable.cs` created
- [ ] Domain tests: 11 state-machine tests (per §6.7 rows 2-6, 9-10)
- [ ] `docs/architecture/current-architecture.md` §4.1 (Ordering Service) updated per Status rule

**Exit criteria**: `dotnet test Ordering.Domain.Tests` green; new aggregate files match the source-side column lists row-by-row.

### Phase 2 — Database migration (Postgres catalogdb → MSSQL orderdb)

**Goal**: schema conversion + data backfill land cleanly.

**Status**: 🔒 Blocked (by Phase 1)

**Deliverables**:
- [ ] `Ordering.Infrastructure/Data/Configurations/{Reservation,WalkInQueue,Table,MergedTable}Configuration.cs` registered in `ApplicationDBContext.OnModelCreating`
- [ ] `dotnet ef migrations add AddReservationsToOrdering --startup-project Ordering.API` — hand-authored schema conversion (Postgres types → MSSQL types per §6.2 table)
- [ ] `dotnet ef migrations add BackfillReservationData --startup-project Ordering.API` — backfill `migrationBuilder.Sql(...)` reading from `catalogdb` via separate connection
- [ ] Pre-migration orphan-check script: `SELECT COUNT(*) FROM catalogdb.reservations WHERE RestaurantId IS NULL` returns 0
- [ ] Backfill verification script: `SELECT (SELECT COUNT(*) FROM catalogdb.reservations) = (SELECT COUNT(*) FROM orderdb.reservations)`; sample-comparison script compares 1% random rows
- [ ] `docs/architecture/current-architecture.md` §6 (Data Stores) updated per Status rule

**Exit criteria**: migration applies cleanly to fresh DB and to staging DB (manual verify on `orderdb`); backfill row counts match source; sample comparison passes.

### Phase 3 — API endpoints (Carter; move + Booking namespace)

**Goal**: 19 source endpoints moved under `/api/v1/booking/*`.

**Status**: 🔒 Blocked (by Phase 2)

**Deliverables**:
- [ ] 6 Reservation endpoint modules copied to `Ordering.Application/Features/Booking/Reservations/` (Create, Get, GetById, Confirm, Cancel, Seat)
- [ ] 5 WalkIn endpoint modules copied to `Ordering.Application/Features/Booking/WalkIns/` (AddTo, Get, Notify, Seat, Remove)
- [ ] 5 Table endpoint modules copied to `Ordering.Application/Features/Booking/Tables/` (Create, Delete, GetById, Get, Update)
- [ ] 3 MergedTable endpoint modules copied to `Ordering.Application/Features/Booking/MergedTables/` (Merge, Split, Get)
- [ ] Each module: namespace rename `Catalog.API.Features.Reservations` → `Ordering.Application.Features.Booking.Reservations`
- [ ] Handler signatures: `ICatalogDbContext` → `IApplicationDbContext`
- [ ] Permission attributes per §6.3 table
- [ ] Handler unit tests pass against `IApplicationDbContext` substitute (mirror `Catalog.API.Tests` patterns)
- [ ] `docs/architecture/current-architecture.md` §4.1 endpoint list updated per Status rule

**Exit criteria**: `dotnet test Ordering.Application.Tests` green; manual smoke per endpoint (create / get / state-transition / list) on dev.

### Phase 4 — Hangfire jobs re-host (3 jobs)

**Goal**: 3 jobs migrate to Ordering's Hangfire schema in `orderdb`.

**Status**: 🔒 Blocked (by Phase 3)

**Deliverables**:
- [ ] `Ordering.Hangfire` schema created in `orderdb` via `Ordering.Infrastructure/Hangfire/OrderingHangfireSchema.cs`
- [ ] `Hangfire.AspNetCore` + `Hangfire.PostgreSql` (or MSSQL equivalent) packages added to `Ordering.Infrastructure.csproj`
- [ ] `Ordering.API/Program.cs` adds `AddHangfire(...)` + `UseHangfireDashboard(...)`; dashboard at `/ordering-api/hangfire` with `HangfireAdminOnlyFilter`
- [ ] `Ordering.HangfireOptions` (`Ordering:Hangfire` config section) with `[Range]` validation for `MaxRowsPerTick` and `WorkerCount`
- [ ] `Ordering.Infrastructure/Scheduling/{ReservationReminderJob,ReservationNoShowJob,WalkInNoShowJob}.cs` created; each emits the same event payload as the source-side job
- [ ] Per-job cron expressions configurable (`ReservationReminderCron`, etc.)
- [ ] `FeatureManagement__OrderingScheduledJobs` (default `false`) gates all three jobs
- [ ] Job logic tests: `Ordering.Infrastructure.Tests/Scheduling/{ReservationReminderJobTests,ReservationNoShowJobTests,WalkInNoShowJobTests}.cs`
- [ ] `docs/architecture/current-architecture.md` §2 (Tech Stack) + §4.1 updated per Status rule

**Exit criteria**: jobs registered in dev `orderdb` Hangfire schema; cron ticks fire correctly against dev data; gated correctly when flag is `false`.

### Phase 5 — Integration event publisher move

**Goal**: 2 publishers re-issued from Ordering; consumer compatibility preserved.

**Status**: 🔒 Blocked (by Phase 4)

**Deliverables**:
- [ ] `Ordering.Infrastructure/Messaging/Publishers/ReservationReminderDuePublisher.cs` — outbox-stages `ReservationReminderDueIntegrationEvent`
- [ ] `Ordering.Infrastructure/Messaging/Publishers/TableStatusChangedPublisher.cs` — outbox-stages `TableStatusChangedIntegrationEvent`
- [ ] `ReservationReminderJob` calls `ReservationReminderDuePublisher.StageAsync(...)` after `SaveChangesAsync`
- [ ] `Reservation.Seat(tableId)` and `WalkInQueue.Seat(tableId)` state-transition methods call `TableStatusChangedPublisher.StageAsync(...)`
- [ ] Event payload wire shape verified byte-for-byte against the source-side schema (`BuildingBlocks.Messaging/Events/Catalog/{ReservationReminderDueIntegrationEvent,TableStatusChangedIntegrationEvent}.cs`)
- [ ] Self-emit test: `TableStatusChangedIntegrationEventHandler` with `TableId` matching the emitting aggregate → idempotent no-op
- [ ] `dotnet test Ordering.Infrastructure.Tests/Messaging/` green
- [ ] `docs/architecture/current-architecture.md` §5.2 (Asynchronous) updated per Status rule

**Exit criteria**: both events fire from Ordering with identical wire shapes; existing consumers (TableStatusChanged) see no regression; future consumer (ReservationReminderDue → Notification) is forward-compatible.

### Phase 6 — Booking API unification

**Goal**: single coherent endpoint family under `/api/v1/booking/*`.

**Status**: 🔒 Blocked (by Phase 5)

**Deliverables**:
- [ ] `Ordering.Application/Features/Booking/BookingModule.cs` (per §6.6) registers all 19 endpoints
- [ ] `Ordering.API/Program.cs` registers `BookingModule` via Carter
- [ ] Module-level XML doc comment + per-endpoint `[ProducesResponseType]` attributes
- [ ] `Ordering.API.Tests/Integration/BookingEndpointsTests.cs` — happy-path per endpoint (19 tests, 1 per endpoint)
- [ ] `docs/architecture/current-architecture.md` §4.1 (Booking subsection) updated per Status rule

**Exit criteria**: `dotnet test Ordering.API.Tests` green; manual smoke (full booking flow end-to-end on dev).

### Phase 7 — Dual-route window + cutover

**Goal**: 7-day dual-route window; both backends serve; cutover complete; frontend migrates.

**Status**: 🔒 Blocked (by Phase 6)

**Deliverables**:
- [ ] `ApiGateway/YarpApiGateway/appsettings.json` gains 4 rewrite routes per §7.3
- [ ] `Ordering:AcceptBookingWrites=true` flag wired in `appsettings.json` (default `false`)
- [ ] `Catalog:BookingEndpointsEnabled=true` flag wired in Catalog `appsettings.json` (default `true` during the window; flips to `false` on day 7)
- [ ] Backfill console job (`Ordering.Infrastructure/Tools/BackfillReservationData.cs`) tested manually on dev + staging
- [ ] Cutover runbook (`docs/runbooks/2026-XX-XX-reservation-cutover.md`) authored; lists every manual step
- [ ] Path-coverage matrix (`docs/architecture/2026-XX-XX-booking-path-coverage.md`) tests every source path lands on the correct Ordering endpoint
- [ ] CI lint: fails on any unmatched `/catalog-api/{reservations,walk-in-queues,tables,merged-tables}/*` request during the window
- [ ] `docs/architecture/current-architecture.md` §11 (Local Development) updated with cutover checklist

**Exit criteria**: 7-day window completed without data-loss or path-routing incidents; row counts match source vs target after backfill.

### Phase 8 — Catalog cleanup (delete source)

**Goal**: source models, endpoints, jobs, tables all deleted from Catalog.

**Status**: 🔒 Blocked (by Phase 7)

**Deliverables**:
- [ ] Delete `Catalog.API/Models/{Reservation,WalkInQueue,Table,MergedTable}.cs`
- [ ] Delete `Catalog.API/Features/{Reservations,WalkInQueues,Tables,MergedTables}/` (38 files)
- [ ] Delete `Catalog.API/Scheduling/{ReservationReminderJob,ReservationNoShowJob,WalkInNoShowJob}.cs`
- [ ] Delete `Catalog.API/Exceptions/{ReservationNotFoundException,WalkInQueueNotFoundException,TableNotFoundException,MergedTableNotFoundException}.cs`
- [ ] `Catalog.API/Program.cs` removes the 4 Carter module registrations + the 3 Hangfire job registrations
- [ ] `dotnet ef migrations add DropReservationTablesFromCatalog --startup-project Catalog.API` — drops `reservations`, `walk_in_queues`, `tables`, `merged_tables` from `catalogdb`
- [ ] `ApiGateway/YarpApiGateway/appsettings.json` removes the 4 rewrite routes from Phase 7
- [ ] `Catalog.API.Tests` deletes 19 endpoint integration tests that target the deleted endpoints
- [ ] `docs/architecture/current-architecture.md` §4.2 (Catalog Service) updated; §4.1 (Ordering Booking) finalized
- [ ] Plan closes: Status block updated, Changelog v1.1 entry appended

**Exit criteria**: `dotnet test` solution-wide green; `catalogdb` no longer contains the 4 tables; `Catalog.API` no longer references Reservation/WalkInQueue/Table/MergedTable types; YARP routes cleanly removed.

---

## 10. Technical considerations

### 10.1 Cross-cutting

> **Phase 1 adoption (2026-07-18):** the cross-cutting items below are part of Phase 1 (aggregate models), not a separate phase.

- **Multi-tenancy hand-off** — `[defer]` per Tech Decision #10. The new aggregates carry `RestaurantId` (already do today) but do NOT implement `ITenantEntity` in this plan. `MULTITENANCY_ROLLOUT_PLAN.md Phase 2` owns the Ordering tenant-scope adoption; when it ships, the new aggregates get the marker + `HasQueryFilter` in a separate PR. No conflict.
- **State-machine consistency** — `[P1 ✅]` Reservation + WalkInQueue state machines use guarded methods (per Tech Decision in §0.2); illegal transitions throw `Invalid*StateTransitionException`. Mirrors the `Order` aggregate pattern from `KITCHEN_INTEGRATION_PLAN.md`.
- **Outbox reuse** — `[P5 ✅]` Reservation + TableStatus publishers use the existing `IOutboxPublisher` + `OrderingOutboxDispatcher` infrastructure. No new outbox wiring; existing `OrderingOutboxMultiReplicaTests` + `OrderingOutboxDeadLetterTests` cover the contract.
- **YARP dual-route window** — `[P7 ✅]` Gateway rewrite rules preserve `/catalog-api/{reservations,walk-in-queues,tables,merged-tables}/*` paths during the 7-day window; removed in Phase 8.
- **Hangfire storage per service** — `[P4 ✅]` New `Ordering.Hangfire` schema in `orderdb`; matches the Catalog pattern. One schema per service is the project convention.
- **Gateway awareness of reservation domain** — `[P5 ✅]` Gateway rate-limiting (when it lands) will be per-tenant via the `restaurantId` claim; reservation endpoints don't add a new dimension.

### 10.2 Phase 1 adoption

- **[P1 ✅]** 4 aggregate models with state machines; pure migration (no new domain behavior).
- **[P1 ⚠]** `AuditableEntity<TId>` base choice (Tech Decision #11) preserves audit columns but skips the event-sourced `Aggregate<TId>` path. Out of scope for this plan; could be revisited if/when Reservation events become first-class.

### 10.3 Phase 2 adoption

- **[P2 ✅]** Schema conversion hand-authored; backfill console job tested manually.
- **[P2 ⚠]** Cross-engine data fidelity: `LocalDate` + `LocalTime` columns need explicit MSSQL types (`date` + `time(7)`); the existing Ordering NodaTime value converters handle in-process mapping but the SQL DDL must specify the types. Documented in §6.2.

### 10.4 Phase 5 adoption

- **[P5 ⚠]** `TableStatusChangedIntegrationEvent` self-emit: Ordering emits the event for its own table changes; the existing consumer (`TableStatusChangedIntegrationEventHandler`) is the same service. The handler is idempotent on `(TableId, NewStatus)`, so self-emit is a no-op, but the test must explicitly assert this.

### 10.5 Effort estimate

| Phase | Effort | Risk |
|---|---|---|
| Phase 1 — Aggregate models + state machines | 1 day | Low |
| Phase 2 — Database migration | 2 days | Medium (cross-engine schema conversion + backfill) |
| Phase 3 — API endpoints (move) | 2 days | Low (mechanical rename + handler-context swap) |
| Phase 4 — Hangfire jobs re-host | 1 day | Low |
| Phase 5 — Integration event publisher move | 1 day | Medium (consumer compatibility + self-emit) |
| Phase 6 — Booking API unification | 0.5 day | Low |
| Phase 7 — Dual-route window + cutover | 2-3 days (manual ops + monitoring) | Medium |
| Phase 8 — Catalog cleanup | 1 day | Low |
| **Total** | **10-11 days** | **Medium** |

The bulk of the risk is in Phase 2 (cross-engine migration) and Phase 7 (cutover ops). Phase 8 is the lightest.

---

## How to use this plan

1. **Find the current phase** in the Status table above. Update its row to 🚧 In progress on the first commit of the phase.
2. **For each phase**, copy the "Phase N" subsection before starting work. After completion, append a new "Phase N implementation notes (DATE)" section using the template below.
3. **Commit messages** convention is in the Status section. The whole plan is the source of truth for what was decided — keep it current.
4. **Drift between the plan and the code is the bug class plans exist to prevent.** When implementation reveals the plan was wrong (schema different than expected, API behaves differently), update the plan *and* the code in the same PR.

### The phase-completion workflow

> **Every phase completion is two commits, not one.**

1. **Code commit** — the work itself (`feat: ...`). Do NOT touch the plan in this commit.
2. **Plan commit** — the plan update only (`docs: mark Phase N complete in reservation-plan`):
   - Bump `Plan version` from `v1.N-1` → `v1.N` in the Status section.
   - Mark the phase's `[ ]` → `[x]` on deliverables; update the Status table row.
   - Append a new `### Phase N implementation notes (DATE)` section below.
   - Update §10's "Phase N adoption" subnote to reflect what was actually adopted vs deferred.
   - Add a Changelog entry at the bottom.
   - **If you skip the plan commit, the phase is not done** — even if the code shipped.

> Two commits keeps the diff reviewable: the code commit is just code, the plan commit is just documentation. Mixing them makes both harder to review and easier to forget.

### Phase implementation notes template

**§6.X items — adopted in Phase N.**
- {{ITEM}} — `[{{STATUS — ✅ adopted, ⚠ deferred, ❌ rejected}}]` {{RESOLUTION_NOTE}}.

**Bugs found + fixed during implementation.**
- {{BUG_AND_FIX — one line per bug, named with the symptom not the root cause.}}.

**Deferred to a Phase N follow-up ({{SCOPE}}).**
- {{DEFERRED_ITEM — link to the follow-up doc / TODO file if it lives elsewhere.}}.

**Phase N verification ({{WITHOUT/WITH}} {{DEPENDENCY}}).**
- {{VERIFICATION_STEP — the command + expected output.}}.

**Files added.** {{LIST}}. **Files modified:** {{LIST}}.

---

## Changelog

### v1.0 (2026-07-18) — initial draft
- Created plan with 8 phases (1–8).
- Sections 0–10 drafted per `_template.md` conventions.
- 12 locked decisions; estimated 10-11 days total effort.
- Scope per user choice 2026-07-18: Migration-with-Booking-API; Tables move with Reservation; BulkOrderUpload stays in Catalog; Catalog closes ✅ COMPLETED.
- Replaces the out-of-plan tracking row `CATALOG_SERVICE_PLAN.md §6.0`.
