# Ops MCP Server — Implementation Plan

> **Scope**: Plan for building `Orderly.OpsMCP.Server`, a production-safe Node.js service that implements the Model Context Protocol (MCP). This server acts as an **"AI Incident Response Gateway"**, giving on-call engineers and system administrators a conversational interface to diagnose, audit, and monitor the live Orderly backend.
>
> **Roadmap context**: [FUTURE_MCP_ROADMAP.md](./FUTURE_MCP_ROADMAP.md) — Priority 2, after `DevMCP`.

---

## 0. Conventions

### 0.1 Mandate

> **All implementation work on this plan MUST use modern Node.js and the official MCP SDKs.**
>
> The server will be built using TypeScript and the `@modelcontextprotocol/sdk`. Unlike `DevMCP`, this server connects **exclusively through the real production APIs** — never via direct database connections. The only exception is the RabbitMQ Management HTTP API (`15672`), which is the accepted monitoring interface for the broker.

### 0.2 Core principle — Read-Only Without Exception

> [!IMPORTANT]
> Every tool in this server is **read-only**. No tool may write, update, delete, or publish anything. If a future tool proposal involves a mutation, it belongs in `ManagerMCP`, not here. This is the single hardest rule of this project.

### 0.3 Code-quality guard rails

- **TypeScript — mandatory**: All source files must be `.ts`. No plain `.js` files in `src/`. Compiled with `tsc` before running.
- **Strict `tsconfig.json`**: Same compiler options as `DevMCP`:
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
- **Zod for schemas**: All tool input schemas defined as `z.ZodObject` types — no `any` or untyped objects.
- **Every tool call is logged**: Each invocation must emit a structured log entry (timestamp, tool name, caller identity, input summary) for compliance and audit purposes.

---

## 1. Context

The Orderly backend is a multi-tenant restaurant management system serving multiple brands and restaurants in production. When an incident occurs — a failed order, a stuck notification, a dead-lettered event, a customer complaint — an on-call engineer currently has to:

1. Log into the server / VPN
2. Connect to the database directly to query `OrderModificationLog`, `NotificationLog`, or the Marten outbox
3. Open the RabbitMQ management dashboard manually
4. Cross-reference multiple services by hand to reconstruct what happened

This is slow, requires elevated database access for all engineers, and leaves no audit trail of what was queried during the investigation.

An AI assistant equipped with `OpsMCP` tools can answer "what happened to order X?" in seconds, with no direct DB access needed and a full log of every query made.

---

## 2. Goal

Build `Orderly.OpsMCP.Server`, an MCP server that exposes the following capabilities to AI assistants during incidents:

1. **Order Diagnostics**: Reconstruct the full lifecycle of any order — status history, price audit, modifications.
2. **Service Health**: Check the health of all running services and the message broker in one call.
3. **Audit & Compliance**: Surface order modification logs, admin approvals, and price change history.
4. **Notification Failures**: Identify failed emails/SMS/WhatsApp notifications and their reasons.
5. **Event Bus Monitoring**: Inspect dead-letter queues and pending messages without touching the broker configuration.
6. **Reservation & Table Ops**: View reservation status and walk-in queue state for a restaurant on a given date.

---

## 3. Out of Scope

- **Any write operation**: No order updates, no notification retries, no queue purges. Read-only without exception.
- **Direct database connections**: All data comes through the real production APIs or the RabbitMQ Management HTTP API. No `pg`, `mssql`, or `ioredis` clients.
- **Development tooling**: Features that belong in `DevMCP` (seeding, resets, token generation) must not be added here.
- **Customer-facing data**: No raw customer PII beyond what is returned by the authenticated production API response. The server does not query or expose user password hashes or payment card data.
- **Infrastructure management**: No Docker commands, no container restarts, no deployments.

---

## 4. Tech Decisions

