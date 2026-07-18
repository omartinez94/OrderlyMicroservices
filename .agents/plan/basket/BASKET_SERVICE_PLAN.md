# Basket.API — Service Plan (v1.6)

> **Scope:** completion plan for the existing `Basket.API` microservice. Closes the gaps surfaced by a csharp-expert review of `Services/Basket/Basket.API/` against `docs/architecture/architecture.md`, `docs/architecture/current-architecture.md`, and the rest of the solution. Today the four happy paths (`GET / PUT / DELETE / POST checkout`) are wired through Carter → MediatR → Marten → Redis → MassTransit, with Phase 1 (tenant safety + identity cross-check + `[PciSensitive]` redaction + 9 unit tests) and Phase 2 atomic-checkout sub-deliverable (Marten `CheckoutBasketOutboxMessage` + `CheckoutBasketOutboxDispatcher` + atomic handler rewrite + 3 new unit tests) shipped. Remaining gaps: discount integration is a `#warning` no-op, idempotency middleware is missing, payment-method redaction (card fields on the wire) is deferred, and the rate-limiter / 201-vs-200 PUT / wrapper-record cleanup have not landed. This is an *evolution* plan, not a green-field design — all four endpoints exist; the work is hardening and adding the missing cross-cutting layers.
>
> **Out-of-plan entity moves:** none. The Basket service is a leaf consumer in the architecture and does not own aggregates that should migrate elsewhere.
>
> **Origin:** synthesized from the csharp-expert review pass of `Basket.API` (Program.cs, Models/, Data/, Basket/{Get,Store,Delete,CheckoutBasket}/). Sections of `DISCOUNT_SERVICE_PLAN.md` and `CATALOG_SERVICE_PLAN.md` are mirrored by reference wherever they apply unchanged.

---

## 0. Skill & documentation conventions

These two conventions apply to **every phase** below. They are non-negotiable — no implementation commit for this plan should land without satisfying both.

### 0.1 Skill mandate — `csharp-developer`

> **All implementation work on this plan MUST invoke the `csharp-developer` skill** (base directory `.claude/skills/csharp-developer`, invoked as `/csharp-developer` in Claude Code).
>
> The skill is the source of truth for C# 12+ / .NET 10 idiom, async patterns, Marten usage, ASP.NET Core + Carter, MediatR CQRS, xUnit + Testcontainers test scaffolding, and the project's "MUST DO / MUST NOT DO" guard rails (nullable enabled, primary constructors, async/await with `CancellationToken`, `Result<T>` for error paths, no blocking calls, DTO mapping for API responses).
>
> At the start of **every phase**, the implementer (human or AI agent) loads the skill. Companion reference files under `.claude/skills/csharp-developer/references/` are loaded on demand per the skill's table:
> - `modern-csharp.md` — records, primary constructors, collection expressions, pattern matching, nullable types.
> - `aspnet-core.md` — Minimal API / Carter endpoints, DI, middleware, routing.
> - `entity-framework.md` — loaded only if a phase migrates Basket to EF Core (this plan does not — Marten stays).
> - `performance.md` — `Span<T>`/`Memory<T>`, async, AOT; loaded only if a phase lands a perf-sensitive hot path (Checkout publish is one such path).
>
> **Marten / EF-style checkpoint:** Basket's storage is a Marten **document store**, not relational EF Core. The catalog-style `dotnet ef migrations add` step does not apply; schema changes (e.g. the Phase 1 `Basket : ITenantEntity` adoption with multi-tenancy) are implemented via `opt.Schema.For<Basket>().MultiTenanted()` in `Program.cs` and verified with a fresh `ApplyAllDatabaseChangesOnStartup()` against an empty Postgres. **Review** the generated schema before booting the service.
>
> The skill is *additional* to whatever other skills are relevant (e.g. `csharp-xunit` for test scaffolding, `api-design-principles` for endpoint shape). It is **not** a substitute for the plan; the plan wins where they disagree.

### 0.2 Phase-completion documentation update

> **After completing every phase (1–5), `docs/architecture/current-architecture.md` MUST be updated to reflect the new state of the codebase before the implementation commit is finalized.**
>
> `current-architecture.md` is described in its own header as *"the snapshot view of the codebase — no planned features, no gap list. As new functionality is built … update this file to match."* It must never describe Basket with capabilities that don't exist yet, and it must never lag a shipped phase.
>
> The implementer writes the doc update as part of the phase, not as a follow-up commit. Each phase below lists its **Doc-update scope** — the §-numbered sections of `current-architecture.md` that phase touches.
>
> For convenience, the recurring touch points are:
>
> | Doc section | Why it usually changes per phase |
> |---|---|
> | §2 Tech Stack | New package rows (gRPC resilience handler, OpenTelemetry, Testcontainers). |
> | §4.3 Basket Service | New endpoints, `Basket : ITenantEntity` adoption, atomic checkout via outbox, idempotency, cache stampede protection, JWT claim cross-check, log redaction, expiry sweep, /live + /ready split. |
> | §5.1 Synchronous | Row "Basket.API → Discount.Grpc" gains the resilience pipeline note. |
> | §5.2 Asynchronous | Outbox-mediated `BasketCheckoutEvent` publish replaces the direct `IPublishEndpoint.Publish` line. |
> | §6 Data Stores | Marten document store gains the per-tenant partitioning note. |
> | §9 Cross-Cutting Patterns | `CachedBasketRepository` stampede protection, `LoggingBehavior` redaction, `outbox_messages` row. |
> | §11 Local Development | New `Basket.API.Tests` project + Testcontainers step. |
> | §12 Observability | `/live` + `/ready` split, new OpenTelemetry sources. |
>
> The phase's checklist entry (see §8) requires the doc commit before the phase is marked complete.

### 0.3 Code-quality guard rails (dotnet-best-practices)

Basket **inherits the guard rails from `CATALOG_SERVICE_PLAN.md §0.3` verbatim**. Mirror-references drift silently; copy-into-context is verbose but drift-proof. The Catalog plan is the authoritative source; if Catalog §0.3 changes, this section changes in lockstep on the next Basket phase commit.

Basket-specific overrides layered on top of the catalog-copied bullets (these re-state Catalog §0.3 in *context* rather than mirror-reference, so a future contributor reading the Basket plan alone has the rules without a second lookup):

- **xUnit + FluentAssertions + Moq** for unit tests (matches `Ordering.Domain.Tests`, `Kitchen.API.Tests`). Test framework override recorded here once; contributes to the §0.3.11 mirror.
- **Testcontainers (Postgres + Redis + RabbitMQ)** for handler/endpoint/repository tests — identical to Catalog.
- **`CachedBasketRepository` re-entrancy guard** uses `SemaphoreSlim` keyed by `cacheKey` stored in a `ConcurrentDictionary` to avoid lock-per-key setup; lifetime must be **Singleton** (lock outlives request scope) and the dictionary cleared on `Dispose` of an `IHostApplicationLifetime`-registered hosted cleanup. Implementation in Phase 3.
- **`LoggingBehavior` redaction** — when `TRequest is CheckoutBasketCommand` (or any future command carrying PII/PCI), the request payload line is replaced with `typeof(TRequest).Name + " (payload redacted)"`. Implementation in Phase 1 alongside the tenant fix.
- **`Basket` is a Marten document, not an EF Core entity** — `IEntity`/`AuditableEntity<T>` conventions from `BuildingBlocks` do **not** apply. The Basket entity implements `BuildingBlocks.Multitenancy.ITenantEntity` (already declared in `BuildingBlocks/Multitenancy/ITenantEntity.cs:3`) and uses NodaTime `Instant` for `CreatedAt`/`ExpiresAt`, matching the existing `Models/Basket.cs` shape.
- **`CancellationToken` end-to-end** — every public async method **must** accept a `CancellationToken` and **must** propagate it to every downstream call (Marten session, Redis client, MassTransit publish, gRPC stub). Phase-2 introduces no new method that breaks this contract; the implementation PR **fails** review if `CancellationToken` is missing on any `Task`/`ValueTask` signature.
- **`ArgumentNullException.ThrowIfNull` on every primary-constructor reference parameter** — nullable-enabled + compiler non-null covers the type-system side; `ThrowIfNull` covers the runtime guard for callers who bypass the compiler (`null!` casts, reflection, etc.). Required for Phase 1's `IBasketIdentityGuard`, `BasketCacheLockRegistry`, and every handler that gains a new dependency.
- **`IAsyncDisposable` for every new `IHostedService`** — `CheckoutBasketOutboxDispatcher` (Phase 2) and `BasketExpirySweepService` (Phase 3) **must** implement both `IDisposable` and `IAsyncDisposable`. `StopAsync(CancellationToken)` must drain in-flight work and release DB / Redis / RabbitMQ connections within the host shutdown grace period (default 30 s; configurable via `IHostOptions.ShutdownTimeout`).
- **`BeginScope` for correlation IDs** — `ILogger<T>` log calls in the outbox dispatcher, the cache stampede guard, and the idempotency middleware each open a `LogScope` carrying `OutboxMessageId`, `MessageVersion`, `EventType`, and `CorrelationId`. This is the bridge to Phase 4's OpenTelemetry — the same key flows through both sinks.
- **`ConfigureAwait(false)` policy** — Basket.API is **application code** (ASP.NET Core request handlers); no `ConfigureAwait(false)` is needed or added. BuildingBlocks libraries (Catalog §0.3.4) **do** use it; do not copy the pattern into `Basket.API/`. A Phase 1 reviewer flags false-positive `ConfigureAwait` calls on application hot paths.
- **No `Console.WriteLine` / `Debug.WriteLine`** — `TreatWarningsAsErrors + GenerateDocumentationFile=true` already enforces the XML-doc rule; the equivalent for logging is the `BanConsoleLogging` Roslyn analyzer configured in `Basket.API/Directory.Build.props`.
- **Result<T> vs exceptions** — v1 keeps the project convention of throwing domain exceptions (`BasketNotFoundException`, `ForbiddenException`, the new `BasketValidationException`); the api-design-principles skill's `Result<T>` example is **not** adopted. `CustomExceptionHandler` is the sole error-translator. Documented to prevent a future contributor copy-pasting the skill's pattern.

#### 0.3.1 Global usings (project-specific)

`Basket.API/GlobalUsings.cs` currently holds 15 entries (verified). Phase 1 promotions to add:

```csharp
global using System.Security.Claims;                                      // JwtClaimExtensions.GetUserId/GetRestaurantId path
global using BuildingBlocks.Multitenancy;                                 // ICurrentRestaurantProvider, ITenantEntity
global using Microsoft.Extensions.Caching.Distributed;                   // IDistributedCache, DistributedCacheEntryOptions
global using Microsoft.AspNetCore.Http;                                   // IHttpContextAccessor (used by ClaimsRestaurantProvider)
```

The "2+ files" promotion rule from CATALOG_SERVICE_PLAN §0.3.12 applies. Phase 1 implements a single concrete claim check helper that surfaces `System.Security.Claims`, triggering the promotion; the other three are exported because Phases 1–3 introduce at least two files that need each. The existing `Marten`, `MediatR`, `Carter`, `FluentValidation`, `Mapster`, `NodaTime`, `BuildingBlocks.CQRS`, `BuildingBlocks.Behaviors`, `BuildingBlocks.Exceptions`, `BuildingBlocks.Authorization` entries stay.

#### 0.3.2 Marten-specific guard rails (project-specific override)

- **Document identity** — `[Identity]` on `Basket.UserId` (existing in `Models/Basket.cs:17`) is a Marten convention that defines the document id; do not add a separate `Id` property. The composite `(UserId, RestaurantId)` semantics mean a single Marten **stream** per user, with the `RestaurantId` as a field; multi-tenanted mode stores per-tenant `tenant_id` columns automatically.
- **Session lifetime** — `IDocumentSession` is **Scoped** (Marten default in this codebase; the `AddMarten().UseLightweightSessions()` in `Program.cs:50` confirms it). Handlers must not capture the session in a background thread or a Singleton; the cache-stampede guard in Phase 3 resolves sessions through `IServiceScopeFactory`.
- **NodaTime + Marten** — `Instant` already round-trips (see the existing `Basket.cs:25-26`). Verify in the Phase 1 multi-tenant migration: write+read of `Basket` preserves `CreatedAt`/`ExpiresAt`.
- **`ApplyAllDatabaseChangesOnStartup()`** — kept. Basket is single-tenant-aware (per-tenant DBs created by `CreateDatabasesForTenants` in `Program.cs:36`); Phase 1's multi-tenancy switch to `MultiTenanted()` documents must coexist with per-tenant DB creation. Verify that the per-tenant connection string is still created when `MultiTenanted()` is enabled.

#### 0.3.3 Disposal patterns (project-specific)

- **`IAsyncDisposable` over `IDisposable`** — every new `IHostedService` (Phase 2 `CheckoutBasketOutboxDispatcher`, Phase 3 `BasketExpirySweepService`, Phase 4 `AdminCartsPurgeHostedService` if added) implements `IAsyncDisposable` first, falls back to `IDisposable` for the synchronous tail. Pattern:

  ```csharp
  public sealed class CheckoutBasketOutboxDispatcher(
      IServiceScopeFactory scopes,
      ILogger<CheckoutBasketOutboxDispatcher> logger,
      IHostApplicationLifetime lifetime)
      : BackgroundService, IAsyncDisposable
  {
      // ... ExecuteAsync(CancellationToken) ...

      public override async Task StopAsync(CancellationToken cancellationToken)
      {
          await base.StopAsync(cancellationToken);
          // drain in-flight work, flush any half-staged outbox row before exit
      }

      public async ValueTask DisposeAsync()
      {
          // dispose the message-channel, cancel the in-flight task scope
          await _stoppingCts.CancelAsync();
          _stoppingCts.Dispose();
      }
  }
  ```

- **`IConnectionMultiplexer` Singleton lifetime** — the `ConnectionMultiplexer` registered by `AddStackExchangeRedisCache` is a Singleton (matches Catalog §0.3.3 lifetime table). Handlers receive `IDistributedCache` (also Singleton-resolved but request-safe in practice); they **must never** `new ConnectionMultiplexer(...)` or capture the multiplexer in a `using`.
- **`SemaphoreSlim` lifetime and disposal** — `BasketCacheLockRegistry` (Phase 3) is a Singleton with `IAsyncDisposable`. The registry clears its `ConcurrentDictionary<string, SemaphoreSlim>` and awaits each semaphore's `DisposeAsync()` in `DisposeAsync()`. Host shutdown grace period is `IHostOptions.ShutdownTimeout`; the registry's `DisposeAsync` registers a `IHostApplicationLifetime.ApplicationStopping` callback that cancels any pending `WaitAsync` callers with `OperationCanceledException` (logged at Information, not Error).
- **`IDisposable` for `IValidator<T>` factories** — FluentValidation validators are stateless and resolved from the container; no manual disposal needed. Validator *composition* (the `IRegisterModule` pattern) is also stateless.
- **No captive dependencies** — every Singleton (including the outbox dispatcher, cache lock registry, and Admin cart services) gets scoped work through `IServiceScopeFactory.CreateScope()`. A Singleton that holds a `Scoped` reference fails code review.

#### 0.3.4 Logging (structured + scoped)

- **`ILogger<T>` with typed category** — every class takes `ILogger<T>` in its primary constructor; `T` is the class itself. Category-inheritance (e.g. `ILogger<CheckoutBasketOutboxDispatcher>`) is wrong; it confuses log filters.
- **`BeginScope` for hot-path correlation** — Phase 2's outbox dispatcher opens a scope per dispatched message:
  ```csharp
  using var _ = logger.BeginScope(new Dictionary<string, object>
  {
      ["OutboxMessageId"] = row.Id,
      ["MessageVersion"] = row.SchemaVersion,
      ["EventType"] = typeof(BasketCheckoutEvent).FullName!,
      ["CorrelationId"] = correlationId,   // propagated from the inbound request
  });
  ```
  The same `CorrelationId` flows into Phase 4's OpenTelemetry `Activity.Current.AddTag(...)`. Log search groups the entire request → outbox → consumer chain.
