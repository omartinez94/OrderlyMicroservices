# Ordering — POS Order Creation & Contract Refinement Implementation Plan

> Scope: Refine the `Ordering.API` order placement pipeline for POS workflows (Dine-In tables/seats, counter Takeout walk-ins, course sequencing), harden it (tenant, server-side pricing/availability, idempotency), and propagate course data through the transactional outbox to Kitchen and its SignalR hub.

---

## Status

> **Plan version**: `v1.1` (2026-10-31)
> **Current state**: ✅ Completed (Phases 1-5 complete)
> **Depends on**: `menu_management_and_storage.md` Phase 1 (`DefaultCourseNumber`, `GET menu` for server-side pricing/availability).
> **Required by**: `order_creation_pos.md` (Web).

| Phase | Name | Status |
|:-----:|---|:-----:|
| 1 | POS Contract & Validation Refinement | ✅ Done |
| 2 | Table & Seat Association Enforcement | ✅ Done |
| 3 | Server-Side Tenant, Price, Availability & Idempotency | ✅ Done |
| 4 | Kitchen Persistence & SignalR Payload (CourseNumber) | ✅ Done |
| 5 | Integration Tests & Phase Guard Validation | ✅ Done |

> **Legend**: ✅ Done · 🚧 In progress · ⏸ Pending · 🔒 Blocked

> **Commit messages**: Conventional Commits (`feat:`, `fix:`, `test:`, `docs:`). Short subject, ≤50 chars, imperative mood, no trailing period. Never include AI attribution or Co-Authored-By.

> **Update rule**: on every phase completion, update this document and commit in the same phase commit.

---

## 0. Skill & Documentation Conventions

### 0.1 Skill Mandate — `add-ordering-feature` & `csharp-developer`
> **All implementation work MUST follow Clean Architecture (Domain → Application → Infrastructure → API), Carter Minimal APIs, FluentValidation pipeline, and strict NodaTime date handling.**

### 0.2 Code-Quality Guard Rails
- **Quality Gate** (run from `OrderlyMicroservices/`): `pwsh ./scripts/phase-guard.ps1 -PhaseName "<Phase Name>" -Quick`
- **NodaTime mandatory**: `Instant` / `LocalDate`, never `DateTime`/`DateTimeOffset`.
- **Relational engine**: SQL Server (`orderdb`) for Ordering; Kitchen is PostgreSQL. Migrations target the owning service's engine.
- **Outbox**: `OrderCreatedIntegrationEvent` is published via `IOutboxPublisher` in the EF transaction.
- **Contract changes are additive**: new event members are nullable.

---

## 1. Context (verified against code, 2026-10-31)

`POST /v1/orders` takes `OrderDto`. `OrderDtoValidator` requires `CustomerId`, `BillingAddress`, `DeliveryAddress`, `Payment`, `Subtotal > 0`, `TotalAmount > 0`. POS reality differs:

1. Dine-In/Takeout guests have no delivery address; walk-ins have no registered customer.
2. Dine-In orders are placed before payment.
3. Kitchen benefits from grouping by `SeatNumber` (bill splitting) and `CourseNumber` (sequencing).

Already present: `OrderItemDto.SeatNumber` (non-null `int`), `OrderDto.TableId` (`Guid?`), Kitchen carries `SeatNumber` end-to-end (`OrderCreatedIntegrationEvent` item seed, `KitchenTicketItem`, DTOs). **`CourseNumber` exists nowhere.**

Verified naming: `OrderType` is `DineIn, Takeout, Delivery` (there is **no `Takeaway`**). `PaymentStatus` is `Pending, Paid, Void`; payment *method* is a separate enum in `BuildingBlocks.Messaging/Events/PaymentMethod.cs` (its values must be listed in Phase 1 task 1.0; there is no "Unpaid" method — deferred payment is a *status*).

Security gaps in the current pipeline (pre-existing, now material for POS): the browser supplies `Subtotal/TaxAmount/TotalAmount`, item prices and `RestaurantId`; nothing checks tenant, table ownership, item availability, or duplicate submissions.

---

## 2. Goal

