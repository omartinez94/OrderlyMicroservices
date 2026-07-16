# DB model drift report — code as source of truth

> **Generated:** 2026-07-15
> **Scope:** `ORDER_ACTIVITY_PLAN.md` (v0.3) — new `OrderActivity` child entity + table on the Ordering service. Drift confined to the Ordering relational block; other services are out of scope.
> **Method:** read the plan + plan-referenced code (`Ordering.Domain/Models/Order.cs`, `Ordering.Domain/Models/OrderItem.cs`, `Ordering.Infrastructure/Data/Configurations/OrderConfiguration.cs`), then diff against the current `db_relational_model.mermaid`. Two-direction analysis per the project convention (`mermaid-code-review-convention`).
>
> Companion report: `db_model_drift_report_mermaid_truth.md` (mermaid as source of truth, same scope).

---

## Legend

| Symbol | Meaning |
|---|---|
| ✅ match | Code / plan and mermaid agree |
| ❌ must-add to mermaid | Mermaid lacks an entity / column / relationship the plan introduces |
| ⚠️ unmodeled extra | Plan introduces something the mermaid convention doesn't have a slot for (resolution needed) |
| 🟡 cosmetic | Description / comment drift, no schema impact |
| 🔁 relationship gap | Column exists but the FK edge is missing or wrong |

---

## Section A — `OrderActivities` (new entity, plan-only)

The plan introduces a new child entity. The mermaid has been updated in this pass; this section documents the diff for traceability.

| Item | Plan | Mermaid (before this pass) | Status |
|---|---|---|---|
| `OrderActivities` entity block | New relational table | Missing | ❌ → ✅ **fixed** (block added in this pass; see `db_relational_model.mermaid` ORDERING section) |
| `Id uuid PK` | `OrderActivityId` value object | n/a | ✅ (renders as `uuid` per the convention on line 26 of the companion doc) |
| `OrderId uuid FK` with `ON DELETE CASCADE` | FK → `Orders.Id` | n/a | ✅ (documented as `FK` + cascade-delete comment) |
| `ActivityType string nvarchar(50)` | Stored as string via `HasConversion<string>()` | n/a | ✅ (comment mirrors `OrderStatus` pattern at `OrderConfiguration.cs:58`) |
| `ActorUserId uuid?` | Optional | n/a | ✅ |
| `OccurredAt timestamp` | Required, indexed | n/a | ✅ |
| `CorrelationId string nvarchar(100) NULL` | From `BuildingBlocks.Correlation.CorrelationContext.Current` | n/a | ✅ (BuildingBlocks contribution noted in mermaid comment) |
| `Notes string nvarchar(2000) NULL` | Only populated on cancellation today | n/a | ✅ |
| `Metadata text NULL` (jsonb column) | Typed `OrderActivityMetadata` record | n/a | ✅ (sub-shape comment block documents the 9-field record) |
| `IX_order_activities_OrderId_OccurredAt` covering index | Configured in `OrderActivityConfiguration` | n/a | ✅ (added as inline comment block + `db_relational_model.md` updates section) |
| Relationship `Orders \|\|--o{ OrderActivities : "audit-trail of"` | Plan §4 service boundaries | n/a | ❌ → ✅ **fixed** (relationship line added in this pass) |

**Verdict:** Section A is **fully reconciled**. The mermaid now matches the plan for the new entity. No outstanding must-add items for `OrderActivities`.

---

## Section B — `Orders` aggregate (pre-existing drift, NOT caused by this plan)

The mermaid's `Orders` block (lines 382-422 of the pre-pass mermaid) is missing **8 snapshot columns** that exist in `Ordering.Domain/Models/Order.cs` and are unaffected by this plan. These columns are part of the "activity-lifecycle-snapshot" pattern that the new `OrderActivity` table complements (does not replace). Flagged here per the convention's "persist findings" rule — these need an Ordering-cleanup pass that is out of scope for the activity-feed plan.

| Item | Code (`Order.cs`) | Mermaid | Status |
|---|---|---|---|
| `ConfirmedAt Instant?` (line 39) | ✅ present | ❌ missing | ❌ must-add |
| `CancelledAt Instant?` (line 35) | ✅ present | ❌ missing | ❌ must-add |
| `CancelledByUserId Guid?` (line 36) | ✅ present | ❌ missing | ❌ must-add |
| `CancellationReason string?` (line 34) | ✅ present | ❌ missing | ❌ must-add |
| `PreparingStartedAt Instant?` (line 46) | ✅ present | ❌ missing | ❌ must-add |
| `ReadyAt Instant?` (line 47) | ✅ present | ❌ missing | ❌ must-add |
| `DeliveredAt Instant?` (line 42) | ✅ present | ❌ missing | ❌ must-add |
| `CompletedAt Instant?` (line 37) | ✅ present | ❌ missing | ❌ must-add |

**Verdict:** 8 missing snapshot columns on `Orders`. **Pre-existing drift.** None of these are changed by the activity-feed plan; the activity feed is **additive** and complements the snapshot, not replaces it. The mermaid block on lines 382-422 needs an Ordering-cleanup pass.

**Why this matters for the activity-feed plan:** the FE can already see the snapshot timestamps (`OrderDto.ConfirmedAt`, `OrderDto.CancelledAt`, etc., per `Ordering.Application/Dtos/OrderDto.cs:49-55`). The activity feed adds a *chronological list of typed events* with actor + correlation. A future cleanup pass should add these columns to the mermaid so the snapshot story is complete; the activity feed then becomes the primary UI element, and the snapshot columns become the "last-known-state" projection of the same data.

