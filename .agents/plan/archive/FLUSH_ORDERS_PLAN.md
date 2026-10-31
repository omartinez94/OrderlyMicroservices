# DevMCP flush_orders Tool — Implementation Plan

> Scope: Implementation plan for adding the `flush_orders` tool to `Orderly.DevMCP.Server`, enabling transactional, schema-preserving purging of orders, order items, activities, bills, and kitchen tickets across `OrderDb` (MSSQL) and `Kitchendb` (Postgres), with safe outbox cleanup.

---

## Status

> **Plan version**: `v1.2` (2026-10-28) — `{{MINOR}}` increments after each phase completion; `{{MAJOR}}` is reserved for breaking restructures of the plan itself.
> **Current state**: ✅ Done on 2026-10-28

| Phase | Name | Status |
|:-----:|---|:-----:|
| 1 | Tool Implementation & Transactional Purge | ✅ Done (2026-10-28) |
| 2 | MCP Registration & Schema Wiring | ✅ Done (2026-10-28) |
| 3 | Automated Tests (mocked + opt-in live) | ✅ Done (2026-10-28) |
| 4 | Documentation & Live Verification | ✅ Done (2026-10-28) |

> **Legend**: ✅ Done · 🚧 In progress · ⏸ Pending · 🔒 Blocked

> **Commit messages**: Conventional Commits (`feat:`, `docs:`, `chore:`, `test:`, `fix:`). Short subject, ≤72 chars, imperative mood, no trailing period.

> **Update rule**: on every phase completion, the plan MUST be updated. Per the template workflow this is a **separate plan commit** (`docs: mark Phase N complete in FLUSH_ORDERS_PLAN`) that follows the code commit.

### Changelog

- **v1.2 (2026-10-28)** — All 4 phases implemented and verified. `flush_orders` deployed to DevMCP server, 8 unit tests passed, live DB test passed, wire tests passed (all 20 tools online).
- **v1.1 (2026-10-27)** — Review fixes: PascalCase Postgres identifiers; outbox purge restricted to undispatched rows and unscoped flushes; dead-letter tables included; per-database result reporting; `scope`/`dryRun`/`resetSequences` inputs; bucket rate limit; `phase-guard.ps1` gate; two-commit workflow; explicit test strategy; full tool-count touchpoint list.
- **v1.0 (2026-10-27)** — Initial plan.

---

## 0. Skill & documentation conventions

### 0.1 Skill mandate — Node.js / TypeScript

> **All implementation work on this plan MUST follow the existing DevMCP conventions (ESM, Node type stripping, Zod input schemas, `pino` logging) and Markdown edits MUST comply with the `markdown-lint` skill.**

### 0.2 Code-quality guard rails

- **Quality Gate script**: `phase-guard.ps1` MUST be executed at the end of every phase with the `-Quick` parameter:
  `pwsh ./scripts/phase-guard.ps1 -PhaseName "<Phase Name>" -Quick`
- **Runtime & compilation**: Node.js `>=22.6.0` native type stripping; typecheck via `pnpm run typecheck` (`tsc --noEmit`).
- **Dev-only**: all DB access goes through the existing pools created behind `assertDevHost`; the server refuses to boot outside `NODE_ENV=development`.
- **Zod for schemas**: every input validated through Zod.
- **Parameterized SQL only**: MSSQL uses `sql.Transaction` + `request.input(...)`; Postgres uses `$n` parameters. Never interpolate caller input.
- **Quoted identifiers**: Kitchendb columns are PascalCase (`"RestaurantId"`, `"KitchenTicketId"`) and MUST be double-quoted.

---

## 1. Context

During development and automated test cycles, developers clear the order pipeline frequently. The only DevMCP tool available today is `reset_databases`, which drops `OrderDb` and `DROP SCHEMA public` in `Kitchendb`.

This causes friction:

- Schema teardown breaks EF Core connection pools and forces restarts of `ordering.api` / `kitchen.api` to re-apply migrations.
- Unrelated seeds (customers, menu items, kitchen stations) are wiped.
- Teardown cannot be limited to a single restaurant tenant.

A dedicated, schema-preserving `flush_orders` tool gives a clean order slate without service disruption.

---

## 2. Goal