1. Adapt validation for POS: addresses optional unless `Delivery`; payment optional/deferred for non-Delivery; walk-in customers supported.
2. Add `CourseNumber` (`int?`, 1–9; `null` = unassigned) to `OrderItemDto` / `OrderItem` / event seed / Kitchen.
3. Validate `TableId` and `SeatNumber` when `OrderType == DineIn`.
4. Make `CreateOrder` safe for POS: tenant check, server-authoritative pricing, availability check, idempotency.
5. Propagate `CourseNumber` to Kitchen persistence, broadcaster payload and KDS.

---

## 3. Out of Scope

- Split payments (dedicated split-bill endpoint); EMV terminal integration.
- Table floor-plan editor.
- **Marking the table as occupied** after order creation (stays a manual/Catalog concern in v1; tracked as follow-up).
- Course *firing/hold* logic in Kitchen (this plan only carries and groups the course data).

---

## 4. Tech Decisions

| Decision | Choice | Reason |
| :--- | :--- | :--- |
| Address handling | `BillingAddress`/`DeliveryAddress` nullable unless `OrderType == Delivery` (`DeliveryAddress` required; `Billing` follows). No fabricated placeholder addresses. If EF owned-type nullability proves costly, fall back to a documented `Address.None` sentinel (spike in task 1.2) | Avoids storing fake billing data. |
| Walk-in customer | Non-Delivery orders may send `CustomerId = Guid.Empty`; the handler substitutes a deterministic per-restaurant walk-in ID (`WalkInCustomer.IdFor(restaurantId)`) | No schema change; stable reporting. |
| Deferred payment | For non-Delivery, `Payment` may be omitted; the handler creates a payment record with `PaymentStatus.Pending` and no method. A supplied `Cash` method is allowed | Fixes the method-vs-status confusion. |
| Course | `int? CourseNumber` 1–9, meaning defined per restaurant; **labels live in the frontend** (default 1 Starters, 2 Mains, 3 Desserts; drinks are not forced to a late course) | Hard-coded "4 = Drinks" is operationally wrong. |
| Event contract | `OrderItemSeed.CourseNumber` is `int?` | Old in-flight messages and old consumers stay valid. |
| Server authority | Handler recomputes subtotal/tax/total from Catalog prices (via `ICatalogPricingClient`, HTTP + Polly, forwards caller token) and overwrites client amounts; unknown/foreign items → `422` | Closes client-tampering gap. See §10 if deferred. |
| Idempotency | Header `Idempotency-Key` (GUID) stored as a unique index `(RestaurantId, IdempotencyKey)` on `Order`; a replay returns the original `201` body | Prevents double-tap duplicates. |
| Kitchen DB | PostgreSQL migration adds `CourseNumber` to `KitchenTicketItem` | Required for persistence. |

---

## 5. Folder Layout

```
OrderlyMicroservices/orderly-microservices/
├── BuildingBlocks.Messaging/Events/OrderCreatedIntegrationEvent.cs   # OrderItemSeed + int? CourseNumber
├── Services/Ordering/
│   ├── Ordering.Domain/Models/{Order.cs,OrderItem.cs}                # CourseNumber, IdempotencyKey
│   ├── Ordering.Application/
│   │   ├── Dtos/{OrderDto.cs,OrderItemDto.cs}                        # + CourseNumber
│   │   ├── Dtos/Validators/{OrderDtoValidator.cs,OrderItemDtoValidator.cs,PaymentDtoValidator.cs}
│   │   ├── Common/Interfaces/ICatalogPricingClient.cs
│   │   └── Orders/Commands/CreateOrder/CreateOrderHandler.cs
│   ├── Ordering.Infrastructure/
│   │   ├── Data/Configurations/{OrderConfiguration.cs,OrderItemConfiguration.cs}   # + migration (SQL Server)
│   │   └── Services/CatalogPricingClient.cs
│   └── Ordering.API/Endpoints/CreateOrder.cs                         # Idempotency-Key, tenant check
└── Services/Kitchen/Kitchen.API/
    ├── Domain/Aggregates/KitchenTicket/{KitchenTicket.cs,KitchenTicketItem.cs}
    ├── Domain/Events/OrderItemSeed.cs                                # confirm single source of truth
    ├── Application/Dtos/KitchenTicketDto.cs + Extensions
    └── Infrastructure/Data/Migrations/                               # + CourseNumber (PostgreSQL)
```

