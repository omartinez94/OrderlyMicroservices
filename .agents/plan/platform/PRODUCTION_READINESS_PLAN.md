# Production Readiness — Implementation Plan

> Scope: Closes all remaining operational, deployment, and hardening gaps required to run OrderlyMicroservices in a production environment. Consumed by platform engineers, DevOps, and service owners.

---

## Status

> **Plan version**: `v1.1` (2026-08-04) — `MINOR` increments after each phase completion; `MAJOR` is reserved for breaking restructures of the plan itself.
> **Current state**: ⏸ Not started

| Phase | Name | Status |
|:-----:|---|:-----:|
| 1 | Secrets & Environment Posture | ⏸ Pending |
| 2 | CI/CD Pipeline & Image Registry | 🔒 Blocked |
| 3 | Migration Safety & Data Seeding | 🔒 Blocked |
| 4 | Test Coverage Enforcement | 🔒 Blocked |
| 5 | Kubernetes Deployment Manifests | 🔒 Blocked |
| 6 | TLS, CORS & Operational Hardening | 🔒 Blocked |

> **Legend**: ✅ Done · 🚧 In progress · ⏸ Pending · 🔒 Blocked

> **Commit messages**: Conventional Commits (`feat:`, `docs:`, `chore:`, `test:`, `fix:`). Short subject, ≤50 chars, imperative mood, no trailing period.

