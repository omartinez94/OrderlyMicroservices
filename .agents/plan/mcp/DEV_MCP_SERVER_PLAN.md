# Dev MCP Server — Implementation Plan

> Scope: Plan for building `Orderly.DevMCP.Server`, a local-only Node.js service that implements the Model Context Protocol (MCP). This server acts as an "AI Developer Gateway," connecting AI coding assistants directly to the OrderlyMicroservices backend during the development of frontend clients (Web CRM and Mobile App).

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

**Deliverables**:

- [ ] Initialize `Orderly.DevMCP.Server/` with `package.json`, `tsconfig.json`, `.env.example`, `.gitignore`.
- [ ] Install all dependencies: `@modelcontextprotocol/sdk`, `zod`, `pg`, `mssql`, `ioredis`, `amqplib`, `jsonwebtoken`, `typescript`, `tsx` (dev runner), `@types/*`.
- [ ] Implement `config/env.ts` — zod-validated env schema. Server exits on startup if any required variable is missing or `NODE_ENV !== "development"`.
- [ ] Implement `config/services.ts` — typed map of service name → base URL, derived from `docker-compose.override.yml` ports.
- [ ] Implement `config/databases.ts` — connection factories for `pg` (×4 instances), `mssql`, `ioredis`, `amqplib`.
- [ ] Implement `db/postgres-client.ts`, `db/mssql-client.ts`, `db/redis-client.ts`, `db/rabbitmq-client.ts`.
- [ ] Implement `index.ts` — boots `McpServer` with `StreamableHTTPServerTransport`, registers zero tools, verifies all DB connections on startup and logs status.
- [ ] Add `Orderly.DevMCP.Server/` to root `.dockerignore`.
- [ ] Verify with: `npx @modelcontextprotocol/inspector http://localhost:8080/mcp` — inspector connects, no tools listed.

**Exit criteria**: Inspector connects. All 4 PostgreSQL pools, SQL Server, Redis, and RabbitMQ report `connected` in startup logs.

---

### Phase 2 — Core Developer Tools

**Goal**: The AI can immediately become situationally aware of the running backend, generate auth tokens, and read API contracts — the minimum viable toolset for starting frontend development.

**Deliverables**:

- [ ] **`tools/api-discovery.ts`** — `get_api_schema(serviceName)` (§6.1)
  - Fetches and normalises Swagger JSON for all 5 services.
  - Returns descriptive error when service is not reachable or not in `Development` mode.

- [ ] **`tools/auth.ts`** — `generate_dev_token` + `verify_token` (§6.4)
  - Signs JWTs with `jsonwebtoken` using `JWT_SECRET` from env.
  - `verify_token` decodes without an API call.

- [ ] **`tools/state-inspection.ts`** — `inspect_basket` + `inspect_order_pipeline` (§6.3)
  - `inspect_basket`: reads from Redis, diff-prints on repeated calls.
  - `inspect_order_pipeline`: cross-queries SQL Server + RabbitMQ Management API + Kitchen PostgreSQL.

- [ ] **`tools/snapshot.ts`** — `get_system_snapshot` + `watch_system` (§6.9)
  - All sub-queries run in parallel via `Promise.allSettled` with 3-second individual timeouts.
  - `watch_system` streams snapshots over SSE at the requested interval.

- [ ] **`tools/log-tracing.ts`** — `get_recent_logs(serviceName, lines?, level?)` (§6.4 of original plan)
  - Shells out to `docker logs --tail {lines} {containerName}`.
  - Severity filter for `Warning`/`Error` when `level: "error"` is passed.

**Exit criteria**: A connected AI assistant can call `get_system_snapshot()`, receive a full cross-service report, then call `generate_dev_token("Admin", ...)` and use the token to hit a live API endpoint successfully.

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


