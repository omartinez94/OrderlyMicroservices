# Web Frontend Backend Prerequisites — Implementation Plan

> Scope: Document and deliver the backend changes required to support the OrderlyWeb frontend roadmap (master plan v1.2). Grouped by consuming phase, risk level, and implementation complexity. Minimal changes are implemented here; large architectural changes are planned for future sprints.

---

## Status

> **Plan version**: `v1.0` (2026-10-17) — Initial plan derived from OrderlyWeb roadmap gap analysis.
> **Current state**: ⏸ Ready for review.

| Phase | Name | Service | Status |
|:-----:|---|---|:-----:|
| 1 | Brand `archive` endpoint + `Slug`/`IsActive` fields | Catalog.API | ⏸ Pending |
| 2 | Generic `AuditEntries` system | Catalog.API | 🔒 Blocked (large) |
| 3 | `GET /identity-api/users?brandId=` filter | Identity.API | 🔒 Blocked (large) |
| 4 | Restaurant `Status`, `OwnerId`, `OperatingHours` fields | Catalog.API | 🔒 Blocked (large) |
| 5 | `AdminSettings` singleton aggregate | Catalog.API | 🔒 Blocked (large) |
| 6 | New permission seeds for CRM actions | Identity.API | ⏸ Pending |

> **Legend**: ✅ Done · 🚧 In progress · ⏸ Pending · 🔒 Blocked

> **Commit messages**: Conventional Commits (`feat(catalog):`, `fix(identity):`, `chore(migrations):`). Short subject, ≤50 chars, imperative mood, no trailing period.

> **Update rule**: On every phase completion, the plan MUST be updated in the same commit group as the phase work.

---

## 0. Skill & Documentation Conventions

### 0.1 Skill Mandate — `csharp-developer`, `add-catalog-endpoint`

> All implementation work MUST follow `OrderlyMicroservices/AGENTS.md`. Carter Minimal API (`ICarterModule`), MediatR CQRS, FluentValidation, NodaTime, and the Vertical Slice Architecture patterns are non-negotiable.

### 0.2 Code-Quality Guard Rails

- **Quality Gate**: `pwsh ./scripts/phase-guard.ps1 -PhaseName "<Phase Name>" -Quick` MUST exit 0 before any phase is marked complete.
- **No `DateTime`**: Always use `NodaTime` (`Instant`, `LocalDate`). Backend migrations use `Instant` for timestamp columns.
- **EF Core migrations**: Every schema change requires a new `dotnet ef migrations add` entry in the target service.
- **No duplicate logic**: Shared behaviors belong in `BuildingBlocks`. Do not copy them into service projects.
- **Explicit types**: Prefer explicit types over `var` except for `using var`, `out var`, and LINQ.

---

## 1. Context

The OrderlyWeb frontend roadmap (master plan v1.2) identified several backend capabilities that are either missing or partially implemented. This plan documents each gap, classifies its complexity, and either implements it directly (minimal changes) or provides a structured plan for a future sprint (large changes).

### 1.1 Backend Gap Inventory

| Gap | Identified In | Complexity | Action |
|---|---|:---:|---|
| `Brand` missing `Slug`, `IsActive`, `AuditableEntity` base | Admin CRM Plan Phase 2 | **Minimal** | Implement here |
| `Brand` missing `archive` endpoint (soft-delete) | Admin CRM Plan Phase 2 | **Minimal** | Implement here |
| New CRM permissions not seeded in Identity | Admin CRM Plan Phase 1 | **Minimal** | Implement here |
| `MergeTables` / `SplitTables` write endpoints | Phase 0 Analysis | ✅ **Already exists** | No action needed |
| Discount resolution is server-side only | Phase 1 Analysis | ✅ **Already correct** | Document only |
| Generic `AuditEntries` table + endpoints | Admin CRM Plan Phase 3 | **Large** | Plan only |
| `GET /identity-api/users?brandId=` filter | Admin CRM Plan Phase 4 | **Large** | Plan only |
| `Restaurant` Status/OwnerId/OperatingHours fields | Admin CRM Plan Phase 5 | **Large** | Plan only |
| `AdminSettings` singleton aggregate | Admin CRM Plan Phase 6 | **Large** | Plan only |

---