> **Update rule**: **on every phase completion, the plan MUST be updated in the same commit as the phase work.** The plan is the source of truth for what was decided and what shipped; a phase that ships without a plan update is a phase that drifted. See [How to use this template](#how-to-use-this-template) for the workflow.

---

## 0. Skill & documentation conventions

### 0.1 Skill mandate — .NET Microservices Platform
> **All implementation work on this plan MUST follow the patterns established in `AGENTS.md` and existing BuildingBlocks shared libraries.**

### 0.2 Code-quality guard rails
- **Quality Gate script**: The `phase-guard.ps1` script MUST be executed at the end of every phase with the `-Quick` parameter to verify the build, tests, nullability, Dockerfiles, and dependencies:
  `pwsh ./scripts/phase-guard.ps1 -PhaseName "<Phase Name>" -Quick`
- **Nullable reference types**: All new `.csproj` files MUST have `<Nullable>enable</Nullable>` (enforced by phase-guard Step 7).
- **Directory.Build.props**: The following shared properties are required:
  ```xml
  <TargetFramework>net10.0</TargetFramework>
  <Nullable>enable</Nullable>
  <ImplicitUsings>enable</ImplicitUsings>
  ```
- **No hardcoded secrets**: `find-secrets.ps1` (phase-guard Step 4) MUST pass — zero leaked AWS keys, PATs, static JWTs, or private key blocks.
- **Dockerfile lint**: All Dockerfiles MUST pass Hadolint with `--failure-threshold error` (phase-guard Step 8).

---

## 1. Context

The OrderlyMicroservices codebase has strong architectural foundations — Clean / Vertical Slice architecture, CQRS with MediatR, JWT authentication (completed in [TRUST_ROOT_HARDENING_PLAN](./TRUST_ROOT_HARDENING_PLAN.md)), MassTransit event-driven messaging with transactional outbox (completed in [PERSISTENCE_AND_RELIABILITY_PLAN](./PERSISTENCE_AND_RELIABILITY_PLAN.md)), OpenTelemetry tracing, and Polly resilience policies.

However, a production readiness audit (2026-08-04) identified **critical operational gaps** that prevent real-world deployment:

1. **Secrets**: No vault or secret store integration. Dev credentials (`postgres:postgres`, `guest:guest`) hardcoded in `appsettings.json`. Env var fallbacks exist but no structured secrets management.
2. **CI/CD**: CI workflows exist (OpenAPI smoke, basket/discount tests) but there is **no CD pipeline** — no automated image builds, registry pushes, or deployment automation.
3. **Migration safety**: `MigratorHostedService` runs `MigrateAsync()` on app startup. With multiple replicas, this creates race conditions and lock contention during rolling deployments.
4. **Test coverage**: 12 test projects exist but only 2 (Basket, Discount) run in CI. No coverage tools or thresholds.
5. **Kubernetes**: No K8s manifests, Helm charts, or Kustomize overlays. Docker Compose + .NET Aspire only.
6. **TLS & hardening**: Internal HTTP by design, but TLS termination strategy is undocumented. CORS hardcoded to `localhost:3000`. `AllowedHosts: *` on all services.

These gaps are **operational, not architectural** — the code quality is production-grade, but the deployment infrastructure is not.

---

## 2. Goal

- **Secrets**: Eliminate all hardcoded credentials from tracked files; introduce a pluggable secrets provider pattern with a Docker Secrets implementation for compose and a Kubernetes Secrets mount for K8s.
- **CI/CD**: Full GitHub Actions pipeline from commit → build → test → image → registry → deploy (staging).
- **Migrations**: Safe out-of-band migration execution (init container / job) that cannot race with app replicas.
- **Test coverage**: All 12 test projects running in CI with Cobertura coverage reports and a minimum threshold gate.
- **Kubernetes**: Production-grade Helm chart with HPA, PDB, resource limits, ingress with TLS, and wired liveness/readiness probes.
- **Hardening**: Environment-specific CORS, locked `AllowedHosts`, documented TLS termination strategy, API versioning middleware.

---

## 3. Out of scope

- **Authentication / authorization enhancements** — fully covered by [TRUST_ROOT_HARDENING_PLAN](./TRUST_ROOT_HARDENING_PLAN.md) (v2.9, ✅ complete).
- **Messaging reliability / outbox** — fully covered by [PERSISTENCE_AND_RELIABILITY_PLAN](./PERSISTENCE_AND_RELIABILITY_PLAN.md) (v3.7, ✅ complete).
- **Quality gate script enhancements** — covered by [QUALITY_GATE_ENHANCEMENT_PLAN](./QUALITY_GATE_ENHANCEMENT_PLAN.md) (v1.1, 🚧 in progress).
- **Cloud-specific managed services** (Azure Service Bus, AWS RDS, etc.) — this plan targets portable Docker/K8s infrastructure.
- **Frontend deployment** — SPA/frontend CI/CD is a separate concern.
- **Performance benchmarking / load testing** — may be a follow-up plan.
- **Multi-region / geo-distributed deployment** — initial production targets a single region.

---

## 4. Tech decisions

| Decision | Choice | Reason |
| :--- | :--- | :--- |
| Secrets management (compose) | Docker Secrets + env var injection | No external vault dependency for self-hosted; secrets mounted as files at `/run/secrets/`. |
| Secrets management (K8s) | Kubernetes Secrets mounted as volumes | Native K8s pattern; can be swapped for External Secrets Operator later. |
| Container registry | GitHub Container Registry (GHCR) | Co-located with source; free for public repos; integrates with GitHub Actions natively. |
| K8s packaging | Helm 3 charts | Industry standard; supports values overrides per environment; integrates with ArgoCD/Flux. |
| Migration runner | K8s Job (pre-upgrade hook) / Compose init service | Decouples migration from app startup; runs exactly once before rolling update. |
| TLS termination | Ingress controller (nginx-ingress) with cert-manager | Automatic Let's Encrypt certificates; TLS terminates at ingress, internal traffic stays HTTP. |
| Coverage tooling | Coverlet + ReportGenerator | Coverlet integrates with `dotnet test`; ReportGenerator produces Cobertura XML for CI gating. |
| API versioning | `Asp.Versioning.Http` middleware | Already referenced in `.csproj` files but not wired; URL segment strategy matches existing `/api/v1/` convention. |

---

## 5. Folder layout

```
orderly-microservices/
├── .github/
│   └── workflows/
│       ├── ci.yml                          # Existing — enhanced in Phase 2
│       ├── openapi-smoke.yml               # Existing
│       ├── basket-tests.yml                # Existing
│       ├── discount-tests.yml              # Existing
│       ├── cd-build-push.yml               # NEW — build + push images to GHCR
│       ├── cd-deploy-staging.yml           # NEW — deploy to staging (Helm)
│       └── full-test-suite.yml             # NEW — runs all 12 test projects
├── deploy/
│   └── helm/
│       └── orderly/
│           ├── Chart.yaml                  # NEW — Helm chart metadata
│           ├── values.yaml                 # NEW — default values
│           ├── values-staging.yaml         # NEW — staging overrides
│           ├── values-production.yaml      # NEW — production overrides
│           └── templates/
│               ├── _helpers.tpl            # NEW — template helpers
│               ├── namespace.yaml          # NEW
│               ├── configmap.yaml          # NEW — non-secret config
│               ├── secret.yaml             # NEW — K8s secrets
│               ├── catalog-api/            # NEW — per-service manifests
│               │   ├── deployment.yaml
│               │   ├── service.yaml
│               │   └── hpa.yaml
│               ├── basket-api/             # NEW — same pattern
│               ├── ordering-api/
│               ├── discount-grpc/
│               ├── identity-api/
│               ├── kitchen-api/
│               ├── yarp-gateway/
│               │   ├── deployment.yaml
│               │   ├── service.yaml
│               │   ├── hpa.yaml
│               │   └── ingress.yaml        # NEW — TLS ingress
│               ├── infrastructure/
│               │   ├── postgresql.yaml      # NEW — StatefulSets for DBs
│               │   ├── redis.yaml
│               │   ├── rabbitmq.yaml
│               │   └── otel-collector.yaml
│               ├── migrations/
│               │   └── job.yaml            # NEW — pre-upgrade migration Job
│               └── pdb.yaml                # NEW — PodDisruptionBudgets
├── docker-compose.yml                      # MODIFIED — secrets integration
├── docker-compose.override.dev.yml         # MODIFIED — dev secrets
├── docker-compose.override.prod.yml        # MODIFIED — prod secrets mount
├── docker-compose.migrations.yml           # NEW — standalone migration runner
├── scripts/
│   ├── phase-guard.ps1                     # Existing
│   └── run-migrations.ps1                  # NEW — CLI migration runner
├── BuildingBlocks/
│   └── BuildingBlocks/
│       └── Configuration/
│           └── SecretsConfigurationSource.cs  # NEW — file-based secrets provider
├── BuildingBlocks.Persistence/
│   └── MigratorHostedService.cs            # MODIFIED — conditional skip via env var
└── Services/
    └── */appsettings.Production.json       # NEW — per-service prod config
```

---

## 6. Infrastructure Specification

> The most important section — describes *what gets built* at a level the implementer can act on.

### 6.1 Secrets management

*   **`SecretsConfigurationSource`** — An `IConfigurationSource` that reads secrets from files mounted at a configurable base path (default: `/run/secrets/`). File name maps to configuration key using `__` as section separator (e.g., file `ConnectionStrings__Database` → `ConnectionStrings:Database`). Registered early in the host builder pipeline so secrets override `appsettings.json` values.
*   **`appsettings.Production.json`** — One per service. Contains only structural keys with `${PLACEHOLDER}` values that reference environment variables. No actual credentials. Example:
    ```json
    {
      "ConnectionStrings": {
        "Database": "${DB_CONNECTION_STRING}"
      },
      "MessageBroker": {
        "Host": "${RABBITMQ_HOST}",
        "UserName": "${RABBITMQ_USER}",
        "Password": "${RABBITMQ_PASSWORD}"
      }
    }
    ```
*   **Docker Compose secrets** — `docker-compose.yml` updated with `secrets:` top-level key. Each service mounts only the secrets it needs. Dev override supplies secrets from local files in a `.secrets/` directory (git-ignored). Prod override mounts from Docker Swarm secrets or external volume.
*   **`appsettings.json` cleanup** — All hardcoded passwords (`postgres`, `guest`, `password123`, `0000...0`) replaced with placeholder values that fail loudly if not overridden. Connection strings use `{{PLACEHOLDER}}` pattern that causes a startup `InvalidOperationException` if not substituted.

### 6.2 CI/CD pipelines

*   **`full-test-suite.yml`** — GitHub Actions workflow triggered on push to `main` and PRs. Matrix strategy runs all 12 test projects. Uses Testcontainers (already configured in Basket/Discount tests) for integration tests. Produces Cobertura coverage XML. Uploads coverage as artifact.
*   **`cd-build-push.yml`** — Triggered on push to `main` after CI passes. Builds all 9 Docker images (6 services + gateway + otel-collector + migration-runner) using `docker buildx` with layer caching. Tags with `sha-<short>` and `latest`. Pushes to GHCR (`ghcr.io/<org>/orderly-<service>`).
*   **`cd-deploy-staging.yml`** — Triggered after `cd-build-push` succeeds. Uses `helm upgrade --install` with `values-staging.yaml`. Runs post-deploy smoke test hitting `/ready` endpoints through ingress. Manual approval gate before production.

### 6.3 Migration runner

*   **`docker-compose.migrations.yml`** — A compose file defining one-shot services per EF Core database (`ordering-migrate`, `discount-migrate`, `identity-migrate`, `kitchen-migrate`). Each service runs `dotnet ef database update` against the target database and exits. Depends on database containers being healthy.
*   **`run-migrations.ps1`** — PowerShell script that runs `docker compose -f docker-compose.yml -f docker-compose.migrations.yml up --abort-on-container-exit` and verifies all migration containers exit 0.
*   **`MigratorHostedService` conditional skip** — Add an environment variable `SKIP_AUTO_MIGRATION=true` that disables the hosted service. Set by default in production compose and K8s. Dev compose leaves it unset (preserving current auto-migrate behavior for local dev).
*   **Helm pre-upgrade Job** — `templates/migrations/job.yaml` runs the migration image as a Kubernetes Job with `helm.sh/hook: pre-upgrade` annotation. Uses `backoffLimit: 3` and `ttlSecondsAfterFinished: 600`. Mounts database connection secrets.
*   **Marten schema** — Catalog and Basket use Marten's auto-provisioning (`AutoCreateSchemaObjects`). In production, set to `CreateOrUpdate` (not `All`) to prevent accidental table drops.

### 6.4 Test coverage

*   **Coverlet integration** — Add `coverlet.collector` NuGet package to all 12 test projects via `Directory.Build.props` conditional on test projects. Configure `dotnet test` with `--collect:"XPlat Code Coverage"` to produce Cobertura XML.
*   **Coverage threshold** — `full-test-suite.yml` parses Cobertura XML and fails if line coverage drops below 60% (initial gate, to be raised over time). Phase-guard Step 10 (from [QUALITY_GATE_ENHANCEMENT_PLAN](./QUALITY_GATE_ENHANCEMENT_PLAN.md) Phase 4) will enforce 80% once the test suite matures.
*   **Missing CI coverage** — Wire the following test projects into `full-test-suite.yml`:
    - `BuildingBlocks.Tests`
    - `BuildingBlocks.Dev.Tests`
    - `BuildingBlocks.Observability.Tests`
    - `Catalog.API.Tests`
    - `Identity.API.Tests`
    - `Kitchen.API.Tests`
    - `Ordering.API.Tests`
    - `Ordering.Application.Tests`
    - `Ordering.Domain.Tests`
    - `Ordering.Infrastructure.Tests`

### 6.5 Kubernetes manifests (Helm)

*   **Deployments** — One per service. Configures:
    - `resources.requests` and `resources.limits` (CPU + memory) with sensible defaults in `values.yaml`.
    - `livenessProbe` → `httpGet /live` (period 30s, failure threshold 3).
    - `readinessProbe` → `httpGet /ready` (period 10s, failure threshold 3, initial delay 15s).
    - `env` from ConfigMap (non-secret) and Secret (credentials).
    - `SKIP_AUTO_MIGRATION=true` environment variable.
    - `securityContext: runAsNonRoot: true, readOnlyRootFilesystem: true`.
*   **HorizontalPodAutoscaler** — Per service. Default: min 2, max 6 replicas, target CPU 70%.
*   **PodDisruptionBudget** — Per service. `minAvailable: 1` to ensure availability during node drains.
*   **Ingress** — YARP gateway only. Configured for nginx-ingress with TLS via cert-manager annotation `cert-manager.io/cluster-issuer: letsencrypt-prod`. Terminates TLS; proxies to gateway on port 8080.
*   **StatefulSets** — PostgreSQL (catalogdb, basketdb, orderdb, discountdb, identitydb, kitchendb), Redis, RabbitMQ. Uses `volumeClaimTemplates` for persistent storage. Production deployments should consider managed database services (out of scope for this plan).
*   **Values layering** — `values.yaml` (defaults), `values-staging.yaml` (staging image tags, 1 replica, relaxed resources), `values-production.yaml` (production image tags, 2+ replicas, strict resources, real secrets).

### 6.6 TLS, CORS & hardening

*   **TLS strategy documentation** — Add `docs/architecture/tls-strategy.md` documenting: TLS terminates at ingress (nginx-ingress + cert-manager); internal pod-to-pod traffic is plain HTTP within the cluster network; `RequireHttpsMetadata = false` is intentional for internal JWT validation; gRPC uses `http://` internally.
*   **CORS** — Move `Cors:AllowedOrigins` from hardcoded `http://localhost:3000` to environment-specific configuration. Dev: `http://localhost:3000`. Staging/Prod: actual frontend domain. Validate that the origin list is non-empty on startup.
*   **AllowedHosts** — Change from `*` to the service's own hostname in `appsettings.Production.json`. Behind the YARP gateway, individual services should only accept traffic from the gateway's internal hostname.
*   **API versioning** — Wire `Asp.Versioning.Http` middleware. Configure `ApiVersionReader` to read from URL segment (matching existing `/api/v1/` convention). Set default version to `1.0`. Add `Sunset` header support for future deprecation.
*   **Response headers** — Add security headers via middleware: `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`, `Referrer-Policy: strict-origin-when-cross-origin`, `Cache-Control: no-store` on authenticated endpoints.

---

## 7. Cross-plan coordination

| This plan | Related plan | Coordination point |
|---|---|---|
| Phase 1 (Secrets) | [TRUST_ROOT_HARDENING](./TRUST_ROOT_HARDENING_PLAN.md) | Identity.API prod certificates already mount via `prod-certs-data` volume. New secrets pattern must not conflict; certificate paths remain as-is. |
| Phase 2 (CI/CD) | [QUALITY_GATE_ENHANCEMENT](./QUALITY_GATE_ENHANCEMENT_PLAN.md) | `full-test-suite.yml` should invoke `phase-guard.ps1` checks. Coordinate with QG Phase 4 (CI integration). |
| Phase 4 (Coverage) | [QUALITY_GATE_ENHANCEMENT](./QUALITY_GATE_ENHANCEMENT_PLAN.md) | Coverage threshold here starts at 60%; QG plan targets 80%. This plan establishes the tooling; QG plan raises the bar. |
| Phase 3 (Migrations) | [PERSISTENCE_AND_RELIABILITY](./PERSISTENCE_AND_RELIABILITY_PLAN.md) | Outbox tables are managed by EF Core migrations. Migration runner must handle outbox schema. `MigratorHostedService` backoff logic remains for dev; skip flag disables in prod. |

---

## 8. Security guardrails

> [!CAUTION]
> No credential — database password, broker password, JWT signing key, or certificate passphrase — may appear in any tracked file. The `find-secrets.ps1` scanner (phase-guard Step 4) is the automated enforcement.

| Risk | Mitigation |
|---|---|
| Hardcoded credentials in `appsettings.json` committed to Git | Replace with `{{PLACEHOLDER}}` values that throw on startup if not overridden; `find-secrets.ps1` blocks commits |
| Docker Compose default passwords (`postgres`, `guest`) | Move to `.secrets/` directory (git-ignored) for dev; Docker Secrets / K8s Secrets for prod |
| Migration race condition with multiple replicas | `SKIP_AUTO_MIGRATION=true` in prod; dedicated migration Job runs before app rollout |
| TLS bypass via `RequireHttpsMetadata = false` | Acceptable only for in-cluster traffic; ingress enforces TLS; document in `tls-strategy.md` |
| `AllowedHosts: *` allows host header attacks | Lock to service hostname in production config |
| CORS allows `localhost` in production | Environment-specific origin lists; fail on empty |
| Missing `X-Frame-Options` / `X-Content-Type-Options` | Security headers middleware added in Phase 6 |
| Stale container images with known CVEs | `dotnet list package --vulnerable` in CI (phase-guard Step 9); Dependabot for automated alerts |

---

## 9. Development Phases

### Phase overview

| Phase | Name | Deliverables | Goal |
|:---:|---|---|---|
| **1** | Secrets & Environment Posture | `SecretsConfigurationSource`, `appsettings.Production.json` × 6, `.secrets/` pattern, compose updates | Zero hardcoded credentials in tracked files |
| **2** | CI/CD Pipeline & Image Registry | `full-test-suite.yml`, `cd-build-push.yml`, `cd-deploy-staging.yml` | Automated path from commit to staged deployment |
| **3** | Migration Safety & Data Seeding | `docker-compose.migrations.yml`, `run-migrations.ps1`, `SKIP_AUTO_MIGRATION` env var, Marten prod config | Migrations run exactly once, before app startup, in all environments |
| **4** | Test Coverage Enforcement | Coverlet in all test projects, Cobertura reports, 60% threshold gate in CI | Every PR reports coverage; merges blocked below threshold |
| **5** | Kubernetes Deployment Manifests | Helm chart with all templates, values layering, ingress with TLS, migration Job | `helm install orderly deploy/helm/orderly -f values-staging.yaml` deploys entire system |
| **6** | TLS, CORS & Operational Hardening | TLS docs, env-specific CORS, AllowedHosts lockdown, API versioning, security headers | Production-hardened request pipeline with documented TLS strategy |

---

### Phase 1 — Secrets & Environment Posture

**Goal**: Every credential consumed by the system is injected at runtime via environment variables, file-mounted secrets, or a secrets provider — never from a tracked source file.

**Status**: ⏸ Pending (update to ✅ Done on completion, with date)

**Deliverables**:

- [ ] `BuildingBlocks/Configuration/SecretsConfigurationSource.cs` — file-based secrets configuration provider reading from `/run/secrets/`
- [ ] `appsettings.Production.json` for Catalog.API, Basket.API, Ordering.API, Discount.Grpc, Identity.API, Kitchen.API — structural keys only, no credentials
- [ ] All `appsettings.json` files cleaned: passwords replaced with `{{OVERRIDE_REQUIRED}}` placeholder that throws `InvalidOperationException` if reached
- [ ] `.secrets/` directory with `.gitignore` entry and `README.md` explaining local setup
- [ ] `docker-compose.yml` updated with `secrets:` top-level key; services mount only their required secrets
- [ ] `docker-compose.override.dev.yml` references local `.secrets/` files
- [ ] `docker-compose.override.prod.yml` updated for external secret sources
- [ ] `.env.example` cleaned of any real or default passwords
- [ ] `find-secrets.ps1` (phase-guard Step 4) passes with zero findings

**Exit criteria**: `docker compose -f docker-compose.yml -f docker-compose.override.dev.yml up -d --build` succeeds with credentials loaded from `.secrets/` files; `pwsh ./scripts/phase-guard.ps1 -PhaseName "Secrets & Environment Posture" -Quick` exits 0.

---

### Phase 2 — CI/CD Pipeline & Image Registry

**Goal**: Every push to `main` that passes CI automatically builds Docker images for all services and pushes them to GHCR with SHA-tagged versions.

**Status**: 🔒 Blocked (on Phase 1)

**Deliverables**:

- [ ] `.github/workflows/full-test-suite.yml` — matrix workflow running all 12 test projects with Testcontainers; produces Cobertura coverage artifacts
- [ ] `.github/workflows/cd-build-push.yml` — multi-image `docker buildx` build with layer caching; pushes to `ghcr.io/<org>/orderly-*` with `sha-<short>` and `latest` tags
- [ ] `.github/workflows/cd-deploy-staging.yml` — `helm upgrade --install` to staging namespace; post-deploy smoke test hitting `/ready` endpoints; manual approval gate for production
- [ ] GitHub repository settings: branch protection on `main` requiring CI pass; GHCR credentials as repository secrets
- [ ] `CODEOWNERS` file mapping service directories to team members

**Exit criteria**: A push to `main` triggers full-test-suite → cd-build-push → cd-deploy-staging in sequence; all 9 images appear in GHCR; staging `/ready` endpoints return healthy.

---

### Phase 3 — Migration Safety & Data Seeding

**Goal**: Database migrations are decoupled from application startup and execute exactly once before the new application version starts accepting traffic.

**Status**: 🔒 Blocked (on Phase 1)

**Deliverables**:

- [ ] `docker-compose.migrations.yml` — one-shot migration service per EF Core database (ordering, discount, identity, kitchen)
- [ ] `scripts/run-migrations.ps1` — orchestrates migration compose run and verifies exit codes
- [ ] `SKIP_AUTO_MIGRATION` environment variable in `MigratorHostedService` — when `true`, skips `MigrateAsync()` and logs a warning
- [ ] `docker-compose.override.prod.yml` sets `SKIP_AUTO_MIGRATION=true` for all services
- [ ] `docker-compose.override.dev.yml` leaves `SKIP_AUTO_MIGRATION` unset (preserving auto-migrate for local dev)
- [ ] Marten `StoreOptions.AutoCreateSchemaObjects` set to `AutoCreate.CreateOrUpdate` in production (not `All`)
- [ ] Ordering seed data (`InitializeDatabaseAsync`) already gated behind `IsDevelopment()` — verified, no change needed
- [ ] Identity seed data (`DataSeeder`) already gates SuperAdmin behind `IsDevelopment()` — verified, no change needed

**Exit criteria**: `pwsh ./scripts/run-migrations.ps1` runs all migrations against a fresh database set and exits 0; application services start with `SKIP_AUTO_MIGRATION=true` and do not attempt migrations; `docker compose up` in dev mode still auto-migrates.

---

### Phase 4 — Test Coverage Enforcement

**Goal**: Every PR to `main` reports code coverage across all test projects and blocks merge if coverage falls below 60%.

**Status**: 🔒 Blocked (on Phase 2)

**Deliverables**:

- [ ] `coverlet.collector` added to all 12 test projects (via `Directory.Build.props` conditional `<IsTestProject>`)
- [ ] `full-test-suite.yml` updated: `dotnet test` with `--collect:"XPlat Code Coverage"` and `--results-directory ./coverage`
- [ ] ReportGenerator step merging per-project Cobertura XML into a single summary
- [ ] Coverage threshold gate: fail workflow if line coverage < 60%
- [ ] Coverage report uploaded as GitHub Actions artifact + PR comment with summary
- [ ] Badge in `README.md` showing current coverage percentage

**Exit criteria**: PR with a test that drops coverage below 60% is blocked by the `full-test-suite` check; a passing PR shows coverage summary in PR comment.

---

### Phase 5 — Kubernetes Deployment Manifests

**Goal**: `helm install orderly deploy/helm/orderly -f values-staging.yaml` deploys the entire OrderlyMicroservices system to a Kubernetes cluster with proper probes, scaling, and TLS.

**Status**: 🔒 Blocked (on Phase 3)

**Deliverables**:

- [ ] `deploy/helm/orderly/Chart.yaml` with chart metadata and dependencies
- [ ] `deploy/helm/orderly/values.yaml` with sensible defaults for all services (image tags, replicas, resources, probe paths)
- [ ] `deploy/helm/orderly/values-staging.yaml` with staging overrides (1 replica, relaxed resources, staging secrets)
- [ ] `deploy/helm/orderly/values-production.yaml` with production overrides (2+ replicas, strict resources, production secrets)
- [ ] Per-service templates: `deployment.yaml`, `service.yaml`, `hpa.yaml` for all 7 services (6 microservices + gateway)
- [ ] Infrastructure templates: PostgreSQL StatefulSets (6 databases), Redis, RabbitMQ, otel-collector
- [ ] `templates/migrations/job.yaml` — pre-upgrade Helm hook running migration image
- [ ] `templates/pdb.yaml` — PodDisruptionBudgets (`minAvailable: 1`) for all services
- [ ] `templates/yarp-gateway/ingress.yaml` — nginx-ingress with TLS via cert-manager
- [ ] `helm template` and `helm lint` pass without errors
- [ ] `cd-deploy-staging.yml` updated to use the Helm chart

**Exit criteria**: `helm lint deploy/helm/orderly` passes; `helm template orderly deploy/helm/orderly -f deploy/helm/orderly/values-staging.yaml` renders valid YAML; deployment to a test cluster results in all pods healthy.

---

### Phase 6 — TLS, CORS & Operational Hardening

**Goal**: The production request pipeline enforces TLS at ingress, returns proper security headers, restricts CORS to configured origins, and supports API versioning.

**Status**: 🔒 Blocked (on Phase 5)

**Deliverables**:

- [ ] `docs/architecture/tls-strategy.md` documenting TLS termination strategy, internal HTTP rationale, and `RequireHttpsMetadata = false` justification
- [ ] CORS configuration moved to environment-specific settings: `Cors:AllowedOrigins` in `appsettings.Development.json` → `http://localhost:3000`; `appsettings.Production.json` → actual frontend domain(s); startup validation fails on empty origin list
- [ ] `AllowedHosts` locked to service hostname in `appsettings.Production.json` for all services
- [ ] `Asp.Versioning.Http` middleware wired: URL segment reader, default version `1.0`, `Sunset` header support
- [ ] Security headers middleware: `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`, `Referrer-Policy: strict-origin-when-cross-origin`, `Cache-Control: no-store` on authenticated endpoints
- [ ] `AGENTS.md` updated: correct test project count, reference this plan, remove stale claims about "no test projects"
- [ ] `docs/architecture/architecture.md` discrepancies resolved (Basket uses Marten not "Redis-only", Discount uses PostgreSQL not SQLite)

**Exit criteria**: `curl -I https://<staging-domain>/catalog-api/api/v1/menuitems` returns `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`, and valid TLS certificate; CORS preflight from unauthorized origin returns 403; `pwsh ./scripts/phase-guard.ps1 -PhaseName "TLS CORS Hardening" -Quick` exits 0.

---

## 10. Technical considerations

> Surfaced from a production readiness audit (2026-08-04). Each item points at a concrete risk and the relevant phase. Phase 1 should adopt the cross-cutting items before any feature code is written — they are far cheaper to retrofit then.

### 10.1 Cross-cutting

**Credential rotation strategy** — `[Pending]` Secrets provider should support rotation without service restart. File-watch on `/run/secrets/` with `IOptionsMonitor<T>` reloading is recommended but not required for initial production. Defer to a follow-up plan.

**Idempotency coverage gaps** — `[Pending]` Basket.API has full IETF `Idempotency-Key` implementation. Ordering.API, Kitchen.API, and Catalog.API write endpoints lack it. This is a resilience concern but not a production blocker; defer to a follow-up plan focused on API resilience.

**Pagination inconsistency** — `[Pending]` Catalog's `GetBrands`, `GetIngredients`, `GetMenuCategories` return unpaginated lists. Should migrate to `PaginatedResult<T>` but is a functional gap, not a production readiness blocker.

**Package cleanup** — `[Phase 6]` Replace `System.Data.SqlClient` (deprecated) with `Microsoft.Data.SqlClient` in `BuildingBlocks.Persistence.csproj`. Align Marten versions (Basket 8.37.4, Catalog 8.37.0 → 8.37.4).

### 10.2 Phase 1 — Secrets & Environment Posture

- **[Pending]** `docker-compose.override.prod.yml` currently has `${PROD_IDENTITY_CERT_PASSWORD:-changeit-please}` — must be replaced with a proper secret mount.
- **[Pending]** `.env.example` contains `ASPNETCORE_Kestrel__Certificates__Default__Password=password123` — must be removed or replaced with documentation-only placeholder.
- **[Pending]** Basket.API idempotency `SecretHex` is zeroed (`0000...0`) in `appsettings.json` — must be overridden via secrets.

### 10.3 Phase 2 — CI/CD

- **[Pending]** GitHub Actions runners need Docker-in-Docker for Testcontainers. Use `ubuntu-latest` runners with Docker pre-installed. Testcontainers Ryuk container cleanup must be configured.
- **[Pending]** Build matrix should handle the `.slnx` solution format (requires .NET 10 SDK on runners).

### 10.4 Phase 3 — Migration Safety

- **[Pending]** `MigratorHostedService` currently retries on `PostgresException` and `SqlException`. The skip-flag should be checked before any retry logic to avoid unnecessary startup delay.
- **[Pending]** Marten's `AutoCreateSchemaObjects` — verify that `CreateOrUpdate` does not drop indices or alter columns destructively.

### 10.5 Phase 5 — Kubernetes

- **[Pending]** StatefulSets for databases are a starting point but production should use managed database services (RDS, Cloud SQL, Azure Database). The Helm chart should support both modes via values toggles (`infrastructure.postgresql.enabled: true/false` + `externalDatabase.host`).
- **[Pending]** RabbitMQ should use the `rabbitmq/cluster-operator` for production K8s deployments. Initial chart uses a simple StatefulSet.
### 10.6 Suggested improvements
- Consider using `KeyPerFileConfigurationProvider` instead of a custom `SecretsConfigurationSource` for initial secret handling.
- Introduce an `ISecretsProvider` abstraction to swap Docker/K8s secrets with external vaults later.
- Document Docker‑in‑Docker requirements in the CI workflow (`ubuntu-latest` with `services: docker`).
- Ensure GitHub Actions runners install .NET 10 SDK to handle `.slnx`.
- Add ServiceAccount & RBAC to Helm chart templates and enforce `runAsNonRoot`.
- Ensure gRPC TLS termination via Ingress (cert‑manager) and update `tls‑strategy.md`.
- Plan to raise coverage threshold to 70 % after Phase 5.
- Configure Helm migration Job with `backoffLimit: 5` and `restartPolicy: OnFailure`.
- Add links to new files (`SecretsConfigurationSource.cs`, `run-migrations.ps1`) for quick reference.

---

## How to use this template

1. **Copy** this file into `.agents/plan/<your-project>.md` (the `_` prefix on `_template.md` keeps it out of the plan list).
2. **Find-and-replace** the `{{...}}` placeholders. Most projects need Sections 0–9; Section 10 is optional but recommended.
3. **Bump the version** in the Status section to `v{{MAJOR}}.{{MINOR}}` and add a Changelog entry every time the plan changes — see [Plan versioning](#plan-versioning) below.
4. **For each phase**, copy the "Phase {{N}}" subsection before starting work. After completion, append a new "Phase {{N}} implementation notes ({{DATE}})" section using the same structure.
5. **Commit messages** convention goes in the Status section. The whole plan is the source of truth for what was decided — keep it current.
6. **Drift between the plan and the code is the bug class plans exist to prevent.** When implementation reveals the plan was wrong (schema different than expected, API behaves differently), update the plan *and* the code in the same commit.

### The phase-completion workflow

> [!IMPORTANT]
> **Quality Gate Constraint:** Before finalizing any phase or committing code, the agent MUST run the quality gate script from the repository root:
> `pwsh ./scripts/phase-guard.ps1 -PhaseName "Phase Name" -Quick`
> The agent MUST wait for this execution to finish and verify that the script successfully exits with code `0`. The phase is NOT complete unless the script passes.

> **Every phase completion is two commits, not one.**

1. **Code commit** — the work itself (`feat: ...`). Do NOT touch the plan in this commit.
2. **Plan commit** — the plan update only (`docs: mark Phase N complete in production-readiness`):
   - Bump `Plan version` from `v1.N-1` → `v1.N` in the Status section.
   - Mark the phase's `[ ]` → `[x]` and update the table row.
   - Append a new `### Phase N implementation notes (DATE)` section under Section 9.
   - Update §10's "Phase N adoption" subnote to reflect what was actually adopted vs deferred.
   - Add a Changelog entry at the bottom.
   - **If you skip the plan commit, the phase is not done** — even if the code shipped. The next person to read the plan will not know what state it's in.

> Two commits keeps the diff reviewable: the code commit is just code, the plan commit is just documentation. Mixing them makes both harder to review and easier to forget.

### Plan versioning

Plans follow `vMAJOR.MINOR` semantics. The version lives in the Status section as the first line so it is the first thing a reader sees.

| Bump | When |
|---|---|
| **Minor** (`v1.0` → `v1.1`) | After each phase completion. Always paired with a Changelog entry. |
| **Major** (`v1.x` → `v2.0`) | When the plan itself is restructured: phase boundaries change, new phases added, or the goal/scope shifts significantly. Reflects that readers who knew the old plan should re-read. |
| **No bump for typos** | Fixing a typo or wording error doesn't need a version bump. The Changelog is for *meaningful* changes, not every commit. |

---

## Changelog

### v1.0 (2026-08-04) — initial draft
- Created plan with 6 phases based on production readiness audit.
- Sections 0–10 drafted.
- Scope explicitly excludes work covered by TRUST_ROOT_HARDENING (auth), PERSISTENCE_AND_RELIABILITY (messaging/outbox), and QUALITY_GATE_ENHANCEMENT (phase-guard script).
- Cross-plan coordination documented in §7.
### v1.1 (2026-08-04) — improvements added
- Added suggested improvements to the plan.
- Updated version to v1.1.

