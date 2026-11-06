# Orderly.DevMCP.Server — Restaurant Lifecycle & Full CRUD Capabilities — Implementation Plan

> Scope: Enhances `Orderly.DevMCP.Server` with complete lifecycle and CRUD capabilities for Restaurant tenants across the microservices ecosystem (Catalog, Identity, Kitchen, Basket, Ordering). Enables AI agents and developers to scaffold, configure, update, inspect, and purge restaurants, staff, floorplan tables, and menus through Model Context Protocol (MCP) tools.

---

## Status

> **Plan version**: `v1.2` (2026-11-06) — all 6 phases complete. 28 tools registered and verified.
> **Current state**: ✅ Done on 2026-11-06

| Phase | Name | Status |
|:-----:|---|:-----:|
| 1 | Core Profile & Operational Settings CRUD | ✅ Done (2026-11-06) |
| 2 | Floorplan & Table Management | ✅ Done (2026-11-06) |
| 3 | Menu Hierarchy, Items, Variations & Inventory CRUD | ✅ Done (2026-11-06) |
| 4 | Staff Provisioning Bridge via Identity Login | ✅ Done (2026-11-06) |
| 5 | Composite Scaffolding & Multi-DB Cascading Purge | ✅ Done (2026-11-06) |
| 6 | Wire Testing, OpenAPI Sync & Tool Registration | ✅ Done (2026-11-06) |

> **Legend**: ✅ Done · 🚧 In progress · ⏸ Pending · 🔒 Blocked

> **Commit messages**: Conventional Commits (`feat:`, `docs:`, `chore:`, `test:`, `fix:`). Short subject, ≤72 chars, imperative mood, no trailing period. Example: `feat(mcp): add restaurant profile crud tools`.

> **Update rule**: On every phase completion, the plan MUST be updated in the same commit as the phase work. The plan is the source of truth for what was decided and what shipped; a phase that ships without a plan update is a phase that drifted.

---

## 0. Skill & documentation conventions

### 0.1 Skill mandate — `csharp-developer` & Node.js MCP
> **All implementation work on this plan MUST maintain compatibility with the Orderly backend architecture (.NET 10 Minimal APIs, PostgreSQL, MSSQL, Redis, RabbitMQ) and adhere to the official Model Context Protocol TypeScript SDK (`@modelcontextprotocol/sdk`).**

### 0.2 Code-quality guard rails
- **Runtime**: Node.js 22.6+ native type stripping (`engines.node: ">=22.6.0"`). No build step (`tsc --noEmit` only for typecheck validation).
- **Environment Gate**: Strictly dev-only (`NODE_ENV === 'development'`). Binds to localhost / allowlisted dev hosts via `assertDevHost`.
- **Validation**: Strict runtime validation using Zod (`z.object(...)`) for all tool input parameters. No implicit `any`.
- **Cache Integrity & Key Format**: In `Catalog.API`, only the menu snapshot and ingredient availability are cached in Redis (see `CacheKeys.cs`). Keys MUST use the 32-character GUID in **"N" format (no hyphens)**: `catalog:menu:{restaurantId:N}` and `catalog:ingredients:{restaurantId:N}`. (Tables and Restaurant profile rows are not cached in Redis).
- **Entity Identity & ShortId Constraints**: `ShortId` columns across `Restaurants`, `Brands`, `MenuItems`, and `Tables` have a database length limit of `HasMaxLength(16)` and a `UNIQUE` index. Generated IDs must follow `prefix + nanoid` conventions (e.g. `rst_`, `tbl_`, `itm_`, `brd_`) and never exceed 16 characters.

---

## 1. Context