---

## 6. Implementation Phases

### Phase 1: POS Contract & Validation Refinement
- [x] **Task 1.0**: Inventory `PaymentDto`, `PaymentDtoValidator`, `PaymentMethod` values, `Address` mapping, and every existing validator constraint that blocks POS (`Subtotal > 0`, `TotalAmount > 0` for comped items, etc.); document decisions in this plan.
  - *Payment Inventory*: `PaymentDto(PaymentMethod Method, string Brand, string LastFour)` carries redacted payment summary. `PaymentDtoValidator` requires defined enum != Unspecified, non-empty brand, exactly 4 digits. `PaymentMethod` enum has `Unspecified = 0`, `Card = 1`, `Cash = 2`, `Wallet = 3` (no "Unpaid" method exists; deferred payment is represented by omitting payment or pending status).
  - *Address Inventory & Spike*: `Order` complex properties `BillingAddress` and `DeliveryAddress` map to non-null columns in SQL Server. Implemented `Address.None` sentinel (`""`, `""`, `""`, `"00000"`, `""`) and `Payment.Pending` (`Unspecified`, `"Pending"`, `"0000"`) so non-Delivery POS orders do not store fake PII, require no database column alterations, and project to `null` in wire DTOs and integration events.
  - *POS Blocking Constraints Removed*: `OrderDtoValidator` relaxes `CustomerId` required only for Delivery (`Guid.Empty` allowed for Dine-In/Takeout); `Subtotal >= 0` and `TotalAmount >= 0` allow comped/free items; `BillingAddress`, `DeliveryAddress`, and `Payment` required only for Delivery; `OrderItemDtoValidator` relaxes `UnitPrice >= 0` and `TotalPrice >= 0` for comped items and validates `CourseNumber in 1..9` when present.
- [x] **Task 1.1**: Add `CourseNumber` (`int?`) to `OrderItemDto`, `OrderItem`, `OrderItemConfiguration` (+ SQL Server migration) and the event `OrderItemSeed`. Confirm `OrderItemSeed` has a single definition (the event file vs `Kitchen.API/Domain/Events/OrderItemSeed.cs`) and remove the duplicate.
  - `CourseNumber` added to `OrderItemDto`, `OrderItem` domain model, `Order.Add`, `OrderItemConfiguration`, and migration `20261105001551_AddCourseNumberToOrderItem` applied to model snapshot.
  - `KitchenOrderItemPreview` on `OrderCreatedIntegrationEvent` and `OrderItemSeed` in `Kitchen.API` updated with `int? CourseNumber = null`. Confirmed `OrderItemSeed` definition is single (event uses `KitchenOrderItemPreview`; kitchen internal carrier is `OrderItemSeed`).
- [x] **Task 1.2**: `OrderDtoValidator`: addresses required only for `Delivery`; `Payment` optional for non-Delivery; `CourseNumber` in `1..9` when present; `CustomerId` may be `Guid.Empty` for non-Delivery. Spike nullable address persistence (nullable owned types vs `Address.None`).
- [x] **Task 1.3**: `CreateOrderHandler`: map `CourseNumber`, substitute walk-in customer, create pending payment when `Payment` is absent.
  - Implemented `WalkInCustomer.IdFor(restaurantId)` to deterministically resolve customer ID for walk-in orders and ensure customer entity exists in `dbContext.Customers`.
  - Maps `CourseNumber` to domain entity and `OrderCreatedIntegrationEvent`.
  - Substitutes `Payment.Pending` and `Address.None` when omitted for non-Delivery.
- [x] **Verification**: `dotnet test Services/Ordering/Ordering.Application.Tests`. All 135 unit tests pass.

### Phase 2: Table & Seat Association Enforcement
- [x] **Task 2.1**: Require `TableId` when `OrderType == DineIn`; forbid it for `Delivery`.
  - In `OrderDtoValidator`, added rules enforcing `TableId` is not null and not empty (`NotEmpty().NotEqual(Guid.Empty)`) when `OrderType == OrderType.DineIn`.
  - Added rule enforcing `TableId` is null (`Null()`) when `OrderType == OrderType.Delivery`.
  - For `Takeout`, `TableId` remains optional or null (counter takeout walk-ins).
