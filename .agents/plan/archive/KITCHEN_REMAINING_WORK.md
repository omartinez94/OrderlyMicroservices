# Kitchen — Remaining Work

> Scope: the two items that survived the M0–M5 / F.1–F.6 / G follow-up cycle. Captures the next small batch of work after the existing plans in this directory close.

> **Status: ✅ COMPLETED — 2026-07-17**
>
> Both R.1 and R.2 shipped. Acceptance criteria at the bottom are all `[x]`. The two items are no longer "remaining work" — the plan is now a historical record of the design + a closed-acceptance checklist.
>
> **Verification date:** 2026-07-17. 153 domain tests + 41 application tests still pass (the R.1 consumer test is among the 41); the 5 `StartItemPrepHandlerTests` cover the kitchen-side R.1 predicate; the `TypedOrderItemCustomizationsJsonb` migration is in `__EFMigrationsHistory` on dev.
>
> **Evidence (verified 2026-07-17):**
>
> - **R.1** — `BuildingBlocks.Messaging/Events/KitchenOrderPrepStartedIntegrationEvent.cs` carries `{ OrderId, ItemId, StaffUserId, StartedAt }`. `Services/Kitchen/Kitchen.API/Application/KitchenTickets/Commands/StartItemPrep.cs` captures the `firstItemStarted = ticket.StartedAt is null` predicate **before** mutating the aggregate, then publishes via `IPublishEndpoint` after `SaveChangesAsync` only on the first-item action. `Services/Ordering/Ordering.Application/Orders/EventHandlers/Integration/KitchenOrderPrepStartedIntegrationEventHandler.cs` calls `Order.MarkPreparing(message.StartedAt)`; sets / clears `CorrelationContext` per `ORDER_ACTIVITY_PLAN.md` §0.6. 3 handler tests in `Ordering.Application.Tests/EventHandlers/Integration/KitchenOrderPrepStartedIntegrationEventHandlerTests.cs` (happy + unknown-order skip + duplicate-delivery throws). 5 kitchen-side tests in `Kitchen.API.Tests/Commands/StartItemPrepHandlerTests.cs` (publish-once on first start, no-publish on second, integration on same aggregate, unauthenticated throws, missing ticket throws).
> - **R.2** — `Services/Ordering/Ordering.Domain/Models/OrderItem.cs::Customizations` is `IReadOnlyList<KitchenOrderItemCustomization>` and `SelectedVariations` is `IReadOnlyList<KitchenOrderItemVariation>`; default `Array.Empty<>()`. `Services/Ordering/Ordering.Application/Extensions/OrderExtensions.cs::MapItem` passes the typed records through directly — the Phase-D `DeserializeVariations` / `DeserializeCustomizations` jsonb parser is gone. EF migration `20260710233247_TypedOrderItemCustomizationsJsonb` is empty at the SQL level (the `System.Text.Json`-backed value converter on `OrderItemConfiguration` handles the jsonb column; aggregate is source of truth). `OrderExtensionsPhaseDTests` covers the typed round-trip.
> - **Docs** — `docs/architecture/current-architecture.md` carries the R.1 row in §5.2 (line 502), the `MarkPreparing` line in §4.5 state-transition methods (line 400, names `KitchenOrderPrepStartedIntegrationEventHandler` as the production driver), the consumer row in §4.5 (line 444, documents the emit-once predicate), the `KitchenOrderPrepStartedIntegrationEvent` payload block in §5.2 (lines 513–514), and the R.2 typed-properties line in §4.5 OrderItem behaviour (line 408). Tests inventory in §11 line 622 names both the new handler test and the `OrderExtensionsPhaseDTests` typed round-trip coverage.

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

