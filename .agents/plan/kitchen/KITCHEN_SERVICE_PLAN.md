# Kitchen.API — Service Plan

> Scope: full design for the new `Kitchen.API` microservice — domain model, API surface, real-time channel, integration events, persistence, ops, and milestones. The companion plan covering everything Ordering must do first is `Services/Ordering/Ordering.API/KITCHEN_INTEGRATION_PLAN.md`.

---

## 1. Context

The codebase already signals that a Kitchen service was always intended:

- `OrderItem.PrepStatus` (Pending / Preparing / Ready) + `PrepStartedAt` + `PrepCompletedAt` columns exist in the Ordering schema.
- `Order` carries `EstimatedPrepTimeMinutes`, `ActualPrepTimeMinutes`, `PreparingStartedAt`, `ReadyAt`.
- `Identity` seeds `kitchen:view_orders`, `kitchen:update_prep_status`, `KitchenManager`, `KitchenStaff`.
- `Ordering` already publishes `OrderCreatedEventHandler` over RabbitMQ behind `FeatureManagement__OrderFullfilment=true` — currently leaking `PaymentDto` data, which the Ordering-side plan will fix first.
- The repo runs MassTransit + RabbitMQ (`BuildingBlocks.Messaging`) and YARP gateway already wires pass-through routes for five services.
- No SignalR is in use today (zero `MapHub` / `IHubContext` references). This service is the first to need it.

There is no frontend in the repo today. The UI is being built in a separate project and folder and is out of scope.

## 2. Goal

A single-purpose microservice that consumes order-creation events, builds a kitchen-shaped projection (`KitchenTicket`), drives per-item prep state through `OrderItem`-level transitions, and pushes live updates to subscribed kitchen displays via SignalR — over a bearer-secured WebSocket route through YARP.

## 3. Out of scope

- The UI project (different folder, per user).
- New authorization infrastructure in Identity (permissions already seeded by the Ordering-side plan + Identity DataSeeder).
- Replacing the existing Ordering CRUD endpoints — Kitchen never calls them; it serves its own projections and publishes events back.
- A saga/orchestrator for fulfilment. Phase 2 keeps Kitchen as a self-contained consumer/publisher; orchestrators come later if/when needed.

## 4. Service boundaries

### Kitchen.API owns

- The `KitchenTicket` aggregate (one per `Order` once the order arrives — keeps a 1:1 with `Order.Id` to make correlation trivial).
- The `KitchenStation` catalog (lightweight — id, display name, sort order; per-restaurant).
- The kitchen queue read model (sorted, filtered, paginated).
- The SignalR hub contract and its group topology (`restaurant:{id}`, `station:{id}`).
- Its own Postgres database (`kitchendb`) and EF Core migrations.

### Kitchen.API does NOT own

- The `Order` aggregate. Ordering remains the write-master for `Order.Status`. Kitchen **publishes** events that Ordering consumes; the plans run in lockstep.
- Payment, identity, menu data. Reads via Ordering's reference objects (`MenuItem`, `OrderNumber`) are loaded from the inbound event only — no cross-service HTTP read on the hot path.
- The kitchen UI.

### Ordering ↔ Kitchen flow (one-liner)

```
Ordering publishes OrderCreatedIntegrationEvent
  → Kitchen consumer builds KitchenTicket, broadcasts to /hubs/kitchen groups
Kitchen receives a UI command (REST) → mutates KitchenTicket → publishes
  OrderPrepStateChangedIntegrationEvent / OrderItemReadyIntegrationEvent /
  KitchenOrderReadyIntegrationEvent / KitchenOrderBumpedIntegrationEvent
Ordering consumes those events and applies the new state-transition
  methods (Confirm / MarkPreparing / MarkReady / ...) introduced in the
  Ordering plan.
```

The UI sees updates two ways:

- Push: the SignalR hub fires the appropriate method on its groups.
- Pull: the REST endpoints expose the kitchen queue and per-ticket detail for initial loads.

## 5. Tech decisions