- **Structured error logging** — every catch that re-throws (or swallows in a known-recoverable case) logs `LogError(ex, "Context {Identifier}", id)`, never `LogError(ex.Message)`. Example from the outbox dispatcher:

  ```csharp
  catch (Exception ex)
  {
      logger.LogError(ex,
          "Outbox dispatch failed for {EventType} {OutboxMessageId} (attempt {AttemptCount}/{MaxAttempts})",
          row.EventType, row.Id, row.AttemptCount, row.MaxAttempts);
      row.IncrementFailure(ex.GetType().Name);   // store the exception class, not the message
  }
  ```

  The exception's *message* is **never** persisted to the outbox row (PII risk). The exception's *type name* is, for operator debugging.
- **PII redaction everywhere** — `LoggingBehavior` redaction (§0.3 list above) is the inbound check; the outbox dispatcher carries the same `Basket:Pii:RedactLogs=true` flag and redaction helper. `CardNumber` never reaches any sink, even at Trace level. The `Logging` section of `appsettings.json` allows `Basket.*:Trace` for dev only; production is Information+.
- **No `Console.WriteLine` / `Debug.WriteLine`** — `BanConsoleLogging` Roslyn analyzer in `Directory.Build.props`.

#### 0.3.5 Configuration (strongly-typed + validated)

- **`IOptions<T>` (not `<Snapshot>`, not `<Monitor>`)** for options that change **only at startup** — `BasketOptions` (cache TTL, multi-tenancy filter mode). `<T>` is the lightest lifetime and avoids per-request allocation.
- **`IOptionsMonitor<T>`** for options that change **without a restart** — the **expiry-sweep interval** (Phase 3) and the **rate-limiter quota** (Phase 2) qualify. Operators expect to bump the sweep interval without redeploying; the option is hot-loaded from `IConfiguration` and re-evaluated on each sweep tick.
- **One options class per concern** — `BasketOptions`, `BasketCheckoutOptions` (idempotency-key secret + replay TTL + HMAC key), `OtelOptions` (Phase 4). Each carries:
  - A `Section` const exposing the configuration key (e.g. `BasketOptions.Section = "Basket"`).
  - `[Required]` / `[Range]` / `[RegularExpression]` data annotations matching the rules in §0.4.10 and §0.4.5.
  - One constructor that the binder invokes; **no** `IConfiguration` field on the options class (configuration is read once at bind time, not stored).
- **`ValidateOnStart()` mandatory** — every options class is registered with:
  ```csharp
  builder.Services
      .AddOptions<BasketCheckoutOptions>()
      .Bind(builder.Configuration.GetSection(BasketCheckoutOptions.Section))
      .ValidateDataAnnotations()
      .ValidateOnStart();
  ```
  A bad config fails the host boot at `startAsync`, not at first request.
- **Secrets never in source** — the `IdempotencyKey` for §0.4.4 comes from env-var `BASKET__CHECKOUT__IDEMPOTENCYKEY` (double-underscore = nested section). `appsettings.Development.json` carries a dev-only 32-byte placeholder; `appsettings.json` references the env-var placeholder.

#### 0.3.6 Performance (allocation-aware hot paths)

- **Hot paths to watch** — `CachedBasketRepository.GetActiveCartAsync` (cart hot read), `CheckoutCartHandler` (orchestration), outbox publisher (Phase 2). These are the only places a benchmark is mandatory pre-merge. Everything else (admin list, single-item upsert) is fine with normal LINQ.
- **`Span<T>` / `Memory<T>` only on parser hot paths** — the `CouponSnapshot.Description` HTML sanitiser (Phase 2 deferred) is the only candidate. Allocation-free is the goal; benchmark before/after.
- **`ValueTask<T>` for cache reads** — `CachedBasketRepository.GetActiveCartAsync` returns `ValueTask<Models.Basket?>` once Phase 3's single-flight guard lands; synchronous cache-hit completion avoids the `Task` allocation.
- **No `Task.Run` for I/O** — the outbox dispatcher runs on the `BackgroundService` loop, **not** on the thread pool. I/O work (Marten read + MassTransit publish) is `await`-ed, never `Task.Run`-wrapped.
- **`async` streams reserved** — `IAsyncEnumerable<T>` for the admin list endpoint (`GET /api/v1/admin/carts`, Phase 4) so the response streams paged results rather than materialising the full list.
- **Log-call allocation budget** — `BeginScope` (above) uses `IDictionary<string, object>` so the structured-logger can serialise directly; no `params object[]` on the hot path.

#### 0.3.7 Code quality / SOLID review checkpoint

- **Single-responsibility checkpoint** — at commit time the implementer self-checks the diff against the five SOLID principles. The most common defect in this codebase is single-responsibility violation. The notable v1 exception is `CheckoutCartHandler.Handle` which intentionally does six steps (identity guard, load, validate, build event, outbox, delete-basket, commit) so the publish-and-delete atomicity holds. That exception is **deliberate** and recorded in the commit message — verify the reviewer sees the rationale, not the omission. Any *other* handler doing more than three of its own dependencies' work is a refactor.
- **`Result<T>` is rejected** — see §0.3 override list; throwing domain exceptions is the project convention.
- **Code duplication / base-classes** — common helpers (`CouponSnapshot`, `PaymentMethodSummary`, `CorrelationIdMiddleware`) live in `Basket.API/Common/` once they're used in 2+ files; one-use helpers stay at the call site.
- **Meaningful names** — the rename from `Basket` → `Cart` for the API surface (§0.4.1) is the canonical example. Internal handler classes keep the `Basket` prefix when they operate on a `Basket` domain model; the *HTTP resource* is `Cart` because the user thinks "cart". Mixed metaphors (e.g. `BasketController.GetCart`) are disallowed.
- **Disposal patterns** — see §0.3.3.
- **XML documentation on every public member** — enforced by `dotnet build /p:TreatWarningsAsErrors=true /p:GenerateDocumentationFile=true`; the contribute path lists `<Include>..\Basket.API\**\*.cs</Include>` in `Basket.API.csproj` `<DocumentationFile>` so every public type emits CS1591 if undocumented.

### 0.4 API design principles (REST + Carter + MediatR)

Basket **inherits `CATALOG_SERVICE_PLAN.md §0.4` verbatim** with project-specific overrides below. The deviation log:

| § | Topic | Basket rule | Why it differs |
|---|---|---|---|
| 0.4.1 | URL shape | **Token-bound singleton** (`/cart`) | A user's active cart is a singleton per (user, restaurant). Encoding both ids in the URL is what created the §1.2 auth gap. Moving to `/cart` lets the JWT carry the identity. |
| 0.4.2 | Validation pipeline | `ValidationBehavior<TRequest,TResponse>` accepts `IRequest<TResponse>` | The BuildingBlocks constraint `TRequest : ICommand<TResponse>` silently skips every query validator. Basket is the first service to add `GetBasketQueryValidator`; this constraint relaxation is a BuildingBlocks contribution tied to this plan. |
| 0.4.3 | Authz layering | Throw `BuildingBlocks.Exceptions.ForbiddenException`; **never** `Results.Forbid()` | `Results.Forbid()` returns an empty 403, breaking the ProblemDetails envelope. Exception-driven flow goes through `CustomExceptionHandler` and emits a consistent problem payload. |
| 0.4.4 | `MapGroup` policies | Group-level default + per-route permission; one group, shared registration | Today each `ICarterModule` calls `MapGroup("/api/v1")` independently. A single `BasketEndpointGroup` extension centralises the policy chain and the `WithOpenApi()` opt-in. |
| 0.4.11 | Error envelope | `AddProblemDetails()` + `BasketProblemDetailsFactory` (per-tenant `traceId` / `correlationId` enrichment; **Phase 1 contribution**, not just a footnote) | Currently the global handler writes a `ProblemDetails` with `httpContext.TraceIdentifier` only; correlation-ids stay out of band. The factory is small and lands alongside `ForbiddenException`. |
| 0.4.12 | API versioning | URL-segment versioning (`/api/v1/`); **no** `Accept` header versioning | Same as Catalog; matches the existing gateway routing. A future v2 breaking change ships at `/api/v2/` and runs alongside v1. |
| 0.4.13 | AsParameters for complex queries | Phase 4 admin endpoints pass `[AsParameters] CartAdminQuery` | Today every query has ≤ 2 route params; once Phase 4 ships the list endpoint, `[AsParameters]` keeps the endpoint lean. |
| 0.4.6 | Idempotency | `Idempotency-Key` per IETF `draft-ietf-httpapi-idempotency-key-header`; replay = cached 200; key reuse with different body = **422**, not 409 | The current plan's "409 on reuse" contradicts the IETF draft. State conflict ≠ semantic inconsistency. |

#### 0.4.1 Resource model (URL shape)

A Basket is a **singleton sub-resource** of the authenticated user, scoped to a single restaurant via the JWT's `restaurantId` claim. There is at most one active Basket per `(UserId, RestaurantId)`. The Plan adopts the **token-bound** URL shape (option A) and the move is part of Phase 1; the existing `/{userId}/{restaurantId}` shape is **deprecated** and removed in Phase 3 (after every consumer migrates).

| Operation | URL (Phase 3+) | Today (Phase 1–2) |
|---|---|---|
| Get active cart | `GET /api/v1/cart` | `GET /api/v1/baskets/{userId}/{restaurantId}` |
| Upsert cart (add/remove items, change qty) | `PUT /api/v1/cart` | `PUT /api/v1/baskets/{userId}/{restaurantId}` |
| Abandon cart | `DELETE /api/v1/cart` | `DELETE /api/v1/baskets/{userId}/{restaurantId}` |
| Checkout | `POST /api/v1/cart/checkout` | `POST /api/v1/baskets/checkout` (kept; the action shape survives the rename) |

Phase 1 keeps the old URL shape for one release as a shim (`[Obsolete]`) so external callers can migrate. The old shape returns the same response payload as the new shape and is removed at the end of Phase 3. The cart resource is **not** nested under `/users/{userId}/` because the user id is the JWT subject; treating `/cart` as the user-rooted resource matches RFC 7231's "uniform interface" rule (URLs identify the resource; identity is the caller).

The DELETE endpoint **does not** take a body; the JWT supplies the (UserId, RestaurantId) pair. The Checkout endpoint takes a `BasketCheckoutRequest` body (no `UserId`/`RestaurantId` — the handler resolves them from the JWT). This removes the §2.10 spoofing footgun.

#### 0.4.2 Validation pipeline (BuildingBlocks contribution)

`BuildingBlocks/Behaviors/ValidationBehavior.cs:9` constrains `TRequest : ICommand<TResponse>`. Any validator registered against a query (`IQuery<>`) is silently skipped. The fix is one-line (drop the constraint to `IRequest<TResponse>`) but the impact is repo-wide; record it here.

- Phase 1 ships the relaxation in BuildingBlocks and a regression test (`ValidationBehaviorTests.QueryValidator_RunsThroughPipeline`).
- Every service that has a query-side validator today (Catalog: `GetMenuItemsQuery`; Ordering: `GetOrdersQuery`; Discount: `EvaluateDiscountRulesQuery`) gets the same gate for free.
- Services that **don't** have a query validator today skip the test (the relaxation doesn't change their behaviour).
- Document the change in `current-architecture.md` §9 (Cross-Cutting Patterns).

#### 0.4.3 HTTP status code matrix (per endpoint)

Every Basket endpoint declares its method, success codes, and client-error codes. The matrix below is the source of truth; flag any deviation in code review.

| Method · URL | Success | Client errors | Server errors | Notes |
|---|---|---|---|---|
| `GET /api/v1/cart` | **200 OK** + body (empty cart returns `200` with `Items: []`, `TotalItems: 0`, `Subtotal: 0` — never 404; see §0.4.7) | 401 unauthenticated, 403 cross-tenant (Phase 1 identity guard) | 500, 503 (DB / cache down) | `200 + ETag` returns **`304 Not Modified`** when the FE sends `If-None-Match` and the cart hasn't changed (Phase 4 stretch). |
| `PUT /api/v1/cart` | **201 Created** + `Location: /api/v1/cart` (new cart) **or 200 OK** + body (existing cart updated) | 400 validation, 401, 403, 409 (concurrent update — optimistic concurrency on `Basket.LastModifiedAt`), 422 (item `MenuItemId` not in catalog) | 500 | Handler returns `StoreBasketResult { bool IsCreated, Guid UserId, Guid RestaurantId }`; endpoint maps to the right status. Same method on every successful PUT (idempotent upsert). |
| `DELETE /api/v1/cart` | **204 No Content** | 401, 403 | 500 | Idempotent: deleting a non-existent cart also returns 204. The `DeleteBasketResponse { IsSuccess = true }` body is **removed**; clients that needed the confirmation instead get `Last-Modified` going absent. |
| `POST /api/v1/cart/checkout` | **200 OK** + `CheckoutBasketResponse` (success), **202 Accepted** (replay still in flight) | 400 validation, 401, 403, **409** (basket empty or already published), **422** (idempotency-key reused with different payload — per IETF draft), **429** (rate limiter; see §0.4.8) | 500, 503 (bus down → outbox retries) | Only endpoint that accepts the `Idempotency-Key` header (Phase 2). |

State-transition or rejection codes that the plan must NOT silently swallow:

- `409` (conflict) — empty cart, idempotency-key conflict-by-state, optimistic-concurrency mismatch.
- `422` (unprocessable) — idempotency-key reused with a different request body, **`MenuItemId` references a deleted catalog item**, `UserId` in the body doesn't match the JWT subject (Phase 1 closes this).

#### 0.4.4 Error envelope + 403 path

`CustomExceptionHandler` already maps `NotFoundException → 404`, `ValidationException → 400`, `BadHttpRequestException → 400`, `DomainException → 500`, and detects `*StateTransitionException → 409`. **Today there is no `ForbiddenException`.** The Phase 1 identity guard currently returns `Results.Forbid()` — empty body, no `ProblemDetails`. The fix is a single new exception type:

```csharp
// BuildingBlocks/Exceptions/ForbiddenException.cs (Phase 1 contribution)
// NOTE (delivered 2026-07-17): the plan called for `: DomainException`
// but no such base class exists — the existing exceptions
// (`NotFoundException`, `BadRequestException`, `InternalServerException`)
// all extend `Exception` directly. The shipped type mirrors that
// pattern (traditional two-constructor shape, optional `Description`).
public class ForbiddenException : Exception
{
    public string? Description { get; }
    public ForbiddenException(string message = "Forbidden.") : base(message) { }
    public ForbiddenException(string message, string description) : base(message) { Description = description; }
}
```

…plus a `ForbiddenException → 403` arm in `CustomExceptionHandler.cs:17-48`. Now the identity guard becomes:

```csharp
var callerUserId = User.GetUserId();
if (callerUserId != requestUserId)
    throw new ForbiddenException($"Cannot {op} basket for {requestUserId} as {callerUserId}.");
```

…and every cross-tenant, cross-user, and admin-bypass-denied case reaches the client as a consistent `application/problem+json` payload. `AddProblemDetails()` is also registered in `Program.cs` (after `AddExceptionHandler<CustomExceptionHandler>()`) so an endpoint that does call `Results.Problem(...)` outside the exception flow still gets the same shape.

#### 0.4.5 Headers (request and response)