In Orderly, a **Restaurant** is the fundamental multitenant boundary of the system:
1. `Catalog.API` owns restaurant metadata, brand linkage, currency (`MXN`), timezone (`America/Monterrey`), tax rates, operational toggles (auto-confirm, turnover), physical floorplans (tables), and the full menu hierarchy (categories, items, variations, recipe ingredients, kitchen coursing).
2. `Identity.API` owns staff credentials and scopes their permissions through the `UserRestaurants` join entity (`UserId`, `RestaurantId`, `IsDefault`) and role assignments (`SuperAdmin`, `RestaurantAdmin`, `Manager`, `KitchenManager`, `Waiter`, `KitchenStaff`, `Cashier`, `Host`).
3. `Basket.API` partitions active shopping carts per tenant using Marten multi-tenancy (`basketdb`) and Redis.
4. `Kitchen.API` routes incoming preparation tickets based on the restaurant context and station layouts.
5. `Ordering.API` stores orders and financial records in `orderdb` (SQL Server) filtered by `restaurantId`.

### The Problem Today
The existing `Orderly.DevMCP.Server` has 20 tools covering mock order creation, flow tracing, and static database seeding (`seed_test_menu` which only injects a hardcoded 5-category, 25-item static menu into a single restaurant). It has **no capabilities** to:
- Create new bespoke restaurants or brands dynamically.
- Update operational parameters (tax rates, turnover time, currency, auto-confirm toggles).
- Customize floorplans and manage table numbers/capacities/positions.
- Perform fine-grained CRUD on categories, subcategories, items, variations, kitchen course numbers, and ingredient stocks.
- Provision staff accounts and bind them to the restaurant with proper roles and credentials.
- Perform clean tenant teardown / resetting across all microservice databases.

Frontend development on the Orderly Web CRM, KDS, and mobile app is blocked from testing onboarding flows, settings configuration, table mapping, and menu engineering without manual SQL scripting.

---

## 2. Goal

Deliver a comprehensive restaurant lifecycle and CRUD toolset inside `Orderly.DevMCP.Server`:
1. **Granular CRUD Tools**: Enable targeted reading, creation, modification, and deletion of Restaurant settings, Tables, Menu items/categories/variations, and Staff assignments.
2. **Composite Scaffolding Tool (`scaffold_restaurant`)**: Provide a single-call high-level tool that provisions a complete, fully functioning restaurant from scratch (Brand + Restaurant + Floorplan Tables + Full Menu + Staff Accounts) for instant frontend preview and test setup.
3. **Cross-Service Teardown Tool (`purge_restaurant`)**: Provide a safe, transactional cascading purge tool that completely wipes a restaurant tenant and its associated entities across Catalog, Identity, Basket, Kitchen, and Ordering databases, respecting foreign key restrictions.

---

## 3. Out of scope

- **Production Usage**: DevMCP is hard-gated for local and LAN development only (`NODE_ENV=development`). It will never be deployed to production.
- **Modifying Backend Relational Schemas**: This plan adapts to the existing C# EF Core and Marten schemas without introducing DB migrations unless a critical bug or gap is uncovered.
- **Payment Gateway Credential Configuration**: Real payment processor onboarding (Stripe/Clover keys) is out of scope for local dev mock environments.

---

## 4. Tech decisions & Implementation Architecture

### 4.1 The Hybrid Execution Architecture: Why This Way?

| Capability Area | Selected Driver | Why this was decided |
| :--- | :--- | :--- |
| **Staff & Auth (`Identity.API`)** | **HTTP API Bridge with Dev SuperAdmin Login** | **OpenIddict Local Server Security**: Unlike the other services, `Identity.API` does **not** have the `DevHs256` fallback scheme wired; it strictly validates tokens against its local OpenIddict authority. Rather than modifying production-adjacent identity code, DevMCP authenticates against `POST /v1/auth/login` using seeded development credentials (`admin@orderly.com` / `Admin@123456`, per `DataSeeder.cs:252`) and acquires a genuine OpenIddict bearer token. It then calls Carter endpoints (`POST /v1/users`, `POST /v1/users/{id}/roles`, `POST /v1/users/{id}/restaurants`), guaranteeing proper PBKDF2 password hashing and security stamps. |
| **Menu, Tables & Profile (`Catalog.API`)** | **Direct SQL (`ctx.pg.catalog`) + Redis Cache Bust** | **Speed & Atomic Setup**: Scaffolding a 5-category, 30-item menu via HTTP requires 40+ serialized HTTP roundtrips. Direct SQL transactions execute in <50ms. Since `Catalog.API` caches full menu snapshots and ingredient availability in Redis, DevMCP issues explicit Redis `DEL` commands on `catalog:menu:{id:N}` and `catalog:ingredients:{id:N}` (32-character GUID without hyphens) immediately after committing SQL. |
| **Tenant Teardown (`purge_restaurant`)** | **Reuse `flush_orders` + Reverse Topological SQL Deletion** | **Foreign Key Restrictions & Code Reuse**: Production microservices intentionally do not expose a public "delete everything for tenant X" endpoint. In `CatalogDbContext`, `Tables`, `MenuItems`, `MenuCategories`, `Reservations`, and `WalkInQueues` all reference `Restaurant` with `DeleteBehavior.Restrict`. DevMCP reuses the battle-tested `flush_orders` routine for OrderDb and KitchenDb, then executes a strict bottom-up cascade in CatalogDb, BasketDb, and IdentityDb. |

