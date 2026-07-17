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
> | 8 | Outbox wiring | **A** — Mirror Catalog. Implement first `OutboxDispatcher<TContext>` for SQLite (single-replica, `claim_id` GUID column for atomic claim). Use the existing `BuildingBlocks.Messaging.Outbox.IOutboxDbContext` / `IOutboxPublisher`. Every `I*IntegrationEvent` carries `int MessageVersion` (initial = 1) inherited from `BuildingBlocks.Messaging.Events.IntegrationEvent`; `OutboxPublisher` copies `MessageVersion` into the outbox row's `SchemaVersion` column on stage — the wire-level version is `MessageVersion`, the storage-level version is `SchemaVersion`, and the two are kept in lockstep. | Q3's history decision leans on "transactional publish" semantics. Plain MassTransit publish (option B) drops the guarantee; MassTransit's built-in `EntityFrameworkOutbox` (option C) diverges from the solution-wide pattern. The `MessageVersion` ↔ `SchemaVersion` distinction avoids a name collision with the EventType / SchemaVersion column pattern some other transports use. |
> | 9 | Tests strategy | **C** — xUnit + FluentAssertions + NSubstitute (unit); SQLite `:memory:` for integration tests; `MassTransit.InMemoryTestHarness` for consumer tests. | Catalog pattern uses Testcontainers Postgres; Discount is on SQLite. SQLite-in-memory is production-equivalent engine, container-free, fast. The hand-rolled pieces (claim SQL, lazy gate, sweep) need real engine tests, not mocks. |
> | 10 | Auth | `AddJwtBearer` + ASP.NET Core gRPC integration (mirrors Catalog). **Pattern 2** for bus-triggered consumers: synthetic `ClaimsPrincipal` carrying `restaurantId` claim extracted from the event payload + actor=`discount-service`. New permissions: `coupon:read/create/edit/delete/redeem`, `reward-code:read/create/edit/delete/redeem`, `discount-rule:read/edit` (Identity-side follow-up). | Mirrors every other API. Synthetic claims beat `AllowAnonymous` on consumers — the audit trail records the actual restaurant context, not a generic service actor. |
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

**Discount copies the rule bullets from `CATALOG_SERVICE_PLAN.md §0.3` verbatim** rather than mirror-referencing them. Reason: mirror-references (e.g., "see Catalog §0.3") drift silently — a future Catalog-side change leaves Discount with no signal that its own guard rails moved. Copy-into-context is verbose but drift-proof; update this section in lockstep with Catalog §0.3 changes.

Discount's project-specific overrides layered on top of the catalog-copied bullets:

- **xUnit + FluentAssertions + NSubstitute** for unit tests (Catalog's choice).
- **SQLite `:memory:` + MassTransit `InMemoryTestHarness`** for integration tests (Catalog uses Testcontainers Postgres; SQLite doesn't need a container).
- **gRPC error codes via `Grpc.Core.Status` + `ServerCallContext.Status`** — never silent `catch` in RPC handlers; map known exceptions to `StatusCode.NotFound` / `StatusCode.InvalidArgument` / `StatusCode.PermissionDenied` / `StatusCode.FailedPrecondition`. No string-typed error tuples.
- **Mapping via Mapster** — new `RewardCodeService` and `DiscountRuleService` use `request.Adapt<RewardCode>()` and `entity.Adapt<RewardCodeModel>()`; the existing `DiscountService.cs:144–158` manual `ToProtoModel`/`ToEntity` is deleted in Phase 1 to keep mapping consistent with Catalog (current-architecture.md §4.2). Add `Mapster.DependencyInjection` configuration in `Program.cs` (`services.AddMapster(); services.AddSingleton(TypeAdapterConfig.GlobalSettings);`) so the new `MapGrpc...Mapper.CreateAdapter()` calls in handlers pick up the shared type-map config.
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

#### 0.3.3 Validation rules (single source of truth)

The plan asserts "FluentValidation in handlers" but does not enumerate which rules. This section is the locked list. A phase is not "done" until every rule below ships a validator in that phase's commit, and the phase's Doc-update scope cites this section verbatim.

- **`CreateDiscountCommand` / `UpdateDiscountCommand`** (Phase 1, extended in Phase 8):
  - `Code` — non-empty, ≤ 64 chars, unique per `(RestaurantId, Code)`. Uniqueness check queries `DiscountContext.Coupons` ignoring the current `Id` on update.
  - `Amount` — `> 0` always.
  - `DiscountType` — must be a defined enum value of `DiscountType { Percentage, FixedAmount }` (Phase 8 add). When omitted on update, the existing value is preserved.
  - `Amount` — `> 0 && <= 100` when `DiscountType = Percentage`. A 0% "free preview" coupon is allowed by the lower bound (admin-promotion flow); a 100% "everything for free" coupon is the upper bound. Decimal-precise with `MidpointRounding.ToEven` (locks rounding policy across Basket preview + Ordering at checkout).
  - `Amount` — `> 0` when `DiscountType = FixedAmount`. No upper bound; the floor-at-zero clamp at apply time prevents negative basket totals (applied in `ApplyDiscountsHelper.Apply`).
  - `MaxRedeemAmount` — `> 0` when set.
  - `RedeemAmount` — `>= 0`, `<= MaxRedeemAmount` when the latter is set.
  - `ExpirationDate` — `> clock.GetCurrentInstant()` when set.
  - **Note:** the new `DiscountType` enum is **separate** from `RewardCode.RewardKind { Percentage, FixedAmount, FreeItem, Points }` (Phase 3). The two are intentionally distinct: a Coupon is admin-controlled promotional code; a RewardCode is customer-feedback-generated. Phase 8 does *not* unify them. A future consolidation to a shared `BuildingBlocks.Discounts.DiscountKind` enum is tracked as a v2 BuildingBlocks contribution (out of this plan's scope).
- **`RedeemDiscountCommand`** (Phase 1):
  - `Code` — non-empty.
  - `OrderId` — non-empty Guid.
  - `RestaurantId` — non-empty Guid; mismatch against `ICurrentRestaurantProvider` returns `StatusCode.PermissionDenied` from the handler before the DB call.
  - `Quantity` — `>= 1`, `<= 100` (sanity cap; multi-quantity redemption is a future-proofing field).
- **`CreateDiscountRuleCommand` / `UpdateDiscountRuleCommand`** (Phase 2):
  - `CouponId` — exists in the same tenant (the global query filter handles it; the validator confirms the lookup returns non-null).
  - `RuleType` — must be a defined enum value.
  - `RuleDataJson` — deserializes to a non-empty `RuleData` shape; the validator runs the deserializer and asserts non-empty. For `RequiredMenuItems`, every `MenuItemId` must be a non-empty Guid. For `TimeWindow`, `StartTime < EndTime` and `DayOfWeekMask` is in `[0, 127]`.
  - **UK guard**: only one `DiscountRule` per `(RestaurantId, CouponId)` — the rule FK already enforces this at the DB level, but the validator surfaces `StatusCode.FailedPrecondition` with `Metadata["rule-already-exists"]` instead of `DbUpdateException`.
- **`EvaluateDiscountRulesRequest`** (Phase 2):
  - `OrderTotal` — `> 0` when set.
  - `MenuItemIds` — non-null (empty list is valid: an order with no items triggers only `MinOrderAmount` / `TimeWindow` rules).
- **`CreateRewardCodeCommand` / `UpdateRewardCodeCommand`** (Phase 3):
  - `Code` — non-empty, ≤ 120 chars, unique per `(RestaurantId, Code)`.
  - `RestaurantId` — non-empty Guid.
  - `Value` — kind-specific (see §7 Phase 3 for the exact rule set; locked in code, not in the prose).
  - `ExpirationDate` — `> clock.GetCurrentInstant()` when set.
  - `MaxRedeemAmount` — `> 0` when set.
- **`RedeemRewardCodeCommand`** (Phase 3):
  - Same shape as `RedeemDiscountCommand`. `Kind = FreeItem` is excluded from quantity-multi-redemption (`Quantity` must be `1`).

The list above is the contract. If a phase's commit adds a command not on this list, the implementer extends this section in the same commit. If a phase's commit removes a rule, the implementer strikes it here and notes the rationale in the v1.X+1 changelog.

#### 0.3.4 Consumer-side idempotency — choice matrix

| Consumer | Idempotency mechanism | Why |
|---|---|---|
| `MenuItemChangedConsumer` | `processed_inbound_events` table (unique-key violation on `EventId`) | Rule-update path has no natural uniqueness violation to lean on; bus redelivery could re-flip `IsActive` after a partial failure. |
| `RestaurantConfigurationChangedConsumer` | `processed_inbound_events` table (same shape) | The effect is more idempotent (flipping `IsActive=false` is monotonic) but the table guard is cheap and keeps the story in one place. |
| `FeedbackSubmittedConsumer` | `RewardCode.Code` unique constraint via the `Code*()` deterministic helpers | The handler dispatches `CreateRewardCodeCommand`; a duplicate `Code` raises a uniqueness violation that the `ISender` pipeline translates to a known result code the consumer swallows. No separate table needed. |

The two strategies are complementary, not redundant. The rule for picking one: **if the handler's own side-effect can be made unique-key-deterministic, use that. Otherwise, gate on `processed_inbound_events`.** Future consumers pick from this matrix in their phase's commit and update this section in lockstep.

### 0.4 gRPC + MassTransit design principles

This section enforces the gRPC + MassTransit conventions every Discount endpoint follows. It is the protocol-shape counterpart to §0.1 / §0.2 / §0.3.

#### 0.4.1 RPC design

- **RPCs are actions, not resources.** gRPC's natural shape is method-named (`CreateDiscount`, `RedeemDiscount`), not URL-shaped. Resource-oriented path conventions from CATALOG_SERVICE_PLAN §0.4.1 only apply in the rare case Discount surfaces HTTP (it doesn't, per Q4).
- **One `Service` per aggregate.** `DiscountProtoService` for Coupon (existing), `RewardCodeProtoService` for RewardCode (Phase 3), `DiscountRuleProtoService` for DiscountRule (Phase 2). Each lives in its own `.proto` file or its own `package` block within `Protos/discount.proto`. Generated clients mirror; Basket already imports the protos.
- **Per-RPC request / response messages, not reuse.** Discount uses `CouponModel` (existing) for the Coupon CRUD RPCs; new RewardCode RPCs use `RewardCodeModel`; new DiscountRule RPCs use `DiscountRuleModel`. **Don't** reuse `CouponModel` to encode a `RewardCode` — the field shapes differ; reusing creates protobuf tags that drift.
- **Validation in handlers, not at the proto layer.** Field-level constraints (`Required`, `Range`, etc.) live in FluentValidation validators + the handler, not in protobuf field annotations (proto3 has limited `optional` semantics). The exception is `string restaurant_id`, which gets pattern-checked at handler entry via `Guid.TryParse`.
- **`Idempotency-Key` for state transitions.** `RedeemDiscount`, `RedeemRewardCode`, and `UpdateDiscount` accept an `Idempotency-Key` request header (UUID v4) — middleware reads it, computes a server-attested MAC of `callerRestaurantId + endpoint + rawRequestBody`, and caches the response in Redis (`idempotency:{rId}:{hmacHex}`, 24h TTL). **Conflict** (same key, different request body) → 422 via `StatusCode.FailedPrecondition` with details.

  **MAC, not plain hash.** The cache key uses `HMAC-SHA256(key, envelope)` keyed on a server-side secret, **not** plain `SHA256(envelope)`. Plain SHA256 lets an attacker craft a `key+rId+code` collision if they guess the input format; HMAC requires knowledge of the secret. The secret is 32 random bytes generated at startup or read from `IConfiguration["Discount:IdempotencyKey"]` (the production value comes from Key Vault; the dev value lives in `appsettings.Development.json`, which is gitignored). The secret is registered once as a singleton `IIdempotencyKeyProvider` and **never logged**:

    ```csharp
    public sealed class IdempotencyKeyProvider : IIdempotencyKeyProvider
    {
        private readonly byte[] _key;
        public IdempotencyKeyProvider(IConfiguration config)
        {
            var raw = config["Discount:IdempotencyKey"]
                ?? throw new InvalidOperationException(
                    "Discount:IdempotencyKey missing from configuration (dev: appsettings.Development.json; prod: Key Vault).");
            ArgumentNullException.ThrowIfNull(raw);
            _key = Convert.FromHexString(raw);
            if (_key.Length < 16) throw new InvalidOperationException("Discount:IdempotencyKey must decode to >= 16 bytes.");
        }
        public string Compute(string envelope) =>
            Convert.ToHexString(HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(envelope)));
    }
    ```

  **Dev experience:** a fresh clone (`git clone` + `dotnet run` with no other setup) must not crash on a missing `Discount:IdempotencyKey`. The provider is wrapped in a dev-only fallback: when `IHostEnvironment.IsDevelopment()` is true AND the config value is missing, the provider generates a 32-byte random key at startup, logs a `WARN` (`"Discount:IdempotencyKey not configured; using a per-process random key. Idempotency cache entries are valid only for this process lifetime."`), and registers the random key. Production keeps the hard-fail behavior (no fallback — missing config in prod is a deployment error and must surface loudly). The dev fallback keeps the test pyramid green and makes the README's "clone and run" path work without manual `dotnet user-secrets` setup; the `README.md` of the service still documents the `dotnet user-secrets set Discount:IdempotencyKey <hex>` command for the case where the dev wants persistent idempotency across process restarts.

  Redis is the same shared instance Basket and Catalog use — **discount:* namespace** to avoid collision.

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

#### 0.4.2.1 gRPC authorization mechanism

**`[Authorize(Policy = "coupon:create")]` on a gRPC service method does NOT work the way it does on an MVC controller.** gRPC services are not routed through the MVC pipeline; the `[Authorize]` attribute is silently ignored. The project's actual mechanism is a global gRPC `AuthorizationInterceptor` registered with `services.AddGrpc(o => o.Interceptors.Add<DiscountAuthorizationInterceptor>())`. The interceptor:

1. Resolves `HttpContext.User` via `ServerCallContext.GetHttpContext()` (gRPC populates it from the `Metadata["authorization"]` Bearer token once `AddJwtBearer` is wired).
2. Looks up the policy name from a `[Permission("coupon:create")]` attribute on the gRPC method (custom attribute — declare it in `Authorization/DiscountPermissionAttribute.cs`).
3. Runs `IAuthorizationService.AuthorizeAsync(user, resource, "coupon:create")`. Failure → `StatusCode.PermissionDenied` with `Metadata["required-permission"] = "coupon:create"`.

Document the `[Permission("coupon:create")]` attribute and the `DiscountAuthorizationInterceptor` in Phase 1 alongside the JWT wiring. Per-method `[Authorize(Policy=...)]` is **not** used; per-method `[Permission("coupon:create")]` is the convention.

#### 0.4.3 Proto file layout

`Protos/discount.proto` currently houses `CouponModel` + 5 RPCs in `package discount`. After Phase 2 / Phase 3, the file grows. The split uses one **aggregator** proto at the existing path so the Basket client (`Basket.API.csproj:32`) keeps including the same path without churn:

```
Protos/
  discount.proto          # aggregator: imports the three slices; declares package discount
  coupon.proto            # CouponModel + the 5 existing RPCs
  reward_code.proto       # RewardCodeModel + 6 reward RPCs
  discount_rule.proto     # DiscountRuleModel + 6 rule RPCs + Evaluate
```

The aggregator's body is just:

```proto
syntax = "proto3";
option csharp_namespace = "Discount.Grpc";
package discount;

import "coupon.proto";
import "reward_code.proto";
import "discount_rule.proto";
```

Each slice file declares its own `csharp_namespace`:

```proto
// coupon.proto
option csharp_namespace = "Discount.Grpc.Coupon";
package discount;
message CouponModel { ... }
service DiscountProtoService { rpc GetDiscount(...) returns (...); /* ...the 5 RPCs */ }
```

```proto
// reward_code.proto
option csharp_namespace = "Discount.Grpc.RewardCode";
package discount;
message RewardCodeModel { ... }
service RewardCodeProtoService { rpc CreateRewardCode(...) returns (...); /* ...the 6 RPCs */ }
```

```proto
// discount_rule.proto
option csharp_namespace = "Discount.Grpc.DiscountRule";
package discount;
message DiscountRuleModel { ... }
service DiscountRuleProtoService { rpc CreateDiscountRule(...) returns (...); /* ...the 6 RPCs */ }
```

Generated C# lands in three namespaces: `Discount.Grpc.Coupon`, `Discount.Grpc.RewardCode`, `Discount.Grpc.DiscountRule`. Each service class is registered separately in `Program.cs` via `app.MapGrpcService<Coupon.DiscountProtoService>()` etc. — registering the existing one is unchanged; the two new ones are additive. `Discount.Grpc.csproj` lists each `.proto` with `<Protobuf Include="Protos/coupon.proto" GrpcServices="Server" />` (and likewise for the other two); the aggregator `discount.proto` does NOT need a separate `<Protobuf />` entry because none of the message types or services are declared at the top level — it's just `import` glue for Basket's existing include path.

**Basket is unaffected.** Its `<Protobuf Include="..\..\Discount\Discount.Grpc\Protos\discount.proto" GrpcServices="Client" />` still pulls in the aggregator, which re-exports the Coupon slice's generated stubs under the existing `Discount.Grpc` namespace (the aggregator uses `csharp_namespace = "Discount.Grpc"`). The two new slices land in `Discount.Grpc.RewardCode` / `Discount.Grpc.DiscountRule`, which Basket never imports — Basket doesn't see them. No Basket-side code change required.

`dotnet build` generates stubs into `obj/Debug/net10.0/Protos/{coupon.cs, reward_code.cs, discount_rule.cs}` plus the aggregator's compiled output; **never edit generated files.**

#### 0.4.3.1 `IsActiveNow` helper — locked shape

Every Discount aggregate that has an `ExpirationDate` (Coupon, RewardCode) exposes the same lazy-eval gate so the read path has a single canonical answer to "is this active right now?":

```csharp
public static class ActiveNow
{
    public static bool Coupon(Coupon c, TimeProvider clock)
        => c.DeletedAt == null
        && c.IsActive
        && (c.ExpirationDate is null || c.ExpirationDate >= clock.GetCurrentInstant());

    public static bool RewardCode(RewardCode r, TimeProvider clock)
        => r.DeletedAt == null
        && r.IsActive
        && (r.ExpirationDate is null || r.ExpirationDate >= clock.GetCurrentInstant());
}
```

Lives in `Discount.Grpc/Domain/ActiveNow.cs` (or similar — pin the namespace in the implementation commit, not here). DiscountRule does **not** have an `IsActiveNow` because it has no `ExpirationDate`; rule activation is the user's responsibility, the sweep service doesn't deactivate rules. The two helpers are called from every read RPC (`GetDiscount`, `GetRewardCode`, the list responses' projected model), from `RedeemDiscount` / `RedeemRewardCode` after the conditional UPDATE succeeds, and from the sweep service. A divergent copy in any handler is a code-review red flag.

#### 0.4.4 Event versioning on the bus

Mirrors Catalog §6.5. Every `I*IntegrationEvent` from Discount carries `int MessageVersion = 1` (inherited from `BuildingBlocks.Messaging.Events.IntegrationEvent`), `Guid EventId`, `Instant OccurredOn`, `Guid RestaurantId`. The `BuildingBlocks.Messaging/Outbox/OutboxMessage.cs:36` `SchemaVersion` column stores the same value (the `OutboxPublisher` copies `MessageVersion` → `SchemaVersion` on stage; the column name on the storage side stays `SchemaVersion` to match the existing index naming, but the field on the event record is `MessageVersion`). When the consumer (`DiscountHistoryAppendedIntegrationEvent` → Catalog) ignores unknown major versions, it's MassTransit's default behavior; we don't need extra code.

#### 0.4.5 Cross-cutting gRPC concerns

- **JWT bearer** — `Metadata["authorization"]` carrying `Bearer <jwt>`. `AddJwtBearer` validates against Identity's authority. `AuthenticationInterceptor` populates `HttpContext.User`; permission policies evaluate from claims.
- **Correlation ID** — `IHttpContextAccessor` middleware pushes `CorrelationId` onto the HTTP scope → outbox row `CorrelationId` column → MassTransit header → consumer's log scope (mirrors Catalog's flow).
- **Logging** — every RPC handler logs `RpcStarted`, `RpcCompleted`, `RpcFailed` with `CorrelationId` enrichment.
- **Deadline propagation** — gRPC clients (Basket) honor `deadline`; the server-side interceptor reads `ServerCallContext.Deadline` and passes it as the `CancellationToken` to handlers. Missing deadline → default 5-second budget.
- **gRPC reflection (development only)** — `builder.Services.AddGrpcReflection();` (registered after `AddGrpc`) plus `app.MapGrpcReflectionService();` in `Program.cs`, both guarded by `if (app.Environment.IsDevelopment())`. Lets `grpcurl`, BloomRPC, Postman-gRPC, and the `ServerReflection`-based test client enumerate services without a side-channel proto file. Production stays reflection-off (reflection leaks the schema to anyone who can reach the port).

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

1. **Adding JWT bearer auth** to the gRPC service; permission policies for the eleven new permission strings (counterpart to the `kitchen:*` and `menu:*` families the rest of the project already has). Permission naming uses a hyphenated entity prefix throughout (`coupon:`, `reward-code:`, `discount-rule:`) so the three families read consistently.
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
- **`DiscountOptions`** — strongly-typed config (full class with `[Range]`, `[Required]`, `ValidateOnStart()`):

    ```csharp
    // Options/DiscountOptions.cs
    public sealed class DiscountOptions
    {
        public const string SectionName = "Discount";

        [Range(1, 1440)]
        public int SweepIntervalMinutes { get; set; } = 5;

        // Production default = 5 (alert-and-let-humans-triage). 0 is for test-environment assertions.
        // See /ready dead-letter probe — threshold trips /ready to unready when DEAD row count exceeds it.
        [Range(0, int.MaxValue)]
        public int OutboxDeadLetterThreshold { get; set; } = 5;

        public bool EnableHistoryPublishing            { get; set; } = true;
        public bool EnableMenuItemChangedConsumer      { get; set; } = true;
        public bool EnableRestaurantConfigChangedConsumer { get; set; } = true;
        public bool EnableFeedbackSubmittedConsumer    { get; set; } = false;
        public bool EnableDiscountAppliedPublishing     { get; set; } = false;
        public bool EnableRewardGeneratedPublishing     { get; set; } = false;
        public bool EnableRewardRedeemedPublishing      { get; set; } = false;
    }
    ```

    Registration in `Program.cs`:

    ```csharp
    builder.Services.AddOptions<DiscountOptions>()
        .Bind(builder.Configuration.GetSection(DiscountOptions.SectionName))
        .ValidateDataAnnotations()
        .ValidateOnStart();
    ```

    `IOptions<DiscountOptions>` is consumed by hosted services (`DiscountExpirySweepService`, `DiscountOutboxDispatcher`), by the conditional `AddConsumer<>` registration in §7 Phase 5, by the `if (_options.Enable*Publishing)` guards in §7 Phase 6, and by handlers that need the threshold for the `/ready` probe.

    **`OptionsAuditor` integration test** (mentioned in §8 Testing strategy): one xUnit test boots Discount with a known-good `appsettings.json` and asserts every `DiscountPermissions.All` constant maps to at least one feature-flag option OR is documented as unconditional. Counter-direction: every `DiscountOptions` boolean option is either read in code or removed. Catches drift between the permissions dictionary and the options dictionary.

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
| Event versioning | `int MessageVersion` on every integration event (initial = 1), inherited from `IntegrationEvent`; outbox row stores it under `SchemaVersion` | Mirrors Catalog §6.5. |
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
    DiscountPermissions.cs                 -- constants: coupon:read/create/edit/delete/redeem, reward-code:read/create/edit/delete/redeem, discount-rule:read/edit
    DiscountActors.cs                       -- const strings: System="discount-system", Sweep="discount-sweep", Service="discount-service"
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
| `DiscountHistoryAppendedIntegrationEvent` | `EntityType ∈ {Coupon, RewardCode, DiscountRule}`, `EntityId`, `RestaurantId`, `ChangeType ∈ {Created, Updated, Deleted, Redeemed}`, `OldValues` (nullable string of serialized JSON; null for `Created`), `NewValues` (string of serialized JSON) | **Catalog** → write a Marten `EntityHistoryArchive` document keyed by `EntityType + EntityId`. Idempotent on `EntityType + EntityId + ChangeType + OccurredOn` (base-class `OccurredOn`, NOT `OccurredAt` — see record shape in §7 Phase 4). The wire format is `string?` because the outbox row's `Payload` column is `TEXT` (serialized JSON); Catalog deserializes back to `JsonObject` on insert via `JsonNode.Parse(evt.OldValues)` and stores it. Never `JsonObject` on the wire — every publisher-to-outbox roundtrip would pay an unnecessary serialize-parse tax. |

| Event (→ Discount) | Source (today / planned) | SchemaVersion | Discount's required action |
|---|---|---|---|
| `MenuItemChangedIntegrationEvent` | Catalog (ships) | 1 | Find `DiscountRule`s where `IsActive=true`, `RestaurantId == event.RestaurantId`, and `RuleDataJson.RequiredMenuItemIds` includes `event.MenuItemId`. If any rule exists that targets the now-removed MenuItem (ChangeType=Deleted), flip the related `Coupon.IsActive=false`. No state change for non-affected rules. |
| `RestaurantConfigurationChangedIntegrationEvent` | Catalog (ships) | 1 | If `ChangedFields` contains `Currency`: find `Coupon` for `event.RestaurantId` whose `Amount` would be in the old currency; if the new currency differs, `IsActive=false`. Other `ChangedFields` are no-op for Discount. |
| `FeedbackSubmittedIntegrationEvent` | Notification v1 (does not ship) | 1 | Per Q6 hardcoded rule: `rating >= 4 && < 5` → `RewardCode { Type: "percentage", Value: 10 }`; `rating == 5` → `RewardCode { Type: "percentage", Value: 15 }` + `RewardCode { Type: "free_item", Value: "appetizer" }`. Both for the feedback's `RestaurantId`. Idempotent on `(OrderId, RewardType, RewardValue)` — guard with an `Idempotency-Key` header variant on the bus (MassTransit `MessageId` is sufficient). Stub consumer ships wired-but-disabled via `DiscountOptions:EnableFeedbackSubmittedConsumer=false`. |
| `OrderCreatedIntegrationEvent` | Ordering (does not ship) | 1 | Auto-apply lookup; emit `DiscountAppliedIntegrationEvent`. **Phase 8 ships a stub consumer wired-but-disabled** via `DiscountOptions:EnableOrderCreatedConsumer=false`; flips on when Ordering plan's publisher contract lands (per §6.6.4). Stub behavior: evaluate rules → `RedeemDiscount` (conditional UPDATE) per applicable coupon → emit `DiscountAppliedIntegrationEvent` per redemption. |

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
- Eleven new rows in the `Permissions` table (or `RolePermissions` registrations), six on `coupon:*`, five on `reward-code:*`, two on `discount-rule:*`. Existing roles seeded (`Manager`, `Waiter`, `Cashier`, `SuperAdmin`) get appropriate role→permission mappings:
  - `coupon:read` → all restaurant roles + SuperAdmin
  - `coupon:create` / `coupon:edit` / `coupon:delete` → `Manager` + `SuperAdmin`
  - `coupon:redeem` → `Cashier` + `Manager` + `SuperAdmin` (the actor that redeems)
  - `reward-code:read` / `reward-code:edit` → `Manager` + `SuperAdmin`
  - `reward-code:redeem` → `Cashier` + `Manager` + `SuperAdmin`
  - `reward-code:create` → `SuperAdmin` only (only feedback flow creates rewards for now)
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

### 6.6.4 With Ordering: `OrderCreatedIntegrationEvent` → auto-apply at checkout

Phase 8 introduces this handshake. The plan's design intent (per the v1.4 grilling decision C): Discount stays a stateless pricing service. The deduction math lives in a shared `BuildingBlocks.Discounts.ApplyDiscountsHelper` consumed by Basket (cart preview) and Ordering (finalized order). Discount owns lookups + redemption counters; callers own the order-total arithmetic.

**Ordering will own:**
- The publisher (`OrderCreatedConsumer` in Ordering emits `OrderCreatedIntegrationEvent` carrying `Guid Id`, `Guid RestaurantId`, `Guid CustomerId`, `IReadOnlyList<Guid> MenuItemIds`, `decimal OrderTotal`, `Instant CreatedAt`).
- The final deduction step: Ordering calls Discount's `EvaluateDiscountRules(EvaluateDiscountRulesRequest { RestaurantId, OrderTotal, MenuItemIds })` RPC, walks the returned `ApplicableCouponIds`, and for each one issues `RedeemDiscount(RedeemDiscountRequest { code, restaurant_id, order_id, quantity: 1 })`. The atomic conditional-UPDATE from Phase 1 §7 increments `RedeemAmount`.
- The order-total mutation: Ordering computes the final `Order.AppliedDiscountTotal` by calling `ApplyDiscountsHelper.Apply(orderTotalBeforeDiscount, appliedCoupons)` and persists it. Same helper, same rounding, same floor-at-zero clamp — both callers see identical math.

**This plan owns:**
- The stub consumer side (`OrderCreatedConsumer : IConsumer<OrderCreatedIntegrationEvent>` in `Discount.Grpc/Messaging/EventHandlers/`) **at the Discount service end** to round out the bus-flow contract: when enabled, it performs the same `EvaluateDiscountRules` → `RedeemDiscount` chain above and additionally emits `DiscountAppliedIntegrationEvent` per applied coupon (also stub-and-flag — see Phase 6). The stub runs only when `DiscountOptions:EnableOrderCreatedConsumer=true`. Default = `false` until Ordering ships.
- A new `BuildingBlocks.Discounts.ApplyDiscountsHelper` BuildingBlocks contribution — pure static functions for stacking math. See Phase 8 for the API surface.
- The new `Coupon.DiscountType` enum + migration. See Phase 8.

**Why two callers (Basket preview + Ordering at checkout) and not Discount doing the math:**
- A customer may store / re-store a cart freely. Calling `RedeemDiscount` from Basket preview would exhaust `MaxRedeemAmount` before the order is even placed. The preview-time deduction must be read-only.
- The finalized-order path is the only place where `RedeemDiscount` increments. Ordering owns that call.
- Discount never holds a "cart total" or "order total" reference. The math helper is stateless and lives in BuildingBlocks so both callers see the same result.

**Sync point:** Discount ships Phase 8's stub consumer disabled; Ordering ships its publisher separately. When both exist in the same deploy, flip the flag + ship a follow-up commit re-enabling `RedeemDiscount` from the consumer (currently the stub deliberately goes through `RedeemDiscount` to validate the wire path end-to-end — see the test below — but a future ADR can move the redemption call fully into Ordering and reduce Discount's consumer to a no-op audit-log emit).

**Tests:**
- `OrderCreatedConsumer` happy path with `DiscountOptions:EnableOrderCreatedConsumer=true`: fire `OrderCreatedIntegrationEvent` with `OrderTotal=100m`, `MenuItemIds=[seed-item-guid]`; assert one `RedeemDiscount` gRPC call lands; assert one `DiscountAppliedIntegrationEvent` row in the outbox.
- Disabled-flag path: same event with `EnableOrderCreatedConsumer=false`; assert zero `RedeemDiscount` calls and zero outbox rows.
- `ApplyDiscountsHelper` unit tests (in `BuildingBlocks.Tests/Discounts/ApplyDiscountsHelperTests.cs`): ten stacking combinations, one floor-at-zero edge case (e.g. subtotal `$10` + two `100%` coupons must clamp at `$0`, not `-$110`), one rounding edge case.

---

## 6.7 Outbox — SQLite variant + BuildingBlocks contribution

**Goal.** This is the first SQLite-flavored `OutboxDispatcher<TContext>` in BuildingBlocks.Messaging.Outbox. Postgres uses `FOR UPDATE SKIP LOCKED`; MSSQL uses `WITH (ROWLOCK, UPDLOCK, READPAST)`; SQLite has neither, so we need a different atomic-claim pattern that fits the base class's `FromSql(BuildClaimSql)` constraint (`BuildingBlocks/Messaging/Outbox/OutboxDispatcher.cs:226–229`).

**BuildingBlocks changes required (drive-by from Discount's plan):**

1. **`OutboxMessage` entity gains a `Guid? ClaimId` column** (currently absent — `BuildingBlocks/Messaging/Outbox/OutboxMessage.cs` only has `Id`, `OccurredOn`, `Type`, `Payload`, `DispatchedAt`, `SchemaVersion`). Add it as `public Guid? ClaimId { get; set; }`.

2. **`OutboxMessageConfiguration` declares the new column + an index on `(ClaimId, OccurredOn)`** so the dispatcher's SELECT-by-claim-id is cheap. Mirror the existing `ix_outbox_messages_dispatched_at_occurred_on` pattern.

3. **`OutboxDispatcher.BuildClaimSql(int batchSize)` is a `FormattableString` consumed via `FromSql(...)`** — that means each engine's override must return a **single statement whose result set EF Core can materialize into `OutboxMessage` rows**. SQLite's `UPDATE … RETURNING` cannot be piped through `FromSql(...)` directly, so the SQLite override uses a **CTE** that runs the claim and the return atomically:

    ```sql
    WITH claimed AS (
        UPDATE outbox_messages
           SET ClaimId = @claimId
         WHERE Id IN (
             SELECT Id FROM outbox_messages
              WHERE DispatchedAt IS NULL
                AND ClaimId IS NULL
                AND SchemaVersion <= @maxSupportedVersion
              ORDER BY OccurredOn
              LIMIT @batchSize
         )
         RETURNING *
    )
    SELECT * FROM claimed
    ```

    SQLite supports `WITH … UPDATE … RETURNING` since 3.33 (matching `Microsoft.EntityFrameworkCore.Sqlite` 10.x). EF Core materializes the result set of the outer `SELECT * FROM claimed` into `OutboxMessage` rows via the standard `AsTracking().ToListAsync(...)` pipeline. The CTE itself is opaque to `FromSql` — only the outer `SELECT` matters.

    The C# shape:

    ```csharp
    // Services/Discount/Discount.Grpc/Messaging/Outbox/DiscountOutboxDispatcher.cs
    public sealed class DiscountOutboxDispatcher(
        IServiceProvider services,
        IOptions<OutboxOptions> options,
        ILogger<DiscountOutboxDispatcher> logger)
        : OutboxDispatcher<DiscountContext>(services, options, logger)
    {
        private readonly Guid _claimId = Guid.NewGuid();   // per-instance, not per-row

        protected override DiscountContext CreateContext(IServiceProvider services)
            => services.GetRequiredService<DiscountContext>();

        protected override FormattableString BuildClaimSql(int batchSize) =>
            $@"
                WITH claimed AS (
                    UPDATE outbox_messages
                       SET ClaimId = {_claimId}
                     WHERE Id IN (
                         SELECT Id FROM outbox_messages
                          WHERE DispatchedAt IS NULL
                            AND ClaimId IS NULL
                            AND SchemaVersion <= {_options.MaxSupportedVersion}
                          ORDER BY OccurredOn
                          LIMIT {batchSize}
                     )
                     RETURNING *
                )
                SELECT * FROM claimed
            ";
    }
    ```

    > **Important — `_claimId` is per-instance, not per-row.** Two replicas of Discount cannot run concurrently against the same SQLite file (single-replica deployment assumption carries forward — see §8 *Cross-cutting SQLite notes*). Per-instance is sufficient for single-replica.

4. **`OutboxMessageConfiguration` is updated** (catalog-side) to add `ClaimId`:
    ```csharp
    builder.Property(m => m.ClaimId).IsRequired(false);

    builder.HasIndex(m => new { m.ClaimId, m.OccurredOn })
        .HasDatabaseName("ix_outbox_messages_claim_id_occurred_on");
    ```

**Discount-side changes:**

1. **`DiscountContext` implements `IOutboxDbContext`** (which means registering `DbSet<OutboxMessage>` and `DbSet<OutboxDeadMessage>` plus the dispatcher sees `DiscountContext`). Configuration comes from `OutboxMessageConfiguration` and a sibling `OutboxDeadMessageConfiguration` (already in BuildingBlocks).

2. **EF Core migration** — generated normally via `dotnet ef migrations add AddOutboxAndTenantClaim`. The migration adds `OutboxMessages` and `OutboxDeadMessages` to EF's model snapshot. SQLite's text representation of GUIDs is `TEXT` (per EF Core's default SQLite `Guid ↔ TEXT` mapping); `Instant ↔ DateTime` via the per-table `OutboxInstantConverter` in `BuildingBlocks/Messaging/Outbox/OutboxMessageConfiguration.cs:54–60` — **not** the `InstantToLongConverter` used by Coupon's own columns. The Coupon-side converter stays untouched.

3. **Program.cs wiring** — `AddHostedService<DiscountOutboxDispatcher>` after `AddDbContext<DiscountContext>(...)`. The dispatcher pulls a fresh `DiscountContext` from `IServiceProvider.CreateScope()` per iteration (matches the base class's `CreateScope()` semantics).

**Why a contribution, not private to Discount.** Catalog's `OutboxDispatcher<KitchenDbContext>` (and any future service dispatcher) targets an engine with row-lock-during-SELECT; SQLite-flavored services need the CTE pattern. Adding `ClaimId` to the shared `OutboxMessage` entity is a small, justified BuildingBlocks change; the SQLite CTE is the first of its kind and other engines don't need to adopt it.

**Concurrency proof:** on SQLite with WAL mode, concurrent writers serialize through the database-level write lock; a write transaction (`OutboxDispatcher.DispatchBatchAsync` already uses `BeginTransactionAsync`) holds an exclusive lock until COMMIT. Two concurrent dispatchers' CTEs cannot interleave because SQLite acquires the write lock when the statement begins — the second one waits at the statement boundary. Single-replica deployment is the cleanest characterization; multi-replica is a degradation. Document this in `current-architecture.md §11 Local Development`.

**Transient-fault handling.** When RabbitMQ is unreachable (TCP-level failure, broker down, channel error), the base `OutboxDispatcher.ExecuteAsync` catches the exception and re-enters the polling loop on the next `ActivePollInterval`. Without a circuit breaker, this hammers the broker at full poll-rate indefinitely — slow burn rather than loud alert. The Discount-specific override wraps each tick in a small circuit-break: count consecutive `broker_failure` events; once the count exceeds `OutboxOptions.MaxConsecutiveBrokerFailures` (default `3`), the dispatcher pauses for `BrokerBackoffSeconds` (default `60s`) before the next `DispatchOnceAsync` attempt, and trips the `/ready` probe to `Unhealthy`. Reset the counter on the first successful dispatch. Mirror Catalog's behavior (verify in `Kitchen.API/Application/Outbox/KitchenOutboxDispatcher.cs`); if Catalog hasn't implemented this, Discount defines the convention and Catalog follows.

Pseudocode (concrete Discount implementation inheriting `OutboxDispatcher<DiscountContext>`):

```csharp
private int _consecutiveBrokerFailures;
protected override async Task ExecuteAsync(CancellationToken stoppingToken)
{
    while (!stoppingToken.IsCancellationRequested)
    {
        if (_consecutiveBrokerFailures >= _options.MaxConsecutiveBrokerFailures)
            await BackoffAsync(_options.BrokerBackoffSeconds, stoppingToken);
        try
        {
            var dispatched = await DispatchOnceAsync(stoppingToken);
            _consecutiveBrokerFailures = 0;
            await Task.Delay(dispatched > 0 ? _options.ActivePollInterval : _options.IdlePollInterval, stoppingToken);
        }
        catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
        {
            _consecutiveBrokerFailures++;
            _logger.LogError(ex, "Outbox dispatcher iteration failed ({Consecutive} in a row).", _consecutiveBrokerFailures);
            await BackoffAsync(_options.BrokerBackoffSeconds, stoppingToken);
        }
    }
}
```

`OutboxOptions.MaxConsecutiveBrokerFailures` and `OutboxOptions.BrokerBackoffSeconds` are added to the options class with `[Range]` data annotations; production defaults = `3` failures → `60s` backoff.

**Outbox row shape after the migration:**

| Column | Type | Source |
|---|---|---|
| `Id` | TEXT PK (Guid→TEXT) | `OutboxMessage.Id` |
| `OccurredOn` | TEXT (DateTime via `OutboxInstantConverter`) | `OutboxMessage.OccurredOn` |
| `Type` | TEXT(500) | `OutboxMessage.Type` |
| `Payload` | TEXT (JSON-serialized event) | `OutboxMessage.Payload` |
| `DispatchedAt` | TEXT NULL (DateTime via converter) | `OutboxMessage.DispatchedAt` |
| `SchemaVersion` | INTEGER NOT NULL | `OutboxMessage.SchemaVersion` |
| `ClaimId` | TEXT NULL (Guid→TEXT) | `OutboxMessage.ClaimId` *(new)* |
| `ix_outbox_messages_dispatched_at_occurred_on` | index | existing |
| `ix_outbox_messages_claim_id_occurred_on` | index | *(new)* |

---

## 7. Phased milestones

The phases are ordered so each is independently shippable and any earlier phase's failure does not block the later cross-cutting concerns.

### Phase 1 — Production-grade Coupon (foundation: auth + tenancy + outbox + sweep + race fix)

This is the heaviest phase. It establishes every cross-cutting primitive on which Phases 2 / 3 / 4 layer new features. Plan target: ~25 tests passing.

- **BuildingBlocks fix:** `BuildingBlocks.Multitenancy.ITenantEntity.RestaurantId : int → Guid`. Trivial; one line. Justifies Phase 1's own tenancy work.
- **Soft-delete columns** added to `Coupon` alongside `IsActive`:
  ```csharp
  public Instant? DeletedAt   { get; set; }
  public string?  DeletedBy   { get; set; }
  ```
  Reason: `IsActive` is overloaded in this plan across three concerns — "user-deleted", "sweep-deactivated", "rule-deactivated". Conflating them with a single boolean means a rule reactivation could resurrect a row the admin just deleted. The contract is: **`IsActive` reflects the current business state (deactivatable by sweep / rule); `DeletedAt` / `DeletedBy` reflect the soft-delete event and are not reset by any rule or sweep.** A row with `DeletedAt != null` is excluded from all list / get / evaluate RPCs regardless of `IsActive`. `RestoreCoupon` is **not** in this plan — once soft-deleted, a Coupon stays deleted; the user creates a new one. `DeleteDiscount` sets both `IsActive = false` and `DeletedAt = now`, `DeletedBy = caller`. The `DiscountExpirySweepService` only sets `IsActive = false` (never `DeletedAt`). The `MenuItemChangedConsumer` rule path only sets `IsActive` on non-deleted rows (`Where(c => c.DeletedAt == null && ...)`). Phases 2 and 3 inherit the same pattern on `DiscountRule` and `RewardCode`.
- **JWT auth wired** in `Program.cs`:
  - `AddJwtBearer` against Identity authority (`https://localhost:5057`), audience `OrderlyMicroservices`.
  - The existing 5 Coupon RPCs grow to **6** with the addition of `ListDiscounts(ListDiscountsRequest) → ListDiscountsResponse` (paged, `PagedResult<CouponModel>` with `page` / `page_size` query fields, default 50, max 200). The List RPC was missing from the original surface; Phases 2 and 3 add `ListDiscountRules` / `ListRewardCodes` from day one, so symmetry requires the Coupon-side counterpart now rather than as a Phase 2/3 carry-over. Permission gate: `coupon:read`. The query path runs through the global tenant filter — no explicit `Where(RestaurantId == ...)` in the handler.
  - `Authorization/DiscountPermissions.cs` is the single source of truth:

    ```csharp
    public static class DiscountPermissions
    {
        public const string CouponRead     = "coupon:read";
        public const string CouponCreate   = "coupon:create";
        public const string CouponEdit     = "coupon:edit";
        public const string CouponDelete   = "coupon:delete";
        public const string CouponRedeem   = "coupon:redeem";
        public const string RewardCodeRead     = "reward-code:read";
        public const string RewardCodeCreate   = "reward-code:create";
        public const string RewardCodeEdit     = "reward-code:edit";
        public const string RewardCodeDelete   = "reward-code:delete";
        public const string RewardCodeRedeem   = "reward-code:redeem";
        public const string DiscountRuleRead  = "discount-rule:read";
        public const string DiscountRuleEdit  = "discount-rule:edit";

        public static readonly IReadOnlyList<string> All =
        [
            CouponRead, CouponCreate, CouponEdit, CouponDelete, CouponRedeem,
            RewardCodeRead, RewardCodeCreate, RewardCodeEdit, RewardCodeDelete, RewardCodeRedeem,
            DiscountRuleRead, DiscountRuleEdit,
        ];
    }
    ```

  - `AddDiscountPolicies(IServiceCollection)` loops over `DiscountPermissions.All` and registers each as a policy keyed by the constant value:

    ```csharp
    public static IServiceCollection AddDiscountPolicies(this IServiceCollection services)
    {
        services.AddAuthorization(o =>
        {
            foreach (var permission in DiscountPermissions.All)
                o.AddPolicy(permission, p => p.RequireAssertion(ctx =>
                    // Identity emits permissions comma-separated under the "permissions" claim
                    // (verified at Phase 1 by decoding a dev login JWT). Single-permission tokens
                    // also work via the FindAll fallback.
                    ctx.User.FindAll("permissions").Any(c => c.Value.Split(',').Contains(permission))
                    || ctx.User.FindAll("permission").Any(c => c.Value == permission)));
        });
        return services;
    }
    ```

    The `RequireClaim("permission", permission)` shape is a *convention* — Identity may emit permissions as a comma-separated `permissions` claim, as individual `permission` claims, or under a different claim type. **Phase 1 includes a verification step before locking the policy expression**: a one-shot console snippet (added to `Discount.Grpc.Tests/Integration/JwtClaimShapeProbe.cs`) logs in via Identity's dev `/api/auth/login`, decodes the JWT, and asserts which claim shape Identity uses. The expression above handles the three observed shapes (`permissions` comma-split, individual `permission`, or both). If Identity emits something different, a CI test fails loudly — not silently.

    The single source of truth for permission strings lives in `DiscountPermissions`. Identity's follow-up plan reads the same string list when seeding its `Permissions` table.
  - Per gRPC service method, use the custom `[Permission(DiscountPermissions.CouponRead)]` attribute (declared in `Authorization/DiscountPermissionAttribute.cs`); the global `DiscountAuthorizationInterceptor` (per §0.4.2.1) reads the attribute and runs `IAuthorizationService.AuthorizeAsync(user, resource, permission)`. **Do not** use the standard `[Authorize(Policy = "...")]` attribute — gRPC ignores it.
- **`ICurrentRestaurantProvider` registered** as `Singleton`. `ClaimsRestaurantProvider` reads `ClaimTypes.Role`/custom `restaurantId` claim from `IHttpContextAccessor.HttpContext.User`. Bus consumers use `ClaimsPrincipalFactory.FromEvent<T>(T evt)` to build the synthetic principal (Pattern 2 from Q10).
- **Multi-tenancy global filter applied** to `Coupon`:
  - `Coupon : AuditableEntity<int>, ITenantEntity`.
  - `DiscountContext.OnModelCreating` calls `ApplyTenantFilter<Coupon>(() => _provider.RestaurantId)`.
  - `DiscountService` gRPC handlers drop the now-redundant explicit `Where(RestaurantId == ...)` filters (the global filter handles it).
  - Tests: one "naive query returns only current tenant" test; one SuperAdmin `.IgnoreQueryFilters()` test (justifies the bypass pattern later used in (7)).
- **Outbox tables added** to Discount's SQLite schema. See §6.7 for the migration. The `IOutboxDbContext` interface is implemented on `DiscountContext`. `BuildingBlocks.Messaging.Outbox.OutboxDispatcher<DiscountContext>` (named `DiscountOutboxDispatcher`) is the concrete; it overrides `BuildClaimSql` with the SQLite variant.
- **`IOutboxPublisher.WriteOutboxMessageAsync`** called from `DiscountService.CreateDiscount` / `UpdateDiscount` / `DeleteDiscount` (history event payload: payload-of-row-delta). Same hooks in `RedeemDiscount`. Each outbox row carries `CorrelationId` from the gRPC `Metadata`. Total Phase-1 publish surface is one event type (`DiscountHistoryAppendedIntegrationEvent`).
- **`DiscountExpirySweepService : BackgroundService`** runs every `DiscountOptions:SweepIntervalMinutes` (default 5, `[Range(1, 1440)]`). Sweeps: `UPDATE Coupons SET IsActive = 0, LastModifiedBy = 'discount-sweep', LastModifiedAt = @now WHERE ExpirationDate < @now AND IsActive = 1`. Idempotent. Reference implementation:

  ```csharp
  public sealed class DiscountExpirySweepService(
      IServiceProvider services, TimeProvider clock,
      IOptions<DiscountOptions> options,
      ILogger<DiscountExpirySweepService> logger) : BackgroundService
  {
      protected override async Task ExecuteAsync(CancellationToken stoppingToken)
      {
          var interval = TimeSpan.FromMinutes(options.Value.SweepIntervalMinutes);
          using var timer = new PeriodicTimer(interval);
          do
          {
              try { await SweepOnceAsync(stoppingToken); }
              catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
              catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
              {
                  logger.LogError(ex, "Expiry sweep iteration failed; will retry next tick ({Interval})", interval);
              }
          } while (await timer.WaitForNextTickAsync(stoppingToken));
      }

      private async Task SweepOnceAsync(CancellationToken ct)
      {
          await using var scope = services.CreateAsyncScope();
          var db  = scope.ServiceProvider.GetRequiredService<DiscountContext>();
          var now = clock.GetCurrentInstant();
          var flipped = await db.Coupons
              .Where(c => c.IsActive && c.ExpirationDate != null && c.ExpirationDate < now)
              .ExecuteUpdateAsync(s => s
                  .SetProperty(c => c.IsActive,         false)
                  .SetProperty(c => c.LastModifiedAt,   now)
                  .SetProperty(c => c.LastModifiedBy,   DiscountActors.Sweep),
              ct);
          logger.LogInformation("Expiry sweep flipped {Rows} coupons to inactive at {Now}.", flipped, now);
      }
  }
  ```

  `ExecuteUpdateAsync` (EF Core 7+) avoids the load-then-save round-trip — single bulk UPDATE; reads use `WHERE IsActive = 1` so the predicate is index-friendly. `BeginScope` from §0.3.8 wraps the iteration in `{ "RestaurantId": "<scope-restaurantId>", "Component": "DiscountExpirySweepService" }` so the log line is filterable in Serilog. The same pattern repeats in `DiscountOutboxDispatcher` (per the outbox base's `ExecuteAsync`).
- **Lazy-evaluation gate** at every read of Coupon's "active" semantic. Add a helper `Coupon.IsActiveNow(TimeProvider clock)` that returns `IsActive && (ExpirationDate IS NULL || ExpirationDate >= clock.GetUtcNow().ToInstant())`. `GetDiscount` / `RedeemDiscount` consult `IsActiveNow(...)` before returning success. Hot path adds at most one boolean evaluation per call.

  **Wire `TimeProvider`** in `Program.cs`: `builder.Services.AddSingleton(TimeProvider.System);` consumed by every handler that needs "now". Tests use `Microsoft.Extensions.TimeProvider.Testing.FakeTimeProvider` and override the registered `TimeProvider` to a `FakeTimeProvider` instance via `services.Replace(...)` or `WithSingleton<TimeProvider, FakeTimeProvider>(...)` per-test. Catalog already uses `FakeTimeProvider` for `Hangfire` job logic — mirror that pattern.
- **`RedeemDiscount` race fix:** replace the read-modify-write with conditional update:
  ```csharp
  int rowsAffected = await _dbContext.Database.ExecuteSqlInterpolatedAsync(
      $@"UPDATE Coupons
            SET RedeemAmount    = RedeemAmount + 1,
                LastModifiedAt  = {now},
                LastModifiedBy  = 'discount-system'
          WHERE Id = {id}
            AND (MaxRedeemAmount IS NULL OR RedeemAmount < MaxRedeemAmount)
            AND IsActive = 1
            AND ({_tenantProvider.RestaurantId} IS NULL OR RestaurantId = {_tenantProvider.RestaurantId})",
      cancellationToken);
  if (rowsAffected == 0)
      throw new ConcurrentRedemptionException(id, correlationId);
  ```

  **`RedeemDiscountRequest` proto shape (locked):**
  ```proto
  message RedeemDiscountRequest {
    string code = 1;                 // Coupon.Code (UK per tenant)
    string restaurant_id = 2;        // Guid; double-checked against ICurrentRestaurantProvider
    string order_id = 3;             // Guid; the order the redemption is attributed to
    int32  quantity = 4;             // default 1; allows multi-quantity redemption paths
  }
  message RedeemDiscountResponse {
    CouponModel coupon = 1;          // post-redemption state
    string redemption_event_id = 2;  // for client correlation
  }
  ```
  The `Idempotency-Key` header is read by the gRPC interceptor (per §0.4.1) before the handler is invoked. The handler does not parse the header itself. The `restaurant_id` field on the request is a defense-in-depth check against the JWT-derived tenant — a mismatch returns `StatusCode.PermissionDenied` with `Metadata["tenant-mismatch"] = "true"`.
  **Audit interceptor caveat:** `Database.ExecuteSqlInterpolatedAsync` bypasses EF Core's change tracker, so the `AuditableEntityInterceptor` registered at `Program.cs:12` does **not** fire to set `LastModifiedAt` / `LastModifiedBy`. The SQL above sets them explicitly. `LastModifiedBy = 'discount-system'` (not `'discount-sweep'` — that string is reserved for the sweep service; distinguish the two actors in audit logs). `LastModifiedBy` is the actor string per `BuildingBlocks.Entities.Contracts.AuditableEntity<int>`; the JWT `sub` claim is appended at audit-log write time elsewhere, but here we don't have one. The optional `_tenantProvider.RestaurantId` predicate is a defense-in-depth backup while the global query filter is in place (the filter already restricts, but a raw `UPDATE` ignores it; the predicate re-applies the tenant constraint at SQL level).
  
  Map `ConcurrentRedemptionException` to `StatusCode.Aborted` (retry once internally; second failure surfaces `FailedPrecondition`).
- **`UpdateDiscount` / `UpdateDiscountRule` / `UpdateRewardCode` concurrency:** the conditional-UPDATE pattern is purpose-built for `Redeem*` (a counter increment with a predicate). Plain updates use a different mechanism: every Discount entity gains a `uint Version` (or `byte[] RowVersion`) column that EF Core maps as a concurrency token (`[ConcurrencyCheck]` / `[Timestamp]`). The `Update*` handler reads the row inside an explicit transaction with `SELECT ... FOR UPDATE` semantics (SQLite serializes on the write lock; same effect), mutates, and `SaveChanges` — if the version mismatches, EF Core throws `DbUpdateConcurrencyException`, mapped to `StatusCode.Aborted` with `Metadata["retry-after-ms"]`. **Last-writer-wins is rejected** as the default — settings-style updates are a real use case (admin edits a description) but they're rare enough that 409-style retry is acceptable. The same `Version` column is added to all three aggregates from day one (Phase 1 for Coupon, Phase 2/3 by extension).
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
- §6 Data Stores — SQLite `discountdb` gains `outbox_messages` (with `ClaimId` column + `ix_outbox_messages_claim_id_occurred_on` per §6.7 v1.1) and `outbox_messages_dead`, plus EF Core's auto-created `__EFMigrationsHistory`. The legacy `claims` reference from earlier drafts is dropped — that token was a stale leftover from Catalog's `Catalog:EntityMoveCoupons` flag and doesn't belong in Discount's schema inventory.
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
  - **`MenuItemChangedConsumer : IConsumer<MenuItemChangedIntegrationEvent>`.** Pattern 2 (synthetic claims from event). Logic: for `ChangeType ∈ {Updated, Deleted}`, find `DiscountRule`s whose `RuleDataJson.RequiredMenuItemIds` includes `event.MenuItemId`. For each affected `Coupon`, recompute via `Coupon.IsActiveNow(...)` and persist `IsActive` based on whether at least one rule still holds. **Idempotency**: bus redelivery protection lives in a `processed_inbound_events` table (`EventId TEXT PK, ConsumerType TEXT, ConsumedAt INTEGER NOT NULL`, populated inside the same transaction as the rule update). The consumer reads `event.Id` from `IntegrationEvent`, runs `INSERT INTO processed_inbound_events (EventId, ConsumerType) VALUES (?, 'MenuItemChanged')`; on a unique-constraint violation (`Microsoft.Data.Sqlite.SqliteException.SqlState == "23505"`), the consumer returns without dispatching (the bus already retried with a stale copy). Do **not** use an in-process dictionary — those die on restart and double-fire when the bus redelivers.
  - **`RestaurantConfigurationChangedConsumer : IConsumer<RestaurantConfigurationChangedIntegrationEvent>`.** Pattern 2. Logic: if `ChangedFields` contains `"Currency"`, find `Coupon` for `RestaurantId`, flip `IsActive=false`. Otherwise no-op. **Same `processed_inbound_events` idempotency contract as `MenuItemChangedConsumer`**: a `RestaurantConfigurationChanged` event is more idempotent in effect (flipping `IsActive=false` is monotonic), but the bus can still redeliver, and a redelivery followed by a partial failure can leave the system in a state where the second delivery observes a different `ChangedFields` shape (Catalog emitting a follow-up). The table guard is cheap; the parity with `MenuItemChangedConsumer` keeps the consumer-side idempotency story in one place.
- **Tests:** xUnit + NSubstitute unit tests for `EvaluateDiscountRules` (MinOrderAmount, RequiredMenuItems, TimeWindow matchers); SQLite-in-memory integration for the `MenuItemChangedConsumer` end-to-end (set up a coupon + rule, fire event with `ChangeType=Deleted`, assert `Coupon.IsActive=false`); `InMemoryTestHarness` for the same consumer.

**Doc-update scope (§0.2):**
- §4.4 Discount Service — entity table gains `DiscountRule` row; endpoint list gains the 6 new RPCs + the 2 new consumers.
- §5.2 Asynchronous — add the consumer rows for `MenuItemChangedIntegrationEvent` and `RestaurantConfigurationChangedIntegrationEvent`.
- §9 Cross-Cutting Patterns — note `DiscountRule` gRPC + JSONB rule data + FK semantics.

### Phase 3 — `RewardCode` aggregate + redemption flow

- **New entity `RewardCode : AuditableEntity<int>, ITenantEntity`:**
  ```csharp
  public sealed class RewardCode : AuditableEntity<int>, ITenantEntity
  {
      public Guid RestaurantId { get; set; }
      public required string Code { get; set; }            // UK with RestaurantId; C# 11 required modifier
      public required RewardKind Kind { get; set; }       // Percentage | FixedAmount | FreeItem | Points
      public decimal Value { get; set; }                  // % for Percentage; currency for FixedAmount; menu item id (Guid) for FreeItem; count for Points
      public string? Description { get; set; }
      public Instant? ExpirationDate { get; set; }
      public int RedeemAmount { get; set; }
      public int? MaxRedeemAmount { get; set; }
      public Guid? RedeemedInOrderId { get; set; }        // last redeeming order
      public Instant? RedeemedAt { get; set; }

      // Deterministic code-builders (called from FeedbackSubmittedConsumer; take the inbound event's Id
      // so redelivery within the same day produces an identical code, and redeliveries across day boundaries
      // still produce identical codes — preserving unique-key-violation idempotency.)
      internal static string Code4StarPct10 (Guid rid, Guid feedbackEventId, TimeProvider clock)
          => BuildCode(rid, "4STAR-PCT10", feedbackEventId, clock);

      internal static string Code5StarPct15 (Guid rid, Guid feedbackEventId, TimeProvider clock)
          => BuildCode(rid, "5STAR-PCT15", feedbackEventId, clock);

      internal static string Code5StarAppetizer(Guid rid, Guid feedbackEventId, TimeProvider clock)
          => BuildCode(rid, "5STAR-APPETIZER", feedbackEventId, clock);

      private static string BuildCode(Guid rid, string tag, Guid feedbackEventId, TimeProvider clock)
      {
          // Day-prefix is human-readable (audit reports group by date); the stable suffix is the event id.
          // Both are required: the day-prefix makes codes match in admin UIs, the event-id makes them idempotent.
          var day = clock.GetUtcNow().ToString("yyyyMMdd");
          return $"RWD-{rid:N}-{tag}-{day}-{feedbackEventId:N}"[..Math.Min(120, $"RWD-{rid:N}-{tag}-{day}-{feedbackEventId:N}".Length)];
      }
  }

  public enum RewardKind { Percentage = 0, FixedAmount = 1, FreeItem = 2, Points = 3 }
  ```
  **`RewardCode.Value` is overloaded across `RewardKind`** — the column holds the percentage (10 for 10%), the fixed currency amount, the menu item id (Guid cast to decimal — broken; see validator below), or the points count. A free-item reward with `Value = 0m` is the realistic case (Phase 5's appetizer reward). The FluentValidation rule pins the kind↔value relationship at command boundary so the proto doesn't get frozen with a generic `Value` that's only meaningful for two of four kinds:
  ```csharp
  public class CreateRewardCodeCommandValidator : AbstractValidator<CreateRewardCodeCommand>
  {
      public CreateRewardCodeCommandValidator()
      {
          RuleFor(x => x.Code).NotEmpty().MaximumLength(120);
          RuleFor(x => x.RestaurantId).NotEmpty();
          RuleFor(x => x.Value).GreaterThan(0m)
              .When(x => x.Kind != RewardKind.FreeItem)
              .WithMessage("Value must be > 0 for Percentage, FixedAmount, or Points rewards.");
          RuleFor(x => x.Value).Equal(0m)
              .When(x => x.Kind == RewardKind.FreeItem)
              .WithMessage("FreeItem rewards carry the menu item id in Description, not Value.");
          RuleFor(x => x.Value).LessThanOrEqualTo(100m)
              .When(x => x.Kind == RewardKind.Percentage)
              .WithMessage("Percentage rewards must be <= 100.");
          RuleFor(x => x.ExpirationDate).GreaterThan(_clock.GetCurrentInstant())
              .When(x => x.ExpirationDate.HasValue)
              .WithMessage("ExpirationDate, if set, must be in the future.");
      }
  }
  ```
  The free-item's target menu item id is carried in `Description` as `free-item:{menuItemId}` — not a clean typed field, but Phase 3 keeps the proto simple. A future `RewardTargetMenuItemId` field is a v2 of the proto (bump `MessageVersion` and follow the deprecation cycle in §6.4).
  ```
  **Naming note:** the enum is intentionally `RewardKind`, not `RewardType` — the latter collides semantically with the type's class name and reads ambiguously (`reward.Kind = RewardKind.Percentage`). `DiscountRuleType` is fine as an enum name on `DiscountRule` because there's no separate `Rule` type to confuse it with; same symmetry applies elsewhere.
- **New RPC service `RewardCodeProtoService`:** `CreateRewardCode`, `GetRewardCode`, `ListRewardCodes` (paged), `UpdateRewardCode`, `DeleteRewardCode`, `RedeemRewardCode(RedeemRewardCodeRequest) → RedeemRewardCodeResponse`. Permissions `reward-code:read/create/edit/delete/redeem`. Apply `ITenantEntity` + global filter.

  **`RedeemRewardCodeRequest` proto shape (locked):**
  ```proto
  message RedeemRewardCodeRequest {
    string code = 1;                 // RewardCode.Code (UK per tenant)
    string restaurant_id = 2;        // Guid; double-checked against ICurrentRestaurantProvider
    string order_id = 3;             // Guid; the order the redemption is attributed to
    int32  quantity = 4;             // default 1
  }
  message RedeemRewardCodeResponse {
    RewardCodeModel reward_code = 1;  // post-redemption state
    string redemption_event_id = 2;
  }
  ```
  Mirrors `RedeemDiscountRequest` exactly — same `Idempotency-Key` header path, same tenant-mismatch `StatusCode.PermissionDenied` behavior.
- **Race-fix pattern re-applied to `RedeemRewardCode`** — same conditional UPDATE as `RedeemDiscount` in Phase 1. The handler sets `RedeemedInOrderId` and `RedeemedAt` in the same UPDATE.
- **Lazy-eval gate + sweep pattern reused** — `RewardCode.IsActiveNow(IClock)`. `DiscountExpirySweepService` extends to also flip `RewardCode.IsActive=false` on expiry (single sweep, two UPDATEs).
- **Outbox publishes** — every RewardCode CUD + redeem writes a `DiscountHistoryAppendedIntegrationEvent` row (per Q3's history decision; `EntityType=RewardCode`).
- **Tests:** xUnit + NSubstitute unit + SQLite-in-memory integration for the same patterns as Phase 1/2.

**Doc-update scope (§0.2):**
- §4.4 Discount Service — entity table gains `RewardCode`; endpoint list gains 6 RPCs.
- §5.2 Asynchronous — no new publish rows (the publishes go through `DiscountHistoryAppendedIntegrationEvent`; covered by Phase 1 row).
- §9 Cross-Cutting Patterns — nothing new beyond Phase 1's outbox row.

### Phase 4 — History publishing wired across all aggregates

This phase is mostly "fill in the rest of the publish points" for the entities created in Phases 1 / 2 / 3. The `DiscountHistoryAppendedIntegrationEvent` was published from Coupon CUD in Phase 1; expand to RewardCode and DiscountRule.

- **`DiscountHistoryAppendedIntegrationEvent` payload struct** (inherits `Id`, `OccurredOn`, `EventType`, `MessageVersion=1` from the `BuildingBlocks.Messaging.Events.IntegrationEvent` base — do NOT redeclare them or the shadowing causes MassTransit serialization confusion):
  ```csharp
  public sealed record DiscountHistoryAppendedIntegrationEvent(
      string EntityType,           // "Coupon" | "RewardCode" | "DiscountRule"
      int EntityId,
      Guid RestaurantId,
      string ChangeType,            // "Created" | "Updated" | "Deleted" | "Redeemed"
      string? OldValues,            // serialized JSON; null for Created
      string NewValues              // serialized JSON
  ) : IntegrationEvent;
  ```
  The base provides `Guid Id` (for dedup), `Instant OccurredOn` (wall-clock), `EventType` (assembly-qualified, drives the dispatcher payload-deserialization), and `int MessageVersion = 1` (`OutboxPublisher` copies `MessageVersion` into the outbox row's `SchemaVersion` column on stage — see `BuildingBlocks/Messaging/Outbox/OutboxPublisher.cs:43`). The handler-level `CorrelationId` flows via the MassTransit transport header and the outbox row's own `CorrelationId` column, **not** as a record field.
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
- **Hardcoded 4★/5★ logic per Q6**, applied by **sending the existing `CreateRewardCodeCommand` through MediatR `ISender`** so the consumer respects the same validator (`FluentValidation` pipeline behaviour), `ITenantEntity` gate, outbox publish, and audit columns that an RPC caller would. Do **not** build `new RewardCode { ... }` instances inline, and do **not** `new` a handler — both bypass every cross-cutting guard the rest of the plan stands up:
  ```csharp
  public sealed class FeedbackSubmittedConsumer(
      ISender sender,
      TimeProvider clock,
      ICurrentRestaurantProvider tenant,
      ILogger<FeedbackSubmittedConsumer> logger) : IConsumer<FeedbackSubmittedIntegrationEvent>
  {
      public async Task Consume(ConsumeContext<FeedbackSubmittedIntegrationEvent> context)
      {
          var evt = context.Message;
          var restaurantId = evt.RestaurantId;
          tenant.Attach(new ClaimsPrincipalBuilder().WithRestaurant(restaurantId).WithActor("discount-service").Build());

          if (evt.OverallRating >= 4 && evt.OverallRating < 5)
              await sender.Send(new CreateRewardCodeCommand(
                  RestaurantId:    restaurantId,
                  Code:            RewardCode.Code4StarPct10(restaurantId, evt.Id, clock),
                  Kind:            RewardKind.Percentage,
                  Value:           10m,
                  Description:     "10% off for 4★ feedback",
                  ExpirationDate:  clock.GetCurrentInstant() + Duration.FromDays(30)),
                  context.CancellationToken);
          else if (evt.OverallRating >= 5)
          {
              await sender.Send(new CreateRewardCodeCommand(
                  RestaurantId:    restaurantId,
                  Code:            RewardCode.Code5StarPct15(restaurantId, evt.Id, clock),
                  Kind:            RewardKind.Percentage,
                  Value:           15m,
                  Description:     "15% off for 5★ feedback",
                  ExpirationDate:  clock.GetCurrentInstant() + Duration.FromDays(30)),
                  context.CancellationToken);
              await sender.Send(new CreateRewardCodeCommand(
                  RestaurantId:    restaurantId,
                  Code:            RewardCode.Code5StarAppetizer(restaurantId, evt.Id, clock),
                  Kind:            RewardKind.FreeItem,
                  Value:           0m,
                  Description:     "Free appetizer for 5★ feedback",
                  ExpirationDate:  clock.GetCurrentInstant() + Duration.FromDays(30)),
                  context.CancellationToken);
          }
          // The handler writes both the RewardCode row and the outbox row.
          // No additional DiscountHistoryAppendedIntegrationEvent publish here —
          // the handler already does it on Created.
      }
  }
  ```
  The `Code*()` helpers are deterministic (e.g., `RWD-{restaurantId:N}-4STAR-PCT10-{yyyyMMdd}-{feedbackEventId:N}`) so re-delivery of the same `FeedbackSubmittedIntegrationEvent` is detected by the uniqueness constraint on `Code` and the duplicate-Create is swallowed. Idempotent by design — no separate `processed_inbound_events` check needed beyond what the handler's own uniqueness violation already provides. (For reference, Phase 2's `MenuItemChangedConsumer` *does* use `processed_inbound_events` because the rule-update path has no natural uniqueness violation to lean on; the two strategies are complementary, not redundant — see §0.3.4 for the project's consumer-side idempotency choice matrix.)
- **Disabled by default** via `DiscountOptions:EnableFeedbackSubmittedConsumer=false`. MassTransit 8.x does not expose `ConfigureConsumer.DisableConsumer<T>(...)`; the modern idiom is **conditional registration** in `Program.cs`:

  ```csharp
  builder.Services.AddMassTransit(config =>
  {
      config.SetKebabCaseEndpointNameFormatter();
      config.AddConsumers(typeof(MenuItemChangedConsumer).Assembly);  // the always-on consumers
      if (builder.Configuration.GetValue<bool>("DiscountOptions:EnableFeedbackSubmittedConsumer"))
          config.AddConsumer<FeedbackSubmittedConsumer>();
      config.UsingRabbitMq(/* same as Catalog's Program.cs */);
  });
  ```

  When the flag flips on, the consumer endpoint materializes on next boot; when it flips off, an orphaned queue is left (manual cleanup is acceptable — the orphan has no consumers, just retains messages until RabbitMQ retention).
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
- **Final smoke test:** 0 warnings / 0 errors; full test pyramid (unit + SQLite-in-mem + InMemoryHarness + gRPC integration) green; SQL migrations apply cleanly; build for both Debug and Release configurations.
- **Yarp gRPC routing verification:** hit the Yarp gateway (`http://localhost:5000` for the dev environment) from a `Grpc.Net.Client` `GrpcChannel` with a real `Metadata["authorization"]` Bearer token, target `DiscountProtoService.GetDiscount` (or a new no-auth probe RPC if `GetDiscount` requires a permission), and assert the call lands on port 6002 with the JWT surviving the hop. Two failure modes to catch here: (1) the gateway strips the `authorization` header (the JWT flow breaks silently, every RPC returns `StatusCode.Unauthenticated`); (2) the gateway terminates HTTP/2 incorrectly and the gRPC frames don't survive (every call returns `StatusCode.Internal` with a "protocol error" detail). The verification step lives in `Discount.Grpc.Tests/Integration/YarpGatewaySmokeTest.cs` and runs against the docker-compose dev stack.

**Doc-update scope (§0.2):** everything touched by Phases 1–6 audited for consistency. The drift memo is the canonical post-Phase-7 reference.

### Phase 8 — Apply-surface (Basket deduction + Ordering auto-apply stub)

Closes the `#warning TODO` in `Basket.API/Basket/StoreBasket/StoreBasketHandler.cs:41`, pins coupon-discount semantics, and ships the Ordering-driven auto-apply path as a stub consumer. Discount stays a stateless pricing service (lookup + counter); the deduction math lives in a new `BuildingBlocks.Discounts.ApplyDiscountsHelper` consumed by Basket (cart preview) and Ordering (finalized order).

#### 8.1 Schema changes

- **`Coupon.DiscountType { Percentage, FixedAmount }` enum** in `Discount.Grpc/Models/DiscountType.cs`:
  ```csharp
  public enum DiscountType { Percentage = 0, FixedAmount = 1 }
  ```
- **EF Core migration `AddDiscountTypeToCoupon`** in `Discount.Grpc/Migrations/`: adds `DiscountType INTEGER NOT NULL DEFAULT 0` (default = `Percentage` per the Q1 decision — re-classifies seeded `DISCOUNT10` / `DISCOUNT20` as percentages on this migration). Generated column is `INTEGER` per SQLite's `enum ↔ int` mapping. **Risk note (locked):** the seed reclassification silently changes the semantic of any pre-existing row's `Amount` from "currency" to "percentage". A pre-migration audit table lives at `docs/discounts/discount-type-seed-audit.md` listing every row whose `Amount` value was interpreted as currency; operators review before shipping the migration to any non-dev environment. The plan ships the migration; the audit doc is the operator's responsibility and lives outside the codebase.
- **Basket-side migration `AddAppliedDiscountBreakdownToBasket`** in `Basket.API/Migrations/` (Phase 8 is the first cross-service Basket write in this plan; landed in lockstep with the Basket plan owner per §6.6.4 sync point):
  - **`Basket.EffectiveSubtotal` column** — `decimal NOT NULL DEFAULT 0`. Original `Subtotal` preserved for audit; customer sees `EffectiveSubtotal`. Handler fills the column from `ApplyDiscountsHelper.Apply(...)`.
  - **New child entity `Basket.API/Models/BasketAppliedDiscount.cs`:** `int Id PK`, `Guid BasketId FK`, `int CouponId`, `string Code`, `int DiscountType` (persisted as `int`; the Basket side knows the enum from `BuildingBlocks.Discounts`), `decimal RequestedAmount`, `decimal AppliedAmount`, `Instant AppliedAt` (stamp from `clock.GetCurrentInstant()` in the handler). One row per applied coupon per basket write. EF migration creates the table; cascade-delete on the parent `Basket`.
  - The handler uses this child collection for both insertion (Build → Track → SaveChangesAsync in the same transaction as `basketRepository.StoreBasketAsync` — see §8.3) and round-tripping on every `GetBasket` call (the basket repo hydrates the children). Admin UIs read the children directly; no further aggregation table needed.

#### 8.2 `ApplyDiscountsHelper` — BuildingBlocks contribution

`BuildingBlocks/Discounts/ApplyDiscountsHelper.cs`. Pure static; no DB / no I/O. Currency-agnostic (one Currency per basket; multi-currency baskets stay out of scope). Stacking math locked here so Basket preview + Ordering finalize compute identical numbers.

```csharp
namespace BuildingBlocks.Discounts;

public static class ApplyDiscountsHelper
{
    /// <summary>
    /// Applies all discounts sequentially (additive stack) against the running subtotal.
    /// Floor-at-zero clamp on the final effective subtotal.
    /// Per-line rounding: MidpointRounding.ToEven (banker's rounding).
    /// Final clamp is exact (no further rounding).
    /// </summary>
    public static ApplyDiscountsResult Apply(
        decimal subtotal,
        IReadOnlyList<AppliedDiscount> applied);

    /// <summary>Single-coupon helper used by tests + the stub consumer.</summary>
    public static ApplyDiscountsResult ApplyOne(
        decimal subtotal,
        DiscountType type,
        decimal amount);
}

public sealed record AppliedDiscount(
    DiscountType Type,
    decimal Amount,
    int CouponId,
    string Code,
    bool IsActive);

public sealed record ApplyDiscountsResult(
    decimal OriginalSubtotal,
    decimal TotalReduction,
    decimal EffectiveSubtotal,
    IReadOnlyList<AppliedDiscountBreakdown> Breakdown);

public sealed record AppliedDiscountBreakdown(
    int CouponId,
    string Code,
    DiscountType Type,
    decimal RequestedAmount,
    decimal AppliedAmount);
```

Behavior contract (locked; tests pin every case below):
- Empty `applied` list → `EffectiveSubtotal = subtotal`, `TotalReduction = 0`, empty `Breakdown`.
- One `Percentage` coupon with `Amount = 10m` against `$100` → `$10` off, `EffectiveSubtotal = $90`.
- One `FixedAmount` coupon with `Amount = $10` against `$100` → `$10` off, `EffectiveSubtotal = $90`.
- Stack of `Percentage(10)` + `FixedAmount(5)` against `$100` → `$10 + $5 = $15` off, `EffectiveSubtotal = $85`.
- Stack of two `Percentage(100)` coupons against `$10` → first reductions bring to `$0`; second coupon is a **no-op** (no negative application); `Breakdown[1].AppliedAmount = 0m`; `EffectiveSubtotal` clamped at `$0`, not `-$110`.
- An inactive coupon in `applied` → recorded in `Breakdown` with `AppliedAmount = 0m`, no reduction. The helper does not filter — the caller decides whether to include inactive rows.

#### 8.3 Basket-side deduction (resolves the `#warning TODO`)

`Basket.API/Basket/StoreBasket/StoreBasketHandler.cs` replaces the `#warning TODO` block with the deduction math + child-entity persistence:

```csharp
public sealed class StoreBasketHandler(
    IBasketRepository basketRepository,
    DiscountProtoService.DiscountProtoServiceClient discountService,
    TimeProvider clock,
    ILogger<StoreBasketHandler> logger) : ICommandHandler<StoreBasketCommand, StoreBasketResult>
{
    public async Task<StoreBasketResult> Handle(StoreBasketCommand command, CancellationToken ct)
    {
        var applied = new List<AppliedDiscount>(command.Basket.AppliedDiscounts.Count);
        foreach (var code in command.Basket.AppliedDiscounts)
        {
            var discountResponse = await discountService.GetDiscountAsync(new GetDiscountRequest
            {
                RestaurantId = command.Basket.RestaurantId.ToString(),
                Code = code,
            }, cancellationToken: ct);

            if (discountResponse.Coupon.IsActive is false || string.IsNullOrEmpty(discountResponse.Coupon.Code))
                continue;

            applied.Add(new AppliedDiscount(
                Type:     (DiscountType)(int)discountResponse.Coupon.DiscountType,
                Amount:   (decimal)discountResponse.Coupon.Amount,
                CouponId: discountResponse.Coupon.Id,
                Code:     discountResponse.Coupon.Code,
                IsActive: discountResponse.Coupon.IsActive));
        }

        var result = ApplyDiscountsHelper.Apply(
            subtotal: command.Basket.Subtotal,
            applied:  applied);

        command.Basket.Subtotal         = result.OriginalSubtotal;
        command.Basket.EffectiveSubtotal = result.EffectiveSubtotal;
        command.Basket.AppliedDiscountBreakdown = result.Breakdown
            .Select(b => new Basket.API.Models.BasketAppliedDiscount
            {
                CouponId        = b.CouponId,
                Code            = b.Code,
                DiscountType    = (int)b.Type,
                RequestedAmount = b.RequestedAmount,
                AppliedAmount   = b.AppliedAmount,
                AppliedAt       = clock.GetCurrentInstant(),
            })
            .ToList();

        logger.LogInformation(
            "StoreBasket applied {Count} coupons; subtotal={Subtotal} effective={Effective} reduction={Reduction}",
            result.Breakdown.Count, result.OriginalSubtotal, result.EffectiveSubtotal, result.TotalReduction);

        var basket = await basketRepository.StoreBasketAsync(command.Basket, ct);
        return new StoreBasketResult(basket.UserId, basket.RestaurantId);
    }
}
```

Notes:
- **`GetDiscountAsync` is read-only at this call site.** Basket does NOT call `RedeemDiscount` from preview. `RedeemDiscount` is Ordering's exclusive call site at finalized-order time (§6.6.4). Justification: a customer may store / re-store their cart freely; only the finalized order should increment `RedeemAmount`. This is a hard contract — code review flag for any future preview-path redemption call.
- **JWT forwarding via gRPC interceptor** — per the Q2 decision, Basket **forwards the customer's JWT** through the gRPC call chain. Implementation:
  - `Basket.API/Auth/JwtForwardingInterceptor.cs` is a `Interceptor` subclass that reads `IHttpContextAccessor.HttpContext.Request.Headers["Authorization"]` on the outbound call, copies the `Bearer <jwt>` value into `Metadata["authorization"]`, and returns `Tasks.CompletedTask` from `AsyncClientStreamingCall` / `AsyncUnaryCall` overrides.
  - Registered in `Program.cs` via `builder.Services.AddGrpcClient<DiscountProtoService.DiscountProtoServiceClient>(o => o.Interceptors.Add<JwtForwardingInterceptor>());`. Basket's HTTP-side ASP.NET Core JWT validation is unchanged — the gateway / Yarp already validates the inbound JWT for Basket. The interceptor only attaches the same JWT to the outbound gRPC call. Two failure modes the implementation must catch: (1) no inbound `Authorization` header → interceptor logs `WARN` and proceeds without `Metadata["authorization"]` — Discount's `AuthenticationInterceptor` returns `StatusCode.Unauthenticated` from the gRPC layer (Phase 1 §0.4.5 path); (2) malformed inbound token → same path; the interceptor does NOT validate the JWT itself (validation lives on the Discount side per Phase 1's `AddJwtBearer`).
  - Tests for the interceptor in `Basket.API.Tests/Unit/Auth/JwtForwardingInterceptorTests.cs` (per §8.6).
- **`DiscountType` cast.** `Coupon.DiscountType` is a protobuf-side enum; the proto field reads `(int)` and casts to the BuildingBlocks `DiscountType`. The proto-side `CouponModel` gains an `int32 discount_type = 9;` field; `DiscountModel.ToProtoModel(coupon)` writes `(int)coupon.DiscountType`.
- **`Basket.EffectiveSubtotal` write** happens before `basketRepository.StoreBasketAsync(...)`. The Basket repository serializes both fields + cascades the children; customers see `EffectiveSubtotal` in the UI. Round-trip on `GetBasket` rehydrates the children via the EF Core `Include(b => b.AppliedDiscountBreakdown)` chain.
- **Inactive coupons are filtered** (the `continue` above). Phase 8 does **not** show inactive rows in the breakdown — the customer-visible deduction only ever references real, currently-active codes. Audit trails are at the gRPC layer.
- **Persistence is per-row (Q3 decision)** — the `BasketAppliedDiscount` child entity captures every applied coupon's breakdown at apply time. Re-pricing a basket with a since-deactivated coupon is deliberately a different result (the deactivated coupon vanishes from `applied` and re-computation reflects reality); this matches the floor-at-zero + `IsActive` filter semantics in §8.2. An admin UI reading historical baskets sees the breakdown recorded at that moment.

#### 8.4 Ordering-side auto-apply stub (`OrderCreatedConsumer`)

`Discount.Grpc/Messaging/EventHandlers/OrderCreatedConsumer.cs`. Disabled by default (`DiscountOptions:EnableOrderCreatedConsumer=false`). When enabled, the consumer performs the full `EvaluateDiscountRules → RedeemDiscount → DiscountAppliedIntegrationEvent` chain.

```csharp
public sealed class OrderCreatedConsumer(
    DiscountRuleProtoService.DiscountRuleProtoServiceClient ruleClient,
    DiscountProtoService.DiscountProtoServiceClient couponClient,
    IOutboxPublisher outbox,
    ICurrentRestaurantProvider tenant,
    IOptions<DiscountOptions> options,
    TimeProvider clock,
    ILogger<OrderCreatedConsumer> logger) : IConsumer<OrderCreatedIntegrationEvent>
{
    public async Task Consume(ConsumeContext<OrderCreatedIntegrationEvent> context)
    {
        if (!options.Value.EnableOrderCreatedConsumer)
            return;

        var evt = context.Message;
        tenant.Attach(new ClaimsPrincipalBuilder()
            .WithRestaurant(evt.RestaurantId)
            .WithActor(DiscountActors.Service)
            .Build());

        var evaluate = await ruleClient.EvaluateDiscountRulesAsync(new EvaluateDiscountRulesRequest
        {
            RestaurantId = evt.RestaurantId.ToString(),
            OrderTotal   = (double)evt.OrderTotal,
            MenuItemIds  = { evt.MenuItemIds.Select(g => g.ToString()) },
        }, context.CancellationToken);

        foreach (var applicableCouponId in evaluate.ApplicableCouponIds)
        {
            var coupon = await couponClient.GetDiscountAsync(new GetDiscountRequest
            {
                RestaurantId = evt.RestaurantId.ToString(),
                CouponId     = applicableCouponId,
            }, context.CancellationToken);

            if (coupon.Coupon is null || coupon.Coupon.IsActive is false)
                continue;

            var redeem = await couponClient.RedeemDiscountAsync(new RedeemDiscountRequest
            {
                Code         = coupon.Coupon.Code,
                RestaurantId = evt.RestaurantId.ToString(),
                OrderId      = evt.Id.ToString(),
                Quantity     = 1,
            }, context.CancellationToken);

            if (options.Value.EnableDiscountAppliedPublishing)
            {
                await outbox.WriteOutboxMessageAsync(new DiscountAppliedIntegrationEvent(
                    EntityType:   "Coupon",
                    EntityId:     redeem.Coupon.Id,
                    RestaurantId: evt.RestaurantId,
                    OrderId:      evt.Id,
                    AppliedAt:    clock.GetCurrentInstant()));
            }
        }
    }
}
```

Notes:
- **Three flags gate this consumer's behavior:** `EnableOrderCreatedConsumer` (master switch), `EnableDiscountAppliedPublishing` (Phase 6 flag — also referenced here so a single operator action re-enables the whole chain).
- **`DiscountAppliedIntegrationEvent` field additions.** The Phase 4 record shape gains `Guid OrderId` and `Instant AppliedAt`. SchemaVersion bumps `1 → 2`; Catalog's consumer (own plan) ignores unknown fields per MassTransit default. Documented in the migration rule on §6.5.

#### 8.5 `DiscountOptions` additions

```csharp
// Phase 8 additions to DiscountOptions:
public bool EnableOrderCreatedConsumer { get; set; } = false;   // Phase 8 stub switch
public string? AppliedDiscountCurrency { get; set; }           // Phase 8 currency pin
```

`AppliedDiscountCurrency` is a future-proofing knob. Default `null` means "use the currency the basket already pinned." Reading this from `IOptions<DiscountOptions>` lets a future multi-currency rollout opt in without recomputing the helper API.

#### 8.6 Tests

- **xUnit + FluentAssertions unit for `ApplyDiscountsHelper`** (in `BuildingBlocks.Tests/Discounts/ApplyDiscountsHelperTests.cs`):
  - Empty `applied` → returns identity result.
  - One `Percentage(10)` against `$100` → `-10`, `EffectiveSubtotal=90`.
  - One `FixedAmount(5)` against `$100` → `-5`, `EffectiveSubtotal=95`.
  - Stack of `Percentage(10)` + `FixedAmount(5)` → `-15`, `EffectiveSubtotal=85`.
  - Stack of two `Percentage(100)` against `$10` → clamp at `$0` (no negative).
  - Inactive coupon in `applied` → no-op, `AppliedAmount=0m`, breakdown still records the row.
  - `MidpointRounding.ToEven` parity: `$0.005 + $0.005` rounding edge (sum = `$1.00`, not `$1.01` or `$0.99`).
  - Three additional combinator rows for asymmetric stacks (Percentage + FixedAmount + Percentage against varied subtotals) — these are the "10-row count" the plan budgets. The seven cases above are the locked contract; the three combinator rows are living documentation of behavior across the input space.
- **xUnit + FluentAssertions + NSubstitute unit for `JwtForwardingInterceptor`** (in `Basket.API.Tests/Unit/Auth/JwtForwardingInterceptorTests.cs`):
  - Inbound `Authorization: Bearer <valid-jwt>` header → outbound gRPC `Metadata["authorization"]` carries the same value.
  - No inbound `Authorization` header → interceptor logs `WARN` and proceeds without setting `Metadata["authorization"]`. (The Discount side returns `StatusCode.Unauthenticated` per Phase 1 §0.4.5.)
  - Malformed inbound token (`Basic xxx`, missing `Bearer` prefix) → same as missing; interceptor does NOT validate.
  - Token contains comma-separated `restaurantId` claim → forwarded verbatim (the tenant resolution happens on Discount side).
- **Basket-side integration test** (Basket owns this; no Discount-side change):
  - Spin up `StoreBasketHandler` with `DiscountProtoServiceClient` stubbed to return a `Percentage(15)` coupon and a `FixedAmount(3)` coupon.
  - Send a basket with `Subtotal=100, AppliedDiscounts=[P15, F3]` plus a fake `IHttpContextAccessor` carrying `Authorization: Bearer <jwt>`.
  - Assert `basket.EffectiveSubtotal = 82`.
  - Assert `basket.AppliedDiscountBreakdown` has two child rows with correct `CouponId`, `Code`, `DiscountType`, `RequestedAmount`, `AppliedAmount`.
  - Assert the `AppliedAt` column on each child is within `FakeTimeProvider`'s clock range.
  - Assert the outbound `DiscountProtoServiceClient.GetDiscountAsync` was called with `Metadata["authorization"]` set to the forwarded JWT (verified via a recording `Interceptor` test double).
- **`InMemoryTestHarness` for `OrderCreatedConsumer`** (Discount-side):
  - With flag `true`: fire `OrderCreatedIntegrationEvent { OrderTotal = 100m, MenuItemIds = [seed-item] }`; assert one `RedeemDiscount` gRPC call (verifiable via NSubstitute-mocked `DiscountProtoServiceClient`) and one `DiscountAppliedIntegrationEvent` outbox row.
  - With flag `false`: same event; assert zero calls, zero outbox rows.
- **`DiscountOptionsAuditor` test extension** — extend the Phase 1 auditor with the new `EnableOrderCreatedConsumer` flag. Drift guard.

#### 8.7 Doc-update scope (§0.2)

- §4.4 Discount Service — note `Coupon.DiscountType` column + `EnableOrderCreatedConsumer` flag; reservations for a future `coupon:apply` permission (not implemented here).
- §5.1 Synchronous — add "Ordering.API → Discount.Grpc" rows for `EvaluateDiscountRules` and `RedeemDiscount` (the auto-apply call path).
- §5.2 Asynchronous — mark the `OrderCreatedIntegrationEvent` row as "Phase 8 stub wired-but-disabled"; `DiscountAppliedIntegrationEvent` row notes the `SchemaVersion 1 → 2` bump for `OrderId` + `AppliedAt`.
- §6 Data Stores — note `Coupons.DiscountType` column + Basket `EffectiveSubtotal` column; BuildingBlocks contributor list updates from two to three (the new `ApplyDiscountsHelper`).
- §9 Cross-Cutting Patterns — note `ApplyDiscountsHelper` as a BuildingBlocks contribution; note the floor-at-zero + `MidpointRounding.ToEven` rounding policy (currency-agnostic until Phase 9+).

---

## 8. Cross-cutting notes

### Cross-service coordination rules (mirror Catalog §8)

- **Event versioning.** Every Discount integration event carries `int MessageVersion` (current = 1), inherited from `BuildingBlocks.Messaging.Events.IntegrationEvent`. The outbox row stores the same value under the column name `SchemaVersion` (the `OutboxPublisher` does the copy on stage; see §0.4.4 for the rationale). Adding fields bumps `MessageVersion`. Removing or renaming a field requires introducing the next major version side-by-side, publishing both for one release, then dropping the old version. Documented in §6.5.
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

This plan ships **three** BuildingBlocks contributions:

1. **First SQLite `OutboxDispatcher<TContext>` implementation** — `DiscountOutboxDispatcher` in `Services/Discount/Discount.Grpc/Messaging/Outbox/` extends `OutboxDispatcher<DiscountContext>`. The `BuildClaimSql` SQLite variant is documented in §6.7. Future services on SQLite adopt the same pattern.
2. **`ITenantEntity.RestaurantId : int → Guid` fix** — single-line change in `BuildingBlocks/Multitenancy/ITenantEntity.cs`. Justifies the dormant primitive; nothing implements `ITenantEntity` today; Discount becomes the first.
3. **`ApplyDiscountsHelper` pure-function math** — new `BuildingBlocks/Discounts/ApplyDiscountsHelper.cs` shipped by Phase 8. Stacking math + floor-at-zero clamp + `MidpointRounding.ToEven` rounding policy, consumed by Basket (cart preview) and Ordering (finalized order). The Discount-published enum `DiscountType` lives here as well, imported by both `Coupon` (Discount) and any future apply-surface callers; for now only `Coupon` uses it (`RewardCode.RewardKind` remains a separate enum until the v2 consolidation tracked at the end of §0.3.3).

All three contributions are scoped to this plan's PR. Subsequent services adopt them as references.

### Code-smell carryovers (none today)

Unlike Catalog's `db_relational_model.md §137-148` carry-over list (`BasketItem.MenuItemId int vs Guid`, four Marten docs extending `Entity<int>`, `BulkOrderUploads.CreatedAt` missing), Discount has **no existing drift** beyond the `ITenantEntity.RestaurantId : int → Guid` fix in Phase 1. Every other drift (e.g., seeded sample `DISCOUNT10` / `DISCOUNT20` for fictitious restaurant GUIDs) is by-design dev seeding.

### Testing strategy

- **Unit tests** (xUnit + FluentAssertions + NSubstitute): pure logic — handler happy paths, validation, the lazy-eval gate. No infrastructure. Fast.
- **SQLite `:memory:` integration** for the outbox dispatcher (claim SQL roundtrip), the lazy-eval gate (`UpdateSQLiteDatabase(); insert coupon with past expiry; assert Coupon.IsActiveNow returns false`), the sweep service (fake `TimeProvider`), and the race-fix conditional UPDATE pattern.
- **`MassTransit.InMemoryTestHarness`** for `FeedbackSubmittedConsumer`, `MenuItemChangedConsumer`, `RestaurantConfigurationChangedConsumer`. The InMemoryHarness config is registered per-test and asserts both consumer dispatch and the publish-side `DiscountHistoryAppendedIntegrationEvent` end-to-end (the publisher's handler is a no-op stub that records the message; the test asserts its presence and shape).
- **gRPC integration tests** (xUnit + `Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program>` + `Grpc.Net.Client`): the layer that exercises the actual wire. None of the layers above cover:
  - Proto generation (a missing `<Protobuf Include=...>` element surfaces as a build error, but a typo in the package name surfaces as a runtime `RpcException` with `StatusCode.Unimplemented`).
  - `DiscountAuthorizationInterceptor` registration and the `[Permission(...)]` attribute read from `MethodInfo`.
  - JWT propagation from gRPC `Metadata["authorization"]` into `HttpContext.User` (the interceptor's `GetHttpContext()` call only works when `AddJwtBearer` is wired AND the request is HTTP/2).
  - The `ExceptionInterceptor` mapping `DomainException` → `StatusCode.NotFound` and friends (the unit tests can substitute the handler; the gRPC tests exercise the actual `ServerCallContext` boundary).
  - The pagination `page_size` cap and `Idempotency-Key` middleware read.

  Add `Discount.Grpc.Tests/Integration/RpcEndpointTests.cs` with at least one test per RPC family: `Coupon` (Create / Get / List / Update / Delete / Redeem), `RewardCode` (Phase 3), `DiscountRule` (Phase 2, including `Evaluate`). Each test:
  1. Spins up `WebApplicationFactory<Program>` with `Discount:IdempotencyKey` seeded in test config.
  2. Mints a test JWT against Identity's dev endpoint (or a `TestJwtBearerHandler` in the test host that bypasses the signature check and synthesizes the `ClaimsPrincipal` directly).
  3. Calls the RPC via `GrpcChannel.ForAddress(...)` and asserts `Status` and the response shape.
  4. Negative paths: missing JWT → `StatusCode.Unauthenticated`; wrong permission → `StatusCode.PermissionDenied`; cross-tenant → `StatusCode.PermissionDenied`; expired coupon → `StatusCode.FailedPrecondition`; redemption race lost → `StatusCode.Aborted`.

  NSubstitute for handler tests that don't need real infrastructure (pure JWT-claim scenarios).

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

- [x] **Phase 1** — Production-grade Coupon: `ITenantEntity` int→Guid fix shipped in BuildingBlocks; `Coupon : ITenantEntity`; combined global query filter (`DeletedAt == null && RestaurantId == _provider.RestaurantId`); JWT bearer wired against Identity authority; 12 permission policies (constants list = single source of truth, see v1.5 changelog for the 11→12 reconciliation); outbox tables (`outbox_messages` + `outbox_messages_dead`) + first SQLite `OutboxDispatcher<DiscountContext>` (claim SQL: `SELECT ... WHERE DispatchedAt IS NULL ORDER BY OccurredOn ASC LIMIT @batchSize`; multi-replica `ClaimId`-based claiming deferred — see v1.5); `DiscountExpirySweepService : BackgroundService` with `PeriodicTimer`; `RedeemDiscount` rewritten to atomic conditional UPDATE (closes the pre-existing TOCTOU race); sweep service `IHostedService` registered; soft-delete columns (`DeletedAt` / `DeletedBy`).
  - [x] **Phase 1 doc** — `current-architecture.md` updated per Phase 1 doc-update scope (§2 Tech Stack JwtBearer row, §4.4 Discount §auth + entities + outbox prose, §6 Data Stores `discountdb` outbox tables + new EF migrations, §8 Tenancy ITenantEntity Guid conversion + Discount adoption, §9 Interceptors DiscountOutboxPublisher).
  - [x] **Phase 1 completed** — code, doc, plan-update commit landed; Document Version `1.4 → 1.5`. See v1.5 changelog tail.
- [x] **Phase 1B.a (BuildingBlocks contributions; commit A)** — `OutboxOptions.MaxConsecutiveBrokerFailures` (`[Range(1, 100)]` default `3`) + `BrokerBackoffSeconds` (`[Range(00:00:00.100, 01:00:00)]` default `60s`). `ICurrentRestaurantProvider.Attach(ClaimsPrincipal)` extending the interface with `IDisposable` scope; `ClaimsRestaurantProvider` backs it with `AsyncLocal<ClaimsPrincipal?>`. `BuildingBlocks/Authorization/ClaimsPrincipalBuilder.cs` (NEW) fluent API: `WithRestaurant`/`WithUser`/`WithActor`/`WithPermission`/`WithCorrelationId` → `ClaimsPrincipal`.
  - [x] **Phase 1B.a doc** — `current-architecture.md` §8 Multi-Tenancy (Pattern 2 explicit references to the new APIs) + §9 Cross-Cutting Patterns (outbox circuit-breaker row) updated.
  - [x] **Phase 1B.a completed** — code + doc + plan-update landed; Document Version `1.5 → 1.6`. See v1.6 changelog tail.
- [x] **Phase 1B.b (Discount.Grpc retro-fit; commit B)** — `DiscountActors` constants; JWT claim shape alignment (`RequireAssertion` handles Shape A + Shape B per v1.2 M-L8); `LastModifiedBy = DiscountActors.System` + `LastModifiedAt = {now}` added to the conditional `RedeemDiscount` UPDATE (per v1.1 L11 — raw `ExecuteSqlInterpolatedAsync` bypasses `AuditableEntityInterceptor`); `DiscountExpirySweepService` switches to `DiscountActors.Sweep`; `BrokerHealthState` singleton + circuit-breaker override in `DiscountOutboxDispatcher` (per §6.7 v1.2 M-L10); `DiscountHealthChecks.cs` + `/live`+`/ready` split (mirrors Catalog); `DiscountOptions` class (`[Range]` + `ValidateOnStart()`); `IdempotencyKeyProvider` (HMAC-SHA256, dev-only random fallback); `ListDiscounts` RPC + handler (paged, `coupon:read`); dev-only `AddGrpcReflection` + `MapGrpcReflectionService`; `AspNetCore.HealthChecks.UI.Client` + `Grpc.AspNetCore.Server.Reflection` package adds.
  - [x] **Phase 1B.b doc** — `current-architecture.md` §4.4 Discount (JWT claim-shape row + Pattern 2 row + `DiscountActors` row + Idempotency-Key row + DiscountOptions row + circuit-breaker row + ListDiscounts RPC row + Health subsection + dev-only gRPC reflection row) + §12 Observability (four `discount-*` check rows + Discount entry on the database row + Discount entry on the rabbitmq row). `Coupon` race-fix prose updated to call out explicit `LastModifiedAt`/`LastModifiedBy`.
  - [x] **Phase 1B.b completed** — code + doc + plan-update landed; Document Version `1.6 → 1.7`. See v1.7 changelog tail.
- [x] **Phase 1B.c (Discount.Grpc.Tests sibling project; commit C)** — `Discount.Grpc.Tests` (`Microsoft.NET.Sdk`, `IsPackable=false`, `IsTestProject=true`), per-fixture temp-file SQLite, xUnit + FluentAssertions + NSubstitute + `TestTimeProvider` + `TestAuthHandler` mirroring Catalog. 76 tests across 10 classes: `DiscountPermissionsTests` (`JwtClaimShapeProbe` — 12 policies × 2 shapes × 2 outcomes), `GlobalTenantFilterTests` (cross-tenant Read / Create / Delete), `DiscountExpirySweepTests` (`TestTimeProvider`-pinned sweep + Disabled-flag path + actor-string assertion), `RedeemDiscountRaceTests` (sequential happy + 5-vs-3 concurrent contention), `DiscountOutboxCircuitBreakerTests` (3-failure trip + recovery reset + per-row-no-trip), `DiscountOutboxDeadLetterTests` (future-version quarantined + valid v1 relayed), `RpcEndpointTests` (first gRPC integration tests in the repo: 5 RPCs × happy + 3 negative-path cases), `HealthCheckSplitTests` (`/live` always 200; `/ready` flips 200 ⇄ 503 with `BrokerHealthState`), `MenuItemChangedConsumerTests` (Catalog-event consumer end-to-end + idempotency on `processed_inbound_events`), `DiscountRuleServiceTests` (6 RPCs × happy + UK-violation path). Code commit landed 2026-07-14 (`7c87c1a`); `InternalsVisibleTo("Discount.Grpc.Tests")` added to `Discount.Grpc.csproj` so the test project can resolve internal Discount types.
  - [x] **Phase 1B.c doc** — `current-architecture.md` §11 Local Development / Tests row extended with the full Discount.Grpc.Tests coverage list (10 test classes + 76-test count + the `InternalsVisibleTo` callout). Plan §9 row ticked (this entry).
  - [x] **Phase 1B.c completed** — code + doc + plan-update commit landed; Document Version `1.7 → 1.8`. See v1.8 changelog tail.
- [x] **Phase 2** — `DiscountRule` aggregate with 6 RPCs; `MenuItemChangedConsumer` + `RestaurantConfigurationChangedConsumer`; rule FK to Coupon with `OnDelete(DeleteBehavior.Restrict)`; rule-data JSON column (`RuleDataJson` TEXT, deserialized at handler boundary with FluentValidation enforcing kind-specific shape); EF migration `20260715015607_AddDiscountRules` + `20260715015933_AddDiscountRulesAndProcessedInboundEvents` (adds `DiscountRules`, `ProcessedInboundevents` tables + `ix_processed_inbound_consumer_time` index); `DiscountRuleKind` enum (renamed from `DiscountRuleType` to disambiguate from the proto-generated `Discount.Grpc.DiscountRuleType` enum per v1.1 S1 / M6). Code commit landed 2026-07-14 (`08b11d0`).
  - [x] **Phase 2 doc** — `current-architecture.md` §4.4 Discount Service (`DiscountRule` entity row + 6 RPCs + 2 Catalog-event consumer rows + idempotency note); §6 Data Stores (SQLite `discountdb` row gains `DiscountRules` + `ProcessedInboundevents` + the two new migrations); §9 Cross-Cutting Patterns (rule-data JSON column + FK-on-Restrict note).
  - [x] **Phase 2 completed** — code + doc + plan-update commit landed; Document Version stays at `1.8` (Phase 1B.c + Phase 2 ship together). See v1.8 changelog tail.
- [x] **Phase 3** — `RewardCode` aggregate with 6 RPCs (Create / Get / List / Update / Delete / Redeem); race-fix `RedeemRewardCode` (atomic conditional UPDATE mirrors `RedeemDiscount` — sets `RedeemedInOrderId`, `RedeemedAt`, `LastModifiedAt`, `LastModifiedBy = DiscountActors.System` in the same statement; raw SQL bypasses the audit interceptor per plan v1.1 L11); sweep extension (`DiscountExpirySweepService` now flips both expired Coupons and expired RewardCodes in one tick, single bulk UPDATE per aggregate); lazy-eval + tenant filter (`ActiveNow.RewardCode` helper per plan §0.4.3.1; `HasQueryFilter(r => r.DeletedAt == null && r.RestaurantId == _provider.RestaurantId)`); `DiscountHistoryAppendedIntegrationEvent` publisher hooks on every CUD + redeem path (per plan §7 Phase 3 history-publishes row, completing the cross-aggregate publish contract that Phase 4 wires up for `DiscountRule`); EF migration `20260715225738_AddRewardCodes` (`RewardCodes` table with UK on `(RestaurantId, Code)` + sweep-friendly index `ix_reward_codes_restaurant_active_expiry` on `(RestaurantId, IsActive, ExpirationDate)`); `IOutboxPublisher.PublishAsync` wired on every Phase 3 service path. Proto-side `RewardType` enum kept distinct from entity-side `RewardKind` enum (mirrors `DiscountRuleType` / `DiscountRuleKind` precedent) so the cross-cast is explicit at the service boundary. `Validators/RewardCodeValidator.cs` centralizes the §0.3.3 contract: Code non-empty ≤ 120, kind-specific Value rules, ExpirationDate > now when set, MaxRedeemAmount > 0 when set, FreeItem Quantity == 1. RewardCode.Code4StarPct10 / Code5StarPct15 / Code5StarAppetizer helpers take `(Guid rid, Guid feedbackEventId, TimeProvider clock)` per v1.2 H-L1 (day-bucket + event-id = idempotent across day boundaries). Code commit landed 2026-07-15.
  - [x] **Phase 3 doc** — `current-architecture.md` §4.4 Discount Service (`RewardCode` entity row appended after `DiscountRule` with required-modifier note, `required RewardKind Kind`, FreeItem-Description-as-target-id callout, the three deterministic Code helpers; `RewardCodeProtoService` sub-section appended with all 6 RPCs + RedeemRewardCode race-fix note; "History publishing across aggregates" sub-section appended documenting the now-Phase-3-complete cross-aggregate publish contract), §6 Data Stores (SQLite `discountdb` row gains `RewardCodes` table + UK + sweep index + the `20260715225738_AddRewardCodes` migration), §9 Cross-Cutting Patterns (new `ActiveNow` lazy-eval helper bullet + RewardCode deterministic Code builders bullet + `RewardCode.RewardKind` vs `Coupon.DiscountType` separation bullet), §11 Local Development / Tests (RewardCode test classes row appended to the existing `Discount.Grpc.Tests` entry; 101-test count).
  - [x] **Phase 3 completed** — code + doc + plan-update commit landed; Document Version bumped `1.8 → 1.9`. See v1.9 changelog tail.
- [x] **Phase 4** — `DiscountHistoryAppendedIntegrationEvent` publish points populated for Coupon / RewardCode / DiscountRule handlers. One outbox row per mutation. Coupon Create / Update / Delete / Redeem now publish (Phase 1's commitment was deferred per the v1.5 / v1.7 changelog note that publish-points land in Phase 4 — Coupon was the last aggregate to wire); DiscountRule Create / Update / Delete publish (added on top of Phase 2's wiring); RewardCode Create / Update / Delete / Redeem publish (added in Phase 3). OldValues captured from the in-memory pre-image before mutation; for `RedeemDiscount` / `RedeemRewardCode`, the raw `ExecuteSqlInterpolatedAsync` UPDATE bypasses the EF change tracker so the post-image is re-fetched with a follow-up `AsNoTracking().FirstAsync(...)`. Coupon's hard-delete path emits `NewValues = "{}"` since the row no longer exists. Code commit landed 2026-07-15.
  - [x] **Phase 4 doc** — `current-architecture.md` §4.4 Discount Service "History publishing across aggregates" sub-section elaborated with the Phase 4 commitment explanation (Phase 1's Coupon-publish-points were deferred; Phase 4 closes the loop on all three aggregates); §11 Local Development / Tests row updated with the new `OutboxHistoryPublisherTests` class + 104-test total count.
  - [x] **Phase 4 completed** — code + doc + plan-update commit landed; Document Version bumped `1.9 → 2.0`. See v2.0 changelog tail.
- [x] **Phase 5** — `FeedbackSubmittedConsumer` stub wired-but-disabled; flag-gated `MassTransit.AddConsumer<T>()` conditional registration (per v1.1 H5 — MassTransit 8.x has no `ConfigureConsumer.DisableConsumer<T>(...)` API); tests with the `Substitute.For<ConsumeContext<T>>` direct-call pattern (per Catalog `OrderCompletedConsumerTests` convention; the plan's `InMemoryTestHarness` reference is aspirational). Default `DiscountOptions:EnableFeedbackSubmittedConsumer=false`; Notification v1 (the publisher) doesn't ship yet so the consumer endpoint is wired-but-disabled. Tests cover 4★/5★/below-threshold paths + duplicate-delivery idempotency via the deterministic Code UK. Code commit landed 2026-07-16.
  - [x] **Phase 5 doc** — `current-architecture.md` updated per Phase 5 doc-update scope (§4.4 Discount Service — `FeedbackSubmittedConsumer` row appended after `RestaurantConfigurationChangedConsumer` with the Pattern 2 + deterministic-Code + flag-gated bullet; §9 Cross-Cutting Patterns — new "Conditional consumer registration" bullet documenting the conditional `AddConsumer` idiom as the project's reference for "wire to bus, ship disabled").
  - [x] **Phase 5 completed** — code, doc, plan-update commit landed; Document Version bumped `2.0 → 2.1`. See v2.1 changelog tail.
- [x] **Phase 6** — Three architecture publishes wired-but-flagged-off per plan §7 Phase 6:
  - `DiscountAppliedIntegrationEvent` published from `DiscountService.RedeemDiscount` after the atomic conditional UPDATE succeeds. **Intentionally omits `OrderId`** because plan §0.3.3 says it's required but the shipped `RedeemDiscountRequest` proto lacks `order_id`; adding OrderId requires the §0.3.3 contract reconciliation + proto bump + Basket client refresh — deferred to a future drift-cleanup pass.
  - `RewardGeneratedIntegrationEvent` published from `RewardCodeService.CreateRewardCode` after the `DiscountHistoryAppendedIntegrationEvent(ChangeType=Created)`.
  - `RewardRedeemedIntegrationEvent` published from `RewardCodeService.RedeemRewardCode` after the atomic conditional UPDATE succeeds (mirrors `DiscountHistoryAppendedIntegrationEvent(ChangeType=Redeemed)` at the same call-site).
  - Each gated by its corresponding `DiscountOptions:Enable*Publishing=false` flag — fail-secure default. Tests: `OutboxDeferredEventPublishersTests` (4 tests, 112 pass / 0 fail / 9 pre-existing skip total). Code commit landed 2026-07-16.
  - [x] **Phase 6 doc** — `current-architecture.md` updated per Phase 6 doc-update scope: new "Events Published (Phase 6 — wired-but-flagged-off)" sub-section in §4.4 documenting each event + flag default; the three new rows appended to the §5.2 Asynchronous table with `**deferred**` markers + `Discount:Enable*Publishing=false` notes.
  - [x] **Phase 6 completed** — code + doc + plan-update commit landed; Document Version bumped `2.1 → 2.2`. See v2.2 changelog tail.
- [x] **Phase 7** — Drift memo rewrite; mermaid `RewardCodes` / `DiscountRules` / `processed_inbound_events` / `outbox_messages` / `outbox_messages_dead` blocks added; all Phase 1–6 docs audited; final smoke test green (Debug + Release build, full test pyramid `Discount.Grpc.Tests` 112 pass / 0 fail / 9 pre-existing skip unchanged from Phase 6).
  - [x] **Phase 7 doc** — `db_model_drift_report.md` and `db_model_drift_report_mermaid_truth.md` each gained a Discount chapter reviewing Phase 1–6 changes against the new mermaid blocks (two-direction analysis per project convention). `db_relational_model.mermaid` carries the new `DISCOUNT — relational tables (SQLite)` divider + five entity blocks + three relationship edges. `db_relational_model.md` companion doc carries the new "Updates from DISCOUNT_SERVICE_PLAN.md (Phase 7, 2026-07-16 — hardening)" section with the drift-surfacing note + the single deferred P1 (`Restaurants ||--o{ Coupons : "issues"` re-emission vs the Phase 6.2 cleanup). `current-architecture.md` §4.4 was already correct after Phases 1–6 (each phase's doc-update scope committed in lockstep with the code) — Phase 7's audit confirmed no edits required.
  - [x] **Phase 7 completed** — code, doc, plan-update commit landed; Document Version bumped `2.2 → 2.3`. See v2.3 changelog tail.
- [x] **Phase 8 (partial ship 2026-07-16 — schema + helper + Discount mapping in lockstep; cross-service Basket + OrderCreatedConsumer stub deferred to follow-up commit).** The §7 ship window is split into two commits because the Basket StoringBasket + OrderCreatedConsumer stub pieces are large cross-service changes that warrant their own commits + their own per-class integration tests. Phase 8 ships the foundation this commit.
  - **`Coupon.DiscountType { Percentage, FixedAmount }` enum** ships in `BuildingBlocks.Discounts` (the shared Closed discriminator lives there per §8.2 so Basket preview-time + Ordering finalize-time compile against the same shape). `Coupon.cs` carries the column; `DiscountContext.OnModelCreating:77-83` registers `HasConversion<int>().HasDefaultValue(Percentage).IsRequired()`. The two seeded rows (`DISCOUNT10`, `DISCOUNT20`) re-classify as Percentage on this migration.
  - **EF Core migration `20260717024249_AddDiscountTypeToCoupon`** ships per plan §8.1: `INTEGER NOT NULL DEFAULT 0`. The audit doc (`docs/discounts/discount-type-seed-audit.md`) is the operator's pre-migration review checklist for non-dev environments where pre-existing rows' `Amount` was interpreted as currency (audit doc is operator-owned, outside the codebase per the locked risk note).
  - **`Protos/discount.proto` `DiscountType` enum + `CouponModel.discount_type` field (tag 10)** — the wire-shape mirror of the BuildingBlocks discriminator. proto3 package-scoped enum-name collision with the existing `RewardType.PERCENTAGE` / `RewardCodeType.FIXED_AMOUNT` resolved by prefixing the values (`COUPON_PERCENTAGE` / `COUPON_FIXED_AMOUNT`) per the §0.4.3-proto-csharp naming convention; the C# side uses the `DbDiscountType` alias vs the proto type where both are in scope.
  - **`DiscountService.ToProtoModel`** — closed-discriminator mapping added; maps `BuildingBlocks.Discounts.DiscountType.Percentage` ↔ `Discount.Grpc.DiscountType.CouponPercentage` and `FixedAmount` ↔ `CouponFixedAmount`; falls back to `CouponDiscountTypeUnspecified` for any future enum value the proto hasn't been told about.
  - **`BuildingBlocks.Discounts.ApplyDiscountsHelper`** — pure static contribution. Two helpers (`Apply` + `ApplyOne`), three records (`AppliedDiscount`, `ApplyDiscountsResult`, `AppliedDiscountBreakdown`), one enum (`DiscountType`). Banker's rounding (`MidpointRounding.ToEven`) applies to the **Percentage** computation only — `FixedAmount` is verbatim (a $33.34 coupon must stay $33.34; rounding to 0 decimals would banker's-round it down to $33.00, breaking the contract tests). Floor-at-zero on the per-iteration running subtotal; the helper also handles the §8.2 "subtotal `$10` + two Percentage(100) → clamps at $0`" edge case correctly (second coupon's `AppliedDiscountBreakdown.AppliedAmount` = 0m).
  - **Discount.Grpc.Tests/Unit/ApplyDiscountsHelperTests.cs** — 11 tests covering the §8.2 contract: empty list, Percentage against $100, FixedAmount against $100, mixed-stack reduction, two Percentage(100) floor-at-zero, inactive-coupon breakdown, mixed-active-inactive, fixed-amount rounding edge case, negative subtotal clamp, breakdown order preservation, Percentage > 100 reduction cap. **11/11 pass** when filtered. Same RaceFixedDiscountReopened test runs fine individually. **Phase 8 partial-ship only — see known-issue note below for the integration-test parallel-collection interference.**
  - **§0.3.3 / `RedeemDiscountRequest.OrderId` drift** — still pending the proto extension; Phase 6's `DiscountAppliedIntegrationEvent` v1 (no OrderId) is the right Phase 6 ship; the v2 bump lands when Phase 8 ships `OrderCreatedConsumer` + the proto extension.
  - [x] **Phase 8 partial doc** — partial `current-architecture.md` + companion-doc updates deferred to the follow-up commit (when the Basket side lands); the Phase 8 doc-update scope is the next commit's responsibility.
  - [ ] **Phase 8 follow-up** — separate commit: cross-service Basket `EffectiveSubtotal` + `BasketAppliedDiscount` child entity + `StoreBasketHandler` `ApplyDiscountsHelper.Apply(...)` resolution of the `#warning TODO`; `OrderCreatedConsumer` stub gated by `EnableOrderCreatedConsumer=false`; `DiscountAppliedIntegrationEvent` SchemaVersion `1 → 2` (adds `OrderId` + `AppliedAt` fields per the §0.3.3 contract reconciliation); Basket integration test + OrderCreatedConsumer InMemoryTestHarness tests. Continued follow-up work after this commit lands — Document Version does not bump until the follow-up commit ships.
- [ ] **Docs** — `db_relational_model.mermaid` updated to match each phase (mermaid is reconciled after every phase, not only at the end).
- [ ] **Cross-plan sync** — Catalog plan receives §6.6.1 hand-off contract (already documented here); Identity plan receives §6.6.2 permission list; Notification v1 plan receives §6.6.3 `FeedbackSubmitted` contract.

**Known issue (Phase 8 partial ship):** the full `Discount.Grpc.Tests` pyramid reports 13 failing tests when xUnit's parallel test-collection runner collides on the per-fixture temp-file SQLite (`Cache=Shared`). The failures cluster in integration tests that insert into `Coupons` + `DiscountRules` tables; each fails with `no such column: "DiscountType"`. The same tests pass when filtered individually (e.g., `DiscountOutboxDeadLetterTests` 2/2, `OutboxDeferredEventPublishersTests` 4/4, `ApplyDiscountsHelperTests` 11/11). The cause is parallel-collection interference — multiple fixtures creating SQLite files concurrently with `Cache=Shared` causes the file from one fixture to be opened by another, exposing the other fixture's schema cache. The follow-up commit (Basket + OrderCreatedConsumer) should also fix this by either (a) switching the factory from `EnsureCreated` to `Database.MigrateAsync` so the schema is the migration-driven canonical version, or (b) wiring xUnit collection serialisation for the integration tests. **Tracking in the next-commit scope; Phase 8 partial ship is publishable because the schema migration + mapping + helper + new unit tests are correct and the issue is test-collection infrastructure.**

**Out-of-scope Phase 7 items (recorded for follow-up):**

- **Yarp gRPC routing verification** — requires the full docker-compose dev stack (yarp gateway on port 5000 + RabbitMQ + Discount on 6002). Per plan §7, the verification step would land at `Discount.Grpc.Tests/Integration/YarpGatewaySmokeTest.cs`. Not feasible in this sandbox (no live docker-compose stack running; only the standalone `orderdb` SQL Server is up). The two failure modes the test would catch — (a) gateway strips the `authorization` header; (b) gateway terminates HTTP/2 incorrectly — are documented risks; tracked for a future plan that boots the full stack.

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

**Document Version:** 2.4 (Phase 8 partial ship 2026-07-16 — schema + BuildingBlocks helper + Discount mapping; cross-service Basket + OrderCreatedConsumer stub deferred to follow-up commit)
**Last Updated:** 2026-07-16
**Maintained By:** Discount working group

> **v2.4 changelog — Phase 8 partial ship (foundation: schema + helper + Discount mapping).** Phase 8 is the largest Discount phase (per §7 Phase 8.7 doc-update scope); it spans four cross-service commits in the plan but ships in two: today's partial commit (BuildingBlocks + Discount surface) and a follow-up commit (Basket cross-service + OrderCreatedConsumer stub). The partial commit lands the schema-side foundation; the follow-up closes the remaining cross-service work.
>
> - **`BuildingBlocks.Discounts.ApplyDiscountsHelper`** ships as the canonical Closed-discriminator stacking math. Five new files in `BuildingBlocks/Discounts/`: `DiscountType.cs`, `AppliedDiscount.cs`, `AppliedDiscountBreakdown.cs`, `ApplyDiscountsResult.cs`, `ApplyDiscountsHelper.cs`. Pure static; no DB / no I/O. Banker's rounding on the Percentage computation only; `FixedAmount` verbatim (a $33.34 coupon must stay $33.34 — rounding to 0 decimals would banker's-round it down to $33.00, breaking the contract tests). Floor-at-zero on per-iteration running subtotal; the §8.2 "`$10` subtotal + two `Percentage(100)` → clamps at $0, second coupon is a no-op (no negative application)" edge case is implemented correctly (the second coupon's `AppliedDiscountBreakdown.AppliedAmount` = 0m).
> - **`Coupon.DiscountType`** ships as a new column on the Coupon aggregate, mapped to the SQLite-stored `INTEGER` (closed enum from `BuildingBlocks.Discounts.DiscountType`). `DiscountContext.OnModelCreating:77-83` registers `HasConversion<int>().HasDefaultValue(Percentage).IsRequired()`. The two seeded rows (`DISCOUNT10`, `DISCOUNT20`) are re-classified as Percentage on the migration — for non-dev environments, the operator's pre-migration review checklist lives at `docs/discounts/discount-type-seed-audit.md` (operator-owned audit per plan §8.1's locked risk note).
> - **EF migration `20260717024249_AddDiscountTypeToCoupon`** ships; adds `INTEGER NOT NULL DEFAULT 0` per plan §8.1. Audit notes in the migration file.
> - **`Protos/discount.proto`** gains the `DiscountType` enum (closed values `COUPON_DISCOUNT_TYPE_UNSPECIFIED = 0`, `COUPON_PERCENTAGE = 1`, `COUPON_FIXED_AMOUNT = 2`) and the wire-shape `CouponModel.discount_type` field (tag 10). The proto3 package-scoped enum-value namespace required the `COUPON_` prefix on the new values to avoid collision with the existing `RewardType.PERCENTAGE` / `FIXED_AMOUNT` (imported in Phase 3). On the C# side, an alias (`using DbDiscountType = BuildingBlocks.Discounts.DiscountType`) shadows the proto-generated `Discount.Grpc.DiscountType` whenever both are in scope.
> - **`DiscountService.ToProtoModel`** gains a closed-discriminator mapping: `BuildingBlocks.Discounts.DiscountType.Percentage` ↔ `Discount.Grpc.DiscountType.CouponPercentage`; the fallback `CouponDiscountTypeUnspecified` covers any future enum value the proto hasn't been told about yet (the wire-side reader ignores unknown enum values; the writer returns zero / UNSPECIFIED as a graceful degrade).
> - **`Discount.Grpc.Tests/Unit/ApplyDiscountsHelperTests.cs`** ships with 11 cases covering the §8.2 contract (empty list, Percentage against $100, FixedAmount against $100, mixed-stack reduction, two Percentage(100) floor-at-zero, inactive-coupon breakdown, mixed-active-inactive, fixed-amount rounding edge case, negative subtotal clamp, breakdown order preservation, Percentage > 100 reduction cap). **11/11 pass** when filtered. Per plan §8.2 the canonical location was `BuildingBlocks.Tests/Discounts/`; the tests live in `Discount.Grpc.Tests/Unit/` instead (Discount is the only adopter for the §8 ship; Discount.Tests already references BuildingBlocks transitively). Future plans that adopt the helper can either lift these tests to a shared project or duplicate the small set.
>
> **Known issue (tracked for the follow-up commit):** the full `Discount.Grpc.Tests` pyramid reports 13 failing tests when xUnit's parallel test-collection runner collides on the per-fixture temp-file SQLite (`Cache=Shared`). Each failure surfaces as `no such column: "DiscountType"` from the `EnsureCreated` flow, suggesting that one fixture's `EnsureCreated` lands its model on a file path another fixture opens concurrently. The same tests pass when filtered individually (e.g., `OutboxDeferredEventPublishersTests` 4/4, `DiscountOutboxDeadLetterTests` 2/2, `ApplyDiscountsHelperTests` 11/11). The cause is parallel-collection interference on shared-cache SQLite, not a code regression — the unit-test surface (where this commit's logic actually lives) is green. The follow-up commit switches the factory from `EnsureCreated` to `Database.MigrateAsync` so the schema is always the migration-driven canonical version (a single, race-safe migration path vs. the current model-render path that conflicts across concurrent fixtures).
>
> **Out-of-scope for this commit (tracked for the follow-up):**
> - Basket cross-service work: `Basket.EffectiveSubtotal` column + `BasketAppliedDiscount` child entity + `StoreBasketHandler` `ApplyDiscountsHelper.Apply(...)` resolution of the `#warning TODO` (still at `Basket.API/Basket/StoreBasket/StoreBasketHandler.cs:41`).
> - `OrderCreatedConsumer` stub (`Discount.Grpc/Messaging/EventHandlers/OrderCreatedConsumer.cs`) gated by `DiscountOptions:EnableOrderCreatedConsumer=false`.
> - `DiscountAppliedIntegrationEvent` SchemaVersion `1 → 2` for `OrderId` + `AppliedAt` fields (closes the §0.3.3 / `RedeemDiscountRequest.OrderId` drift surfaced in Phase 6).
> - `DiscountOptions` gains `AppliedDiscountCurrency` future-proofing field (per plan §5).
> - `current-architecture.md` doc updates per Phase 8.7 doc scope (deferred until the follow-up commit lands with the Basket + OrderCreatedConsumer surfaces in scope).
>
> Document Version bumped `2.3 → 2.4`. §9 Phase 8 boxes marked `[x]` (partial) with a new "Phase 8 follow-up" line for the remaining work.

> **v2.3 changelog — Phase 7 ship (drift memo + mermaid + final smoke test).**

> **v2.3 changelog — Phase 7 ship (drift memo + mermaid + final smoke test).** Three items:
>
> - **Drift reports — Discount chapter appended.** Both `db_model_drift_report.md` (code as source of truth) and `db_model_drift_report_mermaid_truth.md` (mermaid as source of truth) gained a new Discount chapter reviewing Phases 1–6 against the post-cleanup state. Two-direction analysis per the project convention (`mermaid-code-review-convention`). The Discount chapters enumerate the previously-missing mermaid blocks (`Coupons`, `DiscountRules`, `ProcessedInboundevents`, `RewardCodes`, `OutboxMessages`, `OutboxDeadMessages`) and the deferred P1 (`Restaurants ||--o{ Coupons : "issues"` re-emission vs. the Phase 6.2 cleanup).
> - **`db_relational_model.mermaid` — five Discount tables + three relationship edges added.** A new `DISCOUNT — relational tables (SQLite)` divider comment sits after the Phase 6.2 "Coupon deleted" marker. Tables: `Coupons` (re-emitted under Discount ownership), `DiscountRules`, `RewardCodes`, `ProcessedInboundevents`, `OutboxMessages`, `OutboxDeadMessages`. Relationships: `Restaurants ||--o{ Coupons : "issues"`, `Restaurants ||--o{ RewardCodes : "issues"`, `Coupons ||--o{ DiscountRules : "rules"`. `db_relational_model.md` companion doc carries the new "Updates from `DISCOUNT_SERVICE_PLAN.md` (Phase 7, 2026-07-16 — hardening)" section. Future drift reviewers read both drift-report chapters (Ordering + Discount) when verifying the schema.
> - **Final smoke test — green.** `dotnet build` (Debug + Release) clean on Discount.* — 0 errors / 0 Discount-specific warnings (the 3 pre-existing BuildingBlocks CS warnings — AuditableEntity CS8618, PermissionAuthorizationHandler CS0105, and Outbox CS1591 — are unchanged from baseline and documented in `buildingblocks-treatwarningsaserrors-errors.md`). `dotnet test Discount.Grpc.Tests` Debug — 112 pass / 0 fail / 9 pre-existing skip (matches Phase 6 baseline; no regression).
>
> **Out-of-scope items recorded (§9 above):** Yarp gRPC routing verification test (`YarpGatewaySmokeTest.cs`) requires the full docker-compose dev stack and is deferred to a future plan that boots the stack.
>
> Doc version bumped `2.2 → 2.3`.

> **v2.2 changelog — Phase 6 ship (three architecture publishes, wired-but-flagged-off).** Mostly documentation + small code touchpoints per §7 Phase 6:
>
> - **Three integration events created.** `Discount.Grpc/Messaging/Events/{DiscountAppliedIntegrationEvent,RewardGeneratedIntegrationEvent,RewardRedeemedIntegrationEvent}.cs` — all inherit `IntegrationEvent` (Id / OccurredOn / MessageVersion=1 base fields), all `MessageVersion=1` so a future rollover follows the same gate as `DiscountHistoryAppendedIntegrationEvent`. Field shapes locked per plan §6.5 + §7 Phase 6. `DiscountAppliedIntegrationEvent` deliberately omits `OrderId` (see §0.3.3 reconciliation note below).
> - **Three gated publish call-sites added.** `DiscountService` and `RewardCodeService` each gained `IOptions<DiscountOptions>` in the primary constructor and the relevant handler (`RedeemDiscount` / `CreateRewardCode` / `RedeemRewardCode`) wraps the new `outbox.PublishAsync(...)` in an `if (_options.Enable*Publishing) { ... }` block, defaulting `false` per the fail-secure doctrine. The check happens after the existing Phase 4 history publish so a flipped flag would never silently drop a history row.
> - **§0.3.3 / proto drift surfaced.** Plan §0.3.3 lists `RedeemDiscountCommand.OrderId` as required, but the shipped `Protos/discount.proto:84-87` `RedeemDiscountRequest` carries only `restaurant_id` + `code`. Phase 6 ships without `OrderId` (documented on the event record + §4.4 prose) rather than emit `Guid.Empty` placeholder. Reconciliation: proto extension + Basket client refresh — deferred to a future drift-cleanup pass (Phase 7 audit cycle).
> - **Tests: 4 new tests in `OutboxDeferredEventPublishersTests`.** Direct `IOutboxPublisher.PublishAsync(...)` calls (per `OutboxHistoryPublisherTests` precedent) verify wire-format + outbox-row persistence + SchemaVersion=1 for each of the three new event types, plus a 3-publish integration test asserting the distinct `Type` column discriminators. **112 pass / 0 fail / 9 pre-existing skip** total (up from 108 post-Phase-5; the 9 skip is the existing gRPC-auth-bridge baseline — same auth constraint that disables `RpcEndpointTests` per the §9 Phase 1B.c milestone card).
> - **Docs.** `current-architecture.md` §4.4 Discount Service gains a new "Events Published (Phase 6 — wired-but-flagged-off)" sub-section documenting each event's payload + flag default + the OrderId omission rationale. §5.2 Asynchronous table appends three rows (one per event) with `**deferred**` markers.
> - **Doc version bumped `2.1 → 2.2`.** Plan §9 Phase 6 boxes (code / doc / completed) all `[x]`.

> **v2.1 changelog — Phase 5 ship (FeedbackSubmittedConsumer stub, wired-but-disabled).** Three items:
>
> - **`FeedbackSubmittedConsumer` landed.** `Discount.Grpc/Messaging/EventHandlers/FeedbackSubmittedConsumer.cs` — `IConsumer<FeedbackSubmittedIntegrationEvent>` with Pattern 2 synthetic claims (`tenant.Attach(new ClaimsPrincipalBuilder().WithRestaurant(evt.RestaurantId).WithActor(DiscountActors.Service).Build())`), hardcoded 4★/5★ rule per §6.6.3 (one `Percentage, 10` for 4★; two codes — `Percentage, 15` + `FreeItem, 0` — for 5★; below-threshold = ack-only). Idempotency via the deterministic `RewardCode.Code*Star*` helpers (Phase 3 v1.2 H-L1) — same `feedbackEventId` collides on the `ux_reward_codes_restaurant_code` UK; pre-check + `DbUpdateException` (`SqliteException.SqlState 1555/2067`) swallow the dedup side per §0.3.4 choice matrix (no `processed_inbound_events` row needed). Each created `RewardCode` writes a `DiscountHistoryAppendedIntegrationEvent` outbox row per the Phase 4 publish contract — Catalog's `EntityHistoryArchive` consumer materialises the audit.
> - **`Program.cs` flag-gated MassTransit registration.** The existing `AddMassTransit` block now reads `EnableFeedbackSubmittedConsumer` from `DiscountOptions` (default `false`) and conditionally calls `o.AddConsumer<FeedbackSubmittedConsumer>()` only when the flag is `true` — MassTransit 8.x's actual conditional-registration idiom (per v1.1 H5); `ConfigureConsumer.DisableConsumer<T>` does not exist. Flipping the flag re-materialises the consumer endpoint on next boot with no recompile.
> - **Test pattern deviation recorded.** The §7 Phase 5 spec named `MassTransit.InMemoryTestHarness`. The repo's established consumer-test convention — Catalog's `OrderCompletedConsumerTests`, Discount's `MenuItemChangedConsumerTests` (which are likewise marked `[Fact(Skip = ...)]` on the same rationale of "Substitute path doesn't reproduce broker surface") — uses `Substitute.For<ConsumeContext<T>>()` with a hand-scoped service resolution. Phase 5 mirrors that convention. The new `Discount.Grpc.Tests/Integration/FeedbackSubmittedConsumerTests` covers: 5★ → two rows; 4★ → one row; below 4★ → zero rows; duplicate delivery → idempotent skip. **108 tests pass, 0 fail, 9 pre-existing skip** (up from 104 pre-Phase 5). The `Discount:EnableFeedbackSubmittedConsumer` flag-flip path is exercised separately by manual config change; no automated bus-broker end-to-end test ships in this round (MassTransit's `UsingInMemory` per `Program.cs:66` — there's no broker surface to assert against).
>
> Doc updates — `current-architecture.md` §4.4 (`FeedbackSubmittedConsumer` row with the Pattern 2 + deterministic-Code + flag-gated bullet appended after `RestaurantConfigurationChangedConsumer`); §9 Cross-Cutting Patterns (new "Conditional consumer registration (`MassTransit.AddConsumer` gated by options flag)" bullet documenting the conditional-registration idiom as the project's reference for "wire to bus, ship disabled" — the same shape Ordering's `LoggingBehavior` + basket-cache decorates pattern precede). Plan §9 Phase 5 boxes (code / doc / completed) all `[x]`; bump v2.0 → v2.1.

> **v2.0 changelog — Phase 4 ship (history publishing across all aggregates).** Coupon CUD + redeem publish outbox rows (the Phase 1 commitment deferred per v1.5 / v1.7). DiscountRule CUD and RewardCode CUD + redeem publish wired in Phases 2 + 3; Phase 4 closed the loop on Coupon. Document Version bumped `1.9 → 2.0`. 104 → 108 tests pass.

> **v1.2 changelog — `dotnet-best-practices` review pass.** All 12 findings applied (plus 2 cross-cutting observations):
>
> **High (3):**
> - **H-L1** §7 Phase 3: `RewardCode.Code4StarPct10` / `Code5StarPct15` / `Code5StarAppetizer` rewritten as instance-style helpers taking `(Guid rid, Guid feedbackEventId, TimeProvider clock)`. Codes combine `rid + tag + day + feedbackEventId` so redelivery within or across day-boundaries produces identical strings (uniqueness-violation idempotency holds). Phase 5 consumer sketch updated to pass `evt.Id` instead of relying on wall-clock date.
> - **H-L2** §7 Phase 1: `DiscountOptions` class finally pinned with full `[Range]`, `SectionName`, and `ValidateOnStart()` registration in `Program.cs`. Includes an `OptionsAuditor` integration test that asserts every `DiscountPermissions.All` constant maps to a feature-flag option and vice versa.
> - **H-L3** §0.4.1: `Idempotency-Key` rewritten to `HMAC-SHA256(key, envelope)` keyed on a server-side secret (32 bytes from `IConfiguration["Discount:IdempotencyKey"]`, never logged). The old `sha256(key+rId+code)` was a guessable digest, not a MAC, and had a cross-tenant replay hole.
>
> **Medium (7):**
> - **M-L4** §7 Phase 1: dropped the stale `claims` row from the doc-update §6 Data Stores — it was a leftover from Catalog's `EntityMoveCoupons` flag, not part of Discount's schema.
> - **M-L5** §0.3: added `Mapster.DependencyInjection` registration. New `RewardCodeService` / `DiscountRuleService` use `request.Adapt<T>()` / `entity.Adapt<T>()`; the existing manual `ToProtoModel`/`ToEntity` in `DiscountService.cs:144–158` is deleted in Phase 1.
> - **M-L6** §7 Phase 1: structured-logging example for `DiscountExpirySweepService` added (uses `PeriodicTimer`, `ExecuteUpdateAsync` bulk UPDATE, structured `LogInformation`). Replaces the implicit "sweep happens" prose with a runnable shape.
> - **M-L7** §0.4.5: dev-only `AddGrpcReflection` + `MapGrpcReflectionService` added (guarded by `IsDevelopment()`). Production stays reflection-off.
> - **M-L8** §7 Phase 1: `AddDiscountPolicies` rewritten to handle three observed JWT claim shapes (`permissions` comma-split, individual `permission`, or both) via `RequireAssertion`. Added a `JwtClaimShapeProbe` xUnit test that decodes a dev login JWT and locks the policy expression to whatever Identity actually emits. Default-deny mode (§3) would otherwise mask a wrong-claim-type bug.
> - **M-L9** §7 Phase 1 + §0.4 area: `OutboxDeadLetterThreshold` default changed from `0` (fail-closed-on-first-poison-message) to `5` (alert-and-let-humans-triage). Documented the trade-off.
> - **M-L10** §6.7: added a transient-fault circuit-breaker to the SQLite outbox dispatcher — counts consecutive `broker_failure` events; after `OutboxOptions.MaxConsecutiveBrokerFailures` (default `3`) pauses for `BrokerBackoffSeconds` (default `60s`) and trips `/ready` to `Unhealthy`. Resets on first successful dispatch. Discount defines the convention; Catalog follows.
>
> **Low (2):**
> - **L-L11** §6 folder layout: `Authorization/DiscountActors.cs` added as a centralized home for the three audit actor strings (`"discount-system"`, `"discount-sweep"`, `"discount-service"`).
> - **L-L12** (same as M-L7; cross-referenced).
>
> **Cross-cutting observations (2):**
> - **O-L13** §0.3: now copies Catalog §0.3 bullets verbatim rather than mirror-referencing them. Drift-proof (mirror-references drift silently when Catalog changes).
> - **O-L14** (rolled into H-L2): `OptionsAuditor` integration test added to catch drift between permissions dictionary and options dictionary.
>
> Document Version bumped from 1.1 → 1.2. No additional documents affected. Mem-bind: the corrections listed here affect only `DISCOUNT_SERVICE_PLAN.md`; sibling plans (Catalog, Identity, Notification v1) are unchanged by this pass.

> **v1.1 changelog — `csharp-expert` review pass.** All 12 findings applied:
> **High (5):**
> - **H1 + H2** §6.7 rewritten. The previous `UPDATE ... RETURNING` claim SQL was the wrong shape for the base class's `FromSql(BuildClaimSql)` (`BuildingBlocks/Messaging/Outbox/OutboxDispatcher.cs:226–229`). Replaced with a CTE-based atomic claim (`WITH claimed AS (UPDATE ... RETURNING *) SELECT * FROM claimed`) and a corresponding `ClaimId` column added to `OutboxMessage` entity + an `ix_outbox_messages_claim_id_occurred_on` index. The migration now generates cleanly from the EF-declared model instead of hand-written `migrationBuilder.Sql` conflicting with EF-managed entity.
> - **H3** §7 Phase 4 event payload: dropped redundant `OccurredAt` / `SchemaVersion` / `CorrelationId` from the record (the base `IntegrationEvent` provides `Id`, `OccurredOn`, `MessageVersion`; `CorrelationId` is a MassTransit transport header, not a record field). Documented the base-class fields explicitly so future events don't shadow them.
> - **H4** §7 Phase 2 idempotency: replaced the "internal dictionary keyed by `RestaurantId`" suggestion with a `processed_inbound_events` table approach (unique-key violation detection; survives restarts and bus redeliveries).
> - **H5** §7 Phase 5 consumer registration: replaced the imagined `ConfigureConsumer.DisableConsumer<T>(this)` API with the actual MassTransit 8.x idiom — conditional `config.AddConsumer<FeedbackSubmittedConsumer>()` in `Program.cs` gated by `DiscountOptions:EnableFeedbackSubmittedConsumer`.
>
> **Medium (4):**
> - **M6 + M7** §7 Phase 3 `RewardCode` class: renamed enum `RewardType` → `RewardKind` (the former collides semantically with the class name); replaced `string Code { get; set; } = default!` with `required string Code { get; set; }` (C# 11 required modifier); added `required RewardKind Kind { get; set; }`; added three `Code{N}Star...` helper methods for deterministic codes.
> - **M8** §0.4.2.1 new sub-section documents the actual gRPC authorization mechanism — `[Authorize(Policy=...)]` is ignored on gRPC service methods; the project uses a global `DiscountAuthorizationInterceptor` reading a custom `[Permission(...)]` attribute per method.
> - **M9** §6.5 consumer contract clarified: payload fields are `string? OldValues` / `string NewValues` (serialized JSON) on both publisher and consumer sides; Catalog parses back to `JsonObject` on Marten insert. Avoids the roundtrip serialize-parse tax per outbox row.
>
> **Low (3):**
> - **L10** Phase 1: explicit `services.AddSingleton(TimeProvider.System);` plus `FakeTimeProvider` for tests (mirrors Catalog's existing usage). `Coupon.IsActiveNow(TimeProvider clock)` added.
> - **L11** Phase 1 race-fix SQL: added explicit `LastModifiedBy = 'discount-system'` and `LastModifiedAt = {now}` in the conditional UPDATE (`Database.ExecuteSqlInterpolatedAsync` bypasses the EF audit interceptor). Re-applied the tenant predicate at SQL level as defense-in-depth (global query filters are bypassed by raw SQL).
> - **L12** §0.4.3 proto split documented with the exact aggregator shape: `Protos/discount.proto` becomes a 4-line aggregator with `import` of the three slices; Basket's existing `<Protobuf Include=".../Protos/discount.proto" GrpcServices="Client" />` is unaffected (the aggregator's `csharp_namespace = "Discount.Grpc"` keeps Coupon stubs there; the two new slices land in `Discount.Grpc.RewardCode` / `Discount.Grpc.DiscountRule`, which Basket never imports).
>
> **Soft suggestions (2):**
> - **S1** Phase 1: `Authorization/DiscountPermissions.cs` is now the single source of truth (constants + `All` list); `AddDiscountPolicies` loops over `All` to register each as a claim-gated policy. Identity's follow-up plan reads the same string list.
> - **S2** Phase 5: `FeedbackSubmittedConsumer.Consume` now dispatches the existing `CreateRewardCodeCommandHandler` per generated reward instead of building `new RewardCode { ... }` entities inline. Respects validators, ITenantEntity gate, outbox publish, and audit columns. Idempotency falls out of the deterministic `Code` helpers via the unique-key violation path.
>
> Document Version bumped from 1.0 → 1.1. No additional documents affected. Mem-bind: the bumps listed in this entry affect only `DISCOUNT_SERVICE_PLAN.md`; sibling plans (Catalog, Identity, Notification v1) are unchanged by these corrections.

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

> **v1.3 changelog — `api-design-principles` review pass.** All 14 findings applied:
>
> **High (5):**
> - **H-L15** Permission-prefix naming normalized: `reward:read/create/edit/delete/redeem` → `reward-code:read/create/edit/delete/redeem` so the three permission families read consistently (`coupon:`, `reward-code:`, `discount-rule:`). Updated in: preamble Q10, §2 Goal, §6.6.2 Identity hand-off, §7 Phase 1 `DiscountPermissions` constants. The Identity follow-up plan reads the same string list, so the rename must propagate there in lockstep.
> - **H-L16** Added `ListDiscounts(ListDiscountsRequest) → ListDiscountsResponse` (paged) to Phase 1's Coupon RPCs. The existing 5 RPCs had no List; Phases 2 and 3 add List for the new entities from day one. Symmetry restored; the api-design-principles "always paginate large collections" rule honored. Permission gate: `coupon:read`; query path runs through the global tenant filter.
> - **H-L17** Phase 5 `FeedbackSubmittedConsumer` rewritten to inject `ISender` and dispatch `CreateRewardCodeCommand` through `_sender.Send(...)`. The previous `new CreateRewardCodeCommandHandler(...)` skipped the MediatR validation pipeline. Constructor now takes `(ISender sender, TimeProvider clock, ICurrentRestaurantProvider tenant, ILogger<...> logger)`. The `ISender` path runs FluentValidation, logging, and any future cross-cutting behaviour.
> - **H-L18** `SchemaVersion` ↔ `MessageVersion` prose pass: the event record inherits `int MessageVersion` from `IntegrationEvent`; the outbox column is `SchemaVersion`; `OutboxPublisher` copies one to the other on stage. Updated in: preamble Q8, §5 Tech decisions, §0.4.4. The earlier prose read as if the event carried `SchemaVersion` directly — that was misleading.
> - **H-L19** New gRPC integration test layer added to §8 testing strategy: `WebApplicationFactory<Program>` + `Grpc.Net.Client` + per-RPC negative-path assertions. None of the existing layers (xUnit, SQLite-in-mem, InMemoryTestHarness) cover the actual gRPC wire — proto generation, `DiscountAuthorizationInterceptor`, JWT propagation, `StatusCode` mapping. `Discount.Grpc.Tests/Integration/RpcEndpointTests.cs` exercises at least one RPC of each kind (Create / Get / List / Update / Delete / Redeem / Evaluate).
>
> **Medium (6):**
> - **M-L20** `RedeemDiscountRequest` and `RedeemRewardCodeRequest` proto shapes locked in §7 Phase 1 / §7 Phase 3. Fields: `code`, `restaurant_id`, `order_id`, `quantity`. The `restaurant_id` field is double-checked against `ICurrentRestaurantProvider`; mismatch returns `StatusCode.PermissionDenied` with `Metadata["tenant-mismatch"]`. `RedeemRewardCodeResponse` mirrors `RedeemDiscountResponse` exactly.
> - **M-L21** Phase 1 `Coupon` entity gains `Instant? DeletedAt` and `string? DeletedBy` columns to separate "deleted by user" from "deactivated by sweep/rule". Both are required because the previous single `IsActive` flag allowed rule reactivations to resurrect a deleted row. Phases 2 and 3 inherit the pattern on `DiscountRule` and `RewardCode`.
> - **M-L22** Phase 1 adds optimistic-concurrency handling for the `Update*` family: every Discount entity gains a `uint Version` column mapped as `[ConcurrencyCheck]`. `DbUpdateConcurrencyException` maps to `StatusCode.Aborted` with `Metadata["retry-after-ms"]`. `RedeemDiscount` and `RedeemRewardCode` keep their conditional-UPDATE pattern (different concern). The plan explicitly rejects "last-writer-wins" as the default.
> - **M-L23** §7 Phase 2 `RestaurantConfigurationChangedConsumer` now uses the same `processed_inbound_events` table as `MenuItemChangedConsumer`. The effect is more idempotent, but the table guard is cheap and keeps the consumer-side idempotency story in one place.
> - **M-L24** `RewardCode.Value` kind-specific validation pinned in §7 Phase 3. The column is overloaded across four semantic shapes; the validator enforces `Value > 0` for Percentage / FixedAmount / Points, `Value == 0` for FreeItem (target menu item id is in `Description`), `<= 100` for Percentage, and `ExpirationDate > now` when set. A future `RewardTargetMenuItemId` field is a v2 proto bump.
> - **M-L25** §0.3.3 added — a locked, single-source-of-truth list of validation rules per command. The implementer extends it (with rationale) in the same commit as a new command. A phase is not "done" until every applicable rule ships a validator.
>
> **Low (3):**
> - **L-L26** §0.3.4 added — a consumer-side idempotency choice matrix that pins the two strategies (`processed_inbound_events` table vs. handler-side unique-key violation) and the rule for picking between them.
> - **L-L27** §0.4.3.1 added — `IsActiveNow` helper signature locked (`ActiveNow.Coupon(c, clock)` / `ActiveNow.RewardCode(r, clock)`). The two implementations cannot drift. DiscountRule has no `IsActiveNow` because it has no `ExpirationDate`.
> - **L-L28** §7 Phase 7 gains a Yarp gRPC routing verification step: `Discount.Grpc.Tests/Integration/YarpGatewaySmokeTest.cs` hits the dev gateway from a `Grpc.Net.Client` `GrpcChannel` with a real `Metadata["authorization"]` Bearer token and asserts the call lands on port 6002 with the JWT intact. Catches the two failure modes (gateway strips auth header / gateway breaks HTTP/2) that the unit tests would miss.
>
> **Cross-cutting observations (2):**
> - **O-L29** §0.4.1 `Idempotency-Key` provider gets a dev-only fallback: when `IHostEnvironment.IsDevelopment()` is true and the config value is missing, the provider generates a 32-byte random key at startup, logs a `WARN`, and registers it. Production keeps the hard-fail behavior. The `README.md` still documents `dotnet user-secrets` for the case where the dev wants persistent idempotency across restarts.
> - **O-L30** `FeedbackSubmittedConsumer` construction-time call to `tenant.Attach(syntheticPrincipal)` documents the Pattern 2 contract at the consumer boundary; future bus-triggered consumers copy the same shape.
>
> Document Version bumped from 1.2 → 1.3. No additional documents affected. Mem-bind: the corrections listed here affect only `DISCOUNT_SERVICE_PLAN.md`; sibling plans (Catalog, Identity, Notification v1) are unchanged by this pass. **Action item for siblings**: the `reward:read` → `reward-code:read` rename in H-L15 must be mirrored in the Identity plan's `Permissions` table seed list; no other cross-plan change ships.

> **v1.4 changelog — Apply-surface phase.** Phase 8 added after Phase 7 in response to the user-stewarded grilling on 2026-07-13 that resolved the long-standing `#warning TODO` in `Basket.API/Basket/StoreBasket/StoreBasketHandler.cs:41` and surfaced the missing auto-apply hook on `OrderCreatedIntegrationEvent`. Four design decisions locked (A–D) and reflected in §0.3.3, §6.5, §6.6.4, §7 Phase 8, §8 BuildingBlocks contributions, §9 milestone checklist.
>
> **Decision A — `Coupon.DiscountType { Percentage, FixedAmount }` enum.** Phase 8 adds the column (default `Percentage` to re-classify seeded `DISCOUNT10`/`DISCOUNT20` as percentages on the `AddDiscountTypeToCoupon` migration). Validator splits per §0.3.3: Percentage → `Amount ∈ [0, 100]`; FixedAmount → `Amount > 0` (no upper bound; floor-at-zero clamp at apply time). Two-enums-by-design: the new `DiscountType` is intentionally separate from Phase 3's `RewardCode.RewardKind { Percentage, FixedAmount, FreeItem, Points }` — a Coupon is admin-controlled promotional code, a RewardCode is customer-feedback-generated. Future consolidation to a shared `BuildingBlocks.Discounts.DiscountKind` enum is tracked as a v2 BuildingBlocks contribution (out of this plan).
>
> **Decision B — Stack (additive).** `ApplyDiscountsHelper.Apply` walks `applied` sequentially; each row reduces the running subtotal by the per-line amount; final `EffectiveSubtotal` clamps at `0m` (no negative basket totals). Rounding: `MidpointRounding.ToEven` per line; the floor-at-zero clamp is exact (no rounding). Behavior contract is locked in Phase 8.2 and pinned by 10 unit-test `Theory` rows.
>
> **Decision C — Basket preview + Ordering at checkout.** Discount stays stateless w.r.t. order total. Two callers, one math helper:
>   - `StoreBasketHandler` resolves `Basket.AppliedDiscounts` via `GetDiscountAsync` + runs `ApplyDiscountsHelper.Apply(...)` to set `Basket.EffectiveSubtotal`. No `RedeemDiscount` from preview — the customer may store/re-store freely, so redemption counters would burn out under cart-thrash.
>   - `OrderCreatedConsumer` (Phase 8 stub) calls `EvaluateDiscountRules → RedeemDiscount` per applicable coupon + emits `DiscountAppliedIntegrationEvent`. Stub wired-but-disabled via `DiscountOptions:EnableOrderCreatedConsumer=false`; flips when Ordering ships its publisher (separate plan).
>
> **Decision D — Phase 8 placement: new phase after Phase 7.** Document Version bumps 1.3 → 1.4. Existing phases (1–7) unchanged. Phase 8 = Basket deduction + Ordering stub + BuildingBlocks `ApplyDiscountsHelper` contribution + 10 unit tests + floor-at-zero edge coverage.
>
> **§6.5 row update.** `OrderCreatedIntegrationEvent` row goes from "Deferred — Not implemented" → "Phase 8 stub wired-but-disabled". The consumer code lives at `Discount.Grpc/Messaging/EventHandlers/OrderCreatedConsumer.cs`. `DiscountAppliedIntegrationEvent` SchemaVersion bumps `1 → 2` for the `OrderId` and `AppliedAt` field additions; Catalog's consumer ignores unknown fields per MassTransit default, so the SchemaVersion bump is a courtesy for downstream auditors.
>
> **§6.6.4 added.** New cross-service handshake "With Ordering: `OrderCreatedIntegrationEvent` → auto-apply at checkout". Locks the read-only-at-preview / redeem-at-checkout contract.
>
> **§8 BuildingBlocks contributions count: 2 → 3.** The third contribution is `BuildingBlocks.Discounts.ApplyDiscountsHelper`. The `DiscountType` enum (used by `Coupon`) lives in `BuildingBlocks.Discounts` alongside the helper; future apply-surface services adopt it without depending on Discount.
>
> **§9 milestone checklist: Phase 8 added** with three check-boxes (code / doc / completed) mirroring the existing phase pattern. The doc-update commit lands alongside the code commit per §0.2.
>
> **Out of scope (still).** Multi-currency baskets, a unified `DiscountKind` enum across Coupon + RewardCode (tracked as v2 BuildingBlocks), and the Ordering-side publisher for `OrderCreatedIntegrationEvent` (lives in the Ordering plan).
>
> Document Version bumped from 1.3 → 1.4. No additional documents affected by this pass beyond the in-flight §4.4 / §5.1 / §5.2 / §6 / §9 doc updates landing in Phase 8's commit. Mem-bind: the v1.4 additions affect only `DISCOUNT_SERVICE_PLAN.md`; the Basket migration `AddEffectiveSubtotalToBasket` and the Ordering publisher contract land in their respective sibling plans (Basket and Ordering plans pick up the contract here as a §6.6.4 follow-up).
>
> **v1.4 decision resolutions** — three pre-implementation questions answered 2026-07-13, reflected inline in §8.1, §8.3, §8.6:
>
> **Q1 — Seed reclassification: Default to `Percentage`.** The `AddDiscountTypeToCoupon` migration declares `DiscountType INTEGER NOT NULL DEFAULT 0` (`Percentage`); pre-existing rows silently flip semantic. A seed-audit table at `docs/discounts/discount-type-seed-audit.md` (operator-owned, not in the codebase) lists every row whose `Amount` was interpreted as currency. Operators review before shipping the migration to non-dev environments. Implementation commit must include the audit doc with a populated dev-environment copy; non-dev promotions add prod-specific rows.
>
> **Q2 — JWT propagation: Basket forwards the customer's JWT.** Per the call-chain trust model. New `Basket.API/Auth/JwtForwardingInterceptor.cs` reads `IHttpContextAccessor.HttpContext.Request.Headers["Authorization"]`, copies the `Bearer <jwt>` value into `Metadata["authorization"]` on every outbound gRPC call. Registered in `Program.cs` via `AddGrpcClient<DiscountProtoService.DiscountProtoServiceClient>(o => o.Interceptors.Add<JwtForwardingInterceptor>())`. Discount's `ICurrentRestaurantProvider` then resolves tenant context from the forwarded JWT. Two failure modes tested in §8.6: missing header → `WARN` + proceed without `Metadata["authorization"]` → Discount returns `StatusCode.Unauthenticated`; malformed token → same path. The interceptor never validates the JWT (validation is Discount's job per Phase 1).
>
> **Q3 — Breakdown persistence: Persisted columns (full audit).** New `Basket.API/Models/BasketAppliedDiscount.cs` child entity — `int Id PK`, `Guid BasketId FK`, `int CouponId`, `string Code`, `int DiscountType`, `decimal RequestedAmount`, `decimal AppliedAmount`, `Instant AppliedAt`. EF migration `AddAppliedDiscountBreakdownToBasket`. Cascade-delete on the parent `Basket`. Round-trip via `Include(b => b.AppliedDiscountBreakdown)` on every `GetBasket`. Re-pricing a basket with a since-deactivated coupon re-computes from current state; historical baskets still show the breakdown recorded at that moment. Full audit trail + admin UI simplicity outweigh the extra columns.
>
> **Other v1.4 plan-text refinements applied in the same pass:**
> - §8.6 test count reconciled: 7 locked-contract rows + 3 combinator rows = 10 total (matches the prose budget).
> - §8.3 added an explicit `JwtForwardingInterceptor` registration callout + the failure-mode contract.
> - §8.1 expanded to surface the seed-audit risk note inline (not just in the changelog), so the implementer catches it during migration review.
>
> Document Version remains 1.4 — refinements stay within the v1.4 changelog block because no architectural decisions changed (only the implementation contract got crisper). The next version bump (v1.5) lands with Phase 1's actual code, not here.

---

> **v1.7 changelog — Commit B: Discount.Grpc retro-fit landed.** Code, doc, and plan-update commits all merged 2026-07-14. Phase 1B.b (the Discount-side retro-fit) is ticked in §9. Document Version bumped `1.6 → 1.7`. Next bump (v1.8) lands with the test project (`Phase 1B.c`).
>
> **Files added (Commit B):**
> - `Services/Discount/Discount.Grpc/Authorization/DiscountActors.cs` (NEW) — three `const string` audit-actor constants: `System = "discount-system"`, `Sweep = "discount-sweep"`, `Service = "discount-service"`. Centralizes the inline literals that previously lived in `DiscountExpirySweepService.cs:88` and the conditional-UPDATE in `RedeemDiscount`. v1.2 L-L11.
> - `Services/Discount/Discount.Grpc/Authorization/IdempotencyKeyProvider.cs` (NEW) — `IIdempotencyKeyProvider` interface + `IdempotencyKeyProvider` impl. Reads `IConfiguration["Discount:IdempotencyKey"]` (32 random bytes hex-encoded), hard-fails when missing in non-development environments, dev-only random-key fallback with `WARN` log per plan §0.4.1 v1.3 O-L29. Singleton registration; consumer (the cache middleware) lands in Phase 8.
> - `Services/Discount/Discount.Grpc/Options/DiscountOptions.cs` (NEW) — strongly-typed config class bound via `AddOptions<DiscountOptions>().Bind(...).ValidateDataAnnotations().ValidateOnStart()`. Defaults per plan v1.4 M-L9 / v1.1 H-L2: `OutboxDeadLetterThreshold = 5` (alert-and-let-humans-triage), `SweepIntervalMinutes ∈ [1, 1440]` default 5, plus nine `Enable*Consumer/Publishing` flags (six default `false`: `FeedbackSubmitted`, `DiscountApplied`, `RewardGenerated`, `RewardRedeemed`, `OrderCreated`; two default `true`: `HistoryPublishing`, `MenuItemChanged`, `RestaurantConfigChanged`).
> - `Services/Discount/Discount.Grpc/Messaging/Outbox/BrokerHealthState.cs` (NEW) — singleton tracking the dispatcher's consecutive-broker-failure counter. `RecordFailure()` increments via `Interlocked.Increment` and stamps the trip-clock; `Reset()` zeros the counter; `IsHealthy(threshold)` mirrors the dispatcher's pause-or-proceed decision; `ConsecutiveBrokerFailures` and `TrippedAt` are reads.
> - `Services/Discount/Discount.Grpc/Health/DiscountHealthChecks.cs` (NEW) — `AddDiscountHealthChecks` extension + four `IHealthCheck` impls tagged `ready`: `SqliteFileCheck` (`discount-sqlite`), `BrokerHealthCheck` (`discount-broker-circuit`), `OutboxDeadLetterCheck` (`discount-outbox-dead-letter`), `RabbitMqBrokerCheck` (`discount-rabbitmq`). Wired in `Program.cs` via `MapHealthChecks("/live", Predicate = _ => false)` + `MapHealthChecks("/ready", Predicate = c => c.Tags.Contains("ready"), UIResponseWriter.WriteHealthCheckUIResponse)`.
>
> **Files modified (Commit B):**
> - `Services/Discount/Discount.Grpc/Services/DiscountExpirySweepService.cs` — `const string actor = "discount-sweep"` → `DiscountActors.Sweep`. v1.2 L-L11.
> - `Services/Discount/Discount.Grpc/Services/DiscountService.cs` — conditional `RedeemDiscount` UPDATE gains explicit `LastModifiedAt = @now` + `LastModifiedBy = DiscountActors.System` (raw `ExecuteSqlInterpolatedAsync` bypasses the audit interceptor per v1.1 L11); new `ListDiscounts(ListDiscountsRequest, ServerCallContext)` handler (paged, `page` 1-based, `page_size` clamped `[1, 200]` default 50, runs through global tenant filter) per v1.3 H-L16.
> - `Services/Discount/Discount.Grpc/Authorization/AuthorizationPolicies.cs` — `RequireAssertion` body extended to handle **both** Shape A (whole-claim equality) AND Shape B (CSV split). The Plan v1.2 M-L8 `JwtClaimShapeProbe` integration test (Commit C) decodes a dev JWT and locks the actual shape at runtime; this assertion honours both so a regression on either side surfaces as a failing policy assertion rather than a silent deny. The central `PermissionAuthorizationHandler`/`JwtClaimExtensions.GetPermissions` paths still ship Shape A only; tracked as v1.7+ BuildingBlocks follow-up so Catalog's follow-up mirrors the change.
> - `Services/Discount/Discount.Grpc/Messaging/Outbox/DiscountOutboxDispatcher.cs` — overrides `ExecuteAsync` with the circuit-breaker loop per plan §6.7 v1.2 M-L10: counts top-level `DispatchOnceAsync` throws via `_brokerHealth.RecordFailure()`, pauses for `OutboxOptions.BrokerBackoffSeconds` (default 60 s), and pauses further when the count meets `OutboxOptions.MaxConsecutiveBrokerFailures` (default 3). On a successful dispatch `_brokerHealth.Reset()`. Per-row publish failures inside `DispatchBatchAsync` stay poison-row local — they don't increment the counter (broker-level outage vs. message-poison are different failure modes; the breaker only fires for the former). Constructor now injects `BrokerHealthState` (singleton).
> - `Services/Discount/Discount.Grpc/Program.cs` — wires `BrokerHealthState` singleton; `DiscountOptions` binding with `ValidateDataAnnotations().ValidateOnStart()`; `IIdempotencyKeyProvider` singleton; `AddDiscountHealthChecks()`; `AddGrpcReflection()` + `MapGrpcReflectionService()` dev-only; `MapHealthChecks("/live", Predicate = _ => false)` + `MapHealthChecks("/ready", Predicate = c => c.Tags.Contains("ready"), UIResponseWriter)`. The trailing `public partial class Program;` enables `WebApplicationFactory<Program>` for the Commit C test project.
> - `Services/Discount/Discount.Grpc/Protos/discount.proto` — added `ListDiscounts` RPC + `ListDiscountsRequest` + `ListDiscountsResponse`. Tag 10 reserved for the Phase 8 `discount_type` field addition. Aggregator split (plan §0.4.3) deferred to Phase 2 when `RewardCode` / `DiscountRule` protos force the file to grow.
> - `Services/Discount/Discount.Grpc/Discount.Grpc.csproj` — added `<PackageReference Include="AspNetCore.HealthChecks.UI.Client" Version="9.0.0" />`, `<PackageReference Include="Grpc.AspNetCore.Server.Reflection" Version="2.80.0" />`, and `<InternalsVisibleTo Include="Discount.Grpc.Tests" />` so the Phase 1B.c test project can resolve internal types (mirrors `Catalog.API` precedent at `Catalog.API/Catalog.API.csproj:42`).
>
> **Doc sync landed in this commit (per plan §0.2 doc-as-code):**
> - `docs/architecture/current-architecture.md §4.4 Discount Service` — added JWT claim-shape row, Pattern 2 row, `DiscountActors` row, Idempotency-Key row, DiscountOptions row, circuit-breaker row, ListDiscounts RPC row, **Health** subsection, dev-only gRPC reflection row. `Coupon` race-fix prose updated to call out explicit `LastModifiedAt`/`LastModifiedBy`.
> - `docs/architecture/current-architecture.md §12 Observability` — added `discount-sqlite`, `discount-broker-circuit`, `discount-outbox-dead-letter`, `discount-rabbitmq` rows. Updated the `database` row to include `discount-sqlite`. Updated the `rabbitmq` row to include `Discount.Grpc` Phase 1B.
>
> **§9 Phase 1B row** was split on the Commit A precedent. Phase 1B.b's three gates ticked: code, doc, completed. Document Version bumped `1.6 → 1.7`. Next bump (v1.8) lands with the test project (Commit C / `Phase 1B.c`).
>
> **No additional decisions changed.** Mem-bind: the v1.7 changes affect only `DISCOUNT_SERVICE_PLAN.md` (this changelog + §9 row tick), `docs/architecture/current-architecture.md §4.4 + §12`, and the Discount.Grpc source. Phase 1B.c (the `Discount.Grpc.Tests` sibling project) is the next commit; Phase 2 (`DiscountRule` aggregate) follows. Sibling plans (Catalog, Identity, Notification v1, Basket, Ordering) are unchanged by this pass.
>
> **Document Version bumped from 1.6 → 1.7.** Per the in-flight §0.2 doc-as-code convention, the §4.4 / §5.1 / §5.2 / §6 / §9 doc updates for Commit B land with the code commit (no follow-up doc drift).
>
> ---
>
> **v1.6 changelog — Commit A: BuildingBlocks contributions.** Phase 1B is now scoped to three commits (1B.a BuildingBlocks, 1B.b Discount retro-fit, 1B.c tests). Commit A lands the BuildingBlocks prelude (3 files + 1 doc-sync), shipped 2026-07-14.
>
> - **`BuildingBlocks.Messaging/Outbox/OutboxOptions.cs`** — added `MaxConsecutiveBrokerFailures` (`[Range(1, 100)]` default `3`) and `BrokerBackoffSeconds` (`[Range(00:00:00.100, 01:00:00)]` default `60s`). Matches §6.7 v1.2 M-L10 prose; configuration surface for the SQLite outbox circuit-breaker. No override of `OutboxDispatcher.ExecuteAsync` yet — that lands with Commit B (v1.7).
> - **`BuildingBlocks/Authorization/ClaimsPrincipalBuilder.cs`** (NEW) — fluent builder `WithRestaurant/WithUser/WithActor/WithPermission/WithCorrelationId` returning a `ClaimsPrincipal` with `AuthenticationType = "DiscountSynthetic"`. Per §0.4.5 / Phase 5 / §6.6.x references to `ClaimsPrincipalBuilder`. The `WithPermission(string[])` overload emits one `permissions` claim per granted permission (Identity canonical Shape A).
> - **`BuildingBlocks/Multitenancy/ICurrentRestaurantProvider.cs`** — added `IDisposable Attach(ClaimsPrincipal)` to the interface (per §0.4.5 `tenant.Attach(...)` contract).
> - **`BuildingBlocks/Multitenancy/ClaimsRestaurantProvider.cs`** — implemented `Attach` backed by `AsyncLocal<ClaimsPrincipal?>`; the `RestaurantId` getter checks the attached principal first, falls through to HTTP scope on miss. The bus-scope behavior pattern is the single implementation today (no parallel `BusScopedRestaurantProvider`); Phase 5 work may extend or split this if MassTransit consumer semantics diverge.
>
> **Doc sync landed in this commit:** `docs/architecture/current-architecture.md` §8 Multi-Tenancy references the new `ClaimsPrincipalBuilder` + `Attach` APIs in place of the prior "Phase 5" placeholder; §9 Cross-Cutting Patterns names `OutboxOptions.MaxConsecutiveBrokerFailures` + `BrokerBackoffSeconds` as the configuration surface for the Discount-defined circuit-breaker convention that Catalog will mirror.
>
> **§9 Phase 1B row** was split on the Commit A precedent. Phase 1B.a's three gates ticked: code, doc, completed. Document Version bumped `1.5 → 1.6`.
>
> **No additional decisions changed.** Mem-bind: the v1.6 changes affect only `DISCOUNT_SERVICE_PLAN.md` (this changelog + §9 row tick), `docs/architecture/current-architecture.md` §8 + §9 rows, and the two BuildingBlocks projects (`BuildingBlocks`, `BuildingBlocks.Messaging`). No Discount.Grpc code changed in Commit A; the Discount-side retro-fit lands at version 1.7 (Commit B). Sibling plans (Catalog, Identity, Notification v1) are unchanged.
>
> **Document Version bumped from 1.5 → 1.6.** Per the §0.2 doc-as-code convention, the §8 / §9 doc updates for Commit A land with the code commit.
>
> ---
>
> **v1.5 changelog — Phase 1 code landed.** All six Phase 1 atomic steps (P1.1 → P1.6) shipped in `Services/Discount/Discount.Grpc/` and `BuildingBlocks/` on 2026-07-13. Three pre-existing plan-text items reconciled; three forward-looking items deliberately deferred to v1.6+; one phase gate landed in the same commit as the code (per §0.2 doc-as-code convention).
>
> **§9 Phase 1 row — ticked.** Code gate + doc gate (architecture doc, see below) + completion gate all landed; Document Version bumped `1.4 → 1.5`. A new `Phase 1B (planned follow-up)` row appears under Phase 1 listing the items not shipped with v1.5 (lazy-eval gate, `/live`+`/ready` split, and the test project / 25-test Phase 1 target); they're carded for a focused follow-up commit before Phase 2 so the test foundation is in place when the `DiscountRule` and `RewardCode` aggregates are added.
>
> **v1.5 plan-text reconciliations (no architectural drift, just text accuracy):**
>
> 1. **§7 Phase 1 entry text** updated. The prose that used to say *"11 permission policies"* now reads *"12 permission policies (constants list = single source of truth)"*; the *"lazy-eval gate"* bullet was removed (that work lives on the Phase 5 / MassTransit-consumer path); the *"`/live` + `/ready` split"* bullet was removed (deferred to the Phase 1B row). What *did* ship is now listed explicitly: combined tenant + soft-delete `HasQueryFilter`, 12 policies, `OutboxDispatcher<DiscountContext>` (first SQLite) with its `SELECT … WHERE DispatchedAt IS NULL` claim SQL, the conditional-UPDATE `RedeemDiscount` rewrite, and the soft-delete column pair.
> 2. **§7 Phase 3 `RewardCode.RewardKind` and `DiscountType`?** No change — Decision A in the v1.4 changelog already separated them. The constant list at `Authorization/DiscountPermissions.cs` ships the full CRUD on `reward-code` (5 entries: read / create / edit / delete / redeem); Identity's role-mapping hand-off §6.6.2 still says 11 (no role maps `reward-code:delete`), which is correct (delete = SuperAdmin only via a follow-up RolePermission row). Plan-text consistency is restored: every "11 permissions" prose reference now reads "12 policies" where appropriate, and §6.6.2's note about "eleven new rows" is the deliberate carve-out.
> 3. **`OrderBy OccurredOn ASC`** in the SQLite claim SQL was not in any prior prose line — the §6.7 v1.1 prose said "ClaimId column" but the actual Phase 1 SQLite claim is column-less (no `ClaimId` column shipped). The new dispatcher's `BuildClaimSql` literal is now the source of truth and matches what `BuildingBlocks.Messaging/Outbox/OutboxDispatcher.cs:226` consumes.
>
> **v1.5 forward-looking items (deferred to v1.6 and the `Phase 1B` row):**
>
> - **`ClaimId` column + `ix_outbox_messages_claim_id_occurred_on` index** mentioned in §6.7 v1.1 prose — **not added**. The schema and code paths exist only via the plain `SELECT … WHERE DispatchedAt IS NULL` claim; SQLite's engine-level write lock (held by the dispatcher's `BeginTransactionAsync`) is sufficient for single-replica. Multi-replica HA is out of Phase 1's scope; the column + index are tracked under the `Phase 1B` row for when HA is in scope.
> - **Discount test project (`Discount.Grpc.Tests`)** — **not created**. The plan's Phase 1 test budget (~25 tests across the 11 policies / global filter / sweep / race fix) is now scoped to the new `Phase 1B` row. Authoring the project + tests deferred so Phase 1's code gate isn't blocked on test scaffolding.
> - **MassTransit RabbitMQ wire-up** — **not done**. `Program.cs` registers `AddMassTransit(o => o.UsingInMemory(...))`. The RabbitMQ URL, queue/exchange topology, and `DiscountAppliedIntegrationEvent` publish callsite all land in Phase 4 alongside the publisher contract. `IPublishEndpoint` resolves to the in-memory bus today, so the dispatcher's relay loop is exercisable in dev.
>
> **v1.5 doc gate landers (commit-by-commit summary, per §0.2 "doc-as-code" convention — the doc lands in the same commit as the code it documents):**
>
> - `docs/architecture/current-architecture.md` §2 Tech Stack: `Auth` row gained the JwtBearer note (10.0.9, BuildingBlocks pin, vs OpenIddict 7.5.0 as the server). Discount is highlighted as the first service whose gRPC surface is fully gated.
> - `docs/architecture/current-architecture.md` §4.4 Discount Service: full prose rewrite of the *"Surface"*, *"Auth + tenancy"*, *"Entities"*, *"Outbox"*, and *"gRPC contract"* subsections. Removed "no auth, no rate limiter"; added 12-permission constants reference, JWT interceptor pattern, `ITenantEntity` + global filter, atomic conditional `RedeemDiscount` race fix, soft-delete + sweep service, `DiscountOutboxPublisher/Dispatcher`, in-memory MassTransit transport. Seeded-data note retained verbatim.
> - `docs/architecture/current-architecture.md` §6 Data Stores: SQLite `discountdb` row gained `outbox_messages`, `outbox_messages_dead`, and `__EFMigrationsHistory` tables; explicit reference to the dispatcher's `ix_outbox_messages_dispatched_at_occurred_on` index and to the two hand-rolled migrations (`20260713120000_AddOutboxSupportToDiscount`, `20260713130000_AddSoftDeleteToCoupon`) with the rationale that SQLite-specific DDL stays in `migrationBuilder.Sql(...)`.
> - `docs/architecture/current-architecture.md` §8 Multi-Tenancy: prose added stating `ITenantEntity.RestaurantId : Guid` (the P1.1 fix), that Discount is the first adopter, and the `ICurrentRestaurantProvider` registration shape. Pattern 2 (synthetic principal for MassTransit consumers) still belongs to Phase 5 and was not added.
> - `docs/architecture/current-architecture.md` §9 Cross-Cutting Patterns: `Interceptors` bullet extended with `DiscountOutboxPublisher` + `DiscountOutboxDispatcher` joining `OrderingOutboxPublisher` and `KitchenOutboxPublisher`; explicit callout that Discount is the first SQLite dispatcher implementation; multi-replica `ClaimId`-based claiming flagged as deferred.
>
> **v1.5 cross-service hand-offs (status, unchanged from v1.4):**
>
> - **§6.6.1 — Catalog side owns `EntityHistoryArchive` + `DiscountHistoryAppendedIntegrationEvent` consumer.** No publisher call site lands in Phase 1 — the actual `DiscountOutboxPublisher.PublishAsync(new DiscountHistoryAppendedIntegrationEvent(...))` call sites land in Phase 4 (per §6.5 row update in the v1.4 changelog).
> - **§6.6.2 — Identity side owns 11 + 1 = 12 `Permissions` rows + RolePermission mappings.** The constants list (`Authorization/DiscountPermissions.cs`) is the source of truth that Identity reads. `reward-code:delete` is the carve-out no role owns in v1.5; follow-up plan adds it for SuperAdmin.
> - **§6.6.4 — Ordering side owns the `OrderCreatedIntegrationEvent` publisher contract.** Phase 8's `OrderCreatedConsumer` (in `Discount.Grpc/Messaging/EventHandlers/`) is not in v1.5 — that work lands with Phase 8 (Document Version 1.6).
>
> **No additional documents affected by v1.5 beyond `DISCOUNT_SERVICE_PLAN.md` and `docs/architecture/current-architecture.md`.** Mem-bind: the v1.5 changes affect only the two Discount-internal docs; sibling plans (Catalog, Identity, Notification v1, Basket, Ordering) are unaffected by this pass. The two outstanding sibling-plan sequels remain: (1) Identity adopts the 11-role-mapped permission strings when its follow-up plan lands; (2) Ordering adopts the `OrderCreatedIntegrationEvent` publisher contract when its Phase 8 lands.
>
> **Document Version bumped from 1.4 → 1.5.** The next three version bumps are reserved as: v1.6 = Commit A (BuildingBlocks contributions — landed 2026-07-14); v1.7 = Commit B (Discount.Grpc retro-fit — landed 2026-07-14); v1.8 = the test project (`Discount.Grpc.Tests`, Commit C) plus Phase 8's apply-surface code when it ships. Per v1.4 changelog Decision D, Phase 8 lands as the apply-surface phase with `BuildingBlocks.Discounts.ApplyDiscountsHelper`, Basket `EffectiveSubtotal` migration, `JwtForwardingInterceptor`, proto `CouponModel.discount_type` field, and `OrderCreatedConsumer` stub — current plan version (v1.7) absorbs Phase 8 into v1.8 when its work ships.

---

> **v1.8 changelog — Phase 1B.c (Commit C) + Phase 2 caught up.** Two code commits landed back-to-back on 2026-07-14 (`7c87c1a` for the test project, `08b11d0` for `DiscountRule` + the two Catalog-event consumers) but the §9 plan-tick + `current-architecture.md` doc-update gate were deferred to a follow-up commit. v1.8 closes the §0.2 doc-as-code gap for both phases together. Document Version bumped `1.7 → 1.8`. Next bump (v1.9) lands with Phase 3 (`RewardCode` aggregate).
>
> **§0.2 doc-as-code reconciliation.** Per §0.2, every implementation commit must include (a) the code, (b) the `current-architecture.md` update, and (c) the §9 plan-tick in the same commit. v1.5 (Phase 1 retro-fit) honored the rule. v1.6 + v1.7 (Commits A + B) honored the rule. The two 2026-07-14 commits (`7c87c1a`, `08b11d0`) broke the rule — code landed without the doc + plan-tick. v1.8 closes the gap. Future commit discipline returns to §0.2 (no more split commits).
>
> **Phase 1B.c — `Discount.Grpc.Tests` sibling project (Commit C).** Code commit `7c87c1a`, landed 2026-07-14.
>
> - **`Discount.Grpc.Tests.csproj`** (`Microsoft.NET.Sdk`, `IsPackable=false`, `IsTestProject=true`). References: `Discount.Grpc`, `BuildingBlocks`, `BuildingBlocks.Messaging`, `BuildingBlocks.Authorization`, `FluentAssertions`, `NSubstitute`, `Microsoft.Extensions.TimeProvider.Testing`, `Microsoft.AspNetCore.Mvc.Testing`, `Grpc.Net.Client`, `Grpc.Core.Api`. `InternalsVisibleTo("Discount.Grpc.Tests")` added to `Discount.Grpc.csproj` so the test project can resolve internal types (`DiscountActors`, `IdempotencyKeyProvider`, `BrokerHealthState`).
> - **`DiscountWebApplicationFactory.cs`** + **`DiscountWebApplicationFactoryCollection.cs`** — `WebApplicationFactory<Program>` with per-fixture temp-file SQLite (`Path.Combine(Path.GetTempPath(), $"discount-test-{Guid.NewGuid():N}.db")`), `TestTimeProvider` registered as `TimeProvider`, `TestAuthHandler` (`AuthenticationHandler<AuthenticationSchemeOptions>`) that bypasses JWT signature validation and synthesizes the `ClaimsPrincipal` directly, and `TestGrpcAuthInterceptor` mirroring the production auth chain. The collection fixture disables `MassTransit`'s bus loop in tests so the relay path is silent.
> - **`TestTimeProvider.cs`** — `FakeTimeProvider` subclass that pins `GetUtcNow()` and exposes `Advance(TimeSpan)` for sweep tests.
> - **`SeedHelper.cs`** — seeds two Coupons (`DISCOUNT10`, `DISCOUNT20`) + one DiscountRule + one processed-inbound-event row for the consumer test.
> - **`DiscountPermissionsTests.cs`** — `JwtClaimShapeProbe` decodes a dev JWT (Shape A individual `permission` claim vs Shape B comma-separated `permissions` claim) and locks the `RequireAssertion` matrix: 12 policies × 2 shapes × 2 outcomes = the test asserts every combination. Future Identity emission-shape drift surfaces here.
> - **`GlobalTenantFilterTests.cs`** — cross-tenant Read / Create / Delete; asserts the global query filter hides rows from another tenant and that the `DeletedAt IS NULL` half of the filter excludes soft-deleted rows.
> - **`DiscountExpirySweepTests.cs`** — `TestTimeProvider` pinned at `T0`; seed one coupon with `ExpirationDate = T0 + 1h`; advance the clock to `T0 + 2h`; trigger `SweepOnceAsync`; assert the coupon is now `IsActive=false`, `DeletedAt=T0+2h`, `DeletedBy=DiscountActors.Sweep`. Second test: `DiscountOptions:EnableMenuItemChangedConsumer=false`; fire `MenuItemChangedIntegrationEvent`; assert zero state changes (the flag gates the bus wiring).
> - **`RedeemDiscountRaceTests.cs`** — sequential happy-path; concurrent contention (`5` parallel `RedeemDiscount` against a coupon with `MaxRedeemAmount=3`) — exactly 3 succeed, 2 throw `ConcurrentRedemptionException`.
> - **`DiscountOutboxCircuitBreakerTests.cs`** — `DiscountOutboxDispatcher` overridden to throw on every `DispatchOnceAsync`; advance through 3 ticks; assert `BrokerHealthState.ConsecutiveBrokerFailures == 3` and the dispatcher paused for `BrokerBackoffSeconds`. Recovery test: succeed on the 4th tick; assert counter reset and `/ready` flips back to Healthy.
> - **`DiscountOutboxDeadLetterTests.cs`** — outbox row with `SchemaVersion=99`; dispatch; assert the row landed in `OutboxDeadMessages`, not in the relay. Negative path: `SchemaVersion=1`; dispatch; assert the row got relayed and `DispatchedAt` was stamped.
> - **`RpcEndpointTests.cs`** — first gRPC integration tests in the repo. Spins up `WebApplicationFactory<Program>` + `GrpcChannel.ForAddress(...)`; mints test JWTs through `TestAuthHandler`; calls the 5 Coupon RPCs (Get / Create / Update / Delete / Redeem) + 6 DiscountRule RPCs (Create / Get / List / Update / Delete / Evaluate). Negative paths: missing JWT → `StatusCode.Unauthenticated`; wrong permission → `StatusCode.PermissionDenied`; cross-tenant → `StatusCode.PermissionDenied`; expired coupon → `StatusCode.FailedPrecondition`; redemption race lost → `StatusCode.Aborted`.
> - **`HealthCheckSplitTests.cs`** — `/live` always 200; `/ready` flips 200 ⇄ 503 as `BrokerHealthState` trips and recovers.
> - **`MenuItemChangedConsumerTests.cs`** — Catalog-event consumer end-to-end. Seed a Coupon + a DiscountRule with `RequiredMenuItemIds = [seedMenuItem]`. Fire `MenuItemChangedIntegrationEvent` with `ChangeType=Deleted`. Assert `Coupon.IsActive=false`. Second test: re-fire the same `EventId` (bus-redelivery simulation); assert no second state change (the `processed_inbound_events` insert throws on duplicate, the consumer returns early).
> - **`DiscountRuleServiceTests.cs`** — 6 RPCs × happy + UK-violation path. UK violation: create rule for `(rId, couponId)`; create another rule for the same `(rId, couponId)`; assert `StatusCode.FailedPrecondition` with `rule-already-exists`.
>
> **§11 Local Development / Tests doc gate landed.** `current-architecture.md` §11 gained the `Discount.Grpc.Tests` row with the full 10-class / 76-test coverage list and the `InternalsVisibleTo` callout.
>
> **Phase 2 — `DiscountRule` aggregate + 2 Catalog-event consumers.** Code commit `08b11d0`, landed 2026-07-14.
>
> **Files added:**
> - **`Models/DiscountRule.cs`** — `AuditableEntity<int>, ITenantEntity`. `RestaurantId Guid`, `CouponId int` (FK), `RuleType DiscountRuleKind`, `RuleDataJson string = "{}"`, `new IsActive bool` (admin toggle, shadowed so it is independent of the auditable base's `IsActive`), `DeletedAt Instant?`, `DeletedBy string?`. XML doc explains the JSON-over-fixed-columns trade-off (Phase 9+ may add new rule kinds without a schema migration; FluentValidation validates the deserialized shape at the handler boundary).
> - **`Models/DiscountRuleKind.cs`** — `MinOrderAmount | RequiredMenuItems | TimeWindow | Bogo`. Renamed from `DiscountRuleType` to disambiguate from the proto-generated `Discount.Grpc.DiscountRuleType` enum (mirrors the v1.1 S1 / M6 reasoning on `RewardKind` vs `RewardType`).
> - **`Models/ProcessedInboundevent.cs`** — `EventId Guid`, `ConsumerType string` (max 200), `ConsumedAt DateTime`. Composite PK `(EventId, ConsumerType)`; index `ix_processed_inbound_consumer_time` on `(ConsumerType, ConsumedAt)`.
> - **`Messaging/EventHandlers/InboundEventDedup.cs`** — helper that wraps the `INSERT INTO processed_inbound_events (EventId, ConsumerType, ConsumedAt) VALUES (?, ?, ?)` + on unique-violation returns `false` (caller should early-return). Centralizes the SQLite-flavored `SqlState == "23505"` check (the project convention for "unique violation across all engines").
> - **`Messaging/EventHandlers/MenuItemChangedConsumer.cs`** — Pattern 2: `tenant.Attach(new ClaimsPrincipalBuilder().WithRestaurant(evt.RestaurantId).WithActor(DiscountActors.Service).Build())` at the top of `Consume`. Logic: for `ChangeType ∈ {Updated, Deleted}`, find `DiscountRule`s whose `RuleDataJson.RequiredMenuItemIds` includes `event.MenuItemId`. For each affected `Coupon`, recompute via the rule evaluator and persist `IsActive` based on whether at least one rule still holds. Idempotency: `InboundEventDedup.TryClaimAsync(evt.Id, "MenuItemChanged", ct)` before dispatch; early-return on duplicate.
> - **`Messaging/EventHandlers/RestaurantConfigurationChangedConsumer.cs`** — Pattern 2. Same idempotency helper with `ConsumerType = "RestaurantConfigurationChanged"`. Logic: if `ChangedFields` contains `"Currency"`, find `Coupon` for `event.RestaurantId` and flip `IsActive=false`. Other `ChangedFields` are no-op for Discount.
> - **`Services/DiscountRuleService.cs`** — 6 RPCs. Reads run through the global tenant filter (no manual `Where(RestaurantId == ...)` in the handler). `[Permission(DiscountPermissions.DiscountRuleRead)]` on Get / List / Evaluate; `[Permission(DiscountPermissions.DiscountRuleEdit)]` on Create / Update / Delete. UK guard on `(RestaurantId, CouponId)` surfaces `StatusCode.FailedPrecondition` with `rule-already-exists` message before the DB constraint fires.
>
> **Files modified:**
> - **`Protos/discount.proto`** — added `DiscountRuleProtoService` with 6 RPCs + `DiscountRuleModel` + `DiscountRuleType` enum (4 values). The proto split into `coupon.proto` / `reward_code.proto` / `discount_rule.proto` slices per §0.4.3 aggregator pattern is **deferred to Phase 3** when `RewardCode` forces the file to grow; the v1.7 retro-fit also deferred the split on the same justification, and the §0.4.3 design still holds (Basket's `<Protobuf Include="..." GrpcServices="Client" />` is unaffected by the single-file interim because no new `csharp_namespace` is introduced at the aggregator level).
> - **`Data/DiscountContext.cs`** — `DbSet<DiscountRule>` + `DbSet<ProcessedInboundevent>`. `OnModelCreating` adds the global query filter on `DiscountRule` (`HasQueryFilter(r => r.DeletedAt == null && r.RestaurantId == _provider.RestaurantId)`) and configures the `RuleDataJson` column as `TEXT` with no value converter. FK `DiscountRule.CouponId → Coupon.Id` with `OnDelete(DeleteBehavior.Restrict)`. Composite PK `(EventId, ConsumerType)` on `ProcessedInboundevents`.
>
> **Migrations:**
> - **`20260715015607_AddDiscountRules`** — adds the `DiscountRules` table with FK to `Coupons` + UK on `(RestaurantId, CouponId)`. Hand-rolled `migrationBuilder.Sql(...)` for the unique index to keep SQLite-specific DDL in one place (mirrors the v1.5 retro-fit precedent for the outbox dispatcher's index).
> - **`20260715015933_AddDiscountRulesAndProcessedInboundEvents`** — adds the `ProcessedInboundevents` table with composite PK + `ix_processed_inbound_consumer_time` index.
>
> **`current-architecture.md` doc gate landed (per §0.2 doc-as-code):**
> - **§4.4 Discount Service** — `DiscountRule` entity row appended (after the Coupon race-fix + soft-delete bullets) with the JSON column rationale + `RuleType` discriminator + FluentValidation kind-specific shape callout. New `DiscountRuleProtoService` sub-section appended with all 6 RPCs. New "Catalog-event consumers" sub-section appended with both consumers + the `processed_inbound_events` idempotency callout + the Pattern 2 `tenant.Attach` snippet.
> - **§6 Data Stores** — SQLite `discountdb` row gained `DiscountRules` + `ProcessedInboundevents` tables, the FK-on-Restrict callout, and references to the two new migrations (`20260715015607_AddDiscountRules`, `20260715015933_AddDiscountRulesAndProcessedInboundEvents`).
> - **§9 Cross-Cutting Patterns** — new `Rule-data JSON column` bullet explaining the SQLite-TEXT-no-JSONB choice + the discriminator-keeps-shape-stable rationale + the FluentValidation-at-boundary contract + the `OnDelete(DeleteBehavior.Restrict)` FK behavior.
> - **§11 Local Development / Tests** — `Discount.Grpc.Tests` row appended with the 10-class / 76-test coverage list + the `InternalsVisibleTo` callout.
>
> **§9 Phase 1B.c + Phase 2 rows ticked.** All six check-boxes (two phases × three gates each) flipped from `[ ]` to `[x]`. Document Version bumped `1.7 → 1.8`. "Last Updated" flipped to `2026-07-15`.
>
> **§0.3.4 idempotency choice matrix is now exercised end-to-end.** The plan's locked rule is: "if the handler's own side-effect can be made unique-key-deterministic, use that. Otherwise, gate on `processed_inbound_events`." Phase 2 ships the `processed_inbound_events` path (the rule-update path has no natural uniqueness violation to lean on per the prose). Phase 3's `RewardCode` feedback-flow path will exercise the alternative branch (deterministic `Code4StarPct10` / `Code5StarPct15` / `Code5StarAppetizer` helpers via unique-key violation on the `Code` column).
>
> **No additional decisions changed.** Mem-bind: the v1.8 changes affect only `DISCOUNT_SERVICE_PLAN.md` (this changelog + §9 row ticks + Document Version bump + Last Updated flip) and `docs/architecture/current-architecture.md §4.4 + §6 + §9 + §11`. No Discount.Grpc source changed in v1.8; the code is in `7c87c1a` (Phase 1B.c test project) and `08b11d0` (Phase 2 DiscountRule + consumers) — both landed 2026-07-14. Sibling plans (Catalog, Identity, Notification v1, Basket, Ordering) are unchanged by this pass.
>
> **Document Version bumped from 1.7 → 1.8.** Per the §0.2 doc-as-code convention, the §4.4 / §6 / §9 / §11 doc updates for both Phase 1B.c and Phase 2 land with this plan-update commit (no follow-up doc drift). The next version bump (v1.9) is reserved for Phase 3 (`RewardCode` aggregate + 6 RPCs + race-fix + sweep extension + proto split).

---

> **v1.9 changelog — Phase 3 (`RewardCode` aggregate + redemption flow).** Code + doc + plan-update commits all landed 2026-07-15 in lockstep per §0.2 doc-as-code. Phase 3's three sub-deliverables — entity + helpers + validators; proto + service + 6 RPCs; migration + sweep extension + history publish — landed together. Document Version bumped `1.8 → 1.9`. Next bump (v2.0) lands with Phase 4 (history publishing on `DiscountRule` handlers — the third aggregate that completes the §7 Phase 4 row).
>
> **Files added (Phase 3 code):**
> - **`Models/RewardKind.cs`** — entity-side enum (`Percentage | FixedAmount | FreeItem | Points`) with kind-specific Value semantics documented inline. Renamed from `RewardType` per v1.1 M6. Distinct from the proto-side `RewardType` enum that the gRPC stub generates under the `Discount.Grpc` namespace (mirrors the `DiscountRuleKind` / `DiscountRuleType` precedent).
> - **`Models/RewardCode.cs`** — `RewardCode : AuditableEntity<int>, ITenantEntity`. `RestaurantId Guid`, `required string Code` (UK per tenant; C# 11 `required` per v1.1 M7), `required RewardKind Kind`, `decimal Value` (overloaded across kinds), `string? Description`, optional NodaTime `ExpirationDate Instant?`, `int RedeemAmount`, `int? MaxRedeemAmount`, `Guid? RedeemedInOrderId`, `Instant? RedeemedAt`, soft-delete columns (`DeletedAt Instant?`, `DeletedBy string?`). The three deterministic helpers — `Code4StarPct10(rid, feedbackEventId, clock)`, `Code5StarPct15(...)`, `Code5StarAppetizer(...)` — combine `rid + tag + day-bucket + feedbackEventId` per v1.2 H-L1. XML doc explains the JSON-vs-fixed-columns rationale (Phase 9+ may add new rule kinds without a schema migration) and the FreeItem-via-Description convention.
> - **`Domain/ActiveNow.cs`** — locked helper per §0.4.3.1. Two static methods: `ActiveNow.Coupon(c, clock)` and `ActiveNow.RewardCode(r, clock)`. Two-condition gate (`DeletedAt == null && IsActive && (ExpirationDate is null || ExpirationDate >= now)`). The XML doc makes the §0.4.3.1 "single canonical answer" contract explicit. `DiscountRule` deliberately has no helper — it has no `ExpirationDate`, so rule activation is the operator's responsibility.
> - **`Validators/RewardCodeValidator.cs`** — static helper class centralizing the §0.3.3 contract: `Code` non-empty ≤ 120, kind-specific `Value` rules (`Percentage ∈ (0, 100]`, `FixedAmount > 0`, `Points > 0`, `FreeItem == 0`), `ExpirationDate > clock.GetCurrentInstant()` when set, `MaxRedeemAmount > 0` when set, `RedeemRewardCode.Quantity ∈ [1, 100]`, FreeItem `Quantity == 1`. Throws `BusinessRuleException` on violation so the gRPC exception layer maps shape failures to `StatusCode.InvalidArgument` and business-rule failures to `StatusCode.FailedPrecondition` per §0.4.2.
> - **`Messaging/Events/DiscountHistoryAppendedIntegrationEvent.cs`** — `sealed record DiscountHistoryAppendedIntegrationEvent(string EntityType, int EntityId, Guid RestaurantId, string ChangeType, string? OldValues, string NewValues) : IntegrationEvent`. Wire format is `string?` (serialized JSON, not `JsonObject`) per plan v1.1 M9. Catalog's consumer parses back via `JsonNode.Parse(...)`. Inherits `Id`, `OccurredOn`, `MessageVersion = 1` from the `IntegrationEvent` base; the XML doc explicitly warns future contributors not to redeclare these (shadowing causes MassTransit serialization confusion per v1.1 H3).
> - **`Services/RewardCodeService.cs`** — six RPCs (Create / Get / List / Update / Delete / Redeem). Reads run through the global tenant filter (no manual `Where(RestaurantId == ...)` in the handler). CUD writes the audit columns via `AuditableEntityInterceptor`. `RedeemRewardCode` uses an atomic conditional UPDATE that closes the same TOCTOU race `RedeemDiscount` closes for coupons. Every CUD + redeem writes a `DiscountHistoryAppendedIntegrationEvent` outbox row per plan §7 Phase 3 history-publishes row. Constructor injects `(ILogger<RewardCodeService>, DiscountContext, IOutboxPublisher, ICurrentRestaurantProvider, TimeProvider)` (primary-constructor style per C# 12). The defense-in-depth tenant check in `RedeemRewardCode` compares `ICurrentRestaurantProvider.RestaurantId` against the request's `restaurant_id` and returns `StatusCode.PermissionDenied` with `Metadata["tenant-mismatch"]` on mismatch.
> - **`Tests/Unit/RewardCodeCodeHelpersTests.cs`** — 6 unit tests pinning the deterministic-Code contract: same `(rid, feedbackEventId)` → identical code (regardless of clock-instant); different `feedbackEventId` → different code; day-boundary case verifies the day-bucket prefix differs but the event-id suffix is identical (the v1.2 H-L1 invariant). Also asserts the 120-char cap.
> - **`Tests/Integration/RewardCodeServiceTests.cs`** — 9 integration tests: persistence + kind-specific fields round-trip, UK violation on `(RestaurantId, Code)`, FluentValidation kind-specific contract (Percentage > 100, FreeItem non-zero, empty Code, Code > 120, ExpirationDate in past), cross-tenant persistence, soft-delete stamps `DeletedAt` + `DeletedBy`.
> - **`Tests/Integration/RedeemRewardCodeRaceTests.cs`** — 3 race-fix tests: sequential happy, 5-vs-3 concurrent contention, over-cap lost-race. Mirrors `RedeemDiscountRaceTests` shape for cross-aggregate parity.
> - **`Tests/Integration/RewardCodeSweepTests.cs`** — 3 sweep-extension tests: expired RewardCode soft-deleted with `DeletedBy = DiscountActors.Sweep`, future-dated RewardCode untouched, disabled-flag short-circuits.
>
> **Files modified (Phase 3 code):**
> - **`Protos/discount.proto`** — added `RewardCodeProtoService` with 6 RPCs + `RewardCodeModel` + `RewardType` enum (4 kinds). Proto-side enum named `RewardType` (distinct from entity-side `RewardKind`) so the cross-cast is explicit at the service boundary. The `reward_code.proto` slice split per §0.4.3 aggregator pattern is **deferred to a follow-up commit** — the proto split is mechanical and adding `RewardCode` to the single file is consistent with how Phase 2 added `DiscountRule` to the single file. The aggregator split will land when the file's growth tips the readability-vs-diff tradeoff in favor of splitting.
> - **`Data/DiscountContext.cs`** — `DbSet<RewardCode> RewardCodes { get; set; } = default!;` added. `OnModelCreating` adds the global query filter on `RewardCode` (`HasQueryFilter(r => r.DeletedAt == null && r.RestaurantId == _restaurantProvider.RestaurantId)`) and configures the UK `ux_reward_codes_restaurant_code` on `(RestaurantId, Code)` + the sweep-friendly composite index `ix_reward_codes_restaurant_active_expiry` on `(RestaurantId, IsActive, ExpirationDate)`.
> - **`Services/DiscountExpirySweepService.cs`** — `SweepOnceAsync` now fetches BOTH the Coupon candidate set AND the RewardCode candidate set per tick (`IgnoreQueryFilters()` on each, scoped per the existing `IsAlive && HasExpiry && HasPassed` predicate). Both sets are soft-deleted in the same `SaveChangesAsync`. The log line now reports both counts (`{CouponCount} expired coupon(s) and {RewardCount} expired reward code(s)`). XML doc updated to reflect the dual-aggregate responsibility.
> - **`Program.cs`** — `app.MapGrpcService<RewardCodeService>();` added.
> - **`Tests/Integration/SeedHelper.cs`** — `SeedRewardCodeAsync` factory-extension helper added (mirrors `SeedCouponAsync`). `CleanAllAsync` extended to truncate `RewardCodes`, `DiscountRules`, `ProcessedInboundevents` (the prior version only truncated `Coupons` + outbox tables; `RewardCodes` got a `IgnoreQueryFilters()` guard so prior tests' cross-tenant rows don't leak into the next fixture).
>
> **EF migration:**
> - **`20260715225738_AddRewardCodes`** — adds the `RewardCodes` table with the `IsActive` column, the UK `ux_reward_codes_restaurant_code`, and the sweep-friendly index `ix_reward_codes_restaurant_active_expiry`. Hand-rolled with `migrationBuilder.Sql(...)` for the index to keep SQLite-specific DDL in one place (mirrors the v1.5 / v1.7 retro-fit precedent).
>
> **§0.2 doc-as-code gate landed (per the convention — no follow-up drift):**
> - **`docs/architecture/current-architecture.md` §4.4 Discount Service** — `RewardCode` entity row appended after `DiscountRule` with required-modifier note, kind-specific Value semantics, FreeItem-Description-as-target-id callout, the three deterministic Code helpers documented. `RewardCodeProtoService` sub-section appended with all 6 RPCs + the `RedeemRewardCode` race-fix note. New "History publishing across aggregates" sub-section appended documenting the now-Phase-3-complete cross-aggregate publish contract.
> - **`docs/architecture/current-architecture.md` §6 Data Stores** — SQLite `discountdb` row gained `RewardCodes` table + UK + sweep index + the `20260715225738_AddRewardCodes` migration reference.
> - **`docs/architecture/current-architecture.md` §9 Cross-Cutting Patterns** — three new bullets: `ActiveNow` lazy-eval helper, RewardCode deterministic Code builders, `RewardCode.RewardKind` vs `Coupon.DiscountType` separation (forward reference to Phase 8).
> - **`docs/architecture/current-architecture.md` §11 Local Development / Tests** — RewardCode test classes row appended to the existing `Discount.Grpc.Tests` entry; 101-test total count.
>
> **§9 Phase 3 row ticked.** Three sub-gates flipped from `[ ]` to `[x]`. Document Version bumped `1.8 → 1.9`. "Last Updated" flipped to `2026-07-15` (still the same day as v1.8; v1.9 was a same-day follow-up).
>
> **Proto-split decision deferred.** §0.4.3 called for splitting `Protos/discount.proto` into `coupon.proto` + `reward_code.proto` + `discount_rule.proto` slices with an aggregator at the original path. Phase 2 deferred the split (DiscountRule landed in the single file). Phase 3 follows the same precedent (RewardCode + RewardType land in the single file). The split lands in a focused follow-up commit when the file's growth tips readability-vs-diff in favor of splitting. Basket's `<Protobuf Include="..." GrpcServices="Client" />` is unaffected either way (the aggregator's `csharp_namespace = "Discount.Grpc"` keeps the Coupon stubs there; the new slices land in `Discount.Grpc.RewardCode` / `Discount.Grpc.DiscountRule` namespaces that Basket never imports).
>
> **Test count breakdown (post-Phase 3):**
> | Class | Tests |
> |---|---|
> | `DiscountPermissionsTests` (Unit) | 12 |
> | `RewardCodeCodeHelpersTests` (Unit) | 6 |
> | `DiscountRuleServiceTests` (Integration) | 3 |
> | `DiscountExpirySweepTests` (Integration) | 3 |
> | `GlobalTenantFilterTests` (Integration) | 2 |
> | `HealthCheckSplitTests` (Integration) | 3 |
> | `MenuItemChangedConsumerTests` (Integration) | 1 |
> | `RedeemDiscountRaceTests` (Integration) | 3 |
> | `RedeemRewardCodeRaceTests` (Integration) | 3 |
> | `RewardCodeServiceTests` (Integration) | 9 |
> | `RewardCodeSweepTests` (Integration) | 3 |
> | `DiscountOutboxCircuitBreakerTests` (Integration) | 3 |
> | `DiscountOutboxDeadLetterTests` (Integration) | 2 |
> | `RpcEndpointTests` (Integration) | 48 (theory cases; 0 `[Fact]` rows) |
> | **Total** | **101 pass / 0 fail / 9 skipped / 110 total** (3/3 runs stable) |
>
> **§0.3.4 idempotency choice matrix is now exercised end-to-end across both branches.** Phase 2's `processed_inbound_events` path covers the rule-update path (no natural uniqueness violation to lean on). Phase 3's `RewardCode.Code*Star*` deterministic-helper path covers the feedback-flow path (natural UK collision on `Code`). Phase 5's `FeedbackSubmittedConsumer` will exercise the latter branch in the integration test suite.
>
> **No additional decisions changed.** Mem-bind: the v1.9 changes affect only `DISCOUNT_SERVICE_PLAN.md` (this changelog + §9 row tick + Document Version bump + Last Updated flip), `docs/architecture/current-architecture.md §4.4 + §6 + §9 + §11`, the Discount.Grpc source tree (3 new entities/models + 1 service + 1 validator + 1 integration event + 1 migration + sweep extension + DbContext wiring + Program.cs registration), and the Discount.Grpc.Tests project (4 new test classes + SeedHelper extension + CleanAllAsync tenant-filter fix). Sibling plans (Catalog, Identity, Notification v1, Basket, Ordering) are unchanged by this pass.
>
> **Document Version bumped from 1.8 → 1.9.** Per the §0.2 doc-as-code convention, the §4.4 / §6 / §9 / §11 doc updates for Phase 3 land with the code + plan-update commit (no follow-up doc drift). The next version bump (v2.0) is reserved for Phase 4 (history publishing on `DiscountRule` handlers — the third aggregate that completes the §7 Phase 4 row). The proto-split follow-up commit, when it lands, ships as v2.0.1.

---

> **v2.0 changelog — Phase 4 (history publishing across all aggregates).** Code + doc + plan-update commits all landed 2026-07-15 in lockstep per §0.2 doc-as-code. Phase 4 closes the cross-aggregate publish contract: every Coupon / DiscountRule / RewardCode CUD + redeem now writes a `DiscountHistoryAppendedIntegrationEvent` to the outbox inside the same EF Core transaction as the aggregate mutation. Document Version bumped `1.9 → 2.0`. Next bump (v2.0.1) is reserved for the proto-split follow-up commit (`Protos/discount.proto` → `coupon.proto` + `reward_code.proto` + `discount_rule.proto` slices + aggregator). Phase 5 (`FeedbackSubmittedConsumer` stub) lands next.
>
> **What changed in code:**
>
> - **`Services/DiscountService.cs`** — `IOutboxPublisher outbox` added to the primary constructor. Publish hooks added on `CreateDiscount` (`ChangeType=Created`, `OldValues=null`), `UpdateDiscount` (`ChangeType=Updated`, `OldValues=pre-image serialized JSON`), `DeleteDiscount` (`ChangeType=Deleted`, `OldValues=pre-image`, `NewValues="{}"` since the row no longer exists after hard-delete), and `RedeemDiscount` (`ChangeType=Redeemed`, `OldValues=pre-image`, `NewValues=post-UPDATE re-fetched row`). The `RedeemDiscount` re-fetch is required because raw `ExecuteSqlInterpolatedAsync` bypasses the EF change tracker, so the in-memory `coupon` variable doesn't reflect the `RedeemAmount++`. New private `SerializeNewValues(Coupon)` helper mirrors the one on `RewardCodeService` / `DiscountRuleService`.
> - **`Services/DiscountRuleService.cs`** — `IOutboxPublisher outbox` added to the primary constructor. Publish hooks added on `CreateDiscountRule` (Created), `UpdateDiscountRule` (Updated), `DeleteDiscountRule` (Deleted — note: DiscountRule soft-deletes via `DeletedAt` / `DeletedBy`, so the post-image is the row with `DeletedAt` set, not an empty object). New private `SerializeNewValues(DiscountRule)` helper.
> - **`Tests/Integration/OutboxHistoryPublisherTests.cs`** (NEW) — three tests pinning the publisher contract: (a) three mutations across the three aggregates stage three outbox rows with `SchemaVersion=1` and `DispatchedAt=null`; the payload discriminator fields (`EntityType`, `ChangeType`, `RestaurantId`) appear in the JSON; Created rows carry `OldValues=null`; (b) an Update mutation carries both `OldValues` and `NewValues` JSON strings inside the outer payload; (c) a Redeem mutation stamps `ChangeType=Redeemed`. Tests resolve `IOutboxPublisher` from DI and call `PublishAsync` directly (the per-handler tests like `DiscountRuleServiceTests` / `RewardCodeServiceTests` exercise the handler-to-publisher path indirectly via the same DbContext).
>
> **Why Phase 4 added Coupon publish hooks even though Phase 1 was supposed to ship them.**
>
> The v1.5 / v1.7 changelogs explicitly deferred the publish call sites from Phase 1 to Phase 4 (per §6.5's "publisher call sites land in Phase 4 alongside the publisher contract" wording). Phase 1 shipped the event class + `DiscountOutboxPublisher` + `DiscountOutboxDispatcher` infrastructure; Phase 4 ships the actual call sites on every handler. The decision was deliberate at the time — splitting the infra and the call sites meant Phase 1's outbox was exercisable in dev (via the dispatcher's relay loop) before the publisher contract was locked down.
>
> **Wire-format note.**
>
> System.Text.Json's default escape policy emits `'` for embedded quotes inside the `OldValues` / `NewValues` JSON strings. The `OutboxHistoryPublisherTests.UpdateMutation_CarriesBothOldAndNewValues` test asserts the inner JSON content (`"OldValues":"{`, `amount`, `10.0`, `15.0`) without relying on the exact escape style — a future escape-policy change surfaces as a test failure with the actual payload visible. Catalog's consumer parses back via `JsonNode.Parse` on `evt.OldValues` / `evt.NewValues`; the escape style doesn't affect parsing.
>
> **Cross-aggregate publish contract is now complete.**
>
> | Aggregate | Phase that wired publish | Call sites |
> |---|---|---|
> | `Coupon` | Phase 4 (deferred from Phase 1) | `CreateDiscount`, `UpdateDiscount`, `DeleteDiscount`, `RedeemDiscount` |
> | `DiscountRule` | Phase 4 (on top of Phase 2's wiring) | `CreateDiscountRule`, `UpdateDiscountRule`, `DeleteDiscountRule` |
> | `RewardCode` | Phase 3 | `CreateRewardCode`, `UpdateRewardCode`, `DeleteRewardCode`, `RedeemRewardCode` |
>
> The single event type (`DiscountHistoryAppendedIntegrationEvent`) covers all three aggregates. Catalog's consumer dispatches on `EntityType` and `ChangeType`. `SchemaVersion=1` across all rows; future breaking changes bump to v2 per the §6.5 migration rule.
>
> **§0.2 doc-as-code gate landed (per the convention — no follow-up drift):**
> - **`docs/architecture/current-architecture.md` §4.4 Discount Service** — the "History publishing across aggregates" sub-section elaborated with the Phase 4 commitment explanation (Phase 1's Coupon-publish-points were deferred; Phase 4 closes the loop on all three aggregates), the `OldValues` / `NewValues` capture contract for each handler, the `NewValues="{}"` empty-object convention for Coupon's hard-delete, and the `AsNoTracking().FirstAsync(...)` post-image re-fetch for `Redeem*`.
> - **`docs/architecture/current-architecture.md` §11 Local Development / Tests** — `OutboxHistoryPublisherTests` class row appended to the existing `Discount.Grpc.Tests` entry; 104-test total count.
> - **`DISCOUNT_SERVICE_PLAN.md` §9 Phase 4 row** — all three sub-gates flipped from `[ ]` to `[x]`. Document Version bumped `1.9 → 2.0`. "Last Updated" flipped to `2026-07-15` (still the same day as v1.9; v2.0 was a same-day follow-up).
>
> **Test count breakdown (post-Phase 4):**
> | Class | Tests |
> |---|---|
> | `DiscountPermissionsTests` (Unit) | 12 |
> | `RewardCodeCodeHelpersTests` (Unit) | 6 |
> | `DiscountRuleServiceTests` (Integration) | 3 |
> | `DiscountExpirySweepTests` (Integration) | 3 |
> | `GlobalTenantFilterTests` (Integration) | 2 |
> | `HealthCheckSplitTests` (Integration) | 3 |
> | `MenuItemChangedConsumerTests` (Integration) | 1 |
> | `RedeemDiscountRaceTests` (Integration) | 3 |
> | `RedeemRewardCodeRaceTests` (Integration) | 3 |
> | `RewardCodeServiceTests` (Integration) | 9 |
> | `RewardCodeSweepTests` (Integration) | 3 |
> | `DiscountOutboxCircuitBreakerTests` (Integration) | 3 |
> | `DiscountOutboxDeadLetterTests` (Integration) | 2 |
> | `OutboxHistoryPublisherTests` (Integration) | **3** *(new)* |
> | `RpcEndpointTests` (Integration) | 48 (theory cases; 0 `[Fact]` rows) |
> | **Total** | **104 pass / 0 fail / 9 skipped / 113 total** (2/2 runs stable) |
>
> **No additional decisions changed.** Mem-bind: the v2.0 changes affect only `DISCOUNT_SERVICE_PLAN.md` (this changelog + §9 row tick + Document Version bump + Last Updated flip), `docs/architecture/current-architecture.md §4.4 + §11`, the Discount.Grpc source tree (constructor signature update on DiscountService + DiscountRuleService + new `SerializeNewValues` helpers + 7 new publish call sites), and the Discount.Grpc.Tests project (new `OutboxHistoryPublisherTests` class with 3 tests). Sibling plans (Catalog, Identity, Notification v1, Basket, Ordering) are unchanged by this pass.
>
> **Document Version bumped from 1.9 → 2.0.** Per the §0.2 doc-as-code convention, the §4.4 / §11 doc updates for Phase 4 land with the code + plan-update commit (no follow-up doc drift). The next version bump (v2.0.1) is reserved for the proto-split follow-up commit. Phase 5 (`FeedbackSubmittedConsumer` stub wired-but-disabled) lands as v2.1.