| Decision | Choice | Reason |
|---|---|---|
| Architecture | Vertical Slice (Catalog/Basket pattern), single project | Matches 3 of 5 existing services; kitchen is single-purpose. |
| Framework | ASP.NET Core 10 (carter + minimal API) | Same as the rest of the codebase. |
| Persistence | EF Core 10 + `Npgsql.EntityFrameworkCore.PostgreSQL` | Single database per service; matches Catalog. |
| Database | PostgreSQL on host port `5436`, named `kitchendb` | Fills the gap left by Catalog (5433) / Basket (5434) / Identity (5435). |
| Real-time | `Microsoft.AspNetCore.SignalR` (the only SignalR in the repo) | Built-in group semantics for `restaurant:{id}` / `station:{id}`. |
| Messaging | MassTransit + RabbitMQ | Reuse `BuildingBlocks.Messaging`. |
| Auth | `BuildingBlocks.Authorization.AddJwtAuthentication(authority: "<IdentityServiceUrl>", audience: "OrderlyMicroservices")` | Same pattern as other services. |
| Feature flags | `Microsoft.FeatureManagement` | Reuse `OrderFullfilment` flag (kill switch controlled by Ordering, not duplicated here). |
| Time / IDs | NodaTime `Instant`, `Guid` ids | Matches AGENTS.md conventions. |
| Logging | `Microsoft.Extensions.Logging` + structured logging | Default. |
| Ports | HTTP `6005`, HTTPS `6065` (host); container `8080/8081` | Matches existing 6003/6063 (Ordering), 6004/6064 (YARP gateway). |
| Service name | `kitchen.api` (container) / `kitchen-api/...` (gateway prefix) | Consistent with the other `*.api` containers. |
| Outbox | None — Phase 1 inherits the same gap as Ordering | Documented; revisit after both services are live. |

### What this service does NOT introduce

- No gRPC. Same reasoning as Ordering: there is no cross-service need for binary contracts.
- No new message broker. RabbitMQ is fine.
- No new permission system. The two `kitchen:*` permissions already exist in Identity.

## 6. Folder layout

```
orderly-microservices/Services/Kitchen/Kitchen.API/
  Domain/                              -- pure domain, no EF, no MediatR
    Aggregates/
      KitchenTicket/                   -- aggregate + child entities + behaviour
    Events/                            -- in-process domain events (IDomainEvent)
    ValueObjects/                      -- KitchenTicketId, StationId, etc.
    Enums/                             -- KitchenTicketStatus, KitchenItemStatus
  Application/
    Abstractions/                      -- IKitchenTicketRepository, IUnitOfWork, ICurrentUser
    Dtos/                              -- KitchenTicketDto, KitchenTicketItemDto, etc.
    Exceptions/                        -- KitchenTicketNotFoundException, etc.
    EventHandlers/
      Domain/                          -- in-process handlers (e.g. audit/analytics)
      Integration/                     -- IConsumer<X> for inbound contracts
    KitchenTickets/
      Commands/                        -- AcceptOrder, StartItemPrep, MarkItemReady,
      Queries/                         -- GetKitchenQueue, GetTicketById, ...
    KitchenStations/
      Commands/
      Queries/
    Extensions/                        -- .Adapt<>() mappings
    DependencyInjection.cs             -- AddApplication(...)
  Infrastructure/
    Data/
      Configurations/                  -- EF entity type configurations
      Migrations/                      -- dotnet ef migrations
      Interceptors/                    -- DispatchDomainEventsInterceptor (mirror Ordering)
    Repositories/                      -- KitchenTicketRepository, KitchenStationRepository
    Consumers/                         -- hosted-service-style MassTransit glue (if needed)
    DependencyInjection/
      Application.cs
      Infrastructure.cs
      Persistence.cs
      Messaging.cs
      SignalR.cs
  Hubs/
    KitchenHub.cs                      -- /hubs/kitchen, JoinRestaurantGroup, JoinStationGroup
    IKitchenHubClient.cs               -- client-side method shape
  Endpoints/
    GetKitchenQueue.cs                 -- ICarterModule
    AcceptOrder.cs
    StartItemPrep.cs
    MarkItemReady.cs
    MarkOrderReady.cs
    BumpOrder.cs
    RecallOrder.cs                     -- undo a bump, push the ticket back
    GetTicketDetail.cs
  Common/
    PaginationRequest.cs               -- if not lifted to BuildingBlocks.Pagination
  Program.cs
  Kitchen.API.csproj
  Dockerfile                           -- added once dotnet new passes
  KITCHEN_SERVICE_PLAN.md              -- this file
```