### 4.2 Composite vs. Granular Tooling Design

An AI assistant or developer interacting with DevMCP suffers from context exhaustion if forced to execute 30 tool calls to stand up a test restaurant. Conversely, if only a monolithic "all-or-nothing" seeding tool exists, fine-grained UI debugging (e.g. testing what happens when a dish price changes or a table is deleted) becomes impossible.

**Decision**: Implement **both**:
- **Composite Tool (`scaffold_restaurant`)**: Takes an optional preset (`bistro`, `pizzeria`, `taqueria`, `minimal`) or custom JSON and creates Brand, Restaurant, Floorplan, Menu, and Staff in a single atomic flow.
- **Granular Domain Tools (`manage_restaurant_*`)**: Dedicated tools covering focused CRUD operations for settings, tables, menu entities, and staff.

### 4.3 Explicit ShortId Generation (<16 chars)

In `Catalog.API`, entities (`Restaurants`, `Brands`, `MenuItems`, `Tables`) have a `ShortId` column with a `UNIQUE` index and `HasMaxLength(16)`. If omitted, EF Core migrations set default `''`, causing a duplicate key violation on the second insert.
**Decision**: DevMCP utilizes its existing `src/util/short-id.ts` nanoid generator with domain prefixes:
- Restaurant: `rst` (`rst_` + 10 chars = 14 chars)
- Table: `tbl` (`tbl_` + 10 chars = 14 chars)
- Brand: `brd` (`brd_` + 10 chars = 14 chars)
- MenuItem: `itm` (`itm_` + 10 chars = 14 chars)
- MenuCategory: `cat` (`cat_` + 10 chars = 14 chars)

### 4.4 Cache Invalidation Contract

`Catalog.API` uses Redis distributed caching solely for full menu snapshots and ingredient availability (see `Catalog.API/Caching/CacheKeys.cs`). It uses the GUID's 32-character lowercase "N" representation without hyphens to save bytes.
**Decision**: Create a dedicated helper `src/util/cache-invalidation.ts` that removes:
- `catalog:menu:${restaurantId.replace(/-/g, '').toLowerCase()}`
- `catalog:ingredients:${restaurantId.replace(/-/g, '').toLowerCase()}`
Note: Tables and Restaurant profile entities are read directly from PostgreSQL; no Redis invalidation is required for table or profile changes.

---

## 5. Folder layout & File Changes

