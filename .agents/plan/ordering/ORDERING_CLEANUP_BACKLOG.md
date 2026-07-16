# Ordering — Cleanup Backlog (follow-up to `ORDER_ACTIVITY_PLAN.md`)

> **Scope:** the Ordering-specific P1 items surfaced by the `db_model_drift_report.md` / `db_model_drift_report_mermaid_truth.md` two-direction review on 2026-07-15. These are **pre-existing drift** that the activity-feed plan does NOT touch — flagged here so they don't get lost when the activity feed lands.
>
> **Origin:** synthesized from `docs/architecture/db_model_drift_report.md` §B + §C and `docs/architecture/db_model_drift_report_mermaid_truth.md` §C. The activity-feed plan is **safe to implement against the current mermaid**; this backlog is for a future Ordering-cleanup pass that runs **after** the activity feed ships.
>
> **Sibling reference:** `KITCHEN_FOLLOWUP_PLAN.md` — same template, narrower scope.

---

## 0. Skill & documentation conventions

### 0.1 Skill mandate — `csharp-developer`

> **All implementation work on this backlog MUST invoke the `csharp-developer` skill** (base directory `.claude/skills/csharp-developer`, invoked as `/csharp-developer` in Claude Code).
>
> This backlog touches **only the mermaid diagram + companion doc** in this revision — no code changes are required for the P1 items themselves. If a future cleanup pass discovers that the missing snapshot columns on `Orders` are themselves missing from the EF Core configuration (not just the diagram), the implementer promotes the affected items to a code-touching plan and invokes the skill at that point.

### 0.2 Phase-completion documentation update

> **After completing every phase, `docs/architecture/current-architecture.md` MUST be updated to reflect the new state of the codebase before the implementation commit is finalized.**
>
> For this backlog the recurring touch points are:
>
> | Doc section | Why it changes per phase |
> |---|---|
> | §4.5 Ordering Service | If a code-level cleanup is required (e.g. an `Order` aggregate column that's drifted away from the EF Core configuration), the §4.5 column list gains the corrected shape. |
> | `docs/architecture/db_relational_model.mermaid` | All three phases add or correct entity blocks / relationship lines / comment blocks. |
> | `docs/architecture/db_relational_model.md` | "Last reconciled to code" date is bumped + a new "Updates from `ORDERING_CLEANUP_BACKLOG.md` (Phase N, YYYY-MM-DD)" section is added. |

---

## 1. Context

The two drift reports dated 2026-07-15 (`docs/architecture/db_model_drift_report.md`, `..._mermaid_truth.md`) surfaced three items that the activity-feed plan does NOT resolve:

| Item | Source | Severity | Code impact |
|---|---|---|---|
| **P1-1**: 8 missing snapshot columns on the `Orders` mermaid block | `db_model_drift_report.md` §B | P1 — pre-existing drift | None today; future schema risk |
| **P1-2**: Misleading `Orders` audit-fields comment block | `db_model_drift_report.md` §C | P1 — pre-existing drift | None today; doc-only |
| **Convention gap C.1**: Relational jsonb sub-shape not modelled in the mermaid convention | `db_model_drift_report_mermaid_truth.md` §C | P1 — convention revision | Mermaid-convention doc only |

The activity-feed plan is safe to ship against the current mermaid + companion doc. This backlog captures the cleanup so a future Ordering-cleanup pass picks it up without re-deriving the findings.

---

## 2. Goal

Three things, in this order:

1. **Add the 8 missing snapshot columns to the `Orders` block in `db_relational_model.mermaid`** — `ConfirmedAt`, `CancelledAt`, `CancelledByUserId`, `CancellationReason`, `PreparingStartedAt`, `ReadyAt`, `DeliveredAt`, `CompletedAt`. All `timestamp?` or `uuid?` nullable. Document the `ConfirmedByUserId` + `CompletedByUserId` columns that are already on the mermaid block so the symmetry is preserved.
2. **Fix the `Orders` audit-fields comment block** to either delete it or replace it with a note that `Order` does NOT inherit `CreatedAt` / `LastModifiedAt` from `Entity<T>` (the companion doc line 92 explicitly lists `Entity<T>` users as `OrderItem`, `OrderBill`, `MenuItem` — NOT `Order`). The aggregate only carries `CreatedByUserId` as an explicitly-declared field.
3. **Extend the mermaid convention** for relational jsonb sub-shapes (option 2 from `db_model_drift_report_mermaid_truth.md` §C.1). Document the chosen format in `db_relational_model.md` so future plans can use it without re-litigating.

---

## 3. Out of scope

- **Code-level cleanup of the `Order` aggregate.** The aggregate's columns are correct (verified against `Ordering.Domain/Models/Order.cs:34-47` in the activity-feed plan's pre-flight review). The drift is **diagram-only** — the missing columns exist in code, the mermaid just doesn't render them. If a future code-touching cleanup pass discovers that an aggregate column has drifted from the EF Core configuration (e.g. a property exists in `Order.cs` but no `builder.Property(o => o.X)` call exists in `OrderConfiguration.cs`), that becomes a separate code-touching plan.
- **Activity-feed-plan items.** This backlog does NOT revisit `OrderActivity`, the migration, the JSON serialization, the correlation context, or any of the activity-feed plan's locked decisions.
- **The Catalog Marten document cleanup.** The cleanup of `OrderSnapshot` / `OrderModificationLog` / `OrderItemPriceAudit` is a Catalog concern (different service, different schema), not an Ordering-cleanup item.
- **A new mermaid-convention revision for non-jsonb sub-shapes.** Only relational jsonb gets the new sub-shape convention; embedded value-object columns (`Order.BillingAddress`, `Order.DeliveryAddress`, `Order.Payment` — all `ComplexProperty`) keep their existing rendering.

