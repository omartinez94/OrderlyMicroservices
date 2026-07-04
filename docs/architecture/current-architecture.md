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
| Messaging | **MassTransit + RabbitMQ** | 8.5.10 — `rabbitmq:3-management` in compose |
| Auth | **OpenIddict** | 7.5.0 — OIDC server + bearer validation in every service |
| Mapping | **Mapster** | 10.0.7 |
| Validation | FluentValidation | 12.1.1 — open behavior on `ICommand` only |
| Mediator | MediatR | 14.1.0 — BuildingBlocks provides `ICommand<TResponse>` / `IQuery<TResponse>` |
| Time | **NodaTime** | 3.3.2 — `Instant` / `LocalDate` across the schema |
| Gateway | **YARP** | 2.3.0 |
| Resilience | `Microsoft.AspNetCore.RateLimiting` (built-in) | — Fixed-window on Identity (5/15min/IP) and YARP (10/1min/host) |
| Health | `AspNetCore.HealthChecks.{NpgSql,Redis,SqlServer,UI.Client}` | 9.0.0 — every service exposes `/health` |
| API style | Carter modules + MediatR commands/queries | — DTOs and validators co-located under `Features/<Entity>/` |
| Logging | ASP.NET Core default `ILogger` | — |
| Frontend | none in-repo | — |

---

## 3. Solution Layout

Source root: `orderly-microservices/`. 12 projects + 2 test projects + a Docker compose project.

