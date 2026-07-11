# Discount.Grpc — Service Plan

> **Scope:** completion plan for the existing `Discount.Grpc` microservice (port 6002 / 6062). Closes the gaps between `docs/architecture/architecture.md`, `docs/architecture/db_relational_model.mermaid`, `docs/architecture/current-architecture.md`, and the code in `orderly-microservices/Services/Discount/Discount.Grpc/`. This is an *evolution* plan, not a green-field design — one gRPC service with five RPCs and one SQLite-backed entity exists today; the work is bringing Coupon to production-grade, adding `RewardCode` and `DiscountRule` aggregates with full gRPC CRUD, wiring multi-tenancy + JWT auth + transactional outbox + an expiry-sweep hosted service, and routing entity-history events through the bus so the **Catalog service** (its own plan) writes a Marten audit document. The work does NOT touch the four-entity split architecture.md describes verbatim; the Coupon entity collapses what architecture §3 calls `Discounts` + `PromoCodes`, justified inline.
>
> **Locked design decisions (recorded on plan date):**
>
> | # | Question | Choice | Rationale |
> |---|---|---|---|
> | 1 | Plan scope | **D** — Coupon production-grade + RewardCode + DiscountRule full CRUD + infrastructure scaffold (no separate `Discount`/`PromoCode` aggregate split) | Architecture is ambiguous; Coupon already collapses `Discount`+`PromoCode` fields. The four-aggregate scope would re-shape the kernel for no caller. |
> | 2 | Persistence | **SQLite stays.** No Postgres migration. | Discount's read-mostly load fits SQLite; switching is unforced. |
> | 3 | History architecture | **A** — Bus-mediated. Discount publishes `DiscountHistoryAppendedIntegrationEvent`; the Catalog service (own plan) consumes and writes a Marten `EntityHistoryArchive` document. History scope = Discount-only entities (Coupon, RewardCode, DiscountRule). | Honors Catalog §4's "no cross-service writes" rule. Marten stays the natural archive. |
> | 4 | Protocol surface | **A** — gRPC-only CRUD. 12 new gRPC RPCs (Create/Get/List/Update/Delete + redeem/evaluate across the two new entities). No Carter REST added. | No HTTP caller exists in the codebase today; speculative REST is dead code. |
> | 5 | `DiscountRule` shape | **A** — Separate aggregate, FK from Coupon, `RuleDataJson` for conditions. One rule per coupon. | Matches architecture §3; rules stay indexable in SQL; the JSON column keeps rule kinds flexible. |
> | 6 | `RewardCode` generation | **A** — Hardcoded 4★/5★ rule in `FeedbackSubmittedConsumer`. No `RewardRule` aggregate. | Architecture describes behavior, not a rule table. Defer the aggregate until per-restaurant tuning arrives. |
> | 7 | Async lifecycle | **B + C** — `IHostedService` sweep (`DiscountExpirySweepService`, default 5 min) + lazy-evaluation gate at every read. Two-condition semantic: `IsActive && (ExpirationDate IS NULL OR ExpirationDate >= UtcNow)`. | Two complementary guarantees: storage hygiene (queries see clean rows) + read correctness (no race window between sweep ticks). Total cost: ~90 LOC + ~6 tests. Hangfire rejected (no business need, SQLite flakiness, dashboard needs HTTP surface we explicitly rejected in (4)). |
> | 8 | Outbox wiring | **A** — Mirror Catalog. Implement first `OutboxDispatcher<TContext>` for SQLite (single-replica, `claim_id` GUID column for atomic claim). Use the existing `BuildingBlocks.Messaging.Outbox.IOutboxDbContext` / `IOutboxPublisher`. | Q3's history decision leans on "transactional publish" semantics. Plain MassTransit publish (option B) drops the guarantee; MassTransit's built-in `EntityFrameworkOutbox` (option C) diverges from the solution-wide pattern. |
> | 9 | Tests strategy | **C** — xUnit + FluentAssertions + NSubstitute (unit); SQLite `:memory:` for integration tests; `MassTransit.InMemoryTestHarness` for consumer tests. | Catalog pattern uses Testcontainers Postgres; Discount is on SQLite. SQLite-in-memory is production-equivalent engine, container-free, fast. The hand-rolled pieces (claim SQL, lazy gate, sweep) need real engine tests, not mocks. |
> | 10 | Auth | `AddJwtBearer` + ASP.NET Core gRPC integration (mirrors Catalog). **Pattern 2** for bus-triggered consumers: synthetic `ClaimsPrincipal` carrying `restaurantId` claim extracted from the event payload + actor=`discount-service`. New permissions: `coupon:read/create/edit/delete/redeem`, `reward:read/create/edit/delete/redeem`, `discount-rule:read/edit` (Identity-side follow-up). | Mirrors every other API. Synthetic claims beat `AllowAnonymous` on consumers — the audit trail records the actual restaurant context, not a generic service actor. |
> | 11 | Multi-tenancy | **A** — Activate `BuildingBlocks.Multitenancy.ITenantEntity` + apply the global query filter. Fix `ITenantEntity.RestaurantId : int → Guid` (pre-existing drift; Catalog entities use Guid). JWT claims feed `ICurrentRestaurantProvider` at RPC entry; synthetic claims from (10) feed it at consumer entry. | BuildingBlocks primitive is dormant; Discount becomes the first adopter. Global filter blocks the "easy-to-leak" footgun in B; C doubles the surfaces. |
> | 12 | Cache | **A** — None. SQLite indexed reads on `(RestaurantId, Code)` are sub-ms; the Basket checkout hot path never bottlenecks the DB. | A cache layer at this scale is cost > benefit. |
> | 13 | Event phasing | Phase 1 publishes `DiscountHistoryAppendedIntegrationEvent` only. The three architecture-named publishes (`DiscountApplied`, `RewardGenerated`, `RewardRedeemed`) have no listed consumer today → **deferred** until one exists. Phase 1 consumes `MenuItemChangedIntegrationEvent` and `RestaurantConfigurationChangedIntegrationEvent` (Catalog ships these today). `FeedbackSubmitted` + `OrderCreated` consumes are stub-and-flag — wire to the bus with `MassTransit` filter `Where(...).Disable()` until Notification v1 / Ordering plans ship. | Don't pollute the bus with no-consumer publishes; don't gate Phase 1 on services that don't exist. |
>
> **Out-of-plan dependencies** (other service plans):
> 1. **Catalog plan** will need to grow: a Marten `EntityHistoryArchive` document + a consumer for `DiscountHistoryAppendedIntegrationEvent`. Documented as a contract, implemented in Catalog's plan.
> 2. **Identity.API** plan will need to add the eleven new permission strings listed in (10). Tracked as a dependency note; not implemented in this plan.
> 3. **Notification.API v1** plan will publish `FeedbackSubmittedIntegrationEvent`. Discount ships a stub consumer (Phase 5) wired but disabled, ready to flip when Notification v1 lands.
> 4. **Ordering.API** plan will publish `OrderCreatedIntegrationEvent` for auto-apply discounts. Tracked as a dependency note; no Discount work in this plan.
>
> **In-plan entity moves:** none. Discount already owns Coupon (Catalog plan §7.6.2 was a no-op). The `RewardCode` and `DiscountRule` aggregates are net-new.

---

## 0. Skill & documentation conventions

These two conventions apply to **every phase** below. They are non-negotiable — no implementation commit for this plan should land without satisfying both.

### 0.1 Skill mandate — `csharp-developer`

> **All implementation work on this plan MUST invoke the `csharp-developer` skill** (base directory `.claude/skills/csharp-developer`, invoked as `/csharp-developer` in Claude Code).
>
> The skill is the source of truth for C# 12+ / .NET 10 idiom, async patterns, EF Core / Marten usage, ASP.NET Core + Carter, gRPC + protobuf, MassTransit handlers, xUnit + InMemoryTestHarness test scaffolding, and the project's "MUST DO / MUST NOT DO" guard rails (nullable enabled, primary constructors, async/await with `CancellationToken`, `Result<T>` for error paths, no blocking calls).
>
> At the start of **every phase**, the implementer (human or AI agent) loads the skill. Companion reference files under `.claude/skills/csharp-developer/references/` are loaded on demand per the skill's table:
> - `modern-csharp.md` — records, primary constructors, collection expressions, pattern matching, nullable types.
> - `aspnet-core.md` — gRPC + Carter endpoints, DI, middleware, routing (gRPC is covered here as one of the supported endpoints).
> - `entity-framework.md` — EF Core configuration, migrations, interceptors; SQLite-specific concurrency notes.
> - `performance.md` — `Span<T>`/`Memory<T>`, async, AOT; loaded only if a phase lands a perf-sensitive hot path.
>
> **EF Core checkpoint:** after any code change that mutates the schema (Phase 1 outbox tables + tenant interface; Phase 2 `DiscountRule` + apply query filter; Phase 3 `RewardCode`), the implementer runs `dotnet ef migrations add <Name>` from `Services/Discount/Discount.Grpc/`, reviews the generated migration file for unintended drops, and rolls back with `dotnet ef migrations remove` if the diff is wrong. SQLite migrations are textual — review for missing indexes (`IX_OutboxMessages_DispatchedAt_OccurredOn` is critical for the dispatcher).
>
> The skill is *additional* to whatever other skills are relevant (e.g. `csharp-xunit` for test scaffolding; `dotnet-best-practices` for the project-wide guard rails). It is **not** a substitute for the plan; the plan wins where they disagree.

### 0.2 Phase-completion documentation update

