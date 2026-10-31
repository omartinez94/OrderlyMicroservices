# Ordering — POS Order Creation & Contract Refinement Implementation Plan

> Scope: Refine the `Ordering.API` order placement pipeline for POS workflows (Dine-In tables/seats, counter Takeout walk-ins, course sequencing), harden it (tenant, server-side pricing/availability, idempotency), and propagate course data through the transactional outbox to Kitchen and its SignalR hub.

---

## Status

> **Plan version**: `v1.1` (2026-10-31)
> **Current state**: ⏸ Not started
> **Depends on**: `menu_management_and_storage.md` Phase 1 (`DefaultCourseNumber`, `GET menu` for server-side pricing/availability).
> **Required by**: `order_creation_pos.md` (Web).

| Phase | Name | Status |
|:-----:|---|:-----:|
| 1 | POS Contract & Validation Refinement | ⏸ Pending |
| 2 | Table & Seat Association Enforcement | 🔒 Blocked |
| 3 | Server-Side Tenant, Price, Availability & Idempotency | 🔒 Blocked |
| 4 | Kitchen Persistence & SignalR Payload (CourseNumber) | 🔒 Blocked |
| 5 | Integration Tests & Phase Guard Validation | 🔒 Blocked |

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
- **Task 1.0**: Inventory `PaymentDto`, `PaymentDtoValidator`, `PaymentMethod` values, `Address` mapping, and every existing validator constraint that blocks POS (`Subtotal > 0`, `TotalAmount > 0` for comped items, etc.); document decisions in this plan.
- **Task 1.1**: Add `CourseNumber` (`int?`) to `OrderItemDto`, `OrderItem`, `OrderItemConfiguration` (+ SQL Server migration) and the event `OrderItemSeed`. Confirm `OrderItemSeed` has a single definition (the event file vs `Kitchen.API/Domain/Events/OrderItemSeed.cs`) and remove the duplicate.
- **Task 1.2**: `OrderDtoValidator`: addresses required only for `Delivery`; `Payment` optional for non-Delivery; `CourseNumber` in `1..9` when present; `CustomerId` may be `Guid.Empty` for non-Delivery. Spike nullable address persistence (nullable owned types vs `Address.None`).
- **Task 1.3**: `CreateOrderHandler`: map `CourseNumber`, substitute walk-in customer, create pending payment when `Payment` is absent.
- **Verification**: `dotnet test Services/Ordering/Ordering.Application.Tests`.

### Phase 2: Table & Seat Association Enforcement
- **Task 2.1**: Require `TableId` when `OrderType == DineIn`; forbid it for `Delivery`.
- **Task 2.2**: `SeatNumber >= 0` (0 = shared table, 1..N = seat).
- **Task 2.3**: Table ownership/capacity checks live in Phase 3 (they need Catalog); here only structural validation.
- **Verification**: unit tests for valid/invalid Dine-In configurations.

### Phase 3: Server-Side Tenant, Price, Availability & Idempotency
- **Task 3.1**: Endpoint/handler verify `command.RestaurantId ∈ caller's JWT restaurant claims` (`403` otherwise).
- **Task 3.2**: `ICatalogPricingClient` fetches the menu snapshot (`GET /v1/restaurants/{id}/menu`, short cache) and the table (`GET …/tables/{id}`): reject items that are unknown/foreign (`422`), **unavailable** (`409` with `unavailableItemIds[]` in `ProblemDetails`), table not in restaurant, `SeatNumber` beyond the table's seats. On Catalog outage fail closed with `503` + `Retry-After`.
- **Task 3.3**: Overwrite client-supplied unit prices, subtotal, tax and total with server-computed values.
- **Task 3.4**: `Idempotency-Key` handling with the unique index + replay of the original response.
- **Verification**: unit + integration tests (foreign restaurant, unavailable item, tampered totals, replayed key).

### Phase 4: Kitchen Persistence & SignalR Payload
- **Task 4.1**: Event serializes `CourseNumber` (nullable) and `SeatNumber`.
- **Task 4.2**: Kitchen: add `CourseNumber` to `KitchenTicketItem`, `KitchenTicketConfiguration`, **PostgreSQL migration**, `KitchenTicketItemDto`/`KitchenTicketDto`, `KitchenTicket.CreateFromOrder`, `KitchenTicketExtensions`.
- **Task 4.3**: `KitchenHub` `OrderReceived` payload includes seat and course; update the frontend Kitchen DTO types (coordinate with `order_creation_pos.md`).
- **Verification**: `dotnet test Services/Kitchen/Kitchen.API.Tests`.

### Phase 5: Integration Tests & Phase Guard Validation
- **Task 5.1**: `CreatePosOrderTests.cs` in `Ordering.API.Tests/Integration`: Dine-in with seats/courses/deferred payment → `201`; Takeout walk-in → `201`; foreign restaurant → `403`; unavailable item → `409`; tampered total overwritten; duplicate `Idempotency-Key` → same order; missing table on DineIn → `400`.
- **Task 5.2**: `pwsh ./scripts/phase-guard.ps1 -PhaseName "POS Order Creation" -Quick`.

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