- [x] R.1: `KitchenOrderPrepStartedIntegrationEvent` is emitted by `Kitchen.API` exactly once per ticket (on the first item-start action) and consumed by `Ordering.Application` to drive `Order.MarkPreparing`. The REST endpoint on Ordering remains as a manual override.
- [x] R.1: `KitchenOrderPrepStartedIntegrationEventHandlerTests` (Ordering.Application.Tests) and the kitchen-side predicate coverage in `StartItemPrepHandlerTests` (Kitchen.API.Tests/Commands/) pass.
- [x] R.2: `OrderItem.Customizations` and `OrderItem.SelectedVariations` are typed `IReadOnlyList<>` of records on the aggregate; the jsonb-string parser in `OrderExtensions` is removed.
- [x] R.2: the `TypedOrderItemCustomizationsJsonb` migration lands without data shape change (empty SQL — value converter handles the jsonb column); pre-existing rows hold the Phase D `{ Name, Price }` / `{ Label, Value, Price }` shapes which round-trip correctly through the new value converter.
- [x] `docs/architecture/current-architecture.md` reflects both items: §5.2 event table carries `KitchenOrderPrepStartedIntegrationEvent` (line 502 + payload block lines 513–514); §4.5 Ordering service's Aggregate behaviour bullet names `MarkPreparing`'s production driver (line 400); §4.5 OrderItem behaviour notes `OrderItem.Customizations` / `SelectedVariations` are typed records (line 408); §6 data stores does not reference the jsonb-string shape.

---

## 6. Suggested execution order

1. **R.1** first — the wiring is mechanical (new event, new consumer, two new tests) and the predicate ("first item started") already exists on the aggregate. The kitchen UI gains a real production path for `Order.MarkPreparing`.
2. **R.2** second — the schema migration is the heavier item and benefits from a clean checkout (no in-flight changes to the jsonb-parse path). R.2's removal of `DeserializeVariations` / `DeserializeCustomizations` is straightforward once R.1 is committed and the code is otherwise quiet.

---

## 7. Open questions / decisions — resolved 2026-07-17

1. **R.1 — per-item granularity vs per-ticket "first item started" — RESOLVED (per-first adopted).** The shipped `StartItemPrepHandler` captures `firstItemStarted = ticket.StartedAt is null` **before** mutating the aggregate, then publishes via `IPublishEndpoint` only on the first-item action. Subsequent item-starts on the same ticket publish nothing — the predicate lives on the aggregate, not on transient handler state (proven by `Handle_TwoItemStartsOnSameTicket_PublishExactlyOnce`). Ordering's `MarkPreparing` is idempotent in effect (throws on a second call → surfaces as a nack + retry, which becomes a no-op once the order is already in `Preparing`).
2. **R.2 — dual-write column A vs in-place shape change B — RESOLVED (in-place adopted, by way of value converter).** The shipped `TypedOrderItemCustomizationsJsonb` migration is empty at the SQL level: the `nvarchar(max)` jsonb column type stays; the `System.Text.Json`-backed value converter on `OrderItemConfiguration` handles the typed `IReadOnlyList<>` ↔ jsonb serialization. This is effectively option B (single migration, no dual-write column), justified because the Phase D records (`KitchenOrderItemCustomization { Label, Value?, Price? }` and `KitchenOrderItemVariation { Name, Price }`) are the same wire-shape records the basket/checkout payload already carries — there is no "legacy string-shape data" to preserve. Pre-existing dev rows that hold the Phase D shape round-trip cleanly; rows that held the legacy `string[]` shape (if any survive in any environment) deserialise to empty lists at read time, which is acceptable because no wire payload produces that shape today.
3. **R.2 — EF Core jsonb value converter — RESOLVED.** The converter lives on `OrderItemConfiguration` (per-property `HasConversion(...)` wrapping `System.Text.Json`); no BuildingBlocks primitive was needed. The shared `JsonSerializerOptions` lives at `Ordering.Infrastructure/Serialization/OrderActivityJson.cs` (shipped for the activity feed) — the value converter on `OrderItem.Customizations` / `SelectedVariations` reuses the same serialiser so enum-string encoding stays consistent across the two jsonb surfaces.
4. **R.1 — emit from the application handler, not the aggregate — RESOLVED (handler adopted).** The aggregate's domain event (`KitchenTicketItemPrepStartedEvent`) is in-process; the integration event is a cross-service contract. `StartItemPrepHandler` (in `KitchenTickets/Commands/StartItemPrep.cs`) is the publish point — the aggregate stays broker-agnostic. The `firstItemStarted` predicate lives on the aggregate (`StartedAt is null` pre-call) but the publish decision is the handler's, not the aggregate's.

---

**Document Version:** 1.0 (R.1 + R.2 shipped 2026-07-17)
**Last Updated:** 2026-07-17
**Maintained By:** Kitchen working group
**Status:** Both items complete. R.1 — `KitchenOrderPrepStartedIntegrationEvent` is live on the bus, consumed by `Ordering.Application`, and covered by 8 tests across the two projects. R.2 — `OrderItem.Customizations` / `SelectedVariations` are typed `IReadOnlyList<>` of records on the aggregate, the jsonb-string parser in `OrderExtensions` is gone, and the `TypedOrderItemCustomizationsJsonb` migration lands cleanly against a fresh dev DB. All §5 acceptance checkboxes `[x]`. All §7 open questions resolved with rationale.

