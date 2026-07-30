# Orderly.DevMCP.Server

> Local-only MCP server that exposes the OrderlyMicroservices backend to AI assistants during frontend development. Implements the [Model Context Protocol](https://modelcontextprotocol.io/) over HTTP/SSE so LAN clients (the Web CRM, the Mobile App, Claude Desktop, Cursor) can connect.

> [!CAUTION]
> **Strictly forbidden in production.** This server has direct read/write access to every database, can publish arbitrary events, can generate valid auth tokens, and can stop API containers. It refuses to run unless `NODE_ENV=development` and the dev-host allow-list passes.

---

## 1. What it does

17 tools across 9 modules (Phase 1 + 2 + 3 + 4 complete):

| Module | Tools |
|---|---|
| `auth` | `generate_dev_token`, `verify_token` |
| `api-discovery` | `get_api_schema` |
| `state-inspection` | `inspect_basket`, `inspect_order_pipeline` |
| `snapshot` | `get_system_snapshot`, `watch_system` |
| `log-tracing` | `get_recent_logs` |
| `data-seeding` | `seed_test_menu`, `create_mock_order` |
| `event-bus` | `publish_integration_event`, `inspect_dead_letters` |
| `infrastructure` | `reset_databases`, `simulate_service_outage` |
| `jobs` | `seed_historical_sales`, `trigger_scheduled_jobs` |
| `flow-tracing` | `trace_business_flow`, `get_flow_architecture`, `verify_flow_state` |

Run `get_api_schema({serviceName: 'catalog'})` for the full per-tool schema.

## 2. Requirements

- **Node.js 22.6+** (type stripping is native; no `tsc` build, no `tsx`).
- **Docker** running the `orderly-microservices/docker-compose.override.yml` stack (Catalog, Basket, Ordering, Identity, Kitchen, Discount + Yarp + Postgres × 4 + Redis + RabbitMQ + MSSQL).
- A JWT secret (≥ 16 chars) for `JWT_SECRET`.

## 3. Install

```bash
cd orderly-microservices/Orderly.DevMCP.Server
cp .env.example .env
# Edit .env — at minimum set JWT_SECRET to a fresh random value:
#   node -e "console.log(require('crypto').randomBytes(32).toString('base64url'))"
npm install
```

`npm install` does not need Docker — only `npm run dev` does.

## 4. Run

```bash
npm run dev          # nodemon-free --watch + .env
```

On a clean stack the boot log should look like:

```
{"msg":"DevMCP starting in development mode — refuses to run otherwise"}
{"msg":"opening backend connections…"}
{"msg":"all backends connected"}
{"msg":"MCP server listening","url":"http://0.0.0.0:8080/mcp","transport":"streamable-http"}
{"msg":"phase 4 ready — 20 tools registered"}
```

If any of the 7 connections fails the process exits 1 with a structured `DevMCPError` log.

## 5. Connect an AI client

The MCP transport is `streamable-http` on `http://localhost:8080/mcp` (or `http://<lan-ip>:8080/mcp` for non-localhost clients).

### Claude Desktop / Cursor / similar

Add the server to the AI config file:

```json
{
  "mcpServers": {
    "orderly-backend": {
      "type": "streamable-http",
      "url": "http://localhost:8080/mcp"
    }
  }
}
```

For LAN access (frontend repos on a different machine), swap `localhost` for the dev box IP (e.g. `http://192.168.1.65:8080/mcp`).

The full snippet is in [`../docs/sse-config-snippet.json`](../docs/sse-config-snippet.json).

## 6. Tool summary

| Tool | Purpose |
|---|---|
| `generate_dev_token(role, restaurantId?, userId?, ttlSeconds?)` | Signs an HS256 dev JWT. See warning below. |
| `verify_token(token)` | Decodes + validates a JWT. Cached 30 s. |
| `get_api_schema(serviceName)` | Fetches + normalises the OpenAPI doc for a service. Cached 5 min. |
| `inspect_basket(userId, restaurantId)` | Reads the cached basket from Redis. Diff vs previous observation. |
| `inspect_order_pipeline(orderId)` | Cross-queries OrderDb + RabbitMQ + Kitchendb for an order. |
| `get_system_snapshot(restaurantId?)` | One-shot cross-service state report. 3 s per sub-query. |
| `watch_system(intervalSeconds, ...)` | Streams snapshots via `notifications/message`. |
| `get_recent_logs(serviceName, lines?, level?)` | Tails the Docker container for a service. |
| `seed_test_menu(restaurantId, dryRun?)` | Inserts the canonical test menu. Idempotent. |
| `create_mock_order(restaurantId, status?)` | Creates a fake order in OrderDb. Returns the new OrderId. |
| `publish_integration_event(eventName, payload)` | Publishes a JSON event to `BuildingBlocks.Messaging.Events:{EventTypeName}`. Rate-limited 5/min. |
| `inspect_dead_letters()` | Lists failed messages from `*_error` queues. |
| `reset_databases(targets?, confirmText)` | **DESTRUCTIVE.** Two-step confirmation. Rate-limited 1/hour. |
| `simulate_service_outage(serviceName, durationSeconds?)` | Stops an API container for N seconds. Allowlist-enforced. |
| `seed_historical_sales(restaurantId, daysBack)` | Deterministic bulk insert into OrderDb. |
| `trigger_scheduled_jobs(jobName)` | HTTP POST to a dev-only endpoint. Requires `DEV_TRIGGER_SECRET`. |
| `trace_business_flow(flowName, cleanupRunId?)` | Executes a scripted golden path end-to-end. |
| `get_flow_architecture(flowName)` | Returns the Mermaid diagram for a flow. |
| `verify_flow_state(entityType, entityId, expectedState)` | Cross-system pass/fail. |

## 7. Security notes

- **JWT_SECRET** is the same secret the .NET services use to verify dev tokens. Rotate it whenever the dev team rotates. As of the v1.1 close pass, the `.NET-side fallback dev-secret handler` is implemented via `BuildingBlocks.Dev.DevJwtBearerFallbackExtensions.AddJwtAuthenticationWithDevFallback` — services that have it wired accept HS256 tokens signed with `JWT_SECRET` when the OpenIddict `Authority` is unreachable. Production services do not have the fallback wired.
- **`trigger_scheduled_jobs` → `/dev/trigger/*` endpoints** are now live on the .NET services via `BuildingBlocks.Dev.DevTriggerEndpointExtensions.MapDevTriggerEndpoint`. The MCP server POSTs to `http://basket.api:8080/_dev/trigger/clear-abandoned-baskets`, `http://ordering.api:8080/_dev/trigger/daily-reconciliation`, and `http://ordering.api:8080/_dev/trigger/outbox-relay` with the `X-Dev-Trigger-Secret` header (must match `DEV_TRIGGER_SECRET` on each .NET host). The endpoints are gated on `IsDevelopment()` + constant-time secret compare.
- `reset_databases` and `simulate_service_outage` are **DESTRUCTIVE**. The first requires a second `confirmText` field that must equal a target name; the second refuses to stop any container that isn't in the API allow-list (databases and `messagebroker` are always refused).
- `publish_integration_event` and `reset_databases` are rate-limited (5/min, 1/hour) so an AI loop can't fire them indefinitely.
- All DB / cache / broker factories call `assertDevHost` before opening a connection. The allow-list is `localhost,127.0.0.1` by default; override with `DEV_HOST`.
- Every tool call logs `{ tool, params (sanitized), durationMs, outcome }` — pino with `JWT_SECRET` / `password` / `Authorization` redaction.

## 8. Troubleshooting

| Symptom | Fix |
|---|---|
| `error: refusing to connect to "X"; allowed hosts: localhost,127.0.0.1` | Add the host to `DEV_HOST` in `.env` (e.g. `DEV_HOST=localhost,127.0.0.1,postgres`). |
| `Bad "options.issuer" option. The payload already has an "iss" property` | This was a bug in the auth tool during Phase 2 development — the canonical version puts `iss` / `aud` in the claims only, not in `jwt.sign` options. |
| `Cannot find name 'parameter property'` error at import | Make sure no `class Foo { constructor(private x: number) {} }` is used — type stripping forbids parameter properties. |
| Tools don't show up in the Inspector | Check that `npm run dev` printed `phase 4 ready — 20 tools registered` (or whatever phase). If the boot failed, the tools aren't registered. |

## 9. Development

```bash
npm run dev          # --watch + --env-file
npm run typecheck    # tsc --noEmit
npm test             # node --test (skips live-backend tests if Docker is down)
npm run lint:mmd     # fails if any resources/flows/*.mmd is older than tools/flow-tracing.ts
```

The `.mmd` lint exists because drift between diagram and code is the bug class this whole server exists to prevent. Touch a `.mmd` to silence the lint after review; never auto-update.

## 10. Verification with Docker (live-backend tests)

The default `npm test` skips the live-backend integration tests when Docker is unreachable — keeps CI hermetic. To run the full happy-path end-to-end against live backends:

```bash
# 1. Bring up the full backend stack
docker compose up -d

# 2. Opt in to live tests + ensure the trigger secret matches between
#    the MCP server and the .NET hosts.
export MCP_LIVE_TEST=1

# 3. Run the live test (exits 0 on the full happy path)
node --env-file=.env --test test/flows/checkout.live.test.ts
```

The live test asserts `doc.pass === true` (not just 2xx on HTTP steps), so it fails fast when downstream order-projection verification doesn't reconcile. CI doesn't run it because spinning up Docker in CI is out of scope; operators run it locally after a backend change to confirm the end-to-end pipeline.

The `discount-application` flow has its own gRPC client implementation (added in the v1.1 close pass) that loads `Protos/discount.proto` via `@grpc/proto-loader`. To enable it, copy or symlink `orderly-microservices/Services/Discount/Discount.Grpc/Protos/discount.proto` to `Orderly.DevMCP.Server/proto/discount.proto`. When the proto file is missing, the flow emits an `info` step explaining the gap rather than failing.