- Transactional deletion of order records in `OrderDb`: `order_activities`, `OrderItems`, `OrderBills`, `Orders`.
- Deletion of `kitchen_ticket_items` and `kitchen_tickets` in `Kitchendb`.
- Explicit scoping: one restaurant (`restaurantId`) or all tenants (`scope: "all"`).
- Safe outbox cleanup to avoid ghost events (undispatched rows and dead-letter rows only, unscoped flushes only).
- Optional reset of per-day order-number sequences.
- `dryRun` preview.
- Per-database structured result with row counts and errors.

---

## 3. Out of scope

- Dropping or altering schemas or EF Core migrations.
- Purging catalog items, customers, kitchen stations, or identity data.
- Clearing baskets (existing Basket tooling).
- Purging RabbitMQ queues or dead-letter queues (caveat documented in §8).
- Pushing refresh signals to SignalR clients (caveat documented in §8).

---

## 4. Tech decisions

| Decision | Choice | Reason |
| :--- | :--- | :--- |
| Tool location | `src/tools/infrastructure.ts` | Sits next to `reset_databases` as database-state lifecycle tooling. |
| Scope selection | `restaurantId` XOR `scope: "all"` (one is required) | An omitted filter must never silently wipe every tenant. |
| MSSQL strategy | One `sql.Transaction`, child-first deletes, parameterized | Atomic within `OrderDb`; `OrderBills` has no FK to `Orders`, so ordering is for hygiene, not constraints. |
| Postgres strategy | Transaction with filtered `DELETE`, or `TRUNCATE ... CASCADE` for `scope: "all"` | Keeps indices and tables; fast for full wipes. |
| Cross-DB atomicity | None; report per-database `{ ok, error, counts }` | Two engines cannot share a transaction; partial failure must be visible, never hidden. |
| Outbox cleanup | `purgeOutbox` (default `true`), only `DispatchedAt IS NULL` rows plus `outbox_messages_dead`, only when `scope: "all"` | Outbox tables have no `RestaurantId`; a scoped purge would delete other tenants' pending events. |
| Rate limit | New bucket `flush`: 10 per minute | Protects against AI loops (see README) while allowing fast test iterations. |
| Safety | `confirm: z.literal(true)` | Cheap guard; no confirm text needed because schemas are preserved. |

---

## 5. Folder layout

```text
OrderlyMicroservices/orderly-microservices/Orderly.DevMCP.Server/
├── src/
│   ├── index.ts                     # tool-count strings (lines ~128 and ~177)
│   ├── util/rate-limit.ts           # add `flush` bucket
│   └── tools/
│       └── infrastructure.ts        # flush_orders implementation
├── test/
│   └── tools/
│       ├── infrastructure.test.ts   # mocked unit tests (CI)
│       └── flush-orders.live.test.ts # opt-in, MCP_LIVE_TEST=1
└── README.md                        # tool list + counts (lines ~14, ~25, ~64, ~132)
```

---

## 6. Tool Specification

### 6.1 `flush_orders`

*   **Description**: Purges orders, items, activities, bills, and kitchen tickets without dropping schemas or restarting services.
*   **Input schema**:

```typescript
{
  restaurantId: z.string().uuid().optional()
    .describe('Restaurant UUID. Mutually exclusive with scope: "all".'),
  scope: z.literal('all').optional()
    .describe('Set to "all" to flush every tenant. Required when restaurantId is omitted.'),
  includeKitchen: z.boolean().default(true),
  purgeOutbox: z.boolean().default(true)
    .describe('Only honored when scope is "all"; ignored (and reported) for a single restaurant.'),
  resetSequences: z.boolean().default(false)
    .describe('Also clear RestaurantDailySequences so order numbers restart.'),
  dryRun: z.boolean().default(false)
    .describe('Return the counts that WOULD be deleted without deleting anything.'),
  confirm: z.literal(true),
}
```

*   **Validation**: exactly one of `restaurantId` or `scope: "all"` MUST be present; otherwise return an `isError` response.
*   **Output schema**:

```typescript
{
  scope: string;          // restaurantId or 'all'
  dryRun: boolean;
  orderDb: { ok: boolean; error?: string; counts: {
    orders: number; orderItems: number; orderActivities: number; orderBills: number;
    outboxPending: number; outboxDead: number; dailySequences: number } };
  kitchenDb: { ok: boolean; error?: string; skipped?: boolean; counts: {
    tickets: number; ticketItems: number; outboxPending: number; outboxDead: number } };
  notes: string[];        // e.g. "purgeOutbox ignored for single-restaurant scope"
}
```