- [x] **Task 2.2**: `SeatNumber >= 0` (0 = shared table, 1..N = seat).
  - In `OrderItemDtoValidator`, added rule enforcing `SeatNumber >= 0` (`GreaterThanOrEqualTo(0)`).
- [x] **Task 2.3**: Table ownership/capacity checks live in Phase 3 (they need Catalog); here only structural validation.
- [x] **Verification**: unit tests for valid/invalid Dine-In configurations (`OrderDtoValidatorTests` and `OrderItemDtoValidatorTests`). All 145 unit tests pass in `Ordering.Application.Tests`.

### Phase 3: Server-Side Tenant, Price, Availability & Idempotency
- [x] **Task 3.1**: Endpoint/handler verify `command.RestaurantId ∈ caller's JWT restaurant claims` (`403` otherwise via `ForbiddenException`).
  - In `CreateOrder.cs`, added verification that the caller has a matching `restaurantId` or `restaurantIds` claim, with bypass for `SuperAdmin` role. Throws `ForbiddenException` (yielding RFC 7807 403 ProblemDetails) if unauthorized.
- [x] **Task 3.2**: `ICatalogPricingClient` fetches the menu snapshot (`GET /v1/restaurants/{id}/menu`, short distributed cache) and the table (`GET …/tables/{id}`): reject items that are unknown/foreign (`422` via `MenuItemNotFoundUnprocessableException`), **unavailable** (`409` via `MenuItemUnavailableConflictException` with `unavailableItemIds` in `ProblemDetails`), table not in restaurant (`422` via `TableNotFoundUnprocessableException`), `SeatNumber` beyond the table's seats (`422` via `SeatNumberExceedsCapacityUnprocessableException`). On Catalog outage fail closed with `503` (`CatalogUnavailableException` with `Retry-After` header).
  - Defined `ICatalogPricingClient` interface with `GetMenuSnapshotAsync` and `GetTableAsync`.
  - Implemented `CatalogPricingClient` with `HttpClient`, resilience handler, distributed cache, authorization header forwarding, and fail-closed 503 behavior.
  - Enhanced `CustomExceptionHandler` with arms and problem details extensions for conflict (409), unprocessable entity (422), and service unavailable (503 + Retry-After).
- [x] **Task 3.3**: Overwrite client-supplied unit prices, subtotal, tax and total with server-computed values.
  - In `CreateOrderHandler`, recomputes authoritative prices from Catalog base prices + selected variation price modifiers. Overwrites client-supplied item unit/total prices, recomputes order `Subtotal`, `TaxAmount`, and `TotalAmount` on the server.
- [x] **Task 3.4**: `Idempotency-Key` handling with the unique index + replay of the original response.
  - Added `IdempotencyKey` (`Guid?`) to `Order` aggregate.
  - Configured unique index on `(RestaurantId, IdempotencyKey)` with filter `[IdempotencyKey] IS NOT NULL` in `OrderConfiguration`.
  - Generated and applied EF Core migration `20261105005721_AddIdempotencyKeyToOrder`.
  - In `CreateOrder.cs`, extracts `Idempotency-Key` header.
  - In `CreateOrderHandler`, checks for existing order by `(RestaurantId, IdempotencyKey)`; if duplicate is detected, short-circuits and replays the existing order (200 OK / original payload) without re-executing order creation or outbox staging.
- [x] **Verification**: Added comprehensive unit tests in `CreateOrderHandlerTests` covering unknown items (422), unavailable items (409), seat capacity limits (422), price tampering overwrite, and idempotency replay. All 149 unit tests pass in `Ordering.Application.Tests`, and `orderly-microservices.slnx` builds with 0 errors.

### Phase 4: Kitchen Persistence & SignalR Payload
- [x] **Task 4.1**: Event serializes `CourseNumber` (nullable) and `SeatNumber`.
  - Verified `OrderCreatedIntegrationEvent` and `KitchenOrderItemPreview` contain `CourseNumber` (nullable `int?`) and `SeatNumber`.
  - In `KitchenTicketExtensions.ToOrderItemSeeds`, mapped `CourseNumber` and `SeatNumber` into `OrderItemSeed`.