```
OrderlyMicroservices/orderly-microservices/Orderly.DevMCP.Server/
├── src/
│   ├── config/
│   │   ├── env.ts
│   │   └── services.ts
│   ├── tools/
│   │   ├── restaurant-lifecycle.ts       ← [NEW] Core CRUD: create/update/get/purge restaurant
│   │   ├── restaurant-tables.ts          ← [NEW] Table & floorplan management
│   │   ├── restaurant-menu.ts            ← [NEW] Menu category, item, variation, inventory CRUD
│   │   ├── restaurant-staff.ts           ← [NEW] Staff creation & multitenant role binding bridge
│   │   ├── restaurant-scaffold.ts        ← [NEW] Composite one-shot provisioning tool
│   │   ├── infrastructure.ts             ← [MODIFIED] Export flush_orders internals for reuse
│   │   ├── data-seeding.ts               ← Existing tools (seed_test_menu, create_mock_order)
│   │   └── types.ts                      ← Updated ToolContext & interfaces
│   ├── util/
│   │   ├── cache-invalidation.ts         ← [NEW] Redis key eviction ("N" format keys)
│   │   ├── identity-api-client.ts        ← [NEW] HTTP bridge with SuperAdmin dev login
│   │   ├── short-id.ts                   ← Prefix-aware nanoid generator (max 16 chars)
│   │   └── sanitize.ts
│   └── index.ts                          ← Registers new restaurant tool groups
├── resources/
│   └── seeds/
│       ├── presets/                      ← [NEW] Scaffolding archetypes (pizzeria, bistro, cafe)
│       └── catalog-seed.json
└── test/
    └── wire/
        └── restaurant-lifecycle.wire.test.ts ← [NEW] Wire contract validation
```

---

## 6. Specification

### 6.1 Core Restaurant Profile & Settings (`restaurant-lifecycle.ts`)

#### `create_restaurant`
*   **Input**:
    ```typescript
    z.object({
      name: z.string().min(1).max(100),
      brandId: z.string().uuid().optional(), // If omitted, creates default Brand
      brandName: z.string().optional(),
      address: z.string().default('Av. Constitución 1000, Monterrey, NL'),
      phoneNumber: z.string().default('+528180001111'),
      email: z.string().email(),
      currency: z.string().length(3).default('MXN'),
      timeZone: z.string().default('America/Monterrey'),
      taxRate: z.number().min(0).max(1).default(0.16),
      estimatedTurnoverMinutes: z.number().int().positive().default(45),
      autoConfirmOrders: z.boolean().default(false),
      autoConfirmReservations: z.boolean().default(false),
      allowAutoSubstitute: z.boolean().default(true),
    })
    ```
*   **Behavior**: Inserts brand (if needed) and restaurant into `catalogdb`. Generates unique `ShortId` (`rst_...`).
*   **Output**: `{ id: string, shortId: string, brandId: string, name: string, currency: string, timeZone: string }`.

#### `update_restaurant`
*   **Input**: `restaurantId` (UUID or ShortId) plus optional partial fields (name, address, taxRate, autoConfirmOrders, currency, timeZone, etc.).
*   **Behavior**: Updates fields in `catalogdb` with `LastModifiedAt = CURRENT_TIMESTAMP`.
*   **Output**: `{ success: true, updatedFields: string[] }`.

#### `get_restaurant`
*   **Input**: `restaurantId` (UUID or ShortId).
*   **Behavior**: Returns complete aggregate view: Profile details, Brand info, Table counts, Category/Item summary counts, and assigned Staff count from `identitydb`.

---

### 6.2 Floorplan & Table Management (`restaurant-tables.ts`)

#### `manage_restaurant_tables`
*   **Input**:
    ```typescript
    z.object({
      restaurantId: z.string(),
      action: z.enum(['create', 'update', 'delete', 'bulk_layout', 'list', 'update_status']),
      tableId: z.string().uuid().optional(),
      tableNumber: z.string().optional(),
      capacity: z.number().int().positive().optional(),
      positionX: z.number().int().optional(),
      positionY: z.number().int().optional(),
      shape: z.enum(['round', 'square', 'rectangle']).default('rectangle'),
      status: z.enum(['Available', 'Occupied', 'Reserved', 'Cleaning', 'NeedsAttention']).optional(),
      tables: z.array(z.object({
        tableNumber: z.string().max(10),
        capacity: z.number().int().positive(),
        positionX: z.number().int(),
        positionY: z.number().int(),
        shape: z.enum(['round', 'square', 'rectangle']).default('rectangle'),
      })).optional(), // Used for bulk_layout
    })
    ```
*   **Behavior**:
    - Generates `tbl_...` ShortId on insert.
    - Enforces uniqueness of `(RestaurantId, TableNumber)`.
    - Handles status transitions matching `BuildingBlocks.Enums.TableStatus`.
    - Deletion checks and cleans `MergedTables` entries where table is parent or child.

---

