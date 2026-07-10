# Kitchen.Service — Post-M5 Follow-up Plan

> Scope: the items that remained unaddressed when M1–M5 closed (the initial Kitchen service plan + Ordering integration plan). Captures the work needed to make the Kitchen ↔ Ordering loop fully operational and to harden the broker-side health surface.
>
> **Status (last updated 2026-07-10):** Phases A–E are **complete** (shipped in commit `d4afd31`). F.1, F.2, F.3, F.4, F.5, F.6, the broker-uniformity follow-on, and G are **complete**. The open-questions surface (§8.1 — `MarkPreparing` event from Kitchen, §8.3 — `OrderItem.Customizations` schema migration) remains as future work.

---

## 1. Context

M1–M5 are complete and the full Kitchen service is end-to-end functional: aggregates, command handlers, SignalR broadcast, `/health`, and 48/48 tests (36 unit + 12 `WebApplicationFactory` integration). The plan at `Services/Kitchen/Kitchen.API/KITCHEN_SERVICE_PLAN.md` and the Ordering-side plan at `Services/Ordering/Ordering.API/KITCHEN_INTEGRATION_PLAN.md` both close on that milestone.

Items enumerated in the original follow-up:

1. **Ordering never consumes the events Kitchen publishes** (M3 publish path is live but no `KitchenOrder*IntegrationEventHandler` exists in `Ordering.Application`). — **[x] Phase B complete.**
2. **`Order` aggregate lacks the state-transition methods** (`Confirm`, `MarkReady`, `Cancel`) those consumers would invoke — covered by Ordering plan §4.2 but never implemented. — **[x] Phase A complete; `MarkPreparing` was also added per §7.1 recommendation flipped to (a).**
3. **No transactional outbox** in either Ordering or Kitchen (M6 in the original plan) — at-least-once delivery across the bus is best-effort and a process crash between `SaveChangesAsync` and `Publish` loses the event. — **[x] Phase C complete (single-replica safe); multi-replica safety deferred to Phase F.**
4. **`/health` skips the RabbitMQ check** — `AspNetCore.HealthChecks.Rabbitmq` 8.0.x's `RabbitMQ.Client >= 6.8.1` constraint now resolves to the 7.2.1 transitive dep that MassTransit 8.5.10 pulls in, so `AddRabbitMQ(...)` is wired under `name: "messagebroker"`. — **[x] Phase E complete (Path E.1).**
5. **`DeserializeStringList` in `OrderingExtensions.ToOrderCreatedIntegrationEvent` is best-effort** — when `OrderItem.SelectedVariations` / `Customizations` are stored as richer jsonb objects (per `BasketItemCustomizationDto`), the integration event silently drops them. The kitchen UI receives an empty list with no signal that data was lost. — **[x] Phase D complete; jsonb is now projected into typed `KitchenOrderItemVariation` / `KitchenOrderItemCustomization` records.**

Additional items uncovered while closing Phases A–E are tracked as **Phase F** (operational hardening + leftover from `KITCHEN_INTEGRATION_PLAN.md`) and **Phase G** (docs refresh). F.4, the broker-uniformity follow-on, and F.6 (the broker-down `/health` 503 path) all closed together with F.3 — see commit history for the per-phase commit boundaries.

---

## 2. Goal

Make the Kitchen ↔ Ordering loop fully operational (events delivered, aggregate state updated on both sides, no events lost on crash) and bring the broker into the health surface.

---

## 3. Out of scope

- The Ordering plan's M0 acceptance item (clean event contract from Ordering → Kitchen) is already complete in M0; this plan does not redo it.
- The Ordering plan's Phase 2 (`Order.Confirm` / `Order.MarkReady` / `Order.Cancel` aggregate methods + `InvalidOrderStateTransitionException` mapped to 409) is the prerequisite for Phase A below; the methods must land first. — **Done; Phase A closed it.**
- A saga / orchestrator for fulfilment. The original plan notes this is a separate, future effort.
- Migrating `OrderItem.SelectedVariations` / `Customizations` from jsonb-string to a typed array. That's a schema migration that touches Ordering, Catalog, and Basket — out of scope for this follow-up. The Phase D fix is a contract-only change. — **Still out of scope; remains a future schema migration.**

---

## 4. Phases (Phases A–E + F.1 / F.2 / F.3 / F.4 / F.5 / F.6 / G)

### Phase F.4 — Outbox payload schema versioning (follow-up §7.2) — **DONE**

**Files touched (shipped):**

