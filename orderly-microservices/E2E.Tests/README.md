# E2E.Tests

Cross-service end-to-end tests for the Orderly platform. Exercises the full stack via real HTTP / gRPC / RabbitMQ — no in-process shortcuts.

## Prerequisites

- Docker Desktop (or compatible) with at least 8 GB RAM free
- `dotnet 10.0.x` SDK
- `JWT_SECRET` env var set to the same value the API services use

## Running

```powershell
# From the repo root (the directory that holds docker-compose.yml):

# 1. Build the stack — first run is ~10 minutes, subsequent runs ~90 s.
docker-compose -f orderly-microservices/docker-compose.yml `
               -f orderly-microservices/docker-compose.override.dev.yml `
               -f orderly-microservices/E2E.Tests/docker-compose.e2e.yml `
               up -d --build

# 2. Wait for /live on every container to return 200.
#    (E2E.Tests asserts this on its own; the `docker-compose ps` check
#     is just for the human running it.)

# 3. Run the E2E suite. The `[Trait("Category","E2E")]` filter is the
#    opt-in gate — without it, default test runs skip these.
$env:JWT_SECRET = "dev-secret"   # match the override's value
dotnet test orderly-microservices/E2E.Tests/E2E.Tests.csproj `
        --filter "Category=E2E" `
        --logger "console;verbosity=normal"
```

## What this suite covers

| Test | Service chain | Spec reference |
|------|---------------|----------------|
| `FullOrderLifecycle_CartToDelivered` | Basket → Ordering → Delivered | §6.7 |
| `OrderCancellation_MidLifecycle` | Basket → Ordering → Cancel + activity log | §6.7 |
| `KitchenTicketCreatedOnOrderConfirm` | Ordering → Kitchen queue projection | §6.7 |
| `DiscountAppliedToCart_ReflectedInOrder` | Discount.Grpc → Basket → Ordering bill | §6.7 |
| `FeedbackRewardRedemption` | Catalog feedback → Discount reward code → cart | §6.7 |

## Local dev shortcut

`dotnet test` against a manually-launched stack works too. Set `JWT_SECRET` in the host shell to the same value the API services see; the readiness gate in `E2ETestEnvironment.InitializeAsync` will probe `/live` on every container before any test runs (30 attempts × ~2 s = 60 s budget, matching the docker-compose `start_period` window).

## Why this exists as its own project

Phases 1–6 test each service in isolation (real HTTP + WebApplicationFactory for in-process integration; NSubstitute for unit-level). Phase 7 is the only phase that crosses service boundaries through the real broker — `BasketCheckoutEvent` → Ordering consumer → outbox → `OrderCreatedIntegrationEvent` → Kitchen consumer → SignalR broadcast — and so gets its own project, its own docker-compose override, and its own `[Trait("Category","E2E")]` opt-in gate so it doesn't accidentally run in CI on every push.
