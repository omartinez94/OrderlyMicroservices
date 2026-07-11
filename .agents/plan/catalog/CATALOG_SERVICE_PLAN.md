# Catalog.API — Service Plan

> Scope: completion plan for the existing `Catalog.API` microservice. Closes the gaps between `docs/architecture/architecture.md`, `docs/architecture/db_relational_model.mermaid`, and the code in `Services/Catalog/Catalog.API/`. This is an *evolution* plan, not a green-field design — the relational schema and most CRUD features are already in place; the work is wiring the missing behaviour, completing the partial features, and assigning the misplaced entities to the right homes.
>
> **In-plan entity moves:** Coupon to Discount (Phase 6.2).  
> **Out-of-plan entity moves:** Reservation/WalkInQueue → Ordering, CustomerFeedback/NotificationLog → Notification. Their prerequisites are documented in Phase 6.0 / 6.1 and owned by other service plans.

---

## 1. Context

`Catalog.API` already runs end to end for basic menu / restaurant / table CRUD over PostgreSQL + EF Core, plus Marten for the four audit documents (`OrderSnapshot`, `OrderModificationLog`, `OrderItemPriceAudit`, `NotificationLog`). All four extend `Entity<int>` today — a known code smell tracked in `db_relational_model.md` §137-148 and folded into §8 of this plan. Marten documents are not relational entities; the right fix is to **drop the base class**, not swap it for `Entity<Guid>`.

Concretely, three cross-cutting capabilities are not wired and several owned entities have incomplete or absent features:

- **Redis caching is absent** — `architecture.md` §174-181 and §645-658 prescribe `catalog:menu:{restaurantId}` (1h TTL) and `catalog:ingredients:{restaurantId}` (5 min TTL) plus invalidation on writes. `Program.cs:1-86` registers neither Redis nor distributed cache.
- **Message bus is absent** — `BuildingBlocks.Messaging/MassTransit/Extensions.cs` exposes `AddMessageBroker`, but `Catalog.API/Program.cs` never calls it. `BuildingBlocks.Messaging/Events/` has only Ordering/Kitchen events. Outbox pieces (`IOutboxPublisher`) live in `BuildingBlocks.Messaging/Outbox` but Catalog doesn't register them. As a result Catalog publishes **none** of the events listed in `architecture.md` §183-190 and cannot consume `OrderCompleted` to roll up `MenuItemAnalytics`.
- **No Ingredient Availability Engine** — `MenuItem.AvailabilityStatus` (Available/Limited/Unavailable) and `Restaurant.AllowAutoSubstitute` exist as data, but no service recomputes them when an ingredient or alternative changes. The "when ingredient unavailable → check alternatives → flip MenuItem.AvailabilityStatus" rule in `architecture.md` §172-176 is unimplemented.

A detailed gap inventory is in `docs/architecture/_catalog_gap_inventory.md` (added by this plan) and the drift memory `db-model-drift-reports.md`.

---

## 2. Goal

Bring `Catalog.API` to the level `architecture.md` describes by:

1. Wiring **Redis** (existing shared instance) for menu and ingredient availability, with per-feature invalidation and a fail-open reconcile path.
2. Wiring **MassTransit + RabbitMQ + the existing outbox** and emitting all five Catalog-side integration events; consuming `OrderCompleted` for `MenuItemAnalytics`. Event versioning via `int SchemaVersion`.
3. Building the **Ingredient Availability Engine** with `IDomainEvent` triggers dispatched by `DispatchDomainEventsInterceptor`, plus a safety-net hosted reconciler.
4. Closing the partial / missing **vertical-slice features** so each owned entity has the endpoints the architecture describes.
5. Wiring the **async lifecycle** (Hangfire-scheduled) for reservations (reminder, no-show) and walk-in queue (notification, 10-minute response window) plus seasonal/promo availability.
6. In-plan: moving the **`Coupon`** table to Discount (its destination already exists). Out-of-plan: tracking the **prerequisites** for moving Reservation/WalkInQueue to Ordering and CustomerFeedback/NotificationLog to Notification — the receiving services need their own plans first.

