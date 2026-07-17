# Orderly database diagram — conventions & legend

Companion to [`db_relational_model.mermaid`](./db_relational_model.mermaid).
The mermaid `erDiagram` parser does not accept `%%` comment lines between the `erDiagram`
keyword and the first entity block, so the conventions live here in Markdown instead of
inline at the top of the diagram.

**Last reconciled to code:** 2026-07-16 (code is source of truth).

**Updates from `ORDERING_CLEANUP_BACKLOG.md` (Phase A, 2026-07-16):**

- Added 8 missing snapshot columns to the `Orders` block in `db_relational_model.mermaid`: `ConfirmedAt`, `PreparingStartedAt`, `ReadyAt`, `DeliveredAt`, `CompletedAt`, `CancelledAt`, `CancelledByUserId`, `CancellationReason`. All already present in `Ordering.Domain/Models/Order.cs` (lines 34-50); the diagram had drifted away from the aggregate. Closes `db_model_drift_report.md` §B P1-1.
- Placement follows the backlog's literal Phase A spec: `ConfirmedAt` between `ApprovedByAdminId` and `ApprovedAt`; the seven other lifecycle columns in lifecycle order after `ApprovedAt`. A column-cluster comment explains the layout and links back to the drift report + backlog.
- `CancellationReason` rendered as `string` (per the existing `Notes` pattern at line 416), not `text`. Promote to `text` if a future review finds it needs > 2000 chars (matching `OrderActivity.Notes`).
- No FK edges added. `CancelledByUserId` is a flat `uuid` column per the audit-FK convention at line 147 ("Audit-FK edges are flat columns, not navigation"); the diagram continues to render it without an FK edge back to `Users`.
- No code changes. Diff is `Orders`-block-only on the mermaid + this section on the companion doc.

**Updates from `ORDERING_CLEANUP_BACKLOG.md` (Phase B, 2026-07-16):**

- **Option B.2 (correct in place) chosen** for the `Orders` audit-fields comment block. The original 4-line comment claimed that `Order` inherits `CreatedAt` / `LastModifiedAt` / `CreatedBy` / `LastModifiedBy` from `Aggregate<OrderId>` (line 113). It is misleading: the companion doc on the same diagram (line 92) states that `Entity<T>` is used by `OrderItem`, `OrderBill`, and the `MenuItem` (Ordering) value object — **not** `Order`. The `Order` aggregate does not declare any of those audit columns; `CreatedByUserId` is the only audit-style field, and it is explicitly declared on the aggregate and rendered as the `uuid` column above.
- Replacement comment: explains the inheritance boundary (where `Order` falls relative to `Entity<T>` / `Aggregate<TId>`), retains the `IsActive` row (which was correct), anchors to the drift report §C + the backlog Phase B for traceability.
- The `BulkOrderUploads` block at line 280 still renders the inherited audit columns (because `BulkOrderUploads : AuditableEntity<int>` after the Phase 4 fix per line 169); this Phase B change is scoped to the `Orders` block only and does not affect other entities.
- No code changes. Diff is `Orders`-comment-only on the mermaid + this section on the companion doc.

**Updates from `ORDERING_CLEANUP_BACKLOG.md` (Phase C, 2026-07-16):**

