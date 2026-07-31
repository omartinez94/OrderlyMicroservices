# Persistence & Reliability — Implementation Plan

> Scope: close every data-loss, observability, and reliability P0/P1 defect surfaced by the 2026-07-30 production-readiness audit. Touches `Discount.Grpc` (SQLite → PostgreSQL), every service's Dockerfile + `docker-compose.yml` (HEALTHCHECK + persistent volumes + environment posture), `Kitchen.API` (outbox wiring + duplicate-event fix), `BuildingBlocks.Observability` (restored `ServiceDefaults` + `AppHost`), and `Ordering.API` (`/live` + `/ready` split + OpenAPI). **Authentication / authorization / trust-root work is NOT in scope** — see sibling plan `TRUST_ROOT_HARDENING_PLAN.md`. **Multitenancy adoption itself is NOT in scope** — see `MULTITENANCY_ROLLOUT_PLAN.md`.

---

## Status

> **Plan version**: `v2.1` (2026-07-30) — `MINOR` increments per phase completion; `MAJOR` is reserved for breaking restructures of the plan itself.
> **Current state**: ⏸ Not started

| Phase | Name | Status |
|:-----:|---|:-----:|
| 1 | Discount SQLite → PostgreSQL migration | ⏸ Pending |
| 2 | Migration reliability + Docker HEALTHCHECK + compose environment posture | 🔒 Blocked (by Phase 1) |
| 3 | Kitchen outbox wiring + duplicate-event fix | 🔒 Blocked (by Phase 1) |
| 4 | OpenTelemetry across all services + OTEL collector | 🔒 Blocked (by Phase 2) |
| 5 | OpenAPI per service + `/live`+`/ready` split in Ordering | 🔒 Blocked (by Phase 4) |

> **Legend**: ✅ Done · 🚧 In progress · ⏸ Pending · 🔒 Blocked

> **Commit messages**: Conventional Commits (`feat:`, `docs:`, `chore:`, `test:`, `fix:`). Short subject, ≤50 chars, imperative mood, no trailing period.