| Decision | Choice | Reason |
| :--- | :--- | :--- |
| **Runtime** | **Node.js (TypeScript)** | Consistent with the broader Orderly MCP ecosystem. |
| **API access** | Native `fetch` | All data comes from the real production REST APIs via the YARP gateway or direct service URLs. |
| **RabbitMQ monitoring** | RabbitMQ Management HTTP API (`/api/*`) | The accepted, stable monitoring interface — no AMQP connection needed. |
| **Tool Input Validation** | `zod` | Same standard as `DevMCP`. |
| **Auth** | JWT Bearer token (passed by the AI client) | The server forwards a caller-supplied JWT to every API request. It does not generate tokens — that is `DevMCP`'s job. |
| **Transport** | **HTTP / SSE (Server-Sent Events)** | Exposes `http://<host>:8081/sse` (note: different port from `DevMCP` to allow both to run concurrently). |
| **Audit logging** | Structured JSON logs to stdout | Every tool call is logged with timestamp, tool name, input parameters, and caller identity. Pipe to your existing log aggregator. |

---

## 5. Folder Layout

```text
OrderlyMicroservices/
  Orderly.OpsMCP.Server/
    package.json
    tsconfig.json
    .env.example
    src/
      index.ts                    -- MCP Server init + SSE transport + startup health check
      config/
        services.ts               -- Base URLs for all production APIs + gateway
        env.ts                    -- Zod-parsed environment variables
      middleware/
        audit-logger.ts           -- Wraps every tool call with structured audit logging
        auth-forwarder.ts         -- Extracts and forwards caller JWT to downstream API calls
      tools/
        order-diagnostics.ts      -- Order lifecycle, modification trail, price audit
        service-health.ts         -- Health check aggregator across all services
        notification-audit.ts     -- Failed notifications, notification history
        event-bus-monitor.ts      -- DLQ inspection, queue stats (RabbitMQ Management API)
        reservation-ops.ts        -- Reservation status, walk-in queue state
        admin-approvals.ts        -- Pending admin approval queue
      http/
        api-client.ts             -- Authenticated fetch wrapper with timeout + retry
        rabbitmq-client.ts        -- RabbitMQ Management HTTP API client (read-only)
```

---

## 6. Tool Specification

The MCP server will register the following tools on startup. **All tools are read-only.**

---

### 6.1 Order Diagnostics (`order-diagnostics.ts`)

**`get_order_summary(orderId)`**

- **What it does**: Calls `GET /orders/{orderId}` on the Ordering API and returns the full order state — status, items, billing/delivery address, payment method (masked), discount applied, timestamps, and the assigned waiter/confirmer/completer user IDs.
- **Why it matters**: The first tool any engineer calls at the start of an incident. Gives the complete picture of the order in one shot.
- **Implementation notes**:
  - Masks `CardNumber` — returns only the last 4 digits. Never returns `CVV`.
  - Includes `RequiresAdminApproval` and `ApprovedByAdminId` so approval bottlenecks are immediately visible.

**`get_order_modification_trail(orderId)`**