### 6.3 Menu & Inventory Management (`restaurant-menu.ts`)

#### `manage_restaurant_menu`
*   **Input**:
    ```typescript
    z.object({
      restaurantId: z.string(),
      action: z.enum([
        'create_category', 'update_category', 'delete_category',
        'create_item', 'update_item', 'delete_item',
        'add_variation', 'delete_variation',
        'update_availability', 'adjust_ingredient_stock', 'list_menu'
      ]),
      categoryId: z.number().int().optional(),
      subCategoryId: z.number().int().optional(),
      categoryName: z.string().optional(),
      displayOrder: z.number().int().optional(),
      itemId: z.string().uuid().optional(),
      name: z.string().optional(),
      description: z.string().optional(),
      basePrice: z.number().min(0).optional(),
      imageUrl: z.string().url().default('https://images.unsplash.com/photo-1546069901-ba9599a7e63c'),
      prepTimeMinutes: z.number().int().default(15),
      defaultCourseNumber: z.number().int().min(1).max(5).default(1),
      itemType: z.enum(['Regular', 'Combo', 'Promo', 'Seasonal']).default('Regular'),
      availabilityStatus: z.enum(['Available', 'Limited', 'Unavailable']).default('Available'),
      variations: z.array(z.object({
        name: z.string(),
        variationValue: z.string(),
        priceModifier: z.number(),
        isDefault: z.boolean().default(false),
      })).optional(),
      ingredientId: z.number().int().optional(),
      stockDelta: z.number().optional(),
    })
    ```
*   **Behavior**:
    - Full control over menu hierarchy. Generates `itm_...` ShortId for items.
    - Populates `default_course_number` for kitchen display routing.
    - Automatically invalidates `catalog:menu:{id:N}` and `catalog:ingredients:{id:N}` on any write.

---

### 6.4 Staff Provisioning Bridge (`restaurant-staff.ts`)

#### `manage_restaurant_staff`
*   **Input**:
    ```typescript
    z.object({
      restaurantId: z.string(),
      action: z.enum(['create_staff', 'assign_role', 'link_restaurant', 'remove_staff', 'list_staff']),
      userId: z.string().uuid().optional(),
      email: z.string().email().optional(),
      password: z.string().min(8).optional().default('Password123!'),
      firstName: z.string().optional(),
      lastName: z.string().optional(),
      phoneNumber: z.string().optional(),
      role: z.enum([
        'SuperAdmin', 'RestaurantAdmin', 'Manager', 'KitchenManager',
        'Waiter', 'KitchenStaff', 'Host', 'Cashier'
      ]).optional(),
      isDefault: z.boolean().default(true),
    })
    ```
*   **Behavior**:
    - `identity-api-client.ts` logs in via `POST /v1/auth/login` using `admin@orderly.com / Admin@123456` to acquire a dev OpenIddict access token (cached with TTL).
    - Calls Carter endpoint `POST /v1/users` passing `Roles` and `RestaurantIds` in a single request.
    - Supports `assign_role` (`POST /v1/users/{id}/roles`) and `link_restaurant` (`POST /v1/users/{id}/restaurants`).
    - Returns credentials and assigned permissions.

---

### 6.5 Composite Scaffolding & Reverse Topological Purge

#### `scaffold_restaurant`
*   **Input**:
    ```typescript
    z.object({
      name: z.string(),
      preset: z.enum(['bistro', 'pizzeria', 'taqueria', 'minimal']).default('bistro'),
      tableCount: z.number().int().min(1).max(50).default(12),
      createStaff: z.boolean().default(true),
      taxRate: z.number().default(0.16),
      currency: z.string().default('MXN'),
      timeZone: z.string().default('America/Monterrey'),
    })
    ```
*   **Behavior**: In a single operation:
    1. Creates Brand and Restaurant records with generated `ShortId`.
    2. Bulk-inserts a floorplan of N tables with coordinates, capacities, and unique table numbers.
    3. Loads preset archetype from `resources/seeds/presets/{preset}.json` and inserts categories, subcategories, items, and variations.
    4. Provisions 4 staff accounts via the Identity login bridge: 1 Manager, 1 KitchenManager, 1 Waiter, 1 Cashier.
    5. Invalidates Redis `catalog:menu:{id:N}`.