## 2. Goal

1. Deliver minimal backend changes that unblock frontend Phase 1 and Phase 4 admin work.
2. Document large backend changes so they can be planned and scheduled as dedicated sprints.

---

## 3. Out of Scope

- Frontend implementation (covered by OrderlyWeb plans).
- `Ordering.API` changes — no gaps identified there.
- `Kitchen.API` changes — no gaps identified.
- `Basket.API` changes — existing endpoints are sufficient.
- Database migrations for large architectural changes (see §6 for planning notes).

---

## 4. Tech Decisions

| Decision | Choice | Reason |
|:---|:---|:---:|
| Brand soft-delete | `IsActive` flag (not physical delete) | Preserves referential integrity with Restaurants and Staff assignments. |
| `Slug` uniqueness | DB unique index + 409 conflict response | Frontend expects `BRAND_DUPLICATE_SLUG` conflict type guard. |
| Audit system | Separate `AuditEntries` table in Catalog.API | Avoids cross-service coupling; Catalog owns the entities being audited. |
| `brandId` user filter | Identity server-side join through Restaurant | No `UserBrand` table; joins Brand→Restaurant→UserRestaurant→User. |
| Permission seeding | Extend existing `PermissionSeeder` in Identity.API | Consistent with how the 25 existing permissions are seeded. |

---

## 5. Folder Layout (Minimal Changes Only)

```text
OrderlyMicroservices/orderly-microservices/
├── Services/
│   ├── Catalog/Catalog.API/
│   │   ├── Features/
│   │   │   └── Brands/
│   │   │       └── ArchiveBrand/              NEW
│   │   │           ├── ArchiveBrandEndpoint.cs
│   │   │           └── ArchiveBrandHandler.cs
│   │   └── Migrations/
│   │       └── <timestamp>_AddBrandSlugAndIsActive.cs   NEW EF migration
│   └── Identity/Identity.API/
│       └── Data/
│           └── Seeders/
│               └── PermissionSeeder.cs         EXTEND — add CRM permissions
```

---

## 6. Detailed Specification

### 6.1 Phase 1 (Minimal): Brand `Slug`, `IsActive`, and `ArchiveBrand` endpoint

#### 6.1.1 EF Core Migration — `AddBrandSlugAndIsActive`

Target: `Catalog.API` (PostgreSQL, `catalogdb`).

Schema changes to the `Brands` table:

```csharp
// In the migration Up() method:
migrationBuilder.AddColumn<string>(
    name: "Slug",
    table: "Brands",
    type: "character varying(100)",
    maxLength: 100,
    nullable: false,
    defaultValueSql: "lower(replace(\"Name\", ' ', '-'))");

migrationBuilder.AddColumn<bool>(
    name: "IsActive",
    table: "Brands",
    nullable: false,
    defaultValue: true);

migrationBuilder.CreateIndex(
    name: "IX_Brands_Slug",
    table: "Brands",
    column: "Slug",
    unique: true);
```

> [!NOTE]
> `AuditableEntity<Guid>` base class (which provides `CreatedAt`/`UpdatedAt` via `Instant`) requires verifying whether `Brand` already inherits it. If it does, the timestamps are already present. If not, this is a larger migration — defer to Phase 2 of this plan.

#### 6.1.2 `Brand` domain model update

In `Catalog.API` (or `Catalog.Domain` if exists), add `Slug` and `IsActive` to the `Brand` entity. Add FluentValidation rule: `Slug` must match `^[a-z0-9-]+$`, max 100 chars.

#### 6.1.3 `ArchiveBrand` Carter endpoint

New vertical slice: `Features/Brands/ArchiveBrand/`.

```
POST /v1/brands/{id}/archive
Authorization: brands:archive permission (checked via policy attribute)
Response 200: { id: Guid, isActive: false }
Response 404: Brand not found
Response 409: { type: "BRAND_HAS_ACTIVE_RESTAURANTS" } — if any active restaurant is under the brand
```

Handler logic:
1. Fetch brand by `id`; throw `NotFoundException` if not found.
2. Check for any active `Restaurant` records with `BrandId == id`; throw `ConflictException("BRAND_HAS_ACTIVE_RESTAURANTS")` if found.
3. Set `brand.IsActive = false`. Persist via `DbContext`.
4. Return `ArchiveBrandResult(brand.Id)`.