*   **Execution order** (reduces the window for ghost events): outbox first, then `Orders` data, then Kitchen.
*   **MSSQL (`OrderDb`)**, inside one transaction (`@restaurantId` is NULL for `scope: "all"`):

```sql
DELETE oa FROM order_activities oa INNER JOIN Orders o ON oa.OrderId = o.Id
  WHERE (@restaurantId IS NULL OR o.RestaurantId = @restaurantId);
DELETE oi FROM OrderItems oi INNER JOIN Orders o ON oi.OrderId = o.Id
  WHERE (@restaurantId IS NULL OR o.RestaurantId = @restaurantId);
DELETE ob FROM OrderBills ob INNER JOIN Orders o ON ob.OrderId = o.Id
  WHERE (@restaurantId IS NULL OR o.RestaurantId = @restaurantId);
DELETE FROM Orders WHERE (@restaurantId IS NULL OR RestaurantId = @restaurantId);
-- scope = 'all' AND purgeOutbox only:
DELETE FROM outbox_messages WHERE DispatchedAt IS NULL;
DELETE FROM outbox_messages_dead;
-- resetSequences only:
DELETE FROM RestaurantDailySequences WHERE (@restaurantId IS NULL OR RestaurantId = @restaurantId);
```

*   **PostgreSQL (`Kitchendb`)**, when `includeKitchen` is true, in one transaction:
    - Scoped:

```sql
DELETE FROM kitchen_ticket_items WHERE "KitchenTicketId" IN
  (SELECT "Id" FROM kitchen_tickets WHERE "RestaurantId" = $1);
DELETE FROM kitchen_tickets WHERE "RestaurantId" = $1;
```

    - All tenants:

```sql
TRUNCATE TABLE kitchen_ticket_items, kitchen_tickets CASCADE;
-- purgeOutbox only:
DELETE FROM outbox_messages WHERE "DispatchedAt" IS NULL;
DELETE FROM outbox_messages_dead;
```

*   **Dry run**: replace each `DELETE` with the equivalent `SELECT COUNT(*)` using the same predicates; no writes, no rate-limit consumption beyond one token.
*   **Failure semantics**: a failure in one database rolls back that database only and is reported in its block; the other block is still returned. The overall response sets `isError: true` if any block failed.

---

## 7. Integration Points

- **MSSQL (`OrderDb`)**: pooled `deps.mssql` from `ToolContext`.
- **PostgreSQL (`Kitchendb`)**: `deps.pg.kitchen` pool. The database name is `Kitchendb` (capital K).
- **Rate limiter**: `rateLimits` in `src/util/rate-limit.ts` (new `flush` bucket).
- **DevMCP transport**: streamable HTTP at `http://localhost:8080/mcp`.

---

## 8. Security guardrails

> [!CAUTION]
> This tool permanently deletes data. It must never run outside development and must never accept caller-controlled SQL fragments.

| Risk | Mitigation |
|---|---|
| Execution outside development | `assertDevHost` in connection factories and boot refusal when `NODE_ENV !== 'development'`. |
| Accidental all-tenant wipe | `restaurantId` XOR `scope: "all"` validation plus `confirm: true`. |
| Deleting other tenants' events | Outbox purge restricted to `scope: "all"` and undispatched/dead rows. |
| SQL injection | Parameterized queries only; all identifiers are static literals. |
| AI loop abuse | `flush` token bucket, 10 per minute, fixed server-side key. |
| Partial failure hidden | Per-database result blocks and `isError` on any failure. |
| Ghost events from in-flight messages | Documented caveat: messages already in RabbitMQ can still reach consumers and recreate kitchen state. Purge queues manually if needed. |
| Stale UI | Documented caveat: connected SignalR clients keep showing deleted orders until they refresh. |

---

## 9. Development Phases

### Phase overview

