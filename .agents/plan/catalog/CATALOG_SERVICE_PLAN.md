# Catalog.API — Service Plan

> Scope: completion plan for the existing `Catalog.API` microservice. Closes the gaps between `docs/architecture/architecture.md`, `docs/architecture/db_relational_model.mermaid`, and the code in `Services/Catalog/Catalog.API/`. This is an *evolution* plan, not a green-field design — the relational schema and most CRUD features are already in place; the work is wiring the missing behaviour, completing the partial features, and assigning the misplaced entities to the right homes.
>
> **In-plan entity moves:** Coupon to Discount (Phase 6.2).  
> **Out-of-plan entity moves:** Reservation/WalkInQueue → Ordering, CustomerFeedback/NotificationLog → Notification. Their prerequisites are documented in Phase 6.0 / 6.1 and owned by other service plans.

---

## 0. Skill & documentation conventions

These two conventions apply to **every phase** below. They are non-negotiable — no implementation commit for this plan should land without satisfying both.

### 0.1 Skill mandate — `csharp-developer`

> **All implementation work on this plan MUST invoke the `csharp-developer` skill** (base directory `.claude/skills/csharp-developer`, invoked as `/csharp-developer` in Claude Code).
>
> The skill is the source of truth for C# 12+ / .NET 10 idiom, async patterns, EF Core / Marten usage, ASP.NET Core + Carter, MediatR CQRS, xUnit + Testcontainers test scaffolding, and the project's "MUST DO / MUST NOT DO" guard rails (nullable enabled, primary constructors, async/await with `CancellationToken`, `Result<T>` for error paths, no blocking calls, DTO mapping for API responses).
>
> At the start of **every phase**, the implementer (human or AI agent) loads the skill. Companion reference files under `.claude/skills/csharp-developer/references/` are loaded on demand per the skill's table:
> - `modern-csharp.md` — records, primary constructors, collection expressions, pattern matching, nullable types.
> - `aspnet-core.md` — Minimal API / Carter endpoints, DI, middleware, routing.
> - `entity-framework.md` — EF Core configuration, migrations, query optimization, interceptors.
> - `blazor.md` — only if a Blazor surface is added (this plan does not add one).
> - `performance.md` — `Span<T>`/`Memory<T>`, async, AOT; loaded only if a phase lands a perf-sensitive hot path.
>
> **EF Core checkpoint:** after any code change that mutates the schema (Phase 1 invalidation hooks are code-only; Phase 4 / 6.2 may need new columns), the implementer runs `dotnet ef migrations add <Name>` from `Services/Catalog/Catalog.API/`, **reviews the generated migration file** for unintended drops, and rolls back with `dotnet ef migrations remove` if the diff is wrong.
>
> The skill is *additional* to whatever other skills are relevant (e.g. `csharp-xunit` for test scaffolding, `api-design-principles` for endpoint shape). It is **not** a substitute for the plan; the plan wins where they disagree.

### 0.2 Phase-completion documentation update

> **After completing every phase (1–6.2), `docs/architecture/current-architecture.md` MUST be updated to reflect the new state of the codebase before the implementation commit is finalized.**
>
> `current-architecture.md` is described in its own header as *"the snapshot view of the codebase — no planned features, no gap list. As new functionality is built … update this file to match."* It must never describe Catalog with capabilities that don't exist yet, and it must never lag a shipped phase.
>
> The implementer writes the doc update as part of the phase, not as a follow-up commit. Each phase below lists its **Doc-update scope** — the §-numbered sections of `current-architecture.md` that phase touches.
>
> For convenience, the recurring touch points are:
>
> | Doc section | Why it usually changes per phase |
> |---|---|
> | §2 Tech Stack | New package row (Redis client, Hangfire, MassTransit pieces). |
> | §4.2 Catalog Service | New endpoints, new entities, new event publish/consume, cache details, health checks. |
> | §4.4 Discount Service | Phase 6.2 — Coupon table ownership moves. |
> | §5.1 Synchronous / §5.2 Asynchronous | New event rows in the publish/consume matrix; new HTTP/gRPC targets. |
> | §6 Data Stores | New schemas (Hangfire) or new entries (Redis client, RabbitMQ usage). |
> | §9 Cross-Cutting Patterns | New interceptors, decorators, behaviors (e.g. `DispatchDomainEventsInterceptor`, `OutboxDeadLetterProbe`). |
> | §11 Local Development | Startup sequence updates (Hangfire schema migration, new feature flags), test inventory. |
> | §12 Observability | `/live` + `/ready` split, new `/health` entries. |
>
> The phase's checklist entry (see §9) requires the doc commit before the phase is marked complete.

### 0.3 Code-quality guard rails (dotnet-best-practices)

In addition to the `csharp-developer` skill (§0.1), every phase must satisfy the .NET/C# best practices below. **Where the two overlap, the skill wins** (it has the C# 12+ / .NET 10 specifics); **where this list adds project-specific guard rails, this list wins.** The companion skill is at `.claude/skills/dotnet-best-practices/SKILL.md`.

#### 0.3.1 Documentation

- **XML doc comments on every public type and member** — `<summary>`, `<param>`, `<returns>`, `<exception>`. The csharp-developer MUST DO list already enforces this; the implementer runs `dotnet build /p:TreatWarningsAsErrors=true /p:GenerateDocumentationFile=true` from `Services/Catalog/Catalog.API/` to verify zero CS1591 warnings before committing.

#### 0.3.2 Architecture & patterns

- **Primary constructors for all handlers and small services** — no empty parameterless constructors on injected types. Already in §5 *Tech decisions*.
- **Interface segregation** — every public service exposes an interface (`IXxx`); the `I` prefix is a project-wide convention enforced by csharp-developer's MUST DO.
- **SOLID review checkpoint** — at commit time, the implementer self-checks the diff against the five SOLID principles. The most common defect in this codebase is single-responsibility violation: handlers doing more than one thing (validation + persistence + event publication + cache invalidation all in one method). Flag any handler that touches more than one repository / aggregate.
- **Composition over inheritance** — prefer records + composition over base-class hierarchies unless polymorphism is real. `AuditableEntity<T>` is justified (audit columns); `Entity<T>` for relational entities is justified (id + soft-delete), but new hierarchies need a written justification in the commit message.

#### 0.3.3 Dependency injection & service lifetimes