| Header | Direction | Rule |
|---|---|---|
| `Authorization: Bearer <jwt>` | request | Required on every endpoint; missing → 401 via the default policy (Phase 1 sets `.RequireAuthorization("Default")` on the `MapGroup`). |
| `Idempotency-Key: <uuid-v4>` | request | **Required** on `POST /api/v1/cart/checkout`; otherwise the request is rejected with 400 (Phase 2). Mirrors the IETF draft, kebab-case, UUID v4 only. |
| `X-Correlation-Id: <uuid-v4>` | request / response | Optional inbound; if present, `LogScope` carries it into all log lines for the request. If absent, the server assigns one (UUID v4) and **echoes** it in the response header so the FE can correlate. Lives in the `basket:idem:{userId}:{correlation}` Redis namespace. (Phase 4 sets the OTel exporter; Phase 1 sets the header.) |
| `If-None-Match: <etag>` | request | Optional on `GET /api/v1/cart`; Phase 4 stretch: returns 304 when the cart is unchanged. |
| `Accept: application/json` | request | Implicit (the API is JSON-only); `application/problem+json` accepted on errors. |
| `Content-Type: application/json` | request | Implicit on writes;`.ConfigureHttpJsonOptions(... PropertyNamingPolicy = null)` keeps PascalCase on the wire (existing). |
| `ETag` / `Last-Modified` | response | Added in Phase 4 once the cart has a monotonic `LastModifiedAt`. |
| `Cache-Control: no-store` | response | All cart endpoints; the Basket contains PII and must not be cached by intermediaries. The cart never returns `Cache-Control: public` or `private`. |

#### 0.4.6 Carter module shape (`MapGroup` + `WithOpenApi`)

The four `ICarterModule` files declare their own `MapGroup("/api/v1").WithTags("Baskets")`; this duplicates policy chains and `WithOpenApi()` opt-ins. Phase 1 collapses to one shared extension:

```csharp
// Basket.API/Endpoints/BasketEndpointGroup.cs (Phase 1 contribution)
// NOTE (delivered 2026-07-17): `WithOpenApi()` requires the
// `Microsoft.AspNetCore.OpenApi` package which is NOT yet a
// Basket.API.csproj dependency. The shipped extension centralises
// `RequireAuthorization("Default")` + `WithTags("Baskets")` and
// defers `WithOpenApi()` to Phase 4 alongside the Swagger generator.
public static class BasketEndpointGroup
{
    public static RouteGroupBuilder MapBasketGroup(this IEndpointRouteBuilder app) =>
        app.MapGroup("/api/v1")
            .RequireAuthorization("Default")
            .WithTags("Baskets");
}
```