(Folder skeleton exists on disk but contains no `.cs` yet. Each `Application/...` and `Infrastructure/...` subfolder will be populated by implementation phases.)

## 7. Domain model

### 7.1 `KitchenTicket : Aggregate<KitchenTicketId>`

One ticket per incoming `Order`. Created when Kitchen consumes `OrderCreatedIntegrationEvent`. Identifier reused: `KitchenTicketId == OrderId` — there is exactly one ticket per order, no separate sequence, no separate `TicketNumber`.

Properties:

```
Guid RestaurantId
KitchenTicketStatus Status               -- New | InProgress | Ready | Bumped | Cancelled
Instant ReceivedAt                       -- when the consumer built the ticket
Instant? StartedAt                       -- when an item first entered Preparing
Instant? ReadyAt                         -- when Status went Ready
Instant? BumpedAt                        -- when expo acknowledged
string? Notes                            -- free text from Ordering
IReadOnlyList<KitchenTicketItem> Items
Guid? ConfirmedByUserId                  -- who accepted (KitchenManager/KitchenStaff)
string? CancellationReason
```

`KitchenTicketStatus` transitions:

```
New         --accept-->  InProgress
InProgress  --item-all-ready-->  Ready
Ready       --bump-->     Bumped
Bumped      --recall-->   Ready    (rare; chef pulled ticket back)
Any         --cancel-->   Cancelled
```

Methods:

```csharp
static KitchenTicket CreateFromOrder(OrderCreatedIntegrationEvent);
void Accept(Guid staffUserId);
void StartItemPrep(KitchenItemId itemId, Instant now);
void MarkItemReady(KitchenItemId itemId, Instant now);
void MarkReady(Instant now);                      // aggregate-level Ready
void Bump(Instant now);
void Recall(Instant now);
void Cancel(string reason, Guid userId);
```

Each mutation:

- Guards the legal transition, throws `DomainException`-derived exception if invalid.
- Stamps `*At` timestamps.
- Returns `void`; raises a domain event.

### 7.2 `KitchenTicketItem : Entity<KitchenItemId>`

Mirrors `OrderItem` projection. Properties: `OrderItemId`, `MenuItemId`, `MenuItemName`, `Quantity`, `UnitPrice`, `IReadOnlyList<string> SelectedVariations`, `…Customizations`, `SpecialInstructions?`, `SeatNumber?`, `KitchenItemStatus Status` (Pending | Preparing | Ready), `Instant? StartedAt`, `Instant? ReadyAt`, `Guid? StationId`.

### 7.3 `KitchenStation`

Id, name, sort order, `IsActive`. Lightweight catalog used for routing tickets to a station group on the hub.

Seeded per restaurant via an admin endpoint or in `InitialData` mirror (mirror the `Ordering.Infrastructure/Data/Extensions/InitialData.cs` pattern).

### 7.4 Value objects

`KitchenTicketId`, `KitchenItemId`, `StationId` (typed `Guid` wrappers following `Ordering.Domain/ValueObjects/OrderId.cs`).

## 8. Persistence

### 8.1 Schema (3 tables)

| Table | Notes |
|---|---|
| `kitchen_tickets` | 1:1 with `orders.id`. PK `id` (matches the Order id). Includes `restaurant_id`, `status`, `confirmed_by_user_id`, `received_at`, `started_at`, `ready_at`, `bumped_at`, `cancellation_reason`. |
| `kitchen_ticket_items` | Child rows. FK to `kitchen_tickets.id`. Per-item prep state. |
| `kitchen_stations` | Catalog. FK-free, scoped per restaurant. |

### 8.2 Configuration