#### 6.1.4 Update `GetBrands` handler

Add `isActive` query parameter filter to `GetBrandsEndpoint`. Default: return all brands (both active and archived) for admin routes; consumer can pass `?isActive=true` to filter.

Update `GetBrandsResponse` DTO to include `slug`, `isActive`, `createdAt`, `updatedAt` fields.

---

### 6.2 Phase 2 (Minimal): New CRM Permission Seeds

Target: `Identity.API` — `PermissionSeeder.cs` (or equivalent seeder class).

Add the following new permissions to the seeder:

```csharp
// New CRM permissions — add to PermissionSeeder alongside existing 25 permissions
"brands:view",
"brands:create",
"brands:edit",
"brands:archive",
"restaurants:create",
"restaurants:edit",
"restaurants:archive",
"settings:view",
"settings:edit"
```

Assign to roles in the seeder:

| Permission | SuperAdmin | RestaurantAdmin | Manager |
|---|:---:|:---:|:---:|
| `brands:view` | ✅ | ✅ | ✅ |
| `brands:create` | ✅ | — | — |
| `brands:edit` | ✅ | ✅ | — |
| `brands:archive` | ✅ | — | — |
| `restaurants:create` | ✅ | ✅ | — |
| `restaurants:edit` | ✅ | ✅ | — |
| `restaurants:archive` | ✅ | — | — |
| `settings:view` | ✅ | ✅ | — |
| `settings:edit` | ✅ | — | — |

> [!IMPORTANT]
> These permissions must be seeded idempotently (check-before-insert). The `PermissionSeeder` should use `FindOrCreate` semantics to be safe on re-runs.

---

### 6.3 Phase 3 (Large — Plan Only): Generic `AuditEntries` System

**Complexity**: Large — requires a new table, new Carter module, and instrumentation of every entity mutation in Catalog.API.

**Blocking**: Admin CRM Phase 3 (`EntityAuditLog` component) and the global `AdminAuditPage`.

#### Proposed Architecture

```
AuditEntries table (Catalog.API / catalogdb):
  Id             uuid PK
  EntityType     varchar(50)   -- "brand" | "restaurant" | "staff"
  EntityId       uuid
  ActorId        uuid          -- FK → AspNetUsers (Identity)
  ActorName      varchar(200)
  Action         varchar(50)   -- "create" | "update" | "archive" | "role-grant" | ...
  Before         jsonb nullable
  After          jsonb nullable
  Timestamp      timestamptz   -- NodaTime Instant
  Reason         varchar(500) nullable
```

**Endpoints needed**:
- `GET /catalog-api/v1/audit/{entityType}/{entityId}?page=&pageSize=`
- `GET /catalog-api/v1/audit?entityType=&actorId=&since=&until=&page=&pageSize=`

**Instrumentation strategy**: Use a MediatR `IPipelineBehavior<TRequest, TResponse>` that writes an audit row after successful command execution for commands tagged with `[Auditable]` attribute.

**Frontend stub strategy**: Until this is deployed, every audit tab on brand/restaurant detail pages renders `<ZoneSplash />` with an informational placeholder: "Audit trail coming soon." The `AdminAuditPage` does the same.

#### Acceptance Criteria (for future sprint)

- [ ] `AuditEntries` EF Core migration applied to `catalogdb`.
- [ ] Audit rows written automatically on `CreateBrand`, `UpdateBrand`, `ArchiveBrand`, `CreateRestaurant`, `UpdateRestaurant`, `ArchiveRestaurant` commands.
- [ ] Paginated GET endpoints return correct filtered results.
- [ ] `pwsh ./scripts/phase-guard.ps1 -PhaseName "Audit System" -Quick` exits 0.

---

### 6.4 Phase 4 (Large — Plan Only): `GET /identity-api/users?brandId=` Filter

**Complexity**: Large — requires a server-side join through Brand → Restaurant → UserRestaurant → User in `Identity.API`.

**Blocking**: Admin CRM Phase 4 (`BrandStaffPage`).

**Frontend stub strategy**: `BrandStaffPage` renders `<ZoneSplash />` with banner: "Staff list by brand is not yet available — view staff from the Restaurants tab."

