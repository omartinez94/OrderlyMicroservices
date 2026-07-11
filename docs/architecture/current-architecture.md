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
| Auth | **OpenIddict** | 7.5.0 — OIDC server + bearer validation in every service |
| Mapping | **Mapster** | 10.0.7 |
| Validation | FluentValidation | 12.1.1 — open behavior on `ICommand` only |
| Mediator | MediatR | 14.1.0 — BuildingBlocks provides `ICommand<TResponse>` / `IQuery<TResponse>` |
| Time | **NodaTime** | 3.3.2 — `Instant` / `LocalDate` across the schema |
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
| `MenuSubCategory` | relational | Child of category |
| `MenuItem` | relational | `RestaurantId`, base price, prep times, availability, soft-delete |
| `MenuItemVariation` | relational | Size / spice / price modifier |
| `ComboItem` | relational | Combo definition referencing child menu items |
| `Ingredient` | relational | Per restaurant, stock + availability + min stock |
| `MenuItemIngredient` | relational | Quantity required + optional flag |
| `IngredientAlternative` | relational | Original→alternative mapping, auto-substitute flag |
| `PriceHistory` | relational | Audit of price changes (read-only API) |
| `Reservation` | relational | Status workflow via endpoints (`POST` create, `PUT …/{id}/seat|confirm|cancel`) |
| `WalkInQueue` | relational | `POST` create, `PUT …/{id}/seat|notify`, `DELETE …/{id}` |
| `CustomerFeedback` | relational | Read-only API (`GET …/feedback`, `GET …/{id}`) |
| `MenuItemAnalytics` | relational | Read-only aggregated stats |
| `OrderTimingAnalytics` | relational | DbSet |
| `BulkOrderUpload` | relational | DbSet |
| `User` | relational | Domain mirror of the Identity user (Role enum + `RestaurantId` FK) |
| `OrderSnapshot` | Marten document | — |
| `OrderModificationLog` | Marten document | — |
| `OrderItemPriceAudit` | Marten document | — |
| `NotificationLog` | Marten document | — |

**Endpoints by feature (Carter modules, all under `/api/v1`):**
`Brands`, `Restaurants`, `Tables`, `MergedTables`, `Reservations`, `WalkInQueues`, `MenuCategories`, `MenuSubCategories`, `MenuItems`, `MenuItemVariations`, `MenuItemIngredients`, `ComboItems`, `Ingredients`, `IngredientAlternatives`, `PriceHistories`, `CustomerFeedback`, `MenuItemAnalytics`.

**Events published / consumed by Catalog.** Four integration events publish via the outbox (gated by `FeatureManagement__CatalogMenuEvents`, default `true`); one event is consumed.

| Event | Direction | Payload |
|---|---|---|
| `MenuItemChangedIntegrationEvent` (`ChangeType ∈ Created, Updated, Deleted`) | publish | `MenuItemId`, `RestaurantId`, `ChangeType`, optional `Name`/`BasePrice`/`IsAvailable`/`AvailabilityStatus` |
| `IngredientAvailabilityChangedIntegrationEvent` | publish (Phase 3 only) | `MenuItemId`, `RestaurantId`, `AvailabilityStatus`, optional `AutoSubstituteOf` |
| `TableStatusChangedIntegrationEvent` | publish | `TableId`, `RestaurantId`, `NewStatus`, optional `CurrentOrderId` |
| `RestaurantConfigurationChangedIntegrationEvent` | publish | `RestaurantId`, `ChangedFields: IReadOnlyList<string>` |
| `OrderCompletedIntegrationEvent` | consume | `OrderId`, `RestaurantId`, `CompletedAt`, `Items: IReadOnlyList<OrderCompletedItem>` |