- New: `BuildingBlocks.Messaging/Outbox/OutboxDeadMessage.cs` — quarantine row mirroring `OutboxMessage` shape, with `Reason` + `RejectedAt`.
- New: `BuildingBlocks.Messaging/Outbox/OutboxDeadMessageConfiguration.cs` — EF Core mapping (table `outbox_messages_dead`, index on `RejectedAt` for triage queries).
- Modified: `BuildingBlocks.Messaging/Outbox/OutboxOptions.cs` — added `MaxSupportedVersion` (default 1) so the dispatcher knows the upper bound.
- Modified: `BuildingBlocks.Messaging/Outbox/IOutboxDbContext.cs` — added `OutboxDeadMessages` DbSet.
- Modified: `BuildingBlocks.Messaging/Outbox/OutboxDispatcher.cs` — schema-version gate inside `DispatchBatchAsync`. Rows whose `SchemaVersion > MaxSupportedVersion` are copied to `outbox_messages_dead` with `Reason = "unsupported_schema_version"` and deleted from the live table; the broker publish is skipped.
- Modified: `Services/Ordering/Ordering.Infrastructure/Data/ApplicationDBContext.cs` and `Services/Kitchen/Kitchen.API/Infrastructure/Data/KitchenDbContext.cs` — register the new `OutboxDeadMessages` DbSet + `OutboxDeadMessageConfiguration`.
- New migration: `Services/Ordering/Ordering.Infrastructure/Data/Migrations/20260710061207_AddOutboxDeadMessages.cs` (MSSQL).
- New migration: `Services/Kitchen/Kitchen.API/Infrastructure/Data/Migrations/20260710061218_AddOutboxDeadMessages.cs` (Postgres).
- New test: `Services/Ordering/Ordering.API.Tests/Integration/OrderingOutboxDeadLetterTests.cs` — stages a row with `SchemaVersion = 99`, runs the dispatcher once, asserts the row was moved (not copied) to `outbox_messages_dead` with `Reason = "unsupported_schema_version"`, and the live table has zero rows for that id.

**Rules (shipped):**

- The dispatcher is the gate, not the publisher — when a new publisher version rolls out before consumers, the dispatcher routes the unsupported rows to poison instead of letting an unknown payload hit a downstream `Type.GetType(...)` lookup that would throw.
- The new table is a drop-in shape mirror of `outbox_messages` plus `Reason` + `RejectedAt`. An operator can copy a row back to the live table once the underlying consumer code is deployed and `Outbox:MaxSupportedVersion` is bumped.
- Bump workflow: bump `Outbox:MaxSupportedVersion` in config, deploy, then any backlog in the dead table is operator decision (re-emit or discard).

**Acceptance (verified):**
- [x] `dotnet test Ordering.API.Tests` discovers `OrderingOutboxDeadLetterTests.FutureVersionRow_IsMovedToDeadTable`.
- [x] The migration runs against a fresh DB without dropping the live `outbox_messages` table.
- [x] The `OutboxMessage.SchemaVersion` column is unchanged; only the new dead table is added.

### Phase F.5 — `IntegrationEvent` payload versioning on the wire — **DONE**

**Files touched (shipped):**

- Modified: `BuildingBlocks.Messaging/Events/IntegrationEvent.cs` — added `MessageVersion` (int, init, default 1) to the base record. The XML doc spells out the dual-shape rollover protocol: additive changes (new optional fields) don't need a bump because `System.Text.Json` tolerates unknown fields on the read side; breaking changes (rename, type change, drop) ship a new event subtype with `MessageVersion = 2` and the same `EntityName` (MassTransit's `MessageInitializer` pattern) so both shapes route to the same consumer topic during the rollover window.
- Modified: `BuildingBlocks.Messaging/Outbox/OutboxPublisher.cs` — the publisher reads `((IntegrationEvent)message)?.MessageVersion` and stamps it into the new outbox row's `SchemaVersion`. Single source of truth: bumping the message's `MessageVersion` automatically bumps the row's `SchemaVersion`, and the existing F.4 schema-version gate picks it up.
- New test: `Services/Ordering/Ordering.API.Tests/Integration/OrderingOutboxWireVersioningTests.cs` with two tests:
  - `MessageVersionDefaults_ToOne` — proves the default for every existing `IntegrationEvent` is 1 (no existing publisher needs to change).
  - `NewPayload_ExtraFields_RelayWithoutCrash` — stages a v1-shaped payload with an extra `FutureField` the v1 CLR type doesn't declare, runs the dispatcher, asserts the row was relayed (1 dispatched) and stamped. This is the property that has to hold for safe dual-shape rollover: an old consumer deserializes a new payload.

**Rules (shipped):**

- The F.4 schema-version gate is the same gate — bumping `MessageVersion` on a publisher automatically raises the row's `SchemaVersion`, and the dispatcher routes the new shape to `outbox_messages_dead` for any consumer running with `Outbox:MaxSupportedVersion = 1`. Bump `MaxSupportedVersion` in lockstep with the consumer deploy.
- The same `IntegrationEvent` class handles both v1 and v2 publishers. A breaking change (e.g. renaming `OrderId` to `OrderGuid`) creates a new subtype `OrderCreatedIntegrationEventV2 : IntegrationEvent` with `MessageVersion = 2` and the same entity name. The new subtype's `MessageVersion` is what the publisher stamps into the outbox row.
- The dual-shape rollover window is enforced by `Outbox:MaxSupportedVersion` and the F.4 dead-letter table. Old code running with `MaxSupportedVersion = 1` skips v2 rows; new code running with `MaxSupportedVersion = 2` relays them.

**Acceptance (verified):**
- [x] `dotnet test Ordering.API.Tests` discovers `OrderingOutboxWireVersioningTests.NewPayload_ExtraFields_RelayWithoutCrash` and `MessageVersionDefaults_ToOne`.
- [x] The test asserts the dispatcher relays the row (DispatchedAt stamped), proving the wire format is read-tolerant of unknown fields.
- [x] No new migration — `MessageVersion` is on the message payload, not on the row, so the schema is unchanged.

### Phase F.6 — Negative-path coverage for `/health` — **DONE**

**Files touched (shipped):**

- Modified: `Services/Kitchen/Kitchen.API.Tests/Integration/KitchenWebApplicationFactory.cs` — added `StopRabbitMqContainerAsync()` (delegates to `RabbitMqContainer.StopAsync()`).
- Modified: `Services/Kitchen/Kitchen.API.Tests/Integration/KitchenHealthEndpointTests.cs` — added `Health_WhenBrokerDown_Returns503WithBrokerUnhealthy`.

**Rules:** Stop the broker Testcontainers container in place; the next `/health` probe must return 503 with `entries.messagebroker.status == "Unhealthy"`. The EF Core check (Postgres `kitchendb`) stays healthy.

**Acceptance (verified):**
- [x] Test discovered by xUnit.
- [x] Same Testcontainers fixture; only the broker container is stopped.

### Broker uniformity follow-on (Phase G optional) — **DONE**

`AspNetCore.HealthChecks.Rabbitmq` 8.0.2 added to `Services/Ordering/Ordering.API/Ordering.API.csproj` and `Services/Basket/Basket.API/Basket.API.csproj`. Both services now `AddRabbitMQ(rabbitConnectionString, name: "messagebroker", tags: ["broker", "ready"])` in the same shape as Kitchen. Every service that publishes RabbitMQ traffic now reports broker reachability under `entries.messagebroker` on `/health`.

---

## 4. Phases

### Phase A — Ordering-side state-transition methods on `Order` — **DONE**

**Files touched:**

- Modified: `Services/Ordering/Ordering.Domain/Models/Order.cs` — added `Confirm`, `MarkPreparing`, `MarkReady`, `StartDelivery`, `MarkDelivered`, `Complete`, `Cancel` (and the `IDomainEvent` types they raise: `OrderConfirmedEvent`, `OrderPreparingEvent`, `OrderReadyEvent`, `OrderDeliveryStartedEvent`, `OrderDeliveredEvent`, `OrderCompletedEvent`, `OrderCancelledEvent`). Mirror of `KitchenTicket`'s illegal-transition exception pattern.
- New: `Services/Ordering/Ordering.Domain/Exceptions/InvalidOrderStateTransitionException.cs` — derives from `DomainException`, mapped to HTTP 409 by the global exception handler.
- Modified: `Services/Ordering/Ordering.Domain/Models/Order.cs::Update(...)` — dropped the raw `Status` parameter (per the Ordering plan §5 open-question #1, option (a)); the method now mutates only billing/delivery/payment.
- New: `Services/Ordering/Ordering.Domain/Models/OrderItem.cs` got `MarkItemPreparing` / `MarkItemReady` and `Services/Ordering/Ordering.Domain/Exceptions/InvalidOrderItemStateTransitionException.cs`.
- New tests in `Ordering.Domain.Tests/Models/OrderTests.cs` for legal + illegal transitions across all seven methods.

**Actual rules shipped (slight deviation from the original sketch):**

- `Confirm(confirmedByUserId, now)` — `Pending → Confirmed`. ✓
- `MarkPreparing(now)` — `Confirmed → Preparing`. **Added** (the §7.1 decision was flipped from "skip" to "include" so the `POST /orders/{id}/start-prep` REST endpoint can drive the transition directly).
- `MarkReady(now)` — `Preparing → Ready`. ✓
- `StartDelivery()` — `Ready → { DeliveryStatus = Dispatched }`. ✓ (no Status change; used for dispatch audit.)
- `MarkDelivered(now)` — `Ready → Delivered`. ✓
- `Complete(now)` — `Delivered → Completed`. ✓
- `Cancel(reason, cancelledBy, now)` — any non-terminal → `Cancelled`. ✓

**Acceptance (verified):**
- [x] `dotnet test Ordering.Domain.Tests` passes with the new transition tests.
- [x] No `Status = ...` assignment exists outside the new methods (`Order.Update` and the seven behaviour methods exhaust the write paths).

**Drift to fix up later:** §5 of the original `KITCHEN_INTEGRATION_PLAN.md` still describes the seven endpoints with `CancelOrder.cs`; the committed file is `CancelOrderCommandEndpoint.cs` — rename opportunity for Phase G.

---

### Phase B — Ordering-side consumers for Kitchen's outbound events — **DONE**

**Files touched (all under `Services/Ordering/Ordering.Application/Orders/EventHandlers/Integration/`):**

- New: `KitchenOrderAcceptedIntegrationEventHandler.cs` — `IConsumer<KitchenOrderAcceptedIntegrationEvent>` → fetch `Order` → `Order.Confirm(event.ConfirmedByUserId, event.ConfirmedAt)` → save. ✓
- New: `KitchenOrderReadyIntegrationEventHandler.cs` — `IConsumer<KitchenOrderReadyIntegrationEvent>` → fetch `Order` → `Order.MarkReady(event.ReadyAt)` → save. ✓
- New: `KitchenOrderBumpedIntegrationEventHandler.cs` — `IConsumer<KitchenOrderBumpedIntegrationEvent>` → log only (no aggregate change today; records audit row). ✓
- New: `KitchenOrderCancelledIntegrationEventHandler.cs` — `IConsumer<KitchenOrderCancelledIntegrationEvent>` → fetch `Order` → `Order.Cancel(event.Reason, event.CancelledByUserId, event.CancelledAt)` → save. ✓

**Rules (from `KITCHER_INTEGRATION_PLAN.md` §4.4):** all four consumers follow "fetch latest aggregate, call guarded method" — they never trust the inbound event as source of truth on `Status`. Missing order → log + nack (MassTransit transient fault → broker retry). `MassTransit.AddMessageBroker(...)` scans the assembly, so registration is automatic.

**Acceptance (verified):**
- [x] All four consumers registered — start `Ordering.API` and confirm via `MassTransit` debug logs that consumer types are listed.
- [x] `dotnet test Ordering.Application.Tests` covers each consumer with happy-path + not-found-order negative path. Tests in:
  - `Ordering.Application.Tests/EventHandlers/Integration/KitchenOrderAcceptedIntegrationEventHandlerTests.cs`
  - `Ordering.Application.Tests/EventHandlers/Integration/KitchenOrderReadyIntegrationEventHandlerTests.cs`
  - `Ordering.Application.Tests/EventHandlers/Integration/KitchenOrderBumpedIntegrationEventHandlerTests.cs`
  - `Ordering.Application.Tests/EventHandlers/Integration/KitchenOrderCancelledIntegrationEventHandlerTests.cs`

---

### Phase C — M6 transactional outbox (Ordering + Kitchen) — **PARTIAL (single-replica)**

**Files touched (Ordering — shipped):**

- New: `Services/Ordering/Ordering.Infrastructure/Data/Interceptors/OrderingOutboxPublisher.cs` — `SaveChangesInterceptor` that serializes new/updated aggregate domain events into `outbox_messages` inside the same transaction as the aggregate mutation.
- New: `Services/Ordering/Ordering.Infrastructure/Data/Interceptors/OrderingOutboxDispatcher.cs` — `IHostedService` that polls the table, relays rows to `IPublishEndpoint`, marks them dispatched.
- New: EF Core migration `20260706233202_AddOutboxMessages` adds `outbox_messages` (`Id` Guid, `OccurredOn` Instant, `Type` string, `Payload` jsonb, `DispatchedAt` Instant?).
- Modified: `Services/Ordering/Ordering.Infrastructure/DependencyInjection.cs` — registers both.

**Files touched (Kitchen — shipped):**

- Mirror: `Kitchen.API/Infrastructure/Interceptors/KitchenOutboxPublisher.cs`, `KitchenOutboxDispatcher.cs`, migration `20260706233256_AddOutboxMessages` on `kitchendb.outbox_messages`.

**Rules (shipped):**

- Outbox row written inside the same EF Core transaction as the aggregate mutation — crash between commit and `IPublishEndpoint.Publish` is no longer possible, the dispatcher picks up the row on restart.
- Idempotency on the consumer side uses `IntegrationEvent.Id` (constructor-set in M0 per `current-architecture.md:349`) as the dedup key.
- Dispatcher polling cadence: 1 s when the queue is non-empty, 5 s when empty. ✓
- Tests disable the dispatcher with `IConfigureOptions<OutboxOptions>` → `Enabled = false` for fast unit tests; integration tests assert end-to-end round-trips.

**Tools chosen (custom)** — matches the in-house `DispatchDomainEventsInterceptor` pattern, no new dependency, full payload control. Symmetric implementation across Ordering and Kitchen.

**Acceptance (verified):**
- [x] `Ordering.Infrastructure.Tests/Outbox/OrderingOutboxPublisherTests.cs` covers the SaveChangesAsync + crash-then-restart scenario.
- [x] Outbox row remains and is dispatched on restart (verified via the test).
- **Still open:** the round-trip integration test (publish → land in Testcontainers RabbitMQ) was deferred. Add in Phase F.

**Multi-replica gap (deferred to Phase F):** the dispatcher has no row-claim — two replicas will race the same row. Fix with `SELECT FOR UPDATE SKIP LOCKED` (Postgres) or MassTransit's built-in outbox. Single-replica deployments are safe today.

---

### Phase D — Richer order-item event payload — **DONE**

**Files touched (shipped):**

- Modified: `BuildingBlocks.Messaging/Events/OrderCreatedIntegrationEvent.cs` — `Items: IReadOnlyList<KitchenOrderItemPreview>` now carries typed `KitchenOrderItemVariation` + `KitchenOrderItemCustomization` (no more raw `string` lists).
- New: `BuildingBlocks.Messaging/Events/KitchenOrderItemVariation.cs` — `record KitchenOrderItemVariation(string Name, decimal Price)`.
- New: `BuildingBlocks.Messaging/Events/KitchenOrderItemCustomization.cs` — `record KitchenOrderItemCustomization(string Label, string? Value, decimal? Price)`.
- Modified: `Services/Ordering/Ordering.Application/Extensions/OrderExtensions.cs::ToOrderCreatedIntegrationEvent` — replaces `DeserializeStringList` with `DeserializeVariations` / `DeserializeCustomizations`, both tolerant of legacy `string[]` shape **and** richer `{ Name, Price }` / `{ Label, Value, Price }` shapes. `JsonException` or unknown shape → empty list (logged via the surrounding call site), never null.
- Modified: `Kitchen.API/Application/Extensions/KitchenTicketExtensions.cs::ToOrderItemSeeds` — forwards the typed records to `OrderItemSeed`.

**Rules (shipped):**

- New records carry `Name`/`Label` + `Price`/`Value?` so a kitchen display renders either `Size: Large (+$2.50)` or `No onions` with one shape.
- Aggregate (`KitchenTicketItem.SelectedVariations`/`Customizations`) stays as `IReadOnlyList<string>` for EF snapshot — schema unchanged.
- `OrderItem.Customizations` / `SelectedVariations` in Ordering.Domain stay jsonb-string; future schema migration lifts them to typed arrays (still out of scope).

**Acceptance (verified):**
- [x] `Ordering.Application.Tests/Extensions/OrderExtensionsPhaseDTests.cs` asserts realistic Basket payloads round-trip through both legacy and richer shapes.
- [x] Items with no variations/customizations serialize as empty lists, never null.
- [x] Unparseable rows fall back to empty lists rather than crashing the bus.

---

### Phase E — RabbitMQ health check — **DONE (Path E.1)**

**Path E.1 shipped:**

- `AspNetCore.HealthChecks.Rabbitmq` 8.0.2's `RabbitMQ.Client >= 6.8.1` constraint resolves to the `7.2.1` transitive dep that MassTransit 8.5.10 pulls in — no MassTransit upgrade required.
- Modified: `Services/Kitchen/Kitchen.API/Kitchen.API.csproj` — package reference confirmed.
- Modified: `Services/Kitchen/Kitchen.API/Program.cs:52-62` — `AddHealthChecks().AddDbContextCheck<KitchenDbContext>(name: "kitchendb", tags: ["db","ready"]).AddRabbitMQ(rabbitConnectionString: ..., name: "messagebroker", tags: ["broker","ready"])`.

**Acceptance (verified):**
- [x] `GET /health` returns 200 with `entries.messagebroker.status == Healthy` when the broker is reachable (sanity-tested locally).
- [x] `Kitchen.API.Tests/Integration/KitchenHealthEndpointTests.cs` covers the happy path.
- **Still open:** the 503 negative path (broker container stopped mid-test) is not yet covered — small follow-up addition to `KitchenHealthEndpointTests.cs`.

---

## 5. Phases F & G — outstanding hardening and docs refresh

Phases A–E shipped in commit `d4afd31`. The work below did **not** land with that commit and is the next-batch TODO for the project.

### Phase F — Operational hardening & leftover integration-plan items

**F.1 — Remove the dead `BasketCheckoutEventConsumer` (Ordering-side, leftover from `KITCHEN_INTEGRATION_PLAN.md` Phase 5).**

- File: `Services/Ordering/Ordering.API/Consumers/BasketCheckoutEventConsumer.cs` (76 lines, never registered).
- The canonical handler is `Ordering.Application/Orders/EventHandlers/Integration/BasketCheckoutEventHandler.cs`; the API-layer copy is dead.
- Delete the file (and its empty `Consumers/` folder); keep `Ordering.Application` as the single MassTransit registration point.
- Acceptance: solution builds, a checkout-driven run still produces exactly one `Order` per checkout (no double-create regression — this is what made the duplicate risky).

**F.2 — Scaffold real `Ordering.API.Tests`.**

- The project directory exists under `obj/Debug` but has zero `.cs` source files.
- Mirror `Kitchen.API.Tests/Integration/{KitchenWebApplicationFactory,KitchenApiIntegrationTests,KitchenHealthEndpointTests}.cs` and `Identity.API.Tests`.
- Cover the seven new endpoints with `WebApplicationFactory`: 2xx on legal transitions, 409 on illegal transitions, 401 on anonymous calls, 403 on missing `kitchen:update_prep_status`.
- Acceptance: `dotnet test Ordering.API.Tests` passes against a Testcontainers MS SQL + RabbitMQ fixture.

**F.3 — Outbox row-claim for multi-replica safety.**

- Today two replicas of `Ordering.API` would race on the same `outbox_messages` row.
- Fix per side:
  - `Ordering.Infrastructure/Interceptors/OrderingOutboxDispatcher.cs` — Postgres `SELECT FOR UPDATE SKIP LOCKED` claim, or fall back to `pg_try_advisory_lock(row_id)` per row.
  - `Kitchen.API/Infrastructure/Interceptors/KitchenOutboxDispatcher.cs` — mirror.
- Alternative: replace the in-house dispatcher with `MassTransit.EntityFrameworkCore`'s built-in outbox (the custom path was chosen in Phase C for symmetry — revisit when the multi-replica need is real).
- Acceptance: an integration test that boots two API replicas, publishes a single event, asserts it lands in RabbitMQ exactly once.

**F.4 — Outbox payload schema versioning (follow-up §7.2).**

- Add `SchemaVersion int NOT NULL` column to `outbox_messages` (Ordering + Kitchen migrations).
- Publisher stamps the current major version on insert; consumer reads it, drops mismatched versions into a poison queue (`outbox_messages_dead`) for triage.
- Acceptance: a fake "v2" event published to a v1 consumer is logged to the dead queue, not silently applied.

**F.5 — IntegrationEvent payload versioning (follow-up §7.5).**

- The Phase D shape change rolled out without version negotiation. In-flight broker messages may carry the old shape during one deploy cycle.
- Recommended: MassTransit's `MessageInitializer` with `Include = true`, accepting both shapes for one release, then dropping the reader.
- Acceptance: a replay test (re-publish the legacy shape on the new schema) round-trips through the consumer with no crash.

**F.6 — Negative-path coverage for `/health`.**

- `Kitchen.API.Tests/Integration/KitchenHealthEndpointTests.cs` covers the broker happy path only.
- Add a stop-the-broker case (use the existing Testcontainers RabbitMQ fixture; stop the container mid-test, expect 503).
- Acceptance: `/health` returns 503 with `entries.messagebroker.status == Unhealthy` when the broker is offline.

### Phase G — `docs/architecture/current-architecture.md` refresh

> **Tracked here so it lands alongside the rest of this plan, not as an unfiled TODO.**

The current `docs/architecture/current-architecture.md` snapshot was written before Phases A–E shipped and is now stale. Phase G refreshes it in one PR — no new code, pure docs.

**Items that need to land:**

1. **§2 Tech Stack** — add `AspNetCore.HealthChecks.Rabbitmq 8.0.2` to the Health row.
2. **§4 Microservices table** — `Kitchen.API` description should mention the new RabbitMQ broker check, the SignalR broadcaster, the outbox migration, and `Application/EventHandlers/Integration/OrderCreatedIntegrationEventHandler`.
3. **§4.5 Ordering Service** —
   - Rewrite the "Aggregate behaviour" bullet. `Order.Update(...)` no longer writes `Status`; behaviour now lives in `Confirm`/`MarkPreparing`/`MarkReady`/`MarkDelivered`/`Complete`/`Cancel`, each guarded by `InvalidOrderStateTransitionException` (HTTP 409). `Order.Create` raises `OrderCreatedEvent` (handler publishes `OrderCreatedIntegrationEvent`, no `OrderDto`).
   - Mention `OrderItem.MarkItemPreparing` / `MarkItemReady` and `InvalidOrderItemStateTransitionException`.
   - List the seven new Carter endpoints (`POST /api/v1/orders/{id}/{confirm|start-prep|mark-ready|cancel|mark-delivered}` plus `POST /api/v1/orders/{id}/items/{itemId}/{start-prep|mark-ready}`), grouped under `WithTags("Kitchen")`, gated by `kitchen:update_prep_status`.
   - Drop the explicit mention of the dead `BasketCheckoutEventConsumer` (F.1 will have removed it).
   - Add note about the transactional outbox — `OrderFullfilment` flag now wraps the outbox publish, not a direct `IPublishEndpoint` call.
4. **§4.5 cross-service consumer list** — `BasketCheckoutEventHandler` is joined by the four `KitchenOrder*IntegrationEventHandler` classes.
5. **§5.2 event table** — change `KitchenOrder*` rows from "pending consumer" to "Ordering-side handler in `Ordering.Application/Orders/EventHandlers/Integration/`".
6. **§5.2 IntegrationEvent base** — note already in place; double-check the `EventType => GetType().AssemblyQualifiedName!` line is referenced.
7. **§6 Data Stores** — add `outbox_messages` row to both `orderdb` (MSSQL) and `kitchendb` (Postgres).
8. **§9 Cross-Cutting Patterns** — add bullet for the outbox (SaveChangesInterceptor writes the row inside the same transaction, dispatcher hosted service relays).
9. **§11 Local Development → Startup sequence** — note the outbox migration runs alongside the existing migrations, no extra command.
10. **§11 Tests** — add `Kitchen.API.Tests` reference is already in; add a placeholder that the `Ordering.API.Tests` scaffolded in F.2 should also be listed once it exists.
11. **§12 Observability** — `entries.messagebroker` is now part of the health response on Kitchen (and should be on Ordering — see F-extension item below).

**Optional follow-on for Phase G:** wire the same broker check into `Ordering.API` and `Basket.API`'s `/health` so every publisher of RabbitMQ traffic reports broker health uniformly. (Today only Kitchen has it.)

**Acceptance (Phase G):**
- One PR with a docs-only diff to `docs/architecture/current-architecture.md`.
- Doc accurately reflects: seven new endpoints, aggregate transition methods, four new consumers, outbox tables, broker health check, dead consumer gone (after F.1).
- Doc reviewer (a human) signs off — this file is the source of truth for "what the system is today".

---

## 6. Acceptance criteria (overall)

**Shipped in commit `d4afd31`:**

- [x] Phase A: every `Order.Status` assignment outside `Confirm` / `MarkPreparing` / `MarkReady` / `MarkDelivered` / `Complete` / `Cancel` is removed; illegal transitions throw `InvalidOrderStateTransitionException`. `Order.Update(...)` no longer writes `Status`.
- [x] Phase B: the four `KitchenOrder*IntegrationEventHandler` classes exist in `Ordering.Application`, are discovered by MassTransit assembly scanning, and apply the matching `Order` aggregate method.
- [x] Phase C: outbox rows are written inside the same EF Core transaction as the aggregate mutation; the dispatcher relays unprocessed rows on restart; consumer-side dedup via `IntegrationEvent.Id` documented. (Single-replica safe — multi-replica row-claim deferred to F.3.)
- [x] Phase D: `KitchenOrderItemPreview` carries typed `KitchenOrderItemVariation` / `KitchenOrderItemCustomization` records; the integration test asserts realistic Basket payloads round-trip without data loss.
- [x] Phase E: `Kitchen.API/Program.cs` adds the broker check; `KitchenHealthEndpointTests.cs` covers the healthy path.

**Shipped in this follow-up cycle (commits after `d4afd31`):**

- [x] F.1 — Dead `Ordering.API/Consumers/BasketCheckoutEventConsumer.cs` removed.
- [x] F.2 — `Ordering.API.Tests` covers the seven new endpoints (2xx / 409 / 401 / 403); 22 tests across `OrderingApiIntegrationTests` + 2 in `OrderingHealthEndpointTests`.
- [x] F.3 — Outbox dispatcher is multi-replica safe. Engine-native row locks (MSSQL `WITH (ROWLOCK, UPDLOCK, READPAST)`, Postgres `FOR UPDATE SKIP LOCKED`) held inside an explicit transaction across claim + publish + stamp. `OrderingOutboxMultiReplicaTests.ParallelDispatchers_EachRowClaimedExactlyOnce` proves the property end-to-end.
- [x] F.4 — Outbox poison queue: `outbox_messages_dead` table on both `orderdb` and `kitchendb`; `OutboxOptions.MaxSupportedVersion` (default 1) gates the dispatcher; future-version rows are copied to the dead table with `Reason = "unsupported_schema_version"` and skipped on publish. Test: `OrderingOutboxDeadLetterTests.FutureVersionRow_IsMovedToDeadTable`.
- [x] F.5 — `IntegrationEvent.MessageVersion` int (init, default 1) on the base record; the publisher stamps it into the row's `SchemaVersion` so a single bump propagates through the F.4 schema-version gate. `OrderingOutboxWireVersioningTests.NewPayload_ExtraFields_RelayWithoutCrash` proves the read path tolerates a v1+extra-field payload — the property that has to hold for safe dual-shape rollover.
- [x] F.6 — `/health` 503 negative-path covered in `KitchenHealthEndpointTests.Health_WhenBrokerDown_Returns503WithBrokerUnhealthy` (the Testcontainers RabbitMQ container is stopped in place).
- [x] Broker uniformity — `AspNetCore.HealthChecks.Rabbitmq` 8.0.2 wired into `Ordering.API/DependencyInjection.cs` and `Basket.API/Program.cs`. Every service that publishes RabbitMQ traffic reports broker reachability consistently.
- [x] G — `docs/architecture/current-architecture.md` refreshed across §2, §3, §4, §4.5 (the full rewrite of Aggregate behaviour + 13-row endpoint table + 5-consumer list + Transactional outbox subsection), §5.2 event table, §6 data stores, §9 cross-cutting, §11 startup + tests, §12 observability.

**Outstanding (none):**

All F phases plus G are complete. Remaining work is the open-questions surface (§8): §8.1 `MarkPreparing` event from Kitchen (future feature) and §8.3 `OrderItem.Customizations` schema migration (deferred per the Phase D note).

---

## 7. Suggested execution order — final

Already shipped (in order, in this follow-up cycle):

1. **F.1** — five-minute deletion; first because it's the most embarrassing loose thread.
2. **G** — even before any F.2/F.3 work; the doc is already wrong, and the small code change is a natural pairing for a docs PR.
3. **F.2** — `Ordering.API.Tests` scaffold; biggest correctness gain per unit time.
4. **F.3** — outbox row-claim; needed before horizontal scale.
5. **F.6** — small, isolated `/health` test addition.
6. **F.4** — outbox schema-version poison queue.
7. **F.5** — `IntegrationEvent.MessageVersion` on the wire; the dual-shape rollover protocol is in place.
8. Optional follow-on: also wire the broker check into `Ordering.API` and `Basket.API` for uniformity (DONE).

All F phases are complete. The follow-up is done.

---

## 8. Open questions / decisions — updated 2026-07-10

1. **`MarkPreparing` on `Order` — RESOLVED (option a chosen).** The implementation shipped `MarkPreparing` (`Confirmed → Preparing`) on the aggregate so the `POST /orders/{id}/start-prep` REST endpoint can drive the transition. No Kitchen-emitted event currently triggers it; if/when `KitchenTicket.StartItemPrep` emits a `KitchenOrderPrepStartedIntegrationEvent` (when the first item starts), Ordering will add a consumer for it then. Until then, `Preparing` is reached only via the REST path.
2. **Outbox payload schema versioning — RESOLVED (F.4 done).** The dispatcher is the gate; rows whose `SchemaVersion > OutboxOptions.MaxSupportedVersion` are copied to `outbox_messages_dead` with `Reason = "unsupported_schema_version"` and skipped on publish. Bump workflow documented in Phase F.4.
3. **`OrderItem.Customizations` migration — DEFERRED** (out of scope per Phase D note; future schema migration to typed jsonb array).
4. **Outbox dispatcher multi-instance safety — RESOLVED (F.3 done).** Engine-native row locks (MSSQL `WITH (ROWLOCK, UPDLOCK, READPAST)`, Postgres `FOR UPDATE SKIP LOCKED`) held inside an explicit transaction across the claim + broker publish + dispatched-on stamp. Multi-replica integration test proves the property end-to-end.
5. **IntegrationEvent payload versioning — RESOLVED (F.5 done).** `IntegrationEvent.MessageVersion` (int, init, default 1) on the base record. The publisher stamps the value into the outbox row's `SchemaVersion`, so a single bump propagates through the F.4 schema-version gate. Additive changes (new optional fields) are non-breaking because `System.Text.Json` tolerates unknown fields on the read side; breaking changes ship a new event subtype with `MessageVersion = 2` and the same `EntityName` so both shapes route to the same consumer topic during the rollover window. Test: `OrderingOutboxWireVersioningTests.NewPayload_ExtraFields_RelayWithoutCrash`.
6. **`Ordering.API.Tests` scope — RESOLVED (F.2 done).** 22 integration tests across all seven new endpoints + 2 `/health` checks. The consumer-level happy paths stay in `Ordering.Application.Tests/EventHandlers/Integration/`.