| Phase | Name | Deliverables | Goal |
|:---:|---|---|---|
| **1** | Tool Implementation | `flush_orders` in `infrastructure.ts`, `flush` bucket | Core queries, validation, dry run, per-DB reporting. |
| **2** | Registration & Wiring | Tool registration, tool-count updates | Expose the tool and keep counts consistent. |
| **3** | Automated Tests | Mocked suite plus opt-in live test | Verify scoping, validation, outbox rules, failure isolation. |
| **4** | Documentation & Live Verification | README, live run | Document and prove it against the Docker stack. |

---

### Phase 1 — Tool Implementation

**Goal**: `flush_orders` works end-to-end against the dev databases with correct scoping and reporting.

**Status**: ✅ Done (2026-10-28)

**Deliverables**:

- [x] Zod schema with `restaurantId` XOR `scope: "all"` validation.
- [x] Add `flush` bucket (10 per minute) to `rateLimits`.
- [x] MSSQL transaction with parameterized, child-first deletes.
- [x] Postgres transaction with quoted PascalCase identifiers.
- [x] Outbox purge restricted to `scope: "all"`, undispatched and dead rows only.
- [x] `resetSequences` and `dryRun` support.
- [x] Per-database result blocks and `notes`.

**Exit criteria**: a `dryRun` call returns counts matching `SELECT COUNT(*)`, and a real call leaves `Orders` and `kitchen_tickets` empty for the scope.

---

### Phase 2 — Registration & Wiring

**Goal**: Tool is registered and every tool-count reference is consistent (19 → 20).

**Status**: ✅ Done (2026-10-28)

**Deliverables**:

- [x] Register `flush_orders` in `registerInfrastructureTools`.
- [x] Update `index.ts` instructions string (~line 128) and log line (~line 177).
- [x] Update README counts (~lines 14, 64, 132) and the `infrastructure` module row (~line 25).
- [x] `pnpm run typecheck` clean.
- [x] Run `phase-guard.ps1 -Quick`.

**Exit criteria**: server boots and logs `20 tools registered`.

---

### Phase 3 — Automated Tests

**Goal**: Cover every branch without requiring containers in CI.

**Status**: ✅ Done (2026-10-28)

**Deliverables**:

- [x] Mocked tests: validation rejects missing/both of `restaurantId` and `scope`.
- [x] Mocked tests: scoped flush never touches outbox tables; `purgeOutbox` is reported as ignored.
- [x] Mocked tests: `scope: "all"` purges only undispatched and dead outbox rows.
- [x] Mocked tests: `includeKitchen: false`, `resetSequences`, `dryRun` perform no writes.
- [x] Mocked tests: Postgres failure still returns the MSSQL block and `isError: true`.
- [x] Mocked tests: rate-limit exhaustion returns an error.
- [x] Live test gated on `MCP_LIVE_TEST=1` (seed mock orders, flush, assert zero).

**Exit criteria**: `pnpm test` passes; the live test passes with `MCP_LIVE_TEST=1` against the running stack.

---

### Phase 4 — Documentation & Live Verification

**Goal**: Documented and proven against the Docker stack.

**Status**: ✅ Done (2026-10-28)

**Deliverables**:

- [x] Document `flush_orders`, its inputs, and the §8 caveats in the README.
- [x] Live call via MCP client with `dryRun` then a real flush.
- [x] Plan commit marking phases done (separate from code commit).

**Exit criteria**: after a live `scope: "all"` flush, `Orders`, `kitchen_tickets`, and pending outbox rows are all zero while schemas and seeds remain.

---

## 10. Technical considerations

### 10.1 Cross-cutting

- **MSSQL delete-join syntax**: use `DELETE alias FROM table alias INNER JOIN ...` when filtering children by a parent column.
- **Postgres identifiers**: Kitchendb columns are PascalCase and need double quotes; the database name is `Kitchendb`.
- **IDs are GUIDs**: there are no identity columns to reseed, so `RESTART IDENTITY` is unnecessary.
- **Order numbers**: `RestaurantDailySequences` keeps counting after a flush unless `resetSequences` is set. Kitchen tickets are keyed by `OrderNumber`, so reset the sequence and kitchen together.
- **`Orders.RestaurantId` is `uniqueidentifier`**, which confirms `z.string().uuid()` for the input.
- **`OrderBills` has no FK to `Orders`**: deleting `Orders` alone would orphan bills, so bills are deleted explicitly by join.