---

## 4. Phased milestones

### Phase A — Reconcile the `Orders` block (diagram-only)

A single commit touching `docs/architecture/db_relational_model.mermaid` + the companion doc.

**File touched:**

- `docs/architecture/db_relational_model.mermaid` — `Orders` block (lines 382-422 pre-pass). Add the 8 missing columns in the order they appear on the aggregate (`Ordering.Domain/Models/Order.cs`):
  - `ConfirmedAt Instant?` — between `ApprovedByAdminId` and `ApprovedAt` (mirror the existing snapshot-column cluster)
  - `PreparingStartedAt Instant?` — after `ApprovedAt`
  - `ReadyAt Instant?` — after `PreparingStartedAt`
  - `DeliveredAt Instant?` — after `ReadyAt`
  - `CompletedAt Instant?` — after `DeliveredAt`
  - `CancelledAt Instant?` — after `CompletedAt`
  - `CancelledByUserId Guid?` — after `CancelledAt`
  - `CancellationReason string?` — after `CancelledByUserId`
- `docs/architecture/db_relational_model.md` — bump the "Last reconciled to code" date + add a new section: "Updates from `ORDERING_CLEANUP_BACKLOG.md` (Phase A, 2026-MM-DD): added 8 missing `Orders` snapshot columns".

**Rules:**

- **No code changes.** The aggregate already has these columns; only the diagram lacks them. A code change would expand this backlog into a code-touching plan.
- **No new FK edges.** `CancelledByUserId` / `ConfirmedByUserId` / `CompletedByUserId` / `CreatedByUserId` / `ApprovedByAdminId` are all flat `uuid` columns with no FK to `Users` (per the convention at `db_relational_model.md` line 139 — "Audit-FK edges are flat columns, not navigation"). The diagram continues to render them as flat columns.
- **Order the new columns to match the aggregate.** The activity-feed plan's transition callout table (§6.1 Domain commit) reads `Order.cs` top-to-bottom and uses the column order to anchor the type-pairs; the mermaid should match.
- **`CancellationReason` is `string?`, not `text`.** The `CancellationReason` on `Order.cs:34` is a `string?` (default `null`). The mermaid renders it as `string CancellationReason "Reason text"`. If a future review finds it needs to be longer than 2000 chars (matching `OrderActivity.Notes`), promote it to `text` then.

**Acceptance:**

