# DB model drift report — mermaid as source of truth

> **Generated:** 2026-07-15
> **Scope:** `ORDER_ACTIVITY_PLAN.md` (v0.3) — new `OrderActivity` child entity + table on the Ordering service. Mermaid-as-truth analysis: does the plan violate any convention / FK / relationship pattern the mermaid locks in?
> **Method:** read the mermaid + companion doc, then check the plan against every convention documented. Two-direction analysis per the project convention (`mermaid-code-review-convention`).
>
> Companion report: `db_model_drift_report.md` (code as source of truth, same scope).

---

## Legend

| Symbol | Meaning |
|---|---|
| ✅ match | Plan respects the mermaid convention |
| ❌ must-fix in plan | Plan violates a mermaid rule |
| ⚠️ convention gap | Mermaid doesn't have a slot for something the plan does |
| 🟡 cosmetic | Plan wording doesn't quite match mermaid comment, no schema impact |

---

## Section A — Plan-introduced entities vs mermaid rules

### A.1 — `OrderActivity : Entity<OrderActivityId>`

| Mermaid rule | Plan behaviour | Status |
|---|---|---|
| Strongly-typed value objects render as primitives (companion doc line 26) | `OrderActivityId : Guid` value object — plan renders the underlying `Guid` in the entity, mirroring `OrderId` etc. | ✅ |
| Child entities follow the same parent-FK pattern as `OrderItem` / `OrderBill` (mermaid lines 425-459) | `OrderActivityConfiguration.HasMany(o => o.Activities).WithOne().HasForeignKey(a => a.OrderId).OnDelete(DeleteBehavior.Cascade)` — mirrors `OrderConfiguration.cs:14` | ✅ |
| `Orders` aggregate rows are owned by Ordering; the `Orders` block is in the ORDERING section (mermaid line 382) | `OrderActivities` is placed in the ORDERING relational section (just after `OrderBills` in the post-pass mermaid) | ✅ |
| ActivityType is stored as `string` via `HasConversion<string>()` (matches `OrderStatus` pattern at `OrderConfiguration.cs:58`) | Plan explicitly calls out `HasConversion<string>().HasMaxLength(50).IsRequired()` for `ActivityType` | ✅ |
| Cascade delete is configured in code, not modelled (companion doc line 135) | Plan cascades via `OnDelete(DeleteBehavior.Cascade)`; mermaid documents it as a comment on the `OrderId FK` column | ✅ |
| Audit-FK edges are flat columns, not navigation (companion doc line 139) | `OrderActivity.ActorUserId` is a flat `Guid?` column with no FK edge to `Users` — same pattern as `Order.CreatedByUserId` etc. | ✅ |
| jsonb columns are documented with `jsonb <Field> "<description>"` + optional sub-shape comment block | `Metadata` is rendered as `text Metadata "jsonb NULL — typed OrderActivityMetadata record, see comment"` with a 9-line sub-shape comment documenting the typed record's fields (mirrors `OrderItems.SelectedVariations` jsonb pattern at mermaid line 435) | ✅ |

**Verdict:** Section A.1 is **fully compliant**. The plan respects every mermaid convention that applies.

### A.2 — `OrderActivityMetadata` (typed record, jsonb column)

| Mermaid rule | Plan behaviour | Status |
|---|---|---|
| jsonb sub-shape comment block format (`jsonb <Field> "embedded <Type>[]"` + sub-shape, mermaid line 38) | Sub-shape uses Mermaid-comment lines (one per field) instead of a separate `<Type>[]` shape — because this is a **relational jsonb column**, not a Marten embedded array | ⚠️ convention gap — see Section C below |
| `JsonStringEnumConverter` registered so enums serialize as strings | Plan §6.1 Infrastructure commit adds the converter to `OrderActivityJson.Options` | ✅ |
| jsonb columns don't carry FK edges to other tables | `Metadata` is a self-contained typed record (no FKs out) | ✅ |

**Verdict:** Section A.2 is **compliant** with one convention gap (Section C).

---

## Section B — Plan-introduced relationships vs mermaid rules

### B.1 — `Orders ||--o{ OrderActivities : "audit-trail of"`

