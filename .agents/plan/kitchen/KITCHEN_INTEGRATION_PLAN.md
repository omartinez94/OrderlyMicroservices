# Ordering.API — Kitchen Service Integration Plan

> Scope: every change inside the `Ordering.*` projects required to land the upcoming `Kitchen.API` microservice safely. Not a plan for Kitchen itself — see `Services/Kitchen/Kitchen.API/KITCHEN_SERVICE_PLAN.md`.

---

## 1. Context

The codebase already contains the scaffolding for a future fulfillment flow but no consumer:

- `Identity` seeds `kitchen:view_orders`, `kitchen:update_prep_status`, `KitchenManager`, `KitchenStaff` (`Identity.API/Data/DataSeeder.cs`, captured in `docs/architecture/current-architecture.md:121,127`).
- `Ordering.Domain/Models/Order.cs` carries prep time fields and lifecycle timestamps (`EstimatedPrepTimeMinutes`, `ActualPrepTimeMinutes`, `PreparingStartedAt`, `ReadyAt`).
- `OrderItem` carries per-item `PrepStatus`, `PrepStartedAt`, `PrepCompletedAt`.
- `docker-compose.override.yml:164` env `FeatureManagement__OrderFullfilment=true` is wired on `ordering.api`.
- `Ordering.Application/Orders/EventHandlers/Domain/OrderCreatedEventHandler.cs:19` publishes the *full* `OrderDto` (including `PaymentDto.CardNumber/Expiration/Ccv`) over RabbitMQ whenever the feature flag is on.
- `Ordering.API/Consumers/BasketCheckoutEventConsumer.cs` exists but is **not registered** — dead code.

This plan closes three concrete gaps before Kitchen is built:

1. The bus currently leaks cardholder data.
2. The `Order` aggregate has no legal state-transition methods — `Order.Update(...)` overwrites `Status` raw.
3. There are no inbound endpoints a kitchen UI/agent can call to mutate prep state safely.

A separate, smaller pass removes the dead duplicate consumer.

---

## 2. Goal

After this plan is complete, `Ordering.API` exposes a clean, permission-aware, event-safe surface for Kitchen to integrate with — without any PCI data crossing the message bus, with `Order.Status` mutated only through guarded domain methods, and with no leftover dead code confusing the registration scan.

## 3. Out of scope

- The `Kitchen.API` service itself (separate plan).
- Any frontend / UI work (separate project, per user).
- Identity changes — `kitchen:*` permissions are already seeded.
- Catalog changes — Kitchen reads menu items via the existing `MenuItem` reference object, same as `OrderItem` does today.
- Outbox / saga infrastructure (separate, optional future work — the feature flag and lack of outbox are accepted today).
- Architectural replatforming (no transition away from Clean Architecture / Carter).

## 4. Phases

### Phase 1 — Stop broadcasting cardholder data on the message bus

**Files touched:**

- New: `BuildingBlocks.Messaging/Events/OrderCreatedIntegrationEvent.cs`
- New: `BuildingBlocks.Messaging/Events/OrderItemPreviewDto.cs` (or inline inside the event)
- Modified: `Ordering.Application/Orders/EventHandlers/Domain/OrderCreatedEventHandler.cs` (publish the new event; do not reuse `OrderDto`).
- Modified (perhaps): `BuildingBlocks.Messaging/Events/IntegrationEvent.cs` — current base record has `Id`/`OccurredOn`/`EventType` as getter expressions (each read returns a fresh value per `current-architecture.md:345`). Fix that to be a constructor-set `record` here if `OrderCreatedIntegrationEvent` needs a stable identity; otherwise **fix it independently** — do not sneak the change inside the kitchen patch.

**New event shape (proposal):**

```
OrderCreatedIntegrationEvent
  Guid OrderId
  string OrderNumber
  Guid RestaurantId
  Guid? TableId
  OrderType OrderType                // DineIn | Takeout | Delivery
  Guid CustomerId
  decimal Subtotal, TotalAmount, TaxAmount, DiscountAmount
  string Currency, DiscountCode?
  AddressDto BillingAddress          // ships today over BasketCheckoutEvent already
  AddressDto DeliveryAddress?        // only when OrderType.Delivery
  IReadOnlyList<KitchenOrderItemPreview> Items
    record KitchenOrderItemPreview(
      Guid OrderItemId, Guid MenuItemId, string MenuItemName,
      int Quantity, decimal UnitPrice,
      IReadOnlyList<string> SelectedVariations,
      IReadOnlyList<string> Customizations,
      string? SpecialInstructions,
      int? SeatNumber)
  int EstimatedPrepTimeMinutes
  Instant OccurredOn

NO PaymentDto / NO CardNumber / NO Cvv / NO Expiration / NO CardName.
```

