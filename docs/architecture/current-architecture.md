# Orderly Microservices — Current Implementation

> This file describes the system **as it is implemented today**. It is the snapshot
> view of the codebase — no planned features, no gap list. As new functionality is
> built (services, endpoints, events, caching, observability), update this file to
> match.
>
> For the full design picture, including planned-but-unbuilt features (Kitchen /
> Notification / AI services, React frontend, Azure infra, SignalR, Serilog, Hangfire,
> SendGrid / Twilio, the status state machine, bill splitting, customer rewards, the
> bulk-upload flow, the feedback pipeline, performance targets, and the API envelope
> convention), see [`architecture.md`](./architecture.md) §13.

---

## 1. Project Overview

Orderly is a **multi-brand, multi-restaurant** back office platform. It manages restaurants under a shared brand, with menu, tables, reservations, walk-in queue, ordering, basket/checkout, discounts, identity, and operational analytics. The runtime is a set of cooperating .NET services behind a YARP gateway; data lives in three SQL stores plus Redis and RabbitMQ.

- **Tenancy:** Brand → (many) Restaurants → (operations under that restaurant).
- **Initial scale (planned):** 10–20 concurrent users per restaurant, ~15 orders/hour peak.
- **Tech stack:** .NET 10 + ASP.NET Core / Carter / Minimal APIs + PostgreSQL + MS SQL Server + SQLite + Redis + RabbitMQ (MassTransit).
- **Architecture:** Microservices + event-driven (RabbitMQ via MassTransit) + synchronous gRPC (Discount) + YARP API Gateway.

---

## 2. Tech Stack

| Layer | What is wired | Version |
|---|---|---|
| Runtime | .NET (pinned via `global.json`) | **10.0.203** (`rollForward: latestFeature`) |
| HTTP framework | **Carter** (Minimal-API `ICarterModule`) | 10.0.0 |
| ORM (relational) | EF Core | 10.0.9 — Postgres + Sqlite + SqlServer providers |
| Document store | **Marten** | 8.37.0 — Catalog (4 docs) + Basket (per-tenant databases) |
| Cache (distributed) | `Microsoft.Extensions.Caching.StackExchangeRedis` | 10.0.8 — shared `distributedcache` container; clients in `Basket.API` and `Catalog.API` |
| Messaging | **MassTransit + RabbitMQ** | 8.5.10 — `rabbitmq:3-management` in compose; Catalog (Basket, Ordering, Kitchen) all consume; Catalog published events begin with Phase 2. |
| Auth | **OpenIddict** + `Microsoft.AspNetCore.Authentication.JwtBearer` | 7.5.0 + 10.0.9 (BuildingBlocks pin) — OpenIddict is the OIDC server + bearer validation in every service; JwtBearer is the wire-level validator the services call. Discount.Grpc is the first service whose gRPC surface is fully gated (see §4.4 Discount). |
| Mapping | **Mapster** | 10.0.7 |
| Validation | FluentValidation | 12.1.1 — open behavior on `ICommand` only |
| Mediator | MediatR | 14.1.0 — BuildingBlocks provides `ICommand<TResponse>` / `IQuery<TResponse>` |
| Time | **NodaTime** | 3.3.2 — `Instant` / `LocalDate` across the schema |
| Scheduling | **Hangfire** (PostgreSQL storage) | 1.8.14 server + 1.20.10 PostgreSQL storage — `hangfire` schema in `catalogdb`; recurring-job dashboard at `/hangfire` (Admin/Manager role); four recurring jobs (reservation reminder, reservation no-show, walk-in no-show, seasonal availability) gated by `FeatureManagement__CatalogScheduledJobs` |
| Gateway | **YARP** | 2.3.0 |
| Resilience | `Microsoft.AspNetCore.RateLimiting` (built-in) | — Fixed-window on Identity (5/15min/IP) and YARP (10/1min/host) |
| Health | `AspNetCore.HealthChecks.{NpgSql,Redis,SqlServer,Rabbitmq,UI.Client}` | 9.0.0 (SqlServer/NpgSql/Redis) + 8.0.2 (Rabbitmq) — every service exposes `/health`. The Rabbitmq check is wired on `Kitchen.API`, `Ordering.API`, and `Basket.API` under entry `messagebroker` (tags `["broker", "ready"]`). Catalog exposes a Redis check (`/health` reports `redis: Healthy` when the cache client is reachable). |
| Feature flags | `Microsoft.FeatureManagement.AspNetCore` | 4.5.0 — registered in Ordering, Kitchen, and Catalog; `OrderFullfilment` (Ordering) and `CatalogRedisCache` (Catalog) are the first two flags |
| Decorator / DI helpers | **Scrutor** | 7.0.0 — `services.Decorate<TInterface, TDecorator>()` in `Basket.API` (`CachedBasketRepository`) and `Catalog.API` (`CachedMenuReader`) |
| API style | Carter modules + MediatR commands/queries | — DTOs and validators co-located under `Features/<Entity>/` |
| Test infra (services that need Postgres/Redis in tests) | `Testcontainers.PostgreSql` / `Testcontainers.Redis` | 4.1.0 — `Catalog.API.Tests` brings Postgres + Redis Testcontainers; `Ordering.API.Tests` brings MSSQL + RabbitMQ |
| Logging | ASP.NET Core default `ILogger` | — |
| Frontend | none in-repo | — |

---

## 3. Solution Layout

Source root: `orderly-microservices/`. 12 projects + 5 test projects + a Docker compose project.

```
orderly-microservices/
├── ApiGateway/
│   └── YarpApiGateway/                         # YARP front door (port 6004 / 6064)
├── BuildingBlocks/                             # Shared lib (CQRS, Behaviors, Authorization, Multitenancy, Entities)
├── BuildingBlocks.Messaging/                   # MassTransit + IntegrationEvent base + Outbox dispatcher helper
├── Services/
│   ├── Catalog/Catalog.API/                    # Brands, restaurants, tables, menus, reservations, snapshots, Redis cache + Scrutor decorator
│   ├── Catalog/Catalog.API.Tests/              # xUnit + FluentAssertions + NSubstitute + Testcontainers (Postgres + Redis) for the menu cache decorator + options validation
│   ├── Basket/Basket.API/                      # Marten + Redis cache, gRPC client to Discount, publishes BasketCheckoutEvent
│   ├── Basket/Basket.API.Tests/                # xUnit + FluentAssertions + NSubstitute — Phase 1 unit tests on identity guard, redaction, handler; Testcontainers / Verify scaffolding lands in Phase 5
│   ├── Discount/Discount.Grpc/                 # gRPC server, SQLite store, single Coupon entity
│   ├── Identity/Identity.API/                  # OpenIddict + ASP.NET Identity + RBAC permissions
│   ├── Kitchen/Kitchen.API/                    # Kitchen fulfilment, SignalR hub, Postgres `kitchendb`, transactional outbox
│   ├── Kitchen/Kitchen.API.Tests/              # xUnit + FluentAssertions + Testcontainers (Postgres + RabbitMQ) for the Kitchen API
│   └── Ordering/
│       ├── Ordering.Domain/                    # Aggregate<Order> with 7 state-transition methods, OrderItem (per-item prep state), value objects, exceptions
│       ├── Ordering.Application/               # MediatR commands/queries, domain + integration handlers, Outbox publisher wiring
│       ├── Ordering.Infrastructure/            # EF Core + MSSQL, interceptors, outbox_messages table, Outbox dispatcher hosted service
│       ├── Ordering.API/                       # 13 Carter endpoints (6 customer/admin + 7 Kitchen state-transition), no in-assembly MassTransit consumer
│       └── Ordering.API.Tests/                 # xUnit + FluentAssertions + Testcontainers (MSSQL + RabbitMQ) for the new endpoints + /health
└── ...
```

**Naming convention:**
- C#: PascalCase types, camelCase locals.
- DB tables/columns: PascalCase.
- Routes: kebab-case. All Carter modules sit under `/api/v1`.

---

## 4. Microservices

Docker host ports are **6000–6005, 6007** (HTTP) and **6060–6065, 6067** (HTTPS). Inside the container, Kestrel listens on `8080`/`8081`.

| Service | Container | HTTP | HTTPS | Notes |
|---|---|---|---|---|
| Catalog.API | `catalog.api` | 6000 | 6060 | Postgres + Marten |
| Basket.API | `basket.api` | 6001 | 6061 | Postgres + Marten + Redis + RabbitMQ + gRPC client |
| Discount.Grpc | `discount.grpc` | 6002 | 6062 | gRPC only (HTTP/2). SQLite file. |
| Ordering.API | `ordering.api` | 6003 | 6063 | MSSQL + 13 Carter endpoints (6 customer/admin + 7 Kitchen state-transition `kitchen:update_prep_status`) + transactional outbox (`outbox_messages`, hosted dispatcher) |
| YarpApiGateway | `yarpapigateway` | 6004 | 6064 | YARP, fixed-window rate limit |
| Kitchen.API | `kitchen.api` | 6005 | 6065 | Postgres (`kitchendb`) + SignalR `/hubs/kitchen` — domain, read + command endpoints, outbound integration events, live broadcast, transactional outbox (`outbox_messages` table, `KitchenOutboxPublisher` interceptor + `KitchenOutboxDispatcher` hosted service), `/health` (EF Core `KitchenDbContext` check + RabbitMQ broker check `messagebroker`), and `Microsoft.FeatureManagement` registration |
| Identity.API | `identity.api` | 6007 | 6067 | OpenIddict server + ASP.NET Identity |

Gateway public prefixes (`appsettings.json`):