| Mermaid rule | Plan behaviour | Status |
|---|---|---|
| Cardinality `||--o{` (one-to-many, optional on the "many" side) | Plan says `HasMany(o => o.Activities)` — many activities per order, mandatory on the Order side. Mermaid `||--o{` is correct | ✅ |
| Relationship label uses snake-case text in quotes | Plan uses `"audit-trail of"` — matches the convention (`"tracked by"`, `"audit trail"` etc. on lines 534-539) | ✅ |
| Placement: after `Orders ||--o{ OrderBills : "split into"` (mermaid line 533) | Post-pass mermaid places the new line immediately after the OrderBills one | ✅ |

**Verdict:** Section B.1 is **fully compliant**.

### B.2 — `Orders ||--o{ OrderModificationLog : "tracked by"` (existing) vs new `OrderActivities`

| Concern | Mermaid rule | Plan behaviour | Status |
|---|---|---|---|
| Two separate "audit" edges from `Orders`? | Mermaid allows multiple edges from one entity to different children — already the case (`Orders ||--o{ OrderItems`, `||--o{ OrderBills`, `||--o{ OrderModificationLog`, etc.) | Plan adds a new edge with a distinct label (`"audit-trail of"`) — no collision with `"tracked by"` | ✅ |
| Semantic distinctness | `OrderModificationLog` (Catalog Marten doc, line 315) is "Catalog's audit of modifications to orders via Catalog's surface". `OrderActivities` (Ordering relational) is "Ordering's chronological activity feed of state transitions". The two coexist; documented in `ORDER_ACTIVITY_PLAN.md §3 + §7` | The plan explicitly distinguishes the two in §3 + §7 + the new mermaid `Catalog` bullet | ✅ |

**Verdict:** Section B.2 is **fully compliant** with the existing mermaid pattern of multiple edges per entity.

---

## Section C — Convention gaps

### C.1 — Sub-shape comment block format for relational jsonb columns

The mermaid convention (companion doc line 38) specifies the format for **Marten** embedded arrays:

> jsonb `<Field> "embedded <Type>[]"` plus a sub-shape comment block.

For **relational** jsonb columns (e.g. `OrderItems.SelectedVariations` at mermaid line 435), the convention is just `jsonb <Field> "<description>"` with **no** sub-shape. This means the typed `OrderActivityMetadata` record's 9-field shape is not modelled in the diagram — only a comment note ("typed `OrderActivityMetadata` record, see comment").

**Resolution options for v2 of the mermaid convention:**

1. **Status quo (✅ plan-conformant).** Render `text Metadata "jsonb NULL — typed record, see comment"` and document the shape in a comment block (current post-pass mermaid). Simple, matches the existing relational pattern.
2. **Extend the convention to model relational jsonb shapes** with an inline `%% shape` block. More work; only worth it if multiple relational jsonb columns carry complex shapes.
3. **Promote `OrderActivityMetadata` to a pseudo-entity** in the diagram (e.g. as `OrderActivityMetadata` block with a dashed edge). Probably overkill — the column is a value type, not a navigable entity.

**Verdict:** ⚠️ Status quo is acceptable. The plan conforms to the **existing** convention for relational jsonb. A future mermaid-convention revision (out of scope for this plan) could promote option 2 or 3 if the schema grows.

### C.2 — `CorrelationId` column type

The mermaid renders `CorrelationId` as `string nvarchar(100) NULL`. The plan's source of values is `Guid.NewGuid().ToString()` (HTTP fallback) or `context.CorrelationId?.ToString()` (MassTransit). Both produce ≤36-char strings, so `nvarchar(100)` is conservative.

**Resolution:** ✅ acceptable. The 100-char cap gives headroom for non-Guid correlation ids (e.g. traceparent headers from W3C Trace Context, if LoggingBehavior later threads them). The plan's `OrderActivity.Create` enforces `Length ≤ 100` at the domain layer.

---

## Section D — Plan-introduced BuildingBlocks primitives

The plan introduces two new BuildingBlocks artifacts:

1. `BuildingBlocks/Correlation/CorrelationContext.cs` (static class, ambient `AsyncLocal<string?>`)
2. `BuildingBlocks/Behaviors/LoggingBehavior.cs` — gains `Set/Clear` calls (existing file, modified)