**Rules:**

- The event is published from `Ordering.Application` (where `IPublishEndpoint` already lives); Kitchen has no need to import `Ordering.Domain`.
- The payload type lives under `BuildingBlocks.Messaging` so the in-box `IConsumer<OrderCreatedIntegrationEvent>` in Kitchen does not pull a reference to `Ordering.Application`.
- Drop the `OrderFullfilment` feature flag from the publish path **only after** Kitchen is consuming the event reliably. Until then, the flag remains the kill switch.

**Acceptance:**

- No `OrderDto` reference leaves `Ordering` over RabbitMQ.
- Integration test asserts that the published message has zero property names overlapping with `PaymentDto`.
- `Ordering.Application` and `Ordering.Infrastructure` build without referencing `PaymentDto` from any consumer/extending assembly.

---

### Phase 2 — Add legal state transitions to `Order` aggregate

**Files touched:**

- Modified: `Ordering.Domain/Models/Order.cs` (add behaviour methods; no DbModel change).
- New domain events: `Ordering.Domain/Events/OrderConfirmedEvent.cs`, `OrderPreparingEvent.cs`, `OrderReadyEvent.cs`, `OrderCancelledEvent.cs` — each implements `IDomainEvent`.
- Modified (potentially): `Ordering.Domain/Exceptions/DomainException.cs` — add a typed `InvalidOrderStateTransitionException` so Carter's `CustomExceptionHandler` maps it to `409 Conflict`.

**Proposed methods (sketch):**

```csharp
// Ordering.Domain/Models/Order.cs
public void Confirm(Guid confirmedByUserId);            // Pending   -> Confirmed
public void MarkPreparing(Instant startedAt);           // Confirmed -> Preparing
public void MarkReady(Instant readyAt);                 // Preparing -> Ready
public void Cancel(string reason, Guid cancelledBy);    // Pending|Confirmed|Preparing -> Cancelled
public void StartDelivery();                            // Ready     -> (sets DeliveryStatus.Dispatched)
public void MarkDelivered(Instant deliveredAt);         // Dispatched -> Delivered
public void Complete(Instant completedAt);              // Delivered  -> Completed
```

Each method:

- Validates the current `Status` against a `_allowedTransitions` table (private static dict).
- Throws `InvalidOrderStateTransitionException` on illegal transition.
- Sets the appropriate `*At`/`*ByUserId` timestamp/audited column.
- Calls `AddDomainEvent(new OrderXxxEvent(this))`.

The existing `Order.Update(...)` method stays for full-DTO mutations (admin/edits) but **stops writing `Status`** — only the new methods write status. (Implementation note: split into `UpdateOrderDetails(...)` and keep callers honest; or just guard with an `if (status == Status)` no-op for backwards compat — decide when implementing.)

Also: introduce mutators on `OrderItem` for `MarkItemPreparing`, `MarkItemReady`, `MarkItemBumped` so Kitchen can drive per-item progress. These mirror `Order.Mark*` but operate on `_orderItems`.

**Acceptance:**

- Unit tests in `Ordering.Domain.Tests/Models/OrderTests.cs` for every legal transition (P→C→P→R→Delivered→Completed; cancellation paths).
- Unit tests for every illegal transition (e.g. `Pending→Ready` throws).
- No `Status = ` assignment exists outside the new methods.

---

### Phase 3 — Kitchen-callable HTTP endpoints

**Files touched (new):**