> **v1.0 changelog (2026-07-17) — R.1 + R.2 ship.**

- **R.1 — `KitchenOrderPrepStartedIntegrationEvent` end-to-end.** New event in `BuildingBlocks.Messaging/Events/`, payload `{ OrderId, ItemId, StaffUserId, StartedAt }`. `Kitchen.API/Application/KitchenTickets/Commands/StartItemPrep.cs` publishes via `IPublishEndpoint` after `SaveChangesAsync` exactly once per ticket (predicate: `ticket.StartedAt is null` captured **before** the aggregate mutates). `Ordering.Application/Orders/EventHandlers/Integration/KitchenOrderPrepStartedIntegrationEventHandler.cs` drives `Order.MarkPreparing(message.StartedAt)`; sets / clears `CorrelationContext` per `ORDER_ACTIVITY_PLAN.md` §0.6. The handler is the production path; the `POST /orders/{id}/start-prep` REST endpoint remains as a manual override.
  - **Tests (3 in Ordering.Application.Tests, 5 in Kitchen.API.Tests):** happy path (Confirmed → Preparing), unknown-order skip, duplicate-delivery throws (broker nacks for retry). Kitchen-side: first-start publishes, second-start does not, two-starts-on-same-aggregate publishes exactly once (the predicate lives on the aggregate), unauthenticated throws, missing ticket throws.
- **R.2 — `OrderItem.Customizations` / `SelectedVariations` typed jsonb.** Aggregate properties are now `IReadOnlyList<KitchenOrderItemCustomization>` / `IReadOnlyList<KitchenOrderItemVariation>`. EF migration `20260710233247_TypedOrderItemCustomizationsJsonb` is empty at the SQL level — the value converter on `OrderItemConfiguration` handles the jsonb column; no schema data shape change. `OrderExtensions.MapItem` passes the typed records through directly; the Phase D `DeserializeVariations` / `DeserializeCustomizations` jsonb parser is gone. Pre-existing rows that hold the Phase D shape round-trip correctly. `OrderExtensionsPhaseDTests` covers the typed round-trip path.
- **Docs — `current-architecture.md` carries both items.** §4.5 Ordering service: `MarkPreparing` line names `KitchenOrderPrepStartedIntegrationEventHandler` as the production driver (line 400); OrderItem behaviour line documents typed `IReadOnlyList<KitchenOrderItemCustomization>` / `IReadOnlyList<KitchenOrderItemVariation>` (line 408). §4.5 cross-service consumer row: `KitchenOrderPrepStartedIntegrationEventHandler` with the emit-once predicate documented (line 444). §5.2 Asynchronous table: row for `KitchenOrderPrepStartedIntegrationEvent` (line 502) + payload block (lines 513–514). §11 Tests: the handler test + the `OrderExtensionsPhaseDTests` typed round-trip coverage (line 622).
- **No code changes ship with this plan update.** All work landed in earlier commits (the KITCHEN_FOLLOWUP_PLAN.md Phase B referenced the consumer placeholder, then the typed-records Phase D landed; R.1 + R.2 followed in the R-series of small commits referenced by the v2.7 Ordering cleanup + KITCHEN_FOLLOWUP_PLAN.md §8 open-questions surface). This plan-update commit only flips the plan from "future work" to "historical record" — Document Version `0.1 → 1.0`, §5 boxes `[ ]` → `[x]`, §7 questions resolved with rationale, status banner added at the top.
- **Cross-service impact:** none. The integration event is a wire-level additive change (`BuildingBlocks.Messaging` is the source-of-truth namespace); no consumer outside Ordering cares about the new event; the aggregate typing is internal to Ordering.Infrastructure + Ordering.Application and does not affect the wire payload shape on `OrderCreatedIntegrationEvent` (which already carried typed `KitchenOrderItemPreview[]` per Phase D). The `db_relational_model.mermaid` `OrderItems` jsonb sub-shapes were updated retroactively per `ORDERING_CLEANUP_BACKLOG.md` Phase C (shipped 2026-07-16).