…and each module calls `app.MapBasketGroup()`. Phase 4 brings in `AddSwaggerGen()` + `UseSwaggerUI()` for local dev only (auth-gated) AND re-enables `WithOpenApi()` on the group + adds `ExcludeFromOpenApi()` on the deprecated shim routes (today the shims simply skip the OpenAPI metadata with a `[DEPRECATED]` `WithSummary` marker — C#'s `[Obsolete]` attribute is code-only and doesn't have an HTTP-route equivalent).

#### 0.4.7 Empty-cart semantics

`GET /api/v1/cart` for a user with **no active cart** returns:

```json
{ "userId": "...", "restaurantId": "...", "items": [], "appliedDiscounts": [],
  "subtotal": 0, "discountAmount": 0, "totalItems": 0, "createdAt": "...", "expiresAt": null }
```

It does **not** return 404. The repository no longer throws `BasketNotFoundException` on read; it returns an empty `Basket` projected to the response shape. 404 stays as the error for any other lookup (e.g. an admin / audit endpoint that queries by Marten doc id). PUT on an empty cart creates the cart; PUT on a non-empty cart replaces it (with `LastModifiedAt` updated).

#### 0.4.8 Rate limiting

The checkout endpoint is the only "spend money" surface; it gets a `FixedWindowLimiter` keyed on `(userId, restaurantId)` (Phase 2):

```csharp
builder.Services.AddRateLimiter(o => o.AddPolicy("checkout", httpContext =>
{
    var key = $"{(httpContext.User.GetUserId())}:{(httpContext.User.GetRestaurantId())}";
    return RateLimitPartition.GetFixedWindowLimiter(key, _ => new(5, TimeSpan.FromMinutes(1)));
}));
```

The 429 response carries `Retry-After: <seconds>`. The other three endpoints stay unlimited (they're idempotent reads or trivial local writes).

#### 0.4.9 Authorization layering (was §0.4.1 in v0.1)

The identity check **runs in the handler, not the endpoint**:

```csharp
var callerUserId = User.GetUserId();
var callerRestaurantId = User.GetRestaurantId();
if (callerUserId != requestUserId || callerRestaurantId != requestRestaurantId)
    throw new ForbiddenException();   // see §0.4.4
```

Applied inside every `Handle` (Get, Store, Delete, Checkout). Endpoints stay lean — no `if (userId != caller)` plumbing. Phase 4 admin endpoints (`PUT /admin/carts/{userId}`, `DELETE /admin/carts/{userId}` for cross-account support tooling) take the caller check but allow a bypass via the `orders:admin` permission, evaluated by an `IBasketIdentityGuard` helper that understands the metadata-flagged admin route.

#### 0.4.10 Validation rules (single source of truth)

Like `DISCOUNT_SERVICE_PLAN.md §0.3.3`, this plan asserts "FluentValidation in handlers" but does not enumerate rules in prose — the locked list lives in code under each validator and is mirrored below. A phase is not "done" until every rule below ships a validator in that phase's commit, and the phase's Doc-update scope cites this section verbatim.

> **Note on per-endpoint vs per-command body shapes.** Today the API has wrapping `*Request` records (`StoreBasketRequest(Basket)`, `CheckoutBasketRequest(BasketCheckoutDto)`); those wrappers are removed when Phase 2 lands, so the command record binds to the request body directly. The validators listed below are against the **command** shape, which after Phase 2 doubles as the request body shape.

- **`GetCartQuery`** (Phase 1): constructed server-side from the JWT in the endpoint; no body, no parameters on the wire — the only validator-relevant fields come from the request envelope (the handler runs `RuleFor(x => x.UserId)` etc. via a static helper).

- **`UpsertCartCommand`** (Phase 1, body after Phase 2):
  - `Basket` — non-null.
  - `Basket.UserId` / `Basket.RestaurantId` — non-empty Guid (existing).
  - `Basket.Items` — non-null; count `<= 100`; each item:
    - `MenuItemId` — `> 0` (matches Catalog `MenuItem.Id : int`).
    - `Quantity` — `>= 1 && <= 99`.
    - `UnitPrice` — `> 0`.
    - `Variations` — count `<= 10`; each: `Name` non-empty + `<= 64 chars`; `Value` non-empty + `<= 64 chars`; `Price` `>= 0`.
    - `Customizations` — count `<= 20`; each: `Ingredient` non-empty + `<= 64 chars`; `Action` ∈ `{Add, Remove, Substitute}` (case-insensitive).
  - `Basket.AppliedDiscounts` — distinct, count `<= 10`, each code matches `^[A-Z0-9_-]{4,32}$`. *(Phase 2 swap: applied discounts become a `List<CouponSnapshot>` record carrying `Code`, `Description`, `DiscountAmount`, `AppliedAt`; the string list survives the move as a derived field for backwards compatibility.)*
  - `Basket.ExpiresAt` — `> CreatedAt` when both are set; defaulted server-side otherwise.
- **`DeleteCartCommand`** (Phase 1):
  - No body fields; the identity check (§0.4.9) supplies `(UserId, RestaurantId)`. Hand-rolled validator is unnecessary unless the handler accepts a body later (the admin endpoint in Phase 4 does; then a `DeleteCartByAdminCommand` carries `TargetUserId`).
- **`CheckoutCartCommand`** (Phase 2; record doubles as the request body — the `*Request` wrapper is removed):
  - `UserId` / `RestaurantId` — **forbidden on the wire**. The endpoint ignores the body values; the handler resolves them from the JWT. A validator rule (`RuleFor(x => x.UserId).Equal(Guid.Empty)`) **rejects** any user-supplied value with 422, closing the §2.10 spoofing footgun.
  - `FirstName` / `LastName` — non-empty + `<= 100 chars`.
  - `EmailAddress` — RFC-5322-shaped regex (kept loose; Identity owns the canonical email).
  - `AddressLine` — non-empty + `<= 200 chars`.
  - `Country` — ISO-3166-1 alpha-2 regex `^[A-Z]{2}$`.
  - `State` — non-empty + `<= 100 chars`.
  - `ZipCode` — non-empty + `<= 16 chars`.
  - `CardName` — non-empty + `<= 100 chars`.
  - `CardNumber` — Luhn-checked + digits-only + length `<= 19`. **`CardNumber` is `string` today; Phase 2 keeps it as `string`** with a server-side Luhn check. Card data lifecycle (whether to keep this column at all) is a v2 decision deferred until Phase 2's event design clarifies what Ordering actually needs (see §6 Phase 2 §).
  - `CVV` — `length == 3 || length == 4` (digits-only).
  - `Expiration` — format `MM/YY`, parsed to a month-end instant; `> clock.GetCurrentInstant()` at call time.
  - `PaymentMethod` — must be a defined enum value (new `BasketPaymentMethod { Card = 1, Cash = 2, Wallet = 3 }`).
  - **`Idempotency-Key` header** — required (Phase 2); absence → 400 via a `IEndpointFilter` before the command is constructed. UUID v4 strict regex `^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$`.

The list above is the contract. If a phase's commit adds a command not on this list, the implementer extends this section in the same commit. If a phase's commit removes a rule, the implementer strikes it here and notes the rationale in the v1.X+1 changelog.

#### 0.4.11 Correlation-Id enrichment via `BasketProblemDetailsFactory`

`CustomExceptionHandler` already produces a valid `ProblemDetails` body. v1 ships a thin per-service enrichment layer that adds two fields without breaking the RFC 7807 envelope:

```csharp
// Basket.API/ProblemDetails/BasketProblemDetailsFactory.cs
public sealed class BasketProblemDetailsFactory(IProblemDetailsService inner, IHttpContextAccessor accessor)
    : ProblemDetailsFactory  // wraps AddProblemDetails()'s default factory
{
    public override ProblemDetails Create(...) => inner.Create(...);

    public override bool CanHandle(...) => inner.CanHandle(...);

    public override ValueTask<bool> HandleAsync(HttpContext ctx) => inner.HandleAsync(ctx);

    // Override just the enrich step:
    protected override ProblemDetails EnrichProblemDetails(ProblemDetails problem, int? statusCode)
    {
        problem.Extensions["traceId"] = Activity.Current?.TraceId.ToString()
                                      ?? ctx.TraceIdentifier;

        var correlationId = ctx.Request.Headers["X-Correlation-Id"].FirstOrDefault()
                         ?? ctx.Request.Headers["X-Request-Id"].FirstOrDefault();
        if (!string.IsNullOrEmpty(correlationId))
            problem.Extensions["correlationId"] = correlationId;

        var rest = ctx.Request.Headers["X-Restaurant-Id"].FirstOrDefault();
        if (Guid.TryParse(rest, out var restaurantId))
            problem.Extensions["restaurantId"] = restaurantId;

        return problem;
    }
}
```

- **`X-Correlation-Id`** is the canonical name (matches `OpenTelemetry` SDK reading); **`X-Request-Id`** is accepted as an alias for clients that already use it (mirrors what some Yarp middleware injects). Both populate `problem.Extensions["correlationId"]` so consumers correlate on one field.
- The factory is **Phase 1 work**, registered in `Program.cs` immediately after `AddExceptionHandler<CustomExceptionHandler>()`. Tests assert the three extensions are populated for a forced 404 (`GetCartEndpointTests.NotFound_PopulatesTraceIdAndCorrelationId`).
- `IPublisher` consumers reading the response body can rely on the `traceId` matching the OTel `Activity.TraceId`, so logs and traces align.

#### 0.4.12 API versioning strategy

The API uses **URL-segment versioning** (`/api/v1/`). The migration policy:

| Trigger | Action |
|---|---|
| Additive change (new endpoint, new optional field) | Same version. Documented in `docs/api/basket-api-v1.json` (Phase 4 onwards). |
| Backwards-compatible change (new field, new enum value) | Same version. Wire-level `BasketCheckoutEvent` gains fields only with a `SchemaVersion` bump (Catalog §6.5). |
| Breaking wire-level change (renamed/removed field, changed status code, narrowed error envelope) | New `/api/v2/` route group, runs alongside v1 for one quarter before v1 sunsets. |
| Basket-internal refactor (handler signing, repository shape) | No version change. |

The Yarp gateway routes `/api/v1/baskets/**` and `/api/v2/baskets/**` to whichever service version is live; both versions of `Basket.API` coexist in the multi-version deployment if needed (rare — typically only during a sunset window).

#### 0.4.13 `[AsParameters]` for complex query payloads

The four v1 cart queries have 0–2 route params and don't need `[AsParameters]`. Phase 4's admin endpoints will exceed that threshold and adopt the pattern:

```csharp
// Paged list for CS support tooling:
public record ListCartsQuery(
    [FromQuery] Guid? RestaurantId,
    [FromQuery] string? UserEmailContains,
    [FromQuery] DateOnly? ExpiredBefore,
    [FromQuery] int Page = 1,
    [FromQuery] int PageSize = 20);

// Endpoint:
group.MapGet("/admin/carts", async ([AsParameters] ListCartsQuery query, ISender sender) =>
{
    var result = await sender.Send(query);
    return Results.Ok(result);
});
```

`Page` / `PageSize` follow the `BuildingBlocks.Pagination.PaginatedResult<T>` contract (Catalog §0.4.5) with `pageSize` hard-capped at `100`. The validator (`ListCartsQueryValidator`, Phase 4) enforces `RestaurantId` non-empty when `UserEmailContains` is set (at least one filter is required).

---

## 1. Context

Basket is the **intermediate persistence layer between the customer's cart UI and the Ordering service's order-creation flow**. A user adds menu items to a Basket; checkout publishes a `BasketCheckoutEvent` and deletes the Basket; Ordering consumes the event and creates an `Order` aggregate.

Today, four processes work end-to-end:
1. **GetBasket** — fetch a Basket by `(UserId, RestaurantId)` (cached in Redis).
2. **StoreBasket** — upsert a Basket (clears cache, applies discounts in a `#warning` no-op).
3. **DeleteBasket** — drop a Basket (clears cache).
4. **CheckoutBasket** — load the Basket, publish `BasketCheckoutEvent`, delete the Basket, return.

What does **not** work:
- The `Basket` entity does not implement `ITenantEntity` and the Marten store has no multi-tenancy filter. Any authenticated user can access any other user's Basket if they pass the right URL.
- The `discountAmount` calculation in `StoreBasketHandler` is a `#warning` — the published `BasketCheckoutEvent` carries `AppliedDiscounts` as strings only, so the order is created without the discount applied. A coupon can also be injected by the client with no server-side cap.
- `Publish` then `Delete` is non-atomic. A publish-succeed/delete-fail double-charges the customer on retry; a publish-fail/delete-succeeds loses the order silently.
- No idempotency key on checkout. Network retries create duplicate orders.
- No `/live` + `/ready` split, no OpenTelemetry, no cache-stampede guard, no gRPC resilience, no tests project.

The architecture (`docs/architecture/architecture.md` §3, `current-architecture.md` §4.3) treats Basket as a thin Redis-fronted Marten document store that consumes no events. The gaps above violate that contract in ways that need fixing before Basket can be exposed to multi-tenant production traffic.

---

## 2. Goal

Harden `Basket.API` to production-grade by adding:

1. **Multi-tenancy + JWT cross-check** — every endpoint refuses cross-tenant/cross-user access (per §0.4.1).
2. **Atomic checkout via outbox** — `BasketCheckoutEvent` and Basket deletion are part of the same Marten unit-of-work; a publish failure does not delete the basket; a delete failure does not publish.
3. **Idempotent checkout** — `Idempotency-Key` header requirement; replays short-circuit.
4. **Real discount integration** — `Basket.DiscountAmount` is computed server-side from `AppliedDiscounts`, persisted, and emitted on `BasketCheckoutEvent`.
5. **Cache stampede protection** — single-flight `GetOrCreate` on `CachedBasketRepository.GetBasketAsync`.
6. **Card data lifecycle decision** — CardNumber/CVV stay in `BasketCheckoutDto` for now, but the v1 event payload drops them; only the last-four + brand travel in `BasketCheckoutEvent`.
7. **Observability** — `/live` + `/ready` split, OpenTelemetry tracing across Carter → MediatR → Marten → Redis → MassTransit.
8. **Tests** — `Basket.API.Tests` project (xUnit + FluentAssertions + Moq + Testcontainers).

---

## 3. Out of scope (v1)

- **Removing `Basket.API`'s Marten store** — the document model is right for ephemeral cart state; do not migrate to EF Core.
- **Splitting `Basket` into per-restaurant ACL rules** — that is Identity's role; Basket enforces only the strict `(userId, restaurantId)` match.
- **Adding a customer-facing "save cart for later"** — every active Basket has `ExpiresAt = now + 30min`; a "save" feature is a v2 follow-up.
- **Walk-in or kiosk flows** — those bypass Basket entirely and call Ordering directly.
- **The `BasketItem.Name` snapshot field** — adding it is cheap (Phase 2 recommendation in the review) but depends on a Catalog-side decision (`MenuItem.Name` snapshot vs live lookup); deferred to v2 with a hand-off note.
- **Removing `CardNumber` from `BasketCheckoutDto` entirely** — Identity owns payment tokens in v2; for v1 we redact on transit and emit only last-four + brand to the bus.
- **CORS configuration** — Yarp is the only browser-facing surface; Basket stays CORS-free. If a second browser-channel is added in v2, it gets its own CORS policy at the gateway, not here.
- **Cross-cutting rate limiter on reads/upserts** — `DeleteCart` and `UpsertCart` stay unlimited in v1; only `CheckoutCart` carries the limiter. v2 may extend the policy if abuse surfaces.
- **HATEOAS / hypermedia links** — v1 is RESTful level 2 (proper verbs + status codes); level 3 adds `Link` headers or HAL, which is deferred.
- **GraphQL / gRPC front-door** — Basket stays Carter-only; the gRPC client is a *server-side* consumer (Discount). External callers use the Carter API.
- **Pagination on `Basket`** — a cart is a singleton per user; pagination does not apply. (Phase-4 admin paged list `GET /admin/carts` — if it lands — uses the `PaginatedResult<T>` building block from Catalog §0.4.5.)

---

## 4. Service boundaries

### Basket.API owns

- **`Basket`** Marten document (`Models/Basket.cs`) — one row per `(UserId, RestaurantId)`; indexed by `[Identity]` on `UserId` plus a query-filter on `RestaurantId`.
- **`BasketItem`** sub-document — quantity, unit price, variations, customizations. Snapshot fields (`MenuItemId`, `UnitPrice`) are taken at add-time; the catalog is the canonical source for catalog-truth (Phase 5 hand-off note).
- **Idempotency records** — Redis-backed `basket:idem:{userId}:{restaurantId}:{key}` rows, 24h TTL, populated by `IdempotencyMiddleware` (the `IEndpointFilter` on `POST /cart/checkout`, Phase 2) before the handler runs.
- **`Idempotency-Key` consumption** — every `POST /api/v1/cart/checkout` (Phase 2) reads the header (UUID v4); replays return the cached response without re-running the pipeline. Key reuse with a different body returns **422 Unprocessable Content** per the IETF draft; key reuse with the same body returns **200 OK** + cached body.

### Basket.API does NOT own

- **Order data** — created by Ordering from `BasketCheckoutEvent`; Basket never writes to Ordering's tables.
- **Discount data** — owned by Discount.Grpc. Basket calls `GetDiscountAsync` to *look up* coupon metadata and may persist a snapshot of the discount *amount* on the basket, but the canonical coupon record lives in Discount.Grpc.
- **Menu item data** — owned by Catalog.API. Basket snapshots the price/name at add-time.
- **Tenant identity** — issued by Identity.API as `restaurantId` claim; Basket reads it from the JWT and never invents it.

### Events consumed (v1)

| Event | Source | Action |
|---|---|---|
| _(none)_ | — | Basket publishes `BasketCheckoutEvent`, consumes nothing in v1. |

### Events published (v1)

| Event | Trigger | Schema version |
|---|---|---|
| `BasketCheckoutEvent` | `POST /api/v1/cart/checkout` succeeds (Phase 2 atomic publish) | 1 — `int SchemaVersion = 1`; carries `UserId`, `RestaurantId`, `TotalAmount`, `DiscountAmount`, `AppliedDiscounts` (list of strings — derived from the `CouponSnapshot[]` for backwards compat), `Items[]` (each: MenuItemId, Quantity, UnitPrice, TotalPrice, Variations, Customizations), `PaymentMethodSummary { Method, Brand, LastFour }`. **No `CardNumber`, `CVV`, full `AddressLine`.** Ordering receives the address via a separate `Identity.AddressSnapshot` lookup tied to the `UserId`; the wire-level event never carries PII the consumer doesn't need. |

---

## 5. Tech decisions

| Decision | Choice | Reason |
|---|---|---|
| Architecture | Vertical Slice, single project | Matches Catalog / Discount. |
| Framework | ASP.NET Core 10 (Carter + minimal API) | Project standard. |
| Language | C# 12+ (records, primary constructors, nullable enabled) | Project standard. |
| Persistence | Marten (Postgres) — document store | Existing; the right shape for cart state. |
| Cache | Redis via `IDistributedCache` (existing); **Phase 3** adds single-flight guard via `SemaphoreSlim` registry | Reduces DB read pressure on hot paths. |
| Multi-tenancy | `Basket : ITenantEntity` + `MultiTenanted()` Marten store + `ICurrentRestaurantProvider` filter | Reuses BuildingBlocks. Dormant before Phase 1; alive after. |
| Checkout atomicity | `BuildingBlocks.Messaging.Outbox.OutboxDispatcher<TContext>` (mirror Discount's pattern) | Marten-side outbox row written in the same session as Basket state changes; `IHostedService` consumes outbox → publishes to RabbitMQ. |
| Idempotency | Redis SET-NX keyed on `(userId, restaurantId, Idempotency-Key)`, 24h TTL | Same shape used in DISCOUNT_SERVICE_PLAN §0.4.1. |
| Card data | `BasketCheckoutDto` keeps the raw fields (clients integrate them today); **`BasketCheckoutEvent` drops them**; PaymentMethod enum becomes the source of truth downstream | Pragmatic v1; v2 plans PCI tokenization through Identity. |
| Tests | xUnit + FluentAssertions + Moq; Testcontainers (Postgres + Redis + RabbitMQ) | Mirror Catalog/Discount. |
| Time / IDs | NodaTime `Instant`, `Guid` ids | Project convention. |

> **Skill mandate:** all implementation invokes `/csharp-developer` and follows the `dotnet-best-practices` + `api-design-principles` guard rails, same as the Catalog plan §0.

---

## 6. Phased milestones

### Phase 1 — Tenant safety + identity cross-check (SECURITY-CRITICAL)

**Status:** ✅ **Delivered 2026-07-17** as two commits — `(1) BuildingBlocks` and `(2) Basket.API`. 15/15 tests passing (BuildingBlocks 6 + Basket.API 9). Build: 0 errors, 0 new warnings under `-p:TreatWarningsAsErrors=true`.

**Real drift captured in this delivery:**

1. **`ForbiddenException : Exception`, not `: DomainException`** — no `DomainException` base class exists in the codebase. The other 3 exceptions extend `Exception` directly. Mirrored that pattern. See §0.4.4 for the corrected shape.
2. **`WithOpenApi()` deferred to Phase 4** — requires the `Microsoft.AspNetCore.OpenApi` package which is not in `Basket.API.csproj`. `MapBasketGroup` ships with `RequireAuthorization("Default")` + `WithTags("Baskets")` only.
3. **`[Obsolete]` attribute on routes** — C#'s `[Obsolete]` is code-only. The deprecated shims use a `[DEPRECATED]` marker in `WithSummary(...)` + `WithDescription(...)` instead. C# attribute on the ICarterModule class would not propagate to the route definition.
4. **`Basket.API.Tests` scaffolded ahead of plan** — the plan reserved project bootstrap for Phase 5, but Phase 1 created the project shell + 3 test files (4 if you count `RecordingLogger<T>` as a separate fixture). Phase 5 expands with Testcontainers + Verify + `BasketWebApplicationFactory`.
5. **`X-Correlation-Id` response header echo NOT yet wired** — `LoggingBehavior` already reads the inbound header and stamps the ambient `CorrelationContext`, but it does not write the header back on the response. Phase 4's OTel work picks this up; for now the FE correlates via `traceId` in the ProblemDetails body.
6. **`#warning` TODO in `StoreBasketHandler` remains** — the placeholder comment is still in the file. Phase 2 swaps the `#warning` no-op for the real `Parallel.ForEachAsync` discount loop.

**BuildingBlocks commit:**

- [x] New `BuildingBlocks/Exceptions/ForbiddenException.cs` (`Exception` subclass — drift item 1). Arm added to `CustomExceptionHandler.cs:17-48` so the handler emits 403 + `application/problem+json`. XML docs explain the 401-vs-403 distinction.
- [x] `BuildingBlocks/Behaviors/ValidationBehavior.cs:9` drops the `ICommand<TResponse>` constraint → `IRequest<TResponse>` (§0.4.2). Regression test `ValidationBehaviorTests.QueryValidator_RunsThroughPipeline` (and 3 more: query failure throws, query failure skips handler, empty validator list passes through).
- [x] New `BuildingBlocks/Behaviors/PciSensitiveAttribute.cs` — marker attribute. The `LoggingBehavior` reflection check is cached via `ConcurrentDictionary<Type, bool>` so the hot path is a dictionary read after the first invocation.
- [x] New `BuildingBlocks.Tests/` project (xUnit 2.9.3, FluentAssertions 6.12.2, NSubstitute 5.3.0, `Microsoft.AspNetCore.App` framework reference). 6 tests across `ValidationBehaviorTests` (4) + `CustomExceptionHandlerTests` (2).

**Basket.API commit:**

- [x] `Basket : ITenantEntity` (`Models/Basket.cs`); XML doc on the class ties it back to `ICurrentRestaurantProvider`.
- [x] `Program.cs`: `opt.Schema.For<Models.Basket>().MultiTenanted();` registered with the existing `AddMarten(...)` call. `MultiTenanted()` coexists with `CreateDatabasesForTenants` — verified by clean build.
- [x] `Program.cs`: `AddHttpContextAccessor()` + `AddScoped<ICurrentRestaurantProvider, ClaimsRestaurantProvider>()` registered (the latter is not part of `AddAuthorizationServices()` — Phase 1 wires it separately).
- [x] `BasketRepository` (and `CachedBasketRepository`) inject `ICurrentRestaurantProvider`; every read/write asserts `basket.RestaurantId == provider.RestaurantId`; mismatch → `throw new ForbiddenException(...)` via the new private `AssertTenant(...)` helper. New `GetActiveCartOrEmptyAsync(...)` returns empty Basket on miss (no throw). `GetBasketAsync(...)` (throws) stays for admin / audit. Cache layer does not re-implement the filter — exceptions from inner propagate.
- [x] `Basket.API/Behaviors/IBasketIdentityRequest.cs` marker + `Basket.API/Behaviors/BasketIdentityGuardBehavior.cs` pipeline behavior registered BEFORE `ValidationBehavior<,>` in `AddMediatR`. Implements the §0.4.9 identity check. Applies to every command/query that implements the marker — today that's all 4 basket commands + queries.
- [x] All 4 cart commands/queries updated to implement `IBasketIdentityRequest` (`UserId`/`RestaurantId` accessors). `GetBasketHandler` switched to `GetActiveCartOrEmptyAsync` to lock the §0.4.7 contract.
- [x] New `Basket.API/Endpoints/BasketEndpointGroup.cs` (`MapBasketGroup` extension) centralises `RequireAuthorization("Default")` + `WithTags("Baskets")` — drift item 2 deferred `WithOpenApi()` to Phase 4.
- [x] **URL rename (§0.4.1):** new endpoints expose `/api/v1/cart` (token-bound). The old `/api/v1/baskets/{userId}/{restaurantId}` route survives as a `[DEPRECATED]` shim (drift item 3), returns the same payload, removed at end of Phase 3.
- [x] `LoggingBehavior` redacts payload lines for `CheckoutBasketCommand` (annotated `[PciSensitive]`) and any future PII/PCI-bearing commands. The reflection check is cached per-type on first read.
- [x] `services.AddProblemDetails()` registered after `AddExceptionHandler<CustomExceptionHandler>()` (closes the empty-403 gap).
- [x] `CheckoutBasketCommand` annotated `[PciSensitive]` so its payload is redacted in every log line.
- [x] `Basket.API/GlobalUsings.cs` promoted four namespaces per §0.3.1 (`System.Security.Claims`, `BuildingBlocks.Multitenancy`, `Microsoft.Extensions.Caching.Distributed`, `Microsoft.AspNetCore.Http`) plus `Basket.API.Behaviors`, `Basket.API.Endpoints`, `Basket.API.Data` for the new files.
- [x] New `Basket.API.Tests/` project (xUnit + FluentAssertions + NSubstitute, drift item 4). 9 tests across `BasketIdentityGuardBehaviorTests` (5), `GetBasketHandlerTests` (2), `LoggingBehaviorRedactionTests` (2). Includes a `RecordingLogger<T>` test double that captures log calls directly (sidesteps Castle Dynamic Proxy issues with `ILogger<ILoggingBehavior<closedGenericCommand, object>>`).
- [x] **Doc-update scope (delivered):** `current-architecture.md` §3 (Solution Layout — added Basket.API.Tests), §4.3 (Basket Service — `ITenantEntity`, `MultiTenanted()`, identity guard, MapBasketGroup, URL rename, LoggingBehavior redaction, AddProblemDetails, deprecation shim table, repository contract, [PciSensitive] marker), §9 (Cross-Cutting Patterns — `BasketIdentityGuardBehavior` mention + `[PciSensitive]` redaction rule), §10 (ForbiddenException note). §11 / §12 deferred to Phase 2 / Phase 4.

**Phase 1 — checklist coverage vs. plan:**

| Plan test | Where it landed |
|---|---|
| `GetCartHandlerTests.Anonymous_ReturnsUnauthorized` | `BasketIdentityGuardBehaviorTests.UnauthenticatedRequest_ThrowsForbiddenException` — pipeline-level equivalent. The "endpoint returns 401 without JWT" assertion is the responsibility of the `Default` authorization policy + `MapBasketGroup`'s `RequireAuthorization("Default")`; the plan-confirmed check is via `WebApplicationFactory` in Phase 5. |
| `GetCartHandlerTests.OtherUser_ReturnsForbidden` | `BasketIdentityGuardBehaviorTests.OtherUser_ReturnsForbidden` |
| `GetCartHandlerTests.CrossTenant_ReturnsForbidden` | `BasketIdentityGuardBehaviorTests.CrossTenant_ReturnsForbidden` |
| `GetCartHandlerTests.NoCartYet_Returns200WithEmptyBody` | `GetBasketHandlerTests.NoCartYet_ReturnsEmptyBasket` — handler-level projection. The "endpoint returns 200 with empty body" wire-shape assertion is Phase 5's `WebApplicationFactory` job. |
| `LoggingBehaviorTests.CheckoutBasketCommand_Payload_RedactedInLogs` | `LoggingBehaviorRedactionTests.PciSensitiveCommand_PayloadIsRedactedInLogs` (+ `NonSensitiveCommand_PayloadIsLogged` as the negative-case guard) |
| `EndpointGroupTests.MapBasketGroup_RequiresAuthentication` | Deferred to Phase 5 (`WebApplicationFactory` is the right tool for endpoint-level policy assertions). |

### Phase 2 — Atomic checkout + real discount (CORRECTNESS-CRITICAL)

**Status:** 🚧 **Atomic-checkout sub-deliverable delivered 2026-07-18** (commit pending). Real-discount integration (`#warning` TODO removal), idempotency middleware, payment-method redaction, and rate-limiter are still ⏳ not started.

**Real drift captured in this delivery:**

1. **`CheckoutBasketOutboxDispatcher` does not extend `OutboxDispatcher<TContext>`.** The base class is EF-Core-shaped (`DbSet<OutboxMessage>`, `DatabaseFacade`, `FromSql(BuildClaimSql(...))`); the Marten `IDocumentSession` cannot satisfy the `IOutboxDbContext` contract. The dispatcher reimplements the polling loop in the same shape as the base class — same active/idle poll intervals, same OperationCanceledException short-circuit, same per-row poison-row handling. A future `BuildingBlocks.Messaging.Outbox.MartenOutboxDispatcher<TStore>` can be factored once a second Marten-using service adopts the pattern.
2. **`BrokerHealthState` + circuit breaker deferred to Phase 2.x.** Discount's breaker is a per-service state machine; copying it into Basket is mechanical but adds a `/ready` health-check entry that doesn't exist in Basket yet. Phase 2 v1 ships the linear dispatcher; the breaker is a follow-up if broker outages surface.
3. **Claim is Marten LINQ + optimistic concurrency, not raw `FOR UPDATE SKIP LOCKED`.** `mt_version` (the optimistic-concurrency column Marten maintains per document) detects concurrent updates via a `ConcurrencyException` on the second `SaveChangesAsync`; multi-replica safety requires switching to a raw-SQL claim (`SELECT ... FOR UPDATE SKIP LOCKED`) through `IDocumentSession.Connection`. Phase 2 v1 is single-replica (matches Basket's current deployment), so the lighter approach is sufficient. The multi-replica switch is a Phase 4 hand-off alongside the OpenTelemetry work.
4. **`JsonSerializerOptions` duplicated.** The handler's outbox-payload serialization repeats the 4-line options block from `BuildingBlocks.Messaging.Outbox.OutboxPublisher<TContext>.SerializerOptions`. A future BuildingBlocks contribution could expose `OutboxSerializer.SerializePayload<T>(message)` + `DeserializePayload(string, Type)` helpers; tracked as a Phase 2.x refactor — non-blocking.
5. **`InvalidateCacheAsync` added to `IBasketRepository`.** The handler needs to clear the Redis cache *after* the Marten commit so a concurrent reader can't see a deleted basket in the cache. Splitting the cache invalidation from the Marten delete keeps the inner `BasketRepository` free of caching concerns (no-op on the inner; cache-only on the `CachedBasketRepository` decorator).
6. **Card-redaction on the wire (drop `CardNumber`/`CVV`, replace with `PaymentMethodSummary`) is still deferred.** `BasketCheckoutEvent` lives in `BuildingBlocks.Messaging.Events` — the redaction is a BuildingBlocks contribution that ships in Phase 2.1 (separate commit). Phase 2 v1 only changed the *delivery mechanism* (outbox), not the *payload shape*.

**Basket.API commit (atomic-checkout sub-deliverable):**

- [x] New `Basket.API/Messaging/CheckoutBasketOutboxMessage.cs` — Marten document mirroring `OutboxMessage`'s row shape (`Id / OccurredOn / Type / Payload / DispatchedAt / SchemaVersion`). `OccurredOn` + `DispatchedAt` carry `[DuplicateField]` so they extract to typed Postgres columns (`occurred_on`, `dispatched_at`) alongside the JSONB `data` column. `MultiTenanted()` registration adds a `tenant_id` column.
- [x] New `Basket.API/Messaging/CheckoutBasketOutboxDispatcher.cs` — `BackgroundService, IAsyncDisposable`. Polls the Marten store every `OutboxOptions.ActivePollInterval`; LINQ claim (`Where DispatchedAt == null OrderBy OccurredOn Take(batchSize)`); `IPublishEndpoint.Publish` per row; stamps `DispatchedAt`; `SaveChangesAsync` commits the stamp + releases the optimistic-concurrency lock. Schema-version gate (skips rows with `SchemaVersion > MaxSupportedVersion`). Per-row failures leave `DispatchedAt` null (next-tick retry). `BeginScope` carries `OutboxMessageId / MessageVersion / EventType / CorrelationId` per §0.3.4.
- [x] `CheckoutBasketCommandHandler` rewritten — atomic publish-and-delete. Handler injects `IDocumentSession` alongside `IBasketRepository`; loads the basket, validates non-empty, builds the `BasketCheckoutEvent`, stages `CheckoutBasketOutboxMessage` via `session.Store(...)`, deletes the basket via `session.Delete(basket)`, and **one** `await session.SaveChangesAsync(ct)` commits both writes. Cache invalidation (`basketRepository.InvalidateCacheAsync`) runs after the commit. The handler no longer calls `IPublishEndpoint.Publish` directly — the dispatcher owns that.
- [x] `IBasketRepository.InvalidateCacheAsync(userId, restaurantId, ct)` added. `BasketRepository.InvalidateCacheAsync` is a no-op (cache lives in the decorator); `CachedBasketRepository.InvalidateCacheAsync` calls `cache.RemoveAsync(...)`.
- [x] `Program.cs`: `opt.Schema.For<CheckoutBasketOutboxMessage>().MultiTenanted();` registered alongside the existing `opt.Schema.For<Models.Basket>().MultiTenanted();`. `OutboxOptions` bound via `AddOptions<>().Bind(...).ValidateDataAnnotations().ValidateOnStart()` (the existing `OutboxOptions` in `BuildingBlocks.Messaging.Outbox`). `AddHostedService<CheckoutBasketOutboxDispatcher>()` registered.
- [x] `appsettings.json`: new `Outbox` section with the same defaults Discount uses (`Enabled=true, ActivePollInterval=1s, IdlePollInterval=5s, BatchSize=100, MaxSupportedVersion=1, MaxConsecutiveBrokerFailures=3, BrokerBackoffSeconds=60s`).
- [x] `GlobalUsings.cs` — two new global usings (per §0.3.1 2+ files rule): `Basket.API.Messaging` (handler + Program.cs) and `BuildingBlocks.Messaging.Outbox` (dispatcher + Program.cs).
- [x] New `Basket.API.Tests/Unit/CheckoutBasketCommandHandlerTests.cs` — 3 tests:
  - `EmptyBasket_ReturnsFailureResult_DoesNotTouchSession` — locks §0.4.3 (409 when basket is empty): no `Store`, no `Delete`, no `SaveChangesAsync`.
  - `SuccessfulCheckout_StagesOutboxAndDeletesBasket_OneCommit` — the atomicity hinge: `session.Received(1).Store(outbox)` + `session.Received(1).Delete(basket)` + `await session.Received(1).SaveChangesAsync(...)`. Any future contributor who splits this into two commits breaks Phase 2.
  - `OutboxPayload_DeserializesToBasketCheckoutEvent_WithExpectedFields` — locks the v1 payload contract (incl. `CardNumber`/`CVV` still on the wire; Phase 2.1 will redact).
- [x] **Doc-update scope (delivered):** `current-architecture.md` §4.3 (Basket Service — events-published paragraph replaced with the outbox reality), §5.2 (BasketCheckoutEvent publisher cell points at the dispatcher), §6 Data Stores (basketdb row mentions `CheckoutBasketOutboxMessage`), §9 Cross-Cutting Patterns (new bullet: outbox row via Marten).

**Remaining Phase 2 work (real-discount integration, idempotency middleware, payment-method redaction, rate-limiter):**

> **Prerequisites (all in place after the atomic-checkout sub-deliverable).** `CheckoutBasketCommand` carries `[PciSensitive]` (Phase 1). `BasketIdentityGuardBehavior` runs in the pipeline before any handler logic. `IBasketRepository` exposes `GetActiveCartOrEmptyAsync(...)` (Phase 1). `BuildingBlocks.Messaging.Outbox.OutboxOptions` is in place. `BasketCacheLockRegistry` + single-flight guard (Phase 3 work) are NOT yet wired — Phase 2's atomic checkout doesn't depend on cache stampede protection; the un-hardened cache layer is acceptable until Phase 3.

- [x] **Real-discount integration (sub-deliverable 2.2)** ✅ **Delivered 2026-07-18** (commit pending). 23 / 23 tests passing in `Basket.API.Tests` (12 prior + 11 new StoreBasket discount-loop tests). Strict build `-p:TreatWarningsAsErrors=true` clean (the Phase-1-era `#warning` TODO in `StoreBasketHandler.cs:46` is gone).
  - New `Basket.API.Discount.IDiscountLookup` abstraction (`Basket.API/Discount/IDiscountLookup.cs`) + `GrpcDiscountLookup` implementation (`Basket.API/Discount/GrpcDiscountLookup.cs`). The raw `DiscountProtoServiceClient` is wrapped so the discount loop is unit-testable (gRPC's `AsyncUnaryCall<T>` doesn't mock cleanly with NSubstitute). The wrapper normalises wire shape — `string ExpirationDate` → NodaTime `Instant`, `double Amount` → `decimal`, closed `DiscountType` enum passthrough — and fail-closes on parse errors. Program.cs registers `services.AddScoped<IDiscountLookup, GrpcDiscountLookup>()` alongside the existing gRPC client registration.
  - `Basket` domain gains `DiscountAmount : decimal` (sum clamped to `Subtotal`) and `AppliedCoupons : List<CouponSnapshot>` (per-coupon breakdown, each entry unclamped). `Basket.Total` is a derived property `Math.Max(Subtotal - DiscountAmount, 0m)`. `AppliedDiscounts : List<string>` survives for wire compatibility — the user-input coupon codes don't change.
  - `StoreBasketHandler` rewritten: parallel `Parallel.ForEachAsync(MaxDegreeOfParallelism = 4)` over `AppliedDiscounts`, eligibility gate mirrors `Discount.Grpc.Domain.ActiveNow.Coupon` (minus the `DeletedAt` half — Discount's global query filter excludes soft-deleted coupons before they reach the wire), per-coupon `DiscountAmount` computed against `DiscountType` (PERCENTAGE / FIXED_AMOUNT / UNSPECIFIED→0). gRPC failures fail-closed (broker down / malformed `ExpirationDate` → `InvalidOperationException`).
  - `CachedBasketRepository` now uses shared NodaTime-aware `JsonSerializerOptions` (Phase 1 drift item — `ConfigureForNodaTime(DateTimeZoneProviders.Tzdb)` on the cache layer; otherwise cached baskets would round-trip with `Instant` defaulted to `default(Instant)`).
  - `Basket.API/Discount` promoted to a global using (handler + Program.cs + tests, 2+ files).
- [x] **Idempotency middleware (sub-deliverable 2.3)** ✅ **Delivered 2026-07-18** (commit pending). 31 / 31 tests passing in `Basket.API.Tests` (9 Phase 1 + 3 atomic-checkout + 11 real-discount + 8 new BasketIdempotencyFilter tests). Strict build `-p:TreatWarningsAsErrors=true` clean.
  - New `Basket.API/Idempotency/` namespace hosts `BasketIdempotencyFilter` (Carter `IEndpointFilter`), `BasketIdempotencyOptions` (strongly-typed config), `IBasketIdempotencyKeyProvider` + `BasketIdempotencyKeyProvider` (HMAC envelope), and `IdempotencyCacheEntry` (cached payload record).
  - IETF `draft-ietf-httpapi-idempotency-key-header` contract: required UUID v4 header (strict regex `^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$`), body-matching replay returns `200 OK + cached body` + `Idempotent-Replayed: true`, body-mismatching replay returns `422 Unprocessable Content` (NOT 409 per the IETF draft), absence/malformed returns `400`.
  - Redis key shape `basket:idem:{userId}:{restaurantId}:{idempotencyKey}` — tenant-scoped so a cross-user replay misses at the Redis level (belt-and-braces with `BasketIdentityGuardBehavior`).
  - Body fingerprint: `HMAC-SHA256(Basket:Idempotency:SecretHex, "${userId}|${restaurantId}|${sha256(body)}")` — HMAC envelope binds the (user, restaurant, body) triple to the server-side secret, preventing cache-poisoning by an attacker with Redis read access but no secret.
  - TTL 24h (configurable via `Basket:Idempotency:Ttl`). Fail-closed on Redis errors: GET failure → `503 Service Unavailable` (handler does NOT run); SET failure is logged but the request still succeeds (next replay will miss).
  - The deprecated `/baskets/checkout` shim does NOT carry idempotency (it'll be removed at end of Phase 3 — adding idempotency to a route on the way out is wasted work).
  - Mirror of `Discount.Grpc.Authorization.IIdempotencyKeyProvider` (Basket-side variant — separate secret in `Basket:Idempotency:SecretHex` because sharing the secret would let a Discount-cache-poisoning bug bleed into Basket's namespace).
  - `Program.cs`: registered a shared `IConnectionMultiplexer` Singleton (constructed up-front) and wired `AddStackExchangeRedisCache(opts => opts.ConnectionMultiplexerFactory = () => sharedMultiplexer)` so the cache layer and the idempotency filter share one connection (IDistributedCache doesn't expose SETNX-with-EX — `IConnectionMultiplexer.GetDatabase().StringSetAsync(key, value, expiry, When.NotExists)` is the atomic operation). `BasketIdempotencyOptions` bound via `AddOptions<>().Bind().ValidateDataAnnotations().ValidateOnStart()`. The filter is registered as Scoped (per-request instance); the underlying `IConnectionMultiplexer` is the shared Singleton.
  - 8 new unit tests in `Basket.API.Tests/Unit/BasketIdempotencyFilterTests.cs`: `MissingIdempotencyKey_Returns400`, `EmptyIdempotencyKey_Returns400`, `MalformedIdempotencyKey_Returns400`, `FirstRequest_RunsHandler_AndCachesResult`, `ReplayWithSameBody_ReturnsCached200_AndShortCircuitsHandler`, `ReplayWithDifferentBody_Returns422`, `CrossUserReuse_DoesNotCollide_BecauseRedisKeyIsTenantScoped`, `RedisGetFailure_Returns503_AndDoesNotRunHandler`. Includes a `FixedHmacKeyProvider` test double that bypasses `IConfiguration` + `IOptions` for fast deterministic tests.
  - **Doc-update scope (delivered):** `current-architecture.md` §4.3 (Basket Service — new "Idempotency-Key (Phase 2.3)" paragraph after the Discount integration block; UUID v4 regex, replay semantics, 422-not-409, Redis key shape, body fingerprint HMAC envelope, fail-closed policy, deprecated-shim exclusion).
- [x] **Payment-method redaction (sub-deliverable 2.1, BuildingBlocks contribution)** ✅ **Delivered 2026-07-18** (commit pending). New `PaymentMethod` enum (see §0.4.10) replacing the `int` field; `BasketCheckoutDto.CardNumber` stays as `string` but the new `BasketCheckoutEvent` payload includes **only** `PaymentMethodSummary { Method, Brand, LastFour }`. The `BasketCheckoutEvent` shape change is a BuildingBlocks contribution (the event lives in `BuildingBlocks.Messaging.Events`); Ordering's consumer must be updated in lockstep — flagged in §7 cross-service notes.
- [x] **Checkout rate-limiter (sub-deliverable 2.4)** ✅ **Delivered 2026-07-18** (commit pending). 38 / 38 tests passing in `Basket.API.Tests` (9 Phase 1 + 3 atomic-checkout + 11 real-discount + 8 idempotency + 7 new rate-limiter smoke tests). Strict build `-p:TreatWarningsAsErrors=true` clean.
  - New `Basket.API/RateLimiting/CheckoutRateLimiter.cs` — extracted from `Program.cs` so the partition function + OnRejected callback are unit-testable without spinning up the full Basket host. Exposes `PolicyName = "checkout"`, `PermitLimit = 5`, `Window = TimeSpan.FromMinutes(1)` as public constants.
  - Partition function: `RateLimitPartition.GetFixedWindowLimiter(key = "${userId}:${restaurantId}", FixedWindowRateLimiterOptions { PermitLimit = 5, Window = 1min, QueueLimit = 0, AutoReplenishment = true })`. Keying on the (user, restaurant) pair partitions fairly — one user's six attempts against the same restaurant return 429 on the sixth; one user's six attempts spread across six restaurants all succeed.
  - OnRejected callback emits 429 with `Retry-After: <seconds>` header (auto-populated by `AutoReplenishment=true` via `MetadataName.RetryAfter`) and an `application/problem+json` body referencing RFC 6585 §4. ContentType is set AFTER `WriteAsJsonAsync` (which forces `application/json; charset=utf-8`); the override is what surfaces the RFC 7807 envelope.
  - `app.UseRateLimiter()` wired AFTER `app.UseAuthentication()` + `app.UseAuthorization()` so the partition function reads the authenticated principal. The `MapBasketGroup().RequireAuthorization("Default")` chain runs before the rate-limit middleware, so unauthenticated callers don't reach the partition function.
  - Applied via `.RequireRateLimiting("checkout")` on POST /cart/checkout only; the deprecated /baskets/checkout shim does NOT carry the policy (it'll be removed at end of Phase 3).
  - The other three endpoints (GET/PUT/DELETE on the cart) stay unlimited per plan §0.4.8 — they're idempotent reads or trivial local writes.
  - 7 new unit tests in `Basket.API.Tests/Unit/CheckoutRateLimiterTests.cs`: `PolicyName_IsCheckout`, `PermitLimit_IsFivePerMinute_PerPlan_0_4_8`, `PartitionFunc_KeysOnUserIdAndRestaurantId`, `PartitionFunc_DifferentRestaurants_GetDifferentPartitions`, `FixedWindowLimiter_AllowsFive_RejectsSixth` (constructs a real `PartitionedRateLimiter<string>` and exercises the limit; proves the configured 5/minute/partition contract), `OnRejectedAsync_Sets429Status_AndRetryAfterHeader_AndProblemDetailsBody` (uses a real rejected `RateLimitLease` from an exhausted limiter — proves the callback reads `MetadataName.RetryAfter` correctly), `OnRejectedAsync_WhenLeaseExposesNoMetadata_OmitsRetryAfterHeader` (uses a `ConcurrencyLimiter` lease which doesn't expose RetryAfter — proves the belt-and-braces branch where the header is omitted rather than emitted with a bad value).
  - **Doc-update scope (delivered):** `current-architecture.md` §4.3 (Basket Service — new "Rate limiting (Phase 2.4)" paragraph after the Idempotency-Key paragraph; documents policy name, partition key, limit (5/minute), Retry-After header, application/problem+json envelope, QueueLimit=0 rationale, middleware pipeline order, and the three unlimited endpoints).
- [x] **Wrapper-record cleanup + 201-vs-200 PUT + spoofing-footgun fix (sub-deliverable 2.5)** ✅ **Delivered 2026-07-18** (commit pending). 46 / 46 tests passing in `Basket.API.Tests` (9 Phase 1 + 3 atomic-checkout + 11 real-discount + 8 idempotency + 7 rate-limiter + 2 hot-reload + 6 new 2.5 tests). Strict build `-p:TreatWarningsAsErrors=true` clean.
  - **`IBasketRepository.StoreBasketAsync` returns `Task<(Models.Basket Basket, bool IsCreated)>`** — the repository already had the `existingBasket is null` check; the new tuple exposes the signal. `BasketRepository.StoreBasketAsync` and `CachedBasketRepository.StoreBasketAsync` updated; `StoreBasketHandler` reads `IsCreated` from the tuple.
  - **`StoreBasketResult` adds `IsCreated : bool`** — handler returns `new StoreBasketResult(isCreated, stored.UserId, stored.RestaurantId)`. `StoreBasketResponse` mirrors the new shape (`bool IsCreated, Guid UserId, Guid RestaurantId`).
  - **§0.4.3 PUT semantics on `/cart`:** `Results.Created("/api/v1/cart", ...)` when `IsCreated=true` (201 + `Location: /api/v1/cart`); `Results.Ok(...)` when `IsCreated=false` (200). The deprecated `/baskets/{userId}/{restaurantId}` shim returns 200 on every successful PUT (legacy clients expect 200; the 201/200 distinction lives only on the primary route).
  - **§0.4.10 spoofing-footgun fix on `StoreBasketCommandValidator`:** flipped `RuleFor(x => x.Basket.UserId).NotEmpty()` → `RuleFor(x => x.Basket.UserId).Equal(Guid.Empty)` (same for `RestaurantId`). A non-empty body UserId is rejected with **422** by `CustomExceptionHandler`. The endpoint overrides the body's identity fields with the JWT-derived values BEFORE constructing the command so `BasketIdentityGuardBehavior` (Phase 1) sees matching values.
  - **§0.4.10 spoofing-footgun fix on `CheckoutBasketCommandValidator`:** same flip — `RuleFor(x => x.BasketCheckoutDto.UserId).Equal(Guid.Empty)` (and `RestaurantId`). The `POST /cart/checkout` endpoint overrides the body's identity fields with JWT-derived values.
  - **Wrapper records retained for the deprecated shims only:** `StoreBasketRequest(Basket)` and `CheckoutBasketRequest(BasketCheckoutDto)` are still used by the deprecated `/baskets/{userId}/{restaurantId}` PUT and `/baskets/checkout` POST routes respectively. The primary `/cart` PUT and `/cart/checkout` POST routes bind directly to `Models.Basket` and `BasketCheckoutDto` — no wrappers needed. The wrappers are removed at end of Phase 3 when the shims themselves are removed.
  - 6 new unit tests in `Basket.API.Tests/Unit/`:
    - `StoreBasketCommandValidatorTests.cs` (3 tests) — locks the §0.4.10 spoofing-footgun contract: empty body UserId/RestaurantId passes; non-empty UserId or RestaurantId fails with the documented error message; the `Basket` property's `NotNull` rule is preserved.
    - `StoreBasketHandlerTests.cs` (3 tests appended) — `StoreNewCart_HandlerReturnsIsCreatedTrue` (handler returns `IsCreated=true` when repo reports new), `StoreExistingCart_HandlerReturnsIsCreatedFalse` (handler returns `IsCreated=false` when repo reports existing), `EmptyCouponsShortCircuit_PreservesIsCreated` (the empty-cart short-circuit preserves the `IsCreated` signal).
  - **Doc-update scope (delivered):** `current-architecture.md` §4.3 (Basket Service — PUT row in the endpoint matrix now documents the 201 Created + Location / 200 OK contract, the §0.4.10 `Guid.Empty` validator, and the 422 spoofing-footgun reject).

**Drift items captured inline in §6 Phase 2:**

  1. **`IBasketRepository.StoreBasketAsync` signature change** — small interface widening (tuple return type instead of `Models.Basket`). All call sites updated; the change is contained within Basket.
  2. **Wrapper records not deleted** — `StoreBasketRequest` and `CheckoutBasketRequest` survive for the deprecated shims. The "cleanup" in 2.5 is partial — the primary routes bind directly, the wrappers are only used by the shim endpoints which are removed in Phase 3.
  3. **Spoofing-footgun on `CheckoutBasketDto`** — the old validator's `NotEmpty` was the OPPOSITE of the new contract (it encouraged the footgun). The flip to `Equal(Guid.Empty)` is a behavior change for any client sending a non-empty UserId/RestaurantId in the body — those requests now get 422 instead of being silently accepted (and rejected later by the identity guard with 403). The endpoint overrides the body with JWT values BEFORE constructing the command, so legitimate clients are unaffected.
- [x] **Idempotency middleware (sub-deliverable 2.3)** ✅ **Delivered 2026-07-18** (commit pending). 31 / 31 tests passing in `Basket.API.Tests` (9 Phase 1 + 3 atomic-checkout + 11 real-discount + 8 new BasketIdempotencyFilter tests). Strict build `-p:TreatWarningsAsErrors=true` clean.
  - New `Basket.API/Idempotency/` namespace hosts `BasketIdempotencyFilter` (Carter `IEndpointFilter`), `BasketIdempotencyOptions` (strongly-typed config), `IBasketIdempotencyKeyProvider` + `BasketIdempotencyKeyProvider` (HMAC envelope), and `IdempotencyCacheEntry` (cached payload record).
  - IETF `draft-ietf-httpapi-idempotency-key-header` contract: required UUID v4 header (strict regex `^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$`), body-matching replay returns `200 OK + cached body` + `Idempotent-Replayed: true`, body-mismatching replay returns `422 Unprocessable Content` (NOT 409 per the IETF draft), absence/malformed returns `400`.
  - Redis key shape `basket:idem:{userId}:{restaurantId}:{idempotencyKey}` — tenant-scoped so a cross-user replay misses at the Redis level (belt-and-braces with `BasketIdentityGuardBehavior`).
  - Body fingerprint: `HMAC-SHA256(Basket:Idempotency:SecretHex, "${userId}|${restaurantId}|${sha256(body)}")` — HMAC envelope binds the (user, restaurant, body) triple to the server-side secret, preventing cache-poisoning by an attacker with Redis read access but no secret.
  - TTL 24h (configurable via `Basket:Idempotency:Ttl`). Fail-closed on Redis errors: GET failure → `503 Service Unavailable` (handler does NOT run); SET failure is logged but the request still succeeds (next replay will miss).
  - The deprecated `/baskets/checkout` shim does NOT carry idempotency (it'll be removed at end of Phase 3 — adding idempotency to a route on the way out is wasted work).
  - Mirror of `Discount.Grpc.Authorization.IIdempotencyKeyProvider` (Basket-side variant — separate secret in `Basket:Idempotency:SecretHex` because sharing the secret would let a Discount-cache-poisoning bug bleed into Basket's namespace).
  - `Program.cs`: registered a shared `IConnectionMultiplexer` Singleton (constructed up-front) and wired `AddStackExchangeRedisCache(opts => opts.ConnectionMultiplexerFactory = () => sharedMultiplexer)` so the cache layer and the idempotency filter share one connection (IDistributedCache doesn't expose SETNX-with-EX — `IConnectionMultiplexer.GetDatabase().StringSetAsync(key, value, expiry, When.NotExists)` is the atomic operation). `BasketIdempotencyOptions` bound via `AddOptions<>().Bind().ValidateDataAnnotations().ValidateOnStart()`. The filter is registered as Scoped (per-request instance); the underlying `IConnectionMultiplexer` is the shared Singleton.
  - 8 new unit tests in `Basket.API.Tests/Unit/BasketIdempotencyFilterTests.cs`: `MissingIdempotencyKey_Returns400`, `EmptyIdempotencyKey_Returns400`, `MalformedIdempotencyKey_Returns400`, `FirstRequest_RunsHandler_AndCachesResult`, `ReplayWithSameBody_ReturnsCached200_AndShortCircuitsHandler`, `ReplayWithDifferentBody_Returns422`, `CrossUserReuse_DoesNotCollide_BecauseRedisKeyIsTenantScoped`, `RedisGetFailure_Returns503_AndDoesNotRunHandler`. Includes a `FixedHmacKeyProvider` test double that bypasses `IConfiguration` + `IOptions` for fast deterministic tests.
  - **Doc-update scope (delivered):** `current-architecture.md` §4.3 (Basket Service — new "Idempotency-Key (Phase 2.3)" paragraph after the Discount integration block; UUID v4 regex, replay semantics, 422-not-409, Redis key shape, body fingerprint HMAC envelope, fail-closed policy, deprecated-shim exclusion).
- [x] **Payment-method redaction (sub-deliverable 2.1, BuildingBlocks contribution)** ✅ **Delivered 2026-07-18** (commit pending). New `PaymentMethod` enum (see §0.4.10) replacing the `int` field; `BasketCheckoutDto.CardNumber` stays as `string` but the new `BasketCheckoutEvent` payload includes **only** `PaymentMethodSummary { Method, Brand, LastFour }`. The `BasketCheckoutEvent` shape change is a BuildingBlocks contribution (the event lives in `BuildingBlocks.Messaging.Events`); Ordering's consumer must be updated in lockstep — flagged in §7 cross-service notes.
- [x] **Checkout rate-limiter (sub-deliverable 2.4)** ✅ **Delivered 2026-07-18** (commit pending). 38 / 38 tests passing in `Basket.API.Tests` (9 Phase 1 + 3 atomic-checkout + 11 real-discount + 8 idempotency + 7 new rate-limiter smoke tests). Strict build `-p:TreatWarningsAsErrors=true` clean.
  - New `Basket.API/RateLimiting/CheckoutRateLimiter.cs` — extracted from `Program.cs` so the partition function + OnRejected callback are unit-testable without spinning up the full Basket host. Exposes `PolicyName = "checkout"`, `PermitLimit = 5`, `Window = TimeSpan.FromMinutes(1)` as public constants.
  - Partition function: `RateLimitPartition.GetFixedWindowLimiter(key = "${userId}:${restaurantId}", FixedWindowRateLimiterOptions { PermitLimit = 5, Window = 1min, QueueLimit = 0, AutoReplenishment = true })`. Keying on the (user, restaurant) pair partitions fairly — one user's six attempts against the same restaurant return 429 on the sixth; one user's six attempts spread across six restaurants all succeed.
  - OnRejected callback emits 429 with `Retry-After: <seconds>` header (auto-populated by `AutoReplenishment=true` via `MetadataName.RetryAfter`) and an `application/problem+json` body referencing RFC 6585 §4. ContentType is set AFTER `WriteAsJsonAsync` (which forces `application/json; charset=utf-8`); the override is what surfaces the RFC 7807 envelope.
  - `app.UseRateLimiter()` wired AFTER `app.UseAuthentication()` + `app.UseAuthorization()` so the partition function reads the authenticated principal. The `MapBasketGroup().RequireAuthorization("Default")` chain runs before the rate-limit middleware, so unauthenticated callers don't reach the partition function.
  - Applied via `.RequireRateLimiting("checkout")` on POST /cart/checkout only; the deprecated /baskets/checkout shim does NOT carry the policy (it'll be removed at end of Phase 3).
  - The other three endpoints (GET/PUT/DELETE on the cart) stay unlimited per plan §0.4.8 — they're idempotent reads or trivial local writes.
  - 7 new unit tests in `Basket.API.Tests/Unit/CheckoutRateLimiterTests.cs`: `PolicyName_IsCheckout`, `PermitLimit_IsFivePerMinute_PerPlan_0_4_8`, `PartitionFunc_KeysOnUserIdAndRestaurantId`, `PartitionFunc_DifferentRestaurants_GetDifferentPartitions`, `FixedWindowLimiter_AllowsFive_RejectsSixth` (constructs a real `PartitionedRateLimiter<string>` and exercises the limit; proves the configured 5/minute/partition contract), `OnRejectedAsync_Sets429Status_AndRetryAfterHeader_AndProblemDetailsBody` (uses a real rejected `RateLimitLease` from an exhausted limiter — proves the callback reads `MetadataName.RetryAfter` correctly), `OnRejectedAsync_WhenLeaseExposesNoMetadata_OmitsRetryAfterHeader` (uses a `ConcurrencyLimiter` lease which doesn't expose RetryAfter — proves the belt-and-braces branch where the header is omitted rather than emitted with a bad value).
  - **Doc-update scope (delivered):** `current-architecture.md` §4.3 (Basket Service — new "Rate limiting (Phase 2.4)" paragraph after the Idempotency-Key paragraph; documents policy name, partition key, limit (5/minute), Retry-After header, application/problem+json envelope, QueueLimit=0 rationale, middleware pipeline order, and the three unlimited endpoints).
- [x] **Wrapper-record cleanup + 201-vs-200 PUT + spoofing-footgun fix (sub-deliverable 2.5)** ✅ **Delivered 2026-07-18** (commit pending). 46 / 46 tests passing in `Basket.API.Tests` (9 Phase 1 + 3 atomic-checkout + 11 real-discount + 8 idempotency + 7 rate-limiter + 2 hot-reload + 6 new 2.5 tests). Strict build `-p:TreatWarningsAsErrors=true` clean.
  - **`IBasketRepository.StoreBasketAsync` returns `Task<(Models.Basket Basket, bool IsCreated)>`** — the repository already had the `existingBasket is null` check; the new tuple exposes the signal. `BasketRepository.StoreBasketAsync` and `CachedBasketRepository.StoreBasketAsync` updated; `StoreBasketHandler` reads `IsCreated` from the tuple.
  - **`StoreBasketResult` adds `IsCreated : bool`** — handler returns `new StoreBasketResult(isCreated, stored.UserId, stored.RestaurantId)`. `StoreBasketResponse` mirrors the new shape (`bool IsCreated, Guid UserId, Guid RestaurantId`).
  - **§0.4.3 PUT semantics on `/cart`:** `Results.Created("/api/v1/cart", ...)` when `IsCreated=true` (201 + `Location: /api/v1/cart`); `Results.Ok(...)` when `IsCreated=false` (200). The deprecated `/baskets/{userId}/{restaurantId}` shim returns 200 on every successful PUT (legacy clients expect 200; the 201/200 distinction lives only on the primary route).
  - **§0.4.10 spoofing-footgun fix on `StoreBasketCommandValidator`:** flipped `RuleFor(x => x.Basket.UserId).NotEmpty()` → `RuleFor(x => x.Basket.UserId).Equal(Guid.Empty)` (same for `RestaurantId`). A non-empty body UserId is rejected with **422** by `CustomExceptionHandler`. The endpoint overrides the body's identity fields with the JWT-derived values BEFORE constructing the command so `BasketIdentityGuardBehavior` (Phase 1) sees matching values.
  - **§0.4.10 spoofing-footgun fix on `CheckoutBasketCommandValidator`:** same flip — `RuleFor(x => x.BasketCheckoutDto.UserId).Equal(Guid.Empty)` (and `RestaurantId`). The `POST /cart/checkout` endpoint overrides the body's identity fields with JWT-derived values.
  - **Wrapper records retained for the deprecated shims only:** `StoreBasketRequest(Basket)` and `CheckoutBasketRequest(BasketCheckoutDto)` are still used by the deprecated `/baskets/{userId}/{restaurantId}` PUT and `/baskets/checkout` POST routes respectively. The primary `/cart` PUT and `/cart/checkout` POST routes bind directly to `Models.Basket` and `BasketCheckoutDto` — no wrappers needed. The wrappers are removed at end of Phase 3 when the shims themselves are removed.
  - 6 new unit tests in `Basket.API.Tests/Unit/`:
    - `StoreBasketCommandValidatorTests.cs` (3 tests) — locks the §0.4.10 spoofing-footgun contract: empty body UserId/RestaurantId passes; non-empty UserId or RestaurantId fails with the documented error message; the `Basket` property's `NotNull` rule is preserved.
    - `StoreBasketHandlerTests.cs` (3 tests appended) — `StoreNewCart_HandlerReturnsIsCreatedTrue` (handler returns `IsCreated=true` when repo reports new), `StoreExistingCart_HandlerReturnsIsCreatedFalse` (handler returns `IsCreated=false` when repo reports existing), `EmptyCouponsShortCircuit_PreservesIsCreated` (the empty-cart short-circuit preserves the `IsCreated` signal).
  - **Doc-update scope (delivered):** `current-architecture.md` §4.3 (Basket Service — PUT row in the endpoint matrix now documents the 201 Created + Location / 200 OK contract, the §0.4.10 `Guid.Empty` validator, and the 422 spoofing-footgun reject).

**Drift items captured inline in §6 Phase 2:**

  1. **`IBasketRepository.StoreBasketAsync` signature change** — small interface widening (tuple return type instead of `Models.Basket`). All call sites updated; the change is contained within Basket.
  2. **Wrapper records not deleted** — `StoreBasketRequest` and `CheckoutBasketRequest` survive for the deprecated shims. The "cleanup" in 2.5 is partial — the primary routes bind directly, the wrappers are only used by the shim endpoints which are removed in Phase 3.
  3. **Spoofing-footgun on `CheckoutBasketDto`** — the old validator's `NotEmpty` was the OPPOSITE of the new contract (it encouraged the footgun). The flip to `Equal(Guid.Empty)` is a behavior change for any client sending a non-empty UserId/RestaurantId in the body — those requests now get 422 instead of being silently accepted (and rejected later by the identity guard with 403). The endpoint overrides the body with JWT values BEFORE constructing the command, so legitimate clients are unaffected.
- [ ] **Phase 2 tests (the remaining ones).** 
  - `UpsertCartCommandValidatorTests` — full rule coverage (§0.4.10).
  - `UpsertCartCommandHandlerTests.ValidCoupon_DiscountAmountComputed`.
  - `UpsertCartCommandHandlerTests.ExpiredCoupon_Skipped`.
  - `CheckoutCartHandlerTests.PublishFails_BasketNotDeleted` — outbox-rollback proof.
  - `CheckoutCartHandlerTests.ReplayWithSameIdempotencyKey_ReturnsCachedResult`.
  - `CheckoutCartHandlerTests.ReuseWithDifferentPayload_Returns422`.
  - `CheckoutCartHandlerTests.MissingIdempotencyKey_Returns400`.
  - `CheckoutCartHandlerTests.BodyCarriesUserId_RejectedWith422` (§2.10 spoofing footgun).
  - `CheckoutCartHandlerTests.RateLimiter_FifthAttemptInOneMinute_Returns429`.
- [ ] **Phase 2 doc-update scope (remaining):** §4.3 (idempotency envelope, rate limiter), §5.2 (`BasketCheckoutEvent` SchemaVersion + card-redaction note), §11 (idempotency-key config), §12 (OpenTelemetry for outbox).

### Phase 3 — Cache stampede protection + expiry sweep + DELETE 204 + URL cleanup (PERFORMANCE-CRITICAL)

The current `CachedBasketRepository` is a single-flight hole that trashes Postgres on cache miss. Phase 3 hardens it, removes the §0.4.1 deprecation shim, and finalises the HTTP semantics on `DELETE`.

- Single-flight `GetOrCreateAsync` on `CachedBasketRepository.GetCartAsync` via `SemaphoreSlim` registry (singleton), keyed on `cacheKey`, cleared on app shutdown via `IHostApplicationLifetime.ApplicationStopping`.
- New `BasketExpirySweepService : IHostedService` running every 5 minutes that loads all `(UserId, RestaurantId)` pairs whose `ExpiresAt < clock.GetCurrentInstant()` and invokes a private `DeleteExpiredBasket(userId, restaurantId)` path (does **not** publish `BasketCheckoutEvent`; purely cosmetic housekeeping). Lifetime-aligned cache invalidation.
- **HTTP-semantic fixes:**
  - `DELETE /api/v1/cart` returns **204 No Content** (no body); the previous `DeleteBasketResponse { IsSuccess = true }` body is removed per §0.4.3.
  - `GET /api/v1/cart` returns **200 + empty cart** when no cart exists (§0.4.7); `BasketNotFoundException` is no longer thrown for this path.
  - The `[Obsolete]` `/api/v1/baskets/{userId}/{restaurantId}` shim is removed.
- Hardening:
  - `JsonSerializerOptions` field on `CachedBasketRepository` using the same global config (`ConfigureForNodaTime`) — eliminates the Phase 1 latent bug.
  - `SemaphoreSlim` disposal in `BasketCacheLockRegistry.Dispose` (called by an `IHostApplicationLifetime` registration).
- **API contract tests:** the snapshot suite (see §8 / Phase 5) gains `CartsSnapshots.VerifyAllEndpoints` — one snapshot per (verb, URL, status code, body) combination, generated by a `WebApplicationFactory` running against the full stack of Testcontainer fixtures. Snapshots are stored under `Services/Basket/Basket.API.Tests/Snapshots/` and reviewed in PRs.
- Tests:
  - `CachedBasketRepositoryTests.CacheMiss_OnlyOneDbCallUnderContention` (stress test: 100 concurrent gets → 1 DB query).
  - `BasketExpirySweepTests.ExpiredBasket_Deleted`.
  - `BasketExpirySweepTests.LiveBasket_NotTouched`.
  - `BasketCacheLockRegistryTests.Dispose_ReleasesAllSemaphores`.
  - `DeleteCartEndpointTests.EmptyCart_Returns204NoContent`.
  - `DeleteCartEndpointTests.AbsentCart_Returns204NoContent` (idempotent).
  - `GetCartEndpointTests.NoCartYet_Returns200WithEmptyBody`.
  - `BasketEndpointTests.ObsoleteRoute_Returns410Gone` (the removal of the shim returns 410 once `Sunset` header fires, signalling migration).
- **Doc-update scope:** §4.3 (single-flight + expiry sweep + DELETE 204), §11 (sweep interval config), §12 (Redis cache hit/miss metric), `current-architecture.md` header note that the deprecated route is gone.

### Phase 4 — gRPC + bus resilience + observability + Swagger (OPERATIONS)

- Wrap `DiscountProtoServiceClient` registration with `AddStandardResilienceHandler(...)` (Polly v8 — `Microsoft.Extensions.Http.Resilience`) — retry (3x exponential), circuit breaker (5 failures in 30s open), timeout (3s).
- Add `services.AddOpenTelemetry().WithTracing(b => b.AddAspNetCoreInstrumentation().AddHttpClientInstrumentation().AddSource("Marten").AddSource("MassTransit").AddNpgsql()).WithMetrics(m => m.AddAspNetCoreInstrumentation().AddRuntimeInstrumentation())`.
- Wire `OTEL_EXPORTER_OTLP_ENDPOINT` via `IOptions<OtelOptions>` with `ValidateOnStart()`. `X-Correlation-Id` is read at request entry (Phase 1 wiring) and added to the OTel `Activity` bag — OTel's tracing now carries the same id as `LoggingBehavior`'s `BeginScope`.
- Split `Program.cs:117` health-check route into `/live` (process alive only — Postgres + Redis optional) and `/ready` (Postgres + Redis + RabbitMQ + outbox dispatcher). Both use `UIResponseWriter.WriteHealthCheckUIResponse`.
- New `Basket : ITenantEntity` + `HealthCheck` confirms the tenant filter can resolve a basket id from the connection's `restaurantId` claim.
- **OpenAPI generation:** `services.AddEndpointsApiExplorer().AddSwaggerGen(o => o.SwaggerDoc("v1", new OpenApiInfo { Title = "Basket API", Version = "v1" }))`; `MapBasketGroup` adds `WithOpenApi()` which feeds `AddSwaggerGen`. `app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "Basket API"))` enabled only when `env.IsDevelopment()`. The generated `swagger.json` is committed under `docs/api/basket-api-v1.json` on every phase commit that changes an endpoint — the source of truth for external consumers and a regression-detector for breaking changes.
- **`ETag` + `Last-Modified`** on `GET /api/v1/cart`: `Etag = sha256(json(basket))` (cheap); `Last-Modified = basket.LastModifiedAt`. `304 Not Modified` on `If-None-Match` / `If-Modified-Since` hit.
- **Admin endpoints (`/api/v1/admin/carts/...`):**
  - `GET /api/v1/admin/carts` — paged list, `[AsParameters] ListCartsQuery` (§0.4.13), bearer-token `RestaurantSupportAgent` role with `orders:admin` permission; returns `PaginatedResult<CartSummaryDto>` from `BuildingBlocks.Pagination`.
  - `PUT /api/v1/admin/carts/{userId}` — same body shape as `UpsertCartCommand`; bypasses the user-id-vs-JWT-subject check via the `IBasketIdentityGuard` admin bypass; logs `RestaurantId` + `Subject` + `TargetUserId` to the audit log (added in Phase 4); idempotent (200 OK on update, 201 on create).
  - `DELETE /api/v1/admin/carts/{userId}` — bypass + audit; returns `204 No Content`.
  - All three endpoints require the `orders:admin` permission (Identity seed; §7 hand-off).
  - Group registered with `MapBasketGroup().MapGroup("/admin").RequireAuthorization("Default").WithTags("AdminCarts")` (still under `/api/v1`).
- Tests (admin endpoints):
  - `ListCartsQueryValidatorTests.EmptyFilters_Rejected`.
  - `AdminCartEndpointsTests.WithoutOrdersAdminPermission_Returns403`.
  - `AdminCartEndpointsTests.WithOrdersAdminPermission_Returns200AndPagedResult`.
  - `AdminCartEndpointsTests.AuditLogRowWrittenOnAdminMutation`.
  - `AdminCartEndpointsTests.BulkDeleteHonoursRateLimiter` (the admin "drop expired baskets" path goes through its own policy).
- Tests (resilience/observability):
  - `DiscountGrpcResiliencePipelineTests.ResilienceHandlerRegistered` (snapshot the `IHttpClientBuilder` dec config at runtime).
  - `HealthCheckEndpointsTests.Ready_DegradesWhenRabbitMqDown` (Testcontainers RabbitMQ stopped mid-test).
  - `HealthCheckEndpointsTests.Live_OnlyProcessHeartbeat` (ready check stays 503, live stays 200).
  - `BasketTelemetryTests.CheckoutCart_EmitsCheckoutSpan` (in-memory exporter, assert span shape).
  - `OpenApiGenerationTests.AllEndpointsDocumented` (parses the swagger.json, asserts every `MapBasketGroup` endpoint is present, asserts no `MapBasketGroup` endpoint is undocumented; the test fails on the next phase that adds an endpoint without `WithOpenApi()`).
- **Doc-update scope:** §2 Tech Stack (Polly, OpenTelemetry row, Swagger row), §4.3 (resilience + health split + OpenAPI + admin endpoints), §12 (telemetry exporters + health split).

### Phase 5 — Tests project bootstrap (the test-infrastructure phase)

**Status:** ⏳ Partially delivered. **The `Basket.API.Tests` project shell was scaffolded in Phase 1** (drift item 4 above) with 9 unit tests (`BasketIdentityGuardBehaviorTests` × 5, `GetBasketHandlerTests` × 2, `LoggingBehaviorRedactionTests` × 2) and a `RecordingLogger<T>` test double. Phase 5 expands that shell with the integration / snapshot / contract-test infrastructure the plan reserved for this phase.

- **Existing project shell** (Phase 1 deliverable): `Services/Basket/Basket.API.Tests/Basket.API.Tests.csproj` (xUnit, FluentAssertions, NSubstitute, `Microsoft.AspNetCore.App` framework reference, no Testcontainers yet).
- **To add in Phase 5:**
  - `Testcontainers.PostgreSql`, `Testcontainers.Redis`, `Testcontainers.RabbitMq` packages.
  - **Verify** for snapshot tests.
  - `Microsoft.AspNetCore.Mvc.Testing` for `WebApplicationFactory`.
  - `appsettings.Test.json` with empty connection strings (Testcontainers fills them).
  - Shared fixtures:
    - `BasketPostgresFixture` (Testcontainers Postgres + Marten `ApplyAllDatabaseChangesOnStartup`).
    - `BasketRedisFixture` (Testcontainers Redis + `ConnectionMultiplexer`).
    - `BasketRabbitMqFixture` (Testcontainers RabbitMQ + MassTransit in-memory harness reconfigured to bus endpoint at the container's port).
    - `BasketWebApplicationFactory` (`WebApplicationFactory<Program>`) wiring the three fixtures and using `JwtTestAuthenticationHandler` for route authentication (the handler reads `Authorization: Bearer test-token`, parses the JWT subject + `restaurantId` claim, and populates `HttpContext.User`).
    - `TestClock : IClock` returning the `FakeTimeProvider`'s current instant, used for `ExpiresAt` testing.
    - **`BasketSnapshots.VerifyAllEndpoints`** (introduced Phase 3, executed here) — one snapshot per (verb, URL, status code, body) combination, generated by `BasketWebApplicationFactory`. Snapshots stored under `Services/Basket/Basket.API.Tests/Snapshots/` and reviewed in PRs; CI fails on snapshot drift unless the change is acknowledged via `Verify.VerifyTests` `.received` files.
  - **API contract tests per endpoint** (complementing snapshots):
    - `GetCartEndpointTests` — 200 (with cart), 200 (without cart), 401, 403.
    - `UpsertCartEndpointTests` — 201 (new), 200 (existing), 400, 401, 403, 409 (optimistic concurrency), 422 (`MenuItemId` not in catalog).
    - `DeleteCartEndpointTests` — 204 (existing), 204 (absent), 401, 403.
    - `CheckoutCartEndpointTests` — 200, 202 (replay-in-flight), 400, 401, 403, 409 (empty), 422 (idempotency-key reuse), 429 (rate limit).
- CI gate wiring (out of repo scope but documented): the plan's checklist requires `dotnet test Services/Basket/Basket.API.Tests` to be a PR-blocker. Phase 5's PR includes a `.github/workflows/basket-tests.yml` sketch for the implementer.
- **Doc-update scope:** §11 Local Development (test bootstrap snippet), §12 Observability (test-only trace exporter), `docs/api/basket-api-v1.json` first commit (the snapshot of the current API surface generated by OpenAPI tooling once Phase 4 lands; until then the contract tests document it).

---

## 7. Cross-service notes (carried from sibling plans)

- **Multi-tenancy adoption** — Basket joins Discount (its §0.1 design decision #11) as the second adopter of `BuildingBlocks.Multitenancy.ITenantEntity`. Catalog and Ordering are downstream — neither needs changes to consume Basket's events, but if either starts *querying* Basket data directly (today it doesn't), the same query-filter pattern applies. Recorded here so a future plan doesn't re-discover the gap.
- **Discount integration** — the v1 single-call `GetDiscountAsync` per coupon is correct but inefficient. Discount §plans the `EvaluateDiscounts` aggregated RPC for v2 (a logical next step; out of this plan). Phase 2 keeps the per-coupon loop and adds a `BatchGetDiscountAsync` polyfill that calls `GetDiscountAsync` in parallel (`Parallel.ForEachAsync` with `MaxDegreeOfParallelism = 4`) to compensate — sufficient until the aggregated RPC ships.
- **`BasketCheckoutEvent` schema** — Version **1**. MassTransit's default ignores unknown fields, so future additions don't break Ordering today. The v1 contract is published in this plan; any v2 changes (e.g. dropping `AppliedDiscounts` strings in favour of a structured `DiscountSnapshot`) flow through the standard `SchemaVersion` bump per Catalog §6.5.
- **Card data** — v1 redacts on the wire (`BasketCheckoutEvent` drops `CardNumber`/`CVV`). A v2 hand-off to Identity for PCI-tokenized payment methods is tracked as a future Identity plan; until then, Basket keeps the raw fields for the v1 integration window but logs them only via the redacted behavior from Phase 1.
- **Outbox parity** — Basket's `CheckoutBasketOutboxDispatcher` mirrors Discount's `OutboxDispatcher<TContext>` (verified during the recent `BuildingBlocks.Messaging/Outbox/OutboxOptions.cs` modifications — the project already has the BuildingBlocks primitives in place). If Discount's outbox evolves, Basket's follows in the next synchronized phase.
- **JWT cross-check pattern** — `ICurrentRestaurantProvider` (`BuildingBlocks/Multitenancy/ClaimsRestaurantProvider.cs`) is the single source of truth. Basket uses it at the repository layer (per-request) plus the explicit claim-name match in `IBasketIdentityGuard` (per-route). Both defenses are required: query-filter correctness + per-call caller-id correctness.
- **`orders:admin` permission seed — Identity hand-off** — Phase 4 introduces `PUT/DELETE /api/v1/admin/carts/{userId}` and `GET /api/v1/admin/carts` for cross-account support tooling. The bypass in `IBasketIdentityGuard` requires the caller to carry the **`orders:admin`** permission string. **Identity.API must seed this permission** (alongside `coupon:read/create/edit/delete/redeem`, `reward-code:*`, `discount-rule:*` already added by Discount §0.1) and assign it to the `RestaurantSupportAgent` and `RestaurantAdmin` roles. This is tracked as a dependency note — implementer adds a single bullet to the Identity.API plan and a migration row in Identity's `__EFMigrationsHistory` table:
  ```csharp
  // Identity.API/Permissions/2026..._SeedBasketAdminPermissions.cs
  migrationBuilder.InsertData(
      table: "permissions",
      columns: new[] { "name", "description" },
      values: new object[] { "orders:admin", "Cross-account basket administration (CS / support tooling)." });
  ```
  Without this seed the Phase 4 endpoints return 403 for every caller. Phase 4 is gated on the seed landing first; tracked as a `blockedBy` edge in the work tracker.

---

## 8. Milestone checklist

- [x] **Phase 1** ✅ **Delivered 2026-07-17** — BuildingBlocks: `ForbiddenException` + `ValidationBehavior` relaxation + `[PciSensitive]` marker. Basket: `Basket : ITenantEntity` + Marten `MultiTenanted()` + `ICurrentRestaurantProvider`-driven repository filter; `BasketIdentityGuardBehavior` pipeline behavior; `MapBasketGroup` extension with `RequireAuthorization("Default")` + `WithTags("Baskets")` (deferred `WithOpenApi()` to Phase 4 — see §0.4.6 drift); URL rename to `/api/v1/cart` (old route kept as `[DEPRECATED]` shim); `LoggingBehavior` payload redaction for `[PciSensitive]` commands; `AddProblemDetails()`; 15 PR-blocker tests across `BuildingBlocks.Tests` (6) + `Basket.API.Tests` (9). Drift items 1–6 captured inline in §6 Phase 1.
- [ ] **Phase 2** — 🚧 **Atomic-checkout + real-discount integration + idempotency-middleware + checkout-rate-limiter + wrapper-cleanup + spoofing-footgun-fix sub-deliverables delivered 2026-07-18** (commit pending; 46 tests passing in `Basket.API.Tests`, strict build `-p:TreatWarningsAsErrors=true` clean). Atomic-checkout: new `CheckoutBasketOutboxMessage` Marten document (`MultiTenanted()` + `[DuplicateField]` on `OccurredOn`/`DispatchedAt`); new `CheckoutBasketOutboxDispatcher : BackgroundService` (Marten-flavored — does NOT extend `OutboxDispatcher<TContext>`, drift item 1; linear, no `BrokerHealthState` yet, drift item 2; Marten LINQ + optimistic concurrency, not raw `FOR UPDATE SKIP LOCKED`, drift item 3); `CheckoutBasketCommandHandler` rewritten to atomic publish-and-delete (one `SaveChangesAsync` covers outbox stage + basket delete + NodaTime-correct cache invalidation via new `IBasketRepository.InvalidateCacheAsync`); 3 new unit tests. Real-discount integration (sub-deliverable 2.2): new `Basket.API.Discount.IDiscountLookup` abstraction + `GrpcDiscountLookup` (raw gRPC client wrapped so the discount loop is unit-testable — gRPC's `AsyncUnaryCall<T>` doesn't mock cleanly); `Basket.DiscountAmount` (clamped to `Subtotal`) + `Basket.AppliedCoupons: List<CouponSnapshot>` + `Basket.Total` derived; `StoreBasketHandler` rewritten with `Parallel.ForEachAsync(MaxDegreeOfParallelism = 4)` polyfill and fail-closed gRPC policy; `CachedBasketRepository` NodaTime round-trip fix (Phase 1 drift item); 11 new unit tests. Idempotency middleware (sub-deliverable 2.3): new `Basket.API/Idempotency/` namespace — `BasketIdempotencyFilter` (Carter `IEndpointFilter`), `BasketIdempotencyOptions` (strongly-typed config), `IBasketIdempotencyKeyProvider` + `BasketIdempotencyKeyProvider` (HMAC envelope), `IdempotencyCacheEntry` (cached payload); IETF `draft-ietf-httpapi-idempotency-key-header` contract — required UUID v4 header, body-matching replay returns 200 + cached body, body-mismatching replay returns 422 (NOT 409); Redis key `basket:idem:{userId}:{restaurantId}:{idempotencyKey}` (tenant-scoped); body fingerprint `HMAC-SHA256(Basket:Idempotency:SecretHex, "${userId}|${restaurantId}|${sha256(body)}")`; TTL 24h; fail-closed on Redis GET (503 if Redis down); 8 new unit tests; `IConnectionMultiplexer` Singleton shared with the cache layer via `RedisCacheOptions.ConnectionMultiplexerFactory`. Checkout rate-limiter (sub-deliverable 2.4): new `Basket.API/RateLimiting/CheckoutRateLimiter.cs` extracted from `Program.cs` (partition function + OnRejected callback); `FixedWindowRateLimiter` keyed on `(userId, restaurantId)`, 5 requests/minute, `QueueLimit=0`, `AutoReplenishment=true`; `.RequireRateLimiting("checkout")` on POST /cart/checkout only; 429 + Retry-After + application/problem+json; `app.UseRateLimiter()` AFTER auth so the partition reads claims; 7 new unit tests (PartitionFunc shape + real limiter exhaustion + OnRejected with/without RetryAfter metadata). Doc updates: `current-architecture.md` §4.3 + §5.2 + §6 + §9. Still ⏳: (Phase 2 is now feature-complete on the Basket side; only cross-service work remains in Phase 5.)
- [ ] **Phase 3** — `CachedBasketRepository` single-flight guard; `BasketExpirySweepService : IHostedService`; shared `JsonSerializerOptions` honoring NodaTime; `DELETE /api/v1/cart → 204 No Content`; empty-cart → `200 + empty body`; deprecated route removed; API snapshot tests (`Verify.All`); PR-blocker tests.
- [ ] **Phase 4** — gRPC resilience pipeline (Polly v8); OpenTelemetry tracing + metrics; `/live` + `/ready` health-check split; `ETag` + `Last-Modified` on `GET`; Swagger + committed `swagger.json`; OTel `Activity` carries `X-Correlation-Id`; PR-blocker tests.
- [ ] **Phase 5** — `Basket.API.Tests` project expansion (xUnit + FluentAssertions + NSubstitute + Testcontainers + Verify; project shell scaffolded in Phase 1 with 9 unit tests); `BasketWebApplicationFactory` with `JwtTestAuthenticationHandler`; per-endpoint contract tests for every (verb, status) combo; API snapshot suite; CI workflow sketch; smoke test exporting a checkout trace.
- [ ] **Docs** — `current-architecture.md` §2, §4.3, §5.1, §5.2, §6, §9, §11, §12 touched for each phase; `docs/architecture/architecture.md` §3 cross-references the Basket plan from the architecture page; `docs/api/basket-api-v1.json` committed from Phase 4 onwards; Plan and `BASKET_SERVICE_PLAN.md` §0.4 list of deviations stays accurate. Phase 1 delivered §3 + §4.3 + §9 + §10 doc updates; §5.1 / §5.2 / §6 / §11 / §12 still pending their respective phases.

---

## 9. References

- `DISCOUNT_SERVICE_PLAN.md` — §0 design decisions (multi-tenancy adoption, outbox parity, idempotency-key envelope, HMAC pattern).
- `CATALOG_SERVICE_PLAN.md` — §0 conventions (skill mandate, doc-update, code-quality guard rails, API design principles); §6.5 event versioning.
- `NOTIFICATION_SERVICE_PLAN.md` — sibling-plan structure reference (the greenfield analog; this plan is evolution).
- `BuildingBlocks/Multitenancy/ITenantEntity.cs` — already declared; just needs adopters.
- `BuildingBlocks/Multitenancy/ClaimsRestaurantProvider.cs` — single source of truth for tenant resolution.
- `BuildingBlocks/Authorization/JwtClaimExtensions.cs` — `User.GetUserId()` + `User.GetRestaurantId()`; the Phase 1 `IBasketIdentityGuard` is a thin wrapper.
- `BuildingBlocks/Behaviors/LoggingBehavior.cs:9` — current `ICommand<>` constraint; Phase 1 relaxes to `IRequest<>` so query validators run.
- `BuildingBlocks/Behaviors/LoggingBehavior.cs:16` — current request-data logging line; Phase 1 fixes the redaction gap.
- `BuildingBlocks/Exceptions/Handler/CustomExceptionHandler.cs:17-48` — exception → status code mapping; Phase 1 adds the `ForbiddenException` arm.
- `BuildingBlocks.Messaging/Outbox/OutboxOptions.cs` — already in place; Phase 2 connects it.
- `Services/Discount/Discount.Grpc/Protos/discount.proto` — current `GetDiscount` RPC consumed in Phase 2.
- `docs/architecture/architecture.md` §3 — contract for the Basket service; the gaps in this plan are the §3 violations.
- `docs/architecture/current-architecture.md` §4.3 — current snapshot; updated at every phase.
- `db_relational_model.{mermaid,md}` — Basket does not appear (document store, not relational); the [[db-model-drift-reports]] flowing the Mermaid ↔ code review convention ([[mermaid-code-review-convention]]) need to call this out as an explicit exception.
- **External standards:**
  - IETF `draft-ietf-httpapi-idempotency-key-header` — informs the §0.4.4 idempotency contract.
  - RFC 7231 §4.2 (HTTP semantics), §6.3 (PUT), §6.3.5 (DELETE returns 204) — codified in the §0.4.3 status code matrix.
  - RFC 7807 (Problem Details for HTTP APIs) — `application/problem+json` envelope, integrated via `AddProblemDetails()` in Phase 1.
  - RFC 9457 (Problem Details, 2023 successor to RFC 7807) — long-form plan already targets RFC 7807; future migration to 9457 is a v2 follow-up.

---

**Document Version:** 1.1 (Phase 2 fully delivered 2026-07-18 — atomic-checkout + real-discount-integration + idempotency-middleware + checkout-rate-limiter + wrapper-cleanup + spoofing-footgun-fix + payment-method-redaction sub-deliverables all shipped. BuildingBlocks + Basket + Ordering all build clean with `-p:TreatWarningsAsErrors=true`. 72 / 72 Basket tests passing; 147 / 147 Ordering.Domain; 41 / 41 Ordering.Application. The Basket API surface is now feature-complete. The Plan's remaining work is Phase 3 (cache stampede protection + expiry sweep + DELETE 204 + URL cleanup), Phase 4 (gRPC resilience + observability + Swagger), and Phase 5 (test project expansion + Testcontainers + Verify) — all operational/quality work, no more API surface changes.)
**Last Updated:** 2026-07-18
**Maintained By:** Basket working group (TBD)
**Status:** ✅ Phase 1 (2026-07-17) + ALL Phase 2 sub-deliverables delivered 2026-07-18 (atomic-checkout, real-discount-integration, idempotency-middleware, checkout-rate-limiter, wrapper-cleanup + spoofing-footgun-fix, payment-method-redaction). ⏳ Phase 3 (cache stampede protection + expiry sweep + DELETE 204 + URL cleanup). ⏳ Phase 4 (gRPC resilience + observability + Swagger). ⏳ Phase 5 (test project expansion + Testcontainers).
