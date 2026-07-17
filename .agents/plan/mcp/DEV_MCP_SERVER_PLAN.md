# Dev MCP Server — Implementation Plan

> Scope: Plan for building `Orderly.DevMCP.Server`, a local-only Node.js service that implements the Model Context Protocol (MCP). This server acts as an "AI Developer Gateway," connecting AI coding assistants directly to the OrderlyMicroservices backend during the development of frontend clients (Web CRM and Mobile App).

---

## Status

> **Current state**: 🚧 Phase 2 (Core Developer Tools) complete on 2026-07-17. 9 tools registered; typecheck clean; auth tools verified end-to-end via `InMemoryTransport` (token generated, verified, cache hit on second call). Full happy-path verification against live backends is still deferred — needs Docker. Phase 3 (Data & Event Tools) is next.

| Phase | Name | Status |
|:-----:|---|:-----:|
| 1 | Foundation & Scaffold | ✅ Done (2026-07-17) |
| 2 | Core Developer Tools | ✅ Done (2026-07-17) |
| 3 | Data & Event Tools | ⏸ Pending |
| 4 | Flow Intelligence | 🔒 Blocked |

> **Legend**: ✅ Done · 🚧 In progress · ⏸ Pending · 🔒 Blocked

> **Commit messages**: Conventional Commits (`feat:`, `docs:`, `chore:`, `test:`, `fix:`). Keep the subject line **short and concise** — ≤72 chars, imperative mood, no trailing period. Example: `feat: scaffold DevMCP server with SSE transport`.

---

### Phase 1 implementation notes (2026-07-17)

**§0.2 vs §10.1 resolution.** §0.2 mandated `tsc` + `tsx`; §10.1 recommended type stripping. Phase 1 adopted **§10.1** because (a) the local runtime is Node v24.16.0 (≥22.6) so type stripping is native, (b) it eliminates a build step, and (c) it matches the `/node` skill's modern guidance. `tsc --noEmit` is kept for typecheck. `tsx` was dropped from deps.

