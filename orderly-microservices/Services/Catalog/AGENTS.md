# Catalog Service — Agent Instructions

Supplement to root `OrderlyMicroservices/AGENTS.md`. Rules here are Catalog-specific and override nothing in the root file.

## Architecture: Vertical Slice

Features live under `Catalog.API/Features/<Entity>/`. Each slice owns its command/query, handler, validator, and DTO. Do NOT create cross-feature dependencies; use `BuildingBlocks` abstractions for shared concerns.

```
Catalog.API/
├── Features/
│   └── <Entity>/
│       ├── Create<Entity>/   ← Command + Handler + Validator
│       ├── Update<Entity>/
│       ├── Delete<Entity>/
│       └── Get<Entity>/      ← Query + Handler
├── Models/                   ← EF Core + Marten entity classes
├── Data/                     ← CatalogDbContext, migrations
├── Readers/                  ← IMenuReader, CachedMenuReader (Scrutor decorator)
├── Availability/             ← IngredientAvailabilityEngine (pure function)
├── Caching/                  ← ICatalogCache, CacheDriftRepairService, CatalogOptions
├── Messaging/                ← CatalogOutboxDispatcher, event handlers
├── Scheduling/               ← Hangfire jobs (4 recurring jobs)
└── Health/                   ← OutboxDeadLetterProbe
```

## Storage Rules

- **Relational (EF Core + Postgres `catalogdb`)**: all domain entities — `Brand`, `Restaurant`, `Table`, `MenuCategory`, `MenuItem`, `MenuItemVariation`, `Ingredient`, `Reservation`, `WalkInQueue`, `CustomerFeedback`, `MenuItemAnalytics`, `BulkOrderUpload`, `PriceHistory`, `User`, etc.
- **Marten documents (same `catalogdb`)**: exactly 4 document types — `OrderSnapshot`, `OrderModificationLog`, `OrderItemPriceAudit`, `NotificationLog`. Do NOT add relational entities as Marten documents or vice versa.
- **Redis**: menu cache (`catalog:menu:{rid}`) and ingredient cache (`catalog:ingredients:{rid}`). Always inject `ICatalogCache` — never `IDistributedCache` directly in feature handlers.

## Cache Contract

- Read side: always go through `IMenuReader` (which is Scrutor-decorated as `CachedMenuReader`). Never query `CatalogDbContext` directly for menu-tree reads.
- Invalidation: every mutation handler that touches the menu tree or ingredient tree **must** call `ICatalogCache.InvalidateMenuAsync(restaurantId)` or `ICatalogCache.InvalidateIngredientsAsync(restaurantId)` after `SaveChangesAsync`. Missing this = cache drift.
- Cache is fail-open: errors are logged at `Warning` and fall through to source. Do NOT add retry logic inside handlers.

## Domain Events (Catalog-local)

- `IDomainEvent`, `IAggregate`, `Aggregate<TId>`, `AuditableAggregate<TId>` are **Catalog-local** — they live under `Catalog.API/` and are duplicated intentionally (mirrors Kitchen). Do NOT pull them into `BuildingBlocks` without cross-team alignment.
- Domain events flow: handler raises `IDomainEvent` → `DispatchDomainEventsInterceptor` (pre-commit, EF Core `SaveChangesAsync`) dispatches via MediatR → `INotificationHandler` reacts and optionally calls `IOutboxPublisher`.

## Integration Events & Outbox

- Publishing: always `await outbox.PublishAsync(new XxxIntegrationEvent {...}, ct)` AFTER `await dbContext.SaveChangesAsync(...)`. Both calls happen inside the same handler, inside the same EF Core transaction.
- DO NOT call `IPublishEndpoint.Publish` directly. The `CatalogOutboxDispatcher` hosted service relays `outbox_messages` rows to RabbitMQ.
- Consuming: `OrderCompletedIntegrationEvent` consumer is idempotent via `processed_order_items` table (composite PK throws on duplicate; catch `PostgresException.SqlState == "23505"` and skip).
- All integration event types live in `BuildingBlocks.Messaging/Events/Catalog/`.

## Price History

- Every handler that mutates a price field (`BasePrice`, `PriceModifier`, `TaxRate`) **must** call `IPriceHistoryRecorder.RecordAsync(...)`. The recorder is Scoped; inject it via DI.
- The recorder is a no-op when `oldPrice == newPrice`. Do NOT add your own equality guard before calling it.

## Hangfire Scheduled Jobs

- 4 recurring jobs in `Catalog.API/Scheduling/`: `ReservationReminderJob`, `ReservationNoShowJob`, `WalkInNoShowJob`, `SeasonalAvailabilityJob`.
- All are **feature-flag gated** (`FeatureManagement__CatalogScheduledJobs`). The job body checks `IsEnabledAsync` as its first statement; keep that pattern.
- Cron expressions are configurable via `CatalogOptions:Hangfire` — do NOT hardcode cron strings.
- Hangfire dashboard is at `/hangfire`; requires `Admin` or `Manager` JWT role claim.

## Feature Flags

| Flag | Controls |
|---|---|
| `CatalogRedisCache` | `CacheDriftRepairService` sweep loop |
| `CatalogMenuEvents` | Publishing of `MenuItemChangedIntegrationEvent` et al. |
| `CatalogFeedbackEvents` | Publishing of `FeedbackSubmittedIntegrationEvent` |
| `CatalogScheduledJobs` | All 4 Hangfire job executions |
| `CatalogAvailabilityEngineReconcile` | `IngredientAvailabilityReconcileService` sweep |

Check the flag with `IFeatureManager.IsEnabledAsync(...)` at the start of any gated operation.

## Hard Constraints

- Do NOT add `[ApiController]` — Carter `ICarterModule` only. All routes under `/v1`.
- Do NOT use `DateTime` — `NodaTime` (`Instant`, `LocalDate`) only.
- Do NOT duplicate `Entity<TId>` or `AuditableEntity<TId>` — use from `BuildingBlocks.Entities.Contracts`.
- Do NOT add a new Marten document type without explicit design decision. The 4 existing types are intentional.
- Every new Carter endpoint must be registered in the `AddCarter()` scan (automatic) — do NOT add manual `MapGet`/`MapPost` calls.

## Test Project

`Catalog.API.Tests` — xUnit + FluentAssertions + NSubstitute + Testcontainers (Postgres + Redis). Run before committing:

```powershell
dotnet test Services/Catalog/Catalog.API.Tests
```