The companion `docs/architecture/db_relational_model.mermaid` change-log appendix is also kept honest by this plan (no new schema additions that don't update both sides).

---

## 3. Out of scope

- Redesigning the relational schema. The mermaid is reconciled to code; only the known mismatches flagged in `db_relational_model.md` §137-148 are open for code-side fixes (and they are sequenced as small fixes under §8 here, not as schema redesign).
- Splitting `Catalog.API` into `Catalog.Domain` / `Catalog.Application` / `Catalog.Infrastructure` projects. The single-project vertical-slice layout matches the other existing services; keeping it.
- The frontend project (lives in a different folder, owned separately).
- Adding new permissions in Identity — the `kitchen:*` permissions already exist; for Catalog only `menu:edit`, `restaurant:edit`, etc. need review if any new endpoint surfaces a previously-unexposed action. Add in the Identity service as a follow-up if surfaced.
- The four Marten documents' `Entity<int>` / synthetic-`Guid` id mismatch (lifted from `db_relational_model.md` §137-148). Marten documents are **not relational entities** — they should not extend any of `AuditableEntity<>`, `Entity<int>`, or `Entity<Guid>`. The intended fix is to drop the base class entirely and let Marten assign its synthetic `Guid` id via `[HiloSequence]` or the default synthetic-key convention (or, when a known id is required, `[Identity]`/explicit `Guid Id`). §8 below carries this.
- **Introducing `Reservation` / `WalkInQueue` aggregates in Ordering.** That work belongs to a separate Ordering-side plan; this plan only documents the prerequisite (§7.6.0).
- **Building the `Notification.API` skeleton.** That belongs to its own v1 plan; this plan only documents the prerequisite (§7.6.1).

---

## 4. Service boundaries

### Catalog.API owns

- **Restaurants** and **Brands** (multi-tenant scope keys for everything below).
- **Tables** (incl. position X/Y, shape, status, current order), **MergedTables** (parent/child relationships, merge/split semantics).
- **Menu tree**: `MenuCategories` → `MenuSubCategories` → `MenuItems`, with `MenuItemVariations`, `ComboItems`, `MenuItemIngredients`.
- **Ingredients**, `IngredientAlternatives` (incl. `AutoSubstitute`), **`MenuItem.AvailabilityStatus`** computations.
- **`MenuItemAnalytics`** (daily aggregates, updated from the `OrderCompleted` consumer).
- **`PriceHistory`** audit log (append-only history of every menu-item / variation / alternative price change).
- The Redis cache strategies (`catalog:menu:*`, `catalog:ingredients:*`) and their invalidation discipline.
- Publishing: `MenuItemCreated`, `MenuItemUpdated`, `MenuItemDeleted`, `IngredientAvailabilityChanged`, `TableStatusChanged`, `RestaurantConfigurationChanged`.
- Consuming: `OrderCompleted` (drives `MenuItemAnalytics`).

### Catalog.API does NOT own

- **`Orders`, `OrderItems`, `OrderBills`, `OrderModificationLog`, `OrderSnapshot`, `OrderItemPriceAudit`, `OrderTimingAnalytics`** — Ordering domain. Catalog **persists** the four Marten *documents* (`OrderSnapshot`, `OrderModificationLog`, `OrderItemPriceAudit`) and the relational `NotificationLog` because they're cross-service audit records, but the **writers** live in Ordering / Kitchen / Notification. None of these documents extend `AuditableEntity<>` / `Entity<int>` / `Entity<Guid>` (Marten documents are not relational entities).
- **`Reservation`, `WalkInQueue`, `BulkOrderUpload`** — Ordering domain. **No scaffolding exists in Ordering today** (verified by file glob — only `Catalog.API/Models/` defines them). The move requires an Ordering-side plan to introduce the aggregates first (Phase 6.0 prerequisite). Catalog keeps the writers until that plan lands.
- **`CustomerFeedback`, `NotificationLog`** (relational) — Notification domain. **No `Notification.API` exists today** (verified — `Services/` has only Basket/Catalog/Discount/Identity/Kitchen/Ordering). Phase 6.1 prerequisite: Notification v1 plan. Catalog keeps the writers until then.
- **`User`** (in Catalog) — Identity mirror. Keep local-mirror model only; no write path; document explicitly in `db_relational_model.md` §"Out of scope".
- **`Coupon`** — Discount service responsibility (per architecture §210-216 and §283-292). **Discount already has the table** (`Discount.Grpc/Models/Coupon.cs:6-15`), with the same columns plus `AuditableEntity<int>` instead of `Entity<int>`. The Coupon move is in-plan (§7.6.2).

### Ordering ↔ Catalog flow (one-liner)

```
Ordering publishes OrderCompletedIntegrationEvent
  → Catalog consumes → bumps MenuItemAnalytics row for the day
Catalog publishes MenuItemUpdated / IngredientAvailabilityChanged /
  TableStatusChanged / RestaurantConfigurationChanged
  → Basket consumes (cache validate / invalidate)
  → Discount consumes MenuItemUpdated to re-evaluate "Buy X get Y" rules
  → Kitchen reads via its read replica (not the bus)
```

---

## 5. Tech decisions

| Decision | Choice | Reason |
|---|---|---|
| Architecture | Vertical Slice (existing), single project | Matches Catalog / Basket / Discount pattern. |
| Framework | ASP.NET Core 10 (Carter + minimal API) | Already in use. |
| **Language** | **C# 12+** (records for integration events and DTOs; primary constructors for handlers and small services; collection expressions; required members; nullable enabled) | New code uses modern C#. Records and primary constructors match the kitchen plan's idiom. Existing code is updated only where edits are made. |
| Persistence | EF Core 10 + `Npgsql.EntityFrameworkCore.PostgreSQL` for relational; Marten for documents | Already wired in `Program.cs:30-54`. |
| Cache | `Microsoft.Extensions.Caching.StackExchangeRedis` + `IConnectionMultiplexer` | Catalog connects to the **existing shared Redis** instance (`distributedcache:6379` in docker-compose, `localhost:6379` in local dev). **No new Redis database** — just a new client + a typed `ICatalogCache` helper. |
| Messaging | `MassTransit` via `BuildingBlocks.Messaging.MassTransit.AddMessageBroker` | Reuse existing extension. |
| Event versioning | `int SchemaVersion` on every integration event (initial = 1) | Consumers ignore unknown major versions. Documented in §8. |
| Outbox | `BuildingBlocks.Messaging.Outbox.IOutboxPublisher` from Catalog handlers | Matches the pattern Ordering/Kitchen already use (commits c95325c, def4187). |
| Scheduling | **Hangfire** in Catalog (PostgreSQL schema, recurring-job dashboard, persistent schedule) | Decision #3 from csharp-expert pass. Reservation reminder, reservation no-show, walk-in no-show, seasonal availability — all recurring jobs. |
| Engine trigger | In-process `IDomainEvent` raised on ingredient / alternative / menu-item-ingredient mutations, dispatched by `DispatchDomainEventsInterceptor` mirroring Ordering/Kitchen | Decision #7. Same-transaction semantics; latency-bounded; reconcile hosted service is a safety net only. |
| Cache failure path | **Fail-open + log**: cache invalidation best-effort; `CacheDriftRepairService` hosted service runs every 5 min to diff DB against cache keys | Decision #8. Cache outage doesn't fail writes; reconcile repairs drift. |
| **Health checks** | `/live` (process up) + `/ready` (Postgres, Redis, RabbitMQ, outbox dead-message count). **Any** outbox DLQ message trips `/ready` to unready (threshold configurable via `CatalogOptions:OutboxDeadLetterThreshold`, default 0). | Decision #4 / #11. K8s-style split. |
| Time / IDs | NodaTime `Instant`, `Guid` ids | Matches AGENTS.md conventions. |
| Logging | Structured logging (existing setup) | Default. |
| Mapping | `Mapster` (matches the rest of the codebase) | New DTOs only. |
| Validation | FluentValidation (existing) | Default. |
| Tests | xUnit + FluentAssertions + Moq for unit; **Testcontainers** (Postgres + Redis + RabbitMQ) for engine / outbox / cache integration | Decision #12. |

### What this plan does NOT introduce

- **No new database.** Catalog stays on its existing `catalogdb` instance + Marten docs alongside relational.
- **No new Redis database.** Phase 1 attaches Catalog as a new client to the existing shared Redis (`distributedcache` container); no new container, no new image, no new volume.
- **No new message broker.** RabbitMQ (already running in docker-compose).
- **No new permission scheme.** Lean on JWT claims already populated by Identity.
- **No saga/orchestrator for the engine.** The Ingredient Availability Engine is event-driven local recompute, not a process manager.
- **No Reservation/WalkInQueue/CustomerFeedback/NotificationLog tables in Ordering or Notification services.** Those services don't exist in skeleton form yet; this plan flags the prerequisite but does not introduce their tables.

---

## 6. Folder layout

Today's project is flat (`Catalog.API/Features/<entity>/<action>/`). This plan adds a small number of new top-level folders without restructuring what's already there:

```
Services/Catalog/Catalog.API/
  Models/                                  -- existing (no schema changes)
  Data/                                    -- existing (CatalogDbContext, Migrations)
  Features/                                -- existing vertical slices (one folder per entity)
    <NewSliceFolders>                      -- added by §7.4 (MergedTables, BulkOrderUploads,
                                              SubCategories.Delete, PriceHistories,
                                              MenuItemAnalytics.Updater, CustomerFeedback.Submit)
  Cache/
    CatalogCache.cs                        -- ICatalogCache + RedisCatalogCache (typed keys)
    CacheKeys.cs                           -- format strings catalog:menu:{rid}, catalog:ingredients:{rid}
    CacheDriftRepairService.cs             -- hosted service: best-effort diff & repair (decision #8)
  Messaging/
    Events/                                -- the 5 Catalog integration events (each carries int SchemaVersion)
    EventHandlers/
      OrderCompletedHandler.cs             -- plain IConsumer<T>; idempotent on (OrderId, MenuItemId)
  Availability/
    IngredientAvailabilityEngine.cs        -- pure function over aggregates
    Events/                                -- in-process IDomainEvent + handler that calls the engine
    IngredientAvailabilityReconcileService.cs -- safety-net hosted service (flag-gated)
  Scheduling/
    ReservationReminderJob.cs              -- Hangfire recurring (every 5 min)
    ReservationNoShowJob.cs                -- Hangfire recurring (every 1 min)
    WalkInNoShowJob.cs                     -- Hangfire recurring (every 1 min)
    SeasonalAvailabilityJob.cs             -- Hangfire recurring (every 5 min)
  Health/
    CatalogHealthChecks.cs                 -- adds /live + /ready with Redis + RabbitMQ + outbox checks
    OutboxDeadLetterProbe.cs               -- probe that surfaces count to /ready (decision #11)
  Exceptions/                              -- existing; add IngredientAvailabilityStaleException etc.
  DomainEvents/
    DispatchDomainEventsInterceptor.cs     -- registered in DbContext (mirrors Ordering/Kitchen)
  Program.cs                               -- wire the above
```

All new code follows the same CQRS / MediatR handler pattern existing in `Features/`. New code uses C# 12+ records for events and DTOs, primary constructors for small services.

---

## 6.5 Consumer contract matrix

Single source of truth for which event goes where, and what each consumer is expected to do. **Both** publisher (Catalog) and consumers must honour this list at every release. Catalog will not change a published event's shape without a major `SchemaVersion` bump.

| Event (Catalog →) | SchemaVersion=1 fields | Intended consumers → required action |
|---|---|---|
| `MenuItemChangedIntegrationEvent` (with `ChangeType ∈ Created, Updated, Deleted`) | `MenuItemId`, `RestaurantId`, `ChangeType`, `SchemaVersion=1` | **Basket** → invalidate any cached price/availability lookups for `MenuItemId`; validate any pending baskets. **Discount** → if `ChangeType = Deleted`, deactivate rules referencing this item; if `Updated`, re-evaluate BOGO / "Buy X get Y" thresholds. **Ordering** → new orders must validate the menu item is still valid + available (existing orders unaffected — they snapshot price/name). |
| `IngredientAvailabilityChangedIntegrationEvent` | `MenuItemId`, `RestaurantId`, `AvailabilityStatus`, `AutoSubstituteOf?`, `SchemaVersion=1` | **Basket** → re-validate pending baskets; reject checkout if `Unavailable`; prompt customer if `Limited`. **Ordering** → reject new orders where status = Unavailable (Limited must collect substitute choice at order placement). |
| `TableStatusChangedIntegrationEvent` | `TableId`, `RestaurantId`, `NewStatus`, `SchemaVersion=1` | **Ordering** → reservation / order placement checks `Table.Status == Available` (with optimistic concurrency on the hold window). Walk-in worker uses this to assign waiting parties. **Reservation expiry** invalidates the reservation hold when status flips to Cancelled / NoShow. |
| `RestaurantConfigurationChangedIntegrationEvent` | `RestaurantId`, `ChangedFields` (array of names), `SchemaVersion=1` | **Identity** → affected users must re-login for fresh JWT claims (`restaurantId` and any role-bound configuration). **Discount** → if `Currency` changed, deactivate or reissue coupons. **Notification** → receipt templates pick up new tax/currency placeholders. |

| Event (→ Catalog) | Source | SchemaVersion | Catalog's required action |
|---|---|---|---|
| `OrderCompletedIntegrationEvent` | Ordering | 1 | Find `MenuItemAnalytics` row for `(MenuItemId, AnalysisDate = UTC date)`; if missing, create; otherwise bump `TimesOrdered` and `TotalRevenue`. Idempotent on `(OrderId, MenuItemId)`. |

**Migration rule when this matrix changes:**
- **Adding a field** → bump `SchemaVersion`; consumers ignore unknown fields by MassTransit default.
- **Renaming a field** → rename + bump major version (i.e. introduce `SchemaVersion=2` side-by-side); consumers on v1 ignore the new name; publish both for one release cycle, drop v1 after one release with zero traffic.
- **Removing a field** → same: introduce next major, deprecate, drop after one release.

---

## 6.6 Gateway route migration convention

YARP is configured in `ApiGateway/YarpApiGateway/appsettings.json` with one route + cluster per service, address `/{service-name}-api/{**catch-all}` + `PathRemovePrefix` + cluster `{service-name}-cluster`. **There is no existing precedent for moving a route between services.**

**Adopted convention (decision #16):** atomic `ClusterId` change, keep public path stable. For any future entity move:

1. Add a new route in `appsettings.json` with `Path` = `/<target-service>-api/<new-entities>/{**catch-all}`, `ClusterId` = `<target-service>-cluster`. Verify with smoke test.
2. Re-point the existing `/<source-service>-api/<new-entities>/{**catch-all}` route's `ClusterId` to the target service's cluster. Public URL stays stable — existing clients don't change.
3. After one release with zero traffic on the legacy route, remove the source's old route entry.
4. Update `db_relational_model.mermaid` with a "moved from Catalog on YYYY-MM-DD" note.

**Optional cleanup step:** if the migrated endpoints accumulate multiple leftovers, add a deprecation header (`X-Service-Moved: <new prefix>`) for one release to nudge clients forward.

The Coupon move (§7.6.2) is the first application of this convention.

---

## 7. Phased milestones

The phases are ordered so each one is independently shippable and any earlier phase's failure does not block the later cross-cutting concerns.

### Phase 1 — Redis cache

- The repo already has a single shared Redis instance (`distributedcache` container in `docker-compose.override.yml:98`, address `ConnectionStrings__Redis=distributedcache:6379,password=...`; local dev `localhost:6379`). Catalog **does not** provision a new Redis database — it just adds a connection string env var and a new client.
  - Add `ConnectionStrings__Redis=distributedcache:6379,password=${REDIS_PASSWORD:-redisdev}` to the `catalog.api` block in `docker-compose.override.yml`.
  - Add `"Redis": "localhost:6379"` to `Services/Catalog/Catalog.API/appsettings.json`.
- Add `ICatalogCache` abstraction with `GetFullMenuAsync(restaurantId)`, `GetIngredientAvailabilityAsync(restaurantId)`, `InvalidateMenuAsync(restaurantId)`, `InvalidateIngredientsAsync(restaurantId)`.
- Register `IConnectionMultiplexer` (`services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(builder.Configuration.GetConnectionString("Redis")!))`) and wrap in `services.AddStackExchangeRedisCache(...)` only if `IDistributedCache` is needed.
- TTLs: menu 1h, ingredients 5min — match `architecture.md` §174-181.
- Decorator pattern via Scrutor (`AddScoped<IMenuReader, CachedMenuReader>()` wrapping `MenuReader`) — same idiom as `Basket.API/Program.cs:57-66`.
- All cache keys are namespaced under `catalog:*` so Basket / other services don't collide on the shared Redis.
- Invalidation hooks:
  - `CreateMenuItemHandler`, `UpdateMenuItemHandler`, `DeleteMenuItemHandler`, `CreateMenuItemVariationHandler`, `UpdateMenuItemVariationHandler`, `DeleteMenuItemVariationHandler`, all `MenuSubCategory` / `MenuCategory` mutators → call `InvalidateMenuAsync(restaurantId)`.
  - `CreateIngredientHandler`, `UpdateIngredientHandler`, `DeleteIngredientHandler`, `CreateIngredientAlternativeHandler`, `UpdateIngredientAlternativeHandler` → call `InvalidateIngredientsAsync(restaurantId)`.
- **Failure-mode policy (decision #8):** cache calls are best-effort. If Redis is unreachable, the mutation commits; the call is logged at `Warning`. The new `CacheDriftRepairService` (`Cache/CacheDriftRepairService.cs`) is an `IHostedService` that runs every 5 minutes (configurable via `CatalogOptions:CacheRepairInterval`), diffs the per-restaurant DB row counts against cache key presence, and repopulates any missing keys. Flag-gated.
- Flag-gate the rollout behind `FeatureManagement__CatalogRedisCache=true` so it can be disabled without a redeploy if the cache goes stale.
- **Tests:** Testcontainers (real Redis) integration test for hit/miss + invalidation hooks + drift-repair simulation when Redis is killed mid-test.

### Phase 2 — Messaging + outbox wiring

- Add the five Catalog integration event classes in `BuildingBlocks.Messaging/Events/Catalog/`. Each event carries:
  - `int SchemaVersion = 1` (initial)
  - `Guid EventId` (deterministic if redelivery matters, otherwise `Guid.NewGuid()`)
  - `Instant OccurredAt`
  - `Guid RestaurantId`
  - Event-type-specific payload
  - Concrete types:
    - `MenuItemChangedIntegrationEvent` (with `ChangeType ∈ Created, Updated, Deleted`)
    - `IngredientAvailabilityChangedIntegrationEvent`
    - `TableStatusChangedIntegrationEvent`
    - `RestaurantConfigurationChangedIntegrationEvent`
- Convert Catalog handlers that mutate state to publish via `IOutboxPublisher` after `SaveChangesAsync` so the outbox is committed in the same transaction. Mirrors how Ordering's `SaveChangesInterceptor` is wired (commit c95325c).
- **`OrderCompletedIntegrationEvent` consumer** — placed at `Catalog.API/Messaging/EventHandlers/OrderCompletedHandler.cs`. **Plain `IConsumer<OrderCompletedIntegrationEvent>` handler class** (decision #10). Stateless, idempotent: keyed by `(OrderId, MenuItemId)`, the handler skips if a row with that key already exists for the day. Test with MassTransit's `InMemoryTestHarness`.
- Register `AddMessageBroker` in `Catalog.API/Program.cs`.
- **Poison queue / dead-letter handling** following the pattern from commit `def4187`. Outbox dead-letter table is `OutboxDeadMessage` in `BuildingBlocks.Messaging/Outbox`. **`OutboxDeadLetterProbe`** (new `Health/OutboxDeadLetterProbe.cs`) reads `Count()` and exposes it for the `/ready` endpoint.
- **`/ready` health check** (`Health/CatalogHealthChecks.cs`):
  - `AddNpgSql(connectionString)` — Postgres reachable.
  - `AddRedis(connectionString)` — Redis reachable.
  - `AddRabbitMQ(...)` via MassTransit's `CheckHealth` extension — RabbitMQ broker reachable.
  - `AddCheck<OutboxDeadLetterProbe>("outbox_dlq")` — returns `Unhealthy` if `Count > CatalogOptions:OutboxDeadLetterThreshold` (default `0`; decision #11).
  - `MapHealthChecks("/live", new HealthCheckOptions { Predicate = _ => false })` — always green; liveness only.
  - `MapHealthChecks("/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") })` — full suite; 503 if any fail.
- Emit `RestaurantConfigurationChanged` whenever `UpdateRestaurantHandler` mutates `AllowAutoSubstitute`, `AutoConfirmReservations`, `TaxRate`, `Currency`, `TimeZone`, `EstimatedTurnoverMinutes`.
- Add a doc block in `BuildingBlocks.Messaging/Events/` index listing which service publishes which events.
- **Tests:** Testcontainers + MassTransit `InMemoryTestHarness` for publish → consume round-trip on `OrderCompleted`. Unit tests on handler idempotency.

### Phase 3 — Ingredient Availability Engine

- Define a pure `IngredientAvailabilityEngine.AvailabilityProfileFor(menuItemId, restaurantId)` returning `{ AvailabilityStatus, AutoSubstituteOf }` based on:
  - `MenuItemIngredients` joined to `Ingredients.IsAvailable`
  - For each unavailable ingredient, look up `IngredientAlternatives` where `AlternativeIngredientId ∈ {available set}`
  - If no unavailable ingredients → `Available`.
  - If any unavailable ingredient has an alternative → `Limited` (unless auto-substitute applies to all of them) and record which alternative wins.
  - If any unavailable ingredient has no alternative and is **not optional** on the menu item → `Unavailable`.
  - If `Restaurant.AllowAutoSubstitute` AND every unavailable ingredient has exactly one `AutoSubstitute = true` alternative → `Available` with `AutoSubstituteOf` populated.
- **Trigger (decision #7):** in-process `IDomainEvent` raised on `Ingredient.UpdateIngredient`, `IngredientAlternative.{Create,Update,Delete}`, and `MenuItemIngredient.{Add,Remove}` handlers. Domain events are accumulated by the EF Core change tracker and dispatched by a new `DispatchDomainEventsInterceptor` (mirrors Ordering/Kitchen). Synchronous in the same transaction.
- A safety-net `IHostedService` (`Availability/IngredientAvailabilityReconcileService.cs`) runs once at startup and on a 1-minute interval (configurable, off by default) to catch any drift from missed events. Uses `FeatureManagement__CatalogAvailabilityEngineReconcile=true`.
- After recompute, publish `IngredientAvailabilityChangedIntegrationEvent` per affected `MenuItem` (batched, deduped per restaurant).
- **Tests:** Unit tests over the matrix in `architecture.md` §927-931 (no alt + not optional → Unavailable; alt exists → Limited; auto-substitute satisfied → Available). Testcontainers integration for the full refresh path.

### Phase 4 — Complete partial / missing features

| Feature slice | What to add |
|---|---|
| `Features/MergedTables/` | `CreateMergedTableCommand` (parent + N children, validate capacity sum, mark children as unavailable), `SplitMergedTableCommand` (reactivates children), `GetMergedTablesByRestaurantQuery`. Reuses `Tables` DbContext. |
| `Features/MenuSubCategories/DeleteMenuSubCategory/` | Soft-delete command — sets `IsDeleted = true`, `DeletedAt = Instant`, cascades suppression in reads. |
| `Features/ComboItems/UpdateComboItem/` | `UpdateComboItemCommand` allowing quantity / isOptional changes. Validates `IncludedMenuItem` still exists. |
| `Features/BulkOrderUploads/` | Upload endpoint (multipart), validate menu-item ids + table availability, persist a `BulkOrderUpload` row with `ErrorLog` populated. Add `GetBulkOrderUpload`, `ApproveBulkOrderUpload` (manager/admin), `RejectBulkOrderUpload`. **Stays in Catalog** — the move to Ordering is now out-of-plan (§7.6.0). |
| `Features/PriceHistories/CreatePriceHistory/` | Internal write path invoked automatically by the price-mutation handlers (variation update, base price update, alternative price update). Records `OldPrice`, `NewPrice`, `Reason`, `ChangedByUserId`, `EffectiveDate`. |
| `Features/RestaurantConfiguration/AuditOnUpdate/` | Wrap `UpdateRestaurantHandler` to produce a `PriceHistory`-style audit entry for any of the listed mutating fields so changes are traceable across the system. |
| `Features/MenuItemAnalytics/` | Already has reads. Add `RecomputeTodayCommand` (admin) + automatic nightly recompute (`IHostedService`). The hot path is the `OrderCompleted` consumer. |
| `Features/CustomerFeedback/SubmitFeedback/` | `SubmitFeedbackCommand` accepting the four ratings + comments + `OrderId`. On `OverallRating ≥ 4` emit `FeedbackSubmittedIntegrationEvent` (lives in `BuildingBlocks.Messaging/Events/Notification/`). **Stays in Catalog** — the move to Notification is now out-of-plan (§7.6.1). |

### Phase 5 — Async lifecycle (reservations, walk-in, seasonal)

**Decision: Hangfire in Catalog.** Adds a `hangfire` schema in `catalogdb` for job storage and a recurring-job dashboard.

- `ReservationReminderJob` (Hangfire recurring, every 5 minutes):
  - Find `Reservation` rows where `Status = Confirmed`, `ReminderSent = false`, `ReservationDate + ReservationTime - 1h ≤ now`.
  - Publish a `ReservationReminderDueIntegrationEvent`. Notification consumes; sends WhatsApp/email.
  - Set `ReminderSent = true`, `ReminderSentAt`.
- `ReservationNoShowJob` (every 1 minute):
  - Find `Reservation` rows where `Status = Confirmed`, `ReservationDate + ReservationTime + 15m ≤ now`, `SeatedAt` is null.
  - Transition `Status = NoShow`, set `CancelledAt`.
- `WalkInNoShowJob` (every 1 minute):
  - Find `WalkInQueue` rows where `Status = Notified`, `NotifiedAt + 10m ≤ now`, `SeatedAt` is null.
  - Transition `Status = NoShow`. Free the held table.
- `SeasonalAvailabilityJob` (every 5 minutes):
  - For each `MenuItem` with `ItemType = Seasonal`, ensure `IsAvailable` reflects `SeasonStartDate ≤ today ≤ SeasonEndDate`.
  - Same logic for promo items vs `PromoStartDate` / `PromoEndDate`.
- All four jobs are feature-flag gated (`FeatureManagement__CatalogScheduledJobs=true`).
- **Tests:** integration tests using a fake clock (e.g. `Microsoft.Extensions.TimeProvider.Testing`) — not actual time travel, just controlled `TimeProvider`. Hangfire recurring-job timing assertions are out of scope.

### Phase 6 — Entity moves

This phase is split into three sub-phases. **Only §7.6.2 is in this plan.** The other two are out-of-plan prerequisites.

#### Phase 6.0 — Out-of-plan prerequisite: Ordering-side Reservation/WalkInQueue plan

**Verified:** Ordering has **no** `Reservation.cs` or `WalkInQueue.cs` anywhere in `Services/Ordering/` (only `Catalog.API/Models/` defines them). Today, all reservation + walk-in logic — CRUD endpoints, the booking flow, the queue worker, the no-show transitions, the Hangfire jobs — lives in Catalog.

A separate Ordering-side plan must introduce:

- `Reservation` aggregate (with state machine Pending → Confirmed → Seated → Completed / Cancelled / NoShow).
- `WalkInQueue` aggregate.
- The seven reservation CRUD endpoints that already exist in Catalog's `Features/Reservations/` and the five walk-in endpoints in `Features/WalkInQueues/`.
- The state transitions and the `TableStatusChanged` correlation logic.
- The reservation/block window logic (architecture §933-937: `reservation_time ± turnover_minutes`, no-show after 15 min).

Until that plan lands, Catalog keeps the writers, the tables, and the endpoints. The §7.5 Hangfire jobs run against Catalog's tables. When the Ordering-side plan ships, the table move follows §6.6 gateway convention and §7.6.2's backfill pattern.

#### Phase 6.1 — Out-of-plan prerequisite: Notification v1 plan

**Verified:** there is **no** `Notification.API` project under `Services/` (only Basket/Catalog/Discount/Identity/Kitchen/Ordering). `CustomerFeedback` and the relational `NotificationLog` model files live in Catalog and have no destination service.

A separate Notification v1 plan must introduce:

- The `Notification.API` service skeleton (Carter, JWT auth, Postgres, Marten if applicable).
- Notification deliveries: receipt generation (`OrderCompleted`), feedback request, reservation confirmations, reminders, etc. The integrations with Twilio/SendGrid from `architecture.md` §616.
- The `CustomerFeedback` aggregate and the reward-code generation flow (`architecture.md` §411-415, `FeedbackSubmittedIntegrationEvent` defined).
- A relational `NotificationLog` table (Marten document `NotificationLog` stays in Catalog; the relational one moves).

Until that plan lands, Catalog keeps `CustomerFeedback` and `NotificationLog`. The `FeedbackSubmittedIntegrationEvent` is still published (Phase 4 already does this) but no one consumes it yet — that's fine, the bus retains undelivered messages until the consumer exists (subject to its retry / dead-letter policy).

#### Phase 6.2 — **In plan: Coupon move to Discount**

**Verified:** Discount already has the destination schema (`Discount.Grpc/Models/Coupon.cs:6-15`) with all the columns Catalog's `Coupon` carries — *and* Discount's `Coupon` extends `AuditableEntity<int>`, so the move is a strict upgrade (audit fields + `IsActive` come along for free).

**Caveat:** Discount is gRPC-only today (`Discount.Grpc/Services/DiscountService.cs`). Catalog's Coupon features are REST (Carter). Two options:

- **A.** Move writers to Discount.gRPC and accept that the management surface stays gRPC-only. New HTTP consumers are added via gRPC client wrappers.
- **B.** Add a Carter REST surface to Discount alongside the existing gRPC service. Higher scope; recommended only if there's non-Basket HTTP traffic to coupons soon.

**Plan adopts option A** (consistent with the existing pattern: Basket already speaks gRPC to Discount).

Steps (mirrors §7.6 generic step pattern, but more concrete because the destination exists):

1. **Schema pre-flight:** confirm Discount's `Coupon` table is identical to Catalog's at the column level. Add any missing column via a Discount migration (none expected — verified matching fields at `Discount.Grpc/Models/Coupon.cs:6-15`).
2. **Backfill:** one-shot data migration script (`Catalog.Coupons → Discount.Coupons`). Idempotent (`INSERT ... ON CONFLICT DO NOTHING` keyed by `Code + RestaurantId` since `Code` is `UK`).
3. **Move writers:** port the four Catalog `Features/Coupons/*` handlers (Create/Update/Delete/Get) to Discount.gRPC service methods. Cover the gRPC service already has `GetDiscount` / `CreateDiscount` / `UpdateDiscount` / `DeleteDiscount`; add `RedeemDiscount` parity with Catalog's redemption logic if present.
4. **Switch Catalog to read-only source table.** Add an EF interceptor that throws on `INSERT/UPDATE/DELETE` against the Catalog `Coupons` table. Catalog's REST endpoints either proxy to Discount via gRPC (option A) or are deprecated; clients have one release to migrate.
5. **Gateway route migration per §6.6:**
   - Add `discount-api/coupons/{**catch-all}` pointing to `discount-cluster`.
   - If any HTTP consumer still calls `catalog-api/coupons/...`, change its `ClusterId` from `catalog-cluster` to `discount-cluster` for that path.
6. **Drop Catalog source table:** delete the `Coupon` model + `DbSet` from `CatalogDbContext`; add a no-op `2026XX` migration that drops `Coupons` from Catalog's schema. Delete the `Features/Coupons/` folder.
7. **Update docs:** `db_relational_model.mermaid` updated to mark Coupon as Discount-owned with "moved from Catalog on YYYY-MM-DD" annotation; companion md updated identically.
8. **Feature flag:** `Catalog:EntityMoveCoupons=true` for the entire phase. Lets us roll back without a redeploy.

**Tests:** Migration idempotency test (run twice, second run is a no-op). End-to-end test that Discount.gRPC `CreateDiscount` → Catalog's read-only table reflects the value before step 4 (read-only mirror window) and stops reflecting after step 6 (table dropped).

---

## 8. Cross-cutting notes

### Cross-service coordination rules

These are the rules every Catalog change must follow when it touches data or contracts shared with other services. They were settled in the csharp-expert pass and apply regardless of phase.

- **Event versioning.** Every Catalog integration event carries `int SchemaVersion` (current = 1). Adding fields bumps the version; consumers ignore unknown fields by MassTransit default. Removing or renaming a field requires introducing the next major version side-by-side, publishing both for one release, then dropping the old version. Documented in §6.5.
- **Cascade-delete policy.** All shared FKs use `OnDelete(DeleteBehavior.Restrict)`. Soft-delete only. Application layer raises a friendly 409 with a list of FK references when a delete is blocked. Cascade is never at the database level.
- **Migration ownership.** Catalog owns its own EF migrations. Cross-cutting changes (column rename on a shared FK; new required column referenced by another service) land first in Catalog, then the consumer's read paths are updated, then a coordinated migration script is documented in the PR description.
- **Cache failure policy.** Cache calls are best-effort with `Warning`-level logging on failure. `CacheDriftRepairService` is the safety net. Writes never block on Redis.
- **Health check policy.** `/live` for liveness only. `/ready` checks Postgres, Redis, RabbitMQ, and the outbox dead-letter count against `CatalogOptions:OutboxDeadLetterThreshold`. Tripping any of them takes Catalog out of the LB. Threshold is config; default = 0.
- **Engine trigger.** `IDomainEvent` + `DispatchDomainEventsInterceptor` (in-process, same transaction). Reconcile hosted service is a flag-gated safety net, off by default.

### Code-smell carryovers from `db_relational_model.md` §137-148

These are *not* the focus of this plan but should be cleaned up during the relevant phase — small enough to fold in:

- **§1 `Basket.MenuItemId` type** — `Catalog.API/Models` already has `MenuItem.Id : Guid`. Whatever fixes Basket's embedded `BasketItem.MenuItemId` from `int` to `Guid` happens in Basket; Catalog is unaffected.
- **§2 Four Marten documents extend `Entity<int>` but Marten assigns `Guid`** — fix in Phase 4 once the documents have stabilized. **Marten documents are not relational entities**; they should *not* extend `AuditableEntity<>`, `Entity<int>`, or `Entity<Guid>`. Drop the base class entirely and let Marten own the id (synthetic `Guid` by default; `[HiloSequence]` for integer ids if needed; `[Identity]` if a natural key is in play). Update the mermaid labels in the same PR.
- **§3 `BulkOrderUploads.CreatedAt`** — change base to `AuditableEntity<int>` in Phase 4. The entity stays in Catalog (per §7.6.0) — there's no Ordering move to sync with.

### Testing strategy

- **Unit tests** (xUnit + FluentAssertions + Moq): pure logic — engine rules, handler happy paths, validation. No infrastructure. Fast.
- **Testcontainers** (Postgres + Redis + RabbitMQ) for integration tests of:
  - Cache decorator: hit / miss / invalidation hooks fire; drift-repair repopulates after Redis restart.
  - Outbox publisher: events committed in the same transaction as the mutation; dispatcher hosted service delivers them; dead-letter probe exposes count.
  - Engine end-to-end: ingredient update → IDomainEvent → engine recompute → `MenuItemAnalytics`-like audit log.
  - Coupon move: data migration idempotency; end-to-end gRPC round trip.
- **Fake `IBus`, `IDistributedCache`, `IConnectionMultiplexer`** for handler unit tests that don't need real infrastructure.
- **MassTransit `InMemoryTestHarness`** for consumer unit tests (`OrderCompleted` consumer).
- **Fake clock** (`Microsoft.Extensions.TimeProvider.Testing`) for the Hangfire job logic; actual Hangfire timing is not asserted.

### Observability

- Per-feature cache hit/miss counters exposed via a small `/_internal/cache-stats` endpoint under `MapCarter`.
- Engine recompute counters and last-reconcile timestamp on `/ready` under the `availability_engine` key.
- Outbox dead-message count surfaced under `outbox.dead_message_count` (and via `/ready`).
- Standard Serilog structured logging (already in place). Add a correlation-id enrichment that flows from inbound HTTP → outbox row → MassTransit header → consumer (matches `architecture.md` AI-agent guidance §1009).

### Migration / rollout

- Phases 1–5 are **additive** and feature-flag gated. Each can ship behind its flag and roll back without DB changes.
- Phase 6.2 is the **only** sub-phase with a DB migration in this plan. Sequence behind a release notes flag `Catalog:EntityMoveCoupons=true`.

### Resolved architectural decisions

These were tracked as open in plan v1.0; verified against the code and closed in v1.1.

| # | Decision | Verified by | Outcome |
|---|---|---|---|
| 13 | Ordering has Reservation/WalkInQueue aggregates today? | Glob over `Services/Ordering/**/Reservation.cs` and `WalkInQueue.cs` — both empty | **No.** Out-of-plan prerequisite: Ordering-side plan (§7.6.0). |
| 14 | Notification.API skeleton exists? | `ls Services/` — only Basket/Catalog/Discount/Identity/Kitchen/Ordering | **No.** Out-of-plan prerequisite: Notification v1 plan (§7.6.1). |
| 15 | Discount already has Coupon table? | `Discount.Grpc/Models/Coupon.cs:6-15` — fields match Catalog's; extends `AuditableEntity<int>` | **Yes.** In-plan move §7.6.2. |
| 16 | YARP route migration convention? | Read `ApiGateway/YarpApiGateway/appsettings.json` — config-driven, no precedent for cross-cluster moves | **Decided:** atomic `ClusterId` change, keep public path stable (§6.6). |

---

## 9. Milestone checklist

- [ ] **Phase 1** — Redis cache wired behind `CatalogRedisCache` flag; menu and ingredient invalidation hooked into existing handlers; `CacheDriftRepairService` running every 5 min; failure mode is fail-open + log.
- [ ] **Phase 2** — All five Catalog integration events (each with `int SchemaVersion = 1`) publish via outbox; plain `IConsumer<OrderCompletedIntegrationEvent>` handler bumps `MenuItemAnalytics` idempotently; outbox poison queue + `OutboxDeadLetterProbe` reading 0; `/live` + `/ready` split in place.
- [ ] **Phase 3** — IngredientAvailabilityEngine with unit-test matrix; `IDomainEvent` triggers via `DispatchDomainEventsInterceptor`; reconcile hosted service gated by `CatalogAvailabilityEngineReconcile` flag.
- [ ] **Phase 4** — `MergedTables`, `MenuSubCategory.Delete`, `ComboItems.Update`, `BulkOrderUploads` (CRUD + approve/reject; stays in Catalog per §7.6.0), `PriceHistory` write path, `MenuItemAnalytics` nightly recompute, `CustomerFeedback.Submit` + reward event (stays in Catalog per §7.6.1).
- [ ] **Phase 5** — Hangfire + recurring jobs: reservation reminder, reservation no-show, walk-in no-show, seasonal availability. All gated by `CatalogScheduledJobs` flag.
- [ ] **Phase 6.0** — *Out-of-plan.* Track Ordering-side plan introducing `Reservation` / `WalkInQueue` aggregates. Open until the Ordering-side plan lands.
- [ ] **Phase 6.1** — *Out-of-plan.* Track Notification v1 plan introducing `CustomerFeedback` / relational `NotificationLog`. Open until the Notification v1 plan lands.
- [ ] **Phase 6.2** — `Coupon` move to Discount: schema pre-flight, backfill, writers ported to `Discount.Grpc`, Catalog source table read-only, gateway re-pointed per §6.6, source table dropped, mermaid + companion md updated. Gated by `Catalog:EntityMoveCoupons`.
- [ ] **Cleanup** — Four Marten documents (`OrderSnapshot`, `OrderModificationLog`, `OrderItemPriceAudit`, `NotificationLog`) drop the `Entity<int>` base; they are plain Marten documents with no relational base class. `BulkOrderUploads` becomes `AuditableEntity<int>`. Mermaid + companion doc both updated to reflect the new bases / id conventions.
- [ ] **Docs** — `db_relational_model.mermaid` updated to match each phase (mermaid is reconciled after every phase, not only at the end).

---

**Document Version:** 1.1
**Last Updated:** 2026-07-10
**Maintained By:** Catalog working group

For the schema-level drift baseline, see `db_relational_model.md` last reconciliation 2026-06-30 and the memory `db-model-drift-reports.md`.