The four publish events live under `BuildingBlocks.Messaging/Events/Catalog/`. The `OrderCompletedIntegrationEvent` lives at `BuildingBlocks.Messaging/Events/OrderCompletedIntegrationEvent.cs` (introduced by Catalog's Phase 2 because Catalog is the first consumer; Ordering's publish side lands in a separate Ordering plan). Publishing is at-least-once via the `IOutboxPublisher` pattern — handlers call `await outbox.PublishAsync(new XxxIntegrationEvent { ... }, ct)` after `await dbContext.SaveChangesAsync(...)` and the same EF Core transaction persists both the aggregate mutation and the `outbox_messages` row. The `CatalogOutboxDispatcher` (Postgres `FOR UPDATE SKIP LOCKED` claim, multi-replica safe) relays rows to RabbitMQ via MassTransit. The `OrderCompletedIntegrationEventHandler` (`Catalog.API/Messaging/EventHandlers/`) is idempotent on `(OrderId, MenuItemId)` via a `processed_order_items` table — composite PK throws on duplicate, the handler catches `PostgresException.SqlState == "23505"` and skips. The handler upserts `MenuItemAnalytics` rows keyed by `(MenuItemId, AnalysisDate = UTC date)`.

**Transactional outbox.** `outbox_messages` and `outbox_messages_dead` tables live in `catalogdb` next to the relational data; `processed_order_items` carries the `OrderCompleted` idempotency log. All three are EF Core–configured in `Catalog.API/Data/CatalogDbContext.OnModelCreating` and ship as three Postgres migrations (`AddOutboxMessages`, `AddOutboxDeadMessages`, `AddProcessedOrderItems`). The publisher is scoped (pigs back on the ambient `CatalogDbContext` change tracker); the dispatcher is a singleton hosted service, gated by `Outbox:Enabled` so tests can flip it off. Schema versioning: every event carries `int SchemaVersion = 1`; rows whose `SchemaVersion > OutboxOptions.MaxSupportedVersion` are routed to `outbox_messages_dead` with `Reason = "unsupported_schema_version"` instead of being published. Wire-format additions are non-breaking because `System.Text.Json` tolerates unknown fields on read.

**Caching.** Redis-backed `IDistributedCache` (shared `distributedcache` container, connection string `ConnectionStrings__Redis`). The cache is **fail-open**: every read/write failure is logged at `Warning` and the call falls through to the source. Cache key formats: `catalog:menu:{rid}` (TTL `Catalog:MenuCacheTtlMinutes`, default 60 min) and `catalog:ingredients:{rid}` (TTL `Catalog:IngredientCacheTtlMinutes`, default 5 min — populated by the Phase 3 engine). Read-side: `IMenuReader` (in `Catalog.API/Readers/`) is a tree-building read path (categories → sub-categories → items with variations and ingredients); the Scrutor-decorated `CachedMenuReader` (`services.Decorate<IMenuReader, CachedMenuReader>()`) wraps it for cache-on-read, mirroring the Basket `CachedBasketRepository` pattern. Invalidation: every mutation handler (menu tree: `MenuCategories` CUD, `MenuSubCategories` CU, `MenuItems` CUD, `MenuItemVariations` CUD, `ComboItems` CD; ingredient tree: `Ingredients` CUD, `IngredientAlternatives` CUD, `MenuItemIngredients` AR) injects `ICatalogCache` and calls `InvalidateMenuAsync(restaurantId)` / `InvalidateIngredientsAsync(restaurantId)` after `SaveChangesAsync`. Drift repair: `CacheDriftRepairService` (`Catalog.API/Caching/CacheDriftRepairService.cs`, registered as `AddHostedService<CacheDriftRepairService>()`) is a `BackgroundService` that runs every `Catalog:CacheRepairIntervalMinutes` (default 5 min), enumerates restaurants from `MenuCategories`, and repopulates any missing `catalog:menu:{rid}` entries. The hosted service self-gates on the `CatalogRedisCache` feature flag (`FeatureManagement__CatalogRedisCache`, default `true`) so disabling the flag stops the loop without a redeploy. Configuration is bound via `services.AddOptions<CatalogOptions>().Bind(...).ValidateDataAnnotations().ValidateOnStart()`; `CatalogOptions` lives at `Catalog.API/Caching/CatalogOptions.cs` (cache TTLs + repair interval + `OutboxDeadLetterThreshold`).