EF Core `IEntityTypeConfiguration<>` per aggregate, mirroring `Ordering.Infrastructure/Data/Configurations/`. Snapshot of columns owned by `Order` (timestamps like `StartedAt`, `ReadyAt`, `BumpedAt`) live in **Kitchen**, not Ordering. The two services each store their own copy of the timeline; Ordering's columns (`PreparingStartedAt`, `ReadyAt` on `Order`) are the legal record, Kitchen's columns are the projection.

### 8.3 Migrations

`dotnet ef migrations add InitialCreate --project Services/Kitchen/Kitchen.Infrastructure --startup-project Services/Kitchen/Kitchen.API`, then committed alongside the initial feature PR.

### 8.4 Migrations runner

Mirror `Ordering.Infrastructure/DependencyInjection.cs` — call `MigrateWithRetryAsync` (added in commit `2c4bd22`) on startup. Same resilience treatment.

## 9. API surface

All routes live under `/api/v1`. Authorization uses `BuildingBlocks.Authorization.RequirePermission(...)`.

### 9.1 REST endpoints (Carter `ICarterModule`)

| Method | Route | Permission | Notes |
|---|---|---|---|
| GET  | `/api/v1/kitchen/queue` | `kitchen:view_orders` | Paginated, filterable by `restaurantId`, `stationId`, `status`. Default status filter: `New\|InProgress`. |
| GET  | `/api/v1/kitchen/tickets/{id}` | `kitchen:view_orders` | Single ticket with items. |
| POST | `/api/v1/kitchen/tickets/{id}/accept` | `kitchen:update_prep_status` | `New → InProgress`. Records `confirmed_by_user_id`. |
| POST | `/api/v1/kitchen/tickets/{id}/items/{itemId}/start` | `kitchen:update_prep_status` | Per-item start. |
| POST | `/api/v1/kitchen/tickets/{id}/items/{itemId}/ready` | `kitchen:update_prep_status` | Per-item ready. |
| POST | `/api/v1/kitchen/tickets/{id}/mark-ready` | `kitchen:update_prep_status` | Aggregate Ready; **only allowed when all items are Ready**. |
| POST | `/api/v1/kitchen/tickets/{id}/bump` | `kitchen:update_prep_status` | Move to Bumped (expo confirmed). |
| POST | `/api/v1/kitchen/tickets/{id}/recall` | `kitchen:update_prep_status` | `Bumped → Ready`. |
| POST | `/api/v1/kitchen/tickets/{id}/cancel` | `kitchen:update_prep_status` | Body: `{ "reason": "..." }`. |

The existing Ordering endpoints (`POST /orders`, `GET /orders/{id}`) stay unchanged — they are admin/customer concerns, not kitchen concerns.

### 9.2 SignalR hub (`/hubs/kitchen`)

`Hubs/KitchenHub.cs`:

- `[Authorize]` at class level.
- `JoinRestaurantGroup(Guid restaurantId)` / `JoinStationGroup(Guid stationId)` → `Groups.AddToGroupAsync(Context.ConnectionId, "restaurant:{id}", ...)`. The JWT's `restaurantIds` claim (already populated by Identity for `UserRestaurant`) is read on connect to auto-add the user to their restaurants; this is `OnConnectedAsync` mirroring.
- Server-side methods: `Acknowledge(...)` for typing-indicator style — optional.
- `IHubContext<KitchenHub>` is injected into the integration handlers to broadcast.

`IKitchenHubClient` (typed contract for the client):

```csharp
Task OrderReceived(KitchenTicketDto ticket);
Task ItemStateChanged(KitchenTicketId ticketId, KitchenItemId itemId, KitchenItemStatus newStatus);
Task OrderReady(KitchenTicketId ticketId, Instant readyAt);
Task OrderBumped(KitchenTicketId ticketId, Instant bumpedAt);
Task OrderCancelled(KitchenTicketId ticketId, string reason);
```

Negotiation / auth: the YARP pass-through retains the same bearer; SignalR `MapHub` is wired with `AddSignalR().AddJsonProtocol(...)`. The client passes `?access_token=...` on the negotiate request, then the hub authorises on the standard JWT.