*   **Output**: Complete manifest of created IDs (`restaurantId`, `shortId`, staff credentials, table count, menu count).

#### `purge_restaurant`
*   **Input**:
    ```typescript
    z.object({
      restaurantId: z.string().uuid(),
      confirmText: z.string(), // Must match "PURGE {restaurantId}"
      dryRun: z.boolean().default(false),
    })
    ```
*   **Behavior (Reverse Topological Deletion)**:
    1. **OrderDb & KitchenDb**: Invokes `flush_orders` routine to remove orders, order items, activities, bills, and kitchen tickets.
    2. **BasketDb**: Removes Marten basket tenant documents for `restaurantId`.
    3. **IdentityDb**: Deletes `UserRestaurants` linkages for `restaurantId` (preserves users assigned to other restaurants).
    4. **CatalogDb (Strict Bottom-Up Cascade)**:
       - `DELETE FROM "MergedTables" WHERE "ParentTableId" IN (...) OR "ChildTableId" IN (...)`
       - `DELETE FROM "Reservations" WHERE "RestaurantId" = $1`
       - `DELETE FROM "WalkInQueues" WHERE "RestaurantId" = $1`
       - `DELETE FROM "Tables" WHERE "RestaurantId" = $1`
       - `DELETE FROM "MenuItemIngredients" WHERE "MenuItemId" IN (...)`
       - `DELETE FROM "MenuItemVariations" WHERE "MenuItemId" IN (...)`
       - `DELETE FROM "ComboItems" WHERE "ComboMenuItemId" IN (...)`
       - `DELETE FROM "PriceHistory" WHERE "RestaurantId" = $1`
       - `DELETE FROM "MenuItems" WHERE "RestaurantId" = $1`
       - `DELETE FROM "MenuSubCategories" WHERE "CategoryId" IN (...)`
       - `DELETE FROM "MenuCategories" WHERE "RestaurantId" = $1`
       - `DELETE FROM "Restaurants" WHERE "Id" = $1`
    5. **Redis Cache**: Evicts `catalog:menu:{id:N}` and `catalog:ingredients:{id:N}`.

---

## 7. Integration Points

```
┌────────────────────────────────────────────────────────┐
│               Orderly.DevMCP.Server                    │
└────────────────────────────────────────────────────────┘
     │                    │                     │
     │ Direct SQL         │ OpenIddict Token    │ Direct SQL /
     │ + Redis "N" DEL    │ HTTP Bridge         │ flush_orders Reuse
     ▼                    ▼                     ▼
[ catalogdb + Redis ] [ Identity.API ]      [ orderdb / kitchendb / basketdb ]
(PostgreSQL + Redis)  (YARP/HTTP + Login)   (MSSQL / PG / Marten)
  - Restaurants         - POST /v1/auth/login - Orders & Line Items
  - Tables & Layout     - POST /v1/users      - Kitchen Tickets
  - Menus & Stock       - UserRestaurants     - Tenant Baskets
```

1. **Catalog Database (`ctx.pg.catalog`)**: Direct connection for low-latency relational CRUD.
2. **Identity Service (`serviceUrl('identity')`)**: REST calls via internal HTTP port with bearer token obtained from dev SuperAdmin login.
3. **Redis (`ctx.redis`)**: Cache eviction executing `DEL` on `catalog:menu:{id:N}` and `catalog:ingredients:{id:N}`.
4. **OrderDb (`ctx.mssql`) & KitchenDb (`ctx.pg.kitchen`)**: Cleaned via `flush_orders` reuse during `purge_restaurant`.

---

## 8. Security Guardrails

> [!CAUTION]
> **DevMCP contains destructive tenant management tools.** Tools must prevent accidental data deletion or execution in non-development environments.

