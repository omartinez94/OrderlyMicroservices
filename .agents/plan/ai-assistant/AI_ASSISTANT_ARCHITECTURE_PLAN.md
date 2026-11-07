# Orderly In-App AI Assistant & Token Quota Metering Engine — Implementation Plan

> Scope: Delivers an end-to-end, enterprise-grade in-app AI Assistant for Orderly. Implements a secure backend orchestration service in .NET 10 (Semantic Kernel) with tiered model routing, role-scoped tool injection, background execution via MassTransit/SignalR, an atomic Redis ZSET-backed token metering engine with self-healing reservation TTLs (Hard Cap by default + Opt-in Safety Ceiling), PostgreSQL usage ledger persistence, and an industrial-grade React 19 chat interface in OrderlyWeb adhering to *The Stainless Line* design system.

---

## Status

> **Plan version**: `v1.1` (2026-11-06) — upgraded with ZSET self-healing quota reservations, stateless Proposal-Only Write Tools, direct internal service discovery with JWT propagation, role-scoped tool pruning, cached-token weighted billing, event-driven Redis context snapshots, and detached background job execution.
> **Current state**: 🚧 Phase 5 in progress

| Phase | Name | Status |
|:-----:|---|:-----:|
| 1 | Domain Entities, PostgreSQL Persistence & Outbox Events | ✅ Done |
| 2 | Redis ZSET Self-Healing Metering Engine & Weighted Lua Reservations | ✅ Done |
| 3 | AI Assistant Orchestrator (.NET 10 / Semantic Kernel), Tiered Routing & Read/Proposal Tools | ✅ Done |
| 4 | Event-Driven Context Snapshots & Detached Background AI Workers (MassTransit + SignalR) | ✅ Done |
| 5 | Carter Minimal APIs, YARP Ingress Routing & Deterministic Action Execution | 🚧 In progress |
| 6 | OrderlyWeb UI Host, Responsive Sheet, Redux/RTK Query & SignalR Background Job Sync | 🔒 Blocked |
| 7 | Quota Transparency Widget, Discovery Matrix, Tool Confirmation Cards & E2E Verification | 🔒 Blocked |

> **Legend**: ✅ Done · 🚧 In progress · ⏸ Pending · 🔒 Blocked

> **Commit messages**: Conventional Commits (`feat:`, `docs:`, `chore:`, `test:`, `fix:`). Short subject, ≤72 chars, imperative mood, no trailing period. Example: `feat(ai): add redis zset self-healing quota reservation engine`.

> **Update rule**: **On every phase completion, the plan MUST be updated in the same commit as the phase work.** The plan is the source of truth for what was decided and what shipped; a phase that ships without a plan update is a phase that drifted.

---

## 0. Skill & documentation conventions

### 0.1 Skill mandate — `csharp-developer` & `add-rtk-query-slice`

> **All backend C# code MUST adhere to the `csharp-developer` skill (modern C# 14, .NET 10 Minimal APIs via Carter, strict NodaTime date handling, explicit types, and zero `DateTime`). All frontend integration MUST follow `add-rtk-query-slice` and the design tokens in `OrderlyWeb/DESIGN.md`.**

### 0.2 Code-quality guard rails

- **Date/Time**: Always use **NodaTime** (`Instant`, `LocalDate`, `ZonedDateTime`). Native `DateTime` / `DateTimeOffset` is strictly forbidden across the backend.
- **Tenant Isolation**: Never bypass `ICurrentRestaurantProvider`. All AI operations, live context snapshots, and downstream tool calls must be strictly scoped to `restaurantId`.
- **Zero Trust in LLM & Direct Internal Calls**: Tool calls made by the AI model MUST forward the caller's actual JWT Bearer token via a shared `UserJwtPropagationHandler` directly to internal downstream microservices (`http://catalog.api:8080`, `http://ordering.api:8080`, etc.), avoiding an unnecessary loopback hop through YARP while preserving identical RBAC and tenant policies.
- **Self-Healing Concurrency Metering**: All token checks and reservations must execute atomically in Redis via Lua scripts (`EVALSHA`) using a Sorted Set (`ZSET`) for in-flight holds so crashed streams auto-evict expired reservations via `ZREMRANGEBYSCORE`.
- **Stateless Human-in-the-Loop (HITL)**: Write tools (`create_order`, `toggle_item_86`, permission changes) MUST NOT mutate state during the LLM loop or hold SSE streams open. They emit a signed `ActionProposal` and end the turn; user confirmation triggers a deterministic HTTP endpoint (`0` extra LLM tokens).
- **Frontend Strictness**: React 19 functional components only, strict TypeScript (no `any`), no inline styles, all UI text localized via `react-i18next` (`en` + `es`), and adherence to *The Stainless Line* design tokens (Induction Flame ≤ 10% high-heat rule).

---

## 1. Context

Orderly restaurant staff (chefs, hosts, managers, waiters) need an intelligent in-app assistant to rapidly inspect order queues, check 86'd inventory, query sales margins, and trigger operational mutations without navigating through deeply nested menus.

However, introducing AI directly to end users in a high-tempo hospitality environment introduces severe operational and financial risks:

1. **Security & Tenant Leaks**: An untrusted model or prompt injection must never cross restaurant tenant boundaries or allow a kitchen staff member to grant administrative permissions.
2. **"Bill Shock" & Token Waste**: Restaurants operate on thin margins (3–7%). Sending bloated tool schemas on every turn, re-invoking the LLM just to confirm a button click, or leaking unclosed Redis token reservations will rapidly exhaust allowances.
3. **Synchronous Stream Fragility**: Kitchen and floor tablets frequently lock or switch zones. Tying long-running analytical queries exclusively to synchronous HTTP streams causes dropped work and wasted tokens.
4. **Execution Reliability**: Direct database writes bypass domain events, transactional outboxes, and business validation.

This plan establishes a unified, token-efficient managed AI architecture backed by a **Hard Cap by default with an Opt-in Safety Ceiling**, giving restaurant owners complete financial predictability and sub-second operational responsiveness.

---

## 2. Goal

1. **Native .NET 10 AI Assistant Service (`Assistant.API`)**:
   - Integrates Microsoft Semantic Kernel with tiered model routing (`flash-lite` for fast tool routing/ops, `pro`/`gpt-4o` for deep manager analytics).
   - Dynamically prunes injected `KernelPlugin` tool schemas based on the caller's JWT role (`KitchenStaff`, `Waiter`, `Manager`), cutting prompt token overhead by 50–70%.
   - Executes **Read Tools** directly against internal microservices via `UserJwtPropagationHandler` and returns signed **`ActionProposal`** payloads for **Write Tools** without holding SSE connections open.
2. **Self-Healing Atomic Token Metering & Quota Engine**:
   - Tracks prompt tokens, completion tokens, and provider-cached tokens (weighted at `0.25x` billing cost).
   - Uses a Two-Phase Redis Lua reservation engine backed by a TTL-scored `ZSET` (`ai:reservations:{restaurantId}:{yyyyMM}`) that automatically purges orphaned holds if a node crashes mid-stream.
   - Enforces a **Hard Cap by default** (RFC 7807 `429 Too Many Requests`) once the included monthly allowance is consumed, with an **Opt-in Safety Ceiling** toggled by the Restaurant Admin.
   - Records immutable audit records in PostgreSQL (`AiUsageLedger`) via MassTransit transactional outbox.
3. **Background LLM Execution & Event-Driven Context Snapshots**:
   - Maintains compact (~150-token) per-restaurant live operational snapshots in Redis (`ai:snapshot:{restaurantId}`) updated asynchronously by MassTransit consumers (current 86'd items, delayed KDS ticket count, active tables), eliminating repetitive multi-turn read calls.
   - Offloads heavy multi-step analytical prompts to a bounded background worker (`Channel<AiBackgroundJob>` / MassTransit consumer) and streams progress/results back to the user via **SignalR** (`/hubs/assistant`).
4. **OrderlyWeb In-App Chat Experience**:
   - Accessible anywhere via `ZoneTopBar` trigger, global hotkey (`Cmd/Ctrl + J`), `cmdk` command palette, or global FAB.
   - Slide-over responsive sheet (`<AiAssistantSheet />`) with role-based prompt discovery pills, real-time **Quota & Cost Transparency Widget**, **Interactive Tool Confirmation Cards**, and background task toast/drawer sync.

---

## 3. Out of scope

- Reusing or exposing `Orderly.DevMCP.Server` to end users (DevMCP is strictly a local dev harness with raw DB access).
- User-supplied API keys (BYOK) — the platform provides centralized inference and meters consumption.
- Direct database writes from the AI assistant — all mutations must hit downstream microservice endpoints with the user's JWT.
- Uncapped, open-ended pay-as-you-go billing without an explicit admin safety ceiling.

---

## 4. Tech decisions

| Decision | Choice | Reason |
| :--- | :--- | :--- |
| **Backend AI Framework** | Microsoft Semantic Kernel in .NET 10 | Native C#/.NET 10 integration, aligns with `BuildingBlocks`, NodaTime, and OpenTelemetry. |
| **Model Efficiency Strategy** | Tiered Routing + Role-Scoped Tool Pruning | Uses fast/cheap models for 90% of ops and strips unauthorized tool schemas before prompt assembly. |
| **Internal Tool Transport** | Direct HTTP/gRPC with `UserJwtPropagationHandler` | Avoids YARP loopback latency while enforcing downstream Carter RBAC and `ICurrentRestaurantProvider`. |
| **Mutation Execution (HITL)** | Stateless Signed `ActionProposal` (HMAC-SHA256) | Terminates LLM stream immediately on write intent; user confirmation executes deterministically with `0` extra LLM tokens. |
| **Concurrency Metering** | Redis Lua (`EVALSHA`) + `ZSET` Reservation Holds | Prevents race condition overages and self-heals leaked reservations via `ZREMRANGEBYSCORE`. |
| **Background AI Processing** | MassTransit Consumer + Redis Snapshots + SignalR | Keeps common state pre-computed in Redis and runs long analytics asynchronously without tying up HTTP threads. |
| **Audit & Invoicing Store** | PostgreSQL (`AiUsageLedgers`) | Append-only relational persistence for monthly billing audits and tenant reporting. |
| **UI Component Primitive** | Radix UI Sheet (`@/components/ui/sheet`) | Compliant with OrderlyWeb design system, supports keyboard traps, focus management, and smooth transitions. |

---

## 5. Folder layout

```text
orderly/
├── OrderlyMicroservices/
│   ├── orderly-microservices/
│   │   ├── BuildingBlocks/
│   │   │   ├── BuildingBlocks.Messaging/Events/       ← AiUsageRecordedIntegrationEvent.cs, AiBackgroundJobRequestedEvent.cs
│   │   │   └── BuildingBlocks/Http/                   ← UserJwtPropagationHandler.cs
│   │   ├── Services/
│   │   │   ├── Assistant/Assistant.API/               ← NEW: AI Assistant Microservice
│   │   │   │   ├── Extensions/                        ← SemanticKernelExtensions.cs, RoleToolFilter.cs
│   │   │   │   ├── Features/
│   │   │   │   │   ├── Chat/                          ← StreamChatEndpoint.cs, ExecuteProposalEndpoint.cs
│   │   │   │   │   ├── BackgroundJobs/                ← EnqueueAiJobEndpoint.cs, AiBackgroundWorker.cs, AssistantHub.cs
│   │   │   │   │   ├── Quota/                         ← GetQuotaEndpoint.cs, UpdateQuotaConfigEndpoint.cs
│   │   │   │   │   ├── Snapshots/                     ← RestaurantContextSnapshotService.cs, SnapshotConsumers.cs
│   │   │   │   │   └── Tools/                         ← ReadPlugins/ & ProposalWritePlugins/
│   │   │   │   ├── Infrastructure/
│   │   │   │   │   ├── Metering/                      ← RedisQuotaEngine.cs, LuaScripts/
│   │   │   │   │   ├── Security/                      ← ActionProposalSigner.cs
│   │   │   │   │   └── Persistence/                   ← AiDbContext.cs, Configurations/
│   │   │   │   ├── Program.cs
│   │   │   │   └── Assistant.API.csproj
│   │   └── ApiGateway/YarpApiGateway/
│   │       └── appsettings.json                       ← Route: /assistant-api/{**} -> assistant.api:8080
└── OrderlyWeb/
    └── src/
        ├── components/
        │   └── AiAssistant/                           ← NEW: AI Chat UI Components
        │       ├── AiAssistantHost.tsx                ← Mounted in RootLayout.tsx (Singleton + SignalR listener)
        │       ├── AiAssistantSheet.tsx               ← Radix Sheet slide-over panel
        │       ├── AiQuotaWidget.tsx                  ← Realtime quota telemetry & overage meter
        │       ├── AiDiscoveryView.tsx                ← Role-based prompt pill matrix
        │       ├── AiMessageList.tsx                  ← Auto-scrolling message stream & markdown
        │       ├── AiToolConfirmationCard.tsx         ← Stateless signed proposal confirmation card
        │       ├── AiBackgroundJobCard.tsx            ← Live progress card for detached background jobs
        │       ├── AiComposer.tsx                     ← Auto-expanding prompt input + context chips
        │       └── AiTriggerButton.tsx                ← TopBar button + global FAB
        ├── features/
        │   └── ai-assistant/
        │       ├── slices/aiAssistantSlice.ts         ← Redux UI state, stream buffer & background job state
        │       └── api/aiAssistantApi.ts              ← RTK Query slice (YARP /assistant-api/)
        └── locales/
            ├── en/ai.json                             ← English translations
            └── es/ai.json                             ← Spanish translations
```

---

## 6. Specification

### 6.1 Domain & Relational Persistence Specification

#### 6.1.1 `RestaurantAiQuotaConfig` Entity

Configures tenant allowances, overage safety ceilings, and status.

```csharp
namespace Assistant.API.Domain.Entities;

public sealed class RestaurantAiQuotaConfig : Entity, ITenantEntity, IAuditableEntity
{
    public Guid RestaurantId { get; set; }

    // Quotas (Effective Billable Tokens)
    public long IncludedTokensPerCycle { get; set; } = 250_000;
    public bool AllowOverage { get; set; } = false;
    public long MaxOverageTokens { get; set; } = 0;
    public decimal MaxOverageSpendUsd { get; set; } = 0.00m;

    // Auto-Purchase Packs
    public bool AutoPurchasePacksEnabled { get; set; } = false;
    public int PackSizeTokens { get; set; } = 100_000;
    public decimal PricePerPackUsd { get; set; } = 2.00m;

    // Suspension
    public bool IsSuspended { get; set; } = false;
    public string? SuspensionReason { get; set; }

    // Audit Metadata (NodaTime Instant)
    public Instant CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public Instant? LastModified { get; set; }
    public string? LastModifiedBy { get; set; }
}
```

#### 6.1.2 `AiUsageLedger` Entity

Append-only immutable record for every LLM invocation turn (foreground chat or background job).

```csharp
namespace Assistant.API.Domain.Entities;

public sealed class AiUsageLedger : Entity, ITenantEntity
{
    public Guid RestaurantId { get; set; }
    public Guid UserId { get; set; }
    public string ConversationId { get; set; } = string.Empty;
    public string OperationType { get; set; } = string.Empty;   // "FastChat", "DeepAnalyticsJob", "ProposalGeneration"

    public string Provider { get; set; } = string.Empty;        // "Gemini", "OpenAI"
    public string Model { get; set; } = string.Empty;           // "gemini-2.5-flash-lite", "gemini-2.5-pro"

    public int PromptTokens { get; set; }
    public int CompletionTokens { get; set; }
    public int CachedTokens { get; set; }
    public int EffectiveBillableTokens { get; set; }            // UncachedPrompt + Completion + (CachedTokens * 0.25)

    public decimal EstimatedCostUsd { get; set; }
    public string BillingCycle { get; set; } = string.Empty;    // "2026-11"
    public bool WasOverage { get; set; }

    public Instant RecordedAt { get; set; }
}
```

---

### 6.2 Self-Healing Redis Metering & Weighted Lua Specification

#### 6.2.1 Redis Key Layout

| Key Pattern | Data Structure | Purpose | TTL |
|---|---|---|---|
| `ai:quota:{restaurantId}:{yyyyMM}` | Hash | Monthly committed tokens: `used_uncached_prompt`, `used_cached_prompt`, `used_completion`, `used_effective` | 60 days |
| `ai:res_zset:{restaurantId}:{yyyyMM}` | ZSET | Active reservation IDs scored by `expiresAtUnixMs` for self-healing eviction | 60 days |
| `ai:res_amt:{restaurantId}:{yyyyMM}` | Hash | Maps `reservationId -> reservedTokenAmount` | 60 days |
| `ai:config:{restaurantId}` | Hash | Cached limits: `base_quota`, `allow_overage`, `overage_limit`, `is_suspended` | 1 hour |
| `ai:snapshot:{restaurantId}` | String (JSON) | Event-driven ~150-token live operational digest (86'd items, KDS lag, active tables) | 15 mins |

#### 6.2.2 Two-Phase Atomic Lua Scripts with Orphan Eviction

**Phase 1: Self-Healing Pre-Flight Check & Hold (`ai_reserve_quota.lua`)**

```lua
-- KEYS[1] = ai:quota:{restaurantId}:{yyyyMM}
-- KEYS[2] = ai:res_zset:{restaurantId}:{yyyyMM}
-- KEYS[3] = ai:res_amt:{restaurantId}:{yyyyMM}
-- KEYS[4] = ai:config:{restaurantId}
-- ARGV[1] = reservationId (Guid)
-- ARGV[2] = estimatedTokens
-- ARGV[3] = nowUnixMs
-- ARGV[4] = ttlMs (e.g. 120000 for 2 mins)
-- ARGV[5] = defaultBaseQuota (250000)

local quotaKey = KEYS[1]
local resZsetKey = KEYS[2]
local resAmtKey = KEYS[3]
local configKey = KEYS[4]

local resId = ARGV[1]
local estTokens = tonumber(ARGV[2])
local nowMs = tonumber(ARGV[3])
local ttlMs = tonumber(ARGV[4])
local defaultBase = tonumber(ARGV[5])

-- 1. Evict expired in-flight reservations (Self-Healing)
local expiredIds = redis.call("ZRANGEBYSCORE", resZsetKey, "-inf", nowMs)
if #expiredIds > 0 then
    redis.call("ZREMRANGEBYSCORE", resZsetKey, "-inf", nowMs)
    redis.call("HDEL", resAmtKey, unpack(expiredIds))
end

-- 2. Read tenant configuration
local baseQuota = defaultBase
local allowOverage = 0
local overageLimit = 0
local isSuspended = 0

if redis.call("EXISTS", configKey) == 1 then
    baseQuota = tonumber(redis.call("HGET", configKey, "base_quota") or defaultBase)
    allowOverage = tonumber(redis.call("HGET", configKey, "allow_overage") or 0)
    overageLimit = tonumber(redis.call("HGET", configKey, "overage_limit") or 0)
    isSuspended = tonumber(redis.call("HGET", configKey, "is_suspended") or 0)
end

if isSuspended == 1 then
    return { -1, "TENANT_AI_SUSPENDED", 0, 0 }
end

local maxAllowed = baseQuota
if allowOverage == 1 then
    maxAllowed = baseQuota + overageLimit
end

-- 3. Sum active non-expired reservations
local activeResAmounts = redis.call("HVALS", resAmtKey)
local totalReserved = 0
for i = 1, #activeResAmounts do
    totalReserved = totalReserved + tonumber(activeResAmounts[i])
end

local usedEffective = tonumber(redis.call("HGET", quotaKey, "used_effective") or 0)
local currentCommitted = usedEffective + totalReserved

if (currentCommitted + estTokens) > maxAllowed then
    if allowOverage == 0 and (currentCommitted + estTokens) > baseQuota then
        return { 0, "HARD_CAP_EXCEEDED", currentCommitted, baseQuota }
    else
        return { 0, "SAFETY_CEILING_EXCEEDED", currentCommitted, maxAllowed }
    end
end

-- 4. Record reservation hold with expiration timestamp
redis.call("ZADD", resZsetKey, nowMs + ttlMs, resId)
redis.call("HSET", resAmtKey, resId, estTokens)

if redis.call("TTL", quotaKey) < 0 then
    redis.call("EXPIRE", quotaKey, 5184000)
    redis.call("EXPIRE", resZsetKey, 5184000)
    redis.call("EXPIRE", resAmtKey, 5184000)
end

return { 1, "OK", currentCommitted + estTokens, maxAllowed }
```

**Phase 2: Weighted Post-Flight Settle (`ai_settle_quota.lua`)**

Charges cached prompt tokens at `0.25x` (integer ceiling) so tenants benefit from system-prompt and tool-schema caching.

```lua
-- KEYS[1] = ai:quota:{restaurantId}:{yyyyMM}
-- KEYS[2] = ai:res_zset:{restaurantId}:{yyyyMM}
-- KEYS[3] = ai:res_amt:{restaurantId}:{yyyyMM}
-- ARGV[1] = reservationId
-- ARGV[2] = uncachedPromptTokens
-- ARGV[3] = cachedPromptTokens
-- ARGV[4] = completionTokens

local quotaKey = KEYS[1]
local resZsetKey = KEYS[2]
local resAmtKey = KEYS[3]

local resId = ARGV[1]
local uncachedPrompt = tonumber(ARGV[2])
local cachedPrompt = tonumber(ARGV[3])
local completion = tonumber(ARGV[4])

-- 1. Release reservation hold
redis.call("ZREM", resZsetKey, resId)
redis.call("HDEL", resAmtKey, resId)

-- 2. Compute effective billable tokens (cached tokens weighted at 25%)
local discountedCached = math.ceil(cachedPrompt * 0.25)
local effectiveDelta = uncachedPrompt + discountedCached + completion

redis.call("HINCRBY", quotaKey, "used_uncached_prompt", uncachedPrompt)
redis.call("HINCRBY", quotaKey, "used_cached_prompt", cachedPrompt)
redis.call("HINCRBY", quotaKey, "used_completion", completion)
local newEffective = redis.call("HINCRBY", quotaKey, "used_effective", effectiveDelta)

return { newEffective, effectiveDelta }
```

---

### 6.3 LLM Efficiency, Tool Architecture & Background Execution Specification

#### 6.3.1 Role-Scoped Dynamic Tool Filtering (`RoleToolFilter.cs`)

Before building the Semantic Kernel `PromptExecutionSettings`, `Assistant.API` inspects the caller's JWT role claims and injects **only** the plugins permitted for that role:

| Caller Role | Injected Read Plugins | Injected Proposal Write Plugins | Excluded Plugins (Zero Schema Tokens Sent) |
|---|---|---|---|
| `KitchenStaff` / `KitchenManager` | `KitchenTicketsReadPlugin`, `Catalog86ReadPlugin` | `ToggleItem86ProposalPlugin`, `BumpTicketProposalPlugin` | `OrderingWritePlugin`, `IdentityStaffPlugin`, `AnalyticsPlugin`, `QuotaAdminPlugin` |
| `Waiter` / `Cashier` / `Host` | `TableStatusReadPlugin`, `CatalogMenuReadPlugin`, `OrderStatusReadPlugin` | `CreateOrderProposalPlugin`, `ApplyDiscountProposalPlugin` | `IdentityStaffPlugin`, `QuotaAdminPlugin`, `MarginAnalyticsPlugin` |
| `Manager` / `RestaurantAdmin` | All Read Plugins + `MarginAnalyticsPlugin` | All Proposal Write Plugins + `StaffRoleProposalPlugin`, `QuotaAdminProposalPlugin` | None |

#### 6.3.2 Direct Internal Tool Calls & Stateless Write Proposals

1. **Read Tools (Auto-Executed via Direct Internal HTTP)**:
   - Execute inside the Semantic Kernel turn against internal service URLs (`http://catalog.api:8080`, `http://kitchen.api:8080`, `http://ordering.api:8080`) using `UserJwtPropagationHandler` (propagating `Authorization: Bearer <jwt>` and `X-Agent-Invocation: true`).
   - Do **not** loop back through `YarpApiGateway`.
2. **Write Tools (Stateless Proposal-Only — Zero Stream Blocking)**:
   - Write functions (`create_order`, `toggle_item_86`, `update_staff_role`) validate arguments and construct an `ActionProposalDto` signed with an HMAC-SHA256 server secret (`ActionProposalSigner.cs`) bound to `(RestaurantId, UserId, ActionName, ParametersJson, ExpiresAt)`.
   - The LLM stream immediately emits an SSE `event: action_proposal` frame and terminates the turn, settling Redis quota.
   - When the user clicks **Confirm & Execute** in `OrderlyWeb`, the frontend calls `POST /assistant-api/v1/actions/execute` with the signed proposal (or invokes the target RTK Query mutation directly). `Assistant.API` verifies the HMAC signature and expiry, then dispatches the HTTP mutation using the user's JWT—**consuming 0 additional LLM tokens**.

#### 6.3.3 Event-Driven Live Context Snapshots (`ai:snapshot:{restaurantId}`)

To avoid calling 3–4 read tools for routine questions (*"What's 86'd right now and how backed up is the kitchen?"*):

- `Assistant.API` subscribes to existing MassTransit events (`OrderSubmittedIntegrationEvent`, `KitchenTicketUpdatedEvent`, `CatalogItemAvailabilityChangedEvent`) and updates a compact JSON digest in Redis (`ai:snapshot:{restaurantId}`, ~150 tokens).
- Every chat turn injects this compact snapshot into the cached system prompt header, allowing the model to answer high-frequency operational questions in **a single turn with zero tool round-trips**.

#### 6.3.4 Tiered Model Routing & Detached Background Worker

1. **Tiered Model Router**:
   - **Tier 1 (Fast Interactive — Default)**: `gemini-2.5-flash-lite` / `gpt-4o-mini` for status lookups, snapshot queries, and generating `ActionProposal` cards.
   - **Tier 2 (Deep Analytical / Background)**: `gemini-2.5-pro` / `gpt-4o` for multi-day margin audits, shift anomaly detection, and menu engineering reports.
2. **Detached Background Execution (`AiBackgroundWorker` + SignalR)**:
   - When a manager requests a heavy analytical report, `Assistant.API` reserves quota, publishes an `AiBackgroundJobRequestedEvent` (or enqueues to a bounded `Channel<AiBackgroundJob>` with `SemaphoreSlim` max concurrency per tenant = 2), and immediately returns a `202 Accepted` `jobId` to the chat UI.
   - The background worker executes the multi-step query, settles the Redis reservation upon completion, persists the result in PostgreSQL, and broadcasts `AiJobProgress` and `AiJobCompleted` events via SignalR (`/hubs/assistant`) to the user's active session.

#### 6.3.5 Hard Cap Error Response (RFC 7807)

```json
{
  "type": "https://orderly.kalaa.dev/errors/ai-quota-hard-cap-exceeded",
  "title": "Monthly AI Quota Exceeded",
  "status": 429,
  "detail": "Restaurant tenant has consumed all 250,000 included AI tokens for billing cycle 2026-11.",
  "extensions": {
    "errorCode": "AI_HARD_CAP_EXCEEDED",
    "restaurantId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
    "billingCycle": "2026-11",
    "usedTokens": 250120,
    "limitTokens": 250000,
    "overageAllowed": false
  }
}
```

---

### 6.4 OrderlyWeb UI/UX Architecture Specification

#### 6.4.1 Layout, Sheet Host & Persistent Triggers

- **Singleton Host (`AiAssistantHost.tsx`)**: Mounted at the root of the app in `RootLayout.tsx` alongside `<Toaster />` and `<SignInDialogHost />`. Retains active messages, streaming state, SignalR background job subscriptions, and pending confirmations across route transitions.
- **Trigger Hierarchy**:
  1. `ZoneTopBar` Header Button: Machined 36×36px button (`ds-header__icon-btn`) with active LED stream/background-job status.
  2. Global Hotkey: `Cmd + J` (macOS) / `Ctrl + J` (Windows) toggles the drawer.
  3. `cmdk` Palette: Typing query in `Cmd + K` suggests *"Ask AI: '...' (↵)"*.
  4. Floating Action Button (FAB): Fixed at `bottom-6 right-6 z-40`, tactile 48×48px brushed steel button.
- **Slide-over Sheet (`AiAssistantSheet.tsx`)**: Built on Radix UI Sheet. Responsive width (`sm:max-w-md md:max-w-xl lg:max-w-[540px]`), expandable to `xl:max-w-[800px]` for side-by-side data tables and recipes.

#### 6.4.2 Quota & Cost Transparency Widget (`AiQuotaWidget.tsx`)

Positioned at the top of the chat sheet:

- **Normal Tier (<80%)**: Teal progress indicator (`bg-service-teal`), telemetry monospace: `136k / 250k tokens (54%) • Resets in 11 days`.
- **Warning Tier (80%–99%)**: Amber progress indicator (`bg-service-amber`), alert banner: *"Approaching monthly included limit (86% used)."*
- **Capped Tier (100% Limit Reached)**: Red indicator (`bg-danger`), banner: *"Monthly token limit reached. AI generation paused."* Composer input is disabled; CTA displays *"Enable Safety Ceiling to Continue"*.
- **Overage Active Tier (Opt-in Buffer Enabled)**: Tangerine indicator (`bg-service-tangerine`), telemetry: `formatCurrency(spent, currency, locale)` + `formatCurrency(ceiling, currency, locale)` (e.g., `"$4.20 / $20.00 safety ceiling used"`).

#### 6.4.3 Role-Based Quick Discovery Matrix (`AiDiscoveryView.tsx`)

Empty state renders greeting in `MuseoModerno` font and categorizes action pills (`rounded-full`, 12% service tint, hover tactile lift):

- **Kitchen Staff / Chef**:
  - *"Show all 86'd items and low stock"* (Snapshot / Read query)
  - *"Summarize tickets delayed > 15 mins"* (Kitchen KDS query)
  - *"Toggle 86 status on Ribeye Steak"* (Signed Proposal card)
- **Waiters / Floor Staff**:
  - *"Create dine-in order for Table 4"* (Signed Proposal card)
  - *"Check Table 8 order status & fire entrees"* (Kitchen pass trigger)
  - *"Find available 4-top tables for walk-ins"* (Table query)
- **Managers / Admins**:
  - *"What are today's top 5 gross margin sellers?"* (Background analytics job)
  - *"Audit manager price overrides from evening shift"* (Background audit job)
  - *"Adjust AI Assistant monthly safety ceiling to $50"* (Settings proposal card)

#### 6.4.4 Interactive Tool Confirmation Cards (`AiToolConfirmationCard.tsx`)

Mutations render an interactive 8px radius card backed by a signed `ActionProposal` payload:

- **Telemetry Parameter Grid**: Monospace parameters (`tableNumber: 4`, `action: "Toggle 86"`, `expiresIn: "5m"`).
- **Operational Impact Notice**: Highlighting affected kitchen stations.
- **Action Buttons**: `Dismiss` (Ghost button) vs `Confirm & Execute` (Gunmetal primary or High-Heat Induction Flame accent for critical actions). Clicking `Confirm & Execute` hits `POST /assistant-api/v1/actions/execute` directly without calling the LLM.
- **Receipt State**: Transitions into a confirmed card with an emerald checkmark (`#059669`) and timestamp upon execution.

---

## 7. Cross-Repository Communication / Integration Points

```text
[OrderlyWeb (React 19)]
        │  1. SSE Chat Stream / Proposal Execute / SignalR (/hubs/assistant)
        ▼
[YARP Gateway :6004] ─── Path: /assistant-api/{**}
        │
        ▼
[Assistant.API :8080] (.NET 10)
        │
        ├──> Redis Lua (Evict Expired ZSET Holds + Reserve Tokens + Read Live Snapshot)
        │
        ├──> RoleToolFilter (Prune unauthorized plugins by JWT role)
        │
        ├──> Call Tiered LLM (Flash-Lite for Ops / Pro for Background Analytics)
        │       │
        │       ├──> Read Tool Call ──> Direct Internal HTTP (http://catalog.api:8080)
        │       │                       via UserJwtPropagationHandler (No YARP loopback)
        │       │
        │       └──> Write Tool Call ─> Emit HMAC-Signed ActionProposal & Close LLM Turn
        │
        ├──> Redis Lua (Release ZSET Hold + Settle Weighted Tokens [Cached @ 0.25x])
        │
        └──> Outbox Publisher: AiUsageRecordedIntegrationEvent
                │
                ▼ (MassTransit + RabbitMQ)
             [AiUsageLedgerConsumer] ──> PostgreSQL (AiUsageLedgers)
```

---

## 8. Implementation Phases

### Phase 1: Domain Entities, PostgreSQL Persistence & Outbox Events

- Define `RestaurantAiQuotaConfig` and `AiUsageLedger` (including `CachedTokens` and `EffectiveBillableTokens`) in `Assistant.API` with NodaTime `Instant`.
- Implement EF Core configurations, migrations, and PostgreSQL indexes (`RestaurantId`, `BillingCycle`, `RecordedAt`).
- Define `AiUsageRecordedIntegrationEvent` and `AiBackgroundJobRequestedEvent` in `BuildingBlocks.Messaging` using Transactional Outbox.
- **Verification**: `dotnet ef migrations add`, database migration test, and unit tests asserting entity invariants.

### Phase 2: Redis ZSET Self-Healing Metering Engine & Weighted Lua Reservations

- Create `ai_reserve_quota.lua` (with `ZREMRANGEBYSCORE` orphan eviction) and `ai_settle_quota.lua` (with `0.25x` cached token weighting) in `Assistant.API`.
- Implement `IAiQuotaEngine` with `ReserveQuotaAsync()` and `SettleQuotaAsync()`.
- Implement sliding-window rate limiters for requests per minute (RPM) and tokens per minute (TPM).
- **Verification**: Concurrency xUnit tests firing 50 simultaneous simulated streaming threads + simulated mid-stream crashes against a Redis Testcontainer, verifying zero quota overruns and automatic reclamation of expired holds.

### Phase 3: AI Assistant Orchestrator (.NET 10 / Semantic Kernel), Tiered Routing & Read/Proposal Tools

- Scaffold `Assistant.API` service, register Semantic Kernel with tiered model routing (`flash-lite` vs `pro`).
- Implement `UserJwtPropagationHandler` in `BuildingBlocks` to forward caller JWTs directly to internal microservice URLs (`http://catalog.api:8080`, `http://ordering.api:8080`, `http://kitchen.api:8080`, `http://identity.api:8080`).
- Implement `RoleToolFilter` so only role-authorized plugins are injected into the prompt.
- Implement Read Plugins (auto-executing) and Proposal Write Plugins (`ActionProposalSigner` with HMAC-SHA256).
- Configure max-turn safety loops (hard cap of 3 read tool iterations per turn).
- **Verification**: Integration tests verifying that `KitchenStaff` prompts never receive Admin tool schemas, read calls enforce downstream RBAC, and write intents emit signed `ActionProposal` payloads without executing mutations prematurely.

### Phase 4: Event-Driven Context Snapshots & Detached Background AI Workers (MassTransit + SignalR)

- Implement `RestaurantContextSnapshotService` and MassTransit consumers to maintain `ai:snapshot:{restaurantId}` in Redis.
- Implement `AiBackgroundWorker` and SignalR `AssistantHub` (`/hubs/assistant`) with per-tenant `SemaphoreSlim` concurrency bounds.
- **Verification**: Integration tests verifying that catalog/kitchen events update the Redis snapshot and long-running analytical jobs publish completion payloads over SignalR while settling Redis quota accurately.

### Phase 5: Carter Minimal APIs, YARP Ingress Routing & Deterministic Action Execution

- Expose Carter endpoints in `Assistant.API`:
  - `POST /assistant-api/v1/chat/stream`: Server-Sent Events (SSE) streaming chat completions and `action_proposal` events.
  - `POST /assistant-api/v1/actions/execute`: Verifies HMAC-signed `ActionProposal` and executes the downstream mutation using the caller's JWT (`0` LLM tokens used).
  - `POST /assistant-api/v1/jobs`: Enqueues detached background analytical jobs (`202 Accepted`).
  - `GET /assistant-api/v1/restaurants/{id}/quota`: Returns current cycle consumption, status (`normal`, `warning`, `capped`, `overage`), and reset dates.
  - `PUT /assistant-api/v1/restaurants/{id}/quota/config`: Allows `RestaurantAdmin` to configure opt-in safety ceiling and overage thresholds.
- Update `YarpApiGateway` route definitions to route `/assistant-api/{**}` (including SignalR `/hubs/assistant`) to the `assistant.api` cluster.
- **Verification**: End-to-end HTTP tests verifying RFC 7807 `ProblemDetails` on quota exhaustion (`AI_HARD_CAP_EXCEEDED`) and tamper-proof signature validation on `/actions/execute`.

### Phase 6: OrderlyWeb UI Host, Responsive Sheet, Redux/RTK Query & SignalR Background Job Sync

- Create Redux slice `aiAssistantSlice.ts` managing sheet state (`isOpen`, `isExpanded`), active session, message history, and background job progress.
- Implement RTK Query API slice `aiAssistantApi.ts` and SignalR client connection to `/assistant-api/hubs/assistant`.
- Scaffold `<AiAssistantHost />` in `RootLayout.tsx` and integrate keyboard shortcut listener (`Cmd/Ctrl + J`).
- Add triggers to `ZoneTopBar` (`Header.tsx`), `cmdk` palette, and global FAB.
- Create full localized string dictionaries in `OrderlyWeb/src/locales/en/ai.json` and `locales/es/ai.json`.
- **Verification**: Vitest component tests verifying sheet visibility, keyboard shortcut trigger, SignalR job toast/card updates, and i18n rendering across English and Spanish.

### Phase 7: Quota Transparency Widget, Discovery Matrix, Tool Confirmation Cards & E2E Verification

- Build `<AiQuotaWidget />` supporting normal, warning, capped, and overage visual states.
- Build `<AiDiscoveryView />` rendering role-tailored prompt pills based on current user claims (`KitchenStaff`, `Waiter`, `Manager`).
- Build `<AiToolConfirmationCard />` and `<AiBackgroundJobCard />` with monospace parameter telemetry, operational impact notices, and deterministic confirmation CTAs.
- Execute cross-service E2E tests and Playwright flows (`e2e/ai-assistant-flow.spec.ts`) covering proposal confirmation, hard-cap pause, safety-ceiling overage resume, and multi-tenant boundary enforcement.
- Run `phase-guard.ps1` quality gate across the solution.

---

## 9. Verification & Testing Strategy

1. **Unit & Concurrency Tests**:
   - `RedisQuotaEngineTests`: Test atomic Lua reservation under 50 concurrent threads, cached-token `0.25x` weighting, and automatic `ZREMRANGEBYSCORE` reclamation of simulated crashed streams.
   - `ActionProposalSignerTests`: Assert that tampered parameters, expired timestamps, or cross-user replay attempts fail HMAC verification with `403 Forbidden`.
   - `RoleToolFilterTests`: Assert that unauthorized tool schemas are excluded from LLM prompt construction.
2. **Component & A11y Tests (OrderlyWeb)**:
   - Vitest unit tests for `<AiQuotaWidget />`, `<AiToolConfirmationCard />`, and `<AiBackgroundJobCard />`.
   - `jest-axe` tests asserting zero color-contrast, aria-label, or keyboard trap violations.
3. **E2E Playwright Flows**:
   - `e2e/ai-assistant-flow.spec.ts`: Test chat drawer launch via `Cmd+J`, role discovery pill execution, zero-token proposal confirmation execution, background job notification, and quota meter progression in both `en` and `es` locales.