## 10. Integration events

### 10.1 Contracts in `BuildingBlocks.Messaging/Events/`

All new contracts derive from `IntegrationEvent`. Naming convention: `*IntegrationEvent`.

| Contract | Producer | Consumer | Payload |
|---|---|---|---|
| `OrderCreatedIntegrationEvent` | **Ordering** (Phases 1 + 2 of Ordering plan) | **Kitchen** (this plan) | From the Ordering plan's Phase 1. |
| `OrderPrepStateChangedIntegrationEvent` | Kitchen | Ordering | `{ OrderId, PreviousItemStatus, NewItemStatus, ItemId, StaffUserId, OccurredAt }` |
| `OrderItemReadyIntegrationEvent` | Kitchen | Ordering | `{ OrderId, ItemId, ReadyAt }` |
| `KitchenOrderReadyIntegrationEvent` | Kitchen | Ordering | `{ OrderId, ReadyAt }` (whole-order Ready) |
| `KitchenOrderBumpedIntegrationEvent` | Kitchen | Ordering | `{ OrderId, BumpedAt }` (expo signoff) |
| `KitchenOrderCancelledIntegrationEvent` | Kitchen | Ordering | `{ OrderId, Reason, CancelledByUserId, CancelledAt }` |

If the Ordering plan adopts the simpler "single Status event per aggregate transition" model (recommended), collapse `OrderPrepStateChangedIntegrationEvent` and `OrderItemReadyIntegrationEvent` into one — discuss when implementing.

### 10.2 Consumers in this service

- `Application/EventHandlers/Integration/OrderCreatedIntegrationEventHandler.cs` — `IConsumer<OrderCreatedIntegrationEvent>` → builds `KitchenTicket`, persists, broadcasts `OrderReceived` on `restaurant:{restaurantId}`.

### 10.3 Publishers in this service

- `Application/KitchenTickets/Commands/...` handlers call `IPublishEndpoint.Publish(...)` after `SaveChangesAsync`, after raising the domain event. (Same transactional caveat as Ordering — no outbox in Phase 1.)

## 11. Authorization

Permissions (`Identity.API/Data/DataSeeder.cs`, already in place):

- `kitchen:view_orders` — for GET endpoints and hub connect.
- `kitchen:update_prep_status` — for state-mutating endpoints.

Roles (already seeded): `KitchenManager` (gets both), `KitchenStaff` (gets both).

`BuildingBlocks.Authorization.RequirePermission("kitchen:update_prep_status")` is applied on every Carter endpoint via `.RequireAuthorization(...)`.

The hub checks the same claims at `OnConnectedAsync`. Users without `kitchen:view_orders` are rejected with `403` on connect.

## 12. Cross-cutting

### 12.1 Logging

`ILogger<T>` per handler; one structured log line per state transition with `{ OrderId, ItemId, StaffUserId, From, To }`.

### 12.2 Validation

`FluentValidation` for command DTOs, mirrors `Ordering.Application/Orders/Commands/CreateOrder/CreateOrderCommandValidator.cs` style.

### 12.3 Errors

Reuse `BuildingBlocks/Exceptions/NotFoundException`, `BadRequestException`, and the existing `CustomExceptionHandler`. Add `InvalidKitchenStateTransitionException` → 409.

### 12.4 Feature management

Register `Microsoft.FeatureManagement` so Kitchen can participate in shared feature flags if/when needed. Today no flag is consumed here.

### 12.5 Health

`AddHealthChecks().AddDbContextCheck<KitchenDbContext>().AddRabbitMQ(...)` — mirror Ordering's SQL Server check and Basket's RabbitMQ check.

### 12.6 Observability

Inherit the existing logging config; no new exporter introduced in Phase 1.

## 13. Operational

### 13.1 docker-compose additions

`docker-compose.yml` — no top-level block changes.

`docker-compose.override.yml` — add:

```yaml
kitchendb:
  container_name: kitchendb
  environment:
    - POSTGRES_USER=${POSTGRES_USER:-postgres}
    - POSTGRES_PASSWORD=${POSTGRES_PASSWORD:-postgres}
    - POSTGRES_DB=Kitchendb
    - PGDATA=/var/lib/postgresql/data/pgdata
  restart: unless-stopped
  ports:
    - "5436:5432"
  volumes:
    - postgres_kitchen:/var/lib/postgresql/data

kitchen.api:
  container_name: kitchen.api
  environment:
    - ASPNETCORE_ENVIRONMENT=Development
    - ASPNETCORE_HTTP_PORTS=8080
    - ASPNETCORE_HTTPS_PORTS=8081
    - ASPNETCORE_Kestrel__Certificates__Default__Password=${ASPNETCORE_Kestrel__Certificates__Default__Password:-password123}
    - ASPNETCORE_Kestrel__Certificates__Default__Path=/home/app/.aspnet/https/aspnetapp.pfx
    - ConnectionStrings__KitchenDB=Server=kitchendb;Port=5432;Database=Kitchendb;Username=${POSTGRES_USER:-postgres};Password=${POSTGRES_PASSWORD:-postgres};IncludeErrorDetail=true
    - MessageBroker__Host=amqp://messagebroker:5672
    - MessageBroker__UserName=${RABBITMQ_DEFAULT_USER:-guest}
    - MessageBroker__Password=${RABBITMQ_DEFAULT_PASS:-guest}
    - IdentityServiceUrl=http://identity.api:8080
  ports:
    - "6005:8080"
    - "6065:8081"
  depends_on:
    - kitchendb
    - identity.api
    - messagebroker
  volumes:
    - ${XDG_DATA_HOME:-${APPDATA:-~/.local/share}}/ASP.NET/Https:/home/app/.aspnet/https:ro
      - ${XDG_DATA_HOME:-${APPDATA:-~/.local/share}}/ASP.NET/Https:/root/.aspnet/https:ro
```

And `volumes:` at the bottom of the file add `postgres_kitchen:`.

### 13.2 YARP route

`ApiGateway/YarpApiGateway/appsettings.json` — add a sixth route + cluster entry:

```json
"kitchen-route": {
  "ClusterId": "kitchen-cluster",
  "RateLimiterPolicy": "fixed",
  "Match": { "Path": "/kitchen-api/{**catch-all}" },
  "Transforms": [ { "PathRemovePrefix": "/kitchen-api" } ]
}
```

…and the matching `kitchen-cluster` block pointing to `http://kitchen.api:8080`.

WebSocket pass-through is YARP's default behaviour for upstream services that host hubs — no further config required.

### 13.3 Health check

`/api/v1/health` exposed via `AddHealthChecks()` (mirrors Ordering).

### 13.4 Per-service local config

`Properties/launchSettings.json` for `http://localhost:6005` (HTTPS `6065`) — same shape as Catalog/Basket. User secrets id generated separately if needed.

## 14. Testing strategy

| Layer | Test type | What to assert |
|---|---|---|
| `Domain/Aggregates/KitchenTicket/` | unit (xUnit + NSubstitute, mirroring `Ordering.Domain.Tests`) | All legal transitions, all illegal ones throw, items' statuses propagate to parent. |
| `Application/KitchenTickets/Commands/*` | unit with `InMemoryDatabase` for the repository | Side effects: domain event raised, integration event published. |
| `Application/EventHandlers/Integration/OrderCreatedIntegrationEventHandler` | unit with NSubstitute `IPublishEndpoint` | Ticket persisted, broadcast invoked. |
| `Endpoints/*` | `WebApplicationFactory` integration test (planned for `Kitchen.API.Tests`) | 2xx on legal; 409 on illegal; 401/403 on missing/invalid bearer. |
| `Hubs/KitchenHub` | integration via test host | `OnConnectedAsync` adds user to right groups; `BroadcastOrderReceived` reaches mock client subscribed to the group. |
| Migration smoke | `dotnet ef database update` against ephemeral test Postgres | Schema matches plan. |

Test target: parity with `Ordering.Domain.Tests` (12 files) on domain; parity with `Identity.API.Tests` (1 file today, write a real one for Kitchen) on API.