| Risk | Mitigation |
|---|---|
| Accidental tenant purge | `purge_restaurant` enforces exact string matching (`PURGE <guid>`), rate-limits execution, and supports `dryRun`. |
| Production leakage | Process boot asserts `NODE_ENV === 'development'` and validates connecting client against `assertDevHost`. |
| Password storage security | Staff passwords created via the Identity bridge are processed exclusively by ASP.NET Core Identity's official password hasher. |
| Cache inconsistency | Direct SQL mutations immediately execute Redis `DEL` with formatted 32-character hex keys. |

---

## 9. Development Phases

### Phase overview

| Phase | Name | Deliverables | Goal |
|:---:|---|---|---|
| **1** | Core Profile & Settings | `create_restaurant`, `update_restaurant`, `get_restaurant` | Enable restaurant profile CRUD, ShortId generation, and settings management. |
| **2** | Floorplan & Tables | `manage_restaurant_tables` | Provide table layout creation, status updates, and floorplan configuration. |
| **3** | Menu & Inventory | `manage_restaurant_menu` | Full CRUD for categories, items, variations, coursing, and Redis "N"-key eviction. |
| **4** | Staff Provisioning Bridge | `manage_restaurant_staff` + Identity Login Bridge | Authenticate against Identity and provision staff with roles and restaurant mapping. |
| **5** | Composite Scaffolding & Purge | `scaffold_restaurant`, `purge_restaurant` | One-shot tenant provisioning and reverse-topological cascading purge. |
| **6** | Wire Tests & Registration | `index.ts` registration + wire tests in `test/wire/` | Ensure complete JSON-RPC MCP conformance and documentation. |

---

### Phase 1 — Core Profile & Settings
**Goal**: Allow creating, updating, and querying restaurant metadata and operational settings.
**Deliverables**:
- [x] Implement `src/util/cache-invalidation.ts` with "N" format helper.
- [x] Implement `src/tools/restaurant-lifecycle.ts` with `create_restaurant`, `update_restaurant`, and `get_restaurant`.
- [x] Support custom ShortId generation with `rst_` prefix (capped at 16 chars).
**Exit criteria**: Running `create_restaurant` returns valid UUID and ShortId; `get_restaurant` reflects persisted changes in `catalogdb`.

---

### Phase 2 — Floorplan & Tables
**Goal**: Enable dynamic floorplan creation and table status management.
**Deliverables**:
- [x] Implement `src/tools/restaurant-tables.ts` (`manage_restaurant_tables`).
- [x] Support individual table CRUD and `bulk_layout` batch insert.
- [x] Enforce unique constraint `(RestaurantId, TableNumber)` and handle `MergedTables` dependencies.
**Exit criteria**: Executing `manage_restaurant_tables(action='bulk_layout')` creates 12 tables with unique `tbl_` ShortIds and coordinate positions.

---

### Phase 3 — Menu Hierarchy, Items, Variations & Inventory
**Goal**: Deliver full CRUD capabilities for menu engineering with coursing and Redis eviction.
**Deliverables**:
- [x] Implement `src/tools/restaurant-menu.ts` (`manage_restaurant_menu`).
- [x] Support category hierarchy, item creation with `default_course_number`, base prices, images, and variation options.
- [x] Implement ingredient stock adjustments and availability toggles.
- [x] Invalidate Redis `catalog:menu:{id:N}` on every modification.
**Exit criteria**: Can create a Category, add a MenuItem with 2 variations and course number, and verify the Redis cache key is cleared.

---

### Phase 4 — Staff Provisioning Bridge via Identity Login
**Goal**: Bridge DevMCP to `Identity.API` for secure staff onboarding and restaurant binding.
**Deliverables**:
- [x] Implement `src/util/identity-api-client.ts` with auto-login via `admin@orderly.com / Admin@123456` to acquire and cache an OpenIddict bearer token.
- [x] Implement `src/tools/restaurant-staff.ts` (`manage_restaurant_staff`).
- [x] Support user creation, role assignment (including `KitchenManager`), and `UserRestaurants` default mapping.
**Exit criteria**: `manage_restaurant_staff` successfully creates a new user, assigns `KitchenManager` role, and verifies the user in `identitydb`.

---

