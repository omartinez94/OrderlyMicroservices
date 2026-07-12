# Orderly database diagram — conventions & legend

Companion to [`db_relational_model.mermaid`](./db_relational_model.mermaid).
The mermaid `erDiagram` parser does not accept `%%` comment lines between the `erDiagram`
keyword and the first entity block, so the conventions live here in Markdown instead of
inline at the top of the diagram.

**Last reconciled to code:** 2026-06-30 (code is source of truth).

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

> **Code-vs-storage mismatch in Catalog documents.** The four Catalog Marten documents
> (`OrderSnapshot`, `OrderModificationLog`, `OrderItemPriceAudit`, `NotificationLog`) all
> extend `Entity<int>` (so the C# property is `int Id`), but Marten assigns a **synthetic
> Guid** as the actual document id. The diagram uses `uuid Id PK` with the comment
> `"Marten synthetic — code declares Entity<int>"`. This is a known code smell to be fixed
> in a separate task (the entities should either move to `Entity<Guid>` or extend a Marten
> document base class).

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

These were intentionally NOT fixed in the diagram reconciliation — they're real code
issues that need a separate code change:

1. **`Basket.MenuItemId` type** — code stores `int`, but `MenuItem.Id` (Catalog) is
   `Guid`. Diagram flags this with `"TODO: should be uuid to match MenuItem.Id"` on the
   embedded `BasketItem` shape.
2. **Catalog Marten documents using `Entity<int>`** — should be `Entity<Guid>` or a
   proper Marten document base class to match Marten's synthetic Guid id.
3. **`BulkOrderUploads.CreatedAt`** — code has only `ApprovedAt` / `CompletedAt` (no
   `CreatedAt`) because the entity extends `Entity<int>` (not `AuditableEntity`). The
   diagram reflects this; a code change to add `CreatedAt` is recommended.