#### Proposed Implementation

Add `brandId` optional filter to the existing `GET /identity-api/v1/users` endpoint:

```csharp
// In GetUsersHandler (or equivalent)
if (request.BrandId.HasValue)
{
    // Join: AspNetUsers → UserRestaurants → Restaurants WHERE BrandId == request.BrandId
    query = query.Where(u =>
        _context.UserRestaurants
            .Where(ur => ur.UserId == u.Id)
            .Join(_context.Restaurants,
                  ur => ur.RestaurantId,
                  r => r.Id,
                  (ur, r) => r)
            .Any(r => r.BrandId == request.BrandId.Value));
}
```

#### Acceptance Criteria (for future sprint)

- [ ] `GET /identity-api/v1/users?brandId={id}` returns only users assigned to restaurants under that brand.
- [ ] SuperAdmin sees all users under the brand. RestaurantAdmin sees only users in restaurants they manage.
- [ ] Pagination and sorting work consistently with existing `GET /identity-api/v1/users` behavior.
- [ ] Unit test in `Identity.API.Tests` verifies brand-scoped filter.

---

### 6.5 Phase 5 (Large — Plan Only): `Restaurant` Additional Fields

**Complexity**: Large — requires EF Core migration on `Restaurants` table, schema changes to response DTOs, and new Carter endpoints.

**Blocking**: Admin CRM Phase 5 (`AdminRestaurantDetail` full shape, restaurant archive).

**Missing fields**:
- `Status` enum: `active` | `inactive` | `archived`
- `OwnerId` (FK → `AspNetUsers`)
- `OperatingHours` (JSON column or child table with 7 rows)
- `Address` structured (OwnsOne `RestaurantAddress` — line1, line2, city, region, postalCode, country)
- `AuditableEntity<Guid>` base for `CreatedAt`/`UpdatedAt`

**New endpoint**:
- `POST /catalog-api/v1/restaurants/{id}/archive` — sets `Status = "archived"`; returns 409 if active staff assignments exist.

**Frontend stub strategy**: Until deployed, `AdminRestaurantDetail` renders available fields and shows "Not configured" placeholder for missing structured data.

#### Acceptance Criteria (for future sprint)

- [ ] EF Core migration adds all missing columns to `Restaurants` with safe defaults.
- [ ] `GET /catalog-api/v1/restaurants/{id}` returns full `AdminRestaurantDetail` shape.
- [ ] `POST /catalog-api/v1/restaurants/{id}/archive` endpoint implemented.
- [ ] Existing `GET /catalog-api/v1/restaurants` list endpoint unaffected.

---

### 6.6 Phase 6 (Large — Plan Only): `AdminSettings` Singleton Aggregate

**Complexity**: Large — requires a new aggregate, table, and Carter endpoints. No existing schema exists.

**Blocking**: Admin CRM Phase 6 (`AdminSettingsForm`).

**Frontend stub strategy**: Phase 6 of the admin plan ships `RolePermissionMatrix` (no backend dependency) first, then stubs `AdminSettingsPage` with "Platform settings management coming soon."

#### Proposed Architecture

New singleton aggregate in `Catalog.API`:

```
AdminSettings table (catalogdb):
  Id                          uuid PK  (always the same fixed GUID — singleton)
  DefaultTimezone             varchar(100)
  DefaultCurrency             char(3)       -- ISO 4217
  DefaultTaxRate              decimal(5,4)
  ReservationReminderMinutes  int
  MaxReservationPartySize     int
  EnableAutoSubstitute        bool
  EmailFromAddress            varchar(200)
  SupportContactUrl           varchar(500)
  FeatureFlags                jsonb
  UpdatedAt                   timestamptz
```

**Endpoints**:
- `GET /catalog-api/v1/settings` — returns singleton; seeds defaults if table is empty.
- `PUT /catalog-api/v1/settings` — requires `settings:edit` permission.

---

## 7. Cross-Service Communication

- **Catalog.API → Identity.API**: Audit entries record `ActorId` (user GUID from JWT claims). No direct service-to-service call; the actor identity comes from the `ICurrentUserProvider` in the request pipeline.
- **Identity.API → Catalog.API**: The brand-scoped staff filter joins through `Restaurants` which requires `Catalog.API`'s `Restaurants` table. This is best served as Identity.API maintaining its own read-model of the brand→restaurant mapping, OR by an API composition call from the frontend (fetch restaurants for brand from Catalog, then fetch users for those restaurants from Identity and merge client-side).

