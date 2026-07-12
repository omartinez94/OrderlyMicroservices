# Orderly MCP Ecosystem — Future Roadmap

> **Status**: Planning — not started.
> **Related**: [DEV_MCP_SERVER_PLAN.md](./DEV_MCP_SERVER_PLAN.md) (in progress)

This document catalogues all planned MCP servers for the Orderly platform beyond the active `DevMCP` work. Each server targets a distinct audience, access layer, and set of capabilities.

---

## Overview

```text
Orderly MCP Ecosystem
│
├── Orderly.DevMCP.Server         ← IN PROGRESS (see DEV_MCP_SERVER_PLAN.md)
│   Audience : Backend developer / AI coding assistant
│   Access   : Direct DB + Docker
│   Mode     : Read/Write + destructive
│   Env      : LOCAL DEV ONLY
│
├── Orderly.OpsMCP.Server         ← NEXT — detailed plan: OPS_MCP_SERVER_PLAN.md
│   Audience : On-call engineers / system administrators
│   Access   : Real APIs + audit tables (read-only)
│   Mode     : Read-only, incident-response tooling
│   Env      : Production-safe
│
├── Orderly.ManagerMCP.Server     ← PLANNED
│   Audience : Restaurant managers via Web CRM
│   Access   : Real authenticated APIs (via YARP gateway)
│   Mode     : Read/Write (business operations only)
│   Env      : Production
│
├── Orderly.BiMCP.Server          ← PLANNED
│   Audience : Analytics users / Web CRM dashboard
│   Access   : Read replica or analytics DB
│   Mode     : Read-only analytical queries
│   Env      : Production
│
├── Orderly.KitchenMCP.Server     ← PLANNED
│   Audience : Kitchen staff via display tablet / KDS
│   Access   : Kitchen API + event bus
│   Mode     : Write (order state transitions only)
│   Env      : Production
│
└── Orderly.GuestMCP.Server       ← PLANNED (lowest priority)
    Audience : Customers via mobile app / kiosk
    Access   : YARP Gateway (public endpoints only)
    Mode     : Mostly read, light basket writes
    Env      : Production
```

---

## Priority & Sequencing

| Priority | Server | Reason |
|:---:|---|---|
| **1** | `DevMCP` | Already in progress — unblocks frontend development |
| **2** | `OpsMCP` | Read-only, production-safe, high business value for incident response |
| **3** | `ManagerMCP` | Directly monetisable — adds AI value to the Web CRM product |
| **4** | `BiMCP` | Analytics schema is already rich; no backend changes needed |
| **5** | `KitchenMCP` | Depends on Kitchen service being fully stable |
| **6** | `GuestMCP` | Requires security hardening and rate-limiting before public exposure |

---

## 1. Orderly.DevMCP.Server *(In Progress)*

**Detailed plan**: [DEV_MCP_SERVER_PLAN.md](./DEV_MCP_SERVER_PLAN.md)

A local-only Node.js MCP server exposing raw backend capabilities to AI coding assistants during frontend development. Direct database access, event bus manipulation, environment resets. **Strictly forbidden from running in any non-development environment.**

---

## 2. Orderly.OpsMCP.Server *(Next)*

**Detailed plan**: [OPS_MCP_SERVER_PLAN.md](./OPS_MCP_SERVER_PLAN.md)

A production-safe, **read-only** MCP server for on-call engineers and system administrators. Exposes audit trails, service health, dead letter queues, and notification failure logs via the real production APIs — never direct DB connections. Designed for incident response: an engineer can ask the AI "why did this order fail?" and get a full cross-service diagnostic report.

**Key tools**: `get_order_modification_trail`, `get_failed_notifications`, `get_outbox_dead_letters`, `get_service_health`, `get_pending_admin_approvals`, `get_price_audit`, `get_reservation_status`.

---

## 3. Orderly.ManagerMCP.Server *(Planned)*

A production MCP server embedded in the Web CRM, giving restaurant managers an AI assistant that can operate the platform through natural language. Unlike the Ops MCP, this one allows writes — toggling item availability, updating stock, approving order modifications — but only through the real authenticated APIs.

**Key tools**:
- `get_todays_overview(restaurantId)` — active orders, table map, walk-in queue, reservations
- `update_ingredient_stock(ingredientId, currentStock)` — "we're out of salmon"
- `toggle_item_availability(menuItemId, available)` — 86 an item mid-service
- `get_slow_moving_items(restaurantId, days)` — surfaces underperforming menu items from `MenuItemAnalytics`
- `get_feedback_summary(restaurantId, days)` — aggregates `CustomerFeedback` ratings
- `get_revenue_report(restaurantId, from, to)` — queries `Orders` and `OrderTimingAnalytics`
- `approve_order_modification(orderId, adminId)` — handles `RequiresAdminApproval` orders

**Dependencies**: JWT auth from Identity service. All mutations go through the YARP gateway, not direct DB.

---

## 4. Orderly.BiMCP.Server *(Planned)*

A read-only analytical MCP server targeting the Web CRM's reporting section. Acts as a "talk to your data" layer over the existing analytics schema — `MenuItemAnalytics`, `OrderTimingAnalytics`, `PriceHistory`, `CustomerFeedback`, `BulkOrderUploads`, `OrderItemPriceAudit`.

**Key tools**:
- `get_top_items(restaurantId, metric, period)` — by revenue, volume, or modification rate
- `get_peak_hours(restaurantId, days)` — aggregates `MorningOrders`/`AfternoonOrders`/`EveningOrders`
- `get_avg_order_timing(restaurantId, period)` — estimated vs actual prep times from `OrderTimingAnalytics`
- `get_price_change_history(menuItemId)` — full audit trail from `PriceHistory`
- `get_feedback_trends(restaurantId, days)` — tracks rating dimensions over time
- `compare_restaurant_performance(restaurantIdList, metric)` — cross-brand analysis using `Brands`

**Differentiator**: No writes whatsoever. Suitable to run against a production read replica. The AI acts as a data analyst, not an operator.

---

## 5. Orderly.KitchenMCP.Server *(Planned)*

A low-latency MCP server running on kitchen display tablets (KDS). Kitchen staff interact through voice or touch — "bump table 5", "flag the risotto, we're out of arborio". The server drives the existing Kitchen event pipeline (`KitchenOrder*IntegrationEvent` chain) via structured tool calls.

**Key tools**:
- `get_active_kitchen_orders(restaurantId)` — all orders in `Accepted`/`PrepStarted` state, sorted by urgency
- `bump_order(orderId)` — publishes `KitchenOrderBumpedIntegrationEvent`
- `mark_order_ready(orderId)` — publishes `KitchenOrderReadyIntegrationEvent`
- `cancel_kitchen_order(orderId, reason)` — publishes `KitchenOrderCancelledIntegrationEvent`
- `get_item_prep_queue()` — items across all active orders still in `PrepStatus = "pending"`
- `flag_ingredient_shortage(ingredientId)` — marks ingredient unavailable and notifies the manager

**Dependencies**: Depends on Kitchen service being fully stable and the KDS frontend being built.

---

## 6. Orderly.GuestMCP.Server *(Planned — Lowest Priority)*

A public-facing MCP server for customers interacting with an AI ordering assistant (QR code web app, mobile app, kiosk). All access goes through the YARP API Gateway — no internal services exposed directly.

**Key tools**:
- `get_menu(restaurantId)` — available items, prices, variations, ingredient lists
- `check_ingredient(restaurantId, ingredientName)` — live availability check
- `get_item_allergens(menuItemId)` — ingredient breakdown for dietary questions
- `add_to_basket(userId, restaurantId, items)` — calls Basket API
- `get_basket(userId)` — returns current basket for review
- `get_estimated_wait(restaurantId)` — estimated prep time from `OrderTimingAnalytics`
- `submit_feedback(orderId, ratings, comments)` — posts to `CustomerFeedback`

**Prerequisites before building**: Rate limiting on YARP gateway, customer identity/session model, security review of all public tool inputs.

---

## Cross-cutting concerns (all future servers)

| Concern | Decision |
|---|---|
| **Language** | Node.js + TypeScript (strict) — consistent with `DevMCP` |
| **SDK** | `@modelcontextprotocol/sdk` across all servers |
| **Input validation** | `zod` for all tool schemas — no `any` |
| **Shared types** | Extract a `@orderly/mcp-shared` internal package with common zod schemas (service name enum, restaurant ID type, etc.) once ≥2 servers are live |
| **Auth** | Production servers authenticate using real JWT tokens from Identity service — never dev secrets |
| **Transport** | HTTP/SSE for network-accessible servers; `stdio` considered only for local CLI tools |
| **Monitoring** | Each production MCP server should expose a `/health` endpoint and emit structured logs compatible with the existing observability stack |