- `Ordering.API/Endpoints/ConfirmOrder.cs` — `POST /api/v1/orders/{id}/confirm` → guard `kitchen:view_orders`, delegates to new `ConfirmOrderCommand`.
- `Ordering.API/Endpoints/StartOrderPrep.cs` — `POST /api/v1/orders/{id}/start-prep`.
- `Ordering.API/Endpoints/MarkOrderReady.cs` — `POST /api/v1/orders/{id}/mark-ready`.
- `Ordering.API/Endpoints/CancelOrder.cs` — `POST /api/v1/orders/{id}/cancel` (body: `{ "reason": "..." }`).
- `Ordering.API/Endpoints/StartItemPrep.cs` — `POST /api/v1/orders/{id}/items/{itemId}/start-prep`.
- `Ordering.API/Endpoints/MarkItemReady.cs` — `POST /api/v1/orders/{id}/items/{itemId}/mark-ready`.
- `Ordering.API/Endpoints/MarkOrderDelivered.cs` — `POST /api/v1/orders/{id}/mark-delivered`.

All endpoints:

- Group under `app.MapGroup("/api/v1").WithTags("Kitchen")` (separate tag from the existing `"Orders"` group so the OpenAPI stays organised).
- Use `RequirePermission("kitchen:update_prep_status")` from `BuildingBlocks.Authorization` (already wired across other services).
- Take only the minimum body shape (`{ "reason": "..." }`) — never a full `OrderDto`.
- Return `204 NoContent` on success; `CustomExceptionHandler` already maps `InvalidOrderStateTransitionException` → `409 Conflict` once added in Phase 2.

**Endpoint naming convention:** match `POST /api/v1/orders/{id}/{action}` (REST verb on the resource action); reserve kebab-case verbs aligned with the action they perform. Update `db_relational_model.md` and `current-architecture.md` if the table changes.

**No new GET endpoint in this plan.** Reads for the kitchen queue belong to `Kitchen.API` (it owns the kitchen-shape projection). Ordering keeps its existing `GET /orders/{id}` for the one-off fetch by id.

**Acceptance:**

- All seven endpoints respond 2xx on legal transitions and 409 on illegal ones (integration test per endpoint).
- All endpoints reject anonymous calls and calls with a JWT that lacks `kitchen:update_prep_status` (AuthN/AuthZ integration test).

---

### Phase 4 — Consume Kitchen-emitted events

This phase is contingent on the *outbound* events Kitchen publishes. As soon as those contracts land in `BuildingBlocks.Messaging/Events/`:

- New consumer: `Ordering.Application/Orders/EventHandlers/Integration/KitchenOrderAcceptedHandler.cs` → `IConsumer<KitchenOrderAcceptedIntegrationEvent>` → fetches the `Order`, calls `Order.Confirm(...)`.
- New consumer: `Ordering.Application/Orders/EventHandlers/Integration/KitchenOrderPrepStartedHandler.cs` → calls `Order.MarkPreparing(...)`.
- New consumer: `Ordering.Application/Orders/EventHandlers/Integration/KitchenOrderReadyHandler.cs` → calls `Order.MarkReady(...)`.
- New consumer: `Ordering.Application/Orders/EventHandlers/Integration/KitchenOrderItemStateChangedHandler.cs` → for per-item granularity.

A consumer should never trust the inbound event to be the source of truth on `Status`. Pattern: **fetch the latest aggregate by id, then call the aggregate method** so the same legal-transition guards apply. If the aggregate is missing or in the wrong state, the handler logs + nacks the message.

**Registration:** `Ordering.Application/DependencyInjection.cs:24` already calls `AddMessageBroker(configuration, Assembly.GetExecutingAssembly())` — adding new consumers in the same assembly is automatic.

**Replay:** document the `MassTransit` retry/fault policy gap (none configured today) as a follow-up; this plan does not introduce one.

---

### Phase 5 — Cleanup: dead duplicate consumer

**File touched:**

- `Ordering.API/Consumers/BasketCheckoutEventConsumer.cs` (76 lines).

Today only `Ordering.Application/Orders/EventHandlers/Integration/BasketCheckoutEventHandler.cs` is registered — `Ordering.API/DependencyInjection.cs` never calls `AddMessageBroker`, so the duplicate in `Ordering.API` is dead.

Two acceptable resolutions (pick one and remove the other during implementation):

1. **Delete** the API-layer file. The Application-layer handler is canonical. (Preferred — keeps a single canonical place to read when joining the codebase.)
2. **Move** the canonical handler into the API assembly by calling `AddMessageBroker` from `Ordering.API/DependencyInjection.cs` instead of the Application one. Avoids having the Application layer depend on MassTransit — but the rest of `Ordering.Application` already depends on it, so this is upside-down.