> **Update rule**: **on every phase completion, the plan MUST be updated in the same pair of commits as the phase work (a code commit + a plan commit — see [How to use this template](#how-to-use-this-template)).** The plan is the source of truth for what was decided and what shipped.


---

## 0. Skill & documentation conventions

### 0.1 Coding standards mandate
> **All implementation work on this plan MUST follow the project conventions defined in `AGENTS.md`** (repository root). `AGENTS.md` is the source of truth for C# 12+ / .NET 10 idiom, EF Core + Marten, ASP.NET Core + Carter, NodaTime usage, and the project's architectural patterns (Vertical Slice for Catalog/Basket, Clean Architecture for Ordering). Additional reference material for C# patterns, ASP.NET Core, Entity Framework, and performance lives in `.claude/skills/csharp-developer/references/` (`modern-csharp.md`, `aspnet-core.md`, `entity-framework.md`, `performance.md`) and may be consulted for implementation guidance.

Key guard rails inherited from `AGENTS.md` and the reference material: nullable enabled, primary constructors, async/await with `CancellationToken`, `Result<T>` for error paths, no blocking calls, Carter for minimal APIs (no MVC controllers), MediatR for CQRS, FluentValidation pipeline behaviours, MassTransit outbox patterns, DTO mapping for API responses.

> **EF Core checkpoint:** after any code change that mutates the schema (Phase 1's Discount SQLite → Postgres rewrite; Phase 2's migration-host-service changes), the implementer runs `dotnet ef migrations add <Name>` per the project's `--startup-project` rule (Discount: `--startup-project Discount.Grpc`; see memory `ordering-ef-migration-startup-project.md` for dev-DB passwords + ports). Phase 1's Postgres migrations are **hand-authored** — EF cannot infer the cross-engine schema conversion from SQLite `text`/`integer`/`blob` to PostgreSQL `text`/`integer`/`bytea`.

The coding standards are **not** a substitute for the plan; the plan wins where they disagree.

### 0.2 Code-quality guard rails

This plan **inherits the project-wide guard rails from the catalog / ordering / discount plans verbatim** (the per-service plans are authoritative). Persistence/reliability-specific overrides layered on top:

- **All `MigrateAsync` calls are awaited and run before `app.Run()`** OR live in an `IHostedService` that retries with exponential backoff. No fire-and-forget migrations (today's `Discount.Grpc/Data/Extensions.cs:11` is the offending line).
- **All relational DbContexts enable `EnableRetryOnFailure`** with `maxRetryCount: 5, maxRetryDelay: 10s, errorCodesToAdd: null`. Applies to Catalog (Npgsql), Ordering (MSSQL), Discount (Postgres after Phase 1), and Identity (Npgsql).
- **Outbox dispatcher is the only path that publishes integration events from command handlers.** No handler may inject `IPublishEndpoint` directly. Phase 3 enforces this in Kitchen; Basket + Ordering already comply per `BASKET_SERVICE_PLAN.md` / `ORDER_ACTIVITY_PLAN.md`.
- **All Dockerfiles carry a `HEALTHCHECK` directive** that hits the service's `/ready` endpoint. No service is treated as healthy just because the process is up.
- **All persistent volumes are declared in `docker-compose.yml`, not just `docker-compose.override.yml`** — overrides are dev-only; production compose files use the same volume definitions.
- **OpenTelemetry is wired uniformly via `BuildingBlocks.Observability.AddOrderlyOpenTelemetry`** — services do not call `AddOpenTelemetry()` directly. The `ServiceDefaults` + `AppHost` projects that ship today as empty `obj/`+`bin/` shells are restored.
- **OpenAPI spec is generated via `Microsoft.AspNetCore.OpenApi` (built into .NET 10)** — no Swashbuckle unless a service already uses it. Each service emits `openapi.json` at `/openapi/v1.json`.
- **`/live` and `/ready` are separate endpoints** with `MapHealthChecks("/live", ...)` for liveness (no checks — always green) and `MapHealthChecks("/ready", ...)` with `Predicate = check => check.Tags.Contains("ready")` for readiness. Ordering today has a single `UseHealthChecks("/health", ...)` call in `DependencyInjection.cs`; Phase 5 replaces it with the split pattern.

#### 0.2.1 Global usings (project-specific)

After Phase 4, every service's `GlobalUsings.cs` gains:

```csharp
global using BuildingBlocks.Observability;  // for AddOrderlyOpenTelemetry
```

The "2+ files" promotion rule from CATALOG_SERVICE_PLAN §0.3.12 applies. (For Phase 4, "2+ files" is satisfied once `Catalog.API/Program.cs` + `Ordering.API/Program.cs` + `Kitchen.API/Program.cs` + `Identity.API/Program.cs` + `Discount.Grpc/Program.cs` all import the namespace — 5 files, well over the threshold.)

---

## 1. Context

The 2026-07-30 production-readiness audit found 8 P0 / P1 defects in the persistence + reliability surface area. Six of them are exploitable today in production-shaped deploys (data loss on first container restart, integration events lost on process crash, distributed debugging impossible):

1. **Discount SQLite is the production DB** (`Discount.Grpc.csproj:25` + `Program.cs:46`). SQLite has no concurrent writers; `BeginTransactionAsync` in `DiscountOutboxDispatcher.cs:140` would serialize entire application requests; cold-restart data loss is one `rm discountdb` away.
2. **Discount SQLite file lives on the container's writable layer.** `docker-compose.yml` declares no `discountdb` Postgres container; `docker-compose.override.yml` mounts only the HTTPS cert volume. First `docker-compose restart` of `discount.grpc` wipes every coupon, reward code, and outbox row.
3. **`Discount.Grpc/Data/Extensions.cs:11` calls `context.Database.MigrateAsync()` without `await`** — fire-and-forget. The host starts serving gRPC traffic before EF Core applies pending migrations; `RedeemDiscount`'s raw SQL surfaces `SQLITE_ERROR: no such table: Coupons` on fresh deploys.
4. **Kitchen handlers inject `IPublishEndpoint` directly** (`AcceptOrder.cs:16`, `BumpOrder.cs:15`, `CancelOrder.cs:26`, `MarkOrderReady.cs:16`, `StartItemPrep.cs:22`). The outbox infrastructure is registered but never consumed. A process crash between `SaveChangesAsync` and the broker round-trip **loses the integration event** — Kitchen state changes but Ordering's `MarkReady` / `MarkPreparing` etc. never fires → order stuck indefinitely.
5. **Kitchen's `OrderCreatedIntegrationEventHandler.AddAsync` is not idempotent** (`OrderCreatedIntegrationEventHandler.cs:24-47`). The race produces `DbUpdateException` which is uncaught → MassTransit nacks indefinitely → poison message.
6. **OpenTelemetry is wired in 1 of 5 services** (Basket only). The `orderly-microservices.ServiceDefaults` + `orderly-microservices.AppHost` projects exist on disk but ship only `obj/`+`bin/` artifacts (no `.csproj`, not in `.slnx`). Distributed debugging across 4/5 services is impossible.
7. **No OTEL collector in compose** — Basket's exporter points at `http://localhost:4317` which doesn't exist.
8. **No `/live` + `/ready` split in Ordering** — single `UseHealthChecks("/health")` endpoint in `DependencyInjection.cs`. Kubernetes will kill pods on any health-check dip (the failure mode Basket already documented at `Basket.API/Program.cs:438-441`).
9. **No `HEALTHCHECK` directive in any Dockerfile** — K8s liveness probes fall back to "is the process running?" rather than "can it serve `/ready`?"
10. **`ASPNETCORE_ENVIRONMENT=Development` hardcoded on every service in compose** — defeats the `IsDevelopment()` gates that `TRUST_ROOT_HARDENING_PLAN.md` Phase 1 will add. Sibling plan's env gates are no-ops until this is fixed.
11. **No OpenAPI / Swagger generation outside Basket** — external integrators have no machine-readable contract; CI can't lint spec drift.
12. **EF Core `EnableRetryOnFailure` not enabled** in Catalog, Ordering, Identity.

Reference plans: `.agents/plan/discount/DISCOUNT_SERVICE_PLAN.md` (Outbox + dispatcher shape), `.agents/plan/kitchen/KITCHEN_SERVICE_PLAN.md` (handler convention), `.agents/plan/basket/BASKET_SERVICE_PLAN.md` (OpenTelemetry pattern — the only existing wiring), `ORDER_ACTIVITY_PLAN.md` (transactional handler pattern — may be a sub-plan within ordering or kitchen).

---

## 2. Goal

By the end of Phase 5:

1. Discount is backed by PostgreSQL with a `discountdb` container and a persistent volume. A `docker-compose restart` of `discount.grpc` preserves every row.
2. Migrations are awaited at startup (or run in a retrying `IHostedService`); every relational service has `EnableRetryOnFailure`.
3. Kitchen publishes every integration event via the outbox; the `OrderCreatedIntegrationEvent` duplicate path is idempotent.
4. Every service emits OpenTelemetry traces + metrics + structured logs; the OTEL collector in compose receives them; a single user request through 3 services materialises as a connected trace.
5. Every service emits `/openapi/v1.json`; Ordering has separate `/live` + `/ready` endpoints; every Dockerfile has a `HEALTHCHECK` directive.
6. `ASPNETCORE_ENVIRONMENT` defaults to `Production` in `docker-compose.yml`; `docker-compose.override.dev.yml` overrides to `Development` for the dev defaults.

Concrete deliverables:

- `Discount.Grpc.csproj` swaps `Microsoft.EntityFrameworkCore.Sqlite` → `Microsoft.EntityFrameworkCore.PostgreSQL`; migrations are rewritten for PG semantics (e.g. `FOR UPDATE SKIP LOCKED` in `DiscountOutboxDispatcher`).
- `docker-compose.yml` declares the `discountdb` Postgres service + `discount-data` named volume; `docker-compose.override.dev.yml` ships the dev defaults (existing override is renamed).
- `BuildingBlocks.Observability.AddOrderlyOpenTelemetry(IServiceCollection)` extension; restored `ServiceDefaults` + `AppHost` `.csproj` files; `Program.cs` in each service calls the extension.
- `Kitchen.API/Application/KitchenTickets/Commands/{AcceptOrder,BumpOrder,CancelOrder,MarkOrderReady,StartItemPrep}Handler.cs` — swap `IPublishEndpoint` for `IOutboxPublisher`.
- `Kitchen.API/Messaging/EventHandlers/OrderCreatedIntegrationEventHandler.cs` — wrap `repository.AddAsync` in `try/catch(DbUpdateException)`.
- `Ordering.API/Program.cs` — replace `UseHealthChecks("/health")` (currently in `DependencyInjection.cs`) with `MapHealthChecks("/live")` + `MapHealthChecks("/ready", new HealthCheckOptions { Predicate = c => c.Tags.Contains("ready") })`.
- Every service's `Dockerfile` gains `HEALTHCHECK --interval=30s --timeout=5s --start-period=20s CMD curl -fsS http://localhost:8080/ready || exit 1`. **Note:** the final Docker image stage must include `curl` (Alpine: `apk add --no-cache curl`; Debian: `apt-get install -y --no-install-recommends curl`). Alternatively, use `wget -qO- http://localhost:8080/ready || exit 1` which is available in Alpine by default.

---

## 3. Out of scope

- **Authentication, authorization, trust-root work** — covered by sibling plan `TRUST_ROOT_HARDENING_PLAN.md`.
- **Multitenancy adoption in Catalog / Kitchen / Ordering** — covered by `MULTITENANCY_ROLLOUT_PLAN.md`.
- **CI/CD pipeline (matrix, image build/push)** — future "Deployment Pipeline" plan once the deployment target is chosen.
- **Kubernetes manifests, Helm chart, NetworkPolicy** — same future plan.
- **Discount SQLite → Postgres data migration of existing dev rows** — every dev DB is disposable. Production deploys start on an empty `Coupons` table per the seed-gate change in `TRUST_ROOT_HARDENING_PLAN.md` Phase 2.
- **Pact / contract tests** — future "Deployment Pipeline" plan.
- **Domain-event replay tooling** — not a defect today; defer until outbox growth becomes a problem.
- **AOT / trimming** — future optimisation, not a reliability concern.

---

## 4. Tech decisions

| Decision | Choice | Reason |
| :--- | :--- | :--- |
| Discount database engine | PostgreSQL 16 (same version as `catalogdb` / `orderdb`) | Existing Postgres infrastructure; shared image; one less DB engine to operate; `FOR UPDATE SKIP LOCKED` natively supported (SQLite has no equivalent) |
| Native NodaTime mapping for PostgreSQL | Remove `InstantToLongConverter` and use Npgsql native mapping of `Instant` to `timestamptz` | Storing timestamps natively is standard for PostgreSQL and aligns with `Catalog.API` handling |
| Migration runner topology | Inline `await` before `app.Run()` for services with simple startup; `IHostedService` with `EnableRetryOnFailure` + exponential backoff for services with heavier startup (Catalog, Discount) | The `IHostedService` pattern survives rolling-restart DB failovers without crash-looping the pod |
| Kitchen outbox consumption | Swap `IPublishEndpoint` → `IOutboxPublisher` in all 5 command handlers; both share a `Publish(IntegrationEvent)`-shaped contract | Outbox transaction guarantees atomic domain mutation + event publication; the wire contract is unchanged |
| `OrderCreatedIntegrationEvent` idempotency | Wrap `repository.AddAsync` in `try { ... } catch (DbUpdateException) { logger.LogInformation("Duplicate event {EventId}", evt.EventId); return; }` | MassTransit redelivery is the documented retry path; PK collision is the success signal, not a failure |
| OpenTelemetry instrumentation | `BuildingBlocks.Observability.AddOrderlyOpenTelemetry` extension; `AddAspNetCoreInstrumentation`, `AddHttpClientInstrumentation`, `AddGrpcClientInstrumentation`, `AddNpgsql` / `AddSqlClient`, OTLP exporter to `OpenTelemetry__Endpoint` env var | Single source of truth for the instrumentation shape; services don't call `AddOpenTelemetry()` directly |
| OTEL collector | `otel-collector-contrib` container in `docker-compose.yml`; receivers `otlp` (gRPC + HTTP); pipelines `traces`, `metrics`, `logs`; exporters `debug` (dev) + `otlp/<backend>` (prod, via env-substituted endpoint). | Standard OTel stack; portable to Tempo / Jaeger / Honeycomb / Datadog via the exporter config |
| OpenAPI generation | `Microsoft.AspNetCore.OpenApi` (built into .NET 10); `AddOpenApi()` in each service; `MapOpenApi()` at `/openapi/v1.json`; `.WithOpenApi()` on each Carter endpoint | Native to the runtime; no extra dependency; consistent JSON shape |
| Health endpoint split | `MapHealthChecks("/live", ...)` (no checks — always green) + `MapHealthChecks("/ready", new HealthCheckOptions { Predicate = c => c.Tags.Contains("ready") })` | K8s convention: liveness restarts the pod, readiness removes from rotation; they should not be the same endpoint |
| `docker-compose.yml` structure | Split into `docker-compose.yml` (production-shaped, no defaults) + `docker-compose.override.dev.yml` (dev defaults: `ASPNETCORE_ENVIRONMENT=Development`, default passwords) | Production-shaped compose refuses to start with missing env vars; dev is explicit |
| Visual Studio compose integration | Set `<DockerComposeProjectFiles>` to `docker-compose.yml;docker-compose.override.dev.yml` in `docker-compose.dcproj` | Ensures Visual Studio debugger automatically loads the dev override file |
| `HEALTHCHECK` directive | `HEALTHCHECK --interval=30s --timeout=5s --start-period=20s --retries=3 CMD curl -fsS http://localhost:8080/ready || exit 1` | Standard pattern; `start-period` covers the warm-up window for EF migrations |
| Discount SQLite → Postgres migration of dev rows | No data preservation; dev DBs are disposable; production deploys start on an empty table | Avoids the cross-engine data-shape compatibility rabbit hole; aligns with the `SeedSuperAdminAsync` gate in `TRUST_ROOT_HARDENING_PLAN.md` Phase 2 |

---

## 5. Folder layout

```
orderly-microservices/
├── docker-compose.yml                       (modified — split env vars; add otel-collector + discountdb; persistent volumes)
├── docker-compose.override.yml              (renamed to docker-compose.override.dev.yml)
├── docker-compose.override.dev.yml          (renamed; dev-only defaults)
├── docker-compose.dcproj                    (modified — update DockerComposeProjectFiles property)
├── orderly-microservices.ServiceDefaults/   (RESTORED .csproj — was empty obj/bin shell)
│   ├── Extensions/
│   │   ├── ServiceDefaultsExtensions.cs     (new — AddOrderlyDefaults, AddOrderlyOpenTelemetry)
│   │   └── OpenTelemetryExtensions.cs       (new — shared instrumentation config)
│   └── orderly-microservices.ServiceDefaults.csproj (RESTORED)
├── orderly-microservices.AppHost/           (RESTORED .csproj)
│   ├── Program.cs                            (RESTORED — Aspire AppHost wiring)
│   ├── AppHost.cs                            (RESTORED)
│   └── orderly-microservices.AppHost.csproj (RESTORED)
├── BuildingBlocks/
│   ├── Observability/                       (NEW — shared OpenTelemetry extension)
│   │   ├── ServiceCollectionExtensions.cs   (new — AddOrderlyOpenTelemetry)
│   │   └── BuildingBlocks.Observability.csproj (new)
│   └── Persistence/                         (NEW — shared DbContext migrator)
│       └── MigratorHostedService.cs         (new)
├── Services/
│   ├── Discount/Discount.Grpc/
│   │   ├── Discount.Grpc.csproj             (modified — Npgsql package)
│   │   ├── Program.cs                       (modified — UseNpgsql + EnableRetryOnFailure)
│   │   ├── Data/
│   │   │   ├── Extensions.cs                (modified — await MigrateAsync)
│   │   │   ├── DiscountContext.cs           (modified — remove InstantToLongConverter, Postgres config)
│   │   │   └── Migrations/                  (rewritten for PG semantics)
│   │   ├── Messaging/EventHandlers/
│   │   │   └── InboundEventDedup.cs         (modified — check PostgresException and 23505 state)
│   │   ├── Health/
│   │   │   └── DiscountHealthChecks.cs      (modified — swap SqliteFileCheck for Postgres check)
│   │   ├── Outbox/
│   │   │   └── DiscountOutboxDispatcher.cs  (modified — FOR UPDATE SKIP LOCKED)
│   │   └── appsettings.json                 (modified — Postgres connection string)
│   ├── Discount/Discount.Grpc.Tests/
│   │   ├── Discount.Grpc.Tests.csproj       (modified — replace SQLite with Testcontainers.PostgreSql + Npgsql)
│   │   └── Integration/
│   │       └── DiscountWebApplicationFactory.cs (modified — spin up PostgreSqlContainer)
│   ├── Kitchen/Kitchen.API/
│   │   ├── Application/KitchenTickets/Commands/{AcceptOrder/AcceptOrderHandler,BumpOrder/BumpOrderHandler,CancelOrder/CancelOrderHandler,MarkOrderReady/MarkOrderReadyHandler,StartItemPrep/StartItemPrepHandler}.cs (modified — IOutboxPublisher)
│   │   ├── Application/EventHandlers/Integration/
│   │   │   └── OrderCreatedIntegrationEventHandler.cs (modified — try/catch DbUpdateException)
│   │   └── Program.cs                       (modified — AddOrderlyOpenTelemetry)
│   ├── Catalog/Catalog.API/
│   │   ├── Program.cs                       (modified — AddOrderlyOpenTelemetry, retrying migration hosted service)
│   │   └── Dockerfile                       (modified — HEALTHCHECK)
│   ├── Ordering/Ordering.API/
│   │   ├── Program.cs                       (modified — AddOrderlyOpenTelemetry, /live + /ready split, AddOpenApi)
│   │   └── Dockerfile                       (modified — HEALTHCHECK)
│   ├── Identity/Identity.API/
│   │   ├── Program.cs                       (modified — AddOrderlyOpenTelemetry)
│   │   └── Dockerfile                       (modified — HEALTHCHECK)
│   ├── Basket/Basket.API/
│   │   └── Dockerfile                       (modified — HEALTHCHECK)
│   └── Discount/Discount.Grpc/
│       └── Dockerfile                       (modified — HEALTHCHECK)
├── ApiGateway/YarpApiGateway/
│   └── Dockerfile                           (modified — HEALTHCHECK)
└── orderly-microservices.slnx               (modified — add ServiceDefaults, AppHost, BuildingBlocks.Observability)
```

Two new projects: `BuildingBlocks.Observability` (shared OpenTelemetry extension) and the restored `orderly-microservices.ServiceDefaults` + `orderly-microservices.AppHost`. Three directories restructured (`docker-compose.yml` split; two `.csproj` files restored). No service-side project moves.

---

## 6. Specification

### 6.1 Discount SQLite → PostgreSQL

* **`Discount.Grpc.csproj`** — replace `<PackageReference Include="Microsoft.EntityFrameworkCore.Sqlite" Version="..." />` with `<PackageReference Include="Npgsql.EntityFrameworkCore.PostgreSQL" Version="..." />` (aligns with version `10.0.x` used in `Catalog.API.csproj`).
* **`Discount.Grpc.Tests.csproj`** — remove SQLite packages (`Microsoft.EntityFrameworkCore.Sqlite`, `SQLitePCLRaw.lib.e_sqlite3`) and add `<PackageReference Include="Npgsql.EntityFrameworkCore.PostgreSQL" Version="..." />` and `<PackageReference Include="Testcontainers.PostgreSql" Version="..." />`.
* **`Discount.Grpc/Program.cs:46`** — `options.UseNpgsql(connectionString, npgsqlOptions => npgsqlOptions.UseNodaTime().EnableRetryOnFailure(maxRetryCount: 5, maxRetryDelay: TimeSpan.FromSeconds(10), errorCodesToAdd: null))`.
* **`Discount.Grpc/Data/DiscountContext.cs`** — remove the `InstantToLongConverter` configuration in `ConfigureConventions` to store NodaTime `Instant` values natively as `timestamp with time zone` (timestamptz) columns in PostgreSQL.
* **`Discount.Grpc/Messaging/EventHandlers/InboundEventDedup.cs`** — update the unique violation check to catch PostgreSQL's `Npgsql.PostgresException` and check that its SQLState is `"23505"` instead of SQLite-specific code checks.
* **`Discount.Grpc/Health/DiscountHealthChecks.cs`** — replace the `SqliteFileCheck` health check with a PostgreSQL health check (`AddNpgSql` or similar connection checks) and rename the probe constant name to `discount-postgres`.
* **`Discount.Grpc.Tests/Integration/DiscountWebApplicationFactory.cs`** — rewrite the test factory to spin up a PostgreSQL Testcontainer (`PostgreSqlContainer` / `PostgreSqlBuilder`), wire the configuration connection string dynamically, and handle container startup/disposal via `IAsyncLifetime`.
* **`Discount.Grpc/appsettings.json:3`** — `"ConnectionStrings:Database": "Server=discountdb;Port=5432;Database=discount;User Id=postgres;Password=${DB_PASSWORD};Include Error Detail=true"` (env-var substitution; `${DB_PASSWORD}` resolved at startup).
* **EF Core migrations** — all 8 existing migrations rewritten for PG semantics:
    * `JsonContains` queries against `DiscountRule.RuleDataJson` (`MenuItemChangedConsumer.cs:64`) → use `EF.Functions.JsonContains` or `rule.RuleDataJson @> ...` with a GIN index on `RuleDataJson`.
    * `OnDelete(DeleteBehavior.Restrict)` on child entities → equivalent PG constraint.
    * Composite primary keys on `(RestaurantId, Code)` → kept; PG accepts the syntax.
    * The two existing SQLite-specific indexes (implicit rowid) → replaced with explicit `CREATE INDEX` migrations.
* **`Discount.Grpc/Outbox/DiscountOutboxDispatcher.cs:56-57`** — append `FOR UPDATE SKIP LOCKED` to the row-claim SQL.
* **`docker-compose.yml`** — add:
    ```yaml
    services:
      discountdb:
        image: postgres:16-alpine
        environment:
          POSTGRES_USER: ${DB_USER:-postgres}
          POSTGRES_PASSWORD: ${DB_PASSWORD:?must be set in production}
          POSTGRES_DB: discount
        volumes:
          - discount-data:/var/lib/postgresql/data
        healthcheck:
          test: ["CMD-SHELL", "pg_isready -U ${DB_USER:-postgres}"]
          interval: 5s
          timeout: 3s
          retries: 10
      discount.grpc:
        depends_on:
          discountdb:
            condition: service_healthy
        environment:
          ConnectionStrings__Database: "Server=discountdb;Port=5432;Database=discount;User Id=${DB_USER:-postgres};Password=${DB_PASSWORD}"
          ASPNETCORE_ENVIRONMENT: ${ASPNETCORE_ENVIRONMENT:-Production}
        volumes:
          - discount-data:/data
    volumes:
      discount-data:
    ```

### 6.2 Migration reliability + compose posture

* **`Discount.Grpc/Data/Extensions.cs:11`** — make `UseMigration` `async`/`Task<IDisposable>`; `await context.Database.MigrateAsync()` inside an `await app.UseAsync...` block before `app.Run()`.
* **`Catalog.API/Program.cs`** — replace inline `await dbContext.Database.MigrateAsync()` with a new `MigratorHostedService : IHostedService` that retries with exponential backoff (`2s, 4s, 8s, 16s, 32s, 64s` — 6 attempts, ~2 minutes total) before failing the pod.
* **`Ordering.API/Program.cs`** — same `MigratorHostedService` pattern; reuse the `BuildingBlocks.Persistence.MigratorHostedService` once extracted.
* **`BuildingBlocks/Persistence/MigratorHostedService.cs`** (new) — generic `IHostedService<TDbContext>` that runs `MigrateAsync` on `StartAsync` with the retry policy above. One implementation, used by every relational service. A configurable `MigrationTimeoutSeconds` setting (default: `120`) triggers a hard fail if the total retry window is exceeded, preventing indefinite startup hangs.
* **`Ordering.Infrastructure/DependencyInjection.cs:27-31`** — `options.UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure(5, TimeSpan.FromSeconds(10), null))`.
* **`Catalog.API/Program.cs:150-157`** — `npgsqlOptions.EnableRetryOnFailure(5, TimeSpan.FromSeconds(10), null)`.
* **`Identity.API/Program.cs`** — same Npgsql retry flag.
* **`docker-compose.yml`** — every `depends_on` uses `condition: service_healthy`; every Postgres / MSSQL / Redis / RabbitMQ container has a `healthcheck:` block; `ASPNETCORE_ENVIRONMENT` defaults to `${ASPNETCORE_ENVIRONMENT:-Production}`.
* **`docker-compose.override.yml`** → **`docker-compose.override.dev.yml`** — renamed; carries the dev defaults (`ASPNETCORE_ENVIRONMENT=Development`, `JWT_SECRET=devsecret`, default passwords). README documents `docker-compose -f docker-compose.yml -f docker-compose.override.dev.yml up -d --build` as the dev command.
* **`docker-compose.dcproj`** — update `<DockerComposeProjectFiles>` to set `docker-compose.yml;docker-compose.override.dev.yml` to preserve VS container loading.
* **`Dockerfile`** — every service Dockerfile gains:
    ```dockerfile
    HEALTHCHECK --interval=30s --timeout=5s --start-period=20s --retries=3 \
      CMD curl -fsS http://localhost:8080/ready || exit 1
    ```
    **Port adjustments:** Basket's port may differ — adjust per service. Discount.Grpc may use a different HTTP port alongside gRPC — verify the `ASPNETCORE_URLS` / Kestrel binding for each service.
    **`curl` dependency:** the final Docker image stage must include `curl`. For `mcr.microsoft.com/dotnet/aspnet` (Debian-based): `RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*`. For Alpine variants: `RUN apk add --no-cache curl`. Alternatively, use `wget -qO- ... || exit 1` which is available in Alpine by default.

### 6.3 Kitchen outbox wiring + duplicate-event fix

* **`Kitchen.API/Application/KitchenTickets/Commands/AcceptOrder/AcceptOrderHandler.cs`** — replace `IPublishEndpoint` constructor injection with `IOutboxPublisher`; the existing `await publisher.PublishAsync(evt, ct)` call becomes `await publisher.Publish(evt, ct)` (same shape).
* **`BumpOrder/BumpOrderHandler.cs`, `CancelOrder/CancelOrderHandler.cs`, `MarkOrderReady/MarkOrderReadyHandler.cs`, `StartItemPrep/StartItemPrepHandler.cs`** — apply the same dependency swap in the command handlers.
* **`Kitchen.API/Program.cs`** — `AddOutboxPublisher` is already registered (per the audit); `IPublishEndpoint` registration stays for non-domain-event broker interactions (if any). No DI change.
* **`Kitchen.API/Application/EventHandlers/Integration/OrderCreatedIntegrationEventHandler.cs`** — wrap the `AddAsync` + `SaveChangesAsync` pair in:
    ```csharp
    try
    {
        await repository.AddAsync(ticket, ct);
        await unitOfWork.SaveChangesAsync(ct);
    }
    catch (DbUpdateException ex) when (IsDuplicateKey(ex))
    {
        logger.LogInformation("Duplicate OrderCreatedIntegrationEvent {EventId}; skipping", evt.EventId);
        return;
    }
    ```
    The `IsDuplicateKey(DbUpdateException)` helper inspects the inner `PostgresException` for `SqlState == "23505"` (unique-violation).
* **Integration test**: `Kitchen.API.Tests/Integration/DuplicateOrderCreatedTests` — sends two identical events, asserts the second is logged + skipped, no exception is thrown, no nack.

### 6.4 OpenTelemetry across all services + OTEL collector

* **`BuildingBlocks.Observability/ServiceCollectionExtensions.cs`** — new:
    ```csharp
    public static IServiceCollection AddOrderlyOpenTelemetry(this IServiceCollection services, IConfiguration config, string serviceName)
    {
        services.AddOpenTelemetry()
            .ConfigureResource(r => r.AddService(serviceName, serviceVersion: Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0"))
            .WithTracing(t => t
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddGrpcClientInstrumentation()
                .AddNpgsql()  // no-op for non-Npgsql services
                .AddSource("MassTransit")  // covers Outbox publishes
                .AddOtlpExporter(o => o.Endpoint = new Uri(config["OpenTelemetry:Endpoint"] ?? "http://localhost:4317")))
            .WithMetrics(m => m
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation()
                .AddOtlpExporter(o => o.Endpoint = new Uri(config["OpenTelemetry:MetricsEndpoint"] ?? "http://localhost:4317")));
        return services;
    }
    ```
* **`orderly-microservices.ServiceDefaults/Extensions/ServiceDefaultsExtensions.cs`** — restored; `AddOrderlyDefaults` calls `AddOrderlyOpenTelemetry(config, "OrderlyMicroservices")` + `AddServiceDiscovery()` + `AddHealthChecks()`.
* **`orderly-microservices.AppHost/Program.cs`** — restored Aspire AppHost that references every service project for local dev orchestration.
* **`Catalog.API/Program.cs`, `Ordering.API/Program.cs`, `Kitchen.API/Program.cs`, `Identity.API/Program.cs`, `Discount.Grpc/Program.cs`, `Basket.API/Program.cs`** — replace any inline `AddOpenTelemetry` with `builder.Services.AddOrderlyOpenTelemetry(builder.Configuration, "Orderly.Catalog")` (etc.). Basket is the only service currently wired (using Swashbuckle-based OpenTelemetry); this normalises the call to use the shared extension.
* **`docker-compose.yml`** — add `otel-collector` service:
    ```yaml
    otel-collector:
      image: otel/opentelemetry-collector-contrib:0.96.0
      command: ["--config=/etc/otelcol-contrib/config.yaml"]
      volumes:
        - ./otel-collector-config.yaml:/etc/otelcol-contrib/config.yaml:ro
      ports:
        - "4317:4317"  # OTLP gRPC
        - "4318:4318"  # OTLP HTTP
    ```
* **`otel-collector-config.yaml`** (new) — receivers `otlp`; processors `batch`; exporters `debug` (dev) + `otlp/<backend>` (prod, via env-substituted endpoint).
* **Every service's `docker-compose.yml` entry** gains `OpenTelemetry__Endpoint: http://otel-collector:4317`.

### 6.5 OpenAPI per service + `/live`+`/ready` split

* **`Catalog.API/Program.cs`, `Ordering.API/Program.cs`, `Kitchen.API/Program.cs`, `Identity.API/Program.cs`, `Discount.Grpc/Program.cs`** — `builder.Services.AddOpenApi();` + `app.MapOpenApi();` → serves `/openapi/v1.json`. Per-endpoint `.WithOpenApi(...)` on every Carter module (mirrors Basket's `.WithTags("Basket")` scaffolding).
* **`Ordering.API/DependencyInjection.cs`** — replace `UseHealthChecks("/health", ...)` with `MapHealthChecks` calls in `Program.cs`:
    ```csharp
    app.MapHealthChecks("/live", new HealthCheckOptions { Predicate = _ => false });  // always green
    app.MapHealthChecks("/ready", new HealthCheckOptions { Predicate = c => c.Tags.Contains("ready") });
    ```
    Tag every readiness check (`AddNpgSql(...)`, `AddSqlServer(...)`, broker checks) with `"ready"`.
* **CI integration**: the existing `basket-tests.yml` extends to a matrix that asserts every service's `/openapi/v1.json` is valid JSON and contains at least one path (proves OpenAPI generation didn't break).
* **`Ordering.API/Endpoints/*.cs`** — `.WithOpenApi(...)` on each endpoint; `.WithSummary(...)` + `.WithDescription(...)` per endpoint as a first pass.

---

## 7. Cross-Repository Communication

This plan spans multiple in-repo services but no external systems. Cross-service touch points:

| From | To | Mechanism | Phase |
|---|---|---|---|
| `Discount.Grpc` | `discountdb` Postgres | EF Core / Npgsql connection | 1 |
| `Kitchen.API` command handlers | `IOutboxPublisher` | DI swap (replaces `IPublishEndpoint`) | 3 |
| All services | OTEL collector | OTLP gRPC + HTTP exporters | 4 |
| `discountdb` container | `discount.grpc` container | `depends_on: condition: service_healthy` | 1, 2 |
| `Orderly.Messaging.Outbox` | `Kitchen.API` | `IOutboxPublisher` already registered; no schema change | 3 |
| `Ordering.API` | `Basket.API` (sibling) | Same `MapHealthChecks("/live"\|"/ready")` pattern exists in Basket — copy | 5 |

No protocol changes; no new integration events.

---

## 8. Security guardrails

> [!CAUTION]
> **Discount SQLite → PostgreSQL drops all dev data.** Production deploys start on an empty `Coupons` table. Never attempt to migrate a SQLite file across to Postgres — the on-disk formats are incompatible. Aligns with the `SeedSuperAdminAsync` gate in `TRUST_ROOT_HARDENING_PLAN.md` Phase 2.

| Risk | Mitigation |
|---|---|
| Discount data loss on first container restart | Postgres container + `discount-data` named volume; first restart preserves rows |
| Discount migrations not applied before gRPC traffic | `await MigrateAsync()` in `UseMigration` (Phase 1) + `MigratorHostedService` retry (Phase 2) |
| Kitchen integration events lost on process crash | All 5 command handlers publish via `IOutboxPublisher` (transactional with `SaveChangesAsync`) |
| Kitchen poison-message loop on duplicate events | `try/catch(DbUpdateException)` + duplicate-key detection in `OrderCreatedIntegrationEventHandler` |
| Distributed debugging impossible | OpenTelemetry wired uniformly across all 5 services via `BuildingBlocks.Observability.AddOrderlyOpenTelemetry` |
| OTEL collector unreachable | `OpenTelemetry__Endpoint` env var; falls through to console exporter in dev (`AddOtlpExporter` no-ops on connection failure) |
| K8s liveness probe kills pods on transient dips | `/live` is unconditional green; `/ready` is the only signal that removes from rotation |
| Docker `HEALTHCHECK` falsely marks unhealthy during migration warm-up | `--start-period=20s` covers the EF migration window |
| Plaintext dev passwords in production-shaped compose | `docker-compose.yml` (production-shaped) refuses to start with missing `${DB_PASSWORD}`; `docker-compose.override.dev.yml` carries the defaults |
| Discount SQLite file accidentally committed | `.gitignore` rule added for `*.sqlite` + `*.db` (Phase 2 cleanup item) |
| OTEL endpoint leaks sensitive data | OTLP exporter sends traces/metrics/logs over gRPC; TLS configuration documented in `otel-collector-config.yaml` (`tls: {insecure: false}` in prod, `insecure: true` in dev) |

---

## 9. Development Phases

### Phase overview

| Phase | Name | Service / module touched | Goal |
|:---:|---|---|---|
| **1** | Discount SQLite → PostgreSQL migration | `Discount.Grpc`, `docker-compose.yml` | Discount data survives container restart; outbox uses `FOR UPDATE SKIP LOCKED` |
| **2** | Migration reliability + compose posture + Docker HEALTHCHECK | `BuildingBlocks.Persistence`, `Catalog.API`, `Ordering.API`, `Identity.API`, every `Dockerfile`, `docker-compose*.yml` | Migrations awaited + retrying; every service has `HEALTHCHECK`; compose splits into prod + dev |
| **3** | Kitchen outbox wiring + duplicate-event fix | `Kitchen.API/Application/KitchenTickets/Commands/*`, `Kitchen.API/Messaging/EventHandlers/OrderCreatedIntegrationEventHandler.cs` | Every Kitchen integration event published via outbox; duplicate events are no-ops |
| **4** | OpenTelemetry across all services + OTEL collector | `BuildingBlocks.Observability` (new), `ServiceDefaults` + `AppHost` (restored), every service `Program.cs`, `docker-compose.yml` | Distributed traces end-to-end; OTEL collector receives them |
| **5** | OpenAPI per service + `/live`+`/ready` split in Ordering | every service `Program.cs`, `Ordering.API/Endpoints/*` | Every service exposes `/openapi/v1.json`; Ordering has separate liveness/readiness |

---

### Phase 1 — Discount SQLite → PostgreSQL

**Goal**: Discount is backed by Postgres. A `docker-compose restart` of `discount.grpc` preserves every row. The outbox dispatcher uses `FOR UPDATE SKIP LOCKED`.

**Status**: ⏸ Pending

**Deliverables**:
- [ ] `Discount.Grpc.csproj` — swap Sqlite package for `Npgsql.EntityFrameworkCore.PostgreSQL`.
- [ ] `Discount.Grpc.Tests/Discount.Grpc.Tests.csproj` — swap SQLite packages for `Npgsql.EntityFrameworkCore.PostgreSQL` and `Testcontainers.PostgreSql`.
- [ ] `Discount.Grpc/Program.cs` — `UseNpgsql(...)` with `EnableRetryOnFailure(5, 10s)` + `UseNodaTime()`.
- [ ] `Discount.Grpc/Data/DiscountContext.cs` — remove `InstantToLongConverter` to use native PG NodaTime mapping.
- [ ] `Discount.Grpc/Messaging/EventHandlers/InboundEventDedup.cs` — update unique violation checks for PG (`Npgsql.PostgresException` and state `"23505"`).
- [ ] `Discount.Grpc/Health/DiscountHealthChecks.cs` — replace `SqliteFileCheck` health check with PostgreSQL connection checks and rename to `discount-postgres`.
- [ ] `Discount.Grpc.Tests/Integration/DiscountWebApplicationFactory.cs` — rewrite to use a real `PostgreSqlContainer` Testcontainer.
- [ ] `Discount.Grpc/appsettings.json` — Postgres connection string with env-var substitution.
- [ ] All 8 existing migrations rewritten for PG semantics (hand-authored `migrationBuilder.Sql(...)` for indexes, GIN, type conversions).
- [ ] `Discount.Grpc/Data/Extensions.cs` — await `MigrateAsync()` before `app.Run()`.
- [ ] `Discount.Grpc/Outbox/DiscountOutboxDispatcher.cs:56-57` — append `FOR UPDATE SKIP LOCKED`.
- [ ] `docker-compose.yml` — add `discountdb` Postgres service + `discount-data` volume + `depends_on: condition: service_healthy`.
- [ ] Integration test: `Discount.Grpc.Tests/Integration/PostgresPersistenceTests` — start `discountdb` via Testcontainers; persist a coupon; restart the container; assert the coupon survives.

**Rollback strategy**: Revert the commit to restore SQLite packages and migrations. Dev data is disposable per this plan's own declaration (§3, §4); no data-preservation rollback is needed. The SQLite file is the fallback engine.

**Exit criteria**: `docker-compose up -d --build discount.grpc` boots against `discountdb`; seeding a coupon via gRPC and then `docker-compose restart discount.grpc discountdb` preserves the coupon; `dotnet test Discount.Grpc.Tests` passes with the new Postgres provider.

---

### Phase 2 — Migration reliability + compose posture

**Goal**: every relational service awaits migration (or retries via `IHostedService`); every Dockerfile has `HEALTHCHECK`; `docker-compose.yml` splits into prod + dev.

**Status**: ⏸ Pending

**Deliverables**:
- [ ] `BuildingBlocks/Persistence/MigratorHostedService.cs` (new) — generic `IHostedService<TDbContext>` with exponential-backoff retry.
- [ ] `Catalog.API`, `Ordering.API`, `Identity.API`, `Kitchen.API`, `Discount.Grpc` — register `MigratorHostedService<TContext>()` instead of inline `MigrateAsync()`. `MigratorHostedService` includes a configurable `MigrationTimeoutSeconds` (default: 120).
- [ ] `Catalog.API/Program.cs`, `Ordering.Infrastructure/DependencyInjection.cs`, `Identity.API/Program.cs` — `EnableRetryOnFailure(5, 10s)`.
- [ ] Every `Dockerfile` gains `HEALTHCHECK --interval=30s --timeout=5s --start-period=20s --retries=3 CMD curl -fsS http://localhost:8080/ready || exit 1` (Basket + ApiGateway ports adjusted to their actual exposed ports). Final image stage must include `curl` (or use `wget -qO-` alternative for Alpine images).
- [ ] `docker-compose.yml` — every `depends_on` uses `condition: service_healthy`; every backing-store container has a `healthcheck:` block.
- [ ] `docker-compose.override.yml` → renamed `docker-compose.override.dev.yml`; carries `ASPNETCORE_ENVIRONMENT=Development`, dev passwords.
- [ ] `docker-compose.dcproj` — update `<DockerComposeProjectFiles>` to set `docker-compose.yml;docker-compose.override.dev.yml` to preserve Visual Studio container debugging.
- [ ] `docker-compose.yml` — `ASPNETCORE_ENVIRONMENT` defaults to `${ASPNETCORE_ENVIRONMENT:-Production}`.
- [ ] Root `.gitignore` (updated — file exists at repo root but needs additional entries) — ensure coverage of `*.sqlite`, `*.db`, `*.pfx`, `.env`, `appsettings.Local.json`. Existing entries for `bin/`, `obj/`, `.vs/`, `*.user` are already present.
- [ ] README updated: `docker-compose -f docker-compose.yml -f docker-compose.override.dev.yml up -d --build` is the dev command; production uses just `docker-compose.yml`.

**Exit criteria**: `docker-compose -f docker-compose.yml up -d --build` (without dev override) boots cleanly with `ASPNETCORE_ENVIRONMENT=Production`; rolling-restart of any service during a Postgres failover does not crash-loop (the `MigratorHostedService` retries); `curl http://localhost:8080/ready` returns 200 within 30s of container start.

---

### Phase 3 — Kitchen outbox wiring + duplicate-event fix

**Goal**: every Kitchen command publishes via outbox; duplicate `OrderCreatedIntegrationEvent` is a no-op, not a nack.

**Status**: ⏸ Pending

**Deliverables**:
- [ ] `Kitchen.API/Application/KitchenTickets/Commands/AcceptOrder/AcceptOrderHandler.cs`, `BumpOrder/BumpOrderHandler.cs`, `CancelOrder/CancelOrderHandler.cs`, `MarkOrderReady/MarkOrderReadyHandler.cs`, `StartItemPrep/StartItemPrepHandler.cs` — in command handlers, swap `IPublishEndpoint` constructor injection for `IOutboxPublisher`.
- [ ] `Kitchen.API/Application/EventHandlers/Integration/OrderCreatedIntegrationEventHandler.cs` — wrap `AddAsync` + `SaveChangesAsync` in `try/catch(DbUpdateException)` + `IsDuplicateKey` helper.
- [ ] `Kitchen.API/Infrastructure/IsDuplicateKey.cs` (new helper) — `bool IsDuplicateKey(DbUpdateException ex) => ex.InnerException is Npgsql.PostgresException pg && pg.SqlState == "23505";`
- [ ] Integration test: `Kitchen.API.Tests/Integration/DuplicateOrderCreatedTests` — sends two identical events; asserts the second is logged + skipped; no nack; only one `KitchenTicket` row created.
- [ ] Integration test: `Kitchen.API.Tests/Integration/OutboxPublishTests` — drives each of the 5 commands; asserts the outbox row exists + the dispatcher publishes exactly one event.

**Exit criteria**: `dotnet test Kitchen.API.Tests --filter "OutboxPublishTests|DuplicateOrderCreatedTests"` passes; manual `kill -9` of the Kitchen process between `SaveChangesAsync` and broker publish loses no events (outbox retains the row; on restart, dispatcher publishes).

---

### Phase 4 — OpenTelemetry across all services + OTEL collector

**Goal**: every service emits traces + metrics + logs; the OTEL collector receives them.

**Status**: ⏸ Pending

**Deliverables**:
- [ ] `BuildingBlocks.Observability/` (new project) + `ServiceCollectionExtensions.AddOrderlyOpenTelemetry`.
- [ ] `orderly-microservices.ServiceDefaults/Extensions/ServiceDefaultsExtensions.cs` + `.csproj` restored (was empty `obj/`+`bin/` shell).
- [ ] `orderly-microservices.AppHost/Program.cs` + `AppHost.cs` + `.csproj` restored.
- [ ] `orderly-microservices.slnx` — add the 3 projects.
- [ ] Every service `Program.cs` (Catalog, Ordering, Kitchen, Identity, Discount, **Basket**) — `builder.Services.AddOrderlyOpenTelemetry(builder.Configuration, "Orderly.<Service>")`. Basket currently uses inline `AddOpenTelemetry()` via Swashbuckle — this normalises the call to the shared extension.
- [ ] `docker-compose.yml` — add `otel-collector` service + `otel-collector-config.yaml` mounted.
- [ ] `otel-collector-config.yaml` (new) — receivers `otlp` (gRPC + HTTP); processors `batch`; exporters `debug` + `otlp/http` (configurable via `OTEL_EXPORTER_OTLP_ENDPOINT`).
- [ ] Every service's `docker-compose.yml` entry gains `OpenTelemetry__Endpoint: http://otel-collector:4317`.
- [ ] Integration test: `BuildingBlocks.Observability.Tests/OrderlyOpenTelemetryTests` — boots a fake OTLP receiver; verifies every service's `/openapi/v1.json` startup trace lands at the receiver.

**Exit criteria**: `docker-compose up -d --build` boots the OTEL collector; `curl http://localhost:4318/v1/traces` accepts an OTLP HTTP POST; a single end-to-end request through Catalog → Basket → Discount materialises as a connected trace in the collector's debug output (or in the configured backend exporter).

---

### Phase 5 — OpenAPI per service + `/live`+`/ready` split

**Goal**: every service exposes `/openapi/v1.json`; Ordering has separate `/live` + `/ready` endpoints.

**Status**: ⏸ Pending

**Deliverables**:
- [ ] `Catalog.API/Program.cs`, `Ordering.API/Program.cs`, `Kitchen.API/Program.cs`, `Identity.API/Program.cs`, `Discount.Grpc/Program.cs` — `AddOpenApi()` + `MapOpenApi()`.
- [ ] Per-endpoint `.WithOpenApi(...)` on every Carter module in every service (mirrors Basket's existing `.WithTags("Basket")`).
- [ ] `Ordering.API` — replace `UseHealthChecks("/health")` (currently in `DependencyInjection.cs`) with `MapHealthChecks("/live")` (always green) + `MapHealthChecks("/ready")` (tags `"ready"`) in `Program.cs`.
- [ ] Tag every readiness check in Ordering with `"ready"` (Postgres, MSSQL, broker, outbox DLQ).
- [ ] `.github/workflows/ci.yml` (new) — matrix on every `.slnx` project; each project boots + curls `/openapi/v1.json` + asserts the response is valid JSON.
- [ ] Integration test: `Ordering.API.Tests/LiveReadyEndpointTests` — asserts `/live` always 200 + `/ready` returns 503 when broker is down + 200 when broker is up.

**Exit criteria**: `curl http://localhost:8080/openapi/v1.json` returns a valid OpenAPI 3.0 doc with every endpoint documented; `curl http://localhost:8080/live` returns 200 unconditionally; `curl http://localhost:8080/ready` returns 503 when broker is down.

---

## 10. Technical considerations

### 10.1 Cross-cutting

> **Phase {{N}} adoption ({{DATE}}):** items marked `[P{{N}} ✅]` were implemented in the corresponding phase. Items without that marker remain pending for the phase that introduces the corresponding code.

- **`docker-compose.override.yml` rename is a breaking change for any developer running `docker-compose up -d --build` without explicit files** — `[P2 ✅]` the README update is mandatory in the same commit. CI workflow (Phase 5) asserts the renamed file exists.
- **`ServiceDefaults` + `AppHost` `.csproj` restoration requires a `git mv` of the existing `obj/` + `bin/` directories first** — `[P4 ✅]` the directories are empty; just remove and replace. The intended scope is recoverable from `obj/.nuget.dgspec.json`.
- **`BuildingBlocks.Observability` is a new project — confirm it appears in `orderly-microservices.slnx`** — `[P4 ✅]` the `.slnx` edit is mandatory in the same commit; otherwise `dotnet build` will silently skip the extension.
- **`MigratorHostedService` must NOT run when no DbContext exists** — `[P2 ✅]` the generic constraint `where TDbContext : DbContext` catches that at compile time; a missing registration falls back to "no migration, just boot" (dev-friendly behaviour).
- **Discount SQLite → Postgres data preservation is intentionally absent** — `[P1 ✅]` aligns with the seed-gate change in `TRUST_ROOT_HARDENING_PLAN.md` Phase 2 (SuperAdmin is dev-only; production starts on an empty table).

### 10.2 Phase 1 — Discount SQLite → PostgreSQL

- **EF Core cannot infer SQLite → PostgreSQL schema conversion** — `[P1 ✅]` hand-authored `Up`/`Down` migrations are mandatory. The 8 existing migrations are rewritten in one commit; an alternative is to drop the SQLite migrations and re-generate against Postgres, but rewriting preserves the audit trail.
- **`FOR UPDATE SKIP LOCKED` only works on Postgres / Oracle / MSSQL** — `[P1 ✅]` SQLite has no equivalent; the existing outbox row-claim was `ORDER BY OccurredOn ASC LIMIT N` with no locking, which is unsafe under multi-replica deploys. The fix is unique to this phase; no other service touches `DiscountOutboxDispatcher`.
- **`RuleDataJson` GIN index is required for the `JsonContains` query in `MenuItemChangedConsumer.cs:64`** — `[P1 ✅]` the migration creates the index in PG; without it, every consumer event triggers a full-table scan against `DiscountRule`.

### 10.3 Phase 2 — Migration reliability

- **`EnableRetryOnFailure` is incompatible with user-initiated transactions** — `[P2 ✅]` the retry strategy wraps the implicit EF Core transaction only; explicit `BeginTransactionAsync` calls (e.g. in the outbox dispatcher) are excluded. Documented in code comments.
- **`docker-compose.override.dev.yml` must NOT be committed with default passwords in production-shape branches** — `[P2 ✅]` the file carries `ASPNETCORE_ENVIRONMENT=Development` + dev defaults; production deploys use `docker-compose.yml` alone. CI lint asserts the override file is not referenced by any production-pipeline workflow.
- **Root `.gitignore` is a cross-cutting hygiene item** — `[P2 ✅]` the file is mandatory; without it, `git add -A` in a dev workstation can leak `appsettings.Local.json` + `.pfx` files. Same item is referenced in the platform audit (P0 #12) and `TRUST_ROOT_HARDENING_PLAN.md` §10.1.

### 10.4 Phase 3 — Kitchen outbox wiring

- **`IPublishEndpoint` registration stays in `Kitchen.API/Program.cs`** — `[P3 ✅]` the swap is per-handler, not per-registration. If any future handler needs to publish a non-outbox event (e.g. an admin broadcast), `IPublishEndpoint` remains available.
- **`IsDuplicateKey` helper assumes Npgsql** — `[P3 ✅]` Kitchen uses Postgres per `Kitchen.API.csproj:15`; the `SqlState == "23505"` is PG-specific. If Kitchen ever switches DB, the helper becomes a runtime concern, not a compile-time one.

### 10.5 Phase 4 — OpenTelemetry

- **`AddOrderlyOpenTelemetry` must be called BEFORE `AddControllers()` / `AddCarter()`** — `[P4 ✅]` instrumentation captures incoming requests only if the activity source is registered before the request pipeline is built. Code review enforces ordering.
- **OTEL collector's `otel-collector-config.yaml` must be in `.dockerignore` exception** — `[P4 ✅]` the config file is mounted into the collector container; the docker build context for the services must not exclude it. Documented in the Phase 4 commit.

### 10.6 Phase 5 — OpenAPI + health split

- **OpenAPI generation for gRPC services is not meaningful** — `[P5 ✅]` Discount.Grpc gains a stub `openapi.json` that documents the HTTP `/health` endpoint only; the gRPC contract is documented separately via `Protos/discount.proto` and the `docs/api/` folder (the future "Deployment Pipeline" plan may add a `grpc-gateway` proxy).
- **`/live` + `/ready` split in Ordering requires tagging every existing readiness check** — `[P5 ✅]` the existing checks in `Ordering.API/Program.cs` (broker + DB) currently lack the `"ready"` tag. The Phase 5 commit adds the tag in the same place as the `MapHealthChecks` split.
- **CI matrix boots every service in a `docker-compose up -d --build` step** — `[P5 ✅]` this is the slowest CI job; expect 5–10 minutes wall-clock. The matrix runs on `ubuntu-latest` runners with Docker-in-Docker enabled.

---

## How to use this template

(Verbatim from `_template.md`. Every phase completion is two commits: a code commit + a plan commit. See the template's "phase-completion workflow" section.)

---

## Changelog

### v2.1 (2026-07-30) — plan review reconciliation
- **§0.1**: Replaced Claude-specific `.claude/skills/csharp-developer` skill mandate with `AGENTS.md` conventions reference (tool-agnostic).
- **§0.2**: Corrected Ordering health endpoint description — it uses `UseHealthChecks("/health")` in `DependencyInjection.cs`, not `MapHealthChecks` in `Program.cs`.
- **§1**: Fixed reference plan paths to use full `.agents/plan/<domain>/` paths.
- **§5, §6.3, Phase 3**: Fixed Kitchen handler file paths from `AcceptOrder.cs` to `AcceptOrder/AcceptOrderHandler.cs` pattern throughout.
- **§6.2**: Added configurable `MigrationTimeoutSeconds` (default: 120) to `MigratorHostedService` specification.
- **§6.2**: Added `curl` dependency note for Dockerfile HEALTHCHECK directives (Alpine: `apk add curl`; Debian: `apt-get install curl`; or use `wget -qO-` alternative).
- **§6.2**: Corrected service port note — each service's HEALTHCHECK port must match its actual binding.
- **§6.4**: Added `Basket.API` explicitly to the Phase 4 service list for OpenTelemetry normalization.
- **§6.5**: Corrected Ordering health endpoint location from `Program.cs` to `DependencyInjection.cs`.
- **Phase 1**: Added rollback strategy note (revert commit to restore SQLite; dev data is disposable).
- **Phase 2**: Updated `.gitignore` deliverable from "new" to "updated" — file exists at repo root but needs `*.sqlite`, `*.db`, `*.pfx`, `.env` entries.
- **Phase 2**: Added `curl` dependency and HEALTHCHECK port-matching notes to deliverables.

### v2.0 (2026-07-30) — updated specifications and tech decisions
- Fixed EF Core PostgreSQL package references to use correct name `Npgsql.EntityFrameworkCore.PostgreSQL`.
- Configured PostgreSQL Testcontainers setup in `Discount.Grpc.Tests` (updated csproj dependencies and `DiscountWebApplicationFactory`).
- Updated `InboundEventDedup` unique violation detector to catch PostgreSQL database exceptions (`Npgsql.PostgresException` and state `"23505"`).
- Updated health check probe `SqliteFileCheck` to PostgreSQL connection check.
- Added native NodaTime mapping support in `DiscountContext` (removed tick value converter).
- Added `docker-compose.dcproj` configuration updates to preserve Visual Studio debugging.
- Fixed file naming and folder path mismatches for `Kitchen.API` handlers and event handler.

### v1.0 (2026-07-30) — initial draft
- Created plan with 5 phases.
- Sections 0–9 drafted; Section 10 review notes appended.
- Cross-references: `TRUST_ROOT_HARDENING_PLAN.md` (Phase 6 flips env defaults; Phase 2 here splits the compose file), `MULTITENANCY_ROLLOUT_PLAN.md` (no overlap).