## 15. Milestones

| Phase | Deliverable | Depends on |
|---|---|---|
| **M0 — Contracts landed** | `OrderCreatedIntegrationEvent` and the kitchen-emitted events exist in `BuildingBlocks.Messaging`. Ordering no longer publishes `OrderDto`. | Ordering plan Phase 1 |
| **M1 — Skeleton compiles** | `Kitchen.API` project, all DI wired, health endpoint live, **no domain logic yet**. Consumer exists but no-ops on the inbound event. | M0 |
| **M2 — Domain core** | `KitchenTicket` aggregate + tests, `EF Core` schema initial migration, basic GET endpoints. | M1 |
| **M3 — Commands** | Accept / StartItem / MarkItemReady / MarkOrderReady / Bump / Recall / Cancel endpoints. Each publishes its outbound event. Ordering consumes them (the Ordering Phase 4 work). | M2 + Ordering Phase 2 |
| **M4 — SignalR** | `KitchenHub` live, broadcasts wired on every command + on `OrderCreated`. YARP route verified. Smoke test through the gateway to confirm WebSocket upgrade. | M3 |
| **M5 — Hardening** | Health checks, structured logging, integration tests, docker-compose + YARP config committed. | M4 |
| **M6 — Outbox (optional, recommended)** | Transactional outbox for both Ordering and Kitchen; idempotent consumers on both sides. | M5 |

M0 is the gate — do not start M1 until Ordering's card-data leak is closed.

## 16. Open questions / decisions

1. **Single event vs granular events for prep.** Do we publish one `OrderPrepStateChangedIntegrationEvent` per per-item state change, or one aggregate event per `MarkReady` call? Recommended: granular (per-item) because it lets Ordering's aggregate method drive per-item state without a re-fetch. Discuss during M2.
2. **Should `Order` accept the inbound event and *also* be reachable via the new REST endpoints in the Ordering plan?** Yes — both write-paths go through the aggregate, which is idempotent on illegal transitions. Document the rule explicitly in the Ordering plan's open questions.
3. **Recall semantics.** Is `Recall` going from Bumped back to Ready (the ticket was bumped by mistake), or going from any state back to "active" (rare cancellation-rescind)? Plan covers the first; the second can be added later if needed.
4. **KitchenUI's auth claim shape.** Confirm with Identity which claim holds the kitchen user's restaurant memberships (`UserRestaurant.Claims` today). If the claim shape changes, this plan's `OnConnectedAsync` needs to follow.
5. **Recall permission.** Currently `kitchen:update_prep_status`. Should `Recall` require `kitchen:view_orders` only? Recommend keeping `kitchen:update_prep_status` for safety.
6. **Outbox adoption.** Defer (M6) but plan for it — both Ordering and Kitchen share the same gap and a shared mass-transit-friendly outbox would be the right place to land it.

## 17. Acceptance criteria (overall)

- [ ] M0: `OrderCreatedIntegrationEvent` exists in `BuildingBlocks.Messaging`; Ordering publishes only that contract.
- [ ] M1: docker-compose stacks start with `kitchen.api` healthy and no console errors.
- [ ] M2: every transition method on `KitchenTicket` has both a positive and a negative test; migration applied to a fresh DB matches `8.1`.
- [ ] M3: every command endpoint published its integration event; Ordering reflects the new state in its aggregate within one mass-transit round-trip.
- [ ] M4: a non-Kitchen client cannot open `/hubs/kitchen`; an authenticated `KitchenStaff` can subscribe to `restaurant:{id}` and receives `OrderReceived`/`ItemStateChanged`/`OrderReady`/`OrderBumped`/`OrderCancelled` events.
- [ ] M5: `dotnet test` passes locally with the full pipeline; docker-compose stack fully boots with `Kitchen.API` on `:6005`; YARP fronts `/kitchen-api/` and forwards WS upgrades.
- [ ] Docs updated: `docs/architecture/current-architecture.md` lists Kitchen as the 6th service; `db_relational_model.md` and the mermaid have no schema drift.