> [!NOTE]
> The client-side API composition alternative for `brandStaff` (frontend fetches `/catalog-api/brands/{id}/restaurants` → per-restaurant `/identity-api/users?restaurantId={id}` → merge) avoids the server-side join complexity. This is a valid V1 approach if the Identity.API server-side filter is delayed. Document this in the Admin CRM plan as the fallback.

---

## 8. Security Guardrails

> [!CAUTION]
> All new endpoints must enforce authorization via the existing `PermissionAuthorizationHandler`. Never expose admin-tier endpoints without the appropriate `brands:*` or `settings:*` permission check.

| Risk | Mitigation |
|---|---|
| Brand archive deletes data | `ArchiveBrand` is a soft-delete (`IsActive = false`). Physical delete is out of scope. |
| Slug collision | `IX_Brands_Slug` unique index; `ArchiveBrand` handler returns 409 with `BRAND_DUPLICATE_SLUG` type. |
| Audit log data exposure | `GET /audit/*` endpoints scoped by actor's restaurant assignments; SuperAdmin sees all. |
| Cross-brand restaurant access | Existing `ICurrentRestaurantProvider` JWT claim enforcement unchanged. Brand scoping is additive. |
| AdminSettings unauthorized write | `PUT /catalog-api/v1/settings` requires `settings:edit` permission (SuperAdmin only). |

---

## 9. Development Phases

### Phase Overview

| Phase | Name | Complexity | Blocking Frontend Phase |
|:---:|---|:---:|---|
| **1** | Brand `Slug`/`IsActive`/`ArchiveBrand` | Minimal | Admin CRM Phase 2 |
| **2** | New CRM permission seeds | Minimal | Admin CRM Phase 1 |
| **3** | Generic `AuditEntries` system | Large | Admin CRM Phase 3 |
| **4** | `users?brandId=` filter in Identity | Large | Admin CRM Phase 4 |
| **5** | Restaurant additional fields + archive | Large | Admin CRM Phase 5 |
| **6** | `AdminSettings` singleton aggregate | Large | Admin CRM Phase 6 |

---

### Phase 1 — Brand `Slug`, `IsActive`, and `ArchiveBrand`

**Goal**: Unblock Admin CRM Phase 2. Brand entity gains slug uniqueness, soft-delete lifecycle, and an archive endpoint.

**Status**: ⏸ Pending

**Deliverables**:

- [ ] EF Core migration `AddBrandSlugAndIsActive` applied to `catalogdb`.
- [ ] `Brand` entity updated with `Slug` (string, max 100) and `IsActive` (bool, default `true`) properties.
- [ ] `IX_Brands_Slug` unique index created.
- [ ] `GetBrandsHandler` updated to accept `?isActive=` filter; response DTO includes `slug`, `isActive`, `createdAt`, `updatedAt`.
- [ ] `GetBrandByIdHandler` response DTO includes new fields.
- [ ] `CreateBrandHandler` validates slug format (`^[a-z0-9-]+$`), returns 409 on duplicate slug.
- [ ] `UpdateBrandHandler` validates slug on update, handles slug change conflicts.
- [ ] `ArchiveBrandEndpoint.cs` + `ArchiveBrandHandler.cs` in `Features/Brands/ArchiveBrand/`.
- [ ] FluentValidation rules for slug format.
- [ ] MSW handler additions noted in `OrderlyWeb` plan (consumer-side).

**Exit criteria**: `POST /catalog-api/v1/brands/{id}/archive` returns 200 with `{ id, isActive: false }` when no active restaurants exist; returns 409 with `BRAND_HAS_ACTIVE_RESTAURANTS` type when they do. `pwsh ./scripts/phase-guard.ps1 -PhaseName "Brand Archive" -Quick` exits 0.

---

### Phase 2 — CRM Permission Seeds

**Goal**: Ensure the 9 new CRM permissions exist in `Identity.API` so the frontend `usePermission` hook resolves them from JWT claims.

**Status**: ⏸ Pending