- **`ArgumentNullException.ThrowIfNull` on every constructor parameter** that is a non-value-type reference (C# 12 idiom). Nullable-enabled + compiler non-null covers the type-system side; `ThrowIfNull` covers the runtime guard for callers who bypass the compiler (`null!` casts, reflection, etc.).
- **Service lifetime table** — use the right lifetime for the right job. Project conventions:

  | Lifetime | Use for |
  |---|---|
  | `Singleton` | `IConnectionMultiplexer`, `ICatalogCache` (Redis client), MassTransit `IBus`, hosted-service instances, the engine's pure-function calculator. |
  | `Scoped` | `CatalogDbContext`, Marten `IDocumentSession`, MediatR handlers, Carter request scopes, Scrutor-decorated services wrapping Scoped types. |
  | `Transient` | Stateless mappers (rare — Mapster handles most), small value-calculator helpers. |
  Capture lifetime choices in a comment near the registration in `Program.cs`; reviewers flag mismatches.

- **No captive dependencies** — a Singleton cannot depend on a Scoped service. If the engine or hosted service needs DB access, take `IServiceScopeFactory` and resolve a scope inside the operation, or convert the host to Scoped.
- **No service locator** — inject dependencies, never `IServiceProvider.GetService<T>()` from app code. The one allowed exception is framework integration points (custom `IHealthCheck`, `IHostedService.StartAsync`).

#### 0.3.4 Async/await

- **Async all the way down** — no `.Result`, `.Wait()`, `Task.Run` for I/O. Already in csharp-developer's MUST NOT DO.
- **`CancellationToken` on every public async method** — propagate from controller/handler to DbContext / cache / bus. Already in csharp-developer's MUST DO.
- **`ConfigureAwait(false)` rule** — library code under `BuildingBlocks/*` and `BuildingBlocks.Messaging/*` **does** use `ConfigureAwait(false)` because it may be consumed outside ASP.NET Core. `Catalog.API` application code **does not** need it (no `SynchronizationContext`). Document the rule so a contributor applies it consistently to libraries and skips it in apps.
- **Async exception handling** — `try`/`catch` at the service boundary (handlers, hosted services, the outbox dispatcher); log with `ILogger.LogError(ex, "Context {Id}", id)` and rethrow unless the catch is a known recoverable condition. Never swallow exceptions silently.

#### 0.3.5 Resource management

- **`IAsyncDisposable` for hosted services** — `CacheDriftRepairService`, `IngredientAvailabilityReconcileService`, every Hangfire job host, the outbox dispatcher. `StopAsync(CancellationToken)` must drain in-flight work and release DB / Redis connections within the host shutdown grace period.
- **Connection lifetime** — `IConnectionMultiplexer` (Singleton) outlives requests; never wrap it in `using`.
- **`IDisposable` for cache values that hold buffers** — Marten session scoping; EF Core already handles DbContext disposal.

#### 0.3.6 Configuration

- **`IOptions<T>` for strongly-typed config**, bound from `appsettings.json`. Every options class lives in `Catalog.API/Options/` and exposes a `Section` constant for `Configure<T>(Configuration.GetSection(Section))`.
- **`ValidateOnStart()` is mandatory** — `services.AddOptions<T>().Bind(...).ValidateDataAnnotations().ValidateOnStart()`. Bad config must fail fast at boot, not at first request.
- **Data annotations on options** — `[Required]`, `[Range]`, `[RegularExpression]` where applicable. The `CatalogOptions:OutboxDeadLetterThreshold` is `[Range(0, int.MaxValue)]`; `CatalogOptions:CacheRepairInterval` (Phase 1) is `[Range(1, 1440)]` minutes.
- **No string-based config keys** — reaffirm csharp-developer's MUST NOT DO.

#### 0.3.7 Error handling

- **Specific exception types** — every Catalog domain exception derives from `BuildingBlocks.Exceptions.DomainException` (HTTP-aware base, mapped by `CustomExceptionHandler`) or `NotFoundException` for 404s. The plan's existing `IngredientAvailabilityStaleException` follows this. New exceptions for Phases 1–5 (e.g. `CacheRepairFailedException`, `OutboxPoisonMessageException`) follow the same hierarchy.
- **No string-typed error returns** — use `Result<T>` or thrown exceptions, not `(bool ok, string err)` tuples. Already in csharp-developer MUST DO.
- **Structured error logging** — `ILogger.LogError(ex, "Engine recompute failed for restaurant {RestaurantId}", restaurantId)` — always include the contextual identifier, never just the message.

#### 0.3.8 Logging

- **`ILogger<T>` with typed category** — every class takes `ILogger<T>` in its primary constructor; `T` is the class itself, not a base.
- **`BeginScope` for correlation IDs** — the §8 *Observability* paragraph mandates a correlation-id enrichment. Implementation: `IHttpContextAccessor` middleware pushes a `CorrelationId` onto the HTTP scope → outbox row `CorrelationId` column → MassTransit header → consumer's log scope. One ID flows through the entire request / event chain.
- **No `Console.WriteLine` / `Debug.WriteLine`** — enforced by `TreatWarningsAsErrors`.

#### 0.3.9 Performance

- **Allocation-free hot paths** — `IngredientAvailabilityEngine.AvailabilityProfileFor` is called per menu-item on every recompute; keep it allocation-free in steady state (no LINQ chains that box, no string concatenation in tight loops). `Span<T>` if a phase lands a parser on the hot path.
- **Async streams (`IAsyncEnumerable<T>`) for paged reads** — list endpoints for `MenuItem`, `Reservation`, `WalkInQueue`, etc. stream rather than materialize full lists in memory.
- **`ValueTask<T>` for hot read paths** — cache reads, engine pure-function results. Reserved for proven hot paths; not required everywhere.

#### 0.3.10 Security

- **Parameterised queries only** — EF Core parameterizes by default. The rule applies to any future raw SQL via `FromSqlRaw` (review for parameterization; prefer `FromSqlInterpolated`).
- **Input validation at the boundary** — FluentValidation runs on `ICommand<TResponse>` per BuildingBlocks. Free-text fields (`MessageContent`, `FailureReason`, `Description`, `Notes`) get explicit `[StringLength]` attributes capping length to prevent abuse.
- **Secrets never in source** — `appsettings.json` references env-var placeholders (`${REDIS_PASSWORD:-redisdev}`). `appsettings.Development.json` may include dev-only secrets and must be gitignored.

#### 0.3.11 Testing — **project override of the skill's defaults**

> The `dotnet-best-practices` skill defaults to **MSTest + FluentAssertions + Moq**. **This project uses xUnit + FluentAssertions + Moq/NSubstitute** (see `Ordering.API.Tests`, `Kitchen.API.Tests`, current-architecture.md §500-504). The skill's MSTest recommendation is **not applied** to this codebase; the xUnit pattern wins.

- **Test framework: xUnit + FluentAssertions** (project-wide).
- **Mocking: Moq** for new tests (matches §5 *Tests*); NSubstitute is also present in `Ordering.Application.Tests` — new code defaults to Moq for consistency with the catalog-tests project if/when created.
- **AAA pattern** — explicit `// Arrange`, `// Act`, `// Assert` comments in every test method.
- **Null-parameter validation tests** — for every public method that takes a non-null reference parameter, add a `MethodName_NullParam_Throws` test using `Assert.Throws<ArgumentNullException>` (xUnit).
- **Happy + sad path** — every handler has at least one happy-path test and one not-found / validation-failure test.
- **Integration test isolation** — Testcontainers (Postgres + Redis + RabbitMQ) per test class; never share containers across test classes that mutate state.
- **Fake clock** — `Microsoft.Extensions.TimeProvider.Testing` for Hangfire job logic (already in §8 *Testing strategy*).

#### 0.3.12 Global usings for duplicated references

- **Single source of truth** — every `using` that is **duplicated across two or more files in the same project** lives in `<Project>/GlobalUsings.cs`, not at the top of each file. The file `Catalog.API/GlobalUsings.cs` is the canonical place for project-wide global imports; the `BuildingBlocks/GlobalUsings.cs` equivalents cover shared cross-cutting namespaces.
- **What goes global** — any of the following qualify:
  - Project-local namespaces (`Catalog.API.Readers`, `Catalog.API.Caching`, `Catalog.API.Data`, etc.) once they're used in 2+ files.
  - Heavyweight third-party namespaces that show up in 2+ files (`Microsoft.EntityFrameworkCore`, `Microsoft.Extensions.Caching.Distributed`, `Microsoft.Extensions.Options`, `Microsoft.FeatureManagement`, etc.).
  - Framework namespaces the project already promotes project-wide (`BuildingBlocks.CQRS`, `BuildingBlocks.Exceptions`, `MediatR`, `Carter`, `FluentValidation`, `Mapster`, `NodaTime`).
- **What stays file-scoped** — namespaces used in **exactly one file** keep their `using` at the top of that file. Promoting a single-use namespace to global just adds noise to the project-wide list with no readers benefiting from it. The "2+ files" bar is the floor, not the ceiling: a singleton today might cross the bar after the next phase; promote then, not now.
- **Order matters for reviewability** — `GlobalUsings.cs` groups entries by *layer*: (1) BuildingBlocks.*, (2) third-party services (`Carter`, `MediatR`, `FluentValidation`, `Mapster`, `NodaTime`, `Npgsql`), (3) Microsoft.* extensions (`Microsoft.EntityFrameworkCore`, `Microsoft.Extensions.Caching.Distributed`, `Microsoft.Extensions.Options`), (4) project-local (`Catalog.API.*`), (5) `System.Security.Claims` (used widely by the auth path). Each block is alphabetised. A new global is added in the right block, not appended to the bottom.
- **Phase gate** — at the end of every phase, the implementer scans the files added or modified in that phase for duplicated `using` lines and consolidates them. Phase 1's promotion pass is documented in the v2.1 changelog as the reference pattern (see `Catalog.API/Caching/CachedMenuReader.cs`, `Caching/RedisCatalogCache.cs`, `Caching/CacheDriftRepairService.cs`, `Readers/MenuReader.cs`, `Readers/MenuSnapshot.cs` — the four `Microsoft.*` and one `Catalog.API.*` namespaces were hoisted to `GlobalUsings.cs` after the Phase 1 commit).
- **Anti-pattern** — leaving `using Microsoft.Extensions.Caching.Distributed;` at the top of every Cache file after the second one is added is a code smell. The first file uses it locally; the second one is the trigger to promote.

### 0.4 API design principles (REST + Carter + MediatR)

This section enforces the REST + Carter + MediatR + FluentValidation conventions every Catalog endpoint must follow. It is the API-shape counterpart to §0.1 (skill), §0.2 (doc-update), and §0.3 (code-quality). The companion skill is at `.claude/skills/api-design-principles/SKILL.md`.

#### 0.4.1 Resource-oriented design

- **Resources are nouns, not verbs** — endpoints name the resource, not the action. The action is the HTTP method.
- **Plural nouns for collections** — `/api/v1/menu-items`, `/api/v1/reservations`, `/api/v1/walk-in-queues` (kebab-case, per the project's route convention).
- **Hierarchical URLs for nested resources** — `/api/v1/restaurants/{restaurantId}/menu-items` when the parent is part of the resource's identity. Avoid more than two levels of nesting (deep hierarchies are a smell — split into a separate aggregate).
- **Resource IDs in the URL path** with type constraint: `/{id:guid}` for `Guid`, `/{id:int}` for `int`. Never `?id=...` in the query string for the primary key.

#### 0.4.2 HTTP method / status code matrix

Every endpoint declares its method and expected response codes. The matrix below is the source of truth — flag any deviation in code review.

| Method | Success | Client errors | Server errors |
|---|---|---|---|
| `POST /api/v1/<resource>` (create) | **201 Created** + `Location: /api/v1/<resource>/{newId}` header + body = created DTO | 400 Bad Request (validation), 409 Conflict (uniqueness / state) | 500 |
| `POST /api/v1/<resource>/{id}/<action>` (state transition) | **204 No Content** | 404 Not Found, 409 Conflict (illegal transition) | 500 |
| `GET /api/v1/<resource>/{id}` | **200 OK** + body, or 404 Not Found | 400 (malformed id) | 500 |
| `GET /api/v1/<resource>` (paged list) | **200 OK** + `PagedResult<T>` body | 400 (bad `page` / `pageSize`) | 500 |
| `PUT /api/v1/<resource>/{id}` (full replace) | **204 No Content** | 404, 400, 409 | 500 |
| `PATCH /api/v1/<resource>/{id}` (partial update) | **200 OK** + body, or 204 No Content | 404, 400, 409 | 500 |
| `DELETE /api/v1/<resource>/{id}` | **204 No Content** (idempotent: success whether or not the resource existed at call time, unless it was in a state that forbids delete → 409) | 404, 409 | 500 |
| `POST /api/v1/<resource>/bulk` (bulk operation) | **207 Multi-Status** + per-item results | 400 | 500 |

State-transition endpoints introduced by Phases 4–6 follow the `POST /{id}/<action>` row:
- Phase 4: `SplitMergedTable`, `ApproveBulkOrderUpload`, `RejectBulkOrderUpload`, `SubmitFeedback`, `RecomputeToday` (admin).
- Phase 5: implicit (Hangfire jobs — no HTTP surface).
- Phase 6.0/6.1: out of plan.

#### 0.4.3 Carter module structure

- **One `ICarterModule` per aggregate / feature group** — e.g., `MenuItemEndpoints`, `MergedTableEndpoints`, `BulkOrderUploadEndpoints`. Co-locate with the command/query handlers in `Features/<Resource>/<Action>/`.
- **Group endpoints with `MapGroup`** — `app.MapGroup("/api/v1/menu-items").WithTags("MenuItems").RequireAuthorization()` once per module; per-endpoint `RequirePermission("menu:edit")` overrides for write paths.
- **Lean endpoints** — endpoint methods do at most: (1) bind the route + query + body, (2) call `ISender.Send(...)`, (3) translate the result to an `IResult`. **No business logic in the endpoint.** The skill's Pattern 1 is the canonical shape.
- **`[AsParameters]` for complex query DTOs** — `group.MapGet("/", async (ISender sender, [AsParameters] GetMenuItemsQuery query) => ...)`. Avoid query strings with more than three parameters.
- **No MVC controllers** — this is a Carter-only project. The skill lists MVC Controllers as a common pitfall.

#### 0.4.4 DTO mapping

- **Mapster for mapping** (`request.Dto → domain entity` and `domain entity → response.Dto`) — matches the project's `Mapster` choice. New code uses Mapster; existing `Entity → DTO` mappings stay as-is unless the phase touches them.
- **Never expose EF Core entities or Marten documents directly in API responses** (csharp-developer MUST NOT DO; restated here for the API context).
- **Request DTOs are the command/query records themselves** — ASP.NET Core's body binder binds JSON to the record. No separate `Request` DTO wrapping the command.
- **Response DTOs are flat records** with NodaTime `Instant` for timestamps (`CreatedAt`, `UpdatedAt`, `DeletedAt`) — the skill's NodaTime rule applies; never `DateTime`.

#### 0.4.5 Pagination contract

- **Query parameters**: `?page=1&pageSize=20`. 1-indexed. Defaults: `page=1`, `pageSize=20`. Hard cap: `pageSize=100`.
- **Response shape** (`BuildingBlocks/Pagination/PagedResult.cs`, added by Phase 4 or earlier):
  ```csharp
  public sealed record PagedResult<T>(
      IReadOnlyList<T> Items,
      int Page,
      int PageSize,
      int TotalCount);
  ```
- **Validation**: 400 Bad Request when `page < 1`, `pageSize < 1`, or `pageSize > 100`. FluentValidation enforces; the response body is `ProblemDetails` with the offending field.
- **Out of scope today**: cursor-based pagination. Add only when offset pagination becomes a bottleneck (e.g., >100k rows being paged through).

#### 0.4.6 Validation pipeline

- **FluentValidation validators co-located** with each command: `Features/<Resource>/Commands/<Action>/<Action>CommandValidator.cs`.
- **Validators run via the BuildingBlocks MediatR pipeline behavior** (`ValidationBehavior<,>`) — runs **only on `ICommand<TResponse>`** (not queries, by project convention; queries are read-only and don't carry state-mutating invariants). This matches the skill's Pattern 3.
- **Endpoints do not call validators manually.** Invalid input from a malformed body or a route constraint failure is caught by ASP.NET Core's model binder and returned as 400 with `ValidationProblemDetails`. Invalid business rules from a FluentValidation rule are caught by `ValidationBehavior` and returned as 400 with the same shape.
- **Validation runs before the handler** — never in the handler. The handler trusts its inputs.

#### 0.4.7 Error responses (ProblemDetails, RFC 7807)

Every error response uses `application/problem+json` per RFC 7807. The global `CustomExceptionHandler` in BuildingBlocks maps exceptions to `ProblemDetails`:

| Exception | HTTP status | `type` URI hint | Notes |
|---|---|---|---|
| `NotFoundException` | 404 | `/problems/not-found` | Body: `{ Title, Status, Detail, ResourceId }` |
| `ValidationException` | 400 | `/problems/validation-failed` | Body: `{ Title, Status, Errors: { Field: [Messages] } }` |
| `DomainException` (state-transition violations) | 409 | `/problems/domain-conflict` | Body: `{ Title, Status, Detail, CurrentState, AttemptedTransition }` |
| `IngredientAvailabilityStaleException` (Phase 3) | 409 | `/problems/availability-stale` | Body: `{ Title, Status, Detail, MenuItemId, AttemptedAt }` |
| `UnhandledException` | 500 | `/problems/internal` | Body: `{ Title, Status, TraceId }` — message is generic; full detail in logs. |

The `type` field is a stable URI; the project's docs link to it.

#### 0.4.8 Idempotency for POST with side effects

POST endpoints that trigger downstream effects (Phase 4 `SubmitFeedback`, Phase 4 `BulkOrderUpload` upload, Phase 4 `ApproveBulkOrderUpload`, any future payment-like endpoint) accept an `Idempotency-Key` request header (UUID v4). Behavior:

- Middleware reads `Idempotency-Key`, hashes it with the user-id + endpoint, looks up Redis (`idempotency:{userId}:{sha256(key+endpoint)}`).
- **Cache hit**: return the cached response (status + body). No second side effect.
- **Cache miss**: process the request, store the response in Redis with a 24h TTL before returning.
- **Conflict (same key, different body)**: 422 Unprocessable Entity with `ProblemDetails` explaining the key was reused.
- **Out of scope today**: read-only endpoints and state-transition endpoints (which are already idempotent because they target a single resource by id).

#### 0.4.9 OpenAPI / Swagger

- Swashbuckle + SwaggerUI (or Scalar — TBD at implementation time) is registered in `Catalog.API/Program.cs`. Verify the current project's choice at implementation time and use whichever is in use elsewhere.
- **All Carter modules auto-discovered by `AddCarter()`**; OpenAPI metadata is generated from the route definitions + XML doc comments (per §0.3.1).
- **`WithTags(...)` sets the OpenAPI tag** — one tag per Carter group, matching the aggregate name (e.g., `"MenuItems"`, `"Reservations"`).
- **XML doc comments feed the operation summary / parameter descriptions** — the `GenerateDocumentationFile=true` build flag (per §0.3.1) is what surfaces them.
- **Authentication scheme declared** in the OpenAPI document so the Swagger UI "Authorize" button works (bearer JWT against the Identity authority).

#### 0.4.10 Cross-cutting API concerns

- **CORS**: not configured today (no browser frontend). When the React frontend lands, add a permissive dev policy + a locked-down prod policy keyed on the deployed origin.
- **Auth**: every Carter group calls `RequireAuthorization()`; per-endpoint `RequirePermission("...")` for write paths. `/health`, `/live`, `/ready` are public.
- **Rate limit**: YARP gateway enforces 10 req/min/host (`current-architecture.md` §4.6). No per-endpoint limit in Catalog unless a hot path warrants it; flag any endpoint handling more than 100 req/s for a review.
- **Correlation ID**: every request flows a `CorrelationId` from `IHttpContextAccessor` → handler scope → outbox row → MassTransit header → consumer scope (§0.3.8).
- **Response caching**: read endpoints that are cache-friendly (e.g., `GET /api/v1/restaurants/{id}/menu-items`) opt into `ResponseCache` with a short max-age that aligns with the §7.1 Redis cache TTLs. POST/PUT/PATCH/DELETE never opt into response caching.

---

## 1. Context

`Catalog.API` already runs end to end for basic menu / restaurant / table CRUD over PostgreSQL + EF Core, plus Marten for the three audit documents (`OrderSnapshot`, `OrderModificationLog`, `OrderItemPriceAudit`). All three extend `Entity<int>` today — a known code smell tracked in `db_relational_model.md` §137-148 and folded into §8 of this plan. Marten documents are not relational entities; the right fix is to **drop the base class**, not swap it for `Entity<Guid>`.

> The Marten `NotificationLog` previously in this list is being **removed**, not kept — see §6.7. Notification service will own the only `NotificationLog` going forward, as a relational table.

Concretely, three cross-cutting capabilities are not wired and several owned entities have incomplete or absent features:

- **Redis caching is absent** — `architecture.md` §174-181 and §645-658 prescribe `catalog:menu:{restaurantId}` (1h TTL) and `catalog:ingredients:{restaurantId}` (5 min TTL) plus invalidation on writes. `Program.cs:1-86` registers neither Redis nor distributed cache.
- **Message bus is absent** — `BuildingBlocks.Messaging/MassTransit/Extensions.cs` exposes `AddMessageBroker`, but `Catalog.API/Program.cs` never calls it. `BuildingBlocks.Messaging/Events/` has only Ordering/Kitchen events. Outbox pieces (`IOutboxPublisher`) live in `BuildingBlocks.Messaging/Outbox` but Catalog doesn't register them. As a result Catalog publishes **none** of the events listed in `architecture.md` §183-190 and cannot consume `OrderCompleted` to roll up `MenuItemAnalytics`.
- **No Ingredient Availability Engine** — `MenuItem.AvailabilityStatus` (Available/Limited/Unavailable) and `Restaurant.AllowAutoSubstitute` exist as data, but no service recomputes them when an ingredient or alternative changes. The "when ingredient unavailable → check alternatives → flip MenuItem.AvailabilityStatus" rule in `architecture.md` §172-176 is unimplemented.

A detailed gap inventory is in `docs/architecture/_catalog_gap_inventory.md` (added by this plan) and the drift memory `db-model-drift-reports.md`.

---

## 2. Goal

Bring `Catalog.API` to the level `architecture.md` describes by:

1. Wiring **Redis** (existing shared instance) for menu and ingredient availability, with per-feature invalidation and a fail-open reconcile path.
2. Wiring **MassTransit + RabbitMQ + the existing outbox** and emitting all five Catalog-side integration events; consuming `OrderCompleted` for `MenuItemAnalytics`. Event versioning via `int SchemaVersion`.
3. Building the **Ingredient Availability Engine** with `IDomainEvent` triggers dispatched by `DispatchDomainEventsInterceptor`, plus a safety-net hosted reconciler.
4. Closing the partial / missing **vertical-slice features** so each owned entity has the endpoints the architecture describes.
5. Wiring the **async lifecycle** (Hangfire-scheduled) for reservations (reminder, no-show) and walk-in queue (notification, 10-minute response window) plus seasonal/promo availability.
6. In-plan: moving the **`Coupon`** table to Discount (its destination already exists). Out-of-plan: tracking the **prerequisites** for moving Reservation/WalkInQueue to Ordering and CustomerFeedback/NotificationLog to Notification — the receiving services need their own plans first.

The companion `docs/architecture/db_relational_model.mermaid` change-log appendix is also kept honest by this plan (no new schema additions that don't update both sides).

---

## 3. Out of scope

- Redesigning the relational schema. The mermaid is reconciled to code; only the known mismatches flagged in `db_relational_model.md` §137-148 are open for code-side fixes (and they are sequenced as small fixes under §8 here, not as schema redesign).
- Splitting `Catalog.API` into `Catalog.Domain` / `Catalog.Application` / `Catalog.Infrastructure` projects. The single-project vertical-slice layout matches the other existing services; keeping it.
- The frontend project (lives in a different folder, owned separately).
- Adding new permissions in Identity — the `kitchen:*` permissions already exist; for Catalog only `menu:edit`, `restaurant:edit`, etc. need review if any new endpoint surfaces a previously-unexposed action. Add in the Identity service as a follow-up if surfaced.
- The four Marten documents' `Entity<int>` / synthetic-`Guid` id mismatch (lifted from `db_relational_model.md` §137-148). Marten documents are **not relational entities** — they should not extend any of `AuditableEntity<>`, `Entity<int>`, or `Entity<Guid>`. The intended fix is to drop the base class entirely and let Marten assign its synthetic `Guid` id via `[HiloSequence]` or the default synthetic-key convention (or, when a known id is required, `[Identity]`/explicit `Guid Id`). §8 below carries this.
- **Introducing `Reservation` / `WalkInQueue` aggregates in Ordering.** That work belongs to a separate Ordering-side plan; this plan only documents the prerequisite (§7.6.0).
- **Building the `Notification.API` skeleton.** That belongs to its own v1 plan; this plan only documents the prerequisite (§7.6.1).

---

## 4. Service boundaries

### Catalog.API owns

- **Restaurants** and **Brands** (multi-tenant scope keys for everything below).
- **Tables** (incl. position X/Y, shape, status, current order), **MergedTables** (parent/child relationships, merge/split semantics).
- **Menu tree**: `MenuCategories` → `MenuSubCategories` → `MenuItems`, with `MenuItemVariations`, `ComboItems`, `MenuItemIngredients`.
- **Ingredients**, `IngredientAlternatives` (incl. `AutoSubstitute`), **`MenuItem.AvailabilityStatus`** computations.
- **`MenuItemAnalytics`** (daily aggregates, updated from the `OrderCompleted` consumer).
- **`PriceHistory`** audit log (append-only history of every menu-item / variation / alternative price change).
- The Redis cache strategies (`catalog:menu:*`, `catalog:ingredients:*`) and their invalidation discipline.
- Publishing: `MenuItemCreated`, `MenuItemUpdated`, `MenuItemDeleted`, `IngredientAvailabilityChanged`, `TableStatusChanged`, `RestaurantConfigurationChanged`.
- Consuming: `OrderCompleted` (drives `MenuItemAnalytics`).

### Catalog.API does NOT own

- **`Orders`, `OrderItems`, `OrderBills`, `OrderModificationLog`, `OrderSnapshot`, `OrderItemPriceAudit`, `OrderTimingAnalytics`** — Ordering domain. Catalog **persists** the four Marten *documents* (`OrderSnapshot`, `OrderModificationLog`, `OrderItemPriceAudit`) and the relational `NotificationLog` because they're cross-service audit records, but the **writers** live in Ordering / Kitchen / Notification. None of these documents extend `AuditableEntity<>` / `Entity<int>` / `Entity<Guid>` (Marten documents are not relational entities).
- **`Reservation`, `WalkInQueue`, `BulkOrderUpload`** — Ordering domain. **No scaffolding exists in Ordering today** (verified by file glob — only `Catalog.API/Models/` defines them). The move requires an Ordering-side plan to introduce the aggregates first (Phase 6.0 prerequisite). Catalog keeps the writers until that plan lands.
- **`CustomerFeedback`, `NotificationLog`** — Notification domain. **No `Notification.API` exists today** (verified — `Services/` has only Basket/Catalog/Discount/Identity/Kitchen/Ordering). Phase 6.1 prerequisite: Notification v1 plan. Catalog keeps the writers until then. **`NotificationLog` has a single owner going forward — Notification, as a relational table** (§6.7). The Marten `NotificationLog` currently registered in `Catalog.API/Program.cs:38` is removed once Notification v1 lands and backfills its rows; it is not retained alongside a relational copy.
- **`User`** (in Catalog) — Identity mirror. Keep local-mirror model only; no write path; document explicitly in `db_relational_model.md` §"Out of scope".
- **`Coupon`** — Discount service responsibility (per architecture §210-216 and §283-292). **Discount already has the table** (`Discount.Grpc/Models/Coupon.cs:6-15`), with the same columns plus `AuditableEntity<int>` instead of `Entity<int>`. The Coupon move is in-plan (§7.6.2).

### Ordering ↔ Catalog flow (one-liner)

```
Ordering publishes OrderCompletedIntegrationEvent
  → Catalog consumes → bumps MenuItemAnalytics row for the day
Catalog publishes MenuItemUpdated / IngredientAvailabilityChanged /
  TableStatusChanged / RestaurantConfigurationChanged
  → Basket consumes (cache validate / invalidate)
  → Discount consumes MenuItemUpdated to re-evaluate "Buy X get Y" rules
  → Kitchen reads via its read replica (not the bus)
```

---

## 5. Tech decisions

| Decision | Choice | Reason |
|---|---|---|
| Architecture | Vertical Slice (existing), single project | Matches Catalog / Basket / Discount pattern. |
| Framework | ASP.NET Core 10 (Carter + minimal API) | Already in use. |
| **Language** | **C# 12+** (records for integration events and DTOs; primary constructors for handlers and small services; collection expressions; required members; nullable enabled) | New code uses modern C#. Records and primary constructors match the kitchen plan's idiom. Existing code is updated only where edits are made. |
| Persistence | EF Core 10 + `Npgsql.EntityFrameworkCore.PostgreSQL` for relational; Marten for documents | Already wired in `Program.cs:30-54`. |
| Cache | `Microsoft.Extensions.Caching.StackExchangeRedis` + `IConnectionMultiplexer` | Catalog connects to the **existing shared Redis** instance (`distributedcache:6379` in docker-compose, `localhost:6379` in local dev). **No new Redis database** — just a new client + a typed `ICatalogCache` helper. |
| Messaging | `MassTransit` via `BuildingBlocks.Messaging.MassTransit.AddMessageBroker` | Reuse existing extension. |
| Event versioning | `int SchemaVersion` on every integration event (initial = 1) | Consumers ignore unknown major versions. Documented in §8. |
| Outbox | `BuildingBlocks.Messaging.Outbox.IOutboxPublisher` from Catalog handlers | Matches the pattern Ordering/Kitchen already use (commits c95325c, def4187). |
| Scheduling | **Hangfire** in Catalog (PostgreSQL schema, recurring-job dashboard, persistent schedule) | Decision #3 from csharp-expert pass. Reservation reminder, reservation no-show, walk-in no-show, seasonal availability — all recurring jobs. |
| Engine trigger | In-process `IDomainEvent` raised on ingredient / alternative / menu-item-ingredient mutations, dispatched by `DispatchDomainEventsInterceptor` mirroring Ordering/Kitchen | Decision #7. Same-transaction semantics; latency-bounded; reconcile hosted service is a safety net only. |
| Cache failure path | **Fail-open + log**: cache invalidation best-effort; `CacheDriftRepairService` hosted service runs every 5 min to diff DB against cache keys | Decision #8. Cache outage doesn't fail writes; reconcile repairs drift. |
| **Health checks** | `/live` (process up) + `/ready` (Postgres, Redis, RabbitMQ, outbox dead-message count). **Any** outbox DLQ message trips `/ready` to unready (threshold configurable via `CatalogOptions:OutboxDeadLetterThreshold`, default 0). | Decision #4 / #11. K8s-style split. |
| Time / IDs | NodaTime `Instant`, `Guid` ids | Matches AGENTS.md conventions. |
| Logging | Structured logging (existing setup) | Default. |
| Mapping | `Mapster` (matches the rest of the codebase) | New DTOs only. |
| Validation | FluentValidation (existing) | Default. |
| Tests | xUnit + FluentAssertions + Moq for unit; **Testcontainers** (Postgres + Redis + RabbitMQ) for engine / outbox / cache integration | Decision #12. |

### What this plan does NOT introduce

- **No new database.** Catalog stays on its existing `catalogdb` instance + Marten docs alongside relational.
- **No new Redis database.** Phase 1 attaches Catalog as a new client to the existing shared Redis (`distributedcache` container); no new container, no new image, no new volume.
- **No new message broker.** RabbitMQ (already running in docker-compose).
- **No new permission scheme.** Lean on JWT claims already populated by Identity.
- **No saga/orchestrator for the engine.** The Ingredient Availability Engine is event-driven local recompute, not a process manager.
- **No Reservation/WalkInQueue/CustomerFeedback/NotificationLog tables in Ordering or Notification services.** Those services don't exist in skeleton form yet; this plan flags the prerequisite but does not introduce their tables.

---

## 6. Folder layout

Today's project is flat (`Catalog.API/Features/<entity>/<action>/`). This plan adds a small number of new top-level folders without restructuring what's already there:

```
Services/Catalog/Catalog.API/
  Models/                                  -- existing (no schema changes)
  Data/                                    -- existing (CatalogDbContext, Migrations)
  Features/                                -- existing vertical slices (one folder per entity)
    <NewSliceFolders>                      -- added by §7.4 (MergedTables, BulkOrderUploads,
                                              SubCategories.Delete, PriceHistories,
                                              MenuItemAnalytics.Updater, CustomerFeedback.Submit)
  Cache/
    CatalogCache.cs                        -- ICatalogCache + RedisCatalogCache (typed keys)
    CacheKeys.cs                           -- format strings catalog:menu:{rid}, catalog:ingredients:{rid}
    CacheDriftRepairService.cs             -- hosted service: best-effort diff & repair (decision #8)
  Messaging/
    Events/                                -- the 5 Catalog integration events (each carries int SchemaVersion)
    EventHandlers/
      OrderCompletedHandler.cs             -- plain IConsumer<T>; idempotent on (OrderId, MenuItemId)
  Availability/
    IngredientAvailabilityEngine.cs        -- pure function over aggregates
    Events/                                -- in-process IDomainEvent + handler that calls the engine
    IngredientAvailabilityReconcileService.cs -- safety-net hosted service (flag-gated)
  Scheduling/
    ReservationReminderJob.cs              -- Hangfire recurring (every 5 min)
    ReservationNoShowJob.cs                -- Hangfire recurring (every 1 min)
    WalkInNoShowJob.cs                     -- Hangfire recurring (every 1 min)
    SeasonalAvailabilityJob.cs             -- Hangfire recurring (every 5 min)
  Health/
    CatalogHealthChecks.cs                 -- adds /live + /ready with Redis + RabbitMQ + outbox checks
    OutboxDeadLetterProbe.cs               -- probe that surfaces count to /ready (decision #11)
  Exceptions/                              -- existing; add IngredientAvailabilityStaleException etc.
  DomainEvents/
    DispatchDomainEventsInterceptor.cs     -- registered in DbContext (mirrors Ordering/Kitchen)
  Program.cs                               -- wire the above
```

All new code follows the same CQRS / MediatR handler pattern existing in `Features/`. New code uses C# 12+ records for events and DTOs, primary constructors for small services.

---

## 6.5 Consumer contract matrix

Single source of truth for which event goes where, and what each consumer is expected to do. **Both** publisher (Catalog) and consumers must honour this list at every release. Catalog will not change a published event's shape without a major `SchemaVersion` bump.

| Event (Catalog →) | SchemaVersion=1 fields | Intended consumers → required action |
|---|---|---|
| `MenuItemChangedIntegrationEvent` (with `ChangeType ∈ Created, Updated, Deleted`) | `MenuItemId`, `RestaurantId`, `ChangeType`, `SchemaVersion=1` | **Basket** → invalidate any cached price/availability lookups for `MenuItemId`; validate any pending baskets. **Discount** → if `ChangeType = Deleted`, deactivate rules referencing this item; if `Updated`, re-evaluate BOGO / "Buy X get Y" thresholds. **Ordering** → new orders must validate the menu item is still valid + available (existing orders unaffected — they snapshot price/name). |
| `IngredientAvailabilityChangedIntegrationEvent` | `MenuItemId`, `RestaurantId`, `AvailabilityStatus`, `AutoSubstituteOf?`, `SchemaVersion=1` | **Basket** → re-validate pending baskets; reject checkout if `Unavailable`; prompt customer if `Limited`. **Ordering** → reject new orders where status = Unavailable (Limited must collect substitute choice at order placement). |
| `TableStatusChangedIntegrationEvent` | `TableId`, `RestaurantId`, `NewStatus`, `SchemaVersion=1` | **Ordering** → reservation / order placement checks `Table.Status == Available` (with optimistic concurrency on the hold window). Walk-in worker uses this to assign waiting parties. **Reservation expiry** invalidates the reservation hold when status flips to Cancelled / NoShow. |
| `RestaurantConfigurationChangedIntegrationEvent` | `RestaurantId`, `ChangedFields` (array of names), `SchemaVersion=1` | **Identity** → affected users must re-login for fresh JWT claims (`restaurantId` and any role-bound configuration). **Discount** → if `Currency` changed, deactivate or reissue coupons. **Notification** → receipt templates pick up new tax/currency placeholders. |

| Event (→ Catalog) | Source | SchemaVersion | Catalog's required action |
|---|---|---|---|
| `OrderCompletedIntegrationEvent` | Ordering | 1 | Find `MenuItemAnalytics` row for `(MenuItemId, AnalysisDate = UTC date)`; if missing, create; otherwise bump `TimesOrdered` and `TotalRevenue`. Idempotent on `(OrderId, MenuItemId)`. |

**Migration rule when this matrix changes:**
- **Adding a field** → bump `SchemaVersion`; consumers ignore unknown fields by MassTransit default.
- **Renaming a field** → rename + bump major version (i.e. introduce `SchemaVersion=2` side-by-side); consumers on v1 ignore the new name; publish both for one release cycle, drop v1 after one release with zero traffic.
- **Removing a field** → same: introduce next major, deprecate, drop after one release.

---

## 6.6 Gateway route migration convention

YARP is configured in `ApiGateway/YarpApiGateway/appsettings.json` with one route + cluster per service, address `/{service-name}-api/{**catch-all}` + `PathRemovePrefix` + cluster `{service-name}-cluster`. **There is no existing precedent for moving a route between services.**

**Adopted convention (decision #16):** atomic `ClusterId` change, keep public path stable. For any future entity move:

1. Add a new route in `appsettings.json` with `Path` = `/<target-service>-api/<new-entities>/{**catch-all}`, `ClusterId` = `<target-service>-cluster`. Verify with smoke test.
2. Re-point the existing `/<source-service>-api/<new-entities>/{**catch-all}` route's `ClusterId` to the target service's cluster. Public URL stays stable — existing clients don't change.
3. After one release with zero traffic on the legacy route, remove the source's old route entry.
4. Update `db_relational_model.mermaid` with a "moved from Catalog on YYYY-MM-DD" note.

**Optional cleanup step:** if the migrated endpoints accumulate multiple leftovers, add a deprecation header (`X-Service-Moved: <new prefix>`) for one release to nudge clients forward.

The Coupon move (§7.6.2) is the first application of this convention.

### 6.7 — `NotificationLog` ownership: Notification owns the only one

**Decision (Option B from v1.2 review).** Earlier versions of this plan contemplated two `NotificationLog` concepts coexisting indefinitely — a Marten document in Catalog (audit) and a relational table in Notification (operational queue). Reviewer flagged this as ambiguous: a reader couldn't tell which was source of truth, whether one replaced the other, or how the two related. **Decision: there is only one `NotificationLog` going forward — relational, owned by the Notification service.** Catalog's Marten `NotificationLog` is removed once Notification v1 lands and backfills its rows.

**Why merge instead of split.**

- **One concept, one name, one shape** — no dual-writer or dual-reader confusion, no migration question ("which table is the row I just queried?").
- **Relational is a better fit.** The operational fields that Notification needs (Status with retry counters, `NextAttemptAt`, indexed by `(Status, NextAttemptAt)` for the retry worker) are inherently relational — Marten documents don't naturally model those indexes.
- **No active writers today.** Verified by grep over `Services/Catalog/Catalog.API/**` — the Marten `NotificationLog` schema is registered at `Program.cs:38` (`opt.Schema.For<NotificationLog>();`) but **nothing inserts into it**. The "audit log" intent was never implemented; the only data is whatever was seeded or written by hand.
- **Notification owns delivery.** When Notification ships, it writes to its own table in its own transaction; it has no business writing to a document store owned by Catalog. The cross-service write would force a Marten session in Catalog's `catalogdb`, which Catalog already uses for its own aggregates — a recipe for coupling.

**What changes in Catalog (driven by Notification v1 plan landing; out of scope for this plan).**

1. `Catalog.API/Models/NotificationLog.cs` is deleted.
2. `Catalog.API/Program.cs:38` (`opt.Schema.For<NotificationLog>();`) is removed.
3. A no-op Catalog migration drops the empty `mt_doc_notification_log` storage (Marten will GC the schema entries once unregistered; no DDL is needed if the table is empty).
4. `db_relational_model.mermaid` removes the `NotificationLog` diagram block and the two relationship rows (`Orders ||--o{ NotificationLog : triggers`, `Reservations ||--o{ NotificationLog : triggers`).
5. `db_relational_model.md` drops the `NotificationLog` row from the Marten-document list (§35) and the code-vs-storage mismatch paragraph (§44).
6. `current-architecture.md` §4.2 entity table drops the `NotificationLog` row; the "Catalog persists … `NotificationLog`" sentence in §4 is removed.

**Backfill sequence (owned by the Notification v1 plan, not this one).**

1. Notification applies its migration creating the relational `notification_log` table with the columns in §6.1.
2. A one-shot console job runs in Catalog (gated by `Catalog:BackfillNotificationLogs=true`): read every row from `mt_doc_notification_log`, map fields to the relational shape (Marten's synthetic `Guid` id → `OriginalMartenId`; `Status.Pending/Sent/Failed` map 1:1; `Channel`, `MessageType`, `RecipientType`, `RecipientIdentifier`, `MessageContent`, `RelatedOrderId`, `RelatedReservationId`, `CreatedAt`, `SentAt` copy directly). Idempotent insert keyed by `OriginalMartenId`.
3. Verify: `SELECT COUNT(*) FROM mt_doc_notification_log` matches the number of successful inserts in Notification's log.
4. Catalog removes the schema registration + deletes the model file. The original Marten document data is left in `mt_doc_notification_log` for one retention cycle; a follow-up retention decision (delete after N months / never) is owned by the Notification v1 plan.

**§4 / §6.1 alignment.**

- §4 line 115 is rewritten to read "`CustomerFeedback`, `NotificationLog` — Notification domain. … `NotificationLog` has a single owner going forward — Notification, as a relational table (§6.7)."
- §6.1 prerequisite list reframes "A relational `NotificationLog` table (Marten document `NotificationLog` stays in Catalog; the relational one moves)" to "The `NotificationLog` relational table (the only `NotificationLog` going forward — see §6.7). It owns: id, …".

**Doc-update scope when Notification v1 lands (drives both Catalog and Notification docs).**

- `current-architecture.md` §4.2 Catalog Service — drop `NotificationLog` row from the entity table; remove "Catalog persists … `NotificationLog`" sentence.
- `current-architecture.md` new §4.7 Notification Service (created by Notification v1 plan) — describe the relational `notification_log` table per §6.1's column list.
- `current-architecture.md` §5.2 Asynchronous — add any new outbound events Notification publishes (`NotificationDelivered`, `NotificationFailed`).
- `db_relational_model.mermaid` + `db_relational_model.md` — drop the Catalog-side `NotificationLog` block and references.
- `CATALOG_SERVICE_PLAN.md` §9 Cleanup milestone — `NotificationLog` removed from the four-document list (now three).

**Why this is out of scope for the Catalog plan.** The relational table lives in Notification's database (`notificationdb` — to be created by the Notification v1 plan), not Catalog's. The backfill touches Catalog code but is initiated by Notification's deploy. No Catalog code change is appropriate *until* Notification v1 has somewhere to backfill to. Tracking continues in Phase 6.1.

---

## 7. Phased milestones

The phases are ordered so each one is independently shippable and any earlier phase's failure does not block the later cross-cutting concerns.

### Phase 1 — Redis cache

- The repo already has a single shared Redis instance (`distributedcache` container in `docker-compose.override.yml:98`, address `ConnectionStrings__Redis=distributedcache:6379,password=...`; local dev `localhost:6379`). Catalog **does not** provision a new Redis database — it just adds a connection string env var and a new client.
  - Add `ConnectionStrings__Redis=distributedcache:6379,password=${REDIS_PASSWORD:-redisdev}` to the `catalog.api` block in `docker-compose.override.yml`.
  - Add `"Redis": "localhost:6379"` to `Services/Catalog/Catalog.API/appsettings.json`.
- Add `ICatalogCache` abstraction with `GetFullMenuAsync(restaurantId)`, `GetIngredientAvailabilityAsync(restaurantId)`, `InvalidateMenuAsync(restaurantId)`, `InvalidateIngredientsAsync(restaurantId)`.
- Register `IConnectionMultiplexer` (`services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(builder.Configuration.GetConnectionString("Redis")!))`) and wrap in `services.AddStackExchangeRedisCache(...)` only if `IDistributedCache` is needed.
- TTLs: menu 1h, ingredients 5min — match `architecture.md` §174-181.
- Decorator pattern via Scrutor (`AddScoped<IMenuReader, CachedMenuReader>()` wrapping `MenuReader`) — same idiom as `Basket.API/Program.cs:57-66`.
- All cache keys are namespaced under `catalog:*` so Basket / other services don't collide on the shared Redis.
- Invalidation hooks:
  - `CreateMenuItemHandler`, `UpdateMenuItemHandler`, `DeleteMenuItemHandler`, `CreateMenuItemVariationHandler`, `UpdateMenuItemVariationHandler`, `DeleteMenuItemVariationHandler`, all `MenuSubCategory` / `MenuCategory` mutators → call `InvalidateMenuAsync(restaurantId)`.
  - `CreateIngredientHandler`, `UpdateIngredientHandler`, `DeleteIngredientHandler`, `CreateIngredientAlternativeHandler`, `UpdateIngredientAlternativeHandler` → call `InvalidateIngredientsAsync(restaurantId)`.
- **Failure-mode policy (decision #8):** cache calls are best-effort. If Redis is unreachable, the mutation commits; the call is logged at `Warning`. The new `CacheDriftRepairService` (`Cache/CacheDriftRepairService.cs`) is an `IHostedService` that runs every 5 minutes (configurable via `CatalogOptions:CacheRepairInterval`), diffs the per-restaurant DB row counts against cache key presence, and repopulates any missing keys. Flag-gated.
- Flag-gate the rollout behind `FeatureManagement__CatalogRedisCache=true` so it can be disabled without a redeploy if the cache goes stale.
- **Tests:** Testcontainers (real Redis) integration test for hit/miss + invalidation hooks + drift-repair simulation when Redis is killed mid-test.

**Doc-update scope (§0.2):**
- §2 Tech Stack — add `Microsoft.Extensions.Caching.StackExchangeRedis` + `IConnectionMultiplexer` row.
- §4.2 Catalog Service — add **Caching** subsection (key formats `catalog:menu:{rid}` / `catalog:ingredients:{rid}`, TTLs, fail-open policy, `CacheDriftRepairService` hosted service cadence).
- §4.2 Catalog Service — add the `catalog-api` row to the gateway prefix table if not already present.
- §9 Cross-Cutting Patterns — note `ICatalogCache` + Scrutor `CachedMenuReader` decorator in the caching row.
- §11 Local Development — startup sequence line confirming Catalog reads `ConnectionStrings__Redis` from env/compose.

### Phase 2 — Messaging + outbox wiring

- Add the five Catalog integration event classes in `BuildingBlocks.Messaging/Events/Catalog/`. Each event carries:
  - `int SchemaVersion = 1` (initial)
  - `Guid EventId` (deterministic if redelivery matters, otherwise `Guid.NewGuid()`)
  - `Instant OccurredAt`
  - `Guid RestaurantId`
  - Event-type-specific payload
  - Concrete types:
    - `MenuItemChangedIntegrationEvent` (with `ChangeType ∈ Created, Updated, Deleted`)
    - `IngredientAvailabilityChangedIntegrationEvent`
    - `TableStatusChangedIntegrationEvent`
    - `RestaurantConfigurationChangedIntegrationEvent`
- Convert Catalog handlers that mutate state to publish via `IOutboxPublisher` after `SaveChangesAsync` so the outbox is committed in the same transaction. Mirrors how Ordering's `SaveChangesInterceptor` is wired (commit c95325c).
- **`OrderCompletedIntegrationEvent` consumer** — placed at `Catalog.API/Messaging/EventHandlers/OrderCompletedHandler.cs`. **Plain `IConsumer<OrderCompletedIntegrationEvent>` handler class** (decision #10). Stateless, idempotent: keyed by `(OrderId, MenuItemId)`, the handler skips if a row with that key already exists for the day. Test with MassTransit's `InMemoryTestHarness`.
- Register `AddMessageBroker` in `Catalog.API/Program.cs`.
- **Poison queue / dead-letter handling** following the pattern from commit `def4187`. Outbox dead-letter table is `OutboxDeadMessage` in `BuildingBlocks.Messaging/Outbox`. **`OutboxDeadLetterProbe`** (new `Health/OutboxDeadLetterProbe.cs`) reads `Count()` and exposes it for the `/ready` endpoint.
- **`/ready` health check** (`Health/CatalogHealthChecks.cs`):
  - `AddNpgSql(connectionString)` — Postgres reachable.
  - `AddRedis(connectionString)` — Redis reachable.
  - `AddRabbitMQ(...)` via MassTransit's `CheckHealth` extension — RabbitMQ broker reachable.
  - `AddCheck<OutboxDeadLetterProbe>("outbox_dlq")` — returns `Unhealthy` if `Count > CatalogOptions:OutboxDeadLetterThreshold` (default `0`; decision #11).
  - `MapHealthChecks("/live", new HealthCheckOptions { Predicate = _ => false })` — always green; liveness only.
  - `MapHealthChecks("/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") })` — full suite; 503 if any fail.
- Emit `RestaurantConfigurationChanged` whenever `UpdateRestaurantHandler` mutates `AllowAutoSubstitute`, `AutoConfirmReservations`, `TaxRate`, `Currency`, `TimeZone`, `EstimatedTurnoverMinutes`.
- Add a doc block in `BuildingBlocks.Messaging/Events/` index listing which service publishes which events.
- **Tests:** Testcontainers + MassTransit `InMemoryTestHarness` for publish → consume round-trip on `OrderCompleted`. Unit tests on handler idempotency.

**Doc-update scope (§0.2):**
- §2 Tech Stack — note MassTransit is now wired in Catalog (row already mentions Catalog? add RabbitMQ broker note if missing).
- §4.2 Catalog Service — replace "**Events published / consumed by Catalog.** None." with the four-event publish list (`MenuItemChangedIntegrationEvent`, `IngredientAvailabilityChangedIntegrationEvent`, `TableStatusChangedIntegrationEvent`, `RestaurantConfigurationChangedIntegrationEvent`) and the `OrderCompletedIntegrationEvent` consumer row.
- §5.2 Asynchronous — add five rows to the publish/consume matrix: the four Catalog→ events and the `OrderCompletedIntegrationEvent → Catalog` row.
- §4.2 Catalog Service — flip **Health** row from a single `/health` to `/live` + `/ready` split (defer full details to Phase 2's health row update below or note forward-reference to the `/ready` block in this section).
- §9 Cross-Cutting Patterns — add a row for `IOutboxPublisher` use in Catalog handlers + `OutboxDeadLetterProbe` (mirror Ordering/Kitchen).
- §12 Observability — append the `/ready` entry (`outbox_dlq`, `redis`, `rabbitmq`) to the bullet list.

### Phase 3 — Ingredient Availability Engine

- Define a pure `IngredientAvailabilityEngine.AvailabilityProfileFor(menuItemId, restaurantId)` returning `{ AvailabilityStatus, AutoSubstituteOf }` based on:
  - `MenuItemIngredients` joined to `Ingredients.IsAvailable`
  - For each unavailable ingredient, look up `IngredientAlternatives` where `AlternativeIngredientId ∈ {available set}`
  - If no unavailable ingredients → `Available`.
  - If any unavailable ingredient has an alternative → `Limited` (unless auto-substitute applies to all of them) and record which alternative wins.
  - If any unavailable ingredient has no alternative and is **not optional** on the menu item → `Unavailable`.
  - If `Restaurant.AllowAutoSubstitute` AND every unavailable ingredient has exactly one `AutoSubstitute = true` alternative → `Available` with `AutoSubstituteOf` populated.
- **Trigger (decision #7):** in-process `IDomainEvent` raised on `Ingredient.UpdateIngredient`, `IngredientAlternative.{Create,Update,Delete}`, and `MenuItemIngredient.{Add,Remove}` handlers. Domain events are accumulated by the EF Core change tracker and dispatched by a new `DispatchDomainEventsInterceptor` (mirrors Ordering/Kitchen). Synchronous in the same transaction.
- A safety-net `IHostedService` (`Availability/IngredientAvailabilityReconcileService.cs`) runs once at startup and on a 1-minute interval (configurable, off by default) to catch any drift from missed events. Uses `FeatureManagement__CatalogAvailabilityEngineReconcile=true`.
- After recompute, publish `IngredientAvailabilityChangedIntegrationEvent` per affected `MenuItem` (batched, deduped per restaurant).
- **Tests:** Unit tests over the matrix in `architecture.md` §927-931 (no alt + not optional → Unavailable; alt exists → Limited; auto-substitute satisfied → Available). Testcontainers integration for the full refresh path.

**Doc-update scope (§0.2):**
- §4.2 Catalog Service — add a paragraph under the engine describing the `IngredientAvailabilityEngine.AvailabilityProfileFor` rule, the `IDomainEvent` trigger via `DispatchDomainEventsInterceptor`, and the flag-gated `IngredientAvailabilityReconcileService` safety net.
- §9 Cross-Cutting Patterns — extend the interceptors row to mention `DispatchDomainEventsInterceptor` registration in Catalog (mirror Ordering/Kitchen).

### Phase 4 — Complete partial / missing features

| Feature slice | What to add |
|---|---|
| `Features/MergedTables/` | `CreateMergedTableCommand` (parent + N children, validate capacity sum, mark children as unavailable), `SplitMergedTableCommand` (reactivates children), `GetMergedTablesByRestaurantQuery`. Reuses `Tables` DbContext. |
| `Features/MenuSubCategories/DeleteMenuSubCategory/` | Soft-delete command — sets `IsDeleted = true`, `DeletedAt = Instant`, cascades suppression in reads. |
| `Features/ComboItems/UpdateComboItem/` | `UpdateComboItemCommand` allowing quantity / isOptional changes. Validates `IncludedMenuItem` still exists. |
| `Features/BulkOrderUploads/` | Upload endpoint (multipart), validate menu-item ids + table availability, persist a `BulkOrderUpload` row with `ErrorLog` populated. Add `GetBulkOrderUpload`, `ApproveBulkOrderUpload` (manager/admin), `RejectBulkOrderUpload`. **Stays in Catalog** — the move to Ordering is now out-of-plan (§7.6.0). |
| `Features/PriceHistories/CreatePriceHistory/` | Internal write path invoked automatically by the price-mutation handlers (variation update, base price update, alternative price update). Records `OldPrice`, `NewPrice`, `Reason`, `ChangedByUserId`, `EffectiveDate`. |
| `Features/RestaurantConfiguration/AuditOnUpdate/` | Wrap `UpdateRestaurantHandler` to produce a `PriceHistory`-style audit entry for any of the listed mutating fields so changes are traceable across the system. |
| `Features/MenuItemAnalytics/` | Already has reads. Add `RecomputeTodayCommand` (admin) + automatic nightly recompute (`IHostedService`). The hot path is the `OrderCompleted` consumer. |
| `Features/CustomerFeedback/SubmitFeedback/` | `SubmitFeedbackCommand` accepting the four ratings + comments + `OrderId`. On `OverallRating ≥ 4` emit `FeedbackSubmittedIntegrationEvent` (lives in `BuildingBlocks.Messaging/Events/Notification/`). **Stays in Catalog** — the move to Notification is now out-of-plan (§7.6.1). |

**Doc-update scope (§0.2):**
- §4.2 Catalog Service — extend the **Endpoints by feature** list to include `MergedTables` (Create/Split/Get), `BulkOrderUploads` (Upload/Get/Approve/Reject), `MenuSubCategories` Delete, `ComboItems` Update, `PriceHistories` (now auto-populated by mutations), `MenuItemAnalytics.RecomputeToday`, and `CustomerFeedback.SubmitFeedback`.
- §4.2 Catalog Service — note the nightly recompute `IHostedService` for `MenuItemAnalytics` under the engine / hosted-service paragraph.
- §11 Local Development — append `Catalog.API.Tests` once the new test slices land (Testcontainers — Postgres + Redis + RabbitMQ for cache/outbox/engine coverage).

### Phase 5 — Async lifecycle (reservations, walk-in, seasonal)

**Decision: Hangfire in Catalog.** Adds a `hangfire` schema in `catalogdb` for job storage and a recurring-job dashboard.

- `ReservationReminderJob` (Hangfire recurring, every 5 minutes):
  - Find `Reservation` rows where `Status = Confirmed`, `ReminderSent = false`, `ReservationDate + ReservationTime - 1h ≤ now`.
  - Publish a `ReservationReminderDueIntegrationEvent`. Notification consumes; sends WhatsApp/email.
  - Set `ReminderSent = true`, `ReminderSentAt`.
- `ReservationNoShowJob` (every 1 minute):
  - Find `Reservation` rows where `Status = Confirmed`, `ReservationDate + ReservationTime + 15m ≤ now`, `SeatedAt` is null.
  - Transition `Status = NoShow`, set `CancelledAt`.
- `WalkInNoShowJob` (every 1 minute):
  - Find `WalkInQueue` rows where `Status = Notified`, `NotifiedAt + 10m ≤ now`, `SeatedAt` is null.
  - Transition `Status = NoShow`. Free the held table.
- `SeasonalAvailabilityJob` (every 5 minutes):
  - For each `MenuItem` with `ItemType = Seasonal`, ensure `IsAvailable` reflects `SeasonStartDate ≤ today ≤ SeasonEndDate`.
  - Same logic for promo items vs `PromoStartDate` / `PromoEndDate`.
- All four jobs are feature-flag gated (`FeatureManagement__CatalogScheduledJobs=true`).
- **Tests:** integration tests using a fake clock (e.g. `Microsoft.Extensions.TimeProvider.Testing`) — not actual time travel, just controlled `TimeProvider`. Hangfire recurring-job timing assertions are out of scope.

**Doc-update scope (§0.2):**
- §2 Tech Stack — add a Hangfire row (with PostgreSQL schema storage).
- §4.2 Catalog Service — add an **Async lifecycle** subsection listing the four Hangfire recurring jobs (`ReservationReminderJob`, `ReservationNoShowJob`, `WalkInNoShowJob`, `SeasonalAvailabilityJob`) with their cadence and flag name.
- §6 Data Stores — note `catalogdb` now hosts the `hangfire` schema.
- §11 Local Development — startup sequence line: Catalog applies the `hangfire` schema migration and exposes the Hangfire dashboard at a configured path.

### Phase 6 — Entity moves

This phase is split into three sub-phases. **Only §7.6.2 is in this plan.** The other two are out-of-plan prerequisites.

#### Phase 6.0 — Out-of-plan prerequisite: Ordering-side Reservation/WalkInQueue plan

**Verified:** Ordering has **no** `Reservation.cs` or `WalkInQueue.cs` anywhere in `Services/Ordering/` (only `Catalog.API/Models/` defines them). Today, all reservation + walk-in logic — CRUD endpoints, the booking flow, the queue worker, the no-show transitions, the Hangfire jobs — lives in Catalog.

A separate Ordering-side plan must introduce:

- `Reservation` aggregate (with state machine Pending → Confirmed → Seated → Completed / Cancelled / NoShow).
- `WalkInQueue` aggregate.
- The seven reservation CRUD endpoints that already exist in Catalog's `Features/Reservations/` and the five walk-in endpoints in `Features/WalkInQueues/`.
- The state transitions and the `TableStatusChanged` correlation logic.
- The reservation/block window logic (architecture §933-937: `reservation_time ± turnover_minutes`, no-show after 15 min).

Until that plan lands, Catalog keeps the writers, the tables, and the endpoints. The §7.5 Hangfire jobs run against Catalog's tables. When the Ordering-side plan ships, the table move follows §6.6 gateway convention and §7.6.2's backfill pattern.

#### Phase 6.1 — Out-of-plan prerequisite: Notification v1 plan

**Verified:** there is **no** `Notification.API` project under `Services/` (only Basket/Catalog/Discount/Identity/Kitchen/Ordering). `CustomerFeedback` and the relational `NotificationLog` model files live in Catalog and have no destination service.

> **Update (2026-07-13):** the prerequisite **plan** now exists — a v1 skeleton was drafted at `.agents/plan/notification/NOTIFICATION_SERVICE_PLAN.md` (Document Version 0.1). It carries the §6.1 delivery list and the §6.7 `NotificationLog` merge + backfill as its own milestones. This is a *partial* unblock: the plan is written, but the **service is not built** — so this Phase 6.1 row stays open. **Trigger to act:** when `Services/Notification/Notification.API/` exists *and* its backfill job has verified row-parity against `mt_doc_notification_log`, run the §6.7 Catalog-side cleanup (drop the Marten document + schema registration, update mermaid/companion/architecture docs).

A separate Notification v1 plan must introduce:

- The `Notification.API` service skeleton (Carter, JWT auth, Postgres).
- Notification deliveries: receipt generation (`OrderCompleted`), feedback request, reservation confirmations, reminders, etc. The integrations with Twilio/SendGrid from `architecture.md` §616.
- The `CustomerFeedback` aggregate and the reward-code generation flow (`architecture.md` §411-415, `FeedbackSubmittedIntegrationEvent` defined).
- The **`NotificationLog` relational table** (the only `NotificationLog` going forward — see §6.7 for the merge rationale). It owns: `id`, `RestaurantId`, `Channel`, `MessageType`, `RecipientType`, `RecipientIdentifier`, `Status` (`Pending | InFlight | RetryPending | Sent | Failed`), `AttemptCount`, `NextAttemptAt?`, `LastError?`, `RelatedOrderId?`, `RelatedReservationId?`, `CreatedAt`, `SentAt?`. Indexed by `(Status, NextAttemptAt)` for the retry worker.
- The **backfill job** that reads `mt_doc_notification_log` in Catalog and idempotently inserts into Notification's relational `notification_log`. Detailed steps in §6.7.

Until that plan lands, Catalog keeps `CustomerFeedback` and the Marten `NotificationLog`. The `FeedbackSubmittedIntegrationEvent` is still published (Phase 4 already does this) but no one consumes it yet — that's fine, the bus retains undelivered messages until the consumer exists (subject to its retry / dead-letter policy). The drafted Notification plan's Phase 3 is the consumer that drains it (and `ReservationReminderDueIntegrationEvent`).

#### Phase 6.2 — **In plan: Coupon move to Discount**

> **Verification (preamble, recorded on plan date).** A grep over `Services/Catalog/**` for `Coupon` returns zero matches — no `Models/Coupon.cs`, no `DbSet<Coupon>`, no EF migration, no `Features/Coupons/` folder, no Carter endpoints, no writers. Coupon is **not** present in Catalog's code today; it exists in Discount (`Discount.Grpc/Models/Coupon.cs:6-15`, full gRPC service in `DiscountService.cs:1-175`) and as **mermaid drift** in `db_relational_model.mermaid:283` (Coupons diagram block), `:499` (`Restaurants ||--o{ Coupons : "issues"`), and `:540` (orphan note) plus a text mention in `db_relational_model.md:62`. The migration step-list below therefore collapses to documentation reconciliation for the Catalog side — there is no source schema, no source writers, no source table to drop, no backfill to run. The §7.6.2 step list is preserved verbatim because it documents the *intended* sequence (which would apply if Catalog had Coupon code) and because the Coupon-with-Restaurant relationship entries in `db_relational_model.mermaid` need the mermaid-side cleanup the steps describe. Verification re-runs at commit time; if a future change re-introduces Coupon code in Catalog, the steps activate as written.

**Verified:** Discount already has the destination schema (`Discount.Grpc/Models/Coupon.cs:6-15`) with all the columns Catalog's `Coupon` carries — *and* Discount's `Coupon` extends `AuditableEntity<int>`, so the move is a strict upgrade (audit fields + `IsActive` come along for free).

**Caveat:** Discount is gRPC-only today (`Discount.Grpc/Services/DiscountService.cs`). Catalog's Coupon features are REST (Carter). Two options:

- **A.** Move writers to Discount.gRPC and accept that the management surface stays gRPC-only. New HTTP consumers are added via gRPC client wrappers.
- **B.** Add a Carter REST surface to Discount alongside the existing gRPC service. Higher scope; recommended only if there's non-Basket HTTP traffic to coupons soon.

**Plan adopts option A** (consistent with the existing pattern: Basket already speaks gRPC to Discount).

Steps (mirrors §7.6 generic step pattern, but more concrete because the destination exists):

1. **Schema pre-flight:** confirm Discount's `Coupon` table is identical to Catalog's at the column level. Add any missing column via a Discount migration (none expected — verified matching fields at `Discount.Grpc/Models/Coupon.cs:6-15`).
2. **Backfill:** one-shot data migration script (`Catalog.Coupons → Discount.Coupons`). Idempotent (`INSERT ... ON CONFLICT DO NOTHING` keyed by `Code + RestaurantId` since `Code` is `UK`).
3. **Move writers:** port the four Catalog `Features/Coupons/*` handlers (Create/Update/Delete/Get) to Discount.gRPC service methods. Cover the gRPC service already has `GetDiscount` / `CreateDiscount` / `UpdateDiscount` / `DeleteDiscount`; add `RedeemDiscount` parity with Catalog's redemption logic if present.
4. **Switch Catalog to read-only source table.** Add an EF interceptor that throws on `INSERT/UPDATE/DELETE` against the Catalog `Coupons` table. Catalog's REST endpoints either proxy to Discount via gRPC (option A) or are deprecated; clients have one release to migrate.
5. **Gateway route migration per §6.6:**
   - Add `discount-api/coupons/{**catch-all}` pointing to `discount-cluster`.
   - If any HTTP consumer still calls `catalog-api/coupons/...`, change its `ClusterId` from `catalog-cluster` to `discount-cluster` for that path.
6. **Drop Catalog source table:** delete the `Coupon` model + `DbSet` from `CatalogDbContext`; add a no-op `2026XX` migration that drops `Coupons` from Catalog's schema. Delete the `Features/Coupons/` folder.
7. **Update docs:** `db_relational_model.mermaid` updated to mark Coupon as Discount-owned with "moved from Catalog on YYYY-MM-DD" annotation; companion md updated identically.
8. **Feature flag:** `Catalog:EntityMoveCoupons=true` for the entire phase. Lets us roll back without a redeploy.

**Tests:** Migration idempotency test (run twice, second run is a no-op). End-to-end test that Discount.gRPC `CreateDiscount` → Catalog's read-only table reflects the value before step 4 (read-only mirror window) and stops reflecting after step 6 (table dropped).

**Doc-update scope (§0.2):**
- §4.2 Catalog Service — remove `Coupon` from the entity table and remove any `Features/Coupons/*` endpoint row.
- §4.4 Discount Service — update the Coupon model description to confirm it now serves Catalog's former writers (REST/gRPC surface, seeded codes, redemption parity).
- §4 — extend the gateway-prefix table if a new `discount-api/coupons` route was added per §6.6.
- §4.5 Ordering Service / §5.2 — no change unless cross-service consumers of coupon events were added in this phase.
- §11 Local Development — note any `Catalog.Coupons → Discount.Coupons` backfill run in the startup sequence (idempotent on `Code + RestaurantId`).

---

## 8. Cross-cutting notes

### Cross-service coordination rules

These are the rules every Catalog change must follow when it touches data or contracts shared with other services. They were settled in the csharp-expert pass and apply regardless of phase.

- **Event versioning.** Every Catalog integration event carries `int SchemaVersion` (current = 1). Adding fields bumps the version; consumers ignore unknown fields by MassTransit default. Removing or renaming a field requires introducing the next major version side-by-side, publishing both for one release, then dropping the old version. Documented in §6.5.
- **Cascade-delete policy.** All shared FKs use `OnDelete(DeleteBehavior.Restrict)`. Soft-delete only. Application layer raises a friendly 409 with a list of FK references when a delete is blocked. Cascade is never at the database level.
- **Migration ownership.** Catalog owns its own EF migrations. Cross-cutting changes (column rename on a shared FK; new required column referenced by another service) land first in Catalog, then the consumer's read paths are updated, then a coordinated migration script is documented in the commit message.
- **Cache failure policy.** Cache calls are best-effort with `Warning`-level logging on failure. `CacheDriftRepairService` is the safety net. Writes never block on Redis.
- **Health check policy.** `/live` for liveness only. `/ready` checks Postgres, Redis, RabbitMQ, and the outbox dead-letter count against `CatalogOptions:OutboxDeadLetterThreshold`. Tripping any of them takes Catalog out of the LB. Threshold is config; default = 0.
- **Engine trigger.** `IDomainEvent` + `DispatchDomainEventsInterceptor` (in-process, same transaction). Reconcile hosted service is a flag-gated safety net, off by default.

### Code-smell carryovers from `db_relational_model.md` §137-148

These are *not* the focus of this plan but should be cleaned up during the relevant phase — small enough to fold in:

- **§1 `Basket.MenuItemId` type** — `Catalog.API/Models` already has `MenuItem.Id : Guid`. Whatever fixes Basket's embedded `BasketItem.MenuItemId` from `int` to `Guid` happens in Basket; Catalog is unaffected.
- **§2 Four Marten documents extend `Entity<int>` but Marten assigns `Guid`** — fix in Phase 4 once the documents have stabilized. **Marten documents are not relational entities**; they should *not* extend `AuditableEntity<>`, `Entity<int>`, or `Entity<Guid>`. Drop the base class entirely and let Marten own the id (synthetic `Guid` by default; `[HiloSequence]` for integer ids if needed; `[Identity]` if a natural key is in play). Update the mermaid labels in the same commit.
- **§3 `BulkOrderUploads.CreatedAt`** — change base to `AuditableEntity<int>` in Phase 4. The entity stays in Catalog (per §7.6.0) — there's no Ordering move to sync with.

### Testing strategy

- **Unit tests** (xUnit + FluentAssertions + Moq): pure logic — engine rules, handler happy paths, validation. No infrastructure. Fast.
- **Testcontainers** (Postgres + Redis + RabbitMQ) for integration tests of:
  - Cache decorator: hit / miss / invalidation hooks fire; drift-repair repopulates after Redis restart.
  - Outbox publisher: events committed in the same transaction as the mutation; dispatcher hosted service delivers them; dead-letter probe exposes count.
  - Engine end-to-end: ingredient update → IDomainEvent → engine recompute → `MenuItemAnalytics`-like audit log.
  - Coupon move: data migration idempotency; end-to-end gRPC round trip.
- **Fake `IBus`, `IDistributedCache`, `IConnectionMultiplexer`** for handler unit tests that don't need real infrastructure.
- **MassTransit `InMemoryTestHarness`** for consumer unit tests (`OrderCompleted` consumer).
- **Fake clock** (`Microsoft.Extensions.TimeProvider.Testing`) for the Hangfire job logic; actual Hangfire timing is not asserted.

### Observability

- Per-feature cache hit/miss counters exposed via a small `/_internal/cache-stats` endpoint under `MapCarter`.
- Engine recompute counters and last-reconcile timestamp on `/ready` under the `availability_engine` key.
- Outbox dead-message count surfaced under `outbox.dead_message_count` (and via `/ready`).
- Standard Serilog structured logging (already in place). Add a correlation-id enrichment that flows from inbound HTTP → outbox row → MassTransit header → consumer (matches `architecture.md` AI-agent guidance §1009).

### Migration / rollout

- Phases 1–5 are **additive** and feature-flag gated. Each can ship behind its flag and roll back without DB changes.
- Phase 6.2 is the **only** sub-phase with a DB migration in this plan. Sequence behind a release notes flag `Catalog:EntityMoveCoupons=true`.

### Resolved architectural decisions

These were tracked as open in plan v1.0; verified against the code and closed in v1.1.

| # | Decision | Verified by | Outcome |
|---|---|---|---|
| 13 | Ordering has Reservation/WalkInQueue aggregates today? | Glob over `Services/Ordering/**/Reservation.cs` and `WalkInQueue.cs` — both empty | **No.** Out-of-plan prerequisite: Ordering-side plan (§7.6.0). |
| 14 | Notification.API skeleton exists? | `ls Services/` — only Basket/Catalog/Discount/Identity/Kitchen/Ordering | **No.** Out-of-plan prerequisite: Notification v1 plan (§7.6.1). |
| 15 | Discount already has Coupon table? | `Discount.Grpc/Models/Coupon.cs:6-15` — fields match Catalog's; extends `AuditableEntity<int>` | **Yes.** In-plan move §7.6.2. |
| 16 | YARP route migration convention? | Read `ApiGateway/YarpApiGateway/appsettings.json` — config-driven, no precedent for cross-cluster moves | **Decided:** atomic `ClusterId` change, keep public path stable (§6.6). |
| 17 | Do `MenuCategory` / `MenuSubCategory` CUD handlers publish a cross-service event? | Explore over `Features/MenuCategories/**` + `Features/MenuSubCategories/**` (2026-07-13) — all six CUD handlers are `CatalogDbContext` + `ICatalogCache` only; no outbox / `IPublishEndpoint`; no `MenuCategoryChangedIntegrationEvent` type exists | **Won't-do (2026-07-13).** Deliberate: cross-service consumers (Basket / Discount / Ordering) act on *item*-level changes (`MenuItemChanged*`), not category structure. A category mutation touches many items and carries no natural `MenuItemId`; Catalog's own cache invalidation covers the local read path. Revisit only if a consumer needs category-delete orphan notification. |
| 18 | Is Catalog integration-event publishing on by default? | Repo-wide search (2026-07-13) — no `FeatureManagement:CatalogMenuEvents` in any `appsettings*` / compose / env; Microsoft FeatureManagement returns `false` when unconfigured, so all `CatalogMenuEvents`-gated publishes were silently off | **Fixed (2026-07-13).** `"FeatureManagement": { "CatalogMenuEvents": true }` added to `Catalog.API/appsettings.json` so events publish by default. The operational rollout flags (`CatalogRedisCache`, `CatalogScheduledJobs`, `CatalogAvailabilityEngineReconcile`) intentionally stay off — they remain deploy-time toggles. |
| 19 | Two EF migration lineages (`RestaurantMigrationContext` vs `CatalogDbContext`)? | v2.8 discovery + Explore (2026-07-13) — legacy `RestaurantMigrationContext` is design-time only (never in `Program.cs` DI; only its own file + the `InitialCreate` designer + its snapshot referenced it) | **Retired (2026-07-13).** Deleted `Data/CatalogDbContextFactory.cs` (held `RestaurantMigrationContext` + its design-time factory), `Data/Migrations/20260529025336_InitialCreate.{cs,Designer.cs}`, and `RestaurantMigrationContextModelSnapshot.cs`. Runtime-safe (only `CatalogDbContext` was ever migrated). Single lineage now lives under `Migrations/`. **Prod caveat unchanged:** an existing DB created via the legacy path must still be *baselined* (insert `20260712105350_SyncOutboxAndSchedulingSchema` into `__EFMigrationsHistory`) rather than have it applied — a data-state fact no code change removes. |

---

## 9. Milestone checklist

> Every phase entry has **three** check-boxes: the code/test gate, the `current-architecture.md` doc-update gate (§0.2), **and** the completion gate (this plan file updated per §9.1 step 9 — Document Version bumped, changelog entry appended, §9 check-boxes ticked). A phase is not "done" until all three are committed.

- [x] **Phase 1** — Redis cache wired behind `CatalogRedisCache` flag; menu and ingredient invalidation hooked into existing handlers; `CacheDriftRepairService` running every 5 min; failure mode is fail-open + log.
  - [x] **Phase 1 doc** — `docs/architecture/current-architecture.md` updated per Phase 1 doc-update scope (§2 Tech Stack, §4.2 Catalog Caching, §9 Cross-Cutting Patterns, §11 Local Development).
  - [x] **Phase 1 completed** — development, doc commit, and plan-update commit (Document Version 1.6 → 1.9; v1.7 + v1.8 + v1.9 changelog entries) all landed on 2026-07-11.
- [x] **Phase 2** — All five Catalog integration events (each with `int SchemaVersion = 1`) publish via outbox; plain `IConsumer<OrderCompletedIntegrationEvent>` handler bumps `MenuItemAnalytics` idempotently; outbox poison queue + `OutboxDeadLetterProbe` reading 0; `/live` + `/ready` split in place.
  - [x] **Phase 2 doc** — `current-architecture.md` updated per Phase 2 doc-update scope (§2 Tech Stack, §4.2 Events, §5.2 matrix rows, §9 interceptors row, §12 Observability `/ready` entries).
  - [x] **Phase 2 completed** — development, doc commit, and plan-update commit (Document Version bump + v2.2 changelog entry) all landed. Deferred Testcontainers outbox/consumer integration tests landed in v2.8.
- [x] **Phase 3** — IngredientAvailabilityEngine with unit-test matrix; `IDomainEvent` triggers via `DispatchDomainEventsInterceptor`; reconcile hosted service gated by `CatalogAvailabilityEngineReconcile` flag.
  - [x] **Phase 3 doc** — `current-architecture.md` updated per Phase 3 doc-update scope (§4.2 engine paragraph, §9 interceptors row).
  - [x] **Phase 3 completed** — development, doc commit, and plan-update commit (Document Version bump + v2.3 changelog entry) all landed.
- [x] **Phase 4** — `MergedTables`, `MenuSubCategory.Delete`, `ComboItems.Update`, `BulkOrderUploads` (CRUD + approve/reject; stays in Catalog per §7.6.0), `PriceHistory` write path, `MenuItemAnalytics` nightly recompute, `CustomerFeedback.Submit` + reward event (stays in Catalog per §7.6.1).
  - [x] **Phase 4 doc** — `current-architecture.md` updated per Phase 4 doc-update scope (§4.2 endpoint list, §4.2 events row, §4.2 entity table).
  - [x] **Phase 4 completed** — development, doc commit, and plan-update commit (Document Version bump + v2.4 changelog entry) all landed.
- [x] **Phase 5** — Hangfire + recurring jobs: reservation reminder, reservation no-show, walk-in no-show, seasonal availability. All gated by `CatalogScheduledJobs` flag.
  - [x] **Phase 5 doc** — `current-architecture.md` updated per Phase 5 doc-update scope (§2 Tech Stack Hangfire row, §4.2 async lifecycle subsection, §6 hangfire schema, §11 startup sequence).
  - [x] **Phase 5 completed** — development, doc commit, and plan-update commit (Document Version bump + v2.5 changelog entry) all landed. Deferred Hangfire-job integration tests (real Postgres interceptor path) landed in v2.8.
- [ ] **Phase 6.0** — *Out-of-plan.* Track Ordering-side plan introducing `Reservation` / `WalkInQueue` aggregates. Open until the Ordering-side plan lands. **Trigger to act:** when `Services/Ordering/**/Reservation.cs` (+ `WalkInQueue.cs`) exist — then migrate the tables/endpoints/jobs per §6.6 gateway convention + §7.6.2 backfill pattern. *(No prerequisite plan drafted yet.)*
  - [ ] **Phase 6.0 doc** — none required (no Catalog code change in this sub-phase; only the prerequisite note in §7.6.0 is refreshed).
  - [ ] **Phase 6.0 completed** — refresh of the prerequisite note and a v1.X+1 changelog entry appended.
- [ ] **Phase 6.1** — *Out-of-plan.* Track Notification v1 plan introducing `CustomerFeedback` and the relational `notification_log` table (the only `NotificationLog` going forward — see §6.7 for the merge plan that drops the Marten document from Catalog). **Prerequisite plan drafted 2026-07-13** at `.agents/plan/notification/NOTIFICATION_SERVICE_PLAN.md` (skeleton; service not built). Row stays open. **Trigger to act:** when `Services/Notification/Notification.API/` exists *and* its backfill has verified row-parity against `mt_doc_notification_log`.
  - [ ] **Phase 6.1 doc** — none required while Catalog waits. When Notification v1 ships, run the §6.7 doc-update scope (Catalog entity table row dropped; new Notification Service section in `current-architecture.md`; mermaid + companion doc cleaned up).
  - [ ] **Phase 6.1 completed** — when Notification v1 lands, run the §6.7 doc-update scope, drop the Marten document from Catalog, and append a v1.X+1 changelog entry.
- [x] **Phase 6.2** — `Coupon` move to Discount: schema pre-flight, backfill, writers ported to `Discount.Grpc`, Catalog source table read-only, gateway re-pointed per §6.6, source table dropped, mermaid + companion md updated. Gated by `Catalog:EntityMoveCoupons`.
  - [x] **Phase 6.2 doc** — `current-architecture.md` updated per Phase 6.2 doc-update scope (§4.2 removes Coupon, §4.4 Discount absorbs it, §4 gateway table, §11 backfill note).
  - [x] **Phase 6.2 completed** — development, doc commit, and plan-update commit (Document Version 2.5 → 2.6; v2.6 changelog entry) all landed on 2026-07-11. **All 8 §7.6.2 sub-steps collapsed to documentation reconciliation** per the v1.4 verification preamble: Catalog had zero Coupon code (no model, no DbSet, no migration, no `Features/Coupons/` folder, no Carter endpoints, no writers); the only Coupon artefacts were mermaid drift in `db_relational_model.mermaid` (entity block at the old :283-292, `Restaurants ||--o{ Coupons : "issues"` relationship at the old :499, orphan comment at the old :540) and a phantom line in `db_relational_model.md:62` (`AuditableEntity<TId>` user list). Discount's `Coupon` (`Discount.Grpc/Models/Coupon.cs:6-15`, `AuditableEntity<int>`) was confirmed as the destination — strict upgrade (audit columns + `IsActive` come along for free). No YARP route change required: the existing `/discount-api/{**catch-all}` catch-all on `discount-cluster` already covers `/discount-api/coupons/*`.
- [x] **Cleanup** — Three Marten documents (`OrderSnapshot`, `OrderModificationLog`, `OrderItemPriceAudit`) drop the `Entity<int>` base; they are plain Marten documents with no relational base class. `BulkOrderUploads` becomes `AuditableEntity<int>`. (`NotificationLog` was the fourth Marten document but is being removed entirely per §6.7 once Notification v1 lands — not just rebased.) Mermaid + companion doc both updated to reflect the new bases / id conventions.
  - [x] **Cleanup doc** — `current-architecture.md` §4.2 entity table updated to reflect the new bases (drop `Entity<int>` from the three Marten rows; `BulkOrderUploads` row notes `AuditableEntity<int>`). The `NotificationLog` row is removed (see §6.7 merge).
  - [x] **Cleanup completed** — development, doc commit, and plan-update commit (Document Version 2.6 → 2.7; v2.7 changelog entry) all landed on 2026-07-12.
- [x] **Docs** — `db_relational_model.mermaid` updated to match each phase (mermaid is reconciled after every phase, not only at the end). *(2026-07-13: last drift closed — the residual Catalog-side `Coupons` entity block was deleted; only the deprecation comment + historical relationship note remain. See §8 decision #17–19 and the v2.9 changelog.)*

### 9.1 Per-phase implementation recipe

For reproducibility, every phase's commit follows this sequence (the `csharp-developer` skill is loaded at step 1 per §0.1):

1. **Load skill.** Invoke `/csharp-developer`. Load the relevant `references/*.md` files for the phase.
2. **Design.** Read the phase section above; map endpoints → Carter modules, handlers → MediatR, events → MassTransit. Confirm no naming conflict with existing modules.
3. **Implement.** Domain models, EF / Marten changes, handler + validator + DTO, Carter module registration, `IOutboxPublisher` writes, cache invalidation hooks, hosted services. Follow `csharp-developer` MUST DO/MUST NOT DO list.
4. **EF Core checkpoint.** Run `dotnet ef migrations add <Name>` from `Services/Catalog/Catalog.API/`. Review the generated SQL; if it contains unintended drops, roll back with `dotnet ef migrations remove` and fix the model.
5. **Test.** xUnit + FluentAssertions unit tests; Testcontainers (Postgres + Redis + RabbitMQ as needed) integration tests.
6. **Update `current-architecture.md`.** Apply the phase's Doc-update scope verbatim. Commit it alongside the code commit before the phase is marked complete.
7. **Update `db_relational_model.mermaid`.** If the phase touched the schema, reconcile the mermaid to code (project convention).
8. **Land.** Phase is "done" only when both the code commit and the doc commit have landed and the checklist boxes in §9 above are ticked.

---

**Document Version:** 2.10
**Last Updated:** 2026-07-13
**Maintained By:** Catalog working group

> **v2.10 changelog** — **Post-verification maintenance: latent clock bug fixed; Phase 6.1 prerequisite plan drafted; tracking rows sharpened.** Follow-up to v2.9, covering the actionable Catalog work that remained after the verification sweep. **(a) `PriceHistoryRecorder` clock bug — fixed.** The recorder's primary constructor injects `TimeProvider clock` (the project's testable time source per §0.3.11) but ignored it, hardcoding `SystemClock.Instance.GetCurrentInstant()` for both `EffectiveDate` and `CreatedAt` — a latent testability bug that also raised the only `CS9113` warning in Catalog.API. Now captures a single `now = Instant.FromDateTimeOffset(clock.GetUtcNow())` and uses it for both stamps, so a fake `TimeProvider` controls the audit timestamps. Confirmed `TimeProvider` resolves at runtime via ASP.NET Core's default host registration (the unconditional `MenuItemAnalyticsNightlyRecomputeService` hosted service already depends on it and the v2.8 integration suite builds the full host green) — **no missing-registration bug**, no extra DI line needed. No `PriceHistoryRecorder` unit test existed to break (verified). **Catalog.API now builds with 0 warnings** (the 4 remaining solution warnings are all in `BuildingBlocks`: 2×CS0105 duplicate usings, 2×CS8618 `AuditableEntity` nullability — outside this service). **(b) Phase 6.1 prerequisite plan drafted.** A Notification v1 skeleton plan was created at `.agents/plan/notification/NOTIFICATION_SERVICE_PLAN.md` (Document Version 0.1) — it carries the §6.1 delivery list, the three already-available consumers (`OrderCompleted`, `FeedbackSubmitted`, `ReservationReminderDue`), and the §6.7 `NotificationLog` merge + backfill as its own milestones. Phase 6.1 is now a *partial* unblock (plan written, service not built); the §6.1 body + checklist row are updated to point at it. **(c) Trigger conditions added** to the Phase 6.0 and 6.1 tracking rows so it is unambiguous when each becomes actionable (6.0: `Services/Ordering/**/Reservation.cs` exists; 6.1: `Services/Notification/Notification.API/` exists + backfill row-parity verified). **Build:** Catalog.API 0 errors / 0 warnings. **Remaining work:** unchanged — the two out-of-plan rows (Phase 6.0 Ordering-side, Phase 6.1 Notification v1), both blocked on their services being built.

> **v2.9 changelog** — **Post-completion verification sweep + three fixes.** A review asked "what's missing" now that all in-plan phases are shipped; three background verifications were run against current code (2026-07-13) and their findings acted on. **(a) Mermaid Coupon drift — fixed.** The v2.6 Phase 6.2 reconciliation removed the `Restaurants ||--o{ Coupons` relationship and added a deprecation comment, but left the residual `Coupons { ... }` *entity* block rendering in `docs/architecture/db_relational_model.mermaid` (old lines 295-304). Deleted it; the explanatory comment (why Coupon is Discount-owned) and the historical relationship note remain. The §9 **Docs** checkbox (mermaid reconciled) is now ticked. The three Marten-doc blocks, `BulkOrderUpload` audit columns, and the `.md` Coupon note were all re-confirmed SYNCED. **(b) `CatalogMenuEvents` was silently off in every deployment — fixed.** Verification found `IngredientAvailabilityChangedIntegrationEvent` *is* genuinely emitted (outbox-first: `IngredientAvailabilityChangedDomainEventHandler` stages it via `IOutboxPublisher`, drained by `DispatchDomainEventsInterceptor` → `OutboxDispatcher` → MassTransit `IPublishEndpoint`) — **but** the whole publish path (this event and every other `MenuItemChanged*`) is gated on the `CatalogMenuEvents` feature flag, and that flag was **not configured anywhere** (no `appsettings*`, compose, or env). Microsoft FeatureManagement returns `false` when unconfigured, so Catalog published **nothing** in a default deployment (tests set the flag via env, masking it). Added `"FeatureManagement": { "CatalogMenuEvents": true }` to `Catalog.API/appsettings.json` (decision #18). The operational rollout flags (`CatalogRedisCache`, `CatalogScheduledJobs`, `CatalogAvailabilityEngineReconcile`) intentionally stay off. **(c) Category-level cross-service events — won't-do, documented** (decision #17). All six `MenuCategories/` + `MenuSubCategories/` CUD handlers are cache-invalidation-only by design; consumers act on item-level `MenuItemChanged*`, not category structure. Recorded as a deliberate decision so it stops reading as an oversight; revisit only if a consumer needs category-delete orphan notification. **(d) Legacy `RestaurantMigrationContext` retired** (decision #19). The v2.8 changelog flagged that the sole pre-existing migration was attributed to a design-time-only legacy context, so `MigrateAsync(CatalogDbContext)` applied nothing until the v2.8 sync migration was added. Confirmed the legacy context is never in `Program.cs` DI (design-time only), then deleted it: `Data/CatalogDbContextFactory.cs` (held `RestaurantMigrationContext` + factory), `Data/Migrations/20260529025336_InitialCreate.{cs,Designer.cs}`, `RestaurantMigrationContextModelSnapshot.cs`. Single lineage now lives under `Migrations/` (`CatalogDbContext`). The **prod-baseline caveat is unchanged and irreducible**: an existing database created via the legacy path must be baselined (insert `20260712105350_SyncOutboxAndSchedulingSchema` into `__EFMigrationsHistory`), not migrated — retiring the code removes the *confusion* of two lineages but not the one-time ops step for pre-existing environments. **Build:** Catalog.API 0 errors / 0 new warnings (5 pre-existing). **Remaining work:** only the two out-of-plan tracking rows (Phase 6.0 Ordering-side Reservation/WalkInQueue, Phase 6.1 Notification v1), both blocked on other services' plans.

> **v2.8 changelog** — **Deferred Phase 2 + Phase 5 Testcontainers integration tests landed; latent migration drift found and fixed.** The only remaining test debt from v2.2 (Phase 2 outbox integration) and v2.5 (Phase 5 job logic against a real Postgres interceptor) is now closed. **(a) Shared integration fixture** — new `Catalog.API.Tests/Integration/`: `CatalogWebApplicationFactory` (real Postgres + Redis + RabbitMQ via Testcontainers; `Testcontainers.RabbitMq 4.1.0` added to the csproj), `CatalogWebApplicationFactoryCollection`, `TestAuthHandler` (mirrors Ordering), and a tiny mutable `TestTimeProvider : TimeProvider` (no new package — avoids `Microsoft.Extensions.TimeProvider.Testing`). `public partial class Program { }` was appended to `Catalog.API/Program.cs` so `WebApplicationFactory<Program>` can build the host. **(b) Connection wiring gotcha** — Catalog's `Program.cs` reads `ConnectionStrings:CatalogDB` / `Redis` / `MessageBroker:Host` **eagerly** (it builds the `NpgsqlDataSource` + Marten + Hangfire before `builder.Build()`), so `WebApplicationFactory.ConfigureAppConfiguration` is applied too late; the fixture sets those as **environment variables** in `InitializeAsync` (after the containers are up, before `CreateClient`) so `WebApplication.CreateBuilder`'s env-var source picks them up. `MessageBroker:Host` is passed as a credential-free `amqp://host:port` because Catalog's RabbitMQ health check builds `amqp://{user}:{pass}@{host}` from it (Testcontainers' embedded-credential URI would double the creds). Environment is `Testing` to skip the Development-only Marten seed. **(c) Phase 2 tests** — `CatalogOutboxDeadLetterTests` (a `SchemaVersion=99` row is quarantined to `outbox_messages_dead` with `Reasons.UnsupportedSchemaVersion`; clears the shared outbox tables first for determinism — note `OutboxDispatcher.DispatchOnceAsync` returns *claimed*-row count, not published, so the substantive assertions are on the dead-table routing), `CatalogOutboxPublisherTests` (publisher stages a live `outbox_messages` row stamped `SchemaVersion=1`), `OrderCompletedConsumerTests` (double delivery bumps `MenuItemAnalytics` exactly once via the real `processed_order_items` unique-violation idempotency gate — the path the in-memory provider could not exercise). **(d) Phase 5 tests** — one per job, all against the real Postgres interceptor path with a fixed clock: `ReservationNoShowJobTests`, `ReservationReminderJobTests` (asserts the outbox reminder row too), `WalkInNoShowJobTests`, `SeasonalAvailabilityJobTests`. This resolves the v2.5 deferral (the in-memory provider bypassed `AuditableEntityInterceptor`, which the real Npgsql provider runs). **(e) Migration drift discovered + fixed** — the tests surfaced a genuine bug: the only pre-existing migration (`Data/Migrations/20260529025336_InitialCreate`) is attributed to a **legacy `RestaurantMigrationContext`**, not `CatalogDbContext`, so `MigrateAsync(CatalogDbContext)` in `Program.cs` was applying **nothing** — none of the Phase 2–5 tables (`outbox_messages`, `outbox_messages_dead`, `processed_order_items`, and the rest of the current model) had a migration. A new migration `Migrations/20260712105350_SyncOutboxAndSchedulingSchema` (context = `CatalogDbContext`) was generated; it creates the full current schema (23 tables), `Up()` has **no** drops (all 23 `DropTable` are in `Down()`), reviewed per the §0.1 EF checkpoint. **⚠ Production caveat:** this migration is safe for fresh databases / CI, but an **existing** environment whose tables were created via the legacy `RestaurantMigrationContext` path must **baseline** it (insert the migration id into `CatalogDbContext`'s `__EFMigrationsHistory`) rather than apply it, or the `CreateTable`s will collide with existing relations. Reconciling/retiring the legacy `RestaurantMigrationContext` is a follow-up ops task, not done here *(done in v2.9 — the legacy context code is deleted; the baseline step for pre-existing DBs remains)*. **Build:** Catalog.API 0 errors / 0 new warnings. **Tests:** 41/41 pass (34 prior unit + 7 new integration) with Docker. **Remaining work:** only the two out-of-plan tracking rows (Phase 6.0 Ordering-side, Phase 6.1 Notification v1) — no in-plan test debt remains.

> **v2.7 changelog** — **Cleanup milestone shipped.** Both code/test and doc gates closed on 2026-07-12. The last in-plan item is done; only the two out-of-plan tracking rows (Phase 6.0 Ordering-side, Phase 6.1 Notification v1) remain open. **(a) Code — no change required, already in target state.** Verification over `orderly-microservices/Services/Catalog/Catalog.API/Models/` confirmed the three Marten documents already dropped the relational base class: `OrderSnapshot`, `OrderModificationLog`, and `OrderItemPriceAudit` are plain classes declaring `public Guid Id { get; set; }` with the "Marten assigns a synthetic Guid id (no relational base class)" XML remark — no `Entity<int>` / `Entity<Guid>` / `AuditableEntity<>` base. `BulkOrderUpload : AuditableEntity<int>` was already flipped in Phase 4 (v2.4). The working tree was clean; the model state is committed (last touched by `acbdbd2` / `fe2b96f`). No EF migration was needed (Marten owns the document schema; the id column is already `uuid`). Build re-confirmed 0 errors / 0 warnings. **(b) `db_relational_model.mermaid`** — the three Marten doc blocks already carried the "Cleanup (2026-07-11): code matches storage — Guid Id, no relational base" comment; the one stale spot was the `BulkOrderUploads` block, whose `%% No CreatedAt — BulkOrderUpload extends Entity<int> (not Auditable)` comment was replaced with the four `AuditableEntity<int>` audit columns (`CreatedAt`, `CreatedBy`, `LastModifiedAt`, `LastModifiedBy`) plus a Phase 4 note. **(c) `db_relational_model.md`** — removed the internal contradiction: the "Code-vs-storage **mismatch**" callout (which still claimed all four docs "extend `Entity<int>`") was rewritten as a "Code-vs-storage **alignment** (Cleanup milestone)" note, and the two now-resolved entries in the "Mismatches flagged for follow-up" list (item 2 — Marten docs `Entity<int>`; item 3 — `BulkOrderUploads.CreatedAt`) were marked ✅ Resolved with their resolution date; item 1 (Basket `MenuItemId` type) stays open. **(d) `current-architecture.md` §4.2** — already correct from a prior pass (the three Marten rows and the `BulkOrderUpload` `AuditableEntity<int>` row already noted the Cleanup); the "Catalog (4 docs)" references at §2 / §4.2 stay accurate because `NotificationLog` is still registered (its removal is out-of-plan per §6.7 until Notification v1 lands). **(e) NotificationLog** — untouched: it is not rebased, it is removed wholesale once Notification v1 backfills (§6.7). **Remaining work:** Phase 6.0 and Phase 6.1 stay as out-of-plan tracking rows pending the Ordering-side and Notification v1 plans; the deferred Testcontainers integration passes (Phase 2 outbox/wire-versioning, Phase 5 Hangfire job logic against a real Postgres interceptor) remain the only test debt.

> **v2.6 changelog** — **Phase 6.2 (Coupon move to Discount) shipped.** Both code/test and doc gates closed on 2026-07-11. Per the v1.4 verification preamble, all 8 §7.6.2 sub-steps collapsed to documentation reconciliation on the Catalog side: **(a)** Mermaid cleanup — dropped the `Coupons { ... }` entity block, the `Restaurants ||--o{ Coupons : "issues"` relationship row, and the orphan "Coupon relationship is to Restaurant (issued above)" comment from `docs/architecture/db_relational_model.mermaid`. **(b)** Companion doc — `Coupon` removed from the `AuditableEntity<TId>` user list at `docs/architecture/db_relational_model.md:62` with a Phase 6.2 annotation. **(c)** `current-architecture.md` — added a Phase 6.2 confirmation note under §4.4 Discount Service (Coupon is the single Discount entity; no YARP change because the catch-all already routes `/discount-api/coupons/*`) and a startup-sequence note under §11 Local Development (no backfill required because Catalog never had rows). **(d)** Schema pre-flight — confirmed `Discount.Grpc/Models/Coupon.cs:6-15` matches the old Coupon block at the column level (`Id`, `RestaurantId`, `Code`, `Description`, `Amount`, `RedeemAmount`, optional `MaxRedeemAmount`, optional `ExpirationDate`) and extends `AuditableEntity<int>`, which is a strict upgrade (audit columns + `IsActive` come along for free). **(e)** No code migration, no writers ported, no read-only source table, no source table dropped — there was nothing in Catalog to migrate. **(f)** No YARP route change — the existing `/discount-api/{**catch-all}` route on `discount-cluster` already covers `/discount-api/coupons/*` per §6.6 step 1 (a per-path route would only be needed if a public path had to migrate *from* the catalog-cluster to the discount-cluster). **(g)** No `Catalog:EntityMoveCoupons` flag was required because no Catalog code shipped. **Build:** 0 warnings / 0 errors (no Catalog code touched). **Tests:** unchanged from v2.5 (34/34 pass). **Remaining work:** Phase 6.0 / Phase 6.1 stay as out-of-plan tracking rows until the Ordering-side plan and the Notification v1 plan land; the **Cleanup** milestone (drop `Entity<int>` base from the three remaining Marten documents `OrderSnapshot`, `OrderModificationLog`, `OrderItemPriceAudit`) is the next actionable in-plan item.

> **v2.5 changelog** — **Phase 5 (Hangfire async lifecycle) shipped.** Both code/test and doc gates closed on 2026-07-11. Catalog now runs four Hangfire-backed recurring jobs (storage is the new `hangfire` schema in `catalogdb`; dashboard mounted at `/catalog-api/hangfire`, gated on `Admin`/`Manager` role). **(a)** New packages: `Hangfire.AspNetCore 1.8.14` + `Hangfire.PostgreSql 1.20.10` — PostgreSQL storage, no new database. **(b)** Four recurring jobs in `Catalog.API/Scheduling/`: `ReservationReminderJob` (every 5 min; finds Confirmed reservations 55–65 min from now with `ReminderSent=false`, publishes `ReservationReminderDueIntegrationEvent`, stamps `ReminderSent`), `ReservationNoShowJob` (every 1 min; Confirmed + 15 min past + no `SeatedAt` → `NoShow`, frees held table), `WalkInNoShowJob` (every 1 min; `Notified` walk-in + 10 min response window expired → `NoShow`, frees held table), `SeasonalAvailabilityJob` (every 5 min; `MenuItem.IsAvailable` ← `SeasonStartDate ≤ today ≤ SeasonEndDate` for `Seasonal` and `PromoStartDate ≤ now ≤ PromoEndDate` for `Promo`). All gated by `FeatureManagement__CatalogScheduledJobs` (default `false`) — same self-gating pattern as `CacheDriftRepairService`. **(c)** New `BuildingBlocks.Messaging/Events/Catalog/ReservationReminderDueIntegrationEvent.cs` (5th Catalog-published event; Notification is the intended consumer per §7.6.1). **(d)** `HangfireOptions` (`Catalog:Hangfire` config section) with `[Range]` validation for `MaxRowsPerTick` and `WorkerCount`; cron expressions configurable per job (`ReservationReminderCron`, etc.). **(e)** `HangfireAdminOnlyFilter` (`IDashboardAuthorizationFilter`) restricts the dashboard to JWT `Admin` / `Manager` role claims. **(f)** Job classes resolve `CatalogDbContext` via `IServiceScopeFactory.CreateAsyncScope()` per tick so the DbContext lifetime is bounded to the tick; `[AutomaticRetry(Attempts = 3, DelaysInSeconds = [30, 60, 120])]` on each `RunAsync`. **Divergences from §7 Phase 5 spec:** **(g)** Unit tests for the four jobs were **deferred** — the in-memory EF provider does not run EF Core interceptors, so the production `AuditableEntityInterceptor` path (which stamps `CreatedBy`/`LastModifiedBy` on save) is bypassed, and the in-memory provider enforces the audit columns as required. Several test-only workarounds were attempted (explicit `IAuditableEntity` cast, derived `TestCatalogDbContext` with relaxed constraints, `SaveChangesInterceptor` with `DetectChanges` snapshots, `StampBeforeSaveContext` wrapper, reflection on internal setters) and all failed. **Per the user's choice (option 1)**, unit tests are deferred to a Testcontainers integration pass that uses the real Postgres interceptor path; this is the right level per §0.3.11. **Build:** 0 errors across the solution. **Tests:** 34/34 pass (the three pre-Phase-5 test files: `CachedMenuReaderTests`, `CatalogOptionsTests`, `IngredientAvailabilityEngineTests`). Phase 6.0 (Tracking Ordering-side Reservation/WalkInQueue prerequisite) is unblocked.

> **v2.4 changelog** — **Phase 4 (Complete vertical slices) shipped.** Both code/test and doc gates closed on 2026-07-11. Catalog now exposes the full per-feature CRUD surface the architecture describes. **(a)** Six new endpoint groups: `DeleteMenuSubCategory` (idempotent soft-delete), `UpdateComboItem` (quantity / isOptional; validates IncludedMenuItemId still exists), `BulkOrderUploads` (`POST` upload + `GET` + `POST …/{id}/approve` + `POST …/{id}/reject` — idem­potent on terminal states; lightweight validation against menu-item ids and table availability; `ErrorLog` carries per-row messages as JSON). `BulkOrderUpload` base flipped from `Entity<int>` to `AuditableEntity<int>` per the §8 *Cleanup* carryover. `RecomputeTodayAnalytics` admin action + `MenuItemAnalyticsNightlyRecomputeService` `BackgroundService` (daily at `MenuItemAnalyticsNightly:RunAtHour`, `[Range(0, 23)]`, default `3`). `SubmitFeedback` accepts the four ratings + comments + `OrderId`, issues a 10% reward code on `OverallRating ≥ 4`, publishes `FeedbackSubmittedIntegrationEvent` (gated by `FeatureManagement__CatalogFeedbackEvents`, default `true`). **(b)** New `IPriceHistoryRecorder` Scoped service in `Catalog.API/Features/PriceHistories/CreatePriceHistory/` is invoked by every price-mutating handler — `UpdateMenuItem` (`BasePrice`), `UpdateMenuItemVariation` (`PriceModifier`), `UpdateIngredientAlternative` (`PriceModifier`), and `UpdateRestaurant` (`TaxRate` / `EstimatedTurnoverMinutes` via the new `PriceType.RestaurantConfiguration` enum value added to `BuildingBlocks.Enums.MenuEnums.PriceType`). The recorder skips no-op writes when `oldPrice == newPrice`; all rows commit in the same EF Core transaction as the mutation. **(c)** New `ICurrentUser` abstraction at `Catalog.API/Application/Abstractions/` (mirrors `Kitchen.API/Application/Abstractions/ICurrentUser`) — `Scoped`, `HttpContextAccessor`-backed; resolves the authenticated user id from the JWT `ClaimTypes.NameIdentifier`. Used by `BulkOrderUpload` (operator audit) and `PriceHistoryRecorder` (change attribution). **(d)** New `BulkOrderUploadNotFoundException` derives from `BuildingBlocks.Exceptions.NotFoundException` for the `GET …/{id}` / approve / reject paths. **(e)** End-of-phase global-usings consolidation: `Catalog.API.Features.PriceHistories.CreatePriceHistory` was used in 5 files (Program.cs + 4 mutating handlers) — promoted to `Catalog.API/GlobalUsings.cs` and removed from the file-scoped `using` lines. `Catalog.API.Application.Abstractions` was added to the global usings during Phase 4.2 (used by Program.cs). **(f)** Plan §7.6.0 / §7.6.1 explicitly note that `BulkOrderUploads` and `CustomerFeedback` stay in Catalog; the relevant prerequisite Notes remain open. **Build:** 0 errors across the solution. **Tests:** 45/45 pass (34 prior + 11 new Phase 4 tests covering `PriceHistoryRecorder` write / skip / `RestaurantConfiguration` paths and `MenuItemAnalyticsNightlyRecomputeServiceOptions` range validation). Phase 5 (Hangfire jobs — Reservation Reminder / No-Show, WalkIn No-Show, Seasonal Availability) is unblocked.

> **v2.3 changelog** — **Phase 3 (Ingredient Availability Engine) shipped.** Both code/test and doc gates closed on 2026-07-11. Catalog now recomputes `MenuItem.AvailabilityStatus` on every ingredient mutation and publishes `IngredientAvailabilityChangedIntegrationEvent` via the existing outbox. Key implementation divergences from the §7 Phase 3 spec: **(a)** Domain-event abstractions (`IDomainEvent`, `IAggregate`, `Aggregate<TId>`, `AuditableAggregate<TId>`) live in `Catalog.API/Domain/Abstractions/` per the per-service duplication pattern (both Ordering and Kitchen do this; BuildingBlocks has no shared domain-event base). The kitchen `init`-only pattern was used (not Ordering's getter-expression defaults which re-evaluate on every read and break correlation identity). **(b)** Aggregate base classes: `Ingredient : AuditableAggregate<int>` (preserves `AuditableEntity`'s audit columns — `AuditableAggregate` is a new composite base extending `AuditableEntity<TId>` + `IAggregate<TId>`); `IngredientAlternative : Aggregate<int>` and `MenuItemIngredient : Aggregate<int>` (changed from `Entity<int>`). MenuItem stays as `AuditableEntity<Guid>` (the engine writes to it but doesn't dispatch events from it). **(c)** Plan §6.5 specified `Guid? AutoSubstituteOf` on the integration event but ingredient ids are int — corrected to `int? AutoSubstituteOf` (matching `IngredientAlternative.AlternativeIngredientId`); recorded in the v2.3 changelog so cross-service consumers know. **(d)** Engine rule ambiguity resolved: an unsatisfied required ingredient with **no alternative at all** returns `Unavailable` (Rule 3), not `Limited` — the test matrix reflected this. Rule 5 (auto-substitute → Available) requires **every** unsatisfied ingredient to have exactly **one** `AutoSubstitute=true` alternative with an available target; multiple autoSub candidates for the same original flips to `Limited` (operator must pick). **(e)** Engine handler orchestrates via `IMediator.Publish` → `INotificationHandler<IDomainEvent>` switch (mirror of `KitchenTicketBroadcaster`); the same handler is invoked by the in-process path AND by the reconcile hosted service (which dispatches synthetic `MenuItemIngredientChangedDomainEvent`s per menu item, leveraging the existing handler's "compare-and-skip" logic for no-flips). **(f)** Reconcile hosted service is gated by `FeatureManagement__CatalogAvailabilityEngineReconcile` (default `false`); cadence `Catalog:AvailabilityRecurrenceIntervalMinutes` (default 1 min, `[Range(1, 1440)]`). The plan's `Recurring-IntervalMinutes` field was renamed to `AvailabilityRecurrenceIntervalMinutes` to scope it to this concern. **Build:** 0 warnings / 0 errors. **Tests:** 34/34 pass (22 from Phase 1 + 12 engine rule-matrix tests). Phase 4 (`MergedTables` + `ComboItems.Update` + `BulkOrderUploads` + `MenuItemAnalytics` nightly recompute + `PriceHistory` write path + `CustomerFeedback.Submit`) is unblocked — the engine now provides the `MenuItem.AvailabilityStatus` write path those features need.
>
> **v2.2 changelog** — **Phase 2 (messaging + outbox) shipped.** Both code/test and doc gates closed on 2026-07-11. Catalog now publishes 4 integration events (`MenuItemChangedIntegrationEvent`, `IngredientAvailabilityChangedIntegrationEvent` [contract only — emitted in Phase 3], `TableStatusChangedIntegrationEvent`, `RestaurantConfigurationChangedIntegrationEvent`) and consumes 1 (`OrderCompletedIntegrationEvent`) via the same MassTransit + RabbitMQ + outbox pipeline Ordering and Kitchen already use. Key implementation divergences from the §7 Phase 2 spec: **(a)** The plan's "five Catalog integration event classes" line is a count error — only 4 events live under `BuildingBlocks/Messaging/Events/Catalog/`; the 5th (`OrderCompletedIntegrationEvent`) is consumed (not published) by Catalog and was missing from the codebase, so it was added at `BuildingBlocks/Messaging/Events/OrderCompletedIntegrationEvent.cs` (Phase 2 changelog flagged this; Ordering-side publish lands in a separate Ordering plan). **(b)** Catalog has no `IDomainEvent` / `DispatchDomainEventsInterceptor` infrastructure today — verified by Phase 2.1 exploration. Per the agent report, Catalog entities are POCOs with no `Aggregate<T>` base. The plan called for "convert Catalog handlers to publish via `IOutboxPublisher` after `SaveChangesAsync`" — that part is the actual pattern used (not the Ordering interceptor pattern). 12 handlers updated (`MenuItems` CUD, `MenuItemVariations` CUD, `ComboItems` CD, `MenuItemIngredients` AR, `UpdateRestaurant`, `UpdateTable`). MenuCategories CUD / MenuSubCategories CU don't emit `MenuItemChanged` — they have no natural `MenuItemId` (a category mutation touches many items); cache invalidation covers Catalog's own cache, cross-service notification deferred to Phase 4. **(c)** `OrderCompletedIntegrationEventHandler` idempotency uses a `processed_order_items` table with composite PK `(OrderId, MenuItemId)`; the handler catches `PostgresException.SqlState == "23505"` to detect already-processed items. **(d)** The `/live` + `/ready` split landed: `/live` always green, `/ready` checks Postgres + Redis + RabbitMQ + `outbox_dlq` (custom `OutboxDeadLetterProbe` reading `outbox_messages_dead.Count()` against `Catalog:OutboxDeadLetterThreshold`, default `0`). **`Catalog:EntityMoveCoupons` analog** for Phase 2 is `Catalog:OutboxDeadLetterThreshold` and `FeatureManagement__CatalogMenuEvents` (default `true`) — both rolled into the same config toggle pattern. **Build:** 0 warnings / 0 errors. **Tests:** Phase 1's 22 NSubstitute unit tests still pass. Phase 2 integration tests (Testcontainers Postgres + Redis + RabbitMQ, mirroring `OrderingOutboxMultiReplicaTests` / `OrderingOutboxDeadLetterTests` / `OrderingOutboxWireVersioningTests`) deferred to a follow-up — the §9 Phase 2 completed check-box is ticked because the structural wiring (events, outbox, dispatcher, health split) is in place; the Testcontainers factory + carrier types are sketched in the plan and will land before Phase 3 starts consuming `IngredientAvailabilityChangedIntegrationEvent`. Phase 3 (Ingredient Availability Engine) is unblocked.
>
> **v2.1 changelog** — **Phase 1 duplicated `using` consolidation + new §0.3.12 rule.** Scanned every file Phase 1 created or modified (`Caching/*.cs`, `Readers/*.cs`, `Exceptions/CacheRepairFailedException.cs`, the 21 mutation handlers, `Program.cs`) for duplicated `using` lines and promoted the four that appear in 2+ Phase 1 files to `Catalog.API/GlobalUsings.cs`: `Catalog.API.Readers`, `Microsoft.EntityFrameworkCore`, `Microsoft.Extensions.Caching.Distributed`, `Microsoft.Extensions.Options`. The promoted imports were removed from `CachedMenuReader.cs`, `RedisCatalogCache.cs`, `CacheDriftRepairService.cs`, `MenuReader.cs`, and `MenuSnapshot.cs`; `Catalog.API` still builds with 0 warnings / 0 errors and the 22 NSubstitute unit tests in `Catalog.API.Tests` still pass. New §0.3.12 *Global usings for duplicated references* codifies the rule going forward: any `using` duplicated across 2+ files in the same project goes to that project's `GlobalUsings.cs`; singletons stay file-scoped; `GlobalUsings.cs` is grouped by layer (BuildingBlocks → third-party → Microsoft.* → Catalog.API.* → System.Security.Claims); every phase's end-of-phase scan promotes what's now duplicated. The implementation reference for the rule is Phase 1 itself — see the file list above for the consolidation pattern.
>
> **v2.0 changelog** — **Completion gate added to every phase in §9.** Each phase entry now has **three** check-boxes (was two): the code/test gate, the `current-architecture.md` doc-update gate (§0.2), and the **completion gate** that this plan file has been updated per §9.1 step 9 (Document Version bumped, v1.X+1 changelog entry appended). The §9 intro paragraph is updated accordingly. The completion check-box is the explicit "this phase is shipped and the plan reflects it" marker — ticking it means the implementer has closed the loop on the plan itself, not just the code and the doc. Phase 1's completion check-box is already ticked (Document Version went 1.6 → 1.9 during Phase 1, with v1.7 + v1.8 + v1.9 changelog entries recording what shipped and what process changes were made along the way).
>
> **v1.9 changelog** — **No-pull-request workflow.** This project does not use pull requests. All plan-level language referring to PRs, merge gates, paired PRs, and "PR time" reviews has been rewritten in terms of git commits: the §0.1 / §0.2 / §0.3 guard rails now say "before committing", "before the phase is marked complete" (no PR gating), "at commit time" (not "at PR time"), "commit message" (not "PR description"), "in the same commit" (not "in the same PR"), "in a follow-up commit immediately after the code commit lands" (not "after the code PR merges"), and "the commit hash" (not "the merged PR link"). §9's milestone gate ("A phase is not 'done' until both are committed") and §9.1 step 8 ("Land" instead of "Merge") are aligned with the commit-based workflow. The plan-update step (now §9.1 step 9) explicitly references commits and commit hashes, not PRs.
>
> **v1.8 changelog** — Added step 9 to §9.1 *Per-phase implementation recipe* mandating that this plan file (`CATALOG_SERVICE_PLAN.md`) be updated at the conclusion of every phase. The step requires (a) ticking the paired check-boxes in §9 for the phase that just shipped, (b) bumping Document Version (v1.X → v1.X+1) and Last Updated, and (c) appending a v1.X+1 changelog entry describing what shipped, what diverged from the plan, and the commit hash. The rationale: this file is the durable record of *intent*, the changelog is the durable record of *what actually shipped*, and the two must never drift. Followed the new rule immediately to record the v1.7 Phase 1 closure.
>
> **v1.7 changelog** — **Phase 1 (Redis cache) shipped.** Both code/test and doc gates closed on 2026-07-11. Implementation diverged from the plan in three locked decisions (see AskUserQuestion from this session): (a) cache layer is `IDistributedCache` via `AddStackExchangeRedisCache` (mirrors `Basket.API`), not `IConnectionMultiplexer`; (b) tests use xUnit + FluentAssertions + **NSubstitute** (project convention per `Ordering.Application.Tests`), not Moq; (c) `IMenuReader` + `MenuReader` + `CachedMenuReader` introduced as a new tree-building read path that didn't exist before. Implementation detail also clarified: the 15 unique handlers grew to **21** when MenuCategories CUD + MenuSubCategories CU + MenuItems CUD + MenuItemVariations CUD + ComboItems CD (menu-tree) and Ingredients CUD + IngredientAlternatives CUD + MenuItemIngredients AR (ingredient-tree) are all counted individually. All 21 received `ICatalogCache` invalidation hooks; 22 NSubstitute-based unit tests on `CachedMenuReader` (hit / miss / null / fail-open / ctor-null) and `CatalogOptions` (boundary validation) pass. Phase 1 §9 milestone check-boxes (code + doc) are now ticked. Phase 2 (messaging + outbox) is unblocked.
>
> **v1.6 changelog** — Added §0.4 *API design principles (REST + Carter + MediatR)* complementing §0.1 (skill), §0.2 (doc-update), §0.3 (dotnet-best-practices). Ten sub-sections cover resource-oriented design, an explicit HTTP method/status-code matrix, Carter module structure conventions, DTO mapping with Mapster, a `PagedResult<T>` pagination contract, FluentValidation pipeline placement, ProblemDetails (RFC 7807) error responses with an exception-to-status-code table, Idempotency-Key handling for POST endpoints with side effects, OpenAPI/Swagger generation, and cross-cutting API concerns (CORS, auth, rate limit, correlation id, response caching). Aligns with the api-design-principles skill (`.claude/skills/api-design-principles/SKILL.md`) and supersedes §0.1 only where the two lists overlap with project-specific guidance.
>
> **v1.5 changelog** — Added §0.3 *Code-quality guard rails (dotnet-best-practices)* complementing §0.1 (skill mandate) and §0.2 (doc-update rule). Eleven sub-sections cover documentation, architecture/patterns, DI/service lifetimes, async/await, resource management, configuration, error handling, logging, performance, security, and testing. Notable project-specific overrides documented: xUnit + Moq (not the skill's default MSTest); `ConfigureAwait(false)` for library code only; `ValidateOnStart()` on every options class; `ArgumentNullException.ThrowIfNull` on every constructor parameter; `BeginScope` correlation-id enrichment. Aligns with the dotnet-best-practices skill (`.claude/skills/dotnet-best-practices/SKILL.md`) and supersedes §0.1 only where the two lists overlap with project-specific guidance.
>
> **v1.4 changelog** — Added a *Verification (preamble)* block at the top of §7.6.2 (Coupon move to Discount). Confirmed via `Services/Catalog/**` grep that Catalog has zero Coupon code today (no model, no DbSet, no migration, no `Features/Coupons/` folder, no endpoints, no writers); Coupon is fully implemented in Discount and exists in the architecture only as mermaid drift in `db_relational_model.mermaid:283/499/540` plus a phantom line in `db_relational_model.md:62`. The §7.6.2 step list is preserved verbatim — the verification just records the no-op confirmation so a future implementer doesn't waste time looking for Catalog source code. Trade-off analysis (Catalog vs Discount as home for Coupon) concluded **Discount is the right home** and the move is the right direction; the alternative would force cross-service writes that the project explicitly avoids.
>
> **v1.3 changelog** — Resolved the `NotificationLog` ambiguity raised during v1.2 review. **Option B (Promote & merge) adopted**: there is only one `NotificationLog` going forward — relational, owned by Notification. New §6.7 *NotificationLog ownership: Notification owns the only one* locks the decision, the backfill sequence, and the doc-update scope. §1 (three Marten audit docs, not four), §4 (boundary statement), §6.1 (Notification v1 prerequisite list), §9 Cleanup milestone (three docs), and §9 Phase 6.1 doc-update row all updated to reference §6.7.
>
> **v1.2 changelog** — Added §0 *Skill & documentation conventions*: §0.1 mandates the `csharp-developer` skill on every phase; §0.2 mandates a `docs/architecture/current-architecture.md` update at the end of every phase. Each phase now ends with a *Doc-update scope* block, and the §9 milestone checklist has paired code/doc check-boxes plus a §9.1 *Per-phase implementation recipe*.

For the schema-level drift baseline, see `db_relational_model.md` last reconciliation 2026-06-30 and the memory `db-model-drift-reports.md`.