Neither of these is a database entity, so the **mermaid does not need to render them**. They are cross-cutting infrastructure primitives, not schema. `current-architecture.md §9 (Cross-Cutting Patterns)` is the right home for documentation — the plan's §6.1 doc-update scope already calls out §9.

**Verdict:** ✅ no mermaid update needed; plan's doc-update scope is correct.

---

## Section E — Cross-checks against the companion doc

| Companion-doc claim | Plan behaviour | Status |
|---|---|---|
| Line 38: Marten embedded arrays use a sub-shape comment block | Plan uses Marten convention only for Catalog documents (none added by this plan) | ✅ |
| Line 92-103: `Entity<T>` adds `CreatedBy/CreatedAt/LastModified/LastModifiedBy`; used by `OrderItem`, `OrderBill`, `MenuItem` (Ordering); NOT by `Order` | Plan's `OrderActivity` extends `Entity<OrderActivityId>` directly (not `Aggregate<T>`); does NOT inherit audit fields. The plan does not write `CreatedAt` / `LastModifiedAt` columns on `OrderActivity` | ✅ |
| Line 119-123: `ComplexProperty` (BillingAddress, DeliveryAddress, Payment) on `Order` | Plan does not introduce any new ComplexProperty on `OrderActivity` (the metadata is a jsonb column, not a ComplexProperty) | ✅ |
| Line 132-142: Out-of-scope items (Identity tables, cascade behaviors, audit-FK edges, indexes) | Plan respects every "out of scope" line — Identity tables untouched; cascade behavior documented as a comment; `ActorUserId` rendered as flat column; index documented as inline comment | ✅ |
| Line 144-148: Mismatches flagged for follow-up | Plan does not introduce any new mismatches; the existing `Basket.MenuItemId` mismatch is unchanged (Basket plan's concern, not this plan's) | ✅ |

**Verdict:** Section E is **fully compliant** with the companion doc.

---

## Section F — Relationship-label vocabulary drift check

The mermaid uses specific relationship-label vocabulary. New labels introduced by this plan:

| New label | Existing similar labels (mermaid) | Style match? |
|---|---|---|
| `"audit-trail of"` (Orders → OrderActivities) | `"tracked by"` (Orders → OrderModificationLog, line 534), `"audit trail"` (OrderItems → OrderItemPriceAudit, line 539), `"receives feedback"` (line 535), `"price snapshot"` (line 538) | ✅ matches the lower-case-verb-phrase style |

**Verdict:** ✅ plan's label fits the existing vocabulary.

---

## P0 prioritized list — must-fix in plan

| Priority | Item | Action | Status |
|---|---|---|---|
| **P0-1** | Plan must respect the convention that relational jsonb columns don't carry a sub-shape in the mermaid | Plan's mermaid rendering of `Metadata` follows the status quo (comment-only) | ✅ no plan change needed |
| **P0-2** | Plan must respect the convention that audit-FK edges are flat columns | Plan renders `ActorUserId` as flat `uuid` with no FK edge | ✅ no plan change needed |
| **P0-3** | Plan must respect the convention that cascade delete is configured in code, not modelled | Plan uses `OnDelete(DeleteBehavior.Cascade)` in `OrderActivityConfiguration` and documents it as a comment in the mermaid | ✅ no plan change needed |
| **P0-4** | Plan must respect the convention that strongly-typed value objects render as primitives | `OrderActivityId : Guid` renders as `uuid` in the mermaid | ✅ no plan change needed |
| **P1-1** | Mermaid convention for relational jsonb sub-shape is undefined (status quo: comment-only) | Future mermaid-convention revision; out of scope for this plan | ⚠️ **OPEN** |

---

## Sign-off

The plan **does not violate any mermaid rule**. Every convention documented in the companion doc is respected; every relationship pattern matches; every column type / nullability / length cap fits the existing rendering. The single P1 item (relational jsonb sub-shape convention) is a future mermaid-convention revision, not a plan defect.

The activity-feed plan is **safe to implement** against the mermaid conventions. The next reviewer of either file should re-read this report to understand what was checked in this pass.