### Phase 5 — Composite Scaffolding & Multi-DB Cascading Purge
**Goal**: Provide one-shot instant restaurant setup and clean reverse-topological tenant obliteration.
**Deliverables**:
- [x] Add seed presets (`bistro`, `pizzeria`, `taqueria`) in `resources/seeds/presets/`.
- [x] Implement `src/tools/restaurant-scaffold.ts` (`scaffold_restaurant`).
- [x] Implement `purge_restaurant` in `src/tools/restaurant-lifecycle.ts` reusing `flush_orders` and executing reverse-topological SQL deletion.
**Exit criteria**: Running `scaffold_restaurant` produces a ready-to-use restaurant in <1 second; `purge_restaurant` cleanly removes all tenant records without foreign key errors.

---

### Phase 6 — Wire Testing, Tool Registration & Verification
**Goal**: Complete end-to-end integration and MCP protocol verification.
**Deliverables**:
- [x] Register all new tools in `src/index.ts`.
- [x] Author wire tests in `test/wire/restaurant-lifecycle.wire.test.ts`.
- [x] Update `Orderly.DevMCP.Server/README.md` documentation.
- [x] Run `pnpm typecheck` and `pnpm test`.
**Exit criteria**: All wire tests pass and the server boot log reports the expanded tool count over Streamable HTTP.

---

## 10. Technical considerations & Gotchas

### 10.1 ShortId Length Constraint
In `catalogdb`, `Restaurants`, `Brands`, `MenuItems`, and `Tables` have unique indexes on `ShortId` with `HasMaxLength(16)`. The generator must use a 3-letter prefix plus underscore (`rst_`) and at most 12 random characters to never exceed the 16-character column limit.

### 10.2 OpenIddict Auth vs. DevHs256
`Identity.API` does not use the `DevHs256` policy scheme; it validates tokens through `options.UseLocalServer()`. Calling it with an HS256 dev token will fail. The HTTP bridge must log in as `admin@orderly.com / Admin@123456` against `POST /v1/auth/login` to obtain an official asymmetric token.

### 10.3 Redis "N" Format Cache Keys
`Catalog.API` uses `restaurantId:N` (lowercase 32-hex GUID without hyphens). Any key deletion using hyphens (`catalog:menu:11111111-2222-...`) will silently fail to match the real key, leaving the running service with a stale menu cache.

### 10.4 Foreign Key Deletion Order
`CatalogDbContext` sets `DeleteBehavior.Restrict` on `Tables`, `MenuItems`, and `MenuCategories`. Attempting to delete `Restaurants` before deleting dependent child rows triggers a Postgres foreign key violation (`23503`). Deletions must proceed in strict reverse topological order.

### 10.5 Reuse Existing Flush Logic
`flush_orders` in `src/tools/infrastructure.ts` already purges OrderDb and KitchenDb tables safely. `purge_restaurant` reuses this logic to avoid duplicate deletion scripts for SQL Server and Kitchendb.

---

## Changelog

### v1.1 (2026-11-06) — Architectural Fact-Check & Gap Remediation
- Bumped version from `v1.0` to `v1.1`.
- **Identity Bridge**: Corrected auth strategy from HS256 dev tokens to OpenIddict dev SuperAdmin login (`admin@orderly.com` / `Admin@123456`) because `Identity.API` strictly uses `UseLocalServer()` and rejects `DevHs256`.
- **Redis Cache Keys**: Corrected cache key formats to 32-character hex without hyphens (`catalog:menu:{id:N}`, `catalog:ingredients:{id:N}`) and clarified that tables and restaurant profiles are not cached in Redis.
- **Cascade Deletion**: Documented reverse topological deletion order to prevent PostgreSQL foreign key constraint violations (`DeleteBehavior.Restrict`).
- **Tool Reuse**: Explicitly incorporated `flush_orders` from `src/tools/infrastructure.ts` for the OrderDb and KitchenDb phases of `purge_restaurant`.
- **Schema Alignment**: Updated currency default to `MXN`, timezone default to `America/Monterrey`, added `KitchenManager` role, and included `default_course_number` on menu items.

### v1.0 (2026-11-06) — Initial Architectural Blueprint
- Authored initial plan based on `_template.md`.
- Formulated the Pragmatic Hybrid Execution Architecture.