**Deliverables**:

- [ ] Extend `PermissionSeeder` (or equivalent) in `Identity.API` with the 9 new CRM permissions (see §6.2 table).
- [ ] Assign permissions to `SuperAdmin`, `RestaurantAdmin`, and `Manager` roles per the matrix in §6.2.
- [ ] Seeder runs idempotently (check-before-insert; safe on re-run against populated DB).
- [ ] Integration test (or manual smoke) verifies JWT for a `SuperAdmin` user includes `brands:create` claim.

**Exit criteria**: A freshly seeded database includes all 9 new permissions. Existing 25 permissions are unaffected. `pwsh ./scripts/phase-guard.ps1 -PhaseName "CRM Permissions" -Quick` exits 0.

---

### Phase 3 — Generic `AuditEntries` System (Large — Future Sprint)

**Goal**: Provide an entity-level CRUD audit trail for Brand, Restaurant, and Staff entities in Catalog.API.

**Status**: 🔒 Blocked (pending sprint allocation)

**Prerequisites**:
- Phase 1 of this plan must be complete (Brand entity is stable).
- Architecture decision on audit instrumentation approach (pipeline behavior vs. domain events vs. EF Core `SaveChangesInterceptor`) must be made before implementation.

**Deliverables**: See §6.3 Acceptance Criteria.

**Frontend fallback until delivered**: Every audit tab on brand/restaurant detail renders `<ZoneSplash />` with "Audit trail coming soon." placeholder.

---

### Phase 4 — `users?brandId=` Filter (Large — Future Sprint)

**Goal**: Allow frontend to query staff members scoped to a brand.

**Status**: 🔒 Blocked (pending sprint allocation)

**Deliverables**: See §6.4 Acceptance Criteria.

**Frontend fallback until delivered**: `BrandStaffPage` renders `<ZoneSplash />` with banner: "Staff list by brand is not yet available — view staff from the Restaurants tab."

**Alternative V1 approach**: Client-side composition — frontend fetches restaurants for brand, then fans out per-restaurant staff queries and merges. Acceptable for low restaurant counts (<20 per brand).

---

### Phase 5 — Restaurant Additional Fields + Archive (Large — Future Sprint)

**Goal**: Extend `Restaurant` entity with Status, OwnerId, OperatingHours, and structured Address to support full admin restaurant management.

**Status**: 🔒 Blocked (pending sprint allocation)

**Deliverables**: See §6.5 Acceptance Criteria.

---

### Phase 6 — `AdminSettings` Singleton Aggregate (Large — Future Sprint)

**Goal**: Provide a platform-level settings store editable by SuperAdmin.

**Status**: 🔒 Blocked (pending sprint allocation)

**Deliverables**: See §6.6 proposed architecture.

---

## 10. Technical Considerations

### 10.1 Cross-Cutting

**Slug generation**: Frontend should auto-generate slugs from the brand name input (e.g. `"Acme Corp" → "acme-corp"`) to reduce friction, but the server validates and enforces uniqueness. Conflict UX is already specified in the Admin CRM plan.

**Audit instrumentation timing**: Implement the MediatR pipeline behavior for audit AFTER Phase 1 is complete and the `AuditEntries` table migration is confirmed. Pre-instrumenting commands before the table exists creates migration ordering problems.

**Permission seeding order**: Phase 2 (permissions) can run independently of Phase 1 (brand schema). Both are minimal and can be shipped in the same PR if desired.

**EF migrations in CI**: All migrations must be verified with `dotnet ef migrations script --idempotent` before merging. The CI pipeline in `phase-guard.ps1` validates build; migration verification is a separate manual step until a migration-check script is added.

---

## Changelog

### v1.0 (2026-10-17) — Initial Plan

- Created from OrderlyWeb roadmap gap analysis (master plan v1.2).
- Classified 9 backend gaps by complexity (minimal vs. large).
- Confirmed `MergeTables`/`SplitTables` endpoints already exist — no action needed.
- Confirmed discount resolution is server-side — no frontend `discountApi` slice needed.
- Scoped Phase 1 (Brand archive + Slug) and Phase 2 (permission seeds) as immediately actionable.
- Phases 3–6 planned with detailed specs and frontend fallback strategies for each.