- [x] **Task 4.2**: Kitchen: add `CourseNumber` to `KitchenTicketItem`, `KitchenTicketConfiguration`, **PostgreSQL migration**, `KitchenTicketItemDto`/`KitchenTicketDto`, `KitchenTicket.CreateFromOrder`, `KitchenTicketExtensions`.
  - In `KitchenTicketItem.cs`: added `public int? CourseNumber { get; private set; }` and updated internal constructor.
  - In `KitchenTicket.CreateFromOrder`: passed `seed.CourseNumber` to `KitchenTicketItem` creation.
  - In `KitchenTicketConfiguration.cs`: configured `item.Property(i => i.CourseNumber).HasColumnName("CourseNumber")`.
  - Added PostgreSQL EF Core migration `20261105012043_AddCourseNumberToKitchenTicketItem` and updated `KitchenDbContextModelSnapshot.cs`.
  - In `KitchenTicketDto.cs`: added `int? CourseNumber = null` to `KitchenTicketItemDto`.
  - In `KitchenTicketExtensions.ToDto(KitchenTicketItem)`: mapped `CourseNumber: item.CourseNumber`.
- [x] **Task 4.3**: `KitchenHub` `OrderReceived` payload includes seat and course.
  - Verified `KitchenHub` client broadcast `OrderReceived(ticket.ToDto())` and `TicketUpdated(ticket.ToDto())` serialize `CourseNumber` on each ticket item to connected clients.
- [x] **Verification**:
  - Added unit test `CreateFromOrder_PreservesCourseNumberAndSeatNumber` in `KitchenTicketTests.cs`.
  - Added unit tests in `KitchenTicketExtensionsTests.cs` verifying `ToDto()` and `ToOrderItemSeeds()` preserve `CourseNumber`.
  - Added unit tests in `OrderCreatedIntegrationEventHandlerTests.cs` verifying ticket persistence and SignalR broadcast include `CourseNumber`.
  - All unit tests pass and `orderly-microservices.slnx` builds cleanly with 0 errors.

### Phase 5: Integration Tests & Phase Guard Validation
- [x] **Task 5.1**: `CreatePosOrderTests.cs` in `Ordering.API.Tests/Integration`: Dine-in with seats/courses/deferred payment → `201`; Takeout walk-in → `201`; foreign restaurant → `403`; unavailable item → `409`; tampered total overwritten; duplicate `Idempotency-Key` → same order; missing table on DineIn → `400`.
- [x] **Task 5.2**: `pwsh ./scripts/phase-guard.ps1 -PhaseName "POS Order Creation" -Quick`.

---

## 7. Security Guardrails

| Risk | Mitigation |
|---|---|
| Cross-tenant order creation | Tenant claim check (3.1). |
| Price/total tampering | Server recomputation (3.3). |
| Ordering out-of-stock items | Availability check (3.2). |
| Duplicate orders | Idempotency (3.4). |
| Fake PII | No placeholder addresses. |

---

## 8. Technical considerations

- `OrderStatus.Pending` is the initial POS state for Dine-In; payment settles at bill request.
- Ordering has no direct dependency on Catalog today; `ICatalogPricingClient` introduces one (Polly timeouts/circuit breaker, health reflected in `/health`).
- Coordinate event/DTO changes with the Web repo before merging Phase 4.

---

## 9. Open decisions

1. **Server-side pricing scope** (Phase 3). If deferred, record the tampering risk explicitly and keep tasks 3.1 and 3.4 (cheap) in this release.
2. Per-restaurant course labels beyond the three defaults (frontend-only in v1).

---

## Changelog

### v1.1 (2026-10-31) — review fixes
- Corrected enums (`Takeout`, deferred payment as `PaymentStatus.Pending`); verified existing `SeatNumber`/`TableId`.
- `CourseNumber` is nullable (additive contract); drinks no longer hard-coded to course 4.
- No fabricated addresses; deterministic walk-in customer; payment optional for non-Delivery.
- New Phase 3: tenant check, server-authoritative pricing, availability, table ownership, idempotency.
- Kitchen persistence (PostgreSQL migration, DTOs, frontend types) added; duplicate `OrderItemSeed` resolved.
- Removed undefined "order activity" task; added negative tests and working directory for phase guard.

### v1.0 (2026-10-31) — initial draft
- Created plan (4 phases).