- [ ] The mermaid's `Orders` block has all 8 new columns.
- [ ] The companion doc's "Last reconciled" date is bumped; the Phase A section is added.
- [ ] `git diff docs/architecture/db_relational_model.mermaid` shows ONLY the `Orders` block changes + the new `Orders` block contents; no other entity touched.
- [ ] No code files in `Services/Ordering/` are modified.

### Phase B — Fix the `Orders` audit-fields comment block

A single commit touching only the mermaid comment block.

**File touched:**

- `docs/architecture/db_relational_model.mermaid` — replace the misleading comment block (lines 417-421 pre-pass) with one of two options:
  - **Option B.1 (recommended — delete):** remove the comment block entirely. `Order` has no inherited audit fields; the only audit-style column it has is `CreatedByUserId` which is already a documented `uuid` column. The comment was confusing at best, wrong at worst.
  - **Option B.2 (correct in place):** replace with a 2-line note: "`Order` does NOT inherit audit fields from `Entity<T>` (the `Entity<T>` base is not used by `Order` — see `db_relational_model.md` line 92). The `CreatedByUserId` column is explicitly declared on the aggregate, not inherited."
- `docs/architecture/db_relational_model.md` — add a "Updates from `ORDERING_CLEANUP_BACKLOG.md` (Phase B, 2026-MM-DD): corrected `Orders` audit-fields comment" section.

**Rules:**

- **Pick one option, document the choice.** The Phase B commit message records which option was chosen and why. If Option B.1 is chosen, the commit message includes the verbatim `db_model_drift_report.md` §C quote so the rationale is durable.
- **No "audit columns from base" rendering** — the `BulkOrderUploads` block at line 280 still renders inherited audit columns (because `BulkOrderUploads : AuditableEntity<int>` after the Phase 4 fix). The `Orders` block does NOT inherit them; the comment should not pretend otherwise.

**Acceptance:**

- [ ] The `Orders` comment block no longer claims `Order` inherits `CreatedAt` / `LastModifiedAt` from `Aggregate<OrderId>`.
- [ ] The companion doc's "Last reconciled" date is bumped; the Phase B section is added.
- [ ] `git diff docs/architecture/db_relational_model.mermaid` shows ONLY the `Orders` comment-block change.
- [ ] No code files in `Services/Ordering/` are modified.

### Phase C — Extend the mermaid convention for relational jsonb sub-shapes

A single commit touching the companion doc (no mermaid entity changes — the convention is *about* the diagram, not a diagram edit).

**File touched:**

- `docs/architecture/db_relational_model.md` — add a new section under "Type mappings" or a new top-level "Convention: relational jsonb sub-shapes" section. Document the chosen format:

  ```text
  %% jsonb columns on relational tables MAY carry an optional sub-shape
  %% comment block of the form:
  %%   jsonb <Field> "Short description"
  %%     %% <SubField1>: <Type> "Comment"
  %%     %% <SubField2>: <Type> "Comment"
  %%     ...
  %% The sub-shape is informational only — it documents the typed record
  %% shape stored in the column. The convention does NOT require a sub-shape
  %% for every jsonb column; simple jsonb columns (e.g. a List<string>)
  %% continue to render as `jsonb <Field> "<description>"` with no sub-shape.
  ```

- `docs/architecture/db_relational_model.mermaid` — apply the new convention retroactively to the existing relational jsonb columns that have a typed shape:
  - `OrderItems.SelectedVariations` (line 435) — add sub-shape documenting the `KitchenOrderItemVariation[]` record (`Name`, `Value`, `Price`).
  - `OrderItems.Customizations` (line 436) — add sub-shape documenting the `KitchenOrderItemCustomization[]` record (`Ingredient`, `Action`).
  - `OrderActivities.Metadata` (the new block from the activity-feed plan) — leave as-is (comment-only); the convention is permissive, not mandatory.

**Rules:**