---

## Section C — `Orders` audit fields comment drift (pre-existing)

The mermaid's comment on the `Orders` block (lines 417-421 of the pre-pass mermaid) says:

> %% Audit fields from Ordering.Domain.Aggregate<OrderId>:
> %% CreatedBy? (Guid?), CreatedAt? (Instant?),
> %% LastModified? (Instant?), LastModifiedBy? (Guid?).

This is **misleading** in two ways:

1. The mermaid implies `Order` inherits `CreatedAt` / `LastModifiedAt` from `Aggregate<OrderId>` (which extends `Entity<T>`). The `Order.cs` code does NOT declare these columns. The `CreatedByUserId` field on line 41 of `Order.cs` is a different field (explicitly declared on the aggregate, not inherited).
2. `db_relational_model.md` line 92 says `Entity<T>` is "Used by: `OrderItem`, `OrderBill`, and the value-object `MenuItem`" — NOT `Order`. So the audit-field claim in the mermaid's comment is at odds with the companion doc's own statement of which entities use `Entity<T>`.

**Verdict:** 🟡 cosmetic but worth fixing in a cleanup pass. The mermaid comment block should either (a) be deleted (since `Order` does not actually carry these columns), or (b) be replaced with a comment that says "Order does NOT inherit audit fields from Entity<T>; only CreatedByUserId is declared explicitly."

---

## Section D — `OrderItem` parent back-reference (plan introduces)

The plan adds `internal Order Parent { get; set; } = default!;` to `OrderItem.cs` (line 5-78) so that `MarkItemPreparing` / `MarkItemReady` can call back to `Order.RecordActivity`. This is a **domain-layer back-reference** — not a database column. No mermaid update needed (the FK `OrderItems.OrderId → Orders.Id` already exists at line 425 of the mermaid).

| Item | Plan | Mermaid | Status |
|---|---|---|---|
| `OrderItem.Parent : Order` (internal, domain back-ref) | Plan §6.1 Domain commit | n/a (domain object, not a column) | ✅ (no mermaid update needed) |
| `OrderConfiguration` gains `HasMany(o => o.Activities)` | Plan §6.1 Infrastructure commit | Mermaid now shows `Orders \|\|--o{ OrderActivities` (added in this pass) | ✅ |

**Verdict:** Section D is fully reconciled.

---

## Section E — Conventions (no drift, just locked-in reminders)

For traceability, the plan follows every convention in `db_relational_model.md`:

- ✅ **Strongly-typed value objects render as primitives** (line 26 of the companion doc). `OrderActivityId` renders as `uuid` in the mermaid.
- ✅ **Cascade delete is configured in code, not modelled** (line 135 of the companion doc: "The diagram shows the shape, not the delete behavior"). `ON DELETE CASCADE` is documented as a comment on the `OrderId FK` column, matching the pattern at `OrderConfiguration.cs:14`.
- ✅ **Audit-FK edges are flat columns, not navigation** (line 139 of the companion doc). `OrderActivity.ActorUserId` is rendered as a flat `uuid` column with no FK edge to `Users`.
- ✅ **Indexes are not modelled as separate entities** (line 141 of the companion doc: "composite and unique indexes are configured in ... configurations. Not modelled here"). The `IX_order_activities_OrderId_OccurredAt` index is documented as an inline comment block on `OrderItems` (the closest entity), matching the convention.

---

## P0 prioritized list — must-update mermaid

| Priority | Item | Action | Status |
|---|---|---|---|
| **P0-1** | Add `OrderActivities` entity block to the ORDERING relational section | Add the 9-column block (id, orderId, activityType, actorUserId, occurredAt, correlationId, notes, metadata) with cascade-delete FK to `Orders.Id` | ✅ **DONE in this pass** |
| **P0-2** | Add `Orders \|\|--o{ OrderActivities : "audit-trail of"` relationship | Append after `Orders \|\|--o{ OrderBills : "split into"` in the relationships section | ✅ **DONE in this pass** |
| **P0-3** | Document the `IX_order_activities_OrderId_OccurredAt` covering index | Inline comment block on the closest entity (`OrderItems`, since the index lives in the `order_activities` table) + cross-reference to `ORDER_ACTIVITY_PLAN.md §0.5` | ✅ **DONE in this pass** |
| **P0-4** | Update `db_relational_model.md` companion doc "Last reconciled" date + plan-update section | Add a new "Updates from `ORDER_ACTIVITY_PLAN.md` (v0.3, 2026-07-15)" section | ✅ **DONE in this pass** |
| **P1-1** | Add 8 missing snapshot columns to the `Orders` block (`ConfirmedAt`, `CancelledAt`, `CancelledByUserId`, `CancellationReason`, `PreparingStartedAt`, `ReadyAt`, `DeliveredAt`, `CompletedAt`) | Ordering-cleanup pass — **out of scope for the activity-feed plan**; open as a separate drift issue | ⚠️ **OPEN** |
| **P1-2** | Fix the `Orders` audit-fields comment block to either delete or correct the misleading "inherited from `Aggregate<OrderId>`" claim | Ordering-cleanup pass | ⚠️ **OPEN** |

---

## Sign-off

The mermaid is now reconciled with the plan for the new `OrderActivity` entity. The 2 open P1 items are pre-existing drift on `Orders` that the activity-feed plan does NOT touch; they're flagged for a future Ordering-cleanup pass.

The activity-feed plan is **safe to implement** against the current mermaid + companion doc. The next reviewer of either file should re-read this report to understand what was reconciled in this pass.