**Decision: option 1.** Delete `Ordering.API/Consumers/BasketCheckoutEventConsumer.cs` and its containing folder if empty. The Application-layer handler remains.

**Acceptance:**

- The solution builds without the deleted file.
- A test run of `Basket.API`'s checkout path still produces exactly one `Order` per checkout (no double-create regression).
- The `Ordering.API/Consumers/` folder no longer exists.

---

### Phase 6 — Tests, docs, AGENTS.md update

**Tests:**

- Extend `Ordering.Domain.Tests/Models/OrderTests.cs` for every new transition method (Phase 2 acceptance — covers this).
- New `Ordering.Application.Tests/Integration/OrderCreatedEventPublishesCleanContract.cs` — assert the published message has no `Payment*` properties (Phase 1 acceptance).
- New `Ordering.API.Tests` project with WebApplicationFactory + a Testcontainers MS SQL fixture: hits each new endpoint, asserts 2xx / 409 / 401. (Today no API integration test project exists for Ordering — this would be the first. Smaller in scope than it sounds: the Carter + MediatR test wiring is identical to Identity's existing `Identity.API.Tests`.)
- Extend `Ordering.Domain.Tests/Models/OrderItemTests.cs` (if/when item-level methods are added) — not strictly required by this plan but expected to grow.

**Docs to update:**

- `docs/architecture/current-architecture.md` — replace the "no inbound status endpoints" line with the seven new routes; mark `OrderFullfilment` flag as deprecated-but-still-kill-switch until Kitchen is live.
- `docs/architecture/db_relational_model.mermaid` — no schema change, but reconcile any "missing transition methods" call-outs.
- `docs/architecture/architecture.md` (older Phase 3 doc) — leave alone, it's a historical design intent document.

**AGENTS.md:**

- No new convention; state-transition method naming follows the existing Aggregate pattern.

---

## 5. Open questions

1. **`OrderUpdate`'s status assignment.** Today `Order.Update(...)` writes `Status` raw. Two choices: (a) drop the parameter + add an explicit `ChangeStatus(status)` guard, (b) silently ignore mismatched statuses with a warning. (a) is the textbook DDD choice but breaks any caller that today relied on the status round-trip via `Update`. I'll pick (a) and refactor callers; confirm if you want (b).
2. **Outbox.** Today a process crash between `SaveChangesAsync` and `MassTransit` publish loses events. Phase 1 doesn't fix it (card data removal alone is enough to make the event safe to publish even at-least-once). Add a true transactional outbox as a separate plan once Kitchen is live and event reliability becomes load-bearing.
3. **Feature flag.** Should `OrderFullfilment` stay on `ordering.api` permanently as a circuit breaker? Or move it to a per-event toggle inside Kitchen? Recommend: keep on Ordering; it's the publisher's kill switch.
4. **Same-write-source decision.** If Kitchen emits `OrderPrepStateChangedIntegrationEvent` AND a UI also hits the new `POST /orders/{id}/start-prep` endpoint, both reach `Order.MarkPreparing`. That's fine — the aggregate guards transitions idempotently. Confirm UI goes through the REST path and Kitchen uses events; both writes point at the same aggregate.
5. **Per-item granularity vs whole-order granularity.** The plan covers both (`MarkItem*`, `MarkOrder*`). If a future Expo screen reads aggregate state, do we add a `MarkOrderReady` that completes only when **all** items are ready? Probably yes, but defer that decision to Kitchen's plan.

---

## 6. Acceptance criteria (overall)

- [ ] Phase 1: zero `PaymentDto` properties appear in any RabbitMQ-bound message.
- [ ] Phase 2: every `Order.Status = ` assignment is gated by a method on the aggregate; illegal transitions throw `InvalidOrderStateTransitionException`.
- [ ] Phase 3: seven new endpoints exist under `…/orders/{id}/...`, gated by `kitchen:update_prep_status`.
- [ ] Phase 4: Ordering consumes Kitchen's outbound events through aggregate methods (no direct `DbContext` writes in handlers).
- [ ] Phase 5: `Ordering.API/Consumers/BasketCheckoutEventConsumer.cs` and its folder are deleted; tests confirm no double-create regression.
- [ ] Phase 6: new domain tests, new integration tests, docs updated.