**Auth.** `AddJwtAuthentication(authority: configuration["IdentityServiceUrl"] ?? "https://localhost:5057", audience: "OrderlyMicroservices")` plus `AddAuthorizationServices()` from BuildingBlocks.

**Health:** K8s-style split — `/live` (always 200; process up) and `/ready` (Postgres + Redis + RabbitMQ + outbox dead-letter count). `/ready` reports three check entries: `database` (`AspNetCore.HealthChecks.NpgSql`), `redis` (`AspNetCore.HealthChecks.Redis`), `messagebroker` (`AspNetCore.HealthChecks.Rabbitmq` 8.0.2, tags `["ready", "broker"]`), plus the custom `outbox_dlq` check (`OutboxDeadLetterProbe` at `Catalog.API/Health/`) which reads the `outbox_messages_dead` row count and returns `Unhealthy` when it exceeds `Catalog:OutboxDeadLetterThreshold` (default `0` — any dead-letter trips `/ready`). The RabbitMQ check URI is built from `MessageBroker:Host` + `MessageBroker:UserName` + `MessageBroker:Password`. Both `/live` and `/ready` use `UIResponseWriter.WriteHealthCheckUIResponse`; `/ready` filters by `Tags.Contains("ready")`.

---

### 4.3 Basket Service (Port 6001 / 6061)

**Surface:** Carter modules under `/api/v1`. Marten (Postgres, **per-tenant database creation via `CreateDatabasesForTenants`**) + Redis cache (`IDistributedCache` with a 30-minute absolute TTL). Calls Discount over gRPC.

**Two-tier design.** Marten is the durable store; Redis is the cache wrapper applied via `services.Decorate<IBasketRepository, CachedBasketRepository>()`. Cache key is `basket:{userId}:{restaurantId}`; on miss the basket is reloaded from Marten and re-cached for 30 min.