- **New convention documented**: relational jsonb columns on the diagram MAY carry an optional sub-shape comment block below the entity, of the form described in the new "Relational jsonb sub-shapes" section below. The convention is permissive — simple jsonb columns (e.g. `BulkOrderUploads.ErrorLog`) keep their `jsonb <Field> "<description>"` rendering with no sub-shape; only typed-record jsonb columns opt in.
- **Drift surfaced:** the cleanup backlog's Phase C spec named the sub-shape fields as `(Name, Value, Price)` for `SelectedVariations` and `(Ingredient, Action)` for `Customizations`. Both were outdated as of 2026-07-10 when migration `20260710233247_TypedOrderItemCustomizationsJsonb` retyped the jsonb columns to typed `IReadOnlyList<KitchenOrderItem*>` records (per `Ordering.Infrastructure/Data/Configurations/OrderItemConfiguration.cs:49-71`). The sub-shape in the mermaid documents the **current** typed records — `KitchenOrderItemVariation(Name, Price)` and `KitchenOrderItemCustomization(Label, Value?, Price?)` — not the backlog's pre-2026-07-10 field names. Future reviewers reading `db_model_drift_report_mermaid_truth.md` §C.1 should re-derive the field list from the code, not from the backlog.
- **No code changes.** Diff is two new sub-shape comment blocks after the `OrderItems` block + this section on the companion doc.
- **No Marten-document retrofit.** Marten documents (`Basket.Items`, `Catalog`'s `OrderSnapshot` / `OrderModificationLog` / `OrderItemPriceAudit` / `NotificationLog`) keep the existing Marten sub-shape convention (inline `jsonb <Field> "embedded <Type>[]"` per the `Basket.Items` block). The new relational convention is parallel, not merged.

**Updates from `ORDER_ACTIVITY_PLAN.md` (v0.3, 2026-07-15):**

- New relational entity `OrderActivities` added to the ORDERING block. Child of `Orders.Id` (cascade delete), loaded via `Order.Activities` navigation; **no** `DbSet<OrderActivity>` on `IApplicationDbContext`. Strongly-typed `OrderActivityId : Guid` value object (renders as `uuid` per the convention below). Columns: `Id`, `OrderId`, `ActivityType` (string-backed enum), `ActorUserId?`, `OccurredAt`, `CorrelationId?`, `Notes?`, `Metadata?` (jsonb). See `db_model_drift_report.md` §P0-1.
- New index `IX_order_activities_OrderId_OccurredAt` on `(OrderId, OccurredAt)` — covering index for the read pattern `WHERE OrderId = @id ORDER BY OccurredAt ASC`. Configured in `OrderActivityConfiguration.cs` (Phase 1 of the plan).
- New relationship `Orders ||--o{ OrderActivities : "audit-trail of"`.
- The `Metadata` jsonb column carries a typed `OrderActivityMetadata` record with nullable `OrderStatus?` / `PrepStatus?` / `DeliveryStatus?` enum pairs. `JsonStringEnumConverter` is registered in `OrderActivityJson.Options` so enum values serialize as strings (`"Confirmed"`, not `2`). Mirrors the existing pattern for `OrderItem.SelectedVariations` / `OrderItem.Customizations` jsonb columns.
- Catalog's three Marten documents (`OrderSnapshot`, `OrderModificationLog`, `OrderItemPriceAudit`) are documented in the diagram but are **independent** of the Ordering activity feed. See `ORDER_ACTIVITY_PLAN.md §3 + §7` for the four-reason storage decision and the explicit "do not merge" note.

---

## Type mappings

| Diagram | Code |
|---|---|
| `uuid` | `System.Guid` |
| `timestamp` | `NodaTime.Instant` |
| `date` | `NodaTime.LocalDate` |
| `time` | `NodaTime.LocalTime` |
| `decimal` | `System.Decimal` (EF Core precision configured per property in `CatalogDbContext` / `Ordering.Infrastructure/Data/Configurations/`) |
| `jsonb` | PostgreSQL `jsonb`; stored as `System.String` in code, mapped via Npgsql |
| `text` | `System.String` (mapped to long text in PostgreSQL) |
| `enum` | Stored as `string` in Ordering (`OrderStatus`, `OrderType`, etc.) and as `int` in Catalog (`TableStatus`, `AvailabilityStatus`, etc.). Diagram intentionally does not distinguish. |

Strongly-typed value objects wrap their primitive (e.g. `OrderId`, `MenuItemId`,
`CustomerId`, `OrderNumber`); the diagram renders the underlying primitive for clarity.

---

## Marten documents

Entities marked `%% MARTEN DOCUMENT` at the top of their block are persisted by **Marten**,
not EF Core:

- `OrderSnapshot`, `OrderModificationLog`, `OrderItemPriceAudit`, `NotificationLog` — registered
  in `Catalog.API/Program.cs` lines 30–39 via `opt.Schema.For<T>()`.
- `Basket` — registered in `Basket.API/Program.cs` via `AddMarten(...)` with `CreateDatabasesForTenants`.
  Identity is `UserId` (declared via the `[Identity]` attribute), so the document id column is
  `user_id` (Guid). Multi-tenancy creates per-tenant schemas/doc tables (`mt_doc_basket`).

Embedded arrays in Marten documents are documented inline as
`jsonb <Field> "embedded <Type>[]"` plus a sub-shape comment block.

> **Code-vs-storage alignment in Catalog documents (Cleanup milestone, 2026-07-11).** The
> three Catalog Marten documents (`OrderSnapshot`, `OrderModificationLog`,
> `OrderItemPriceAudit`) no longer extend any relational base class — they are plain Marten
> documents declaring `Guid Id`, which matches the synthetic Guid Marten assigns. The former
> `Entity<int>` code-vs-storage mismatch is resolved; the diagram uses `uuid Id PK "Marten
> synthetic"`. `NotificationLog` (the fourth Catalog Marten document) is out of plan — it is
> being removed entirely per §6.7 of `CATALOG_SERVICE_PLAN.md` once the Notification v1 plan
> lands, so it is not rebased.

---

## Audit fields — inherited, not enumerated

The diagram does **not** repeat the same audit columns on every entity. They are inherited
from base classes.

### `BuildingBlocks.Entities.Contracts.AuditableEntity<TId>`

Used by: `Brand`, `Restaurant`, `Table`, `User`, `MenuItem`, `MenuCategory`,
`Reservation`, `WalkInQueue`, `Ingredient`, `Customer`. (`Coupon` was removed in
Phase 6.2 — 2026-07-11; Coupon is now owned by Discount, see
`CATALOG_SERVICE_PLAN.md` §7.6.2.)

Adds:

- `string CreatedBy`
- `Instant CreatedAt`
- `string LastModifiedBy`
- `Instant? LastModifiedAt`
- `bool IsActive`

### `BuildingBlocks.Entities.Contracts.Entity<TId>`

Used by: `MergedTable`, `MenuSubCategory`, `MenuItemVariation`, `ComboItem`,
`MenuItemIngredient`, `IngredientAlternative`, `PriceHistory`, `BulkOrderUpload`,
`CustomerFeedback`, `MenuItemAnalytics`, `OrderTimingAnalytics`.

The Catalog Marten documents (`OrderSnapshot`, `OrderModificationLog`,
`OrderItemPriceAudit`) do **not** extend any relational base class — they are
plain Marten documents with a synthetic `Guid Id` (Cleanup milestone 2026-07-11;
the code-vs-storage mismatch flagged in §137-148 of this doc is resolved).
`NotificationLog` (the fourth Catalog Marten document) is being removed entirely
per §6.7 of `CATALOG_SERVICE_PLAN.md` once the Notification v1 plan lands — at
that point the `Entity<TId>` list loses its trailing "and Marten documents"
phrase entirely.

Adds only `TId Id`. No audit fields.

### `Ordering.Domain.Abstractions.Entity<T>`

Used by: `OrderItem`, `OrderBill`, and the value-object `MenuItem` (Ordering).

Adds:

- `T Id`
- `Guid? CreatedBy`
- `Instant? CreatedAt`
- `Guid? LastModifiedBy`
- `Instant? LastModified`

### `Ordering.Domain.Abstractions.Aggregate<T>` — `Order`

Inherits all of `Entity<T>`. **Does not expose `IsActive`.** The `Order` aggregate
explicitly declares `bool IsActive` because the diagram requires it (and the application
needs to soft-cancel orders).

---

## Ownership types (EF Core `OwnsOne` / `ComplexProperty`)

Some entities have nested value-object properties that EF Core stores as **owned types**
in the same row. The diagram represents them with a single `text` or scalar `X` line plus
a comment naming the owned fields.

| Entity | Owned property | Stored as |
|---|---|---|
| `Customer` | `Address` (`OwnsOne`) | `Street`, `City`, `State`, `ZipCode`, `Country` |
| `Order` | `BillingAddress` (`ComplexProperty`) | Same as above |
| `Order` | `DeliveryAddress` (`ComplexProperty`) | Same as above |
| `Order` | `Payment` (`ComplexProperty`) | `CardName`, `CardNumber`, `Expiration`, `Ccv`, `PaymentMethod` |

---

## Convention: relational jsonb sub-shapes

Relational tables may have jsonb columns (`jsonb <Field> "<description>"`) whose on-disk payload
is a typed record. The optional **sub-shape** convention documents that typed record directly
under the entity block, in the form:

```text
<EntityName> {
    ...
    jsonb <Field> "<description>"
    ...
}

%% <Field> sub-shape (jsonb on <EntityName>):
%%   <TypeRecordName>[] — typed per <ConfigurationFilePath>.
%%     <Type> <SubField1> "<comment>"
%%     <Type> <SubField2> "<comment>"
%%     ...
```

The sub-shape comment block carries:

- **One record-type line** naming the typed record (`<TypeRecordName>[]`) and the file where
  the EF Core `HasConversion` / `ValueConverter` mapping lives, so the field list is anchored
  to code rather than to drift-prone prose.
- **One line per sub-field** of the record, with the field's `<Type>` and an inline comment
  the diagram reader can use to disambiguate the wire/storage shape.

**The sub-shape is informational only.** It does **not** introduce a new entity in the diagram;
the typed record is a value object, not a navigable entity. The jsonb column still renders as
a single line on its parent entity block.

**The convention is permissive, not mandatory.** Simple jsonb columns (e.g. a `List<string>`
or an untyped bag of log lines) keep their `jsonb <Field> "<description>"` rendering with no
sub-shape. Only typed-record jsonb columns opt in. Existing retro-fitted relational jsonb
columns at the time of writing:

| Entity | Column | Sub-shape |
|---|---|---|
| `OrderItems` | `SelectedVariations` | `KitchenOrderItemVariation[] (Name, Price)` |
| `OrderItems` | `Customizations` | `KitchenOrderItemCustomization[] (Label, Value?, Price?)` |

(`OrderActivities.Metadata` is a typed `OrderActivityMetadata` record too, but its field list
is long enough that the inline sub-shape comment block lives on the `OrderActivities` block
itself rather than as a post-block sub-shape — see the existing comments.)

**Marten-document sub-shapes** are a parallel convention (inline `jsonb <Field> "embedded
<Type>[]"` per the `Basket.Items` block + Catalog's `OrderSnapshot` etc.). They are not
merged with this relational convention.

---

## Out of scope

The following are intentionally not modelled in this diagram:

- **Identity.API tables** — `ApplicationUser`, `ApplicationRole`, `UserRestaurant`,
  `Permission`, `RolePermission`, `LoginAuditLog`. The catalog-domain `Users` entity is
  a mirror of the Identity user (one row per Identity user that has access to a
  restaurant); it is NOT the source of truth for authentication.
- **Cascade / SetNull / Restrict FK behaviors** — configured in
  `Catalog.API/Data/CatalogDbContext.cs` and
  `Ordering.Infrastructure/Data/Configurations/*Configuration.cs`. The diagram shows the
  shape, not the delete behavior.
- **Audit-FK edges** — `CreatedByUserId`, `ModifiedByUserId`, `ApprovedByUserId`, etc.
  are stored as raw `Guid` columns in code with **no navigation property**. The diagram
  treats them as flat columns and does not draw FK edges back to `Users`. This keeps the
  diagram readable and matches the code.
- **Indexes** — composite and unique indexes are configured in `CatalogDbContext` /
  `Ordering.Infrastructure` configurations. Not modelled here.

---

## Mismatches flagged for follow-up

These are real code issues flagged during diagram reconciliation. Item 1 remains open;
items 2–3 were resolved by the Catalog Cleanup / Phase 4 work (2026-07-11) and are kept
here with their resolution note for traceability:

1. **`Basket.MenuItemId` type** — code stores `int`, but `MenuItem.Id` (Catalog) is
   `Guid`. Diagram flags this with `"TODO: should be uuid to match MenuItem.Id"` on the
   embedded `BasketItem` shape.
2. **Catalog Marten documents using `Entity<int>`** — ✅ **Resolved (Cleanup milestone,
   2026-07-11).** `OrderSnapshot`, `OrderModificationLog`, and `OrderItemPriceAudit` now
   drop the relational base class entirely and declare `Guid Id`, matching Marten's
   synthetic Guid document id.
3. **`BulkOrderUploads.CreatedAt`** — ✅ **Resolved (Phase 4, 2026-07-11).** The entity base
   flipped from `Entity<int>` to `AuditableEntity<int>`, so it now carries `CreatedAt` /
   `CreatedBy` / `LastModifiedAt` / `LastModifiedBy`; the diagram reflects the audit columns.