| Upstream path | → | Cluster |
|---|---|---|
| `/catalog-api/{**catch-all}` | → | `catalog-cluster` (http://catalog.api:8080) |
| `/basket-api/{**catch-all}` | → | `basket-cluster` (http://basket.api:8080) |
| `/discount-api/{**catch-all}` | → | `discount-cluster` (http://discount.grpc:8080) |
| `/ordering-api/{**catch-all}` | → | `ordering-cluster` (http://ordering.api:8080) |
| `/kitchen-api/{**catch-all}` | → | `kitchen-cluster` (http://kitchen.api:8080) |
| `/identity-api/{**catch-all}` | → | `identity-cluster` (http://identity.api:8080) |

---

### 4.1 Identity Service (Port 6007 / 6067)

**Surface:** Carter HTTP + OpenIddict OAuth/OIDC endpoints.

**Authentication model.** OpenIddict 7.5 with EF Core on Postgres. Password, authorization-code-with-PKCE, and refresh-token flows are allowed; default scopes are `openid email profile offline_access`; access-token lifetime 15 min, refresh-token lifetime 7 days (configurable via `Jwt.AccessTokenLifetimeMinutes` / `Jwt.RefreshTokenLifetimeDays`).

**Authorization model.** Each permission is a string name (no enum) enforced via `EndpointRouteBuilderExtensions.RequirePermission(...)`. That helper builds a policy `Permission:<name>` that a custom `PermissionAuthorizationHandler` matches against the user's `permissions` claim list. There are **25 seeded permissions** under resources `users / roles / permissions / orders / menu / kitchen / reservations / payments / audit`:

```
users:view_all          users:create           users:edit
users:assign_roles      users:assign_restaurants
roles:view              roles:edit             roles:edit_permissions
permissions:view
orders:create           orders:view_own        orders:view_all
orders:modify_ordering  orders:modify_confirmed  orders:modify_ready
menu:view               menu:edit
kitchen:view_orders     kitchen:update_prep_status
reservations:view       reservations:create    reservations:edit
payments:process        payments:split_bill    payments:view_reports
audit:view
```

**Roles (8, seeded by `DataSeeder.cs`):** `SuperAdmin`, `RestaurantAdmin`, `Manager`, `KitchenManager`, `Waiter`, `KitchenStaff`, `Host`, `Cashier`. `SuperAdmin` is granted every permission; the others have explicit allowlists. A `SuperAdmin` user (`admin@orderly.com`) is seeded at startup.

**Multi-restaurant access.** `UserRestaurant` (composite PK `UserId + RestaurantId`, plus `IsDefault`). `ClaimsTransformer.GenerateClaimsAsync` reads the user's `UserRestaurants` rows and emits one `restaurantId` claim (the default, or the first row) plus one `permissions` claim per permission granted by the user's roles. Assignments are driven from `AssignRestaurantsCommand`.

**Claims produced per token:**
`NameIdentifier` (Guid), `Email`, `Name`, `firstName`, `lastName`, `isActive`, one `Role` claim per role name, one `restaurantId` claim, one `permissions` claim per permission.

**Lockout & throttling:**
- Lockout 5 failed attempts → 30 min, applied at both `/api/auth/login` and `/connect/token`.
- Password: length ≥ 8, requires digit + non-alphanumeric + upper + lower, unique email.
- Global fixed-window rate limit: 5 requests / 15 min **per remote IP**.

**Audit.** `LoginAuditLog` rows are written by `AuditLogger.LogAsync` for events `RegisterSuccess`, `LoginSuccess`, `LoginFailure`, `AccountLocked`, `Logout`, `TokenIssued`, `TokenFailure`, `TokenRefreshed`. Read back via `GET /api/audit-log` (paginated, filters by `UserId`/`EventType`).

**Refresh tokens.** Implemented through `/connect/token` with `grant_type=refresh_token`. Tokens are revoked at logout via `IOpenIddictTokenManager.TryRevokeAsync`.

**Standard OpenIddict endpoints:** `/connect/authorize`, `/connect/token`, `/.well-known/openid-configuration`, `/.well-known/jwks.json`, `/connect/userinfo` are auto-mounted.

**Carter module surface:**

| Method | Route | Permission |
|---|---|---|
| POST | `/api/auth/register` | public |
| POST | `/api/auth/login` | public |
| POST | `/api/auth/logout` | authenticated |
| GET/POST | `/api/users`, `/api/users/{id}`, `…/{id}/roles`, `…/{id}/restaurants` | varies |
| GET/POST | `/api/roles`, `/api/roles/{id}`, `…/{id}/permissions` | varies |
| GET | `/api/permissions` | `permissions:view` |
| POST | `/api/permissions/assign-to-role` | `roles:edit_permissions` |
| GET | `/api/audit-log` | `audit:view` |
| GET | `/health` | public |

---

### 4.2 Catalog Service (Port 6000 / 6060)

**Surface:** Carter modules under `/api/v1`. Postgres for relations (Npgsql + EF Core) **and** Marten for 4 event/log documents in the same database.

**This service is the de-facto "operations" service for the restaurant — it owns a far wider footprint than the name suggests.** It is the only service with the restaurant and table domain, and it also owns every operational concern outside Ordering/Basket/Discount/Identity (reservations, walk-in queue, customer feedback, menu, ingredients, order snapshots, modification logs, price audit, menu analytics, notifications log, bulk-order upload staging).

**Entities.** Every entity below is in `Catalog.API/Models/`:

| Entity | Storage | Notes |
|---|---|---|
| `Brand` | relational | Multi-brand container (`Name`, `LogoUrl`, `ContactEmail/Phone`, `CuisineType`) |
| `Restaurant` | relational | FK `BrandId`. Holds `TaxRate`, `Currency`, `TimeZone`, `EstimatedTurnoverMinutes`, `AutoConfirmReservations`, `AutoConfirmOrders`, `AllowAutoSubstitute` |
| `Table` | relational | Number/capacity/position/shape per restaurant |
| `MergedTable` | relational | Parent-child table grouping |
| `MenuCategory` | relational | Soft-delete (`!IsDeleted`) |
| `MenuSubCategory` | relational | Child of category; soft-delete (`!IsDeleted`) — `DeleteMenuSubCategory` |
| `MenuItem` | relational | `RestaurantId`, base price, prep times, availability, soft-delete |
| `MenuItemVariation` | relational | Size / spice / price modifier |
| `ComboItem` | relational | Combo definition referencing child menu items |
| `Ingredient` | relational | Per restaurant, stock + availability + min stock |
| `MenuItemIngredient` | relational | Quantity required + optional flag |
| `IngredientAlternative` | relational | Original→alternative mapping, auto-substitute flag |
| `PriceHistory` | relational | Audit of price changes; auto-populated by `IPriceHistoryRecorder` from every price-mutating handler (Phase 4) |
| `Reservation` | relational | Status workflow via endpoints (`POST` create, `PUT …/{id}/seat|confirm|cancel`) |
| `WalkInQueue` | relational | `POST` create, `PUT …/{id}/seat|notify`, `DELETE …/{id}` |
| `CustomerFeedback` | relational | `GET …/feedback`, `GET …/{id}`, `POST …/feedback` (SubmitFeedback — Phase 4) |
| `MenuItemAnalytics` | relational | Read-only aggregated stats; nightly `MenuItemAnalyticsNightlyRecomputeService` re-validates today (Phase 4); `POST …/analytics/menu-items/recompute-today` admin action |
| `OrderTimingAnalytics` | relational | DbSet |
| `BulkOrderUpload` | relational | `AuditableEntity<int>` (Phase 4 — added audit columns); `POST …/bulk-order-uploads` upload, `GET …/{id}`, `POST …/{id}/approve`, `POST …/{id}/reject` |
| `User` | relational | Domain mirror of the Identity user (Role enum + `RestaurantId` FK) |
| `OrderSnapshot` | Marten document | Cleanup 2026-07-11: synthetic `Guid Id`, no relational base class (previously `: Entity<int>`) |
| `OrderModificationLog` | Marten document | Cleanup 2026-07-11: synthetic `Guid Id`, no relational base class (previously `: Entity<int>`) |
| `OrderItemPriceAudit` | Marten document | Cleanup 2026-07-11: synthetic `Guid Id`, no relational base class (previously `: Entity<int>`) |
| `NotificationLog` | Marten document | Out-of-plan: being removed entirely per §6.7 of `CATALOG_SERVICE_PLAN.md` once Notification v1 lands (synthetic `Guid Id`, no relational base; no `Entity<int>` to drop on this one) |

**Endpoints by feature (Carter modules, all under `/api/v1`):**
`Brands`, `Restaurants`, `Tables`, `MergedTables`, `Reservations`, `WalkInQueues`, `MenuCategories`, `MenuSubCategories`, `MenuItems`, `MenuItemVariations`, `MenuItemIngredients`, `ComboItems`, `Ingredients`, `IngredientAlternatives`, `PriceHistories`, `CustomerFeedback`, `MenuItemAnalytics`.

**Events published / consumed by Catalog.** Four integration events publish via the outbox (gated by `FeatureManagement__CatalogMenuEvents`, default `true`); one event is consumed.

| Event | Direction | Payload |
|---|---|---|
| `MenuItemChangedIntegrationEvent` (`ChangeType ∈ Created, Updated, Deleted`) | publish | `MenuItemId`, `RestaurantId`, `ChangeType`, optional `Name`/`BasePrice`/`IsAvailable`/`AvailabilityStatus` |
| `IngredientAvailabilityChangedIntegrationEvent` | publish | `MenuItemId`, `RestaurantId`, `AvailabilityStatus`, optional `AutoSubstituteOf` (int — alternative `Ingredient.Id`) |
| `TableStatusChangedIntegrationEvent` | publish | `TableId`, `RestaurantId`, `NewStatus`, optional `CurrentOrderId` |
| `RestaurantConfigurationChangedIntegrationEvent` | publish | `RestaurantId`, `ChangedFields: IReadOnlyList<string>` |
| `FeedbackSubmittedIntegrationEvent` | publish (Phase 4) | `FeedbackId`, `RestaurantId`, `OrderId`, `OverallRating`, `Comments`, `RewardType`, `RewardDescription`, `RewardValue` — emitted by `SubmitFeedback` when `OverallRating ≥ 4`. Notification service is the intended consumer (out-of-plan per §7.6.1) |
| `OrderCompletedIntegrationEvent` | consume | `OrderId`, `RestaurantId`, `CompletedAt`, `Items: IReadOnlyList<OrderCompletedItem>` |

**Ingredient Availability Engine (Phase 3).** Pure-function calculator at `Catalog.API/Availability/IngredientAvailabilityEngine.cs` that takes a menu item's recipe (`MenuItemIngredient` rows), the availability of every referenced ingredient + alternative target, and `Restaurant.AllowAutoSubstitute`; returns `IngredientAvailabilityProfile` (`Available` | `Limited(autoSubstituteOf?)` | `Unavailable`). Allocation-free in steady state (per §0.3.9). Triggered by in-process `IDomainEvent` raised on `Ingredient` / `IngredientAlternative` / `MenuItemIngredient` mutations, drained by `DispatchDomainEventsInterceptor` (pre-commit, mirror of Ordering/Kitchen) via MediatR. `IngredientAvailabilityChangedDomainEventHandler` (single `INotificationHandler<IDomainEvent>` switch) loads the engine inputs, calls the engine, writes `MenuItem.AvailabilityStatus` via a nested `SaveChangesAsync`, invalidates the menu cache, and publishes `IngredientAvailabilityChangedIntegrationEvent` via `IOutboxPublisher` — all in the same transaction. `IngredientAvailabilityReconcileService` (BackgroundService) is the safety-net sweep: enumerates every menu item, dispatches synthetic `MenuItemIngredientChangedDomainEvent`s, and lets the existing handler recompute. Self-gates on `FeatureManagement__CatalogAvailabilityEngineReconcile` (default `false`); cadence `Catalog:AvailabilityRecurrenceIntervalMinutes` (default 1). Domain-event abstractions (`IDomainEvent`, `IAggregate`, `Aggregate<TId>`, `AuditableAggregate<TId>`) are Catalog-local (per-service duplication, mirrors Kitchen); only `BuildingBlocks.Entities.Contracts.Entity<TId>` / `AuditableEntity<TId>` is shared.

**Async lifecycle — Hangfire recurring jobs (Phase 5).** Four cron-scheduled jobs in `Catalog.API/Scheduling/`:
- `ReservationReminderJob` — every 5 minutes (`*/5 * * * *`); finds `Confirmed` reservations with `ReminderSent = false` whose reservation time is between 55–65 minutes away, publishes `ReservationReminderDueIntegrationEvent` via `IOutboxPublisher`, stamps `ReminderSent = true` + `ReminderSentAt`.
- `ReservationNoShowJob` — every minute; transitions `Confirmed` reservations to `NoShow` once they're 15 minutes past their reservation time and have no `SeatedAt`. Frees the held `Table` back to `Available` if one was assigned.
- `WalkInNoShowJob` — every minute; transitions `Notified` walk-in parties to `NoShow` once the 10-minute response window expires. Frees the held `Table` back to `Available`.
- `SeasonalAvailabilityJob` — every 5 minutes; for each `MenuItem` with `ItemType = Seasonal` or `Promo`, sets `IsAvailable` based on `SeasonStartDate ≤ today ≤ SeasonEndDate` (seasonal) or `PromoStartDate ≤ now ≤ PromoEndDate` (promo). Soft-deleted items are skipped. The job does not publish a `MenuItemChanged` event (the read-side cache picks up the value on the next menu-tree read).

All four jobs are **feature-flag gated** (`FeatureManagement__CatalogScheduledJobs`, default `false`). When the flag is off, the cron tick fires but each job's first action is a `IsEnabledAsync` check that returns without doing work — same self-gating pattern as `CacheDriftRepairService`. Cron expressions are configurable in `CatalogOptions:Hangfire` (`ReservationReminderCron`, `ReservationNoShowCron`, `WalkInNoShowCron`, `SeasonalAvailabilityCron`); `MaxRowsPerTick` (default 500) bounds transaction length; `WorkerCount` (default 4) is the Hangfire server worker pool size. Each job resolves its own `CatalogDbContext` via `IServiceScopeFactory.CreateAsyncScope()` so the lifetime is bounded to the tick — the outer `AddHangfireServer` is a Singleton. `[AutomaticRetry(Attempts = 3, DelaysInSeconds = [30, 60, 120])]` decorates each `RunAsync`. `ReservationReminderJob` and `ReservationNoShowJob` use UTC anchoring of `ReservationDate + ReservationTime` (per-restaurant `TimeZone` is a Phase 6.0 concern). The Hangfire dashboard is mounted at `/hangfire` with a `HangfireAdminOnlyFilter` that requires the JWT `Admin` or `Manager` role claim.

**Phase 4 — Complete vertical slices.** Six new endpoint groups ship:
- `DeleteMenuSubCategory` (`DELETE /api/v1/menu-sub-categories/{id}`) — soft-delete (idempotent on already-deleted rows).
- `UpdateComboItem` (`PUT /api/v1/combo-items/{id}`) — quantity / `isOptional`; validates `IncludedMenuItemId` still exists.
- `BulkOrderUploads` (`POST /restaurants/{rid}/bulk-order-uploads`, `GET …/{id}`, `POST …/{id}/approve`, `POST …/{id}/reject`) — uploads are parsed client-side to JSON rows; the handler runs lightweight validation (menu item ids exist, table availability) and persists the batch envelope with `ErrorLog`. Approve / reject are idempotent on already-completed / already-failed rows. `BulkOrderUpload` base class flipped from `Entity<int>` to `AuditableEntity<int>` to carry the audit columns. The handler resolves the operator via `ICurrentUser` (Catalog-local abstraction mirroring `Kitchen.API`'s).
- `RecomputeTodayAnalytics` (`POST /api/v1/restaurants/{rid}/analytics/menu-items/recompute-today`) — admin drift-repair action.
- `MenuItemAnalyticsNightlyRecomputeService` — `BackgroundService` that runs daily at `MenuItemAnalyticsNightly:RunAtHour` (default `3`, `[Range(0, 23)]`); re-validates today's analytics rows for negative values.
- `SubmitFeedback` (`POST /api/v1/restaurants/{rid}/feedback`) — accepts the four ratings + comments + `OrderId`; issues a 10% reward code on `OverallRating ≥ 4` and publishes `FeedbackSubmittedIntegrationEvent` (gated by `FeatureManagement__CatalogFeedbackEvents`).

The shared `IPriceHistoryRecorder` (Scoped, in `Catalog.API/Features/PriceHistories/CreatePriceHistory/`) is invoked by every price-mutating handler — `UpdateMenuItem` (`BasePrice`), `UpdateMenuItemVariation` (`PriceModifier`), `UpdateIngredientAlternative` (`PriceModifier`), and `UpdateRestaurant` (`TaxRate` / `EstimatedTurnoverMinutes` via the new `PriceType.RestaurantConfiguration` enum value). The recorder skips no-op writes when `oldPrice == newPrice`. All audit rows commit in the same EF Core transaction as the mutation.

The four publish events live under `BuildingBlocks.Messaging/Events/Catalog/`. The `OrderCompletedIntegrationEvent` lives at `BuildingBlocks.Messaging/Events/OrderCompletedIntegrationEvent.cs` (introduced by Catalog's Phase 2 because Catalog is the first consumer; Ordering's publish side lands in a separate Ordering plan). Publishing is at-least-once via the `IOutboxPublisher` pattern — handlers call `await outbox.PublishAsync(new XxxIntegrationEvent { ... }, ct)` after `await dbContext.SaveChangesAsync(...)` and the same EF Core transaction persists both the aggregate mutation and the `outbox_messages` row. The `CatalogOutboxDispatcher` (Postgres `FOR UPDATE SKIP LOCKED` claim, multi-replica safe) relays rows to RabbitMQ via MassTransit. The `OrderCompletedIntegrationEventHandler` (`Catalog.API/Messaging/EventHandlers/`) is idempotent on `(OrderId, MenuItemId)` via a `processed_order_items` table — composite PK throws on duplicate, the handler catches `PostgresException.SqlState == "23505"` and skips. The handler upserts `MenuItemAnalytics` rows keyed by `(MenuItemId, AnalysisDate = UTC date)`.

**Transactional outbox.** `outbox_messages` and `outbox_messages_dead` tables live in `catalogdb` next to the relational data; `processed_order_items` carries the `OrderCompleted` idempotency log. All three are EF Core–configured in `Catalog.API/Data/CatalogDbContext.OnModelCreating` and ship as three Postgres migrations (`AddOutboxMessages`, `AddOutboxDeadMessages`, `AddProcessedOrderItems`). The publisher is scoped (pigs back on the ambient `CatalogDbContext` change tracker); the dispatcher is a singleton hosted service, gated by `Outbox:Enabled` so tests can flip it off. Schema versioning: every event carries `int SchemaVersion = 1`; rows whose `SchemaVersion > OutboxOptions.MaxSupportedVersion` are routed to `outbox_messages_dead` with `Reason = "unsupported_schema_version"` instead of being published. Wire-format additions are non-breaking because `System.Text.Json` tolerates unknown fields on read.

**Caching.** Redis-backed `IDistributedCache` (shared `distributedcache` container, connection string `ConnectionStrings__Redis`). The cache is **fail-open**: every read/write failure is logged at `Warning` and the call falls through to the source. Cache key formats: `catalog:menu:{rid}` (TTL `Catalog:MenuCacheTtlMinutes`, default 60 min) and `catalog:ingredients:{rid}` (TTL `Catalog:IngredientCacheTtlMinutes`, default 5 min — populated by the Phase 3 engine). Read-side: `IMenuReader` (in `Catalog.API/Readers/`) is a tree-building read path (categories → sub-categories → items with variations and ingredients); the Scrutor-decorated `CachedMenuReader` (`services.Decorate<IMenuReader, CachedMenuReader>()`) wraps it for cache-on-read, mirroring the Basket `CachedBasketRepository` pattern. Invalidation: every mutation handler (menu tree: `MenuCategories` CUD, `MenuSubCategories` CU, `MenuItems` CUD, `MenuItemVariations` CUD, `ComboItems` CD; ingredient tree: `Ingredients` CUD, `IngredientAlternatives` CUD, `MenuItemIngredients` AR) injects `ICatalogCache` and calls `InvalidateMenuAsync(restaurantId)` / `InvalidateIngredientsAsync(restaurantId)` after `SaveChangesAsync`. Drift repair: `CacheDriftRepairService` (`Catalog.API/Caching/CacheDriftRepairService.cs`, registered as `AddHostedService<CacheDriftRepairService>()`) is a `BackgroundService` that runs every `Catalog:CacheRepairIntervalMinutes` (default 5 min), enumerates restaurants from `MenuCategories`, and repopulates any missing `catalog:menu:{rid}` entries. The hosted service self-gates on the `CatalogRedisCache` feature flag (`FeatureManagement__CatalogRedisCache`, default `true`) so disabling the flag stops the loop without a redeploy. Configuration is bound via `services.AddOptions<CatalogOptions>().Bind(...).ValidateDataAnnotations().ValidateOnStart()`; `CatalogOptions` lives at `Catalog.API/Caching/CatalogOptions.cs` (cache TTLs + repair interval + `OutboxDeadLetterThreshold`).

**Auth.** `AddJwtAuthentication(authority: configuration["IdentityServiceUrl"] ?? "https://localhost:5057", audience: "OrderlyMicroservices")` plus `AddAuthorizationServices()` from BuildingBlocks.

**Health:** K8s-style split — `/live` (always 200; process up) and `/ready` (Postgres + Redis + RabbitMQ + outbox dead-letter count). `/ready` reports three check entries: `database` (`AspNetCore.HealthChecks.NpgSql`), `redis` (`AspNetCore.HealthChecks.Redis`), `messagebroker` (`AspNetCore.HealthChecks.Rabbitmq` 8.0.2, tags `["ready", "broker"]`), plus the custom `outbox_dlq` check (`OutboxDeadLetterProbe` at `Catalog.API/Health/`) which reads the `outbox_messages_dead` row count and returns `Unhealthy` when it exceeds `Catalog:OutboxDeadLetterThreshold` (default `0` — any dead-letter trips `/ready`). The RabbitMQ check URI is built from `MessageBroker:Host` + `MessageBroker:UserName` + `MessageBroker:Password`. Both `/live` and `/ready` use `UIResponseWriter.WriteHealthCheckUIResponse`; `/ready` filters by `Tags.Contains("ready")`.

---

### 4.3 Basket Service (Port 6001 / 6061)

**Surface:** Carter modules under `/api/v1`. Marten (Postgres, **per-tenant database creation via `CreateDatabasesForTenants`** + `opt.Schema.For<Models.Basket>().MultiTenanted()`) + Redis cache (`IDistributedCache` with a 30-minute absolute TTL). Calls Discount over gRPC.

**Two-tier design.** Marten is the durable store; Redis is the cache wrapper applied via `services.Decorate<IBasketRepository, CachedBasketRepository>()`. Cache key is `basket:{userId}:{restaurantId}`; on miss the basket is reloaded from Marten and re-cached for 30 min.

**Multi-tenancy.** `Basket : BuildingBlocks.Multitenancy.ITenantEntity` — the document carries `RestaurantId` and the `MultiTenanted()` registration in `Program.cs` adds a `tenant_id` column. Every read/write is filtered by `ICurrentRestaurantProvider` (`ClaimsRestaurantProvider` reads the `restaurantId` JWT claim); mismatched tenants throw `ForbiddenException` → 403. The pipeline-level `BasketIdentityGuardBehavior<TRequest,TResponse>` (registered BEFORE `ValidationBehavior<,>` in `AddMediatR`) cross-checks the inbound command's `(UserId, RestaurantId)` against the JWT before any validation cost is paid; the repository layer re-applies the same filter as defence in depth. Every cart command implements `IBasketIdentityRequest { Guid UserId; Guid RestaurantId; }`.

**Cart shape (`Models/Basket.cs`):**
```csharp
public class Basket : ITenantEntity
{
    [Identity] public Guid UserId { get; set; }
    public Guid RestaurantId { get; set; }
    public List<BasketItem> Items { get; set; } = [];
    public List<string> AppliedDiscounts { get; set; } = [];          // user-input codes
    public List<CouponSnapshot> AppliedCoupons { get; set; } = [];     // per-coupon breakdown (Phase 2.2)
    public decimal DiscountAmount { get; set; }                       // sum, clamped to Subtotal (Phase 2.2)
    public decimal Subtotal => Items.Sum(x => x.TotalPrice);
    public decimal Total => Math.Max(Subtotal - DiscountAmount, 0m);  // derived
    public Instant CreatedAt { get; set; }     // NodaTime
    public Instant ExpiresAt { get; set; }     // stored, not enforced — no cleanup job
}
public record CouponSnapshot(string Code, string Description, decimal DiscountAmount, Instant AppliedAt);
public class BasketItem
{
    public int MenuItemId { get; set; }
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public List<BasketItemVariation> Variations { get; set; } = [];
    public List<BasketItemCustomization> Customizations { get; set; } = [];
    public decimal TotalPrice => (UnitPrice + Variations.Sum(v => v.Price)) * Quantity;
}
```

The repository queries by both `UserId` and `RestaurantId`; `[Identity]` is on `UserId` only — uniqueness is logical, not Marten-enforced.

**Endpoint group.** `BasketEndpointGroup.MapBasketGroup()` (`Basket.API/Endpoints/BasketEndpointGroup.cs`) centralises the `MapGroup("/api/v1")` + `RequireAuthorization("Default")` + `WithTags("Baskets")` calls. Each Carter module calls `app.MapBasketGroup()` instead of duplicating the chain. `WithOpenApi()` lands in Phase 4 alongside the Swagger generator (`Microsoft.AspNetCore.OpenApi` is not yet a Basket dependency).

**Endpoints (Carter modules):**

| Method | Route | Permission | Notes |
|---|---|---|---|
| GET | `/api/v1/cart` | `orders:view_own` | **Token-bound** (Phase 1 primary). `(UserId, RestaurantId)` come from JWT. Returns 200 + empty cart body when no cart exists (never 404). |
| PUT | `/api/v1/cart` | `orders:create` | **Token-bound**. Body `UserId`/`RestaurantId` ignored — JWT is authoritative; mismatch → 403 via the identity guard. |
| DELETE | `/api/v1/cart` | `orders:create` | **Token-bound**. Idempotent — returns 200 + `IsSuccess = true` even when no cart exists. |
| POST | `/api/v1/cart/checkout` | `orders:create` | **Token-bound**. Body `UserId`/`RestaurantId` enforced by the identity guard. |
| GET | `/api/v1/baskets/{userId}/{restaurantId}` | `orders:view_own` | **[DEPRECATED shim]** — Phase 1 keeps the legacy shape for one release; removed end of Phase 3. |
| PUT | `/api/v1/baskets/{userId}/{restaurantId}` | `orders:create` | **[DEPRECATED shim]**. Identity guard enforces URL ids vs JWT. |
| DELETE | `/api/v1/baskets/{userId}/{restaurantId}` | `orders:create` | **[DEPRECATED shim]**. |
| POST | `/api/v1/baskets/checkout` | `orders:create` | **[DEPRECATED shim]** — `CheckoutBasketRequest(BasketCheckoutDto)` body wrapper kept for backward compat. |
| GET | `/health` | public | |

**Repository contract.** `IBasketRepository` exposes both `GetBasketAsync(...)` (throws `BasketNotFoundException` on miss — admin / audit path) and `GetActiveCartOrEmptyAsync(...)` (returns an empty `Basket` projected from the ids on miss — `GET /api/v1/cart` happy path per §0.4.7). Every operation runs the tenant assertion before the Marten query.

**Logging redaction.** `CheckoutBasketCommand` is annotated `[PciSensitive]`. `LoggingBehavior<TRequest,TResponse>` reflects the attribute (cached per-type on first read) and replaces the request-data slot in every `[START]` / `[END]` / `[PERFORMANCE]` log line with `CheckoutBasketCommand (payload redacted)`. The card number never reaches a log sink.

**Error envelope.** `builder.Services.AddProblemDetails()` is registered after `AddExceptionHandler<CustomExceptionHandler>()` so every 4xx/5xx response — including the `ForbiddenException → 403` arm added in Phase 1 of the BuildingBlocks contribution — flows through the same `application/problem+json` factory.

**Discount integration.** `Program.cs` registers `DiscountProtoServiceClient` from `Protos/discount.proto` (a shared project include; `GrpcServices="Client"`). The raw client is wrapped by `Basket.API.Discount.GrpcDiscountLookup` (`Basket.API/Discount/GrpcDiscountLookup.cs`), which implements the basket-side `IDiscountLookup` abstraction (`Basket.API/Discount/IDiscountLookup.cs`). The wrapper normalises the wire shape — gRPC `string ExpirationDate` → NodaTime `Instant`, `double Amount` → `decimal`, closed `DiscountType` enum passthrough — and fail-closed on parse errors. `StoreBasketHandler` depends on the abstraction (not the raw client) so the discount loop is unit-testable. **Phase 2.2 polyfill (live today):** the handler iterates `Basket.AppliedDiscounts` via `Parallel.ForEachAsync(MaxDegreeOfParallelism = 4)`, mirroring `Discount.Grpc.Domain.ActiveNow.Coupon`'s eligibility gate (minus the `DeletedAt` half — Discount's global query filter excludes soft-deleted coupons before they reach the wire). The per-coupon contribution lands on `Basket.AppliedCoupons: List<CouponSnapshot>` (each entry unclamped); the basket-level `Basket.DiscountAmount` is the sum clamped to `Basket.Subtotal`. `Basket.Total = Subtotal - DiscountAmount` is the user-visible total. gRPC failures (broker down, auth failure, malformed `ExpirationDate`) fail-closed — the whole upsert throws. Phase 2.2 keeps the `BasketCheckoutEvent` v1 wire shape (no `PaymentMethodSummary` yet — that's the Phase 2.1 BuildingBlocks contribution). The aggregated `EvaluateDiscounts` RPC lives on Discount's roadmap; once it ships, `IDiscountLookup` gains a batch overload and the per-coupon loop collapses to one call.

**TTL semantics.** Redis side: 30-minute absolute TTL on cache reads/writes (empty carts are not cached — only populated ones). Marten side: `Basket.ExpiresAt` is set but no `IHostedService`, no MassTransit consumer, no Marten TTL pragma actually prunes expired rows — the field is informational. Phase 3 introduces `BasketExpirySweepService`.

**Events published.** `BasketCheckoutEvent` only — published outbox-mediated by `Basket.API/Messaging/CheckoutBasketOutboxDispatcher` (Phase 2). The handler no longer calls `IPublishEndpoint.Publish` directly; instead it stages a `CheckoutBasketOutboxMessage` Marten document (`Basket.API/Messaging/CheckoutBasketOutboxMessage.cs`) and deletes the cart in the same `IDocumentSession.SaveChangesAsync()` commit — publish and delete are now atomic. The dispatcher (`BackgroundService`) polls every `OutboxOptions.ActivePollInterval`, claims undispatched rows via Marten LINQ + optimistic concurrency (`mt_version` column), relays them onto `IPublishEndpoint`, and stamps `DispatchedAt`. The dispatcher does **not** extend `BuildingBlocks.Messaging.Outbox.OutboxDispatcher<TContext>` — that base class is EF-Core-shaped (`DbSet<OutboxMessage>`, `DatabaseFacade`, `FromSql(BuildClaimSql(...))`) and can't be reused against `IDocumentSession`; the polling loop is mirrored verbatim in `Basket.API/Messaging/CheckoutBasketOutboxDispatcher.cs` so a future `MartenOutboxDispatcher<TStore>` can be factored into `BuildingBlocks.Messaging.Outbox` once a second Marten-using service adopts the pattern (BASKET_SERVICE_PLAN.md §6 Phase 2 drift item 1). Multi-replica safety (raw-SQL `FOR UPDATE SKIP LOCKED` claim) is deferred to a Phase 4 hand-off (drift item 3). The broker circuit breaker (Discount's `BrokerHealthState`) is also deferred — Phase 2 v1 ships the linear dispatcher. The `Outbox` section in `appsettings.json` carries `Enabled=true`, `ActivePollInterval=1s`, `IdlePollInterval=5s`, `BatchSize=100`, `MaxSupportedVersion=1`, `MaxConsecutiveBrokerFailures=3`, `BrokerBackoffSeconds=60s`; tests flip `Enabled=false` to skip the relay loop.

**Card-redaction on the wire.** Still **deferred** (BASKET_SERVICE_PLAN §6 Phase 2.1). `BasketCheckoutEvent` v1 carries the same fields it does today, including `CardNumber` / `CVV` / `CardName` / `Expiration` / full `AddressLine`. Phase 2 only changed the *delivery mechanism* (outbox), not the *payload shape*. The card-redaction commit is a separate BuildingBlocks contribution (the event lives in `BuildingBlocks.Messaging.Events`) and ships alongside the `PaymentMethodSummary { Method, Brand, LastFour }` event shape in §6 Phase 2.1.

---

### 4.4 Discount Service (Port 6002 / 6062)

**Surface:** gRPC only (HTTP/2, no HTTP routes). SQLite file store. `DiscountService` extends `DiscountProtoServiceBase`.

**Auth + tenancy.**
- **JWT bearer** against `Identity` (`Authority=https://localhost:5057`, `Audience=OrderlyMicroservices`). Implemented by `BuildingBlocks.Authorization.AddJwtAuthentication`; per-method permission policies enforced by a global gRPC interceptor (`DiscountAuthorizationInterceptor` in `Authorization/`) that reads a custom `[Permission(...)]` attribute off each RPC method. Standard `[Authorize(Policy=...)]` is silently ignored on gRPC services, hence the interceptor pattern.
- **12 permission constants** in `Authorization/DiscountPermissions.cs` (single source of truth — the project convention per `DISCOUNT_SERVICE_PLAN.md` §S1). Locked: 5 `coupon:*` + 5 `reward-code:*` + 2 `discount-rule:*`. Identity's follow-up plan reads the same list when seeding its `Permissions` table; until that lands, missing claims default-deny (no leakage, no bypass). The five Coupon RPCs ship today; the seven reward-code / discount-rule constants are pre-declared for Phases 2 and 3.
- **JWT claim-shape handling** — `AuthorizationPolicies.AddDiscountPolicies` registers each policy with a `RequireAssertion` that honours **both** Shape A (`c.Value == permission`, one claim per permission) **and** Shape B (comma-separated single `permissions` claim). The Phase 1B `JwtClaimShapeProbe` integration test in `Discount.Grpc.Tests` decodes a dev JWT and locks the actual shape at first run so a future Identity emission-shape drift surfaces as a failing test rather than silent deny. The central `BuildingBlocks.Authorization.PermissionAuthorizationHandler` + `JwtClaimExtensions.GetPermissions` paths still ship Shape A only; tracked as a v1.7+ BuildingBlocks follow-up so Catalog follows on the same change.
- **Global tenant query filter** on `Coupon` (`HasQueryFilter(c => c.DeletedAt == null && c.RestaurantId == _provider.RestaurantId)`) — `BuildingBlocks.Multitenancy.ITenantEntity.RestaurantId` is now `Guid` (was `int` before P1.1). Tenant identity is fed by `ICurrentRestaurantProvider` (`ClaimsRestaurantProvider` reads the `restaurantId` JWT claim via `IHttpContextAccessor`; returns `Guid.Empty` for unauthenticated/missing claim, so the filter matches no rows — fail-secure default). Admin tooling opts back in via `IgnoreQueryFilters()`.
- **Pattern 2 bus-side tenant attachment** — `ICurrentRestaurantProvider.Attach(ClaimsPrincipal)` (added in Phase 1B.a) pushes an `AsyncLocal<ClaimsPrincipal?>` for the duration of an `IDisposable` scope; while active, the `RestaurantId` getter reads the attached principal's `restaurantId` claim in preference to the HTTP context. Synthetic principals are minted via `BuildingBlocks.Authorization.ClaimsPrincipalBuilder` (fluent `.WithRestaurant(...).WithActor(...).WithPermission(...).Build()`). Phase 5's `FeedbackSubmittedConsumer` is the first bus-scope consumer; the API surface ships now so Phase 5 doesn't need a follow-up BuildingBlocks PR.

**Entities.**
- **`Coupon`** — `AuditableEntity<int>, ITenantEntity`. Columns: `RestaurantId Guid`, `Code string`, `Description string`, `Amount decimal`, `RedeemAmount int`, optional `MaxRedeemAmount int?`, optional NodaTime `ExpirationDate Instant?`, `DeletedAt Instant?` (soft-delete column set by `DiscountExpirySweepService`), `DeletedBy string?`. Inherits `Id`, `CreatedBy/At`, `LastModifiedBy/At`, `IsActive` from the auditable base. Unique lookup by `(RestaurantId, Code)`.
- **`Coupon` race-fix:** `RedeemDiscount` now uses an atomic `ExecuteSqlInterpolatedAsync` conditional UPDATE (`WHERE Id=@id AND IsActive=1 AND DeletedAt IS NULL AND (MaxRedeemAmount IS NULL OR RedeemAmount < MaxRedeemAmount)`, with explicit `LastModifiedAt = @now` + `LastModifiedBy = "discount-system"` since raw SQL bypasses the `AuditableEntityInterceptor`). The pre-existing TOCTOU race is closed because the read-then-write pair collapses into one engine-native UPDATE; race losers see `rowsAffected=0` and the RPC returns `Success=false`. The actor string comes from `Authorization/DiscountActors.cs` (constants `System = "discount-system"`, `Sweep = "discount-sweep"`, `Service = "discount-service"`).
- **Soft-delete + sweep:** `DiscountExpirySweepService : BackgroundService` with `PeriodicTimer` (default 5 min, `DiscountExpirySweepOptions.SweepInterval`) scans expired coupons and sets `DeletedAt = clock.GetUtcNow()`, `DeletedBy = DiscountActors.Sweep`. `TimeProvider` is injected so tests use `FakeTimeProvider`.
- **`DiscountRule`** — `AuditableEntity<int>, ITenantEntity`. Eligibility predicate attached to a `Coupon` (FK with UK on `(RestaurantId, CouponId)` so one rule per coupon per tenant; `OnDelete(DeleteBehavior.Restrict)` per the project-wide rule). Columns: `RestaurantId Guid`, `CouponId int` (FK), `RuleType DiscountRuleKind` (`MinOrderAmount | RequiredMenuItems | TimeWindow | Bogo`), `RuleDataJson string` (JSON-encoded payload keyed by `RuleType` — SQLite has no native JSONB so the column is `TEXT` with `RuleDataJson` parsed at the handler boundary; the discriminator keeps the shape stable across future rule kinds), `IsActive bool` (admin toggle, shadowed with `new` so it is independent of the auditable base's `IsActive`), `DeletedAt Instant?`, `DeletedBy string?`. Evaluator matches the `RuleType` and dispatches on the deserialized `RuleDataJson`. FluentValidation enforces shape at the handler boundary per plan §0.3.3 (`MinOrderAmount: decimal > 0`; `RequiredMenuItemIds: Guid[]` non-empty; `TimeWindow: StartTime < EndTime, DayOfWeekMask ∈ [0, 127]`; `Bogo: BuyQuantity >= 1, GetQuantity >= 1`). Read RPCs (`GetDiscountRule`, `ListDiscountRules`) gate on `discount-rule:read`; CUD on `discount-rule:edit`; `Evaluate` is read-permission.
- **`RewardCode`** — `AuditableEntity<int>, ITenantEntity`. Customer-feedback-generated reward (issued by the Phase 5 `FeedbackSubmittedConsumer` stub; redeemed via `RedeemRewardCode`). Columns: `RestaurantId Guid`, `required string Code` (UK on `(RestaurantId, Code)` per tenant; `required` modifier per plan v1.1 M7), `required RewardKind Kind` (`Percentage | FixedAmount | FreeItem | Points`; proto-side `RewardType` discriminator keeps the wire-side and entity-side enums in separate namespaces per the `DiscountRuleType`/`DiscountRuleKind` precedent), `decimal Value` (overloaded across kinds — FluentValidation pins the kind-specific contract: `Value ∈ (0, 100]` for Percentage, `> 0` for FixedAmount / Points, `== 0` for FreeItem; FreeItem rewards carry the target menu-item id in `Description` as `free-item:{menuItemId}` per proto v1 simplicity, with a v2 `RewardTargetMenuItemId` field as a future proto bump), `string? Description`, optional NodaTime `ExpirationDate Instant?` (validator requires `> clock.GetCurrentInstant()` when set), `int RedeemAmount` (incremented by the atomic conditional UPDATE in `RedeemRewardCode`), optional `int? MaxRedeemAmount`, optional `Guid? RedeemedInOrderId`, optional `Instant? RedeemedAt` (set by the conditional UPDATE alongside `RedeemInOrderId`), `DeletedAt Instant?`, `DeletedBy string?`. The entity exposes three static `Code*Star*` helpers — `Code4StarPct10(rid, feedbackEventId, clock)`, `Code5StarPct15(...)`, `Code5StarAppetizer(...)` — that combine `rid + tag + day-bucket + feedbackEventId` per plan v1.2 H-L1 so bus redelivery collides on the same UK row while different feedback events land on distinct codes. Read RPCs gate on `reward-code:read`; CUD on `reward-code:edit`; `RedeemRewardCode` on `reward-code:redeem`.

**Outbox + circuit breaker.** `DiscountContext` implements `BuildingBlocks.Messaging.Outbox.IOutboxDbContext` with `DbSet<OutboxMessage>` + `DbSet<OutboxDeadMessage>`. `Messaging/Outbox/DiscountOutboxPublisher.cs` stages rows in the caller's EF Core transaction (registered `Scoped`, shares the same `DiscountContext` instance as the gRPC handler). `DiscountOutboxDispatcher : OutboxDispatcher<DiscountContext>` is registered as an `IHostedService` and polls via `BuildingBlocks.Messaging.Outbox.OutboxDispatcher<TContext>` — the first SQLite implementation (claim SQL: `SELECT * FROM outbox_messages WHERE DispatchedAt IS NULL ORDER BY OccurredOn ASC LIMIT @batchSize`). SQLite serializes writes via the database lock held by the dispatcher's `BeginTransactionAsync`; the plan's §6.7 `ClaimId`-based multi-replica claiming is deferred to a follow-up. **`DiscountOutboxDispatcher` overrides `ExecuteAsync` to add a broker-circuit breaker** (per plan §6.7 v1.2 M-L10): on a top-level `DispatchOnceAsync` throw (TX-commit failure, broker unreachable, channel-closed), the dispatcher calls `BrokerHealthState.RecordFailure()` (a singleton thread-safe counter), pauses for `OutboxOptions.BrokerBackoffSeconds` (default 60 s), and surfaces `/ready=Unhealthy` once the counter meets `OutboxOptions.MaxConsecutiveBrokerFailures` (default 3). On the first successful dispatch the counter resets. Per-row publish failures inside `DispatchBatchAsync` stay poison-row local — they don't increment the counter (broker-level outage vs. message-poison are different failure modes). MassTransit bus is `UsingInMemory` for Phase 1 dev (RabbitMQ wiring is the Phase 4 cross-service hand-off).

**Idempotency-Key.** `Authorization/IdempotencyKeyProvider.cs` registers `IIdempotencyKeyProvider` as a singleton. The provider reads `IConfiguration["Discount:IdempotencyKey"]` (32 random bytes hex-encoded) at startup, hard-fails when missing in non-development environments, and emits a per-process random key + `WARN` log in development per plan §0.4.1 v1.3 changelog O-L29. The cache middleware that consumes the provider lands in Phase 8; Phase 1B ships the provider so the wiring commit is additive.

**DiscountOptions.** `Options/DiscountOptions.cs` is the strongly-typed configuration class bound via `AddOptions<DiscountOptions>().Bind(...).ValidateDataAnnotations().ValidateOnStart()`. Default value posture (per plan §6.7 + v1.4 M-L9 + v1.1 H-L2):
- `OutboxDeadLetterThreshold = 5` (alert-and-let-humans-triage, not fail-closed-on-first-poison).
- `SweepIntervalMinutes ∈ [1, 1440]` (5 default; the actual sweep reads `DiscountExpirySweepOptions:SweepInterval`).
- `EnableFeedbackSubmittedConsumer = false` (Notification v1 doesn't ship).
- `EnableDiscountAppliedPublishing = false`, `EnableRewardGeneratedPublishing = false`, `EnableRewardRedeemedPublishing = false` (no consumers landed).
- `EnableMenuItemChangedConsumer = true`, `EnableRestaurantConfigChangedConsumer = true` (Catalog ships both).
- `EnableOrderCreatedConsumer = false` (Ordering plan doesn't ship).

**gRPC contract (`Protos/discount.proto`):**
- `GetDiscount(GetDiscountRequest) → GetDiscountResponse` (returns `CouponModel` with `IsActive=false` if not found). Permission: `coupon:read`.
- `CreateDiscount(CreateDiscountRequest) → CreateDiscountResponse`. Permission: `coupon:create`.
- `UpdateDiscount(UpdateDiscountRequest) → UpdateDiscountResponse`. Permission: `coupon:edit`.
- `DeleteDiscount(DeleteDiscountRequest) → CreateDiscountResponse`. Permission: `coupon:delete`.
- `RedeemDiscount(RedeemDiscountRequest) → RedeemDiscountResponse` (atomic conditional UPDATE; rejects when over cap or coupon is soft-deleted). Permission: `coupon:redeem`.
- `ListDiscounts(ListDiscountsRequest) → ListDiscountsResponse` (paged, `page` 1-based, `page_size` clamped `[1, 200]` with default 50, runs through the global tenant filter, no manual `Where(RestaurantId == ...)` in the handler). Permission: `coupon:read`. Response carries `repeated CouponModel coupons` + `int32 total_count`.

**`DiscountRuleProtoService` (`Services/DiscountRuleService.cs`, six RPCs):**
- `CreateDiscountRule(CreateDiscountRuleRequest) → CreateDiscountRuleResponse`. Permission: `discount-rule:edit`. UK guard on `(RestaurantId, CouponId)` returns `StatusCode.FailedPrecondition` with `rule-already-exists` message before the DB constraint fires.
- `GetDiscountRule(GetDiscountRuleRequest) → GetDiscountRuleResponse` (returns empty when not found / soft-deleted). Permission: `discount-rule:read`.
- `ListDiscountRules(ListDiscountRulesRequest) → ListDiscountRulesResponse` (paged, same shape as `ListDiscounts`). Permission: `discount-rule:read`.
- `UpdateDiscountRule(UpdateDiscountRuleRequest) → UpdateDiscountRuleResponse`. Permission: `discount-rule:edit`.
- `DeleteDiscountRule(DeleteDiscountRuleRequest) → DeleteDiscountRuleResponse` (soft-delete via `DeletedAt = now`, `DeletedBy = actor`). Permission: `discount-rule:edit`.
- `EvaluateDiscountRules(EvaluateDiscountRulesRequest { restaurant_id, order_total, menu_item_ids }) → EvaluateDiscountRulesResponse { repeated int32 applicable_coupon_ids }` — read-only, walks the rule set, returns the coupons whose rules hold against the supplied basket. Permission: `discount-rule:read`.

**`RewardCodeProtoService` (`Services/RewardCodeService.cs`, six RPCs):**
- `CreateRewardCode(CreateRewardCodeRequest) → CreateRewardCodeResponse`. Permission: `reward-code:create`. UK guard on `(RestaurantId, Code)` returns `StatusCode.FailedPrecondition` with `code-already-exists` message before the DB constraint fires. FluentValidation at the handler boundary enforces the §0.3.3 kind-specific contract on `Value`.
- `GetRewardCode(GetRewardCodeRequest) → GetRewardCodeResponse` (returns empty when not found / soft-deleted). Permission: `reward-code:read`.
- `ListRewardCodes(ListRewardCodesRequest) → ListRewardCodesResponse` (paged, same shape as `ListDiscounts` / `ListDiscountRules`). Permission: `reward-code:read`.
- `UpdateRewardCode(UpdateRewardCodeRequest) → UpdateRewardCodeResponse`. Permission: `reward-code:edit`.
- `DeleteRewardCode(DeleteRewardCodeRequest) → DeleteRewardCodeResponse` (soft-delete via `DeletedAt = now`, `DeletedBy = DiscountActors.System`). Permission: `reward-code:delete`.
- `RedeemRewardCode(RedeemRewardCodeRequest { restaurant_id, reward_code_id, order_id, quantity }) → RedeemRewardCodeResponse` — atomic conditional UPDATE that closes the same TOCTOU race `RedeemDiscount` closes for coupons. WHERE-clause guards: `IsActive=1`, `DeletedAt IS NULL`, `(MaxRedeemAmount IS NULL OR RedeemAmount < MaxRedeemAmount)`. Sets `RedeemedInOrderId`, `RedeemedAt`, `LastModifiedAt`, `LastModifiedBy = DiscountActors.System` in the same UPDATE (raw SQL bypasses the audit interceptor per plan v1.1 L11). Race losers see `rowsAffected=0` and the RPC returns `Success=false`. FreeItem rewards must have `Quantity=1` (validator). Permission: `reward-code:redeem`. Defense-in-depth tenant check: `ICurrentRestaurantProvider.RestaurantId` is compared against the request's `restaurant_id`; mismatch returns `StatusCode.PermissionDenied` with `Metadata["tenant-mismatch"]`.

**History publishing across aggregates.** Every `Coupon`, `RewardCode`, and `DiscountRule` CUD + redeem writes a `DiscountHistoryAppendedIntegrationEvent` to the outbox inside the same EF Core transaction as the aggregate mutation. **Phase 4** completes the cross-aggregate publish contract: Phase 1 wired Coupon CUD + redeem; Phase 2 added the same hooks on the DiscountRule create / update / delete paths; Phase 3 added them on the RewardCode create / update / delete / redeem paths; Phase 4 added them on the Coupon create / update / delete paths (Phase 1's commitment to land those was deferred per the v1.5 / v1.7 changelog "publish-points land in Phase 4" note). The event payload (locked by plan §6.5) is `EntityType ∈ {Coupon, RewardCode, DiscountRule}`, `EntityId`, `RestaurantId`, `ChangeType ∈ {Created, Updated, Deleted, Redeemed}`, `OldValues: string?` (serialized JSON of pre-image; `null` for Created), `NewValues: string` (serialized JSON of post-image; `"{}"` empty object for Coupon's hard-delete path since the row no longer exists). Catalog consumes the event in its own plan and writes a Marten `EntityHistoryArchive` document per §6.6.1. The wire format is `string?` (not `JsonObject`) per plan v1.1 M9 — every publisher-to-outbox roundtrip would pay an unnecessary serialize-parse tax if we shipped `JsonObject` on the bus. Each handler captures the `OldValues` pre-image before mutation (or, for `Redeem*`, before the raw `ExecuteSqlInterpolatedAsync` UPDATE that bypasses the EF change tracker); the `NewValues` post-image is either the in-memory mutated entity, a re-fetch after the conditional UPDATE (`RedeemDiscount` / `RedeemRewardCode`), or `"{}"` for Coupon's hard-delete. The handler's `SaveChangesAsync` commits both the aggregate mutation and the outbox row in the same EF Core transaction.

**Catalog-event consumers (`Messaging/EventHandlers/`).** Both consumers use Pattern 2 synthetic claims (per §8 Multi-Tenancy) — `tenant.Attach(new ClaimsPrincipalBuilder().WithRestaurant(evt.RestaurantId).WithActor(DiscountActors.Service).Build())` runs at the top of `Consume`, and the rest of the handler runs under the attached scope. **Idempotency: `processed_inbound_events` table** (`EventId TEXT`, `ConsumerType TEXT` as composite PK; index `ix_processed_inbound_consumer_time` on `(ConsumerType, ConsumedAt)`). The consumer reads `evt.Id` from the `IntegrationEvent` base, attempts the insert; on a unique-key violation it returns without dispatching (the bus already retried a stale copy). Per plan §0.3.4 the table-based guard is the right choice here because the rule-update path has no natural uniqueness violation to lean on.
- `MenuItemChangedConsumer : IConsumer<MenuItemChangedIntegrationEvent>` — for `ChangeType ∈ {Updated, Deleted}`, finds `DiscountRule`s whose `RuleDataJson.RequiredMenuItemIds` includes `event.MenuItemId`, recomputes eligibility for each affected `Coupon`, and persists `IsActive` based on whether at least one rule still holds. No state change for non-affected rules. Flag: `DiscountOptions:EnableMenuItemChangedConsumer = true`.
- `RestaurantConfigurationChangedConsumer : IConsumer<RestaurantConfigurationChangedIntegrationEvent>` — if `ChangedFields` contains `"Currency"`, finds `Coupon` for `event.RestaurantId` and flips `IsActive=false` (the effect is monotonic but the table guard is cheap and keeps the consumer-side idempotency story in one place per plan §0.3.4). Other `ChangedFields` are no-op for Discount. Flag: `DiscountOptions:EnableRestaurantConfigChangedConsumer = true`.
- `FeedbackSubmittedConsumer : IConsumer<FeedbackSubmittedIntegrationEvent>` (Phase 5) — mints per-rating `RewardCode`s when `OverallRating ≥ 4`. Hardcoded 4★/5★ rule per plan §6.6.3: `rating ∈ [4, 5)` → one `RewardCode { Percentage, 10 }`; `rating ≥ 5` → two codes (`Percentage, 15` + `FreeItem, 0`); below 4★ → ack-only. **Pattern 2 synthetic claims** via `tenant.Attach(new ClaimsPrincipalBuilder().WithRestaurant(evt.RestaurantId).WithActor(DiscountActors.Service).Build())`. **Idempotency: deterministic Code via `RewardCode.Code*Star*` helpers** (per plan v1.2 H-L1) — the same `feedbackEventId` produces the same `Code` on redelivery, colliding on the `ux_reward_codes_restaurant_code` UK; pre-check + `DbUpdateException` swallow handle the dedup path. No `processed_inbound_events` row needed (the §0.3.4 choice matrix's unique-constraint branch). **Flag: `DiscountOptions:EnableFeedbackSubmittedConsumer = false`** — Notification v1 (the publisher per plan §6.6.3) does not ship yet, so the consumer endpoint is wired-but-disabled; flipping the flag to `true` re-materializes the endpoint on next boot without a code change.

**Health:** K8s-style split — `/live` (no checks attached; always 200 when the process is up) and `/ready` (the four `discount-*` checks under tag `ready`). Wired via `services.AddDiscountHealthChecks()` + `MapHealthChecks("/live", Predicate = _ => false)` + `MapHealthChecks("/ready", Predicate = c => c.Tags.Contains("ready"), ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse)`. The four `IHealthCheck` impls live in `Health/DiscountHealthChecks.cs`:
- `discount-sqlite` (`SqliteFileCheck`) — `DiscountContext.Database.CanConnectAsync()` from a fresh scope.
- `discount-broker-circuit` (`BrokerHealthCheck`) — reads `BrokerHealthState.ConsecutiveBrokerFailures`, returns `Unhealthy` at or above `DiscountOptions.OutboxDeadLetterThreshold` (also spelled in `OutboxOptions.MaxConsecutiveBrokerFailures`; both default to 3). Mirrors the dispatcher's pause-or-proceed decision.
- `discount-outbox-dead-letter` (`OutboxDeadLetterCheck`) — counts `OutboxDeadMessages` rows; `Unhealthy` when the count exceeds `DiscountOptions.OutboxDeadLetterThreshold` (default `5`, per v1.4 M-L9).
- `discount-rabbitmq` (`RabbitMqBrokerCheck`) — TCP probe of `MessageBroker:Host:5672` when `Outbox:Enabled=true`; no-op `Healthy` in dev where the broker isn't configured; `Unhealthy` in production posture with no broker host configured.

**gRPC reflection (development only).** `services.AddGrpcReflection()` + `app.MapGrpcReflectionService()` guarded by `if (app.Environment.IsDevelopment())` per plan §0.4.5 M-L7. Production never reflects (the schema would leak to anyone who can reach the port). Lets `grpcurl` / Postman-gRPC / BloomRPC enumerate services without a side-channel proto file during dev.

Seeded at startup: `DISCOUNT10` (10 off, restaurantId `11111111…`) and `DISCOUNT20` (20 off, restaurantId `22222222…`); both `IsActive=true`; both eligible for the global filter on `(RestaurantId, Code)`.

**Phase 6.2 (2026-07-11):** Coupon ownership confirmed as Discount-only. Catalog never implemented Coupon — the entity existed only as mermaid drift in `db_relational_model.mermaid` (block + `Restaurants ||--o{ Coupons : "issues"` relationship row). The move was documentation-only on the Catalog side (mermaid + companion md cleanup); no YARP route change required because the existing `/discount-api/{**catch-all}` catch-all on `discount-cluster` already covers `/discount-api/coupons/*`. See `CATALOG_SERVICE_PLAN.md` §7.6.2 + §6.6.

**Events Published (Phase 6 — wired-but-flagged-off).** Three architecture-named publishes exist as `I*IntegrationEvent` types (`Discount.Grpc/Messaging/Events/`) with gated `outbox.PublishAsync(...)` call-sites in the relevant gRPC service methods. Each defaults to `false`; flipping any single flag is a config-only change and starts emitting rows on the next boot. Per plan §6.5 + §7 Phase 6:
- **`DiscountAppliedIntegrationEvent`** — published from `DiscountService.RedeemDiscount` after the atomic conditional UPDATE succeeds (mirrors the existing `DiscountHistoryAppendedIntegrationEvent` publish at the same call-site). Payload: `CouponId`, `CouponCode`, `RestaurantId`, `Quantity`. **Intentionally omits `OrderId`** because plan §0.3.3 lists `RedeemDiscountCommand.OrderId` as required but the shipped `RedeemDiscountRequest` proto in `Protos/discount.proto` lacks an `order_id` field — adding OrderId requires a coordinated proto bump + Basket's `DiscountProtoServiceClient` refresh, tracked for the §0.3.3 reconciliation pass (deferred; same shape as the §7 plan-only fields in `DiscountHistoryAppendedIntegrationEvent`). Flag: `DiscountOptions:EnableDiscountAppliedPublishing=false`.
- **`RewardGeneratedIntegrationEvent`** — published from `RewardCodeService.CreateRewardCode` after `DiscountHistoryAppendedIntegrationEvent(ChangeType=Created)` for the same entity. Payload: `RewardCodeId`, `Code`, `RestaurantId`, `Kind` (string form of `RewardKind`), `Value`, `OrderId` (always `null` on creation; consumer reads the wire code to forward to the customer). Flag: `DiscountOptions:EnableRewardGeneratedPublishing=false`.
- **`RewardRedeemedIntegrationEvent`** — published from `RewardCodeService.RedeemRewardCode` after the atomic conditional UPDATE succeeds (mirrors `DiscountHistoryAppendedIntegrationEvent(ChangeType=Redeemed)` at the same call-site). Payload: `RewardCodeId`, `Code`, `RestaurantId`, `OrderId`, `Quantity`. Flag: `DiscountOptions:EnableRewardRedeemedPublishing=false`.

All three are `MessageVersion=1` per `IntegrationEvent` base default; the `OutboxPublisher` copies the value into the outbox row's `SchemaVersion` column on stage (so a future `MessageVersion=2` rollover follows the same gate). Each new event tier (deferred publishes + Phase 4 history) commits inside the same `SaveChangesAsync` transaction as the aggregate mutation, so the outbox row + the aggregate row are atomic.

---

### 4.5 Ordering Service (Port 6003 / 6063)

**Surface:** Carter modules under `/api/v1`. MSSQL Server 2022 via EF Core SqlServer. Consumes `BasketCheckoutEvent` from RabbitMQ.

**Layered DDD layout.**
- `Ordering.Domain` (no external deps except `BuildingBlocks`): `Order : Aggregate<OrderId>` with private `OrderItems` list exposed as `IReadOnlyCollection`; entities `OrderItem : Entity<OrderItemId>` (with `MarkItemPreparing` / `MarkItemReady` per-item state transitions), `OrderBill : Entity<int>`, `OrderActivity : Abstractions.Entity<OrderActivityId>` (append-only child row — see "Activity feed" below), `Customer : AuditableEntity<CustomerId>`, `MenuItem : Entity<MenuItemId>`. Value objects in `ValueObjects/`: `OrderId`, `OrderItemId`, `OrderActivityId`, `MenuItemId`, `OrderNumber`, `CustomerId`, `Address` (5-digit ZipCode enforced), `Payment` (3-digit Ccv, MM/YY regex). Domain exceptions in `Ordering.Domain/Exceptions/`: `DomainException`, `InvalidOrderStateTransitionException` (→ HTTP 409), `InvalidOrderItemStateTransitionException` (→ HTTP 409), `OrderActivityInvariantException` (→ HTTP 422 Unprocessable Content; surfaced when the activity factory rejects unknown enum values or over-length free-text), `OrderNotFoundException`, `OrderItemNotFoundException`.
- `Ordering.Application`: MediatR commands + queries + open behaviors (`ValidationBehavior<,>` runs only on `ICommand<TResponse>`; `LoggingBehavior<,>` runs on everything and now stamps the per-request correlation id into `BuildingBlocks.Correlation.CorrelationContext` — see "Activity feed" / §9 below). Inter-feature segment: `Orders/Commands/` (Create, Update, Delete, Confirm, StartOrderPrep, MarkOrderReady, Cancel, StartItemPrep, MarkItemReady, MarkOrderDelivered), `Orders/Queries/`, `Orders/EventHandlers/Domain/`, `Orders/EventHandlers/Integration/` (`BasketCheckoutEventHandler` + the four `KitchenOrder*IntegrationEventHandler` consumers — all five set `CorrelationContext.Set(context.CorrelationId?.ToString() ?? Guid.NewGuid().ToString())` and clear it in `finally`). `Dtos/` carries `OrderActivityDto` (record `(Guid Id, OrderActivityType ActivityType, Guid? ActorUserId, Instant OccurredAt, string? CorrelationId, string? Notes, OrderActivityMetadata? Metadata)`) and `OrderDto` (gains an `IReadOnlyList<OrderActivityDto> Activities` field ordered by `OccurredAt ASC, Id ASC`). `Dtos/Validators/`. `FeatureManagement` is registered so the `OrderFullfilment` flag gates `OrderCreatedEventHandler`.
- `Ordering.Infrastructure`: `ApplicationDBContext` with DbSets `Customers`, `Orders`, `OrderItems`, `MenuItems`, `OrderBills`, `OutboxMessages` (no `DbSet<OrderActivity>` — the activity child is loaded via the `Order.Activities` navigation only). Configurations use EF Core's `ComplexProperty` for nested `BillingAddress` / `DeliveryAddress` / `Payment` value objects and `HasMany(o => o.Activities).WithOne().HasForeignKey(a => a.OrderId).OnDelete(DeleteBehavior.Cascade)` to wire the activity child table; `OrderActivityConfiguration` maps `Metadata` as `nvarchar(max)` jsonb via the cached `OrderActivityJson.Options` (with `JsonStringEnumConverter`) and creates the covering index `IX_order_activities_OrderId_OccurredAt`. Migrations: `InitialCreate`, `AddOrderBill`, `20260706233202_AddOutboxMessages`, `20260710233247_TypedOrderItemCustomizationsJsonb`, `20260716001148_AddOrderActivities` (creates the `order_activities` table + index, no backfill — pre-existing orders return `Activities: []`). Migrations retry up to 30× on SQL errors 1801/4060/233/-2, then seed four orders via `InitialData`. Transactional outbox: `OrderingOutboxPublisher` (interceptor) writes to `outbox_messages` inside the same transaction as the aggregate mutation; `OrderingOutboxDispatcher` (hosted service) polls + relays to `IPublishEndpoint`. The activity row commits in the same `SaveChangesAsync` transaction as the outbox row (same atomicity guarantee).
- `Ordering.API`: 13 Carter endpoints (6 customer/admin + 7 kitchen state-transition); no in-assembly MassTransit consumer — `Ordering.Application/Orders/EventHandlers/Integration/` is the single MassTransit registration point

**Aggregate behaviour.**
- `Order.Create(...)` → raises `OrderCreatedEvent`. The handler (gated by the `OrderFullfilment` feature flag) projects the aggregate to the bus-safe `OrderCreatedIntegrationEvent` (no `PaymentDto` / no `Card*` fields) via `OrderExtensions.ToOrderCreatedIntegrationEvent` and writes the row through the outbox publisher.
- `Order.Update(billingAddress, deliveryAddress, payment)` → mutates the customer-editable parts only; **`Status` is no longer written here**. Raises `OrderUpdatedEvent` (handler only logs).
- `Order.Add(menuItemId, quantity, price)` / `Order.Remove(menuItemId)`.
- **State-transition methods** (each guarded by `InvalidOrderStateTransitionException` → HTTP 409 when the current `Status` does not permit the transition; each raises the matching `Order*Event` for downstream consumption):
  - `Confirm(confirmedByUserId, now)` — `Pending` → `Confirmed`. Used by `KitchenOrderAcceptedIntegrationEventHandler` and the `POST /orders/{id}/confirm` endpoint.
  - `MarkPreparing(now)` — `Confirmed` → `Preparing`. Driven in production by `KitchenOrderPrepStartedIntegrationEventHandler` (emitted when the kitchen's first-item-prep action lands on a still-`New` ticket); the `POST /orders/{id}/start-prep` endpoint is kept as a manual override.
  - `MarkReady(now)` — `Preparing` → `Ready`. Used by `KitchenOrderReadyIntegrationEventHandler` and the `POST /orders/{id}/mark-ready` endpoint.
  - `StartDelivery()` — `Ready` → `DeliveryStatus = Dispatched` (for delivery orders; aggregate `Status` stays at `Ready`).
  - `MarkDelivered(now)` — `Ready` → `Delivered`.
  - `Complete(now)` — `Delivered` → `Completed`.
  - `Cancel(reason, cancelledByUserId, now)` — any non-terminal → `Cancelled`. Used by `KitchenOrderCancelledIntegrationEventHandler` and the `POST /orders/{id}/cancel` endpoint.
- `OrderItem.MarkItemPreparing(now)` — `Pending` → `Preparing`. Driven by `POST /orders/{id}/items/{itemId}/start-prep`. Throws `InvalidOrderItemStateTransitionException` (→ HTTP 409).
- `OrderItem.MarkItemReady(now)` — `Preparing` → `Ready`. Driven by `POST /orders/{id}/items/{itemId}/mark-ready`. Throws `InvalidOrderItemStateTransitionException` (→ HTTP 409).
- `OrderItem.Customizations` is `IReadOnlyList<KitchenOrderItemCustomization>` and `OrderItem.SelectedVariations` is `IReadOnlyList<KitchenOrderItemVariation>` — typed records stored as `nvarchar(max)` jsonb columns via `OrderItemConfiguration`'s `System.Text.Json`-backed value converter. The aggregate is the source of truth: the jsonb-string parser in `OrderExtensions` is gone.

**Activity feed.** Every state-transition method on `Order` (and `OrderItem.MarkItemPreparing` / `MarkItemReady`) appends one `OrderActivity` row to the in-memory aggregate in the same `SaveChangesAsync` transaction as the outbox row (mirrors the `outbox_messages` atomicity rule). The activity row carries `CorrelationId` (≤100 chars; stamped from `BuildingBlocks.Correlation.CorrelationContext.Current` — see §9), `ActorUserId` (where the source method takes one — `Confirm`/`Cancel`; `null` for kitchen-driven / system transitions), and `Metadata` (typed `OrderActivityMetadata` record with nullable `OrderStatus?` / `PrepStatus?` / `DeliveryStatus?` prev/new enum pairs, populated per transition type — see `ORDER_ACTIVITY_PLAN.md` §6.1 callout table). The `OrderCreated` activity is appended by `CreateOrderHandler` (the application service that invokes `Order.Create`) because the factory cannot call `RecordActivity` itself. The `OrderDto.Activities` field is populated by `OrderExtensions.ToOrderDto` (ordered `OccurredAt ASC, Id ASC` for stable rendering) and the three query handlers (`GetOrderByIdHandler`, `GetOrdersHandler`, `GetOrdersByCustomerHandler`) load the child collection via `.Include(o => o.Activities)` so the read path is a single SQL round-trip. The covering index `IX_order_activities_OrderId_OccurredAt` makes the read pattern `WHERE OrderId = @id ORDER BY OccurredAt ASC` an index seek — do not drop without re-measuring.

**Status enum (`BuildingBlocks/Enums/OrderEnums.cs`):**
`OrderStatus { Ordering, Pending, Confirmed, Preparing, Ready, Delivered, Completed, Cancelled, OnHold }`, plus `OrderType { DineIn | Takeout | Delivery }`, `DeliveryStatus`, `PrepStatus`, `SplitType { Equal | Custom }`, `PaymentStatus { Pending | Paid | Void }`.

**Bill splitting.** `OrderBill` has `SplitType` (Equal | Custom) and money columns. `OrderItem.SeatNumber` exists in the schema.

**Endpoint surface (Carter modules):**

| Tag | Method | Route | Sends | AuthZ |
|---|---|---|---|---|
| Orders | POST | `/api/v1/orders` | `CreateOrderCommand` | `orders:create` |
| Orders | PUT | `/api/v1/orders` | `UpdateOrderCommand` | `orders:modify_*` |
| Orders | DELETE | `/api/v1/orders/{id}` | `DeleteOrderCommand` | `orders:modify_*` |
| Orders | GET | `/api/v1/orders/{id}` | `GetOrderByIdQuery` — response carries `Activities[]` (chronological activity feed, each row includes `correlationId` for log-trace correlation) | `orders:view_*` |
| Orders | GET | `/api/v1/orders/{id}/activities` | `GetOrderActivitiesQuery` — standalone paged activity feed (`?type=&from=&to=&page=&pageSize=`) for callers that don't want the full order payload. Filters by `OrderActivityType` + half-open `Instant` date range. Returns `PaginatedResult<OrderActivityDto>` ordered by `OccurredAt ASC, Id ASC`. | `orders:view_own` |
| Orders | GET | `/api/v1/orders` | `GetOrdersQuery` (paged) | `orders:view_*` |
| Orders | GET | `/api/v1/orders/customer/{customerId}` | `GetOrdersByCustomerQuery` | `orders:view_*` |
| **Kitchen** | POST | `/api/v1/orders/{id}/confirm` | `ConfirmOrderCommand` | `kitchen:update_prep_status` |
| **Kitchen** | POST | `/api/v1/orders/{id}/start-prep` | `StartOrderPrepCommand` | `kitchen:update_prep_status` |
| **Kitchen** | POST | `/api/v1/orders/{id}/mark-ready` | `MarkOrderReadyCommand` | `kitchen:update_prep_status` |
| **Kitchen** | POST | `/api/v1/orders/{id}/mark-delivered` | `MarkOrderDeliveredCommand` | `kitchen:update_prep_status` |
| **Kitchen** | POST | `/api/v1/orders/{id}/cancel` (`{ "reason": "..." }`) | `CancelOrderCommand` | `kitchen:update_prep_status` |
| **Kitchen** | POST | `/api/v1/orders/{id}/items/{itemId}/start-prep` | `StartItemPrepCommand` | `kitchen:update_prep_status` |
| **Kitchen** | POST | `/api/v1/orders/{id}/items/{itemId}/mark-ready` | `MarkItemReadyCommand` | `kitchen:update_prep_status` |

The seven Kitchen-tagged endpoints are grouped under `app.MapGroup("/api/v1").WithTags("Kitchen")` and use `RequirePermission("kitchen:update_prep_status")`. They all return `204 NoContent` on success, `404 NotFound` when the order/item is unknown, and `409 Conflict` on illegal transitions (via `InvalidOrderStateTransitionException` / `InvalidOrderItemStateTransitionException`).

**Cross-service HTTP/gRPC.** None. `Ordering.Infrastructure` and `Ordering.API` contain no `HttpClient` / `GrpcClient` / `AddHttpClient` registrations. The only external HTTP target is the Identity service for JWT validation. All coordination with Basket and Kitchen is via RabbitMQ events.

**Consumers.** Six `IConsumer<T>` classes in `Ordering.Application/Orders/EventHandlers/Integration/`, all discovered by `MassTransit.AddMessageBroker(...)` scanning the Application assembly:

- `BasketCheckoutEventHandler` — `IConsumer<BasketCheckoutEvent>` (basket checkout → `Order.Create`).
- `KitchenOrderAcceptedIntegrationEventHandler` — fetch `Order` → `Order.Confirm(...)`.
- `KitchenOrderPrepStartedIntegrationEventHandler` — fetch `Order` → `Order.MarkPreparing(...)`. Emitted exactly once per ticket by `Kitchen.API` on the first item-start action while the ticket is still `New`; the `POST /orders/{id}/start-prep` endpoint remains as a manual override.
- `KitchenOrderReadyIntegrationEventHandler` — fetch `Order` → `Order.MarkReady(...)`.
- `KitchenOrderBumpedIntegrationEventHandler` — log only (no aggregate change today).
- `KitchenOrderCancelledIntegrationEventHandler` — fetch `Order` → `Order.Cancel(...)`.

All five Kitchen-side consumers follow the "fetch latest aggregate, call guarded method" pattern; missing order → log + nack (`InvalidOrderStateTransitionException` on a re-attempted illegal transition is MassTransit-faulted and re-tried by the broker).

**Transactional outbox.** Aggregate events raised in domain methods are dispatched to `IOutboxPublisher` (the EF Core `SaveChangesInterceptor` writes an `outbox_messages` row inside the same transaction). `OrderingOutboxDispatcher` (hosted service) polls the table (1 s active / 5 s idle) and relays each row to `IPublishEndpoint.Publish(...)`, marking it `DispatchedAt` on success. Disabled in tests via `Outbox:Enabled=false`. Consumer-side idempotency keys off `IntegrationEvent.Id`. **Multi-replica safe** — the claim uses engine-native row locks (MSSQL `WITH (ROWLOCK, UPDLOCK, READPAST)` here; Postgres `FOR UPDATE SKIP LOCKED` on Kitchen) held inside an explicit transaction across the claim + broker publish + dispatched-on stamp. **Poison queue in place since F.4**: rows whose `SchemaVersion > OutboxOptions.MaxSupportedVersion` are copied to `outbox_messages_dead` (mirror shape of `outbox_messages` + `Reason` + `RejectedAt`) with `Reason = "unsupported_schema_version"` and skipped on publish. Operators triage from the dead table by bumping `Outbox:MaxSupportedVersion` (after a new consumer deploys) or by patching the payload and replaying.

**Caching.** No Redis usage in Ordering.

**Health:** `/health` via `AspNetCore.HealthChecks.SqlServer` (the database reachability check) plus the broker RabbitMQ check (`AspNetCore.HealthChecks.Rabbitmq` 8.0.2, entry `messagebroker`, tags `["broker", "ready"]`) — landed as the Phase G broker-uniformity follow-on so every service that publishes RabbitMQ traffic reports the broker on `/health` consistently.

---

### 4.6 YarpApiGateway (Port 6004 / 6064)

**Surface:** YARP reverse-proxy only. No controllers, no auth middleware, no token-forward transforms.

- 6 routes, all prefixed `/<service>-api/{**catch-all}` with `PathRemovePrefix` transform. WebSocket upgrades are forwarded transparently — the kitchen SignalR hub is reachable at `ws://localhost:6004/kitchen-api/hubs/kitchen` (negotiate with `?access_token=...`).
- Rate limit policy `"fixed"`: **10 requests / 1 minute per `User.Identity.Name ?? Host`**, no queue. Applied to every route.
- Pipeline: `UseRateLimiter()` → `MapReverseProxy()`.
- The downstream services each enforce their own JWT validation (Identity authority is the configured `IdentityServiceUrl`). The gateway does **not** re-validate tokens. The caller's `Authorization` header reaches downstream services via the ASP.NET HttpClient default propagation.

---

## 5. Inter-Service Communication

### 5.1 Synchronous

| Caller | Callee | Mechanism | Purpose |
|---|---|---|---|
| `Basket.API` | `Discount.Grpc` | gRPC | `GetDiscount` during store |
| every API | `Identity.API` | JWT bearer validation | Validate access tokens (`https://localhost:5057` authority, audience `OrderlyMicroservices`) |
| `YarpApiGateway` | every backend | HTTP reverse proxy | Public entry point for SPA/external clients |

There are no other `HttpClient` registrations across the services. No service-to-service REST calls.

### 5.2 Asynchronous (RabbitMQ via MassTransit)

**Transport.** `rabbitmq:3-management` exposed on `5672` (AMQP) and `15672` (management UI). Configured via `MessageBroker:Host`, `User`, `Password`. Endpoint naming is kebab-case (`SetKebabCaseEndpointNameFormatter()`).

**Abstraction.** No `IEventBus`. MassTransit primitives are used directly (`IPublishEndpoint.Publish`, `IConsumer<T>`, `AddMassTransit` from `BuildingBlocks.Messaging/Extensions.cs`). Base type: `record IntegrationEvent { Id { get; init; } = Guid.NewGuid(); OccurredOn { get; init; } = SystemClock.Instance.GetCurrentInstant(); EventType => GetType().AssemblyQualifiedName!; MessageVersion { get; init; } = 1; }`.

> **Note:** `Id`, `OccurredOn`, and `MessageVersion` are constructor-set (init properties), captured once per instance. Earlier releases used getter expressions that returned a fresh value per read — so consumers can rely on stable event identity for correlation and idempotency. The `MessageVersion` field is the wire-format-versioning handle: the publisher reads it and stamps it into the outbox row's `SchemaVersion` so a single bump propagates through the schema-version gate. Additive changes (new optional fields) are non-breaking because `System.Text.Json` tolerates unknown fields on the read side; breaking changes ship a new event subtype with `MessageVersion = 2` and the same `EntityName` so both shapes route to the same consumer topic during the rollover window.

**Integration events emitted / consumed:**

| Event | Publisher | Consumer |
|---|---|---|
| `BasketCheckoutEvent` | `Basket.API/Messaging/CheckoutBasketOutboxDispatcher` (outbox-mediated; row staged by `Basket.API/Basket/CheckoutBasket/CheckoutBasketHandler`) | `Ordering.Application/.../BasketCheckoutEventHandler` |
| `OrderCreatedIntegrationEvent` | `Ordering.Application/Orders/EventHandlers/Domain/OrderCreatedEventHandler` (gated by `OrderFullfilment` feature flag) | `Kitchen.API/Application/EventHandlers/Integration/OrderCreatedIntegrationEventHandler` (M2) |
| `MenuItemChangedIntegrationEvent` (`ChangeType ∈ Created, Updated, Deleted`) | `Catalog.API/Messaging/.../Feature/MenuItems/*` (gated by `CatalogMenuEvents` feature flag) | **Basket** → invalidate cached price/availability for `MenuItemId` + validate pending baskets. **Discount** → if Deleted, deactivate rules referencing the item; if Updated, re-evaluate BOGO thresholds. **Ordering** → new orders must validate menu item is still valid + available. |
| `IngredientAvailabilityChangedIntegrationEvent` | `Catalog.API/Availability/...` (Phase 3 — Ingredient Availability Engine) | **Basket** → re-validate pending baskets, reject checkout if `Unavailable`, prompt if `Limited`. **Ordering** → reject new orders where status = `Unavailable`. |
| `TableStatusChangedIntegrationEvent` | `Catalog.API/Features/Tables/UpdateTable/UpdateTableHandler` (when `Status` flips) | **Ordering** → reservation / order placement checks `Table.Status == Available`. Walk-in worker assigns waiting parties. Reservation expiry invalidates the hold when status flips to Cancelled / NoShow. |
| `RestaurantConfigurationChangedIntegrationEvent` | `Catalog.API/Features/Restaurants/UpdateRestaurant/UpdateRestaurantHandler` (when any of `TaxRate`/`Currency`/`TimeZone`/`AutoConfirmReservations`/`AllowAutoSubstitute`/`EstimatedTurnoverMinutes` flips) | **Identity** → affected users re-login for fresh JWT claims. **Discount** → if `Currency` changed, deactivate or reissue coupons. **Notification** → receipt templates pick up new tax/currency placeholders. |
| `OrderCompletedIntegrationEvent` | `Ordering.Application` (publish side — wired by separate Ordering plan) | **Catalog** → `OrderCompletedIntegrationEventHandler` updates `MenuItemAnalytics` keyed by `(MenuItemId, AnalysisDate = UTC date)`. Idempotent on `(OrderId, MenuItemId)` via `processed_order_items` table. |
| `KitchenOrderAcceptedIntegrationEvent` | `Kitchen.API/Application/KitchenTickets/Commands/AcceptOrderHandler` | `Ordering.Application/Orders/EventHandlers/Integration/KitchenOrderAcceptedIntegrationEventHandler` → `Order.Confirm(event.ConfirmedByUserId, event.ConfirmedAt)` |
| `KitchenOrderPrepStartedIntegrationEvent` | `Kitchen.API/Application/KitchenTickets/Commands/StartItemPrepHandler` — emitted exactly once per ticket, on the first item-start action while the ticket is still `New` | `Ordering.Application/Orders/EventHandlers/Integration/KitchenOrderPrepStartedIntegrationEventHandler` → `Order.MarkPreparing(event.StartedAt)` |
| `KitchenOrderReadyIntegrationEvent` | `Kitchen.API/Application/KitchenTickets/Commands/MarkOrderReadyHandler` | `Ordering.Application/Orders/EventHandlers/Integration/KitchenOrderReadyIntegrationEventHandler` → `Order.MarkReady(event.ReadyAt)` |
| `KitchenOrderBumpedIntegrationEvent` | `Kitchen.API/Application/KitchenTickets/Commands/BumpOrderHandler` | `Ordering.Application/Orders/EventHandlers/Integration/KitchenOrderBumpedIntegrationEventHandler` (logs only — no aggregate change today) |
| `KitchenOrderCancelledIntegrationEvent` | `Kitchen.API/Application/KitchenTickets/Commands/CancelOrderHandler` | `Ordering.Application/Orders/EventHandlers/Integration/KitchenOrderCancelledIntegrationEventHandler` → `Order.Cancel(event.Reason, event.CancelledByUserId, event.CancelledAt)` |
| `DiscountAppliedIntegrationEvent` (Phase 6, **deferred** — publish-point wired but gated by `Discount:EnableDiscountAppliedPublishing=false`) | `Discount.Grpc/Services/DiscountService` (after the atomic conditional UPDATE in `RedeemDiscount`) | (no consumer today — emit when one is wired) |
| `RewardGeneratedIntegrationEvent` (Phase 6, **deferred** — gated by `Discount:EnableRewardGeneratedPublishing=false`) | `Discount.Grpc/Services/RewardCodeService` (after `DiscountHistoryAppendedIntegrationEvent(ChangeType=Created)` in `CreateRewardCode`) | (no consumer today — emit when one is wired) |
| `RewardRedeemedIntegrationEvent` (Phase 6, **deferred** — gated by `Discount:EnableRewardRedeemedPublishing=false`) | `Discount.Grpc/Services/RewardCodeService` (after the atomic conditional UPDATE in `RedeemRewardCode`) | (no consumer today — emit when one is wired) |

**`OrderCreatedIntegrationEvent` payload** (`BuildingBlocks.Messaging/Events/OrderCreatedIntegrationEvent.cs`):
`OrderId`, `OrderNumber`, `RestaurantId`, `TableId?`, `OrderType`, `CustomerId`, `Subtotal`, `TotalAmount`, `TaxAmount`, `DiscountAmount`, `Currency`, `DiscountCode?`, `BillingAddress`, `DeliveryAddress?` (only when `OrderType.Delivery`), `Items: IReadOnlyList<KitchenOrderItemPreview>`, `EstimatedPrepTimeMinutes`, `Notes`. **No** `Payment*` / `Card*` / `Cvv` / `Expiration` fields — those stay internal to Ordering.

**`KitchenOrderPrepStartedIntegrationEvent` payload** (`BuildingBlocks.Messaging/Events/KitchenOrderPrepStartedIntegrationEvent.cs`, R.1):
`OrderId`, `ItemId`, `StaffUserId`, `StartedAt`. Emitted exactly once per ticket by `StartItemPrepHandler` (when the aggregate's `StartedAt` is still `null` before the call), so Ordering's `MarkPreparing` is driven by the kitchen UI's first-item-prep action rather than the manual REST endpoint.

**Event payload reference:**
```csharp
record BasketCheckoutEvent : IntegrationEvent
{
    public Guid UserId { get; init; }
    public Guid RestaurantId { get; init; }
    public List<BasketCheckoutItem> Items { get; init; }   // MenuItemId, Quantity, UnitPrice, Variations, Customizations
    public decimal TotalAmount { get; init; }
    public BillingAddressForCheckout BillingAddress { get; init; }
    public PaymentForCheckout Payment { get; init; }
}
```

---

## 6. Data Stores

| Store | Image / file | Used by |
|---|---|---|
| Postgres `catalogdb` | `postgres`, host `localhost:5433`, `Database=Catalogdb` | `Catalog.API` (relations + Marten docs + Hangfire `hangfire` schema) |
| Postgres `basketdb` | `postgres`, host `localhost:5434` | `Basket.API` (Marten — `Basket` + `CheckoutBasketOutboxMessage` per-tenant documents, per-tenant databases created on startup) |
| Postgres `identitydb` | `postgres`, host `localhost:5435` | `Identity.API` (Identity + OpenIddict + custom) |
| Postgres `kitchendb` | `postgres`, host `localhost:5436` | `Kitchen.API` — tables `kitchen_tickets`, `kitchen_ticket_items`, `kitchen_stations`, `outbox_messages`, `outbox_messages_dead` |
| MS SQL `orderdb` | `mcr.microsoft.com/mssql/server:2022-latest`, `Server=localhost,1433`, user `sa` | `Ordering.API` — tables `Orders`, `OrderItems`, `OrderBills`, `Customers`, `MenuItems`, `order_activities` (FK to `Orders.Id` with `OnDelete(Cascade)` + covering index `IX_order_activities_OrderId_OccurredAt`; columns `Id`, `OrderId`, `ActivityType nvarchar(50)`, `ActorUserId?`, `OccurredAt`, `CorrelationId nvarchar(100) NULL`, `Notes nvarchar(2000) NULL`, `Metadata nvarchar(max) NULL` — typed `OrderActivityMetadata` record with `JsonStringEnumConverter` so enum values serialize as strings), `outbox_messages`, `outbox_messages_dead` |
| SQLite `discountdb` | file `Data Source=discountdb` | `Discount.Grpc` — tables `Coupons`, `DiscountRules`, `ProcessedInboundevents`, `RewardCodes`, `__EFMigrationsHistory`, plus outbox tables `outbox_messages` (with the `ix_outbox_messages_dispatched_at_occurred_on` index for the dispatcher's hot path) and `outbox_messages_dead` (quarantine for rows whose `SchemaVersion > OutboxOptions.MaxSupportedVersion`). The Coupon table gains `DeletedAt` + `DeletedBy` soft-delete columns (Phase 1.5); the migrations `20260713120000_AddOutboxSupportToDiscount`, `20260713130000_AddSoftDeleteToCoupon`, `20260715015607_AddDiscountRules`, `20260715015933_AddDiscountRulesAndProcessedInboundEvents`, and `20260715225738_AddRewardCodes` are hand-rolled to keep SQLite-specific DDL in one place (the dispatcher's index + the inbound-event dedup index are `migrationBuilder.Sql(...)` rather than EF-managed). The `DiscountRules` table has FK to `Coupons` with `OnDelete(DeleteBehavior.Restrict)` per the project-wide cascade-delete rule (plan §8); the `ProcessedInboundevents` composite PK `(EventId, ConsumerType)` plus `ix_processed_inbound_consumer_time` are the consumer-side idempotency guard for the two Catalog-event consumers. The `RewardCodes` table has UK on `(RestaurantId, Code)` for natural-key lookup per tenant + sweep-friendly index `ix_reward_codes_restaurant_active_expiry` on `(RestaurantId, IsActive, ExpirationDate)`. |
| Redis `distributedcache` | `redis`, host `localhost:6379`, password `redisdev` | `Basket.API` cache (`CachedBasketRepository`) + `Catalog.API` cache (`CachedMenuReader` + `ICatalogCache` invalidation) |
| RabbitMQ `messagebroker` | `rabbitmq:3-management`, ports `5672` / `15672`, `guest`/`guest` | `Basket.API` + `Ordering.Application` |

---

## 7. Authentication & Authorization

- **Identity is the OAuth/OIDC server.** Implements `OpenIddict` server + validation. Tokens are JWTs containing `sub`, `email`, `name`, `firstName`, `lastName`, `isActive`, one `Role` claim per role, one `restaurantId` (default restaurant), one `permissions` claim per granted permission. Access-token lifetime configurable (default 15 min). Refresh-token lifetime configurable (default 7 days).
- **Every other service** calls `AddJwtAuthentication(authority: "<IdentityServiceUrl>", audience: "OrderlyMicroservices")` from `BuildingBlocks.Authorization`. They validate tokens locally.
- **Permission enforcement** is done at the endpoint level via `endpoint.RequirePermission("orders:create")`. The handler reads the `permissions` claim set on the principal.

---

## 8. Multi-Tenancy

- **Brand → Restaurants → everything operational** is the hierarchy in code. `Catalog.API/Models/Brand.cs` is the tenant root; `Restaurant.BrandId` is the FK.
- **Identity multi-restaurant:** `UserRestaurant` (composite PK `UserId + RestaurantId`, `IsDefault`).
- **Basket multi-tenant:** `Marten.CreateDatabasesForTenants(...)` creates one database per tenant on startup, with `ForTenant().CheckAgainstPgDatabase()`.
- **BuildingBlocks/Multitenancy** provides `ITenantEntity` + `TenantQueryFilterExtensions` for global filters. **`ITenantEntity.RestaurantId` is now `Guid`** (P1.1 fixed the pre-existing `int` drift — Catalog entities were always `Guid`). Discount.Grpc is the first adopter: `Coupon` implements `ITenantEntity` and `DiscountContext.OnModelCreating` applies a combined `HasQueryFilter(c => c.DeletedAt == null && c.RestaurantId == _provider.RestaurantId)`. Tenant identity flows through `ICurrentRestaurantProvider` — `ClaimsRestaurantProvider` reads the `restaurantId` JWT claim via `IHttpContextAccessor` (HTTP scope); MassTransit consumers and other out-of-HTTP scopes use **Pattern 2** synthetic principals minted via `BuildingBlocks.Authorization.ClaimsPrincipalBuilder.WithRestaurant(...).WithActor(...).Build()` and attached for the duration of the consume scope via `ICurrentRestaurantProvider.Attach(ClaimsPrincipal)` (an `IDisposable` scope backed by `AsyncLocal<ClaimsPrincipal?>` that overrides the HTTP read while active). The bus-scope implementation in `ClaimsRestaurantProvider` is the single source of truth — no parallel `BusScopedRestaurantProvider` exists today; that surfaces as Phase 5 work. Future services register their provider with `services.AddSingleton<ICurrentRestaurantProvider, ClaimsRestaurantProvider>()` and `ApplyTenantFilter<TEntity>` (or the combined filter when also gating soft-delete).

---

## 9. Cross-Cutting Patterns

- **CQRS via MediatR.** `BuildingBlocks/CQRS` defines `ICommand<TResponse>`, `IQuery<TResponse>`, handlers. Ordering registers open behaviors (`ValidationBehavior<,>`, `LoggingBehavior<,>`); Catalog/Basket register them too. Basket additionally registers `Basket.API.Behaviors.BasketIdentityGuardBehavior<,>` (an open-generic MediatR behaviour) BEFORE `ValidationBehavior<,>` so cross-tenant / cross-user requests short-circuit with 403 before any validation cost is paid — the behaviour matches `IBasketIdentityRequest` and compares the command's `(UserId, RestaurantId)` against the JWT's `ClaimTypes.NameIdentifier` + `restaurantId` claims via `JwtClaimExtensions.GetUserId()` / `GetRestaurantId()`.
- **Validation via FluentValidation.** `services.AddValidatorsFromAssembly(...)`. `ValidationBehavior<TRequest, TResponse>` runs against every `IRequest<TResponse>` (commands *and* queries) — the generic constraint was relaxed in the Phase-1 BuildingBlocks contribution so any validator registered against an `IQuery<TResponse>`-shaped request participates in the pipeline. Empty validator lists remain a no-op.
- **PII / PCI redaction in `LoggingBehavior`.** Requests carrying the `[PciSensitive]` attribute (`BuildingBlocks.Behaviors.PciSensitiveAttribute`) are logged with their payload replaced by `typeof(TRequest).Name + " (payload redacted)"`. The attribute lookup is cached per-type via `ConcurrentDictionary<Type, bool>` so the hot-path cost is a dictionary read after the first invocation. `CheckoutBasketCommand` in `Basket.API` is the first adopter — the card number never reaches a log sink. Future commands carrying PII (addresses, names, emails) or PCI follow the same pattern.
- **Mapster.** Global `using` imports across Catalog/Basket/Ordering. DTOs are flat records.
- **NodaTime everywhere.** EF Core columns are configured with `InstantConverter`; `Npgsql.EntityFrameworkCore.PostgreSQL.NodaTime` is used. `ConfigureForNodaTime(DateTimeZoneProviders.Tzdb)` is set on JSON options, and `dataSourceBuilder.UseNodaTime()` is wired in Catalog.
- **Feature flags.** `Microsoft.FeatureManagement.AspNetCore` exposes `OrderFullfilment` (default true per `appsettings.json`) which gates `OrderCreatedEventHandler`'s publish step.
- **Interceptors.** `BuildingBlocks.Entities.Interceptors.AuditableEntityInterceptor` is registered in every service. **`DispatchDomainEventsInterceptor`** (in-process domain-event dispatch) is registered in Ordering, Kitchen, and **Catalog (Phase 3)**. `OrderingOutboxPublisher` (`Ordering.Infrastructure`), `KitchenOutboxPublisher` (`Kitchen.API`), and now **`DiscountOutboxPublisher`** (`Discount.Grpc`) intercept `SaveChangesAsync` to write `outbox_messages` rows inside the same EF Core transaction as the aggregate mutation; the matching dispatcher hosted services (`OrderingOutboxDispatcher`, Kitchen's equivalent, `DiscountOutboxDispatcher`) relay the rows to `IPublishEndpoint`. **Discount is the first SQLite implementation** of the dispatcher — `OutboxDispatcher<DiscountContext>` overrides `BuildClaimSql(batchSize)` to emit a `SELECT ... WHERE DispatchedAt IS NULL ORDER BY OccurredOn ASC LIMIT @batchSize`; SQLite serializes writes via the engine's database lock held by `BeginTransactionAsync`. The plan's §6.7 `ClaimId`-based multi-replica claiming (Postgres `FOR UPDATE SKIP LOCKED` / MSSQL `WITH (ROWLOCK, UPDLOCK, READPAST)` equivalents) is deferred to a follow-up — the SQLite single-replica case is satisfied by the existing transaction lock. **Outbox circuit breaker (commit-time, not yet wired to Discount):** `BuildingBlocks.Messaging.Outbox.OutboxOptions` exposes `MaxConsecutiveBrokerFailures` (`[Range(1, 100)]` default `3`) and `BrokerBackoffSeconds` (`[Range(00:00:00.100, 01:00:00)]` default `60s`). Discount defines the convention: a per-instance override of `OutboxDispatcher.ExecuteAsync` that increments the counter on a top-level `DispatchOnceAsync` throw (TX-commit failure, broker unreachable, channel closed), resets on the first successful dispatch, and pauses `BrokerBackoffSeconds` between attempts once the counter trips. Per-row publish failures inside `DispatchBatchAsync` stay poison-row local — the breaker only fires for broker-level outages. The `/ready` health-check `broker-circuit` probe reads the counter via a `BrokerHealthState` singleton and trips when the counter exceeds `OutboxOptions.MaxConsecutiveBrokerFailures`. Catalog and future services adopt the same defaults when they wire their dispatchers; the Central BuildingBlocks option only ships the configuration knobs — each service opts into the circuit-breaker behavior by overriding `ExecuteAsync` on its dispatcher. MassTransit bus config is `UsingInMemory` for Phase 1 dev; RabbitMQ wiring is the Phase 4 cross-service hand-off.
- **Caching via Scrutor decorate.** `services.Decorate<IBasketRepository, CachedBasketRepository>()` and `services.Decorate<IMenuReader, CachedMenuReader>()` are the two `IDistributedCache` consumers (Basket + Catalog). The decorator pattern is fail-open: Redis read/write exceptions are caught and logged at `Warning`, never propagated to the caller. The `CatalogRedisCache` feature flag gates the `CacheDriftRepairService` `BackgroundService` that re-populates missing `catalog:menu:{rid}` entries from the DB every `Catalog:CacheRepairIntervalMinutes` (default 5 min). Mutation handlers inject `ICatalogCache` (a thin invalidation helper in `Catalog.API/Caching/ICatalogCache.cs`) and call `InvalidateMenuAsync(restaurantId)` / `InvalidateIngredientsAsync(restaurantId)` after `SaveChangesAsync`; cache-key formats live in `CacheKeys` (`catalog:menu:{rid}`, `catalog:ingredients:{rid}`).
- **Outbox row via Marten (Basket, Phase 2).** Basket is the first non-EF-Core outbox host. The `CheckoutBasketCommandHandler` stages a `CheckoutBasketOutboxMessage` document (`Basket.API/Messaging/CheckoutBasketOutboxMessage.cs`, mirror of `BuildingBlocks.Messaging.Outbox.OutboxMessage`'s row shape) directly via `IDocumentSession.Store(...)` in the same transaction as the cart delete — one `SaveChangesAsync` covers both writes. The `CheckoutBasketOutboxDispatcher : BackgroundService` (`Basket.API/Messaging/CheckoutBasketOutboxDispatcher.cs`) does not extend `OutboxDispatcher<TContext>` — that base class is EF-Core-shaped and cannot be reused against `IDocumentSession`. The polling loop mirrors the base class verbatim (active/idle poll intervals, OperationCanceledException short-circuit) so a future `BuildingBlocks.Messaging.Outbox.MartenOutboxDispatcher<TStore>` can be factored once a second Marten-using service adopts the pattern. The claim uses Marten LINQ (`Where DispatchedAt == null OrderBy OccurredOn Take(batchSize)`) against typed columns extracted via `[DuplicateField]` (the `OccurredOn` + `DispatchedAt` properties duplicate into the `mt_doc_checkoutbasketoutboxmessage` table alongside the JSONB `data` column). Single-replica safety uses Marten's optimistic concurrency (`mt_version` column increments per update); multi-replica safety (raw-SQL `FOR UPDATE SKIP LOCKED` claim via `session.Connection`) is deferred to Phase 4. The broker circuit breaker (Discount's `BrokerHealthState`) is also deferred — Phase 2 v1 ships the linear dispatcher. See `BASKET_SERVICE_PLAN.md` §6 Phase 2 drift items 1–3 for the deviation rationale.

- **Rule-data JSON column.** `DiscountRule.RuleDataJson` is stored as `TEXT` in SQLite (no native JSONB on SQLite) and parsed at the handler boundary; the discriminator (`DiscountRuleKind`) keeps the shape stable across future rule kinds without a proto bump. FluentValidation enforces the deserialized shape per plan §0.3.3 — invalid payloads fail at the handler boundary before they reach the DB. FK from `DiscountRules.CouponId → Coupons.Id` uses `OnDelete(DeleteBehavior.Restrict)` so a coupon with an active rule cannot be deleted; the application surfaces `StatusCode.FailedPrecondition` on the conflict path.
- **`ActiveNow` lazy-eval helper** (`Discount.Grpc/Domain/ActiveNow.cs`). Single canonical answer to "is this entity active right now?" — locked signature per plan §0.4.3.1. `ActiveNow.Coupon(c, clock)` and `ActiveNow.RewardCode(r, clock)` are the two-condition gates (`DeletedAt == null && IsActive && (ExpirationDate is null || ExpirationDate >= now)`). Used by every read path, by `RedeemDiscount` / `RedeemRewardCode` after the conditional UPDATE succeeds, and by `DiscountExpirySweepService`. A divergent copy in any handler is a code-review red flag. `DiscountRule` deliberately has no `ActiveNow.Rule` helper — it has no `ExpirationDate`, so rule activation is the operator's responsibility (the sweep service does not deactivate rules).
- **Reward-code deterministic code builders.** `RewardCode.Code4StarPct10(rid, feedbackEventId, clock)` / `Code5StarPct15(...)` / `Code5StarAppetizer(...)` combine `rid + tag + day-bucket + feedbackEventId` per plan v1.2 H-L1. The day-bucket is the human-readable prefix (audit reports group by date); the event-id suffix is the actual idempotency anchor. Different feedback events land on distinct codes; the same feedback event (bus redelivery) collides on the same UK row — natural-key determinism, no separate `processed_inbound_events` table needed for the `FeedbackSubmittedConsumer` path.
- **Conditional consumer registration (`MassTransit.AddConsumer` gated by options flag).** The project's reference for "wire to the bus, ship disabled." `Program.cs` reads `builder.Configuration.GetSection(DiscountOptions.SectionName).GetValue<bool>(nameof(DiscountOptions.EnableFeedbackSubmittedConsumer))` (default `false`) and conditionally calls `o.AddConsumer<FeedbackSubmittedConsumer>()` inside the `AddMassTransit(...)` builder only when the flag is `true`. MassTransit 8.x has no `ConfigureConsumer.DisableConsumer<T>(...)` API (per plan v1.1 H5); conditional registration is the idiom. On flag flip an orphaned queue may persist — the endpoint materializes on the next boot with no recompile, and the dead-end consumer just holds messages until retention. Each flag (the four `Enable*Consumer` options + the four `Enable*Publishing` options) maps 1:1 to a feature flag in `DiscountOptions`; the `OptionsAuditor` integration test asserts every constant in `DiscountPermissions.All` maps to at least one feature flag OR is unconditional.
- **`RewardCode.RewardKind` vs `Coupon.DiscountType` (planned v8).** Intentionally distinct enums per plan v1.4 Decision A: a `Coupon` is admin-controlled promotional code (Phase 8 adds `Coupon.DiscountType { Percentage, FixedAmount }`), a `RewardCode` is customer-feedback-generated (`RewardKind { Percentage, FixedAmount, FreeItem, Points }`). Future consolidation to a shared `BuildingBlocks.Discounts.DiscountKind` enum is tracked as a v2 BuildingBlocks contribution (out of this plan's scope). The proto-side `RewardType` enum is distinct from the entity-side `RewardKind` enum so the two namespaces don't collide and the cross-cast is explicit at the service boundary (mirrors the `DiscountRuleType` / `DiscountRuleKind` precedent).
- **Correlation context (`BuildingBlocks/Correlation/CorrelationContext.cs`).** Ambient source for the per-request correlation id, propagated via `AsyncLocal<string?>` so the domain layer can stamp it on rows without threading it through every method signature. Three sources feed it: HTTP requests via `LoggingBehavior<TRequest, TResponse>` (reads `X-Correlation-Id` from the request, generates `Guid.NewGuid().ToString()` if absent, sets via `CorrelationContext.Set` and clears in `finally`); MassTransit bus consumers via `ConsumeContext.CorrelationId` (5 Ordering consumers wrap their aggregate call the same way); out-of-band paths leave the ambient null (acceptable — the activity row's `CorrelationId` stays null). Internal setters (`Set` / `Clear`) keep the write surface small; `Current` is public for the domain layer to read. The Ordering `OrderActivity.CorrelationId` column is the first row-level consumer of this primitive; future BuildingBlocks / Catalog / Kitchen audit rows are expected to adopt the same pattern. The Ordering `order_activities` row commits in the same `SaveChangesAsync` transaction as the `outbox_messages` row — same atomicity guarantee, no extra interceptor.

---

## 10. Error Handling & API Conventions

- Global exception handling via `AddExceptionHandler<CustomExceptionHandler>()` from BuildingBlocks; pipeline adds `UseExceptionHandler`. Business exceptions derive from `BuildingBlocks.Exceptions.NotFoundException` (e.g., `OrderNotFoundException`). `BuildingBlocks.Exceptions.ForbiddenException` (added Phase-1 of the Basket plan) maps to HTTP 403 via the handler's switch expression — cross-tenant reads, cross-user mutations, and admin-bypass without the required permission claim throw `ForbiddenException` instead of returning an empty `Results.Forbid()`. Other domain exceptions (`BadRequestException`, `InternalServerException`) extend `Exception` directly per the codebase convention.
- HTTP responses are produced in **PascalCase** in Catalog/Basket (the global `PropertyNamingPolicy = null`). Ordering reuses the framework default and emits camelCase. Standard `Results.Problem(...)` / typed-results pattern from Minimal APIs.
- Carter modules implement `ICarterModule` and `AddCarter()` discovers them via assembly scanning; routes are defined with extension methods on `IEndpointRouteBuilder`.
- Health endpoints at `/health` use `UIResponseWriter.WriteHealthCheckUIResponse`.

---

## 11. Local Development

### Prerequisites
- .NET SDK 10.0.203 (installed by `global.json`).
- Docker Engine + Compose v2.
- `dotnet dev-certs` for the dev cert (`ASPNETCORE_Kestrel__Certificates__Default__Password=password123`).

### Startup sequence
1. `cp orderly-microservices/.env.example orderly-microservices/.env` (optional — defaults bake into `docker-compose.override.yml`).
2. `cd orderly-microservices && docker compose up -d` — brings up `catalogdb`, `basketdb`, `identitydb`, `orderdb`, `kitchendb`, `distributedcache` (Redis), `messagebroker` (RabbitMQ), then each API container.
3. The override file publishes ports **6000–6005, 6007** (HTTP) and **6060–6065, 6067** (HTTPS).
4. Identity seeds the 8 roles, 25 permissions, role-permission mappings, and a `SuperAdmin` user (`admin@orderly.com` / `Admin@123456`) on first start.
5. Catalog migrates and seeds `Brand`/`Restaurant`/menu data via `InitializeMartenWith<CatalogInitialData>()` (dev only). Catalog reads `ConnectionStrings__Redis` from env/compose (`distributedcache:6379`); when `FeatureManagement__CatalogRedisCache=true` the `CacheDriftRepairService` `BackgroundService` starts and begins repopulating missing `catalog:menu:{rid}` keys on the `Catalog:CacheRepairIntervalMinutes` cadence. **Phase 5**: Hangfire's `hangfire` schema is auto-created in the same `catalogdb` on first `AddHangfireServer` startup (Hangfire's `UsePostgreSqlStorage` runs the schema migration as part of its bootstrap); the four recurring jobs schedule themselves via `RecurringJob.AddOrUpdate` once the host is built. The Hangfire dashboard is mounted at `/catalog-api/hangfire` (admin/manager only via `HangfireAdminOnlyFilter`).
6. Ordering migrates with 30-attempt retry and seeds four customers, two menu items, four orders, four bills. The `AddOutboxMessages` migration runs alongside the existing ones; no extra command is required. The `TypedOrderItemCustomizationsJsonb` migration is empty at the SQL level (only the .NET property type changes; the on-disk column stays `nvarchar(max)` jsonb) and lands automatically with the existing migration set.
6a. Kitchen.API migrates the `kitchendb` schema (3 tables + `outbox_messages`). Both services start their outbox dispatcher hosted services alongside the API; tests in either project flip `Outbox:Enabled=false` to skip the relay loop.
7. Discount uses `EF Core Migrations` and runs `Database.MigrateAsync()` on startup; seed data is in `OnModelCreating`. **Phase 6.2:** Coupon is the single Discount entity (no Catalog-side writers, no backfill). `db_relational_model.mermaid` and `db_relational_model.md` updated to drop the Catalog-side Coupon block and the `Restaurants ||--o{ Coupons` relationship row; the `Coupon` entry is removed from the `AuditableEntity<TId>` user list in the companion doc.
8. Kitchen.API migrates the `kitchendb` schema (3 tables: `kitchen_tickets`, `kitchen_ticket_items`, `kitchen_stations`) on first start. The `KitchenTicket` aggregate is built from every inbound `OrderCreatedIntegrationEvent` (status `New`) and is queryable via `GET /api/v1/kitchen/queue` and `GET /api/v1/kitchen/tickets/{id}` (both require `kitchen:view_orders`). State-mutating commands (`accept`, `items/{id}/start`, `items/{id}/ready`, `mark-ready`, `bump`, `recall`, `cancel`) require `kitchen:update_prep_status` and publish aggregate-level integration events (`KitchenOrderAcceptedIntegrationEvent`, `KitchenOrderReadyIntegrationEvent`, `KitchenOrderBumpedIntegrationEvent`, `KitchenOrderCancelledIntegrationEvent`) for Ordering to consume. Live updates broadcast over `/hubs/kitchen` (SignalR) — `IKitchenHubClient` carries `OrderReceived`, `TicketAccepted`, `ItemStateChanged`, `OrderReady`, `OrderBumped`, `OrderCancelled`, `TicketRecalled`. Group topology `restaurant:{id}` (auto-joined from the JWT's `restaurantIds` claim) and `station:{id}` (explicit `JoinStationGroup` invocation).

### YARP, called from outside the compose network
```
https://localhost:6064/identity-api/api/auth/login      # login proxy
http://localhost:6004/catalog-api/api/v1/menu-items     # public catalog proxy
http://localhost:6004/basket-api/api/v1/baskets/...     # public basket proxy
http://localhost:6004/ordering-api/api/v1/orders        # public ordering proxy
http://localhost:6004/kitchen-api/health               # kitchen health proxy
http://localhost:6004/discount-api/                     # gRPC is HTTP/2, not callable via REST
```

> The full discount flow is via `Basket.API` (which holds the gRPC client); calling Discount over the gateway requires a gRPC client, not a REST endpoint.

### Tests
- `Ordering.Domain.Tests` (xUnit + FluentAssertions + NSubstitute).
- `Ordering.Application.Tests` (xUnit + FluentAssertions + NSubstitute — handler-level tests; includes the `OrderCreatedEventHandler` contract tests for "no `PaymentDto` on the bus" guarantee, every state-transition handler's happy + not-found path, the `KitchenOrder*IntegrationEventHandler` cases — including the `KitchenOrderPrepStartedIntegrationEventHandler` — and the `OrderExtensionsPhaseDTests` exercising typed `IReadOnlyList<>` round-trips through `ToOrderCreatedIntegrationEvent`).
- `Ordering.API.Tests` (xUnit + FluentAssertions + Testcontainers + `Microsoft.AspNetCore.Mvc.Testing` — 22 `WebApplicationFactory` integration tests for the seven new Kitchen-tagged Carter endpoints (anonymous 401 / missing-permission 403 / unknown-id 404 / empty-reason 400 / happy 200-204), 2 `/health` checks, plus the F.3 multi-replica outbox row-claim proof (`OrderingOutboxMultiReplicaTests.ParallelDispatchers_EachRowClaimedExactlyOnce`), the poison-queue proof (`OrderingOutboxDeadLetterTests.FutureVersionRow_IsMovedToDeadTable`), and the F.5 wire-format-versioning proof (`OrderingOutboxWireVersioningTests.NewPayload_ExtraFields_RelayWithoutCrash` + `MessageVersionDefaults_ToOne`). Spins up MSSQL 2022 + RabbitMQ 3-management in Testcontainers per test run; `Outbox:Enabled=false` and `FeatureManagement:OrderFullfilment=false` keep the test host quiet.
- `Identity.API.Tests` (xUnit + FluentAssertions + NSubstitute + EF Core InMemory).
- `Kitchen.API.Tests` (xUnit + FluentAssertions + NSubstitute + Testcontainers + `Microsoft.AspNetCore.Mvc.Testing` — 41 unit tests on the `KitchenTicket`/`KitchenTicketItem` aggregates + every command handler + the SignalR broadcaster (the `StartItemPrepHandlerTests` adds 5 publish-once contract tests), plus 12 `WebApplicationFactory` integration tests spinning up Postgres + RabbitMQ in Testcontainers: anonymous 401 paths, authenticated 200/404/400 paths, and a `/health` 200 happy-path check that asserts `entries.messagebroker.status == Healthy`).
- `Catalog.API.Tests` (xUnit + FluentAssertions + NSubstitute + Testcontainers — 22 unit tests on `CachedMenuReader` (hit / miss / null / fail-open paths via NSubstitute on `IDistributedCache.GetAsync`/`SetAsync`) and `CatalogOptions` `DataAnnotation` validation. Testcontainers packages (`Testcontainers.PostgreSql`, `Testcontainers.Redis`) are declared for the integration-test follow-up that exercises the full cache-decorator end-to-end path; the package inventory is in `Catalog.API.Tests.csproj` so the integration tests can land in Phase 1.8 without a project change.
- `Discount.Grpc.Tests` (Phase 1B.c sibling project — xUnit + FluentAssertions + NSubstitute + `TestTimeProvider` + `TestAuthHandler`; SQLite temp-file per fixture; `InternalsVisibleTo("Discount.Grpc.Tests")` so the test project can resolve internal Discount types). Coverage: `DiscountPermissionsTests` (12 policies × both JWT claim shapes × 2 outcomes = `RequireAssertion` matrix lock), `GlobalTenantFilterTests` (cross-tenant read / create / delete), `DiscountExpirySweepTests` (`TestTimeProvider`-pinned sweep + Disabled-flag path + actor-string assertion), `RedeemDiscountRaceTests` (sequential happy + 5-vs-3 concurrent contention), `DiscountOutboxCircuitBreakerTests` (3-failure trip + recovery reset + per-row-no-trip), `DiscountOutboxDeadLetterTests` (future-version quarantined + valid v1 relayed), `RpcEndpointTests` (first gRPC integration tests in the repo: 5 RPCs × happy + 3 negative-path cases through the actual `ServerCallContext` boundary), `HealthCheckSplitTests` (`/live` always 200; `/ready` flips 200 ⇄ 503 with `BrokerHealthState`), `MenuItemChangedConsumerTests` (Catalog-event consumer end-to-end + idempotency on `processed_inbound_events`), `DiscountRuleServiceTests` (6 RPCs × happy + UK-violation path). **Phase 3 additions:** `RewardCodeCodeHelpersTests` (deterministic `Code4StarPct10` / `Code5StarPct15` / `Code5StarAppetizer` helpers — same rid+eventId across day-boundaries → identical code suffix; different events → different codes; 120-char cap respected), `RewardCodeServiceTests` (6 RPCs × happy + UK-violation + FluentValidation kind-specific contract: Percentage-over-100, FreeItem non-zero, empty Code, Code > 120, ExpirationDate in past), `RedeemRewardCodeRaceTests` (sequential happy + 5-vs-3 concurrent contention + over-cap lost-race), `RewardCodeSweepTests` (`TestTimeProvider`-pinned sweep extension flips expired RewardCode rows with `DeletedBy = DiscountActors.Sweep`; future-dated rows untouched; disabled-flag short-circuit). **Phase 4 additions:** `OutboxHistoryPublisherTests` (3 mutations across the three aggregates stage 3 outbox rows with `SchemaVersion=1`; Update mutation carries both `OldValues` + `NewValues` JSON strings; Redeem mutation stamps `ChangeType=Redeemed`). 104 tests total post-Phase 4.

---

## 12. Observability

- **Logging:** stock `ILogger<T>` via Microsoft.Extensions.Logging.
- **Health checks:** `/live` (always 200; process up) and `/ready` (per-service readiness probes tagged `ready`) per service, `UIResponseWriter.WriteHealthCheckUIResponse` for the JSON shape. The full health response includes each registered check:
  - `database` — every backing-store check (`kitchendb`, `orderdb`, `catalogdb`, etc., plus `discount-sqlite` for Discount.Grpc).
  - `redis` — `Basket.API` and `Catalog.API` cache reachability.
  - `rabbitmq` — broker reachability under entry `messagebroker` (tags `["broker", "ready"]`) on `Kitchen.API`, `Ordering.API`, `Basket.API`, **Catalog.API** (Phase 2), and **Discount.Grpc** (Phase 1B — `discount-rabbitmq` under tag `ready`).
  - `outbox_dlq` — `OutboxDeadLetterProbe` reading the `outbox_messages_dead` row count; on **Catalog** returns `Unhealthy` when count exceeds `Catalog:OutboxDeadLetterThreshold` (default `0`). On **Discount** (`discount-outbox-dead-letter`) returns `Unhealthy` when count exceeds `Discount:OutboxDeadLetterThreshold` (default `5` per v1.4 M-L9).
  - **Discount.Grpc only**: `discount-broker-circuit` reads the `BrokerHealthState` singleton written by `DiscountOutboxDispatcher` (per plan §6.7 v1.2 M-L10). Returns `Unhealthy` when the dispatcher's consecutive-broker-failure counter meets or exceeds `OutboxOptions.MaxConsecutiveBrokerFailures` (default `3`).
- **Tracing / metrics:** no OpenTelemetry / Application Insights integration in code.

---

**Last updated against:** `orderly-microservices/` on the date this file was last edited.
**Maintainers:** Development Team + AI agents.