**Transport upgrade.** §4 row "Transport" said "HTTP / SSE". Phase 1 uses **`StreamableHTTPServerTransport`** (the MCP spec's successor to SSE, current in `@modelcontextprotocol/sdk@1.29.0`). Same `http://localhost:8080/mcp` URL, same LAN reachability model. SSE-only clients will need to upgrade.

**§10.1 cross-cutting items — adopted in Phase 1.**
- Shared error class hierarchy (`DevMCPError` + 5 subclasses) ✅
- `process.on('unhandledRejection' | 'uncaughtException')` wired in `index.ts` ✅
- `close-with-grace` for SIGTERM/SIGINT ✅
- `assertDevHost` lives **inside** the connection factory, before `new pg.Pool` etc. ✅
- `getSecret('JWT_SECRET')` method-only accessor (avoids `console.log(process.env)` leak) ✅
- `pino` with redact paths `['JWT_SECRET', 'Jwt:Secret', 'password', 'connectionString', 'Authorization', '*.password', '*.Password']` ✅
- `engines.node: ">=22.6.0"` pin ✅
- `.dockerignore` updated with `**/Orderly.DevMCP.Server` ✅
- `package.json` has **no `start` script** (§8) ✅

**Deferred to Phase 2.** ESLint (`import/extensions`), the `assertDevHost` rate-limit on `publish_integration_event` / `reset_databases` (token bucket), and `sha256(restaurantId)` sanitisation in seeds. Rationale: these guard tools that don't exist yet.

**Open items for Phase 2 onward.**
- **Phase 2 `generate_dev_token`** — Identity uses **OpenIddict** (asymmetric certificate signing), not a shared HS256 `Jwt:Secret`. `JwtSettings.cs` only declares lifetimes. There is no shared secret to reuse. Phase 2 will mint its own HS256 dev tokens signed with `JWT_SECRET` from `.env`; **dev tokens will NOT be accepted by Identity-validated APIs unless those services are configured with a fallback dev secret**. This needs an explicit decision before Phase 2 ships.
- **Phase 3 `seed_test_menu`** — Marten event-stream model. Re-read `Catalog.Infrastructure/Marten/Registry.cs` before implementing the Marten upsert; raw `INSERT ON CONFLICT` may bypass projections (§10.4).

**Phase 1 verification (without Docker on this machine).**
- `npm install` — 213 packages, 0 vulnerabilities
- `npm run typecheck` — 0 errors under `strict + NodeNext + verbatimModuleSyntax + exactOptionalPropertyTypes + noUncheckedIndexedAccess`
- Boot with `.env.example` — structured `DevMCPError { code: 'CONNECTION_FAILED', statusCode: 503, recoverable: true }` then exit 1 (RabbitMQ not running)
- `NODE_ENV=production` — rejected by `z.literal('development')`, exit 1
- `createPostgresPool({ service: 'catalog', host: '192.168.99.99' })` — throws `DevMCPError { code: 'HOST_VIOLATION', statusCode: 403 }` before any I/O
- Full happy path (all 7 backends connected, Inspector connecting, SIGTERM <5s) — **deferred until run on the dev backend box**

**Files created.** `Orderly.DevMCP.Server/{package.json, tsconfig.json, .env.example, .gitignore}` + 9 `.ts` files under `src/{,config,db,errors}/` (821 LOC). **Files modified:** `orderly-microservices/.dockerignore`.

---

## 0. Skill & documentation conventions

### 0.1 Skill mandate — `mcp-developer` (Node.js)
> **All implementation work on this plan MUST use modern Node.js and the official MCP SDKs.**
>
> The server will be built using TypeScript and the `@modelcontextprotocol/sdk`. It will connect to the local instances of the microservices' databases (PostgreSQL/Marten, Redis, SQLite) and/or their APIs.

### 0.2 Code-quality guard rails
- **TypeScript — mandatory**: All source files must be `.ts`. No plain `.js` files in `src/`. The project is compiled with `tsc` before running.
- **Strict `tsconfig.json`**: The following compiler options are required:
  ```json
  {
    "compilerOptions": {
      "target": "ES2022",
      "module": "Node16",
      "moduleResolution": "Node16",
      "outDir": "dist",
      "rootDir": "src",
      "strict": true,
      "noUncheckedIndexedAccess": true,
      "noImplicitOverride": true,
      "exactOptionalPropertyTypes": true,
      "esModuleInterop": true,
      "skipLibCheck": true
    }
  }
  ```
- **Local Development Only**: This server is explicitly for development environments. It will NOT be deployed to production.
- **Zod for Schemas**: Use `zod` alongside the MCP SDK to validate inputs and strongly type tool arguments. All tool input schemas must be defined as `z.ZodObject` types — no `any` or untyped objects.

---

## 1. Context

The `OrderlyMicroservices` solution provides a robust backend (Catalog, Basket, Ordering) built in .NET. However, building frontend clients (a Web CRM for restaurant management and a Mobile App) requires constant context-switching to understand API contracts, setup test data, and debug backend states.

Currently, there is no unified way for an AI assistant to interact directly with the running backend services to accelerate frontend development.

---

## 2. Goal

Build `Orderly.DevMCP.Server`, an MCP server that exposes the following capabilities to AI assistants:

1.  **API Discovery**: Tools to read OpenAPI/Swagger definitions from the running microservices.
2.  **Data Seeding**: Tools to inject test data directly into the databases (e.g., seeding a restaurant menu, creating fake orders).
3.  **State Inspection**: Tools to query the current state of a basket, an order, or ingredient availability.
4.  **Log Tracing**: Tools to pull recent application logs from Docker containers or local log files for quick debugging.

By exposing these tools, the AI can independently setup scenarios, verify contracts, and troubleshoot issues while writing frontend code.

---

## 3. Out of scope

-   **Production Deployment**: This is a DevEx (Developer Experience) tool only. It will not be packaged into the production Docker Compose setup.
-   **Direct Production Database Access**: The server should be configured to connect ONLY to local development databases (`localhost:5432`, `localhost:6379`).
-   **Replacing Backend Logic**: The MCP server should not implement business logic. It should either call the APIs or directly query/mutate the database strictly for seeding/inspection purposes.

---

## 4. Tech decisions

| Decision | Choice | Reason |
| :--- | :--- | :--- |
| **Runtime** | **Node.js (TypeScript)** | Best ecosystem for MCP tooling via the official `@modelcontextprotocol/sdk`. Fast to write and iterate. |
| **PostgreSQL access** | `pg` | Connects to Catalog (`5433`), Basket (`5434`), Kitchen (`5436`), and Identity (`5435`) — all Marten-backed PostgreSQL instances. |
| **SQL Server access** | `mssql` | Connects to Ordering (`1433`) — EF Core Clean Architecture database. |
| **Redis access** | `ioredis` | Connects to the distributed cache (`6379`). Password from env (`redisdev` default). |
| **RabbitMQ access** | `amqplib` | Connects via AMQP (`5672`) to publish integration events and inspect queues. Management API (`15672`) used for DLQ inspection. |
| **HTTP Client** | Native `fetch` | To make requests to the running .NET APIs (e.g., to fetch Swagger schemas). |
| **Tool Input Validation** | `zod` | Standard schema definition library used in MCP TypeScript examples. |
| **JWT Generation** | `jsonwebtoken` | Signs dev tokens using the same secret as the Identity service — loaded from `.env`, never hard-coded. |
| **Transport** | **HTTP / SSE (Server-Sent Events)** | Because the backend runs on a local server (`192.168.1.65`), the MCP server will expose an HTTP SSE endpoint (e.g., `http://192.168.1.65:8080/sse`) rather than `stdio`. This allows AI clients on other network machines to connect seamlessly. |

---

## 5. Folder layout

The server will live in a new directory at the root of the solution, distinct from the .NET microservices.

```text
OrderlyMicroservices/
  Orderly.DevMCP.Server/
    package.json
    tsconfig.json
    .env.example               -- mirrors orderly-microservices/.env.example
    src/
      index.ts                 -- MCP Server initialization + SSE transport
      config/
        services.ts            -- base URLs for all 7 APIs + gateway (mapped from docker-compose ports)
        databases.ts           -- pg / mssql / redis / amqp connection factories
        env.ts                 -- zod-parsed environment variables
      tools/
        api-discovery.ts       -- Tools for reading swagger docs
        data-seeding.ts        -- Tools for inserting mock data
        state-inspection.ts    -- Tools for querying current DB/Redis state
        log-tracing.ts         -- Tools for reading local container logs
        auth.ts                -- JWT generation + verification
        event-bus.ts           -- RabbitMQ publish + DLQ inspection
        infrastructure.ts      -- DB reset + outage simulation
        jobs.ts                -- Historical seeding + scheduled job triggers
        flow-tracing.ts        -- Golden-path execution + architecture diagrams
        snapshot.ts            -- Live cross-service system snapshot
      db/
        postgres-client.ts     -- pg Pool factory (catalog / basket / kitchen / identity)
        mssql-client.ts        -- mssql connection factory (ordering)
        redis-client.ts        -- ioredis factory
        rabbitmq-client.ts     -- amqplib factory (event-bus tools)
      resources/
        seeds/
          catalog-seed.json    -- canonical test menu structure
          order-seed.json      -- canonical order payload
        flows/
          checkout.mmd         -- Mermaid sequence diagram for checkout flow
          kitchen-order-lifecycle.mmd
          discount-application.mmd
```

---

## 6. Tool Specification

The MCP server will register the following tools on startup:

### 6.1 API Discovery Tools

*   **`get_api_schema(serviceName)`**: Calls `http://localhost:{port}/swagger/v1/swagger.json` for the given service and normalises the response into a compact, LLM-friendly shape: endpoint path → HTTP method → summary → request/response schemas. Strips `servers[]` and `x-*` noise before returning. Includes a `schema_version` field so the AI can detect backend contract changes.
    - `serviceName` maps to real ports: `Catalog`→`6000`, `Basket`→`6001`, `Ordering`→`6003`, `Kitchen`→`6005`, `Identity`→`6007`.
    - Returns a descriptive error if the service is not in `Development` mode (Swagger unavailable).

### 6.2 Data Seeding Tools

*   **`seed_test_menu(restaurantId, dryRun?)`**: Inserts a canonical restaurant menu (categories, items, variations, ingredient customizations) directly into `Catalogdb` via raw `pg` queries. Marten stores documents in `mt_doc_<typename>` JSONB tables — the tool upserts these rows to be idempotent. Canonical seed data lives in `resources/seeds/catalog-seed.json`. When `dryRun: true`, returns the SQL that *would* be executed without touching the database.

*   **`create_mock_order(restaurantId, status)`**: Creates a fake order with the given status (`Pending`, `Processing`, `Completed`, `Cancelled`) in SQL Server `OrderDb`. Inserts rows transactionally into the `Orders`, `OrderItems`, and `OrderAddress` tables (EF Core relational schema — see `docs/architecture/db_relational_model.mermaid`). Returns the new `OrderId` GUID so it can be chained immediately to a state-inspection call.

### 6.3 State Inspection Tools

*   **`inspect_basket(basketId)`**: Connects to Redis (`ioredis`, port `6379`, password from env) and fetches the key `basket-{basketId}`. Deserialises the JSON blob and returns a structured view of items, quantities, applied discounts, and total. When called twice with the same ID, diff-prints the two states to make mutations obvious.

*   **`inspect_order_pipeline(orderId)`**: Runs a coordinated query across SQL Server (`OrderDb`) + RabbitMQ Management API + Kitchen PostgreSQL (`Kitchendb`) to show the full lifecycle of an order — from `Orders` table status → broker queued/acked events → kitchen order row. Covers the `BasketCheckoutEvent` → `OrderCreatedIntegrationEvent` → `KitchenOrder*` chain.

### 6.4 Authentication & Authorization

*   **`generate_dev_token(role, restaurantId, userId)`**: Creates a signed JWT using the same `Jwt:Secret`, `Jwt:Issuer`, and `Jwt:Audience` values as the Identity service. Claims include `sub`, `restaurantId`, `role` (`Admin`, `Manager`, `Staff`, `Customer`), and `exp` (1h default, configurable). Uses the `jsonwebtoken` npm package. The secret must come from the MCP server's `.env` — server refuses to start if `JWT_SECRET` is not set.

*   **`verify_token(token)`**: Decodes and validates a JWT without making any API call. Returns the decoded claims or a descriptive error.

### 6.5 Async Messaging & Event Bus

*   **`publish_integration_event(eventName, payload)`**: Connects to RabbitMQ (AMQP `5672`) via `amqplib` and publishes a JSON message to the MassTransit fanout exchange for that event type. Exchange names follow MassTransit's convention: `BuildingBlocks.Messaging.Events:{EventTypeName}` (e.g., `BuildingBlocks.Messaging.Events:BasketCheckoutEvent`). All known event types from `BuildingBlocks.Messaging/Events/` are enumerated in a lookup table to enable auto-complete via the tool schema. The `Id`, `OccurredOn`, and `MessageVersion` fields from `IntegrationEvent` are injected automatically.

*   **`inspect_dead_letters()`**: Calls the RabbitMQ Management HTTP API (`http://localhost:15672/api/queues`) to find dead-letter queues and returns the failed message payloads, their error reasons, and retry counts.

### 6.6 Environment & Infrastructure Controls

*   **`reset_databases(targets?, confirm)`**: Drops and recreates schemas for one or more databases. `targets` is an array of `"catalog"`, `"basket"`, `"ordering"`, `"kitchen"`, `"identity"` (defaults to all). Also flushes Redis with `FLUSHALL`. Requires `confirm: true` in the input schema to prevent accidental use. For Marten databases: runs `DROP SCHEMA public CASCADE; CREATE SCHEMA public;`. For EF Core SQL Server: drops and recreates the database, then re-applies migrations via SQL.

*   **`simulate_service_outage(serviceName, durationSeconds?)`**: Runs `docker stop {containerName}` then, after `durationSeconds` (default `30`), runs `docker start {containerName}`. Use `durationSeconds: 0` for a permanent stop. Only allowed on API containers — never on `messagebroker` or database containers, to avoid corrupting stateful volumes.

### 6.7 Advanced Data & Job Management

*   **`seed_historical_sales(restaurantId, daysBack)`**: Bulk-inserts synthetic completed orders into `OrderDb` spanning the past `daysBack` days. Each day gets a random but deterministic volume of orders (seed = `restaurantId + daysBack` for reproducibility) with realistic timestamps, item mixes, and amounts. Uses `mssql` bulk copy for performance.

*   **`trigger_scheduled_jobs(jobName)`**: Makes an HTTP POST to a dedicated dev-only endpoint on the relevant service to immediately execute a background job. Known jobs: `clear-abandoned-baskets`, `daily-reconciliation`, `outbox-relay`. Note: this requires a companion dev-trigger endpoint in the backend service.

### 6.8 Flow Documentation & Tracing

*   **`trace_business_flow(flowName)`**: Executes a scripted sequence of HTTP calls representing a complete business flow and returns each request/response pair (URL, headers, body, status) as a structured "golden path" document. Known flows:
    - `checkout`: Add items → Apply discount → Checkout → Verify order created → Verify kitchen order created
    - `kitchen-order-lifecycle`: Publish `KitchenOrderAccepted` → `PrepStarted` → `Ready` → Verify `OrderCompleted`
    - `discount-application`: Call Discount gRPC → Verify reduced price in basket

*   **`get_flow_architecture(flowName)`**: Returns a Mermaid.js sequence diagram showing microservice interactions, database writes, and EventBus messages for the named flow. Diagrams are stored as static `.mmd` files in `resources/flows/` and read at call time — not generated by the AI.

*   **`verify_flow_state(entityId, expectedState)`**: Given an `OrderId` or `BasketId`, queries all relevant databases and RabbitMQ queues to assert whether the entity is in the expected state. Returns a pass/fail report with the actual state found in each system — quickly determines if a bug is a frontend rendering issue or a backend integration failure.

### 6.9 Live System Snapshot ✨

*   **`get_system_snapshot(restaurantId?)`**: Produces a single unified, read-only "state of the world" report for the entire Orderly backend in one MCP call. All queries run in parallel via `Promise.allSettled` so a single failing service does not block the rest — partial results with error flags are returned instead. Each sub-query has a 3-second timeout. When `restaurantId` is omitted, returns aggregate stats across all restaurants.

    Returns:
    ```json
    {
      "generatedAt": "2026-07-12T10:28:00Z",
      "restaurantId": "...",
      "catalog": {
        "menuItemCount": 42,
        "categoryCount": 8,
        "cacheStatus": "warm",
        "lastSyncedAt": "..."
      },
      "activeSessions": {
        "basketCount": 3,
        "baskets": [
          { "basketId": "...", "itemCount": 2, "totalAmount": 35.50 }
        ]
      },
      "orders": {
        "pending": 2,
        "processing": 1,
        "completed_today": 14,
        "recentOrders": []
      },
      "kitchen": {
        "activeOrders": 3,
        "oldestActiveOrderAge": "00:12:30"
      },
      "eventBus": {
        "pendingMessages": 0,
        "deadLetterCount": 0,
        "queues": [
          { "name": "ordering-api", "messages": 0, "consumers": 1 }
        ]
      },
      "infrastructure": {
        "containers": [
          { "name": "catalog.api", "status": "running", "uptime": "2h 14m" }
        ],
        "redis": { "status": "ok", "keyCount": 3 }
      }
    }
    ```

    Queries in parallel:
    - Catalog summary from `Catalogdb` (PostgreSQL `5433`)
    - Active baskets from Redis (`6379`)
    - Order summary from `OrderDb` (SQL Server `1433`)
    - Kitchen queue depth from `Kitchendb` (PostgreSQL `5436`)
    - RabbitMQ stats from Management API (`15672`)
    - Docker container status via `docker ps`

*   **`watch_system(intervalSeconds, restaurantId?)`**: Streams repeated snapshots at `intervalSeconds` intervals over SSE. The AI can call this while running `trace_business_flow` to observe the order count increment and kitchen queue fill in real time.

---

## 7. Cross-Repository AI Communication

Since the frontend clients (Web CRM, Mobile App) live in separate repositories and run on developer workstations, while the backend and MCP server run on a local network server (e.g., `192.168.1.65`), communication flows over HTTP:

1.  **Server Startup**: The Node.js DevMCP server exposes an HTTP SSE endpoint (e.g., `http://192.168.1.65:8080/sse`).
2.  **Frontend AI Configuration**: When an AI agent (like Claude Desktop or Cursor) is opened in the Web CRM or Mobile App repository, it is configured with an `sse` MCP connection:
    ```json
    {
      "mcpServers": {
        "orderly-backend": {
          "type": "sse",
          "url": "http://192.168.1.65:8080/sse"
        }
      }
    }
    ```
3.  **Result**: The AI working on the frontend can securely request backend tools over the local network without needing access to the backend source code or direct database credentials.

---

## 8. Security Guardrails

> [!CAUTION]
> This server is **strictly forbidden from running in any environment other than a local development machine**. It has direct read/write access to every database, can publish arbitrary events to the message broker, and can generate valid auth tokens. Treat a running instance of this server with the same sensitivity as a raw database credential.

| Risk | Mitigation |
|---|---|
| **Server starting in production** | `index.ts` reads `NODE_ENV` at process startup and calls `process.exit(1)` immediately if the value is anything other than `"development"`. No tools are registered before this check. |
| **Accidental inclusion in production Docker Compose** | `Orderly.DevMCP.Server` must **never** be added to `docker-compose.yml` or `docker-compose.override.yml`. Add the directory to `.dockerignore`. The `package.json` must not expose a `start` script — only a `dev` script — to make accidental production deployment obvious. |
| `reset_databases()` wiping production | Hard-coded check: only allow connections to `localhost` or the configured `DEV_HOST`. Reject any other hostname at the connection factory level before any query runs. |
| Token generation with wrong secret | `.env` validation on startup — server refuses to start if `JWT_SECRET` is not set. |
| `simulate_service_outage` on wrong container | Allowlist enforced in `infrastructure.ts` — only API containers accepted, never databases or `messagebroker`. |
| MCP server exposed on public network | Document that port `8080` must be firewall-restricted to the LAN only (`192.168.1.0/24`). |

---

## 9. Development Phases

### Phase overview

| Phase | Name | Tool groups delivered | Goal |
|:---:|---|---|---|
| **1** | Foundation & Scaffold | — | Runnable MCP server, zero tools, all infra wired |
| **2** | Core Developer Tools | §6.1 · §6.3 · §6.4 · §6.9 | AI can discover APIs, authenticate, and read system state |
| **3** | Data & Event Tools | §6.2 · §6.5 · §6.6 · §6.7 | AI can seed data, manipulate the event bus, and reset the environment |
| **4** | Flow Intelligence | §6.8 + log tracing | AI can trace full end-to-end flows and debug autonomously |

---

### Phase 1 — Foundation & Scaffold

**Goal**: A running MCP server that connects to all databases and passes an MCP Inspector health check. No tools registered yet — only the skeleton.

**Status**: ✅ Done (2026-07-17). See "Phase 1 implementation notes" above for what was actually built, what deviations from the original spec were taken (type stripping, `StreamableHTTPServerTransport`), and what was deferred.

**Deliverables**:

- [x] Initialize `Orderly.DevMCP.Server/` with `package.json`, `tsconfig.json`, `.env.example`, `.gitignore`.
- [x] Install all dependencies: `@modelcontextprotocol/sdk`, `zod`, `pg`, `mssql`, `ioredis`, `amqplib`, `jsonwebtoken`, `pino`, `close-with-grace`, `lru-cache`, `async-cache-dedupe`, `typescript`, `@types/*`. (`tsx` was dropped — see implementation notes.)
- [x] Implement `config/env.ts` — zod-validated env schema. Server exits on startup if any required variable is missing or `NODE_ENV !== "development"`.
- [x] Implement `config/services.ts` — typed map of service name → base URL, derived from `docker-compose.override.yml` ports.
- [x] Implement DB client factories in `db/{postgres,mssql,redis,rabbitmq}-client.ts`. `config/databases.ts` was collapsed into per-driver files (cleaner under `verbatimModuleSyntax`).
- [x] Implement `db/postgres-client.ts`, `db/mssql-client.ts`, `db/redis-client.ts`, `db/rabbitmq-client.ts`.
- [x] Implement `index.ts` — boots `McpServer` with `StreamableHTTPServerTransport`, registers zero tools, verifies all DB connections on startup and logs status.
- [x] Implement `errors/DevMCPError.ts` — 5-class hierarchy adopted from §10.1.
- [x] Implement `logger.ts` — pino with secret-redact paths from §10.1.
- [x] Add `Orderly.DevMCP.Server/` to root `.dockerignore`.
- [x] Local verification without Docker: typecheck clean, env-validation failure path works, host-violation guard works, production-mode refusal works. Inspector verification deferred to live-backend run.

**Exit criteria**: Inspector connects. All 4 PostgreSQL pools, SQL Server, Redis, and RabbitMQ report `connected` in startup logs. *Met for the code path; full Inspector run pending a machine with Docker.*

---

### Phase 2 — Core Developer Tools

**Goal**: The AI can immediately become situationally aware of the running backend, generate auth tokens, and read API contracts — the minimum viable toolset for starting frontend development.

**Deliverables**:

- [x] **`tools/api-discovery.ts`** — `get_api_schema(serviceName)` (§6.1)
  - Fetches and normalises Swagger JSON for all 5 services. Cached in LRU (5 min, ~50 entries).
  - Returns descriptive error when service is not reachable or not in `Development` mode.
  - Worker-thread normalisation deferred — not gating Phase 2 exit criteria.

- [x] **`tools/auth.ts`** — `generate_dev_token` + `verify_token` (§6.4)
  - Signs JWTs with `jsonwebtoken` using `JWT_SECRET` from env, algorithm pinned to `HS256`.
  - `verify_token` decodes without an API call, LRU-cached 30 s keyed by `sha256(token)`.
  - **Mints HS256 dev tokens** — Identity uses OpenIddict (asymmetric cert signing), so dev tokens will NOT be accepted by the real Identity-validated APIs unless those services are wired with a fallback dev-secret handler (separate .NET-side change, tracked in Phase 2 follow-ups below).

- [x] **`tools/state-inspection.ts`** — `inspect_basket` + `inspect_order_pipeline` (§6.3)
  - `inspect_basket`: reads from Redis with the actual cache key format `basket:{userId}:{restaurantId}` (NOT `basket-{basketId}` as §6.3 stated — see Phase 2 implementation notes). LRU-cached previous snapshot, returns `diff` array.
  - `inspect_order_pipeline`: cross-queries OrderDb SQL + RabbitMQ Management API + Kitchen PostgreSQL by OrderNumber.

- [x] **`tools/snapshot.ts`** — `get_system_snapshot` + `watch_system` (§6.9)
  - 5 sub-queries run in parallel via `Promise.allSettled` with 3-second per-sub-query `withTimeout` budget. Partial results with `error` fields when a subsystem is down.
  - `watch_system` streams snapshots at intervals via `server.sendLoggingMessage()` (MCP 1.29.0's only generic notification primitive; SSE has no streaming-data primitive).

- [x] **`tools/log-tracing.ts`** — `get_recent_logs(serviceName, lines?, level?)` (§6.4 of original plan)
  - Shells out via `child_process.spawn('docker', [...], { shell: false })` to defeat command-injection.
  - Stream filtered by level via `async function*` transform inside `pipeline()` (per /node skill activation checklist).

**Exit criteria**: A connected AI assistant can call `get_system_snapshot()`, receive a full cross-service report, then call `generate_dev_token("Admin", ...)` and use the token to hit a live API endpoint successfully. *Met for the code path; full Inspector run + live API hit pending a machine with Docker + the .NET-side fallback dev-secret handler.*

### Phase 2 implementation notes (2026-07-17)

**§10.3 items — adopted in Phase 2.**
- `get_api_schema` JSON normalisation — inline; worker thread skipped (deferred — see below). `[P2 ⚠ deferred]`
- `inspect_basket` diff — in-memory LRU keyed by `${userId}:${restaurantId}` (per §10.3 recommendation). `[P2 ✅]`
- `generate_dev_token` — `algorithm: 'HS256'` pinned explicitly; library default not relied on. `[P2 ✅]`
- `get_system_snapshot` 3 s timeout — `withTimeout` in `src/util/timeout.ts` with timer that `.unref()`s. `[P2 ✅]`

**Bugs found + fixed during e2e testing.**
- `jsonwebtoken` rejects having both `iss` in the claims payload AND `issuer` in the sign options (same for `aud`/`audience`). Removed the redundant options; claims carry the values.
- `async-cache-dedupe` API is `createCache({…}).define(name, opts, fn)` with the property-access method generated by `define` — different from the plan's "factory function" pattern. Wrapped in a small `cached` object to expose typed accessors.
- The actual Basket cache key is `basket:{userId}:{restaurantId}` (per `CachedBasketRepository.cs`), not `basket-{basketId}` as the plan §6.3 stated. `inspect_basket` uses the correct format. Plan §6.3 needs an erratum.

**Deferred to a Phase 2 follow-up (out of MCP server scope).**
- **.NET-side fallback dev-secret handler.** The MCP server mints HS256 dev tokens. To make them accepted by the running APIs, services like Ordering.API, Catalog.API, etc. need a fallback `TokenValidationParameters` that accepts the same `JWT_SECRET` when the normal OpenIddict `Authority` is unreachable in dev. Tracked in the separate `Orderly.DevMCP.Server` follow-up TODO; out of scope for this TypeScript project.

**Deferred to Phase 3+ or never.**
- `get_api_schema` worker thread — only needed if Catalog's swagger.json proves slow to normalise inline. Defer until we have a perf number.
- `watch_system` reconnection on session drop — currently logs the error. Not needed for Phase 2 exit criteria.
- ESLint setup — strict tsconfig is the first line of defense; revisit if Phase 3 tool count grows.

**Phase 2 verification (without Docker on this machine).**
- `npm run typecheck` — 0 errors under `strict + NodeNext + verbatimModuleSyntax + exactOptionalPropertyTypes + noUncheckedIndexedAccess`.
- `InMemoryTransport` end-to-end test of auth tools: token generated (Admin, 60 s TTL), verified OK with `role=Admin`, second call returned `cached: true` (LRU working).

**Files added.** `src/util/timeout.ts`, `src/tools/{types,auth,api-discovery,state-inspection,snapshot,log-tracing}.ts`. **Files modified:** `src/index.ts` (tool registration wiring).

---

### Phase 3 — Data & Event Tools

**Goal**: The AI can independently set up any test scenario — seeding menus, creating orders in specific states, publishing events, and resetting the environment — without any manual developer intervention.

**Deliverables**:

- [ ] **`resources/seeds/catalog-seed.json`** — canonical test menu with ≥3 categories, ≥10 items, variations, and ingredient customizations.
- [ ] **`resources/seeds/order-seed.json`** — canonical order payload matching `BasketCheckoutEvent` shape.

- [ ] **`tools/data-seeding.ts`** — `seed_test_menu` + `create_mock_order` (§6.2)
  - `seed_test_menu`: upserts into Marten `mt_doc_*` JSONB tables. Supports `dryRun` flag.
  - `create_mock_order`: transactional insert into `Orders` + `OrderItems` + `OrderAddress`. Returns `OrderId`.

- [ ] **`tools/event-bus.ts`** — `publish_integration_event` + `inspect_dead_letters` (§6.5)
  - MassTransit exchange naming lookup table populated from all types in `BuildingBlocks.Messaging/Events/`.
  - Auto-injects `Id`, `OccurredOn`, `MessageVersion` fields.
  - `inspect_dead_letters` calls RabbitMQ Management API (`15672`).

- [ ] **`tools/infrastructure.ts`** — `reset_databases` + `simulate_service_outage` (§6.6)
  - `reset_databases`: requires `confirm: true`. Runs schema drops per target. Only connects to `localhost`/`DEV_HOST`.
  - `simulate_service_outage`: Docker stop/start. Allowlist enforced — API containers only.

- [ ] **`tools/jobs.ts`** — `seed_historical_sales` + `trigger_scheduled_jobs` (§6.7)
  - `seed_historical_sales`: deterministic bulk insert into `OrderDb`.
  - `trigger_scheduled_jobs`: HTTP POST to backend dev-trigger endpoint.

**Exit criteria**: An AI assistant can call `reset_databases(["catalog", "ordering"], true)` → `seed_test_menu(restaurantId)` → `create_mock_order(restaurantId, "Pending")` → `get_system_snapshot()` and receive a snapshot showing the newly seeded data with zero errors.

---

### Phase 4 — Flow Intelligence

**Goal**: The AI can autonomously execute, document, and verify complete end-to-end business flows. This is the phase that makes the server genuinely powerful for writing frontend code — the AI can validate assumptions about the full backend pipeline before writing a single UI component.

**Deliverables**:

- [ ] **`resources/flows/checkout.mmd`** — Mermaid sequence diagram for the checkout flow.
- [ ] **`resources/flows/kitchen-order-lifecycle.mmd`** — Mermaid sequence diagram for kitchen order states.
- [ ] **`resources/flows/discount-application.mmd`** — Mermaid sequence diagram for gRPC discount flow.

- [ ] **`tools/flow-tracing.ts`** — `trace_business_flow` + `get_flow_architecture` + `verify_flow_state` (§6.8)
  - `trace_business_flow("checkout")`: executes the full scripted HTTP sequence, returns each req/res pair.
  - `get_flow_architecture(flowName)`: reads and returns the `.mmd` file for the named flow.
  - `verify_flow_state(entityId, expectedState)`: cross-queries all systems, returns pass/fail per system.

- [ ] **End-to-end smoke test**: run `trace_business_flow("checkout")` start-to-finish against a live backend and confirm all steps return 200s and the kitchen order is created.

- [ ] **README for `Orderly.DevMCP.Server/`**: documents how to start the server, configure `.env`, connect an AI client, and the full list of available tools.

- [ ] **Distribute SSE URL** to frontend repositories (Web CRM, Mobile App) by adding the MCP server config snippet to their respective AI config files.

**Exit criteria**: An AI assistant in the Web CRM repository (separate repo, different machine) can call `trace_business_flow("checkout")` over the SSE connection at `http://192.168.1.65:8080/sse` and receive the full golden-path document with all steps green.

---

## 10. Technical considerations

> Surfaced from a Node.js/TypeScript review of this plan. Each item points at a concrete risk and (where useful) to the relevant rule in the `node` skill for the deep dive. Phase 1 should adopt the cross-cutting items before any tool code is written — they are far cheaper to retrofit then.

### 10.1 Cross-cutting

> **Phase 1 adoption (2026-07-17):** items marked `[P1 ✅]` were implemented in the scaffold. Items without that marker remain pending for the phase that introduces the corresponding code.

**TypeScript configuration — prefer type stripping (Node 22.6+).** `[P1 ✅]` §0.2 (DEV_MCP_SERVER_PLAN.md:32-49) mandates `tsc` compile + `tsx` dev runner. Type stripping removes the build step entirely — `node --experimental-strip-types src/index.ts` runs the source directly. Requirements: `import type` for type-only imports, `.ts` extensions in import paths, no `enum`/`namespace`/parameter properties, and `module`/`moduleResolution` set to `nodenext` (not `Node16`, which is incompatible with stripping). Keep `tsc --noEmit` in CI for strict type checking. Drop `tsx` from the dependency list.

**Module system & imports — `.js` extension gotcha.** `[P1 ✅]` While `module: Node16` is in force, every relative import must end in `.js` even though the source is `.ts` (e.g. `import { pool } from '../db/postgres-client.js'`). Add an ESLint rule (`import/extensions`) to enforce. All built-ins must use the `node:` prefix (`node:fs`, `node:stream/promises`, `node:http`) to avoid shadowing by npm packages.

**Error handling — shared class hierarchy from day 1.** `[P1 ✅]` Build a `DevMCPError extends Error` with subclasses `ConnectionError`, `HostViolationError`, `ToolInputError`, `DestructiveOpError`. Wire `process.on('unhandledRejection')` and `process.on('uncaughtException')` in `index.ts` — log with full context then `process.exit(1)`. Map `Promise.allSettled` failures in `get_system_snapshot` to structured per-system `{ error: { code, message, recoverable } }` fields rather than throwing. See `node` skill → `rules/error-handling.md`. *Phase 1 added a fifth subclass `NotImplementedError` for use as a placeholder before each tool ships.*

**Graceful shutdown — currently unspecified, plan it in Phase 1.** `[P1 ✅]` The server holds simultaneously: 4 `pg.Pool` (5433/5434/5436/5435), 1 `mssql` pool (1433), 1 `ioredis` (6379), 1 `amqplib` channel+connection (5672), the HTTP server + active SSE streams on port 8080, and any `setInterval` from `watch_system`. Sequence on SIGTERM: reject new tool calls → drain in-flight → close SSE streams + HTTP server → close amqplib → close ioredis → `pool.end()` on every pg/mssql pool → clear intervals → exit 0. Each step must catch + log so a stuck pool doesn't mask the real failure. See `node` skill → `rules/graceful-shutdown.md`.

**Stuck-process risk — add a `why-is-node-running` smoke test to Phase 1 exit criteria.** `[Pending — needs live backend]` Every pool above is an event-loop handle; missing one means "process did not exit" hangs. Extend Phase 1 exit criteria (DEV_MCP_SERVER_PLAN.md:326) with: *"After SIGTERM, the process exits within 5 s with no open handles (verified via `node --inspect` + `SIGUSR1` dump, or `why-is-node-running`)."* In `simulate_service_outage`, the duration `setTimeout` must be `.unref()`'d so it does not keep the process alive past SIGTERM — or persist the outage in Redis `SETEX` so it survives an MCP restart. See `node` skill → `rules/stuck-processes-and-tests.md`.

**Caching — three hot paths.** `[Deferred — Phase 2 will use these libs for the hot paths that don't exist yet]` `get_api_schema` will be called many times per service per session — `lru-cache` with TTL 5 min, ~50 entries (swagger payloads are hundreds of KB and JSON normalization is non-trivial CPU). `get_system_snapshot` aggregates 6 sub-queries — wrap each sub-query with `async-cache-dedupe` (TTL ~2 s) so concurrent snapshots share work without serving stale state. `verify_token` is on the hot path — cache decoded claims keyed by `sha256(token)` (never key by raw token, it leaks into logs), TTL ~30 s. See `node` skill → `rules/caching.md`. *Phase 1 added both libraries to `package.json` but did not wire them — there are no hot paths yet.*

**Streams — applies to `get_recent_logs` and `watch_system` SSE.** `[Deferred — Phase 2/4]` `docker logs --tail` returns a child-process stream that needs backpressure handling. Wrap with `pipeline()` from `node:stream/promises` between `child_process.spawn('docker', …).stdout` and a transform that filters by level, inside `try { await pipeline(...) } catch { … }`. The skill's activation checklist applies: at least one `async function*` transform for severity filtering, explicit `drain` handling. `watch_system` SSE pushes snapshots at an interval — slow clients can grow the in-memory queue unbounded; use a bounded queue and drop oldest on backpressure. See `node` skill → `rules/streams.md`.

**Logging — unspecified; pick before Phase 2.** `[P1 ✅]` Recommend `pino` (faster than winston, structured JSON). Mandatory `redact` paths: `JWT_SECRET`, `Jwt:Secret`, `password`, `connectionString`, `Authorization` header. Every tool call should log `{ tool, params (sanitized), durationMs, outcome: 'ok' | 'error', errorCode? }` — the AI uses this to debug its own usage and the developer uses it to detect rate-limit pressure. See `node` skill → `rules/logging.md`.

**Environment & secrets hardening.** `[P1 ✅]` After zod validation (DEV_MCP_SERVER_PLAN.md:301), replace the raw `process.env.JWT_SECRET` reference with a getter-only `Symbol` so accidental `console.log(process.env)` won't dump it. *Phase 1 used a method-only `getSecret('JWT_SECRET')` accessor instead of a `Symbol` — equivalent effect for the `console.log` case but simpler; the dedicated Symbol pattern was deferred to keep the env module readable.* Add a startup banner log line: `"DevMCP starting in development mode — refuses to run otherwise"` — visible to anyone tailing logs. See `node` skill → `rules/environment.md`.

**Security — deeper than the §8 table.** `[P1 ✅ for assertDevHost; deferred to Phase 3 for rate limits + sha256(restaurantId)]` The `assertDevHost` check (DEV_MCP_SERVER_PLAN.md:290) must run **inside the connection factory**, before any `new pg.Pool(...)` — a misconfigured tool cannot bypass it. `publish_integration_event` and `reset_databases` need rate limits (token bucket: 5/min, 1/hour) — an AI loop can fire them indefinitely otherwise. `simulate_service_outage` must use `child_process.spawn('docker', ['stop', name], { shell: false })` — never ``exec(`docker stop ${name}`)``; the AI will pass service names you didn't anticipate. Sanitize `restaurantId` before seeding (`sha256(restaurantId).slice(0, 8)`) — it is interpolated into the seed string in §6.7 (DEV_MCP_SERVER_PLAN.md:187).

### 10.2 Phase 1 — Foundation & Scaffold

> **Phase 1 status:** ✅ Done (2026-07-17). All three items below implemented and verified locally (without Docker).

- **[✅] Decide type-stripping vs `tsc` compile here** — changing later means rewriting every `import` statement. Add `npm run typecheck` (`tsc --noEmit`) regardless of choice. *Resolved: type stripping adopted; `tsx` dropped; `npm run typecheck` wired.*
- **[✅] Wire SIGTERM/SIGINT handlers and per-pool close-on-shutdown before any tool code** — far easier to test with zero tools registered. *Resolved: `close-with-grace` wired; `process.on('unhandledRejection' | 'uncaughtException')` exit 1.*
- **[✅] Connection-verification step needs a per-pool `ping()` / health check** that fail-fasts (exit 1) on any one — the AI cannot recover from a misconfigured DB. *Resolved: `pingPostgres`, `pingMssql`, `pingRedis`, `pingRabbit` all run in `Promise.all` during boot; failure throws `DevMCPError(ConnectionError)` and exits 1.*

### 10.3 Phase 2 — Core Developer Tools

> **Phase 2 status:** ✅ Done (2026-07-17). 9 tools registered across 5 files. See "Phase 2 implementation notes" above for the deviations + bugs found.

- **`get_api_schema` JSON normalization** — `[⚠ deferred to Phase 3+]` run on a worker thread when the payload exceeds 256 KB (Catalog swagger.json is large). Phase 2 uses inline normalisation; defer until perf data shows a need.
- **`inspect_basket` "diff-prints the two states"** — `[✅]` picked the in-memory LRU keyed by `basketId` (stateful server, easier for the AI). Phase 2 stores the previous snapshot under the `${userId}:${restaurantId}` composite key.
- **`generate_dev_token` — pin `algorithm: 'HS256'` explicitly`** in `jwt.sign`. `[✅]` Pinned. Library default not relied on.
- **`get_system_snapshot` 3 s per-sub-query timeout** (DEV_MCP_SERVER_PLAN.md:204) — `[✅]` implemented via `withTimeout` helper using a timer that `.unref()`s so it doesn't keep the event loop alive.

### 10.4 Phase 3 — Data & Event Tools

- **`seed_test_menu` Marten upsert** — Marten uses an event-stream model; raw `INSERT ON CONFLICT` may bypass event tracking and silently desync projections. Read `Catalog.Infrastructure/Marten/Registry.cs` before implementing; prefer `IDocumentStore.BulkInsertAsync` with `IDocumentSession` upsert.
- **`create_mock_order`** — `mssql` driver requires parameterized queries; verify EF Core column types against `docs/architecture/db_relational_model.mermaid` before seeding.
- **`publish_integration_event` event-type lookup table** — **generate at build time** by scanning `BuildingBlocks.Messaging/Events/`. Do not hardcode — new events get added and a stale list is worse than none. Add a `prebuild` script: `tsx scripts/generate-event-types.ts`.
- **`inspect_dead_letters`** — paginate the RabbitMQ Management API messages endpoint; cap each returned payload at ~10 KB.
- **`reset_databases`** — require `confirm: true` **and** a `confirmText` field that must equal a target service name (e.g. `"catalog"`). Two-step confirmation makes accidental invocation nearly impossible.
- **`seed_historical_sales`** — `mssql` bulk copy needs `ADMINISTER BULK OPERATIONS` or `db_owner` on the dev container. Confirm docker-compose grants it; otherwise fall back to multi-row `INSERT`.
- **`trigger_scheduled_jobs`** — the companion dev-only HTTP endpoint is a security hole in any other context. Mitigate by gating it on `ASPNETCORE_ENVIRONMENT=Development` plus a shared dev-secret header.

### 10.5 Phase 4 — Flow Intelligence

- **`trace_business_flow("checkout")` must be idempotent** — generate unique IDs (`crypto.randomUUID()`) per run and clean up at the end, or accept a `cleanupRunId: string?` parameter that tears down a previous run.
- **`verify_flow_state` return shape must be typed** — `{ entityId, expected, actual: Record<System, State>, pass: boolean, failures: Array<{ system, expected, actual }> }`. The AI needs structured pass/fail, not freeform text.
- **The three `.mmd` files must be committed alongside the flow scripts** — drift between diagram and code is exactly the kind of bug this server exists to prevent. Add a CI lint that fails if a flow script changes without the corresponding `.mmd` mtime being updated.
- **End-to-end smoke test (DEV_MCP_SERVER_PLAN.md:405)** — wrap it in `node --test` so it runs in CI on every flow-script change. A green smoke test is the only reliable proof the plan's exit criteria is met.