```
orderly-microservices/
├── ApiGateway/
│   └── YarpApiGateway/                         # YARP front door (port 6004 / 6064)
├── BuildingBlocks/                             # Shared lib (CQRS, Behaviors, Authorization, Multitenancy, Entities)
├── BuildingBlocks.Messaging/                   # MassTransit + IntegrationEvent base
├── Services/
│   ├── Catalog/Catalog.API/                    # Brands, restaurants, tables, menus, reservations, snapshots
│   ├── Basket/Basket.API/                      # Marten + Redis cache, gRPC client to Discount, publishes BasketCheckoutEvent
│   ├── Discount/Discount.Grpc/                 # gRPC server, SQLite store, single Coupon entity
│   ├── Identity/Identity.API/                  # OpenIddict + ASP.NET Identity + RBAC permissions
│   ├── Kitchen/Kitchen.API/                    # Kitchen fulfilment, SignalR hub, Postgres `kitchendb`
│   ├── Kitchen/Kitchen.API.Tests/              # xUnit + FluentAssertions + NSubstitute for the Kitchen domain
│   └── Ordering/
│       ├── Ordering.Domain/                    # Aggregate<Order>, OrderItem, OrderBill, Customer, MenuItem; value objects
│       ├── Ordering.Application/               # MediatR commands/queries, domain + integration handlers
│       ├── Ordering.Infrastructure/            # EF Core + MSSQL, interceptors, migrations
│       └── Ordering.API/                       # Carter endpoints + MassTransit consumer
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
| Ordering.API | `ordering.api` | 6003 | 6063 | MSSQL + MassTransit consumer |
| YarpApiGateway | `yarpapigateway` | 6004 | 6064 | YARP, fixed-window rate limit |
| Kitchen.API | `kitchen.api` | 6005 | 6065 | Postgres (`kitchendb`) + SignalR `/hubs/kitchen` — domain, read + command endpoints, outbound integration events, and live broadcast |
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

**Events published / consumed by Catalog.** None. Catalog registers no `IHostedService`, no MassTransit endpoint, no `IPublishEndpoint`.

**Caching.** None. The Redis distributed cache is registered only in `Basket.API`.

**Auth.** `AddJwtAuthentication(authority: configuration["IdentityServiceUrl"] ?? "https://localhost:5057", audience: "OrderlyMicroservices")` plus `AddAuthorizationServices()` from BuildingBlocks.

**Health:** `/health` via `AspNetCore.HealthChecks.NpgSql`.

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
- `Ordering.Domain` (no external deps except `BuildingBlocks`): `Order : Aggregate<OrderId>` with private `OrderItems` list exposed as `IReadOnlyCollection`; entities `OrderItem : Entity<OrderItemId>`, `OrderBill : Entity<int>`, `Customer : AuditableEntity<CustomerId>`, `MenuItem : Entity<MenuItemId>`. Value objects in `ValueObjects/`: `OrderId`, `OrderItemId`, `MenuItemId`, `OrderNumber`, `CustomerId`, `Address` (5-digit ZipCode enforced), `Payment` (3-digit Ccv, MM/YY regex). Domain exceptions live in `Ordering.Domain/Exceptions/DomainException.cs`.
- `Ordering.Application`: MediatR commands + queries + open behaviors (`ValidationBehavior<,>` runs only on `ICommand<TResponse>`; `LoggingBehavior<,>` runs on everything). Inter-feature segment: `Orders/Commands/`, `Orders/Queries/`, `Orders/EventHandlers/Domain/`, `Orders/EventHandlers/Integration/`. `Dtos/` + `Dtos/Validators/`. `FeatureManagement` is registered so the `OrderFullfilment` flag gates `OrderCreatedEventHandler`.
- `Ordering.Infrastructure`: `ApplicationDBContext` with DbSets `Customers`, `Orders`, `OrderItems`, `MenuItems`, `OrderBills`. Configurations use EF Core's `ComplexProperty` for nested `BillingAddress` / `DeliveryAddress` / `Payment` value objects. Two migrations exist (`InitialCreate` and `AddOrderBill`). Migrations retry up to 30× on SQL errors 1801/4060/233/-2, then seed four orders via `InitialData`.
- `Ordering.API`: 6 Carter endpoints + a `BasketCheckoutEventConsumer`.

**Aggregate behaviour.**
- `Order.Create(...)` → raises `OrderCreatedEvent`. The handler (under the `OrderFullfilment` feature flag) maps to an `OrderDto` and publishes via `IPublishEndpoint`.
- `Order.Update(...)` → overwrites billing/delivery address, payment, and status in one shot; raises `OrderUpdatedEvent` (handler only logs).
- `Order.Add(menuItemId, quantity, price)` / `Order.Remove(menuItemId)`.

**Status enum (`BuildingBlocks/Enums/OrderEnums.cs`):**
`OrderStatus { Ordering, Pending, Confirmed, Preparing, Ready, Delivered, Completed, Cancelled, OnHold }`, plus `OrderType { DineIn | Takeout | Delivery }`, `DeliveryStatus`, `PrepStatus`, `SplitType { Equal | Custom }`, `PaymentStatus { Pending | Paid | Void }`.

**Bill splitting.** `OrderBill` has `SplitType` (Equal | Custom) and money columns. `OrderItem.SeatNumber` exists in the schema.

**Endpoint surface (Carter modules):**

| Method | Route | Sends |
|---|---|---|
| POST | `/api/v1/orders` | `CreateOrderCommand` |
| PUT | `/api/v1/orders` | `UpdateOrderCommand` |
| DELETE | `/api/v1/orders/{id}` | `DeleteOrderCommand` |
| GET | `/api/v1/orders/{id}` | `GetOrderByIdQuery` |
| GET | `/api/v1/orders` | `GetOrdersQuery` (paged) |
| GET | `/api/v1/orders/customer/{customerId}` | `GetOrdersByCustomerQuery` |

**Cross-service HTTP/gRPC.** None. `Ordering.Infrastructure` and `Ordering.API` contain no `HttpClient` / `GrpcClient` / `AddHttpClient` registrations. The only external HTTP target is the Identity service for JWT validation. All coordination with Basket is via `BasketCheckoutEvent` on RabbitMQ.

**Consumer.** `Ordering.Application/Orders/EventHandlers/Integration/BasketCheckoutEventHandler.cs` is the registered `IConsumer<BasketCheckoutEvent>` (only the Application assembly is scanned).

**Caching.** No Redis usage in Ordering.

**Health:** `/health` via `AspNetCore.HealthChecks.SqlServer`.

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

**Abstraction.** No `IEventBus`. MassTransit primitives are used directly (`IPublishEndpoint.Publish`, `IConsumer<T>`, `AddMassTransit` from `BuildingBlocks.Messaging/Extensions.cs`). Base type: `record IntegrationEvent { Id { get; init; } = Guid.NewGuid(); OccurredOn { get; init; } = SystemClock.Instance.GetCurrentInstant(); EventType => GetType().AssemblyQualifiedName!; }`.

> **Note:** `Id` and `OccurredOn` are constructor-set (init properties), captured once per instance. Earlier releases used getter expressions that returned a fresh value per read — fixed in M0 per KITCHEN_INTEGRATION_PLAN.md Phase 1 so consumers can rely on stable event identity for correlation and idempotency.

**Integration events emitted / consumed:**

| Event | Publisher | Consumer |
|---|---|---|
| `BasketCheckoutEvent` | `Basket.API/CheckoutBasket/CheckoutBasketHandler` | `Ordering.Application/.../BasketCheckoutEventHandler` |
| `OrderCreatedIntegrationEvent` | `Ordering.Application/Orders/EventHandlers/Domain/OrderCreatedEventHandler` (gated by `OrderFullfilment` feature flag) | `Kitchen.API/Application/EventHandlers/Integration/OrderCreatedIntegrationEventHandler` (M2) |
| `KitchenOrderAcceptedIntegrationEvent` | `Kitchen.API/Application/KitchenTickets/Commands/AcceptOrderHandler` | **Ordering** — pending consumer (Pending → Confirmed via `Order.Confirm`) |
| `KitchenOrderReadyIntegrationEvent` | `Kitchen.API/Application/KitchenTickets/Commands/MarkOrderReadyHandler` | **Ordering** — pending consumer (Preparing → Ready via `Order.MarkReady`) |
| `KitchenOrderBumpedIntegrationEvent` | `Kitchen.API/Application/KitchenTickets/Commands/BumpOrderHandler` | none today; recorded for audit / analytics |
| `KitchenOrderCancelledIntegrationEvent` | `Kitchen.API/Application/KitchenTickets/Commands/CancelOrderHandler` | **Ordering** — pending consumer (any → Cancelled via `Order.Cancel`) |

**`OrderCreatedIntegrationEvent` payload** (`BuildingBlocks.Messaging/Events/OrderCreatedIntegrationEvent.cs`):
`OrderId`, `OrderNumber`, `RestaurantId`, `TableId?`, `OrderType`, `CustomerId`, `Subtotal`, `TotalAmount`, `TaxAmount`, `DiscountAmount`, `Currency`, `DiscountCode?`, `BillingAddress`, `DeliveryAddress?` (only when `OrderType.Delivery`), `Items: IReadOnlyList<KitchenOrderItemPreview>`, `EstimatedPrepTimeMinutes`, `Notes`. **No** `Payment*` / `Card*` / `Cvv` / `Expiration` fields — those stay internal to Ordering.

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
| Postgres `kitchendb` | `postgres`, host `localhost:5436` | `Kitchen.API` — tables `kitchen_tickets`, `kitchen_ticket_items`, `kitchen_stations` |
| MS SQL `orderdb` | `mcr.microsoft.com/mssql/server:2022-latest`, `Server=localhost,1433`, user `sa` | `Ordering.API` |
| SQLite `discountdb` | file `Data Source=discountdb` | `Discount.Grpc` |
| Redis `distributedcache` | `redis`, host `localhost:6379`, password `redisdev` | `Basket.API` cache only |
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
- **Interceptors.** `BuildingBlocks.Entities.Interceptors.AuditableEntityInterceptor` and `DispatchDomainEventsInterceptor` are registered in Ordering, Catalog, Basket (the latter via `Scrutor` decorator).
- **Caching via Scrutor decorate.** `services.Decorate<IBasketRepository, CachedBasketRepository>()` is the only `IDistributedCache` consumer.

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
5. Catalog migrates and seeds `Brand`/`Restaurant`/menu data via `InitializeMartenWith<CatalogInitialData>()` (dev only).
6. Ordering migrates with 30-attempt retry and seeds four customers, two menu items, four orders, four bills.
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
- `Ordering.Application.Tests` (xUnit + FluentAssertions + NSubstitute — handler-level tests; includes the `OrderCreatedEventHandler` contract tests for "no `PaymentDto` on the bus" guarantee).
- `Identity.API.Tests` (xUnit + FluentAssertions + NSubstitute + EF Core InMemory).
- `Kitchen.API.Tests` (xUnit + FluentAssertions + NSubstitute — aggregate-level transition tests for `KitchenTicket`/`KitchenTicketItem`; covers every legal transition plus the corresponding negative-path rejections).

---

## 12. Observability

- **Logging:** stock `ILogger<T>` via Microsoft.Extensions.Logging.
- **Health checks:** `/health` per service, UI response writer. The full health response includes each registered check (`database`, `redis`, `rabbitmq` via Basket only).
- **Tracing / metrics:** no OpenTelemetry / Application Insights integration in code.

---

**Last updated against:** `orderly-microservices/` on the date this file was last edited.
**Maintainers:** Development Team + AI agents.