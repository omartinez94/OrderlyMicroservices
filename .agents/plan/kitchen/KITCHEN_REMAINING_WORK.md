# Kitchen — Remaining Work

> Scope: the two items that survived the M0–M5 / F.1–F.6 / G follow-up cycle. Captures the next small batch of work after the existing plans in this directory close.

---

## 1. Context

The kitchen-side plan series is complete. M0–M5 shipped in commit `d4afd31`. F.1, F.2, F.3, F.4, F.5, F.6, the broker-uniformity follow-on, and the G docs refresh landed in the follow-up cycle. The full current state is captured in `docs/architecture/current-architecture.md` and the `KITCHEN_FOLLOWUP_PLAN.md` §6 acceptance checklist is fully ticked.

The two open items that fall out of the follow-up plan's open-questions surface (§8 in `KITCHEN_FOLLOWUP_PLAN.md`):

1. **`KitchenOrderPrepStartedIntegrationEvent` from Kitchen → Ordering** — `Order.MarkPreparing` is reachable today only via the `POST /orders/{id}/start-prep` REST endpoint. There is no Kitchen-emitted event that drives it from a kitchen user's first-item-prep action.
2. **`OrderItem.Customizations` schema migration** — `OrderItem.Customizations` and `OrderItem.SelectedVariations` are jsonb-encoded `string` columns in `Ordering.Infrastructure`. The integration event's typed records (Phase D) already project them into `KitchenOrderItemCustomization` / `KitchenOrderItemVariation` shapes, but the underlying schema is still opaque-string.

Both are scoped feature additions, not the multi-file hardening that the F phases required. They live here so the next batch has a small, clear surface to work on.

For the full design picture, see:

- `KITCHEN_SERVICE_PLAN.md` — the original M0–M6 service plan (closed).
- `KITCHEN_INTEGRATION_PLAN.md` — the original Ordering-side plan (closed).
- `KITCHEN_FOLLOWUP_PLAN.md` — the post-M5 follow-up plan (closed).
- `docs/architecture/current-architecture.md` — the snapshot of the system as it is implemented today.

---

## 2. Goal

Land the two open items with the smallest reasonable blast radius, and refresh the architecture doc to reflect the new shape. No new cross-cutting infrastructure, no MassTransit / outbox / schema changes — the F phases already locked in those.

---

## 3. Out of scope

- A new saga / orchestrator for fulfillment. Both items are isolated event-emission or schema-refactor work; the orchestration concern is a separate future plan.
- Additional integration event shapes. The wire-format-versioning protocol (F.5) already covers breaking changes; future shape work uses `MessageVersion`.
- Per-item kitchen event channel changes. The existing `KitchenOrderItemReadyIntegrationEvent` / `KitchenOrderPrepStartedIntegrationEvent`-equivalent (not yet emitted) are scoped to the per-item event family, not the per-order-aggregate flow.
- Catalog/Basket cross-service changes for the jsonb-string → typed-array migration. That schema migration touches the Ordering DB; Basket + Catalog store the data differently (Marten documents in Catalog, jsonb-as-string in Basket), so their data flows are not affected.

---

## 4. Items

### Item 1 — `KitchenOrderPrepStartedIntegrationEvent` (R.1)

**Why now:** the kitchen UI's first-item-prep action is the only thing that should drive an `Order` from `Confirmed` to `Preparing` in production. Today the only path is the REST endpoint on Ordering. Without the event, the kitchen display can show the ticket's first item in `Preparing` while the `Order` aggregate stays at `Confirmed` — a state divergence that downstream consumers and the customer notification pipeline will see.

**Files touched (estimate):**