- **What it does**: Fetches the full `OrderModificationLog` for the given order — every status change, item addition/removal, price change, and manager override, with before/after state snapshots and who made each change.
- **Why it matters**: The primary tool for answering "what happened to this order?". Surfaces exactly when and why an order went off the happy path.
- **Implementation notes**: Data comes from `GET /orders/{orderId}/modifications` on the Ordering API (or directly from the Catalog API's Marten document endpoint if the Ordering API does not expose it). Returns entries in chronological order.

**`get_price_audit(orderId)`**

- **What it does**: Fetches the `OrderItemPriceAudit` records for every item in the order. Shows the menu price at order time vs the price actually charged, the breakdown of variations and customizations, any discount applied, and who captured the price.
- **Why it matters**: Answers "was this customer charged the correct amount?" and "why does the total not match the menu price?".

**`get_order_snapshot(orderId)`**

- **What it does**: Fetches the `OrderSnapshot` Marten document — the complete JSON snapshot of the order including menu prices, tax configuration, and discount rules as they were at the moment the order was placed.
- **Why it matters**: When a price dispute arises days or weeks later, this is the immutable source of truth for what the customer saw and agreed to.

---

### 6.2 Service Health (`service-health.ts`)

**`get_service_health()`**

- **What it does**: Concurrently pings the `/health` endpoint of every service — `catalog.api`, `basket.api`, `ordering.api`, `kitchen.api`, `identity.api`, `discount.grpc` — and the RabbitMQ Management API. Runs all checks via `Promise.allSettled` with a 5-second timeout per service. Returns a structured health report.
- **Why it matters**: The first thing to run at the start of any incident. Immediately surfaces whether the problem is a service outage, a dependency failure, or a data issue.
- **Returns**:
  ```json
  {
    "checkedAt": "2026-07-12T11:00:00Z",
    "overall": "degraded",
    "services": [
      { "name": "catalog.api", "status": "healthy", "latencyMs": 12 },
      { "name": "ordering.api", "status": "unhealthy", "error": "Connection refused" },
      { "name": "messagebroker", "status": "healthy", "latencyMs": 5 }
    ]
  }
  ```

**`get_service_version(serviceName?)`**

- **What it does**: Calls `/info` or `/version` on the named service (or all services if omitted) and returns the deployed assembly version, build date, and environment name.
- **Why it matters**: During incidents caused by a bad deployment, confirms which version is actually running before rolling back.

---

### 6.3 Notification Audit (`notification-audit.ts`)

**`get_failed_notifications(restaurantId, since)`**

- **What it does**: Fetches `NotificationLog` records where `Status = "failed"` for the given restaurant, since the specified timestamp. Returns the recipient, channel (`email`/`whatsapp`/`sms`), message type, related order/reservation ID, and the failure reason.
- **Why it matters**: When a customer says "I never received my order confirmation", this is the first place to look. Surfaces exactly which notification failed and why.

**`get_notification_history(relatedEntityId)`**

- **What it does**: Fetches all `NotificationLog` entries for a given `OrderId` or `ReservationId` — sent, failed, and pending — in chronological order.
- **Why it matters**: Provides the full notification timeline for a single entity, making it easy to see what the customer was and was not notified about.

---

### 6.4 Event Bus Monitor (`event-bus-monitor.ts`)

**`get_queue_stats()`**

- **What it does**: Calls the RabbitMQ Management API (`GET /api/queues`) and returns a summary of all queues — name, message count, consumer count, and ready/unacked/total message breakdown.
- **Why it matters**: Immediately shows if a consumer is falling behind (queue depth growing) or if a queue has no consumers attached.

**`get_dead_letter_messages(queueName?, limit?)`**

- **What it does**: Fetches messages from dead-letter queues via the RabbitMQ Management API (`GET /api/queues/{vhost}/{queue}/get`). Returns the message payload, routing key, death reason, and original queue name. Default `limit = 10`.
- **Why it matters**: Dead-lettered messages are the primary evidence of consumer failures. This tool surfaces them without requiring RabbitMQ dashboard access.
- **Implementation notes**:
  - Uses the Management API's message preview endpoint — does **not** consume/ack the messages. Messages remain in the DLQ after this call.
  - Redacts any PII fields from the payload (e.g., `CardNumber`, `EmailAddress`) before returning.

**`get_exchange_bindings(exchangeName?)`**

- **What it does**: Calls `GET /api/bindings` to show which queues are bound to which exchanges. Useful for diagnosing routing issues where an event is published but no consumer receives it.

---

### 6.5 Reservation & Table Operations (`reservation-ops.ts`)

**`get_reservations(restaurantId, date)`**

- **What it does**: Fetches all reservations for the restaurant on the given date, with their current status (`pending`, `confirmed`, `seated`, `completed`, `cancelled`, `no_show`), party size, table assignment, and any special requests.
- **Why it matters**: When a customer calls to dispute a reservation, or a manager reports a seating conflict, this gives the full picture instantly.

**`get_walkin_queue(restaurantId)`**

- **What it does**: Returns the current walk-in queue state — all waiting, notified, and seated entries with estimated wait times, party sizes, and assigned tables.
- **Why it matters**: During busy periods, gives operational visibility into queue depth and whether customers are being seated in order.

**`get_table_map(restaurantId)`**

- **What it does**: Returns all tables for the restaurant with their current status (`available`, `occupied`, `reserved`, `cleaning`, `needs_attention`), capacity, and the `CurrentOrderId` if occupied.
- **Why it matters**: Surfaces table-level issues — a table stuck in `occupied` when it should be `available`, or a `needs_attention` flag that wasn't cleared.

---

### 6.6 Admin Approvals (`admin-approvals.ts`)

**`get_pending_approvals(restaurantId?)`**

- **What it does**: Fetches all orders with `RequiresAdminApproval = true` and `ApprovedAt = null` across the given restaurant (or all restaurants if omitted). Returns the order ID, order number, reason for approval requirement, amount, and how long it has been waiting.
- **Why it matters**: Approval bottlenecks silently block orders from progressing. This tool makes them visible without anyone having to manually check the order list.

**`get_approval_history(restaurantId, since)`**

- **What it does**: Fetches all orders that required admin approval since the given timestamp — both approved and still pending — with who approved them and when.
- **Why it matters**: Audit trail for approval actions. Useful when a manager is queried about why a large or unusual order was processed.

---

### 6.7 Ingredient & Stock Visibility (`catalog-ops.ts`)

**`get_low_stock_ingredients(restaurantId, thresholdMultiplier?)`**

- **What it does**: Fetches all ingredients where `CurrentStock <= MinimumStock * thresholdMultiplier` (default `1.0`). Returns ingredient name, unit, current stock, minimum stock, and whether it is currently marked unavailable.
- **Why it matters**: Prevents "why are items showing as unavailable?" incidents. Immediately shows which ingredients triggered the unavailability.

**`get_unavailable_items(restaurantId)`**

- **What it does**: Returns all menu items currently marked `IsAvailable = false` or `AvailabilityStatus = "unavailable"`, with the ingredient that triggered the unavailability if known.
- **Why it matters**: Quick way to confirm which items a restaurant's customers cannot currently order.

---

### 6.8 Centralized Logging & Tracing (`log-viewer.ts`)

**`search_application_logs(serviceName, query, since, level?)`**

- **What it does**: Queries the centralized logging system (or raw container logs) for a specific microservice. Returns timestamped log entries matching the query string or log level (e.g., `Error`, `Warning`).
- **Why it matters**: Allows the AI to instantly pull exception stack traces or hunt for specific error codes without an engineer needing to manually write KQL/LogQL queries.

**`trace_correlation_id(correlationId)`**

- **What it does**: Fetches log entries across *all* microservices that share the same correlation ID or trace ID.
- **Why it matters**: Crucial for distributed systems. If an order fails, this tool reveals the exact path the request took (e.g., Gateway -> Ordering -> RabbitMQ -> Catalog) and exactly which service threw the exception.

---

## 7. Cross-Repository AI Communication

Since the Ops MCP server runs on the same local network server as the backend (`192.168.1.65`) but targets production APIs, communication is identical in shape to `DevMCP` but on a different port:

1. **Server Startup**: The Node.js OpsMCP server exposes `http://192.168.1.65:8081/sse`.
2. **AI Client Configuration**:
    ```json
    {
      "mcpServers": {
        "orderly-ops": {
          "type": "sse",
          "url": "http://192.168.1.65:8081/sse"
        }
      }
    }
    ```
3. **Auth flow**: The AI client must supply a valid JWT (generated separately via the Identity service or via `DevMCP`'s `generate_dev_token` in dev). The OpsMCP server extracts this token from the SSE connection context and forwards it on every downstream API request via `Authorization: Bearer {token}`.

> [!NOTE]
> The OpsMCP server itself never stores or generates credentials. It is a pass-through — the caller's identity governs what the downstream APIs return.

---

## 8. Security Guardrails

> [!CAUTION]
> Even though this server is read-only, it surfaces sensitive operational data — order contents, customer contact details, notification failures, and message payloads. Access must be tightly controlled.

| Risk | Mitigation |
|---|---|
| **Unauthorized access** | The server validates the incoming JWT on every request. Requests without a valid `Admin` or `Manager` role claim are rejected before any tool executes. |
| **PII exposure in tool responses** | `CardNumber` is masked to last 4 digits. `CVV` is never returned. `EmailAddress` and `CustomerPhone` in dead-letter payloads are redacted to `***@***.***` and `+***-***-**XX`. |
| **Server running without audit logging** | `index.ts` checks that the audit logger is wired before registering any tools. Server exits if the logger fails to initialise. |
| **Accidental mutation via the Management API** | `rabbitmq-client.ts` is constructed with a base URL allowlist — only `GET` requests to `/api/queues`, `/api/bindings`, and `/api/exchanges` are permitted. Any attempt to call a `DELETE`, `POST`, or `PUT` route throws at the client level. |
| **Running against wrong environment** | The `ORDERLY_API_BASE_URL` env variable must end in a known domain pattern. A misconfigured URL pointing to localhost dev services causes a startup warning and requires `FORCE_NON_PROD=true` to override. |
| **DLQ message consumption** | The RabbitMQ Management API preview endpoint is used — messages are **not consumed or acknowledged**. They remain in the queue after being read. |

---

## 9. Development Phases

### Phase overview

| Phase | Name | Tool groups delivered | Goal |
|:---:|---|---|---|
| **1** | Foundation & Auth | — | Runnable server, JWT forwarding, audit logging wired |
| **2** | Incident Core | §6.1 · §6.2 | AI can answer "what happened to this order?" end-to-end |
| **3** | Operational Visibility | §6.4 · §6.5 · §6.6 | AI can inspect queues, reservations, and approval backlogs |
| **4** | Full Audit Coverage | §6.3 · §6.7 | AI can diagnose notification failures and stock-driven outages |

---

### Phase 1 — Foundation & Auth

**Goal**: A running MCP server with zero tools, but with JWT forwarding, audit logging, and startup health check all wired correctly.

**Deliverables**:

- [ ] Initialize `Orderly.OpsMCP.Server/` with `package.json`, `tsconfig.json`, `.env.example`, `.gitignore`.
- [ ] Install dependencies: `@modelcontextprotocol/sdk`, `zod`, `typescript`, `tsx`, `@types/*`.
- [ ] Implement `config/env.ts` — zod-validated env schema including `ORDERLY_API_BASE_URL`, `RABBITMQ_MGMT_URL`, `JWT_REQUIRED_ROLE`.
- [ ] Implement `config/services.ts` — typed map of all production service URLs.
- [ ] Implement `http/api-client.ts` — authenticated `fetch` wrapper that:
  - Forwards the caller's JWT as `Authorization: Bearer`
  - Enforces a 10-second timeout per request
  - Returns typed errors on non-2xx responses
- [ ] Implement `http/rabbitmq-client.ts` — read-only RabbitMQ Management API client. Allowlist: `GET /api/queues`, `GET /api/bindings`, `GET /api/exchanges`, `GET /api/queues/{vhost}/{queue}/get`.
- [ ] Implement `middleware/audit-logger.ts` — wraps every tool invocation: logs tool name, caller role, input summary (PII stripped), timestamp, and response status.
- [ ] Implement `middleware/auth-forwarder.ts` — extracts JWT from the MCP session context. Rejects requests missing a token or lacking the required role claim.
- [ ] Implement `index.ts` — boots server, wires middleware, registers zero tools, runs startup health check against all configured service URLs.
- [ ] Add `Orderly.OpsMCP.Server/` to root `.dockerignore`.

**Exit criteria**: MCP Inspector connects to `http://localhost:8081/mcp`. An unauthenticated request is rejected with a clear error. An authenticated request with the wrong role is also rejected.

---

### Phase 2 — Incident Core

**Goal**: The minimum viable toolset for answering the most common incident question: "what happened to this order?".

**Deliverables**:

- [ ] **`tools/order-diagnostics.ts`** (§6.1) — `get_order_summary`, `get_order_modification_trail`, `get_price_audit`, `get_order_snapshot`
  - `get_order_summary`: masks `CardNumber`, never returns `CVV`.
  - `get_order_modification_trail`: chronological, includes `PreviousData`/`NewData` diff.
  - `get_price_audit`: returns per-item breakdown with discount attribution.
  - `get_order_snapshot`: immutable order-time snapshot from Marten document.

- [ ] **`tools/service-health.ts`** (§6.2) — `get_service_health`, `get_service_version`
  - All health checks run in parallel via `Promise.allSettled` with 5-second timeout.
  - Returns `overall: "healthy" | "degraded" | "unhealthy"` at the top level.

**Exit criteria**: An engineer can ask the AI "diagnose order {orderId}" and receive: current status, full modification history, price audit, and service health — all in one conversation turn.

---

### Phase 3 — Operational Visibility

**Goal**: Expand coverage to the event bus, reservations, and admin approval queue — the operational areas most likely to cause silent failures.

**Deliverables**:

- [ ] **`tools/event-bus-monitor.ts`** (§6.4) — `get_queue_stats`, `get_dead_letter_messages`, `get_exchange_bindings`
  - `get_dead_letter_messages`: does NOT consume messages. PII fields redacted in returned payloads.
  - `get_exchange_bindings`: useful for diagnosing missing consumer bindings.

- [ ] **`tools/reservation-ops.ts`** (§6.5) — `get_reservations`, `get_walkin_queue`, `get_table_map`
  - All three return live state from the Catalog API.
  - `get_table_map` includes `CurrentOrderId` for occupied tables, allowing immediate drill-down.

- [ ] **`tools/admin-approvals.ts`** (§6.6) — `get_pending_approvals`, `get_approval_history`
  - `get_pending_approvals`: includes wait time since order creation to surface time-sensitive bottlenecks.

**Exit criteria**: An engineer can call `get_queue_stats()` and see if any queues are backing up, then call `get_dead_letter_messages()` to see what failed, then call `get_pending_approvals()` to check if there is an unrelated approval backlog — all without opening a browser.

---

### Phase 4 — Full Audit Coverage

**Goal**: Close the remaining gaps — notification failures and ingredient-driven item outages. This phase completes the tool surface area and prepares the server for distribution.

**Deliverables**:

- [ ] **`tools/notification-audit.ts`** (§6.3) — `get_failed_notifications`, `get_notification_history`
  - PII redaction enforced: email addresses and phone numbers are partially masked.
  - Both tools support pagination via `limit` and `offset` parameters.

- [ ] **`tools/catalog-ops.ts`** (§6.7) — `get_low_stock_ingredients`, `get_unavailable_items`
  - `get_low_stock_ingredients`: configurable threshold multiplier.
  - `get_unavailable_items`: links unavailable items back to their triggering ingredient where possible.

- [ ] **`tools/log-viewer.ts`** (§6.8) — `search_application_logs`, `trace_correlation_id`
  - `trace_correlation_id`: queries across all configured service logging endpoints in parallel to reconstruct the distributed trace.

- [ ] **Audit log export**: Add a `get_tool_audit_log(since, tool?)` meta-tool that returns the server's own audit log — what tools were called, by whom, and when. This is the compliance record of who queried what during an incident.

- [ ] **README for `Orderly.OpsMCP.Server/`**: Covers server setup, `.env` configuration, required JWT role, available tools, and the PII redaction policy.

- [ ] **Runbook entry**: Add a section to the engineering runbook documenting OpsMCP as the first tool to reach for during an incident, with example prompts for common scenarios.

**Exit criteria**: An engineer can go from "customer complained about order X" to a full incident report — order state, modification trail, price audit, notification history, and relevant queue stats — in a single AI conversation, without opening a browser, database client, or SSH session.