**Cart shape (`Models/Basket.cs`):**
```csharp
public class Basket
{
    [Identity] public Guid UserId { get; set; }
    public Guid RestaurantId { get; set; }
    public List<BasketItem> Items { get; set; } = [];
    public List<string> AppliedDiscounts { get; set; } = [];
    public decimal Subtotal => Items.Sum(x => x.TotalPrice);
    public Instant CreatedAt { get; set; }     // NodaTime
    public Instant ExpiresAt { get; set; }     // stored, not enforced — no cleanup job
}
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

**Endpoints (Carter modules):**

| Method | Route | Permission |
|---|---|---|
| GET | `/api/v1/baskets/{userId}/{restaurantId}` | `orders:view_own` |
| PUT | `/api/v1/baskets/{userId}/{restaurantId}` | `orders:create` |
| DELETE | `/api/v1/baskets/{userId}/{restaurantId}` | none |
| POST | `/api/v1/baskets/checkout` | `orders:create` |
| GET | `/health` | public |

**Discount integration.** `Program.cs` registers `DiscountProtoServiceClient` from `Protos/discount.proto` (a shared project include; `GrpcServices="Client"`). `StoreBasketHandler` calls `discountService.GetDiscountAsync(...)` per `AppliedDiscounts` entry.

**TTL semantics.** Redis side: 30-minute absolute TTL on cache reads/writes. Marten side: `Basket.ExpiresAt` is set but no `IHostedService`, no MassTransit consumer, no Marten TTL pragma actually prunes expired rows — the field is informational.

**Events published.** `BasketCheckoutEvent` only — published by `CheckoutBasketHandler` via MassTransit `IPublishEndpoint.Publish`.

---

### 4.4 Discount Service (Port 6002 / 6062)

**Surface:** gRPC only (HTTP/2, no HTTP routes, no auth, no rate limiter). SQLite file store. `DiscountService` extends `DiscountProtoServiceBase`.

**Single entity: `Coupon`.** Extends `AuditableEntity<int>` (so it has `Id`, `CreatedBy/At`, `LastModifiedBy/At`, `IsActive`, plus `RestaurantId`, `Code`, `Description`, `Amount`, `RedeemAmount`, optional `MaxRedeemAmount`, optional NodaTime `ExpirationDate`). Unique lookup is by `(RestaurantId, Code)`.

**gRPC contract (`Protos/discount.proto`):**
- `GetDiscount(GetDiscountRequest) → GetDiscountResponse` (returns `CouponModel` with `IsActive=false` if not found)
- `CreateDiscount(CreateDiscountRequest) → CreateDiscountResponse`
- `UpdateDiscount(UpdateDiscountRequest) → UpdateDiscountResponse`
- `DeleteDiscount(DeleteDiscountRequest) → CreateDiscountResponse`
- `RedeemDiscount(RedeemDiscountRequest) → RedeemDiscountResponse` (increments `RedeemAmount`; rejects when `MaxRedeemAmount` reached)

Seeded at startup: `DISCOUNT10` (10 off, restaurantId `11111111…`) and `DISCOUNT20` (20 off, restaurantId `22222222…`).

---

### 4.5 Ordering Service (Port 6003 / 6063)

**Surface:** Carter modules under `/api/v1`. MSSQL Server 2022 via EF Core SqlServer. Consumes `BasketCheckoutEvent` from RabbitMQ.

**Layered DDD layout.**
- `Ordering.Domain` (no external deps except `BuildingBlocks`): `Order : Aggregate<OrderId>` with private `OrderItems` list exposed as `IReadOnlyCollection`; entities `OrderItem : Entity<OrderItemId>` (with `MarkItemPreparing` / `MarkItemReady` per-item state transitions), `OrderBill : Entity<int>`, `Customer : AuditableEntity<CustomerId>`, `MenuItem : Entity<MenuItemId>`. Value objects in `ValueObjects/`: `OrderId`, `OrderItemId`, `MenuItemId`, `OrderNumber`, `CustomerId`, `Address` (5-digit ZipCode enforced), `Payment` (3-digit Ccv, MM/YY regex). Domain exceptions in `Ordering.Domain/Exceptions/`: `DomainException`, `InvalidOrderStateTransitionException` (→ HTTP 409), `InvalidOrderItemStateTransitionException` (→ HTTP 409), `OrderNotFoundException`, `OrderItemNotFoundException`.
- `Ordering.Application`: MediatR commands + queries + open behaviors (`ValidationBehavior<,>` runs only on `ICommand<TResponse>`; `LoggingBehavior<,>` runs on everything). Inter-feature segment: `Orders/Commands/` (Create, Update, Delete, Confirm, StartOrderPrep, MarkOrderReady, Cancel, StartItemPrep, MarkItemReady, MarkOrderDelivered), `Orders/Queries/`, `Orders/EventHandlers/Domain/`, `Orders/EventHandlers/Integration/` (`BasketCheckoutEventHandler` + the four `KitchenOrder*IntegrationEventHandler` consumers). `Dtos/` + `Dtos/Validators/`. `FeatureManagement` is registered so the `OrderFullfilment` flag gates `OrderCreatedEventHandler`.
- `Ordering.Infrastructure`: `ApplicationDBContext` with DbSets `Customers`, `Orders`, `OrderItems`, `MenuItems`, `OrderBills`, `OutboxMessages`. Configurations use EF Core's `ComplexProperty` for nested `BillingAddress` / `DeliveryAddress` / `Payment` value objects. Migrations: `InitialCreate`, `AddOrderBill`, `20260706233202_AddOutboxMessages`. Migrations retry up to 30× on SQL errors 1801/4060/233/-2, then seed four orders via `InitialData`. Transactional outbox: `OrderingOutboxPublisher` (interceptor) writes to `outbox_messages` inside the same transaction as the aggregate mutation; `OrderingOutboxDispatcher` (hosted service) polls + relays to `IPublishEndpoint`.
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

**Status enum (`BuildingBlocks/Enums/OrderEnums.cs`):**
`OrderStatus { Ordering, Pending, Confirmed, Preparing, Ready, Delivered, Completed, Cancelled, OnHold }`, plus `OrderType { DineIn | Takeout | Delivery }`, `DeliveryStatus`, `PrepStatus`, `SplitType { Equal | Custom }`, `PaymentStatus { Pending | Paid | Void }`.

**Bill splitting.** `OrderBill` has `SplitType` (Equal | Custom) and money columns. `OrderItem.SeatNumber` exists in the schema.

**Endpoint surface (Carter modules):**

| Tag | Method | Route | Sends | AuthZ |
|---|---|---|---|---|
| Orders | POST | `/api/v1/orders` | `CreateOrderCommand` | `orders:create` |
| Orders | PUT | `/api/v1/orders` | `UpdateOrderCommand` | `orders:modify_*` |
| Orders | DELETE | `/api/v1/orders/{id}` | `DeleteOrderCommand` | `orders:modify_*` |
| Orders | GET | `/api/v1/orders/{id}` | `GetOrderByIdQuery` | `orders:view_*` |
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
| `BasketCheckoutEvent` | `Basket.API/CheckoutBasket/CheckoutBasketHandler` | `Ordering.Application/.../BasketCheckoutEventHandler` |
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
| Postgres `catalogdb` | `postgres`, host `localhost:5433`, `Database=Catalogdb` | `Catalog.API` (relations + Marten docs) |
| Postgres `basketdb` | `postgres`, host `localhost:5434` | `Basket.API` (Marten, per-tenant databases created on startup) |
| Postgres `identitydb` | `postgres`, host `localhost:5435` | `Identity.API` (Identity + OpenIddict + custom) |
| Postgres `kitchendb` | `postgres`, host `localhost:5436` | `Kitchen.API` — tables `kitchen_tickets`, `kitchen_ticket_items`, `kitchen_stations`, `outbox_messages`, `outbox_messages_dead` |
| MS SQL `orderdb` | `mcr.microsoft.com/mssql/server:2022-latest`, `Server=localhost,1433`, user `sa` | `Ordering.API` — tables `Orders`, `OrderItems`, `OrderBills`, `Customers`, `MenuItems`, `outbox_messages`, `outbox_messages_dead` |
| SQLite `discountdb` | file `Data Source=discountdb` | `Discount.Grpc` |
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
- **BuildingBlocks/Multitenancy** provides `ITenantEntity` + `TenantQueryFilterExtensions` for global filters.

---

## 9. Cross-Cutting Patterns

- **CQRS via MediatR.** `BuildingBlocks/CQRS` defines `ICommand<TResponse>`, `IQuery<TResponse>`, handlers. Ordering registers open behaviors (`ValidationBehavior<,>`, `LoggingBehavior<,>`); Catalog/Basket register them too.
- **Validation via FluentValidation.** `services.AddValidatorsFromAssembly(...)`. Validation behavior runs only on `ICommand<TResponse>`.
- **Mapster.** Global `using` imports across Catalog/Basket/Ordering. DTOs are flat records.
- **NodaTime everywhere.** EF Core columns are configured with `InstantConverter`; `Npgsql.EntityFrameworkCore.PostgreSQL.NodaTime` is used. `ConfigureForNodaTime(DateTimeZoneProviders.Tzdb)` is set on JSON options, and `dataSourceBuilder.UseNodaTime()` is wired in Catalog.
- **Feature flags.** `Microsoft.FeatureManagement.AspNetCore` exposes `OrderFullfilment` (default true per `appsettings.json`) which gates `OrderCreatedEventHandler`'s publish step.
- **Interceptors.** `BuildingBlocks.Entities.Interceptors.AuditableEntityInterceptor` and `DispatchDomainEventsInterceptor` are registered in Ordering, Catalog, Basket (the latter via `Scrutor` decorator). `OrderingOutboxPublisher` (`Ordering.Infrastructure`) and `KitchenOutboxPublisher` (`Kitchen.API`) intercept `SaveChangesAsync` to write `outbox_messages` rows inside the same EF Core transaction as the aggregate mutation; the matching dispatcher hosted service relays the rows to `IPublishEndpoint`. **Catalog's Phase 2 implementation skips the interceptor pattern** — there is no `IDomainEvent` infrastructure in Catalog today (Catalog entities are POCOs, no `Aggregate<T>` base, no `IAggregate`). Mutation handlers inject `IOutboxPublisher` directly and call `await outbox.PublishAsync(new XxxIntegrationEvent { ... }, ct)` after `await dbContext.SaveChangesAsync(ct)`. The same `SaveChangesAsync` that persists the aggregate mutation persists the `outbox_messages` row, so the semantics match Ordering's interceptor pattern; the difference is mechanical (direct call vs. interceptor-driven dispatch).
- **Caching via Scrutor decorate.** `services.Decorate<IBasketRepository, CachedBasketRepository>()` and `services.Decorate<IMenuReader, CachedMenuReader>()` are the two `IDistributedCache` consumers (Basket + Catalog). The decorator pattern is fail-open: Redis read/write exceptions are caught and logged at `Warning`, never propagated to the caller. The `CatalogRedisCache` feature flag gates the `CacheDriftRepairService` `BackgroundService` that re-populates missing `catalog:menu:{rid}` entries from the DB every `Catalog:CacheRepairIntervalMinutes` (default 5 min). Mutation handlers inject `ICatalogCache` (a thin invalidation helper in `Catalog.API/Caching/ICatalogCache.cs`) and call `InvalidateMenuAsync(restaurantId)` / `InvalidateIngredientsAsync(restaurantId)` after `SaveChangesAsync`; cache-key formats live in `CacheKeys` (`catalog:menu:{rid}`, `catalog:ingredients:{rid}`).

---

## 10. Error Handling & API Conventions

- Global exception handling via `AddExceptionHandler<CustomExceptionHandler>()` from BuildingBlocks; pipeline adds `UseExceptionHandler`. Business exceptions derive from `BuildingBlocks.Exceptions.NotFoundException` (e.g., `OrderNotFoundException`).
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
5. Catalog migrates and seeds `Brand`/`Restaurant`/menu data via `InitializeMartenWith<CatalogInitialData>()` (dev only). Catalog reads `ConnectionStrings__Redis` from env/compose (`distributedcache:6379`); when `FeatureManagement__CatalogRedisCache=true` the `CacheDriftRepairService` `BackgroundService` starts and begins repopulating missing `catalog:menu:{rid}` keys on the `Catalog:CacheRepairIntervalMinutes` cadence.
6. Ordering migrates with 30-attempt retry and seeds four customers, two menu items, four orders, four bills. The `AddOutboxMessages` migration runs alongside the existing ones; no extra command is required. The `TypedOrderItemCustomizationsJsonb` migration is empty at the SQL level (only the .NET property type changes; the on-disk column stays `nvarchar(max)` jsonb) and lands automatically with the existing migration set.
6a. Kitchen.API migrates the `kitchendb` schema (3 tables + `outbox_messages`). Both services start their outbox dispatcher hosted services alongside the API; tests in either project flip `Outbox:Enabled=false` to skip the relay loop.
7. Discount uses `EF Core Migrations` and runs `Database.MigrateAsync()` on startup; seed data is in `OnModelCreating`.
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

---

## 12. Observability

- **Logging:** stock `ILogger<T>` via Microsoft.Extensions.Logging.
- **Health checks:** `/live` (always 200; process up) and `/ready` (Postgres + Redis + RabbitMQ + outbox dead-letter count) per service, UI response writer. The full health response includes each registered check:
  - `database` — every backing-store check (`kitchendb`, `orderdb`, `catalogdb`, etc.).
  - `redis` — `Basket.API` and `Catalog.API` cache reachability.
  - `rabbitmq` — broker reachability under entry `messagebroker` (tags `["broker", "ready"]`); wired via `AspNetCore.HealthChecks.Rabbitmq` 8.0.2 on `Kitchen.API`, `Ordering.API`, `Basket.API`, and **Catalog.API** (Phase 2).
  - `outbox_dlq` — `OutboxDeadLetterProbe` (Catalog only, Phase 2) reading the `outbox_messages_dead` row count; returns `Unhealthy` when count exceeds `Catalog:OutboxDeadLetterThreshold` (default `0` — any dead-letter trips `/ready`).
- **Tracing / metrics:** no OpenTelemetry / Application Insights integration in code.

---

**Last updated against:** `orderly-microservices/` on the date this file was last edited.
**Maintainers:** Development Team + AI agents.