- New: `BuildingBlocks.Messaging/Events/KitchenOrderPrepStartedIntegrationEvent.cs` — payload `{ OrderId, ItemId, StaffUserId, StartedAt, OccurredOn, Id, MessageVersion }`. Mirror shape of the existing `KitchenOrderReadyIntegrationEvent` / `KitchenOrderAcceptedIntegrationEvent`.
- Modified: `Services/Kitchen/Kitchen.API/Application/KitchenTickets/Commands/StartItemPrep.cs` — when `KitchenTicket.StartItemPrep` mutates the first item to `Preparing`, the handler also publishes `KitchenOrderPrepStartedIntegrationEvent` if `Ticket.Status == New` and the item being started is the first `Preparing` item. The "first item started" predicate lives on the aggregate.
- New: `Services/Ordering/Ordering.Application/Orders/EventHandlers/Integration/KitchenOrderPrepStartedIntegrationEventHandler.cs` — `IConsumer<KitchenOrderPrepStartedIntegrationEvent>` → fetch `Order` → `Order.MarkPreparing(event.StartedAt)` → save. Mirrors the four existing Kitchen* consumers.
- New tests:
  - `Ordering.Application.Tests/EventHandlers/Integration/KitchenOrderPrepStartedIntegrationEventHandlerTests.cs` — happy path (Confirmed → Preparing) + not-found-order negative path.
  - `Kitchen.API.Tests/Integration/KitchenOrderPrepStartedEventTests.cs` — proves the kitchen handler publishes the event when the first item starts, and skips it on subsequent item starts (otherwise we'd double-emit).

**Rules:**

- Pattern: "fetch latest aggregate, call guarded method" — same as the four existing consumers. Missing order → log + nack. Illegal transition (`Confirmed` not yet reached, or `Preparing` already passed) → log + nack; the broker re-tries.
- "First item started" lives on the aggregate: `KitchenTicket` already knows when the first `Preparing` item lands (it's the trigger for the existing `KitchenTicketItemPrepStartedEvent` domain event). The application handler reads that and decides whether to emit the integration event.
- The REST endpoint on Ordering (`POST /orders/{id}/start-prep`) is not removed. It's a manual override path; the new event is the production path.

**Acceptance:**

- The four existing consumer tests + the new `KitchenOrderPrepStartedIntegrationEventHandlerTests` pass.
- `KitchenOrderPrepStartedEventTests` proves the integration event is emitted exactly once per ticket (on the first item-start action), not per item.
- A round-trip integration test that hits `POST /api/v1/kitchen/tickets/{id}/items/{itemId}/start` and then verifies the order's `Status == Preparing` is the natural proof of the end-to-end flow. Optional but recommended.

### Item 2 — `OrderItem.Customizations` schema migration to typed jsonb (R.2)

**Why now:** the F.5 wire-format-versioning protocol already lets us evolve `KitchenOrderItemCustomization` / `KitchenOrderItemVariation` (Phase D's typed records) without breaking the wire. The on-disk column is still a jsonb-encoded `string` though — typed only at the integration-event boundary, opaque on the storage side. The Catalog + Basket payloads (`BasketItemVariation`, `BasketItemCustomization`) already carry typed arrays of records; the asymmetry between upstream (typed) and storage (string) is the last open seam.

**Files touched (estimate):**

- New EF Core migration on `Ordering.Infrastructure` adding a typed jsonb array of `KitchenOrderItemCustomization` / `KitchenOrderItemVariation` records as a new column. Two options for the storage shape; pick during implementation:
  - **A. New column + dual-write.** Add `CustomizationsV2 jsonb` and `SelectedVariationsV2 jsonb` columns; the aggregate writes both during the rollout. After the migration lands and all deployments pick up the dual-write, the old string columns can be dropped in a follow-up migration. Safe, two-step.
  - **B. In-place shape change.** Update the existing column to a jsonb-typed array directly. The .NET type for the property stays as `IReadOnlyList<KitchenOrderItemCustomization>` (already a list of records) — only the column type changes. Single migration, but the new code is forced on every consumer immediately.
- Modified: `Services/Ordering/Ordering.Domain/Models/OrderItem.cs` — `Customizations` and `SelectedVariations` change from `string` to `IReadOnlyList<KitchenOrderItemCustomization>` / `IReadOnlyList<KitchenOrderItemVariation>`. The aggregate's EF mapping moves to jsonb with NodaTime + Guid + value-converter support (mirrors the Catalog-side `Jsonb` column on Marten documents).
- Modified: `Services/Ordering/Ordering.Application/Extensions/OrderExtensions.cs::ToOrderCreatedIntegrationEvent` — drops the `DeserializeVariations` / `DeserializeCustomizations` JSON parser (Phase D code) now that the aggregate is typed. The integration event still uses the same `KitchenOrderItemVariation` / `KitchenOrderItemCustomization` records, so the wire contract is unchanged.
- Modified: `Services/Ordering/Ordering.Application.Tests/Extensions/OrderExtensionsPhaseDTests.cs` — either removed (the jsonb-parse path is gone) or rewritten to assert the aggregate's typed properties round-trip through the integration event.

**Rules:**

- `BuildingBlocks.Messaging/Events/KitchenOrderItemCustomization` and `KitchenOrderItemVariation` are already the canonical wire shape. The aggregate's properties adopt them directly.
- The phase-D code in `OrderExtensions` (the `DeserializeVariations` / `DeserializeCustomizations` string-list parser) is the workaround for the opaque string column. It is removed in this item; the aggregate is now the single source of truth.
- The `OrderItem.Customizations` jsonb-string shape was an early-stage design when Basket + Ordering both stored a flat `string[]` of pre-formatted text. With Basket's `BasketItemCustomization` already being a typed record and the integration event also being typed, the only thing left opaque is the storage column. This item closes the gap.
- EF Core Postgres jsonb column type with a value converter for `IReadOnlyList<>` requires a per-property converter (the type is open-generic). Implementations exist on the catalog-side and on BuildingBlocks — pick the existing converter rather than rolling a new one.

**Acceptance:**

- The new migration runs against a fresh `Orderdb` without data loss on the existing jsonb-string column (dual-write column A) or with explicit data shape conversion (in-place option B — recommended only if the team is willing to write a one-time migration script that maps the string to the typed array).
- `dotnet test Ordering.Application.Tests` passes with the new property types and the simplified `ToOrderCreatedIntegrationEvent` (the jsonb parser is gone).
- A round-trip integration test: create an `Order` with two items, each with two variations + two customizations; fetch the order; assert the typed properties match the staged values.

---

## 5. Acceptance criteria (overall)

- [ ] R.1: `KitchenOrderPrepStartedIntegrationEvent` is emitted by `Kitchen.API` exactly once per ticket (on the first item-start action) and consumed by `Ordering.Application` to drive `Order.MarkPreparing`. The REST endpoint on Ordering remains as a manual override.
- [ ] R.1: `KitchenOrderPrepStartedIntegrationEventHandlerTests` (Ordering.Application.Tests) and `KitchenOrderPrepStartedEventTests` (Kitchen.API.Tests) pass.
- [ ] R.2: `OrderItem.Customizations` and `OrderItem.SelectedVariations` are typed `IReadOnlyList<>` of records on the aggregate; the jsonb-string parser in `OrderExtensions` is removed.
- [ ] R.2: a fresh-`Orderdb` migration lands without data loss; an existing-data migration script (in-place option) or a dual-write column (option A) preserves the existing rows.
- [ ] `docs/architecture/current-architecture.md` reflects both items: §5.2 event table gains `KitchenOrderPrepStartedIntegrationEvent`; §4.5 Ordering service's Aggregate behaviour bullet notes `OrderItem.Customizations` / `SelectedVariations` are typed records; §6 data stores no longer references the jsonb-string shape.

---

## 6. Suggested execution order

1. **R.1** first — the wiring is mechanical (new event, new consumer, two new tests) and the predicate ("first item started") already exists on the aggregate. The kitchen UI gains a real production path for `Order.MarkPreparing`.
2. **R.2** second — the schema migration is the heavier item and benefits from a clean checkout (no in-flight changes to the jsonb-parse path). R.2's removal of `DeserializeVariations` / `DeserializeCustomizations` is straightforward once R.1 is committed and the code is otherwise quiet.

---

## 7. Open questions / decisions — 2026-07-10

1. **R.1 — per-item granularity vs per-ticket "first item started".** The proposed R.1 emits the event on the first item-start action, not on every item. Confirm: is the kitchen display's notion of "the order has begun preparing" the right signal, or should the event fire on every item-start and let Ordering apply `MarkPreparing` only on the first? (Ordering's `MarkPreparing` already throws on a second call, so either is safe; the per-first signal is more chatty on the wire but more granular on the UI side.) Recommend: per-first — matches the `Order` aggregate's notion of "is in Preparing?".
2. **R.2 — dual-write column A vs in-place shape change B.** Option A is safer; option B is cheaper. If the team is comfortable with a one-time data-shape migration, B ships in a single migration. If not, A keeps the rollout reversible. Recommend: option A — the jsonb-string column is dropped in a follow-up migration once dual-write is verified in production.
3. **R.2 — EF Core jsonb value converter.** The `IReadOnlyList<KitchenOrderItemCustomization>` property needs a jsonb converter. The existing `BuildingBlocks.Persistence` (if present) or a local converter on the entity is fine. Resolve during implementation; not a research question, just a file-organization one.
4. **R.1 — emit from the application handler, not the aggregate.** The aggregate's domain event (`KitchenTicketItemPrepStartedEvent`) is in-process; the integration event is a cross-service contract. Keep the application handler in `KitchenTickets/Commands/StartItemPrep.cs` as the publish point so the aggregate stays broker-agnostic.