> **After completing every phase (1–6), `docs/architecture/current-architecture.md` MUST be updated to reflect the new state of the codebase before the implementation commit is finalized.**
>
> `current-architecture.md` is described in §1 ("Tech stack — Discount is gRPC-only, SQLite, no auth") as the snapshot view of the codebase. It must never describe Discount with capabilities that don't exist yet, and it must never lag a shipped phase.
>
> The implementer writes the doc update as part of the phase, not as a follow-up commit. Each phase below lists its **Doc-update scope** — the §-numbered sections of `current-architecture.md` that phase touches.
>
> | Doc section | Why it usually changes per phase |
> |---|---|
> | §2 Tech Stack | Outbox row gains the SQLite-flavored flavor; JWT validation row gains Discount entry. |
> | §4.4 Discount Service | New entities (RewardCode, DiscountRule), new endpoints (gRPC RPCs), auth, tenancy, cache removal confirmation, health-check split (`/live`+`/ready`). |
> | §5.1 Synchronous | Table row "Basket.API → Discount.Grpc" stays; add rows for any new internal caller (none today). |
> | §5.2 Asynchronous | Add `DiscountHistoryAppendedIntegrationEvent` publish row; add `MenuItemChangedIntegrationEvent` consume row; add `RestaurantConfigurationChangedIntegrationEvent` consume row; **deferred** rows for the three architecture publishes + `FeedbackSubmitted`/`OrderCreated` consumes. |
> | §6 Data Stores | SQLite `discountdb` gains `outbox_messages`, `outbox_messages_dead`, `coupons`, `reward_codes`, `discount_rules`, `_ef_migrations` rows. |
> | §9 Cross-Cutting Patterns | Add row for first SQLite outbox claim SQL, JWT auth pattern, `ITenantEntity` adoption on Discount entities. |
> | §12 Observability | `/live` + `/ready` split for Discount. |
>
> The phase's checklist entry (see §9) requires the doc commit before the phase is marked complete.

### 0.3 Code-quality guard rails (dotnet-best-practices)

Same role as `CATALOG_SERVICE_PLAN.md §0.3`. Discount's project-specific overrides:

- **xUnit + FluentAssertions + NSubstitute** for unit tests (Catalog's choice).
- **SQLite `:memory:` + MassTransit `InMemoryTestHarness`** for integration tests (Catalog uses Testcontainers Postgres; SQLite doesn't need a container).
- **gRPC error codes via `Grpc.Core.Status` + `ServerCallContext.Status`** — never silent `catch` in RPC handlers; map known exceptions to `StatusCode.NotFound` / `StatusCode.InvalidArgument` / `StatusCode.PermissionDenied` / `StatusCode.FailedPrecondition`. No string-typed error tuples.
- **`ArgumentNullException.ThrowIfNull`** on every constructor parameter of every Discount service.
- **JWT bearer validation** in `Program.cs` (`AddJwtBearer`); per-RPC permission checks via `[Authorize(Policy = "coupon:create")]`-style attributes on the gRPC service methods (the `Microsoft.AspNetCore.Authorization.AuthorizationPolicy` works through gRPC's `IAsyncStreamReader`-adjacent pipeline).

For the project-wide guard rails (XML comments, async/await, DI lifetimes, resource management, configuration, error handling, logging, security, testing), mirror `CATALOG_SERVICE_PLAN.md §0.3` verbatim — Discount shares the same BuildingBlocks and the same conventions.

#### 0.3.1 Global usings (project-specific)

`Discount.Grpc/GlobalUsings.cs` will, after Phase 1, host:

```csharp
// BuildingBlocks
global using BuildingBlocks.Entities.Contracts;
global using BuildingBlocks.Messaging.Outbox;
global using BuildingBlocks.Multitenancy;

// Microsoft
global using Microsoft.EntityFrameworkCore;
global using Microsoft.Extensions.Logging;
global using Microsoft.Extensions.Options;

// Project-local
global using Discount.Grpc.Data;
global using Discount.Grpc.Models;
global using Discount.Grpc.Services;
global using Discount.Grpc.Exceptions;
global using Discount.Grpc.Messaging.Events;
global using Discount.Grpc.Authorization;  // for ICurrentRestaurantProvider + synthetic claims
```

The "2+ files" promotion rule from CATALOG_SERVICE_PLAN §0.3.12 applies. `Discount` is small enough that single-use namespaces will stay file-scoped until Phase 6 cleanup.

#### 0.3.2 SQLite-specific guard rails

- **Time conversion** — `Instant ↔ long` via `InstantToLongConverter` (already in `Data/DiscountContext.cs:57-67`). Verify every new entity uses `Instant` for timestamps via this converter; never `DateTime`.
- **Migrations on SQLite** — every migration must include an explicit `migrationBuilder.Sql("CREATE INDEX ...")` when filter predicates need them, since EF Core's `HasIndex` translates but lacks some SQLite-specific tuning.
- **Connection lifetime** — register `DiscountContext` as `Scoped` with `AddDbContext<DiscountContext>(...)`. SQLite's `Data Source=discountdb` is a file; the lock is serialized across processes via file lock. Single-replica deployment assumption carries forward into the outbox claim SQL design (Phase 1).
- **Money** — `decimal` for `Amount`. SQLite stores it as TEXT per IEEE 754; EF Core's `HasConversion<string>()` is unnecessary on .NET 8+ (decimal maps to TEXT automatically and round-trips losslessly). Verify on first run.

### 0.4 gRPC + MassTransit design principles

This section enforces the gRPC + MassTransit conventions every Discount endpoint follows. It is the protocol-shape counterpart to §0.1 / §0.2 / §0.3.

#### 0.4.1 RPC design

- **RPCs are actions, not resources.** gRPC's natural shape is method-named (`CreateDiscount`, `RedeemDiscount`), not URL-shaped. Resource-oriented path conventions from CATALOG_SERVICE_PLAN §0.4.1 only apply in the rare case Discount surfaces HTTP (it doesn't, per Q4).
- **One `Service` per aggregate.** `DiscountProtoService` for Coupon (existing), `RewardCodeProtoService` for RewardCode (Phase 3), `DiscountRuleProtoService` for DiscountRule (Phase 2). Each lives in its own `.proto` file or its own `package` block within `Protos/discount.proto`. Generated clients mirror; Basket already imports the protos.
- **Per-RPC request / response messages, not reuse.** Discount uses `CouponModel` (existing) for the Coupon CRUD RPCs; new RewardCode RPCs use `RewardCodeModel`; new DiscountRule RPCs use `DiscountRuleModel`. **Don't** reuse `CouponModel` to encode a `RewardCode` — the field shapes differ; reusing creates protobuf tags that drift.
- **Validation in handlers, not at the proto layer.** Field-level constraints (`Required`, `Range`, etc.) live in FluentValidation validators + the handler, not in protobuf field annotations (proto3 has limited `optional` semantics). The exception is `string restaurant_id`, which gets pattern-checked at handler entry via `Guid.TryParse`.
- **`Idempotency-Key` for state transitions.** `RedeemDiscount`, `RedeemRewardCode`, and `UpdateDiscount` accept an `Idempotency-Key` request header (UUID v4) — middleware reads it, hashes with `restaurantId + code`, and caches the response in Redis (`idempotency:{rId}:{sha256(key+rId+code)}`, 24h TTL). **Conflict** (same key, different request body) → 422 via `StatusCode.FailedPrecondition` with details. Redis is the same shared instance Basket and Catalog use — **discount:* namespace** to avoid collision.

#### 0.4.2 gRPC error → StatusCode mapping

Catalog §0.4.7 (ProblemDetails, RFC 7807) doesn't map cleanly to gRPC. Discount's protocol-native shape is `StatusCode` + `ServerCallContext.Status` with `Metadata` for richer detail. The exception-to-code map:

| Exception | `StatusCode` | `Metadata` keys | Notes |
|---|---|---|---|
| `DomainException` / `NotFoundException` | `NotFound` | `resource-id` (binary) | Entity not found / soft-deleted. |
| `ValidationException` (FluentValidation) | `InvalidArgument` | per-violation field names | One detail string per failed rule. |
| `BusinessRuleException` (e.g. "redeeming expired coupon") | `FailedPrecondition` | `rule-id`, `rule-version` | Business invariant violated. |
| `CrossTenantAccessException` | `PermissionDenied` | `tenant-id`, `attempted-tenant-id` | Tenant query filter triggers this on cross-tenant reads/writes. |
| `ConcurrentRedemptionException` (race lost) | `Aborted` | `conflicting-request-id` | Conditional UPDATE failed; the rpc retries once. |
| `UnhandledException` | `Internal` | `trace-id` | Generic; full detail in logs only. |

`CustomExceptionHandler` from BuildingBlocks doesn't apply directly to gRPC; Discount uses an `ExceptionInterceptor` registered on the gRPC service collection that maps the same `DomainException` hierarchy → `StatusCode`.

#### 0.4.3 Proto file layout

`Protos/discount.proto` currently houses `CouponModel` + 5 RPCs in `package discount`. After Phase 2 / Phase 3, the file grows. Recommendations:

- Split into `Protos/coupon.proto`, `Protos/reward_code.proto`, `Protos/discount_rule.proto` and re-import in a single `.proto` aggregator. This keeps the generated C# out of one giant namespace.
- Generated proto stubs land in `obj/Debug/net10.0/Protos/`. **Never edit these.** They regenerate on every build.
- All proto files declare `csharp_namespace = "Discount.Grpc"` and `package discount`. The Basket client imports `Protos/discount.proto` today (Basket.API.csproj:32); after the split, Basket gets only `Protos/coupon.proto` for the existing RPCs (no churn in Basket), and `reward_code.proto` / `discount_rule.proto` exist only for Discount internal.

#### 0.4.4 Event versioning on the bus

Mirrors Catalog §6.5. Every `I*IntegrationEvent` from Discount carries `int SchemaVersion = 1`, `Guid EventId`, `Instant OccurredAt`, `Guid RestaurantId`. The `BuildingBlocks.Messaging/Outbox/OutboxMessage.cs:36` `SchemaVersion` column pins it. When the consumer (`DiscountHistoryAppendedIntegrationEvent` → Catalog) ignores unknown major versions, it's MassTransit's default behavior; we don't need extra code.

#### 0.4.5 Cross-cutting gRPC concerns

- **JWT bearer** — `Metadata["authorization"]` carrying `Bearer <jwt>`. `AddJwtBearer` validates against Identity's authority. `AuthenticationInterceptor` populates `HttpContext.User`; permission policies evaluate from claims.
- **Correlation ID** — `IHttpContextAccessor` middleware pushes `CorrelationId` onto the HTTP scope → outbox row `CorrelationId` column → MassTransit header → consumer's log scope (mirrors Catalog's flow).
- **Logging** — every RPC handler logs `RpcStarted`, `RpcCompleted`, `RpcFailed` with `CorrelationId` enrichment.
- **Deadline propagation** — gRPC clients (Basket) honor `deadline`; the server-side interceptor reads `ServerCallContext.Deadline` and passes it as the `CancellationToken` to handlers. Missing deadline → default 5-second budget.

---

## 1. Context

`Discount.Grpc` already runs end-to-end for Coupon CRUD over SQLite + EF Core. Five gRPC RPCs (`GetDiscount`, `CreateDiscount`, `UpdateDiscount`, `DeleteDiscount`, `RedeemDiscount`) on a single `DiscountProtoService`. One entity, `Coupon : AuditableEntity<int>`, with fields `RestaurantId, Code, Description, Amount, RedeemAmount, MaxRedeemAmount, ExpirationDate` and seeded sample rows (`DISCOUNT10` / `DISCOUNT20`).

Three cross-cutting capabilities are missing, and several owned entities have incomplete or absent features:

- **Auth is absent.** `current-architecture.md §4.4` documents this directly: *"no auth, no rate limiter"*. Basket is the only caller today, and it does so without authentication. Anyone on the network can `CreateDiscount` for any restaurant.
- **Multi-tenancy is unenforced.** `Coupon.RestaurantId` is filtered manually in 4 of the 5 RPCs but the global query-filter primitive (`BuildingBlocks.Multitenancy.ApplyTenantFilter`) is dormant. A miswritten query that forgets `Where(RestaurantId == ...)` is a cross-tenant leak waiting to happen.
- **Transactional outbox is absent.** `BuildingBlocks.Messaging.Outbox.IOutboxDbContext` etc. live in BuildingBlocks but Discount never implements them on its `DiscountContext`. No events emit from Discount today. No outbox table in the SQLite schema.
- **`RedeemDiscount` has a TOCTOU race.** Today: read coupon → check `RedeemAmount < MaxRedeemAmount` → increment → save. Two concurrent redemptions race past the read, both increments commit, both redemptions succeed past the cap. The Catalog plan doesn't address Discount's hot-path concurrency — Discount's plan does.

And the architectural entities are mostly absent:

- **`RewardCode`** — not in `Models/`. Architecture §411-415 prescribes the 4★/5★ generation; nothing implements it.
- **`DiscountRule`** — not in `Models/`. Architecture §3 prescribes the rule shape; nothing implements it.
- **The four-aggregate `Discount`/`PromoCode`/`RewardCode`/`DiscountRule` split** — collapses into the existing `Coupon` (Q1) plus the two new aggregates.

A detailed gap inventory is in `docs/architecture/_discount_gap_inventory.md` (added by this plan) and the drift memory `db-model-drift-reports.md` (which we'll re-write with a Discount chapter in Phase 1).

---

## 2. Goal

Bring `Discount.Grpc` to the level `architecture.md` describes (modulo the Coupon-collapse from §3) by:

1. **Adding JWT bearer auth** to the gRPC service; permission policies for the eleven new permission strings (counterpart to the `kitchen:*` and `menu:*` families the rest of the project already has).
2. **Activating multi-tenancy** via the dormant `BuildingBlocks.Multitenancy.ITenantEntity` primitive; fixing the `int → Guid` drift on `ITenantEntity.RestaurantId`.
3. **Wiring MassTransit + RabbitMQ + the existing outbox** for `DiscountHistoryAppendedIntegrationEvent`; implementing the first SQLite `OutboxDispatcher<TContext>` in BuildingBlocks. Emitting the consumer contract for Catalog (separate plan).
4. **Adding `RewardCode` and `DiscountRule` aggregates** with full gRPC CRUD + event consumers (`MenuItemChangedIntegrationEvent`, `RestaurantConfigurationChangedIntegrationEvent`).
5. **Building the `DiscountExpirySweepService : IHostedService`** + lazy-eval gate at every read; two-condition semantic.
6. **Fixing the `RedeemDiscount` race** via atomic conditional `UPDATE WHERE RedeemAmount < MaxRedeemAmount`.
7. **Stubbing** `FeedbackSubmittedConsumer` (deferred until Notification v1) and **documenting** the three architecture publishes (`DiscountApplied`, `RewardGenerated`, `RewardRedeemed`) as deferred-until-consumer.

The companion `docs/architecture/db_relational_model.mermaid` change-log appendix is also kept honest by this plan (the Coupon mermaid `Coupons` block stays; no other schema blocks need new lines this plan adds).

---

## 3. Out of scope

- **Redesigning the relational schema.** Drift baseline is held under `db-model-drift-reports.md`. The mermaid has only `Coupons` from Discount; this plan adds `RewardCode`, `DiscountRule`, the outbox tables. Mermaid updates land in the same commit as the code per §9.1.
- **Splitting `Discount.Grpc` into `Discount.Domain` / `Discount.Application` / `Discount.Infrastructure`** projects. The single-project layout is intentional; matches Coupon's existing structure.
- **The frontend project.** Lives in a different folder, owned separately.
- **Switching Discount to Postgres.** Q2 record: SQLite stays. The plan accommodates a future Postgres migration by routing all engine-specific code through `IOutboxDbContext` / `OutboxDispatcher<TContext>` (engine-neutral in BuildingBlocks); a Postgres-specific `OutboxDispatcher<KitchenDbContext>` etc. already exists in BuildingBlocks. A future Discount-on-Postgres would mean writing `OutboxDispatcher<DiscountContext>` with Postgres-specific `BuildClaimSql` — same shape as the SQLite one we ship here.
- **`OutboxDeadLetterProbe` surrogate for Discount.** Catalog has one; the same primitive from `BuildingBlocks.Messaging.Outbox.OutboxDeadMessage` is reused. The Discount `/ready` health check counts `outbox_messages_dead` rows against `DiscountOptions:OutboxDeadLetterThreshold` (default 0).
- **The four-aggregate `Discount`/`PromoCode`/`RewardCode`/`DiscountRule` split.** Architecture §3 describes it; Q1 collapses it. The Coupon mermaid block keeps the name `Coupons` and adds the new tables (`RewardCodes`, `DiscountRules`) without introducing a new `Discounts`/`PromoCodes` table. A future ADR can split if/when a real need appears.
- **Adding new permissions in Identity.** Per-identity string addition is owned by the Identity service. Discount lists the eleven strings required and their gating intent; Identity adds the rows. Until Identity ships, **Discount's policies enforce the names but unknown-permission gating defaults to deny** (a permission-policy request with no matching claim is `Failure(...)`, not `Success(...)`).
- **Adding `Notification.API v1` skeleton.** Out-of-plan. Discount's Phase 5 stub consumer waits for it.
- **Adding the Ordering-side `OrderCreatedIntegrationEvent` publisher.** Out-of-plan. Discount's `OrderCreatedConsumer` is not written.
- **Frontend admin-portal HTTP surface.** Q4 chose gRPC-only. Future admin portal surfaces gRPC-Web or grpc-gateway.

---

## 4. Service boundaries

### Discount.Grpc owns

- **`Coupon`** — the unified concept that collapses architecture's `Discounts` + `PromoCodes`. Existing entity extended with audit columns from `AuditableEntity<int>` (already there) and the `Q7` lazy-eval gate.
- **`RewardCode`** — new aggregate; Phase 3.
- **`DiscountRule`** — new aggregate; Phase 2.
- **Outbox tables** (`outbox_messages`, `outbox_messages_dead`) on the same SQLite DB; engine-native row-locking handled by the SQLite claim-SQL we add to BuildingBlocks.
- **DiscountExpirySweepService** — `IHostedService`, in-process, no Hangfire.
- **`DiscountOptions`** — strongly-typed config (`SweepIntervalMinutes`, `OutboxDeadLetterThreshold`, `EnableHistoryPublishing`, `EnableMenuItemChangedConsumer`, `EnableRestaurantConfigChangedConsumer`, `EnableFeedbackSubmittedConsumer`).

### Discount.Grpc publishes

| Event | Phase | Consumers (planned) |
|---|---|---|
| `DiscountHistoryAppendedIntegrationEvent` (SchemaVersion=1) | Phase 4 — ships | **Catalog** (own plan) → writes Marten `EntityHistoryArchive` document. |
| `DiscountAppliedIntegrationEvent` | **Deferred** | (no current consumer; emit when one is wired) |
| `RewardGeneratedIntegrationEvent` | **Deferred** | (no current consumer; emit when one is wired) |
| `RewardRedeemedIntegrationEvent` | **Deferred** | (no current consumer; emit when one is wired) |

### Discount.Grpc consumes

| Event | Publisher (today) | Phase | Discount's action |
|---|---|---|---|
| `MenuItemChangedIntegrationEvent` | Catalog (ships) | Phase 2 | Find active `DiscountRule`s for the affected `RestaurantId` whose `RuleDataJson.RequiredMenuItemIds` includes `MenuItemId`; evaluate; flip `Coupon.IsActive=false` (or activate) per rule outcome. |
| `RestaurantConfigurationChangedIntegrationEvent` | Catalog (ships) | Phase 2 | If `ChangedFields` contains `Currency`, deactivate `Coupon`s for that `RestaurantId` whose currency doesn't match. |
| `FeedbackSubmittedIntegrationEvent` | Notification v1 (doesn't ship) | Phase 5 (stub) | Generated-reward-code flow per Q6's hardcoded 4★/5★ rule. Stub consumer is wired (InMemoryTestHarness passes) but `MassTransit` registration gated by `DiscountOptions:EnableFeedbackSubmittedConsumer=false` until publisher ships. |
| `OrderCreatedIntegrationEvent` | Ordering (doesn't ship) | **Deferred** | Auto-apply: find active `Coupon`s + `RewardCode`s for `RestaurantId` whose `DiscountRule` matches the order; emit `DiscountAppliedIntegrationEvent`. Not written this plan. |

### Discount.Grpc does NOT own

- **`Users`**, **`Restaurants`** — Identity / Catalog respectively. Discount references `RestaurantId` only; never reads/writes user or restaurant rows.
- **`Orders`**, **`Baskets`**, **`MenuItems`** — read-only ID references; no row writes.
- **`CustomerFeedback`** — Notification, when its plan lands. Discount consumes `FeedbackSubmitted` but doesn't persist the feedback row.
- **`PromotionHistory`** — Catalog (Marten). Q3's history architecture puts the historical archive document in Catalog; Discount is the publisher.

### Cross-service flow (one-liner)

```
Catalog publishes MenuItemChanged / RestaurantConfigurationChanged
  → Discount consumes → re-evaluate active DiscountRules
Notification (when it ships) publishes FeedbackSubmitted
  → Discount consumes → generate RewardCode per hardcoded 4★/5★ rule → publish RewardGenerated (deferred)
Discount publishes DiscountHistoryAppended
  → Catalog consumes → write Marten EntityHistoryArchive
Basket calls Discount gRPC at checkout
  → GetDiscount / RedeemDiscount
```

---

## 5. Tech decisions

| Decision | Choice | Reason |
|---|---|---|
| Architecture | Single-project vertical slice (existing) | Matches Catalog / Basket / Kitchen pattern. |
| Framework | ASP.NET Core 10 + gRPC (`Grpc.AspNetCore` already in use) | Today. |
| Language | C# 12+ (records for integration events; primary constructors for handlers and small services; collection expressions; required members; nullable enabled) | New code uses modern C#. Existing `DiscountService.cs` updated only where edits are made. |
| Persistence | EF Core 10 + `Microsoft.EntityFrameworkCore.Sqlite` (existing) | Per Q2 — SQLite stays. |
| Auth | `Microsoft.AspNetCore.Authentication.JwtBearer` (new in Phase 1) | Mirrors every other API; `AddJwtBearer` against Identity authority. |
| Permissions | `Microsoft.AspNetCore.Authorization` policy provider; per-RPC attributes | `[Authorize(Policy = "coupon:create")]` on the gRPC methods. |
| Tenancy | `BuildingBlocks.Multitenancy.ITenantEntity` (after int→Guid fix) + `ApplyTenantFilter` | Per Q11. |
| Cache | **None** for `GetDiscount` | Per Q12 — SQLite indexed reads are sub-ms; cache layer would cost > benefit. |
| **`Idempotency-Key`** | Redis (`idempotency:{rId}:{sha256(key+rId+code)}`, 24h TTL) | For state-transition RPCs (`RedeemDiscount`, `UpdateDiscount`, `RedeemRewardCode`). Mirrors Catalog §0.4.8. |
| Messaging | `MassTransit` via `BuildingBlocks.Messaging.MassTransit.AddMessageBroker` (modified signature `AddMessageBroker(this IServiceCollection, Assembly? consumers = null)` so Discount can pass `typeof(FeedbackSubmittedConsumer).Assembly`). | Reuse; consumer assembly hint needed because Discount's consumer types live in `Discount.Grpc`, not the default `BuildingBlocks.Messaging` scan target. |
| Event versioning | `int SchemaVersion` on every integration event (initial = 1) | Mirrors Catalog §6.5. |
| Outbox | First SQLite `OutboxDispatcher<DiscountContext>` in BuildingBlocks (single-replica, `claim_id` GUID column atomic-claim pattern) | Per Q8. |
| Async lifecycle | `IHostedService` (no Hangfire) | Per Q7. |
| Engine trigger | `IConsumer<T>` with **Pattern 2** (synthetic ClaimsPrincipal from event payload) for bus-triggered consumers; primary JWT for RPC-triggered callers | Per Q10. |
| Health checks | `/live` (process up) + `/ready` (Postgres … no, SQLite file reachable + RabbitMQ broker reachable + outbox dead-letter count against threshold). | Mirrors Catalog §0.4's split; SQLite file reachability = `new DbContext<DiscountContext>().Database.CanConnectAsync()`. |
| Time / IDs | NodaTime `Instant`, `Guid` ids (RestaurantId), `int` ids (entity PK) | Mixed today (Coupon PK is `int`; RestaurantId is `Guid`); matches the rest of the project. |
| Logging | Structured logging with `Serilog` (already in use project-wide via BuildingBlocks). Add `CorrelationId` enrichment. | Default. |
| Tests | xUnit + FluentAssertions + NSubstitute (unit) + SQLite `:memory:` (integration) + `MassTransit.InMemoryTestHarness` (consumer) | Per Q9. |

### What this plan does NOT introduce

- **No new database.** Discount stays on its existing `discountdb` instance.
- **No new message broker.** RabbitMQ (already running in docker-compose).
- **No new permission scheme at the protocol level.** Eleven new permission *names* (defined in Discount) require eleven Identity-side rows (deferred to Identity plan).
- **No saga/orchestrator.** RewardCode generation is a one-shot Consumer method, not a saga.
- **No Postgres.** Out of scope per Q2.
- **No Hangfire.** Per Q7.

---

## 6. Folder layout

Today's project is flat. This plan adds folders, no restructuring:

```
orderly-microservices/Services/Discount/Discount.Grpc/
  Models/                                  -- existing (Coupon.cs); add RewardCode.cs (Phase 3), DiscountRule.cs (Phase 2)
  Data/                                    -- existing (DiscountContext.cs, Migrations/); add ICurrentRestaurantProvider.cs, RestaurantContext.cs, Outbox/MartenOutboxMessageConfiguration.cs (Phase 1, SQLite-flavored)
  Services/
    DiscountService.cs                     -- existing (modified Phase 1: JWT-validate, lazy-gate, conditional UPDATE)
    RewardCodeService.cs                   -- Phase 3
    DiscountRuleService.cs                 -- Phase 2
  Authorization/
    DiscountPermissions.cs                 -- constants: coupon:read/create/edit/delete/redeem, reward:read/create/edit/delete/redeem, discount-rule:read/edit
    AuthorizationPolicies.cs               -- extension methods: AddDiscountPolicies(IServiceCollection)
    ICurrentRestaurantProvider.cs          -- interface (Phase 1)
    ClaimsRestaurantProvider.cs            -- implementation: reads restaurantId from ClaimsPrincipal (Phase 1)
  Exceptions/                              -- existing; add ConcurrentRedemptionException, CrossTenantAccessException (Phase 1)
  Messaging/
    Events/
      DiscountHistoryAppendedIntegrationEvent.cs   -- Phase 4
      FeedbackSubmittedIntegrationEvent.cs         -- Phase 5 (consume-side reference, defined in Notification when its plan lands)
    EventHandlers/
      FeedbackSubmittedConsumer.cs                 -- Phase 5 stub (Pattern 2 synthetic claims)
      MenuItemChangedConsumer.cs                   -- Phase 2
      RestaurantConfigurationChangedConsumer.cs    -- Phase 2
    Outbox/
      DiscountOutboxPublisher.cs                   -- Phase 1, instance class wrapping IOutboxPublisher
      DiscountOutboxDispatcher.cs                  -- Phase 1, inherits OutboxDispatcher<DiscountContext>; SQLite claim SQL
  Scheduling/
    DiscountExpirySweepService.cs                  -- Phase 1, IHostedService
  Hosting/
    (none today; consider splitting out of Scheduling/ in Phase 4 if more hosted services arrive)
  Health/
    DiscountHealthChecks.cs                         -- Phase 1; /live + /ready split
  Protos/
    discount.proto                                  -- existing; split into coupon.proto, reward_code.proto, discount_rule.proto in Phase 2/3
  Program.cs                                        -- Phase 1 wiring: JWT, AddDiscountPolicies, ICurrentRestaurantProvider, AddDiscountContext, AddMessageBroker(typeof(...).Assembly), AddHostedService<DiscountExpirySweepService>, AddHostedService<DiscountOutboxDispatcher>, MapGrpcService<DiscountService>, MapHealthChecks(...)
```

The `BuildingBlocks.Messaging.Outbox` project gains one new file: `OutboxDispatcher` extension / override hooks (or a partial method override on the abstract base) that allow SQLite-specific claim SQL.

---

## 6.5 Consumer contract matrix

Single source of truth for which event Discount publishes / consumes. Both sides must honor this list at every release. Discount will not change a published event's shape without a major `SchemaVersion` bump.

| Event (Discount →) | SchemaVersion=1 fields | Intended consumers → required action |
|---|---|---|
| `DiscountHistoryAppendedIntegrationEvent` | `EntityType ∈ {Coupon, RewardCode, DiscountRule}`, `EntityId`, `RestaurantId`, `ChangeType ∈ {Created, Updated, Deleted, Redeemed}`, `OldValues` (jsonb|null), `NewValues` (jsonb|null), `OccurredAt`, `SchemaVersion=1` | **Catalog** → write a Marten `EntityHistoryArchive` document keyed by `EntityType + EntityId`. Idempotent on `EntityType + EntityId + ChangeType + OccurredAt`. |

| Event (→ Discount) | Source (today / planned) | SchemaVersion | Discount's required action |
|---|---|---|---|
| `MenuItemChangedIntegrationEvent` | Catalog (ships) | 1 | Find `DiscountRule`s where `IsActive=true`, `RestaurantId == event.RestaurantId`, and `RuleDataJson.RequiredMenuItemIds` includes `event.MenuItemId`. If any rule exists that targets the now-removed MenuItem (ChangeType=Deleted), flip the related `Coupon.IsActive=false`. No state change for non-affected rules. |
| `RestaurantConfigurationChangedIntegrationEvent` | Catalog (ships) | 1 | If `ChangedFields` contains `Currency`: find `Coupon` for `event.RestaurantId` whose `Amount` would be in the old currency; if the new currency differs, `IsActive=false`. Other `ChangedFields` are no-op for Discount. |
| `FeedbackSubmittedIntegrationEvent` | Notification v1 (does not ship) | 1 | Per Q6 hardcoded rule: `rating >= 4 && < 5` → `RewardCode { Type: "percentage", Value: 10 }`; `rating == 5` → `RewardCode { Type: "percentage", Value: 15 }` + `RewardCode { Type: "free_item", Value: "appetizer" }`. Both for the feedback's `RestaurantId`. Idempotent on `(OrderId, RewardType, RewardValue)` — guard with an `Idempotency-Key` header variant on the bus (MassTransit `MessageId` is sufficient). Stub consumer ships wired-but-disabled via `DiscountOptions:EnableFeedbackSubmittedConsumer=false`. |
| `OrderCreatedIntegrationEvent` | Ordering (does not ship) | 1 | Auto-apply lookup; emit `DiscountAppliedIntegrationEvent`. **Not implemented in this plan** — flagged as a follow-up when Ordering plan ships. |

**Migration rule when this matrix changes (mirrors Catalog §6.5):**
- Adding a field → bump SchemaVersion; consumers ignore unknown fields by MassTransit default.
- Renaming a field → rename + bump major; consumers on v1 ignore the new name; publish both for one cycle, drop v1 after one release with zero traffic.
- Removing a field → same: introduce next major, deprecate, drop after one release.

---

## 6.6 Cross-service handshakes

Three handshakes bridge this plan with sibling plans:

### 6.6.1 With Catalog: `DiscountHistoryAppendedIntegrationEvent` → `EntityHistoryArchive`

**Catalog will own:**
- New Marten document `EntityHistoryArchive` in `Catalog.API/Domain/Events/EntityHistoryArchive.cs` with fields `Guid Id`, `EntityType`, `int EntityId`, `int RestaurantId`, `string ChangeType`, `JsonObject OldValues`, `JsonObject NewValues`, `Instant OccurredAt`, `string CorrelationId`.
- New `Messaging/EventHandlers/DiscountHistoryAppendedConsumer.cs : IConsumer<DiscountHistoryAppendedIntegrationEvent>`. Idempotent on `(EntityType, EntityId, ChangeType, OccurredAt)` — Marten session checks before insert.
- Marten schema registration in `Program.cs` (`opt.Schema.For<EntityHistoryArchive>();`).
- Tests in `Catalog.API.Tests/Integration/DiscountHistoryAppendedConsumerTests.cs` using Testcontainers Postgres + the InMemoryTestHarness or the existing RabbitMQ container.

**This plan owns:**
- The publisher side: `DiscountHistoryAppendedIntegrationEvent` class, `IOutboxPublisher.WriteOutboxMessageAsync(new DiscountHistoryAppendedIntegrationEvent(...))` at the end of every `Coupon`/`RewardCode`/`DiscountRule` CUD handler.
- Consumer contract fields (the table in §6.5).

**Sync point:** the consumer contract is locked; if Catalog adds a field the consumer ignores, Discount publish-side stays at SchemaVersion=1. Catalog's plan documents the consumer implementation; this plan documents the producer.

### 6.6.2 With Identity: eleven permission strings

**Identity will own:**
- Eleven new rows in the `Permissions` table (or `RolePermissions` registrations), six on `coupon:*`, five on `reward:*`, two on `discount-rule:*`. Existing roles seeded (`Manager`, `Waiter`, `Cashier`, `SuperAdmin`) get appropriate role→permission mappings:
  - `coupon:read` → all restaurant roles + SuperAdmin
  - `coupon:create` / `coupon:edit` / `coupon:delete` → `Manager` + `SuperAdmin`
  - `coupon:redeem` → `Cashier` + `Manager` + `SuperAdmin` (the actor that redeems)
  - `reward:read` / `reward:edit` → `Manager` + `SuperAdmin`
  - `reward:redeem` → `Cashier` + `Manager` + `SuperAdmin`
  - `reward:create` → `SuperAdmin` only (only feedback flow creates rewards for now)
  - `discount-rule:read` / `discount-rule:edit` → `Manager` + `SuperAdmin`

**This plan owns:**
- Permission string constants in `DiscountPermissions.cs`.
- Authorization policy wiring in `Program.cs` (`AddDiscountPolicies` registers each policy as `Policy = "coupon:create"` → require Claim `permission == "coupon:create"`).
- Per-RPC `[Authorize(Policy = "coupon:create")]` attributes.

**Sync point:** until Identity ships, the policy enforcement denies on missing claim (default deny). Discount ships fully; Identity catchup is a follow-up commit.

### 6.6.3 With Notification v1: `FeedbackSubmittedIntegrationEvent`

**Notification v1 will own:**
- The publisher (`FeedbackSubmittedConsumer` in Notification consumers `OrderCompletedIntegrationEvent` and emits `FeedbackSubmittedIntegrationEvent`).
- The `CustomerFeedback` aggregate.

**This plan owns:**
- The consumer side stub: `Discount.Grpc/Messaging/EventHandlers/FeedbackSubmittedConsumer.cs`. Wired to the bus via `MassTransit.AddConsumer<FeedbackSubmittedConsumer>()`. **Disabled** via `DiscountOptions:EnableFeedbackSubmittedConsumer=false` default. The flag means `Bus.Factory.CreateUsingRabbitMq(...)` adds a `IConsumerDefinition<>` that returns a no-op bus configuration when disabled (MassTransit's `ConfigureConsumer<T>(x => x.Enabled = false)`).
- Tests in `Discount.Grpc.Tests/Integration/FeedbackSubmittedConsumerTests.cs` using `InMemoryTestHarness` — when `EnableFeedbackSubmittedConsumer=true`, fire a `FeedbackSubmittedIntegrationEvent` with `OverallRating=5`, assert two `RewardCode` rows are written for the feedback's `RestaurantId`. Without the flag, the consumer doesn't run; tests are gated by the same flag.

**Sync point:** this plan ships, Notification v1 ships independently. When both exist in the same deploy, the flag flips; no code change required in Discount.

---

## 6.7 Outbox claim SQL — SQLite variant (BuildingBlocks contribution)

This is the first SQLite-flavored `BuildClaimSql` in BuildingBlocks.Messaging.Outbox. The pattern differs from the Postgres `FOR UPDATE SKIP LOCKED` and MSSQL `WITH (ROWLOCK, UPDLOCK, READPAST)` shapes.

```sql
UPDATE outbox_messages
   SET claim_id = @claimId
 WHERE Id IN (
     SELECT Id FROM outbox_messages
      WHERE DispatchedAt IS NULL AND claim_id IS NULL
        AND SchemaVersion <= @maxSupportedVersion
      ORDER BY OccurredOn
      LIMIT @batchSize
 )
RETURNING *;
```

Key differences from the Postgres pattern:

- **`claim_id` GUID column** — populated with `Guid.NewGuid()` at the start of each dispatcher iteration (per-instance, not per-row). Atomic single-SQL claim; SQLite serializes writers through its database-level lock, so two concurrent dispatchers can't both observe the same undispatched rows.
- **`RETURNING *`** — SQLite supports this since 3.35 (matching the project's SQLite 10.x in EF Core 10).
- **Per-iteration transaction** — `OutboxDispatcher<TContext>.DispatchBatchAsync` already wraps the claim+dispatch+stamp in `BeginTransactionAsync`. SQLite's per-database write lock means the lock is held until commit, which gives us the same "no double-publish" guarantee Postgres's `FOR UPDATE SKIP LOCKED` gives via row locks — just coarser.

**Migration that creates the `outbox_messages` + `outbox_messages_dead` tables + `claim_id` column on `outbox_messages`:**

```csharp
public partial class OutboxMigration : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
            CREATE TABLE outbox_messages (
                Id TEXT NOT NULL PRIMARY KEY,
                OccurredOn INTEGER NOT NULL,
                Type TEXT NOT NULL,
                Payload TEXT NOT NULL,
                DispatchedAt INTEGER NULL,
                SchemaVersion INTEGER NOT NULL DEFAULT 1,
                claim_id TEXT NULL
            )");
        migrationBuilder.Sql("CREATE INDEX IX_outbox_messages_DispatchedAt_OccurredOn ON outbox_messages (DispatchedAt, OccurredOn)");
        migrationBuilder.Sql(@"
            CREATE TABLE outbox_messages_dead (
                Id TEXT NOT NULL PRIMARY KEY,
                OccurredOn INTEGER NOT NULL,
                Type TEXT NOT NULL,
                Payload TEXT NOT NULL,
                SchemaVersion INTEGER NOT NULL,
                Reason TEXT NOT NULL,
                RejectedAt INTEGER NOT NULL
            )");
    }
}
```

Instant `INTEGER` round-trip is `ToUnixTimeTicks()` / `FromUnixTimeTicks()` per the existing `InstantToLongConverter` in `Data/DiscountContext.cs:57-67`. EF Core `HasConversion` keeps the mapping aligned across both tables.

**Why this is a BuildingBlocks contribution, not Discount-private:** Catalog's `OutboxDispatcher<KitchenDbContext>` etc. all target Postgres; the SQLite shape is generic enough that Discount-on-SQLite AND any future service-on-SQLite (`docs/architecture/current-architecture.md` lists SQLite as a first-class engine — line 22) can adopt it. The contribution is the `BuildClaimSql` override hook — Discount opens the door; future services walk through.

---

## 7. Phased milestones

The phases are ordered so each is independently shippable and any earlier phase's failure does not block the later cross-cutting concerns.

### Phase 1 — Production-grade Coupon (foundation: auth + tenancy + outbox + sweep + race fix)

This is the heaviest phase. It establishes every cross-cutting primitive on which Phases 2 / 3 / 4 layer new features. Plan target: ~25 tests passing.

- **BuildingBlocks fix:** `BuildingBlocks.Multitenancy.ITenantEntity.RestaurantId : int → Guid`. Trivial; one line. Justifies Phase 1's own tenancy work.
- **JWT auth wired** in `Program.cs`:
  - `AddJwtBearer` against Identity authority (`https://localhost:5057`), audience `OrderlyMicroservices`.
  - `AddDiscountPolicies(IServiceCollection)` extension registers `coupon:read/create/edit/delete/redeem` + `reward:*` + `discount-rule:*` policies that gate on the corresponding claim.
  - `[Authorize(Policy = "coupon:read")]` on `GetDiscount`; per-method attributes for the rest.
- **`ICurrentRestaurantProvider` registered** as `Singleton`. `ClaimsRestaurantProvider` reads `ClaimTypes.Role`/custom `restaurantId` claim from `IHttpContextAccessor.HttpContext.User`. Bus consumers use `ClaimsPrincipalFactory.FromEvent<T>(T evt)` to build the synthetic principal (Pattern 2 from Q10).
- **Multi-tenancy global filter applied** to `Coupon`:
  - `Coupon : AuditableEntity<int>, ITenantEntity`.
  - `DiscountContext.OnModelCreating` calls `ApplyTenantFilter<Coupon>(() => _provider.RestaurantId)`.
  - `DiscountService` gRPC handlers drop the now-redundant explicit `Where(RestaurantId == ...)` filters (the global filter handles it).
  - Tests: one "naive query returns only current tenant" test; one SuperAdmin `.IgnoreQueryFilters()` test (justifies the bypass pattern later used in (7)).
- **Outbox tables added** to Discount's SQLite schema. See §6.7 for the migration. The `IOutboxDbContext` interface is implemented on `DiscountContext`. `BuildingBlocks.Messaging.Outbox.OutboxDispatcher<DiscountContext>` (named `DiscountOutboxDispatcher`) is the concrete; it overrides `BuildClaimSql` with the SQLite variant.
- **`IOutboxPublisher.WriteOutboxMessageAsync`** called from `DiscountService.CreateDiscount` / `UpdateDiscount` / `DeleteDiscount` (history event payload: payload-of-row-delta). Same hooks in `RedeemDiscount`. Each outbox row carries `CorrelationId` from the gRPC `Metadata`. Total Phase-1 publish surface is one event type (`DiscountHistoryAppendedIntegrationEvent`).
- **`DiscountExpirySweepService : BackgroundService`** runs every `DiscountOptions:SweepIntervalMinutes` (default 5, `[Range(1, 1440)]`). Sweeps: `UPDATE Coupons SET IsActive = 0, LastModifiedBy = 'discount-sweep', LastModifiedAt = @now WHERE ExpirationDate < @now AND IsActive = 1`. Idempotent.
- **Lazy-evaluation gate** at every read of Coupon's "active" semantic. Add a helper `Coupon.IsActiveNow(IClock clock)` that returns `IsActive && (ExpirationDate IS NULL || ExpirationDate >= UtcNow)`. `GetDiscount` / `RedeemDiscount` consult `IsActiveNow(...)` before returning success. Hot path adds at most one boolean evaluation per call.
- **`RedeemDiscount` race fix:** replace the read-modify-write with conditional update:
  ```csharp
  int rowsAffected = await _dbContext.Database.ExecuteSqlInterpolatedAsync(
      $"UPDATE Coupons SET RedeemAmount = RedeemAmount + 1, LastModifiedAt = {now} WHERE Id = {id} AND (MaxRedeemAmount IS NULL OR RedeemAmount < MaxRedeemAmount) AND IsActive = 1",
      cancellationToken);
  if (rowsAffected == 0)
      throw new ConcurrentRedemptionException(id, correlationId);
  ```
  Map `ConcurrentRedemptionException` to `StatusCode.Aborted` (retry once internally; second failure surfaces `FailedPrecondition`).
- **`/live` + `/ready` split for Discount:**
  - `/live` always green; liveness only.
  - `/ready` checks: SQLite file reachable (try-`open`), RabbitMQ broker reachable (`MassTransit` health check), outbox dead-letter count against `DiscountOptions:OutboxDeadLetterThreshold` (default 0). The custom probe is a `IHealthCheck` reading `_dbContext.OutboxDeadMessages.CountAsync()`.
  - `DiscountOptions:OutboxDeadLetterThreshold` exposed, `[Range(0, int.MaxValue)]`. `ValidateOnStart()` enforces.
- **Tests:** xUnit + FluentAssertions + NSubstitute unit tests per Coupon RPC happy + not-found + cross-tenant-deny paths; SQLite-in-memory integration tests for outbox dispatcher claim SQL (publish 5 messages, dispatch once, assert 5 rows `DispatchedAt` is set; second dispatch yields 0); `InMemoryTestHarness` for any consumer test (none yet, but the harness is configured); sweep service tests via `Microsoft.Extensions.TimeProvider.Testing`.

**Doc-update scope (§0.2):**
- §2 Tech Stack — add `Microsoft.AspNetCore.Authentication.JwtBearer`; add SQLite outbox note.
- §4.4 Discount Service — replace "no auth, no rate limiter" with the JWT auth + 11 permission policies. Replace "single entity Coupon" with "Coupon + outbox tables + sweep service + ITenantEntity adopted".
- §4.4 — note `/live` + `/ready` split.
- §5.1 Synchronous — "every API | Identity.API | JWT bearer validation" already covers Discount by Q1's wiring; no row change needed.
- §5.2 Asynchronous — add `DiscountHistoryAppendedIntegrationEvent` publish row (deferred consumer = Catalog own plan).
- §6 Data Stores — SQLite `discountdb` gains `outbox_messages`, `outbox_messages_dead`, `claims`, `_ef_migrations`.
- §9 Cross-Cutting Patterns — note first SQLite outbox claim SQL, JWT auth + 11 permission names, `ITenantEntity` adoption.

### Phase 2 — `DiscountRule` aggregate + Catalog event consumers

- **New entity `DiscountRule : AuditableEntity<int>, ITenantEntity`:**
  ```csharp
  public class DiscountRule : AuditableEntity<int>, ITenantEntity
  {
      public Guid RestaurantId { get; set; }
      public int CouponId { get; set; }            // FK → Coupons.Id; UK per coupon
      public DiscountRuleType RuleType { get; set; } // MinOrderAmount | RequiredMenuItems | TimeWindow | Bogo
      public string RuleDataJson { get; set; } = "{}";  // rule-type-specific payload
      public bool IsActive { get; set; } = true;
  }
  ```
  `RuleDataJson` shape: `{ MinOrderAmount?: decimal, RequiredMenuItemIds?: Guid[], TimeWindow?: { StartTime: time, EndTime: time, DayOfWeekMask: int }, BuyQuantity?: int, GetQuantity?: int }`.
- **New RPC service `DiscountRuleProtoService`:** `CreateDiscountRule`, `GetDiscountRule`, `ListDiscountRules` (paged, `PagedResult<DiscountRuleModel>`), `UpdateDiscountRule`, `DeleteDiscountRule`, `EvaluateDiscountRules(EvaluateDiscountRulesRequest) → EvaluateDiscountRulesResponse { IReadOnlyList<int> ApplicableCouponIds }`. All permission-gated with `discount-rule:read` / `discount-rule:edit`. `Evaluate` is `discount-rule:read`.
- **Schema:** `DiscountRules` table with FK to `Coupons`, UK `(RestaurantId, CouponId)`. The FK uses `OnDelete(DeleteBehavior.Restrict)` per the project-wide rule (Catalog §8 / "Cascade-delete policy").
- **Two new consumers** wired to `DiscountRuleProtoService` lifecycle + bus:
  - **`MenuItemChangedConsumer : IConsumer<MenuItemChangedIntegrationEvent>`.** Pattern 2 (synthetic claims from event). Logic: for `ChangeType ∈ {Updated, Deleted}`, find `DiscountRule`s whose `RuleDataJson.RequiredMenuItemIds` includes `event.MenuItemId`. For each affected `Coupon`, recompute via `Coupon.IsActiveNow(...)` and persist `IsActive` based on whether at least one rule still holds. **Idempotent** on `(MenuItemId, ChangeType, RestaurantId)`. Implementation uses an internal dictionary keyed by `RestaurantId` for the last-seen `EventId + OccurredAt`.
  - **`RestaurantConfigurationChangedConsumer : IConsumer<RestaurantConfigurationChangedIntegrationEvent>`.** Pattern 2. Logic: if `ChangedFields` contains `"Currency"`, find `Coupon` for `RestaurantId`, flip `IsActive=false`. Otherwise no-op.
- **Tests:** xUnit + NSubstitute unit tests for `EvaluateDiscountRules` (MinOrderAmount, RequiredMenuItems, TimeWindow matchers); SQLite-in-memory integration for the `MenuItemChangedConsumer` end-to-end (set up a coupon + rule, fire event with `ChangeType=Deleted`, assert `Coupon.IsActive=false`); `InMemoryTestHarness` for the same consumer.

**Doc-update scope (§0.2):**
- §4.4 Discount Service — entity table gains `DiscountRule` row; endpoint list gains the 6 new RPCs + the 2 new consumers.
- §5.2 Asynchronous — add the consumer rows for `MenuItemChangedIntegrationEvent` and `RestaurantConfigurationChangedIntegrationEvent`.
- §9 Cross-Cutting Patterns — note `DiscountRule` gRPC + JSONB rule data + FK semantics.

### Phase 3 — `RewardCode` aggregate + redemption flow

- **New entity `RewardCode : AuditableEntity<int>, ITenantEntity`:**
  ```csharp
  public class RewardCode : AuditableEntity<int>, ITenantEntity
  {
      public Guid RestaurantId { get; set; }
      public string Code { get; set; } = default!;       // UK with RestaurantId
      public RewardType RewardType { get; set; }        // Percentage | FixedAmount | FreeItem | Points
      public decimal Value { get; set; }                // % for Percentage; currency for FixedAmount; item id for FreeItem; count for Points
      public string? Description { get; set; }
      public Instant? ExpirationDate { get; set; }
      public int RedeemAmount { get; set; }
      public int? MaxRedeemAmount { get; set; }
      public Guid? RedeemedInOrderId { get; set; }      // last redeeming order
      public Instant? RedeemedAt { get; set; }
  }
  ```
- **New RPC service `RewardCodeProtoService`:** `CreateRewardCode`, `GetRewardCode`, `ListRewardCodes` (paged), `UpdateRewardCode`, `DeleteRewardCode`, `RedeemRewardCode(RedeemRewardCodeRequest) → RedeemRewardCodeResponse`. Permissions `reward:read/create/edit/delete/redeem`. Apply `ITenantEntity` + global filter.
- **Race-fix pattern re-applied to `RedeemRewardCode`** — same conditional UPDATE as `RedeemDiscount` in Phase 1.
- **Lazy-eval gate + sweep pattern reused** — `RewardCode.IsActiveNow(IClock)`. `DiscountExpirySweepService` extends to also flip `RewardCode.IsActive=false` on expiry (single sweep, two UPDATEs).
- **Outbox publishes** — every RewardCode CUD + redeem writes a `DiscountHistoryAppendedIntegrationEvent` row (per Q3's history decision; `EntityType=RewardCode`).
- **Tests:** xUnit + NSubstitute unit + SQLite-in-memory integration for the same patterns as Phase 1/2.

**Doc-update scope (§0.2):**
- §4.4 Discount Service — entity table gains `RewardCode`; endpoint list gains 6 RPCs.
- §5.2 Asynchronous — no new publish rows (the publishes go through `DiscountHistoryAppendedIntegrationEvent`; covered by Phase 1 row).
- §9 Cross-Cutting Patterns — nothing new beyond Phase 1's outbox row.

### Phase 4 — History publishing wired across all aggregates

This phase is mostly "fill in the rest of the publish points" for the entities created in Phases 1 / 2 / 3. The `DiscountHistoryAppendedIntegrationEvent` was published from Coupon CUD in Phase 1; expand to RewardCode and DiscountRule.

- **`DiscountHistoryAppendedIntegrationEvent` payload struct:**
  ```csharp
  public sealed record DiscountHistoryAppendedIntegrationEvent(
      string EntityType,           // "Coupon" | "RewardCode" | "DiscountRule"
      int EntityId,
      Guid RestaurantId,
      string ChangeType,            // "Created" | "Updated" | "Deleted" | "Redeemed"
      string? OldValues,            // serialized JSON, null for Created
      string NewValues,             // serialized JSON
      Guid CorrelationId,
      Instant OccurredAt,
      int SchemaVersion = 1) : IntegrationEvent;
  ```
- **`IOutboxPublisher.WriteOutboxMessageAsync(...)` calls** added to all Phase 2 / 3 handlers. OldValues captured via EF Core `ChangeTracker.OriginalValues` (already in `BuildingBlocks.Entities.Interceptors.AuditableEntityInterceptor`).
- **Documentation-only hand-off to Catalog:** this phase's commit message names the consumer contract; Catalog's plan will own the consumer implementation.
- **Tests:** one idempotency test in `Discount.Grpc.Tests/Integration/OutboxHistoryPublisherTests.cs` — fire 3 mutations, dispatch once, assert 3 outbox rows + 3 bus messages with `SchemaVersion=1`.

**Doc-update scope (§0.2):**
- §4.4 Discount Service — note that all three aggregates now publish `DiscountHistoryAppendedIntegrationEvent`.
- §5.2 Asynchronous — the existing `DiscountHistoryAppendedIntegrationEvent` row gets the "publishes from" fields elaborated.
- §9 Cross-Cutting Patterns — note the change-tracker-driven OldValues capture.

### Phase 5 — Stub `FeedbackSubmittedConsumer` (wired, disabled until Notification v1)

- **`FeedbackSubmittedConsumer : IConsumer<FeedbackSubmittedIntegrationEvent>`** lives at `Discount.Grpc/Messaging/EventHandlers/FeedbackSubmittedConsumer.cs`.
- **Pattern 2 synthetic claims:** consumer constructs `ClaimsPrincipal` with `RestaurantId` from `event.RestaurantId`, `actor=discount-service`. The principal is attached to an `ICurrentRestaurantProvider` scope for the duration of `Consume`.
- **Hardcoded 4★/5★ logic per Q6:**
  ```csharp
  public async Task Consume(ConsumeContext<FeedbackSubmittedIntegrationEvent> context)
  {
      var evt = context.Message;
      if (evt.OverallRating >= 4 && evt.OverallRating < 5)
          await _handler.CreateAsync(new RewardCode { RewardType = Percentage, Value = 10, ... }, context.CancellationToken);
      else if (evt.OverallRating >= 5)
      {
          await _handler.CreateAsync(new RewardCode { RewardType = Percentage, Value = 15, ... }, context.CancellationToken);
          await _handler.CreateAsync(new RewardCode { RewardType = FreeItem, Value = "appetizer", ... }, context.CancellationToken);
      }
      await context.Publish(new DiscountHistoryAppendedIntegrationEvent(EntityType: "RewardCode", ChangeType: "Created", ...));
  }
  ```
- **Disabled by default** via `DiscountOptions:EnableFeedbackSubmittedConsumer=false`. `services.AddMassTransit(config => config.AddConsumer<FeedbackSubmittedConsumer>();...)` plus a `IConsumerDefinition<FeedbackSubmittedConsumer>` whose endpoint config fetches the option and toggles:
  ```csharp
  public class FeedbackSubmittedConsumerDefinition : ConsumerDefinition<FeedbackSubmittedConsumer>
  {
      private readonly bool _enabled;
      public FeedbackSubmittedConsumerDefinition(IOptions<DiscountOptions> options)
          => _enabled = options.Value.EnableFeedbackSubmittedConsumer;
      protected override void ConfigureConsumer(IReceiveEndpointConfigurator configurator)
      {
          if (!_enabled) configurator.DisableConsumer<FeedbackSubmittedConsumer>(this);
      }
  }
  ```
- **Tests:** `InMemoryTestHarness` — when the flag is true, fire `FeedbackSubmittedIntegrationEvent` with `OverallRating=5`, `RestaurantId=seed-rid`, assert two `RewardCode` rows are written. The `BusTestHarness` config also asserts the publish path on `DiscountHistoryAppendedIntegrationEvent`.

**Doc-update scope (§0.2):**
- §4.4 Discount Service — add the `FeedbackSubmittedConsumer` row under "Consumers" with the flag note.
- §5.2 Asynchronous — add `FeedbackSubmittedIntegrationEvent` row (publisher = Notification v1, planned).
- §9 Cross-Cutting Patterns — note the `ConsumerDefinition<>` flag-gating pattern as the project's reference for "wire to the bus, ship disabled."

### Phase 6 — Architecture events deferred (documentation + emit-when-wiring)

This phase is **mostly documentation** with two small code touchpoints.

- **`DiscountAppliedIntegrationEvent` publication point** in `RedeemDiscount`:
  ```csharp
  if (_options.EnableDiscountAppliedPublishing)
      await _outbox.WriteOutboxMessageAsync(new DiscountAppliedIntegrationEvent(...));
  ```
  Default `false` until a real consumer appears. Flag-gated.
- **`RewardGeneratedIntegrationEvent` publication point** in `RewardCode.CreateRewardCode`:
  ```csharp
  if (_options.EnableRewardGeneratedPublishing)
      await _outbox.WriteOutboxMessageAsync(new RewardGeneratedIntegrationEvent(...));
  ```
- **`RewardRedeemedIntegrationEvent` publication point** in `RedeemRewardCode`.
- **Prose note in `current-architecture.md` §5.2** describing the three events as "documented but unmaintained consumer". Future plans update when consumers arrive.

**Doc-update scope (§0.2):**
- §4.4 Discount Service — under "Events Published", the three events get an explicit "deferred until consumer exists" note + the corresponding `DiscountOptions:Enable*Publishing=false` default.
- §5.2 Asynchronous — the three publish rows get the deferred marker.
- §9 Cross-Cutting Patterns — note the "publish-points-wired-but-flagged-off" idiom.

### Phase 7 — Hardening (final doc + tests + drift cleanup)

- **Drift memo:** rewrite the `Discount` chapter of `db-model-drift-reports.md` to reflect post-Phase-3 reality (`Coupons` matches code; `RewardCodes` + `DiscountRules` matches code; outbox tables match code).
- **Mermaid updates:** add `RewardCodes` and `DiscountRules` blocks under the CATALOG block (Discount mermaid lives in `db_relational_model.mermaid` under that header even though Discount owns the tables — same authoring pattern as Catalog's NotificationLog residue).
- **`/ready` probe config knob:** confirm `DiscountOptions:OutboxDeadLetterThreshold` is `ValidateOnStart()`; verify an unreachable RabbitMQ flips `/ready` to unready in tests.
- **All Phase 1–6 doc-update scopes audited.** Verifies every phase's checklist box is matched by a committed doc change.
- **Final smoke test:** 0 warnings / 0 errors; full test pyramid (unit + SQLite-in-mem + InMemoryHarness) green; SQL migrations apply cleanly; build for both Debug and Release configurations.

**Doc-update scope (§0.2):** everything touched by Phases 1–6 audited for consistency. The drift memo is the canonical post-Phase-7 reference.

---

## 8. Cross-cutting notes

### Cross-service coordination rules (mirror Catalog §8)

- **Event versioning.** Every Discount integration event carries `int SchemaVersion` (current = 1). Adding fields bumps the version. Removing or renaming a field requires introducing the next major version side-by-side, publishing both for one release, then dropping the old version. Documented in §6.5.
- **Cascade-delete policy.** All FKs use `OnDelete(DeleteBehavior.Restrict)`. Soft-delete only. Application layer raises a friendly 409 via `StatusCode.FailedPrecondition` when a delete is blocked. The conditional UPDATE race-fix in `RedeemDiscount` is one example.
- **Outbox ownership.** Discount owns its own `outbox_messages` table. Cross-cutting schema changes (column rename on a shared FK; new required column referenced by another service) land first in Discount, then consumer read paths update, then a coordinated migration script is documented in the commit message.
- **Cache failure policy.** N/A — per Q12, no cache.
- **Health check policy.** `/live` for liveness only. `/ready` checks SQLite file + RabbitMQ + outbox dead-letter count. Tripping any takes Discount out of the LB. Threshold is config; default = 0.
- **Tenant enforcement.** Every entity implements `ITenantEntity`. Every `DbSet` query goes through the global query filter. `.IgnoreQueryFilters()` is a forbidden pattern unless inside an explicit `if (caller.IsSuperAdmin)` branch — checked at code review.
- **Bus consumer auth.** Pattern 2: synthetic `ClaimsPrincipal` from event payload. No `[Authorize]` on consumers (they don't have an HTTP context); trust boundary = the bus + the event signature.

### Cross-cutting SQLite notes

- **Single-replica assumption** — Discount ships with one process; the outbox dispatcher relies on SQLite's database-level write lock to prevent duplicate publishes. Multi-replica on the same SQLite file is a degradation: writers serialize, latency goes up, FIFO ordering is preserved. Document this in `current-architecture.md §11 Local Development`.
- **Migration idempotency** — `dotnet ef database update` and `dotnet ef migrations script` both work. SQLite's textual migration file format means a typo'd column type can render the migration harmless but the runtime column-attribute mismatch confusing. Always run `dotnet ef migrations build` (or `dotnet build`) after `migrations add`.

### Cross-cutting BuildingBlocks contributions

This plan ships **two** BuildingBlocks contributions:

1. **First SQLite `OutboxDispatcher<TContext>` implementation** — `DiscountOutboxDispatcher` in `Services/Discount/Discount.Grpc/Messaging/Outbox/` extends `OutboxDispatcher<DiscountContext>`. The `BuildClaimSql` SQLite variant is documented in §6.7. Future services on SQLite adopt the same pattern.
2. **`ITenantEntity.RestaurantId : int → Guid` fix** — single-line change in `BuildingBlocks/Multitenancy/ITenantEntity.cs`. Justifies the dormant primitive; nothing implements `ITenantEntity` today; Discount becomes the first.

Both contributions are scoped to this plan's PR. Subsequent services adopt them as references.

### Code-smell carryovers (none today)

Unlike Catalog's `db_relational_model.md §137-148` carry-over list (`BasketItem.MenuItemId int vs Guid`, four Marten docs extending `Entity<int>`, `BulkOrderUploads.CreatedAt` missing), Discount has **no existing drift** beyond the `ITenantEntity.RestaurantId : int → Guid` fix in Phase 1. Every other drift (e.g., seeded sample `DISCOUNT10` / `DISCOUNT20` for fictitious restaurant GUIDs) is by-design dev seeding.

### Testing strategy

- **Unit tests** (xUnit + FluentAssertions + NSubstitute): pure logic — handler happy paths, validation, the lazy-eval gate. No infrastructure. Fast.
- **SQLite `:memory:` integration** for the outbox dispatcher (claim SQL roundtrip), the lazy-eval gate (`UpdateSQLiteDatabase(); insert coupon with past expiry; assert Coupon.IsActiveNow returns false`), the sweep service (fake `TimeProvider`), and the race-fix conditional UPDATE pattern.
- **`MassTransit.InMemoryTestHarness`** for `FeedbackSubmittedConsumer`, `MenuItemChangedConsumer`, `RestaurantConfigurationChangedConsumer`. The InMemoryHarness config is registered per-test and asserts both consumer dispatch and the publish-side `DiscountHistoryAppendedIntegrationEvent` end-to-end (the publisher's handler is a no-op stub that records the message; the test asserts its presence and shape).
- **NSubstitute for handler tests** that don't need real infrastructure (pure JWT-claim scenarios).

### Observability

- **Per-RPC latency / hit / fail counters** on `/ready` under the `discount.rpcs` key.
- **Outbox dead-letter count** under `outbox.dead_message_count` (and via `/ready`).
- **Sweep service counters** under `discount.sweeps.last_run_at`, `discount.sweeps.rows_flipped_total`.
- **Serilog correlation-id enrichment** flows from inbound gRPC `Metadata["x-correlation-id"]` → handler scope → outbox row → MassTransit header → consumer's log scope.

### Migration / rollout

- **Phases 1–5 are additive** and feature-flag gated where they touch the bus. Each can ship behind its flag and roll back without DB changes.
- **Phase 6 is doc-only.** No code beyond three single-line `if (_options.Enable*Publishing)` guards.
- **Phase 7 is also doc-only** + the drift memo + the final test pass.
- **First migration** lands in Phase 1 with the outbox tables + `claim_id` column. Each subsequent phase ships its own migration.

### Resolved architectural decisions

These were settled during the initial grilling and locked at the top of this plan (§0 preamble).

| # | Question | Locked answer | Source |
|---|---|---|---|
| 1–13 | (see table at top) | (see table at top) | Preamble |

---

## 9. Milestone checklist

> Every phase entry has **three** check-boxes: the code/test gate, the `current-architecture.md` doc-update gate (§0.2), **and** the completion gate (this plan file updated per §9.1 step 9 — Document Version bumped, changelog entry appended, §9 check-boxes ticked). A phase is not "done" until all three are committed.

- [ ] **Phase 1** — Production-grade Coupon: `ITenantEntity` int→Guid fix shipped in BuildingBlocks; `Coupon : ITenantEntity`; global query filter active; JWT auth wired; 11 permission policies; outbox tables + first SQLite `OutboxDispatcher<DiscountContext>`; lazy-eval gate; sweep service; `RedeemDiscount` race fix; `/live`+`/ready` split.
  - [ ] **Phase 1 doc** — `current-architecture.md` updated per Phase 1 doc-update scope.
  - [ ] **Phase 1 completed** — dev, doc commit, plan-update commit (Document Version bump + v1.1 changelog) all landed.
- [ ] **Phase 2** — `DiscountRule` aggregate with 6 RPCs; `MenuItemChangedConsumer` + `RestaurantConfigurationChangedConsumer`; rule FK to Coupon.
  - [ ] **Phase 2 doc** — `current-architecture.md` updated.
  - [ ] **Phase 2 completed** — dev, doc, plan-update commit.
- [ ] **Phase 3** — `RewardCode` aggregate with 6 RPCs; race-fix `RedeemRewardCode`; sweep extension; lazy-eval + tenant filter.
  - [ ] **Phase 3 doc** — `current-architecture.md` updated.
  - [ ] **Phase 3 completed** — dev, doc, plan-update commit.
- [ ] **Phase 4** — `DiscountHistoryAppendedIntegrationEvent` publish points populated for Coupon / RewardCode / DiscountRule handlers. One outbox row per mutation.
  - [ ] **Phase 4 doc** — `current-architecture.md` updated.
  - [ ] **Phase 4 completed** — dev, doc, plan-update commit.
- [ ] **Phase 5** — `FeedbackSubmittedConsumer` stub wired-but-disabled; flag-gated `IConsumerDefinition<>`; tests with `InMemoryTestHarness`.
  - [ ] **Phase 5 doc** — `current-architecture.md` updated.
  - [ ] **Phase 5 completed** — dev, doc, plan-update commit.
- [ ] **Phase 6** — Three architecture publishes documented and gated by flags; no consumer arrives in this plan.
  - [ ] **Phase 6 doc** — `current-architecture.md` updated.
  - [ ] **Phase 6 completed** — dev, doc, plan-update commit.
- [ ] **Phase 7** — Drift memo rewrite; mermaid `RewardCodes` / `DiscountRules` blocks added; all Phase 1–6 docs audited; final smoke test green.
  - [ ] **Phase 7 doc** — `current-architecture.md` updated.
  - [ ] **Phase 7 completed** — dev, doc, plan-update commit (Document Version bump + v1.X+1 changelog).
- [ ] **Docs** — `db_relational_model.mermaid` updated to match each phase (mermaid is reconciled after every phase, not only at the end).
- [ ] **Cross-plan sync** — Catalog plan receives §6.6.1 hand-off contract (already documented here); Identity plan receives §6.6.2 permission list; Notification v1 plan receives §6.6.3 `FeedbackSubmitted` contract.

### 9.1 Per-phase implementation recipe

For reproducibility, every phase's commit follows this sequence (the `csharp-developer` skill is loaded at step 1 per §0.1):

1. **Load skill.** Invoke `/csharp-developer`. Load the relevant `references/*.md` files for the phase.
2. **Design.** Read the phase section above; map gRPC RPCs → proto files, handlers → primary-constructor service classes, events → MassTransit. Confirm no naming conflict with existing protos / handlers.
3. **Implement.** Domain models, EF migrations, handler + validator + protobuf message + DI registration + policy attributes. Follow csharp-developer MUST DO/MUST NOT DO list.
4. **EF Core checkpoint.** Run `dotnet ef migrations add <Name>` from `Services/Discount/Discount.Grpc/`. Review the generated SQL; if it contains unintended drops, roll back with `dotnet ef migrations remove` and fix the model.
5. **Test.** xUnit + FluentAssertions + NSubstitute unit; SQLite-in-memory integration; MassTransit InMemoryTestHarness.
6. **Update `current-architecture.md`.** Apply the phase's Doc-update scope verbatim. Commit it alongside the code commit before the phase is marked complete.
7. **Update `db_relational_model.mermaid`.** If the phase touched the schema, reconcile the mermaid to code (project convention).
8. **Land.** Phase is "done" only when both the code commit and the doc commit have landed and the checklist boxes in §9 above are ticked.
9. **Plan update.** Bump Document Version. Append a v1.X+1 changelog entry. Tick the phase's three checklist boxes.

---

**Document Version:** 1.0
**Last Updated:** 2026-07-11
**Maintained By:** Discount working group

> **v1.0 changelog — Initial plan.**
>
> **Scope locked at scope-defining grilling on plan date.** 13 design questions resolved and recorded in the preamble; 7 phases laid out. The plan's `current-architecture.md` baseline is the snapshot from 2026-07-11, where Discount has 1 entity (Coupon), 5 RPCs, no auth, no tenancy enforcement, no outbox, no cache, no events, no tests. Phase 1's checkpoint is the inflection point: a 1-entity, gRPC-only, anonymous service becomes the canonical reference for "production-grade cross-cutting primitives" in the project, contributing two BuildingBlocks changes (first SQLite outbox claim SQL; `ITenantEntity.RestaurantId int → Guid`).
>
> **Key divergences from architecture.md:** the four-aggregate split (`Discounts`/`PromoCodes`/`RewardCodes`/`DiscountRules`) is collapsed to three aggregates (`Coupon` collapses `Discount`+`PromoCode`; `RewardCodes` and `DiscountRules` are net-new as designed). Architecture rename to `Discounts`/`PromoCodes` is tracked in §3 *Out of scope*. The architecture's two consumes (`FeedbackSubmitted`, `OrderCreated`) are largely stub-and-flag because their publishers (Notification v1, Ordering plan) don't ship in this plan's window — the bus wiring is ready, the consumers are wired-but-disabled.
>
> **Cross-service hand-offs documented:**
> §6.6.1 — Catalog side owns `EntityHistoryArchive` Marten document + `DiscountHistoryAppendedIntegrationEvent` consumer.
> §6.6.2 — Identity side owns eleven new `Permissions` rows + RolePermission mappings.
> §6.6.3 — Notification v1 side owns the publisher for `FeedbackSubmittedIntegrationEvent`.
>
> No cross-plan code changes ship with Discount's plan; the docs land here so siblings can plan against the contract.

For the schema-level drift baseline, see `db-model-drift-reports.md` (Discount chapter rewritten in Phase 7) and the project convention `mermaid-code-review-convention.md`.