- **The convention is permissive, not mandatory.** Simple jsonb columns (e.g. `BulkOrderUploads.ErrorLog`) keep their `jsonb <Field> "<description>"` rendering without a sub-shape. The new convention is an opt-in for typed-shape columns.
- **Sub-shape is informational.** The convention explicitly states that the sub-shape does NOT introduce a new entity in the diagram. The typed record is a value object, not a navigable entity.
- **No Marten-document retrofit.** Marten documents already have a sub-shape convention (line 38 of the companion doc). This new convention is for **relational jsonb columns** only. The two are parallel, not merged.

**Acceptance:**

- [ ] The companion doc carries the new "Convention: relational jsonb sub-shapes" section.
- [ ] The mermaid retroactively applies the convention to `OrderItems.SelectedVariations` + `OrderItems.Customizations` (the two existing relational jsonb columns with typed shapes).
- [ ] `git diff docs/architecture/db_relational_model.mermaid` shows ONLY the two `OrderItems` column-comment changes.
- [ ] No code files in `Services/Ordering/` are modified.

---

## 5. Cross-service notes

- **Catalog** — out of scope. Catalog's three Marten documents (`OrderSnapshot`, `OrderModificationLog`, `OrderItemPriceAudit`) are independent of the Ordering block; this backlog does not touch the Catalog section of the mermaid.
- **Kitchen / Basket / Discount / Identity** — out of scope. This backlog touches only the Ordering relational section of the mermaid + the companion doc.
- **BuildingBlocks** — out of scope. No new primitives are introduced. The activity-feed plan's `CorrelationContext` is unchanged by this backlog.

---

## 6. Milestone checklist

- [ ] **Phase A — `Orders` block reconciled** — 8 missing snapshot columns added to `db_relational_model.mermaid`. Companion doc "Last reconciled" date bumped + new Phase A section. No code changes. Diff is `Orders`-block-only.
- [ ] **Phase B — Audit-fields comment block fixed** — misleading comment replaced with Option B.1 (delete) or Option B.2 (corrected in place). Companion doc "Last reconciled" date bumped + new Phase B section. No code changes.
- [ ] **Phase C — Relational jsonb sub-shape convention** — companion doc carries the new convention section. Mermaid retroactively applies the convention to `OrderItems.SelectedVariations` + `OrderItems.Customizations`. No code changes.
- [ ] **Verification** — re-run `db_model_drift_report.md` / `db_model_drift_report_mermaid_truth.md` Section A/B/C against the post-cleanup mermaid; both P1-1 + P1-2 are now ✅, and the convention gap C.1 is closed.

---

## 7. References

- `docs/architecture/db_model_drift_report.md` — §B (P1-1), §C (P1-2), §P0/P1 prioritized list at the bottom.
- `docs/architecture/db_model_drift_report_mermaid_truth.md` — §C.1 (convention gap C.1), §P0/P1 prioritized list at the bottom.
- `docs/architecture/db_relational_model.mermaid` — current state; pre-cleanup `Orders` block at lines 382-422 (pre-pass numbering may have shifted by the Phase A diff).
- `docs/architecture/db_relational_model.md` — companion doc; "Last reconciled to code" date + new sections get appended per phase.
- `.agents/plan/ordering/ORDER_ACTIVITY_PLAN.md` — the plan that surfaced these findings during the §6.1 mermaid reconciliation pass. v0.3 references this backlog in §9 References once Phase A lands.
- `Ordering.Domain/Models/Order.cs` — source of truth for the 8 snapshot columns; do NOT modify.
- `.agents/plan/kitchen/KITCHEN_FOLLOWUP_PLAN.md` — sibling template for follow-up plans in this repo.
- **Mermaid ER parser limits** — the diagram parser does NOT accept `%%` comment lines between `erDiagram` and the first entity block (per `db_relational_model.md` line 2-6). All convention comments live BELOW the entity they document, not above.

---

**Document Version:** 0.1 (2026-07-15 — derived from the 2026-07-15 mermaid-drift review of `ORDER_ACTIVITY_PLAN.md` v0.3).
**Last Updated:** 2026-07-15.
**Maintained By:** Ordering working group (TBD).
**Status:** Not started. Safe to schedule as a single-session cleanup pass after the activity-feed plan ships.