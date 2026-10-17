# BuildingBlocks — Agent Instructions

Supplement to root `OrderlyMicroservices/AGENTS.md`. Rules here govern shared kernel libraries.

## Purpose & Scope

`BuildingBlocks` is the shared kernel across all Orderly microservices. It contains reusable infrastructure, cross-cutting concerns, CQRS abstractions, pipeline behaviors, and domain interfaces.

```
BuildingBlocks/
├── Authorization/     ← Permission handlers, JWT helpers, claim constants
├── Behaviors/         ← MediatR pipeline behaviors (Logging, Validation, Performance, PciSensitive)
├── Correlation/       ← Correlation ID accessor and activity middleware
├── CQRS/              ← ICommand<T>, ICommandHandler<T>, IQuery<T>, IQueryHandler<T>
├── Discounts/         ← Shared discount calculation contracts and snapshot models
├── Entities/          ← Entity<TId>, AuditableEntity<TId>, interceptors
├── Enums/             ← Common domain enums (CuisineType, OrderEnums, MenuEnums, etc.)
├── Exceptions/        ← Standard exceptions (NotFound, BadRequest, Forbidden, InternalServer)
├── Multitenancy/      ← ITenantEntity, ICurrentRestaurantProvider, ClaimsRestaurantProvider
└── Pagination/        ← PaginatedResult<T>, PaginationRequest
```

Additional shared sibling projects:
- `BuildingBlocks.Messaging` — MassTransit setup, IntegrationEvent base classes, Outbox abstractions.
- `BuildingBlocks.Observability` — OpenTelemetry instrumentation, metrics, and tracing builders.
- `BuildingBlocks.Persistence` — Shared EF Core and interceptor utilities.
- `BuildingBlocks.Dev` — Development-only fixtures, seeder utilities.

## Golden Rules for Modifying BuildingBlocks

1. **Never break existing service contracts**: Changes here affect ALL microservices (`Catalog`, `Basket`, `Ordering`, `Kitchen`, `Identity`, `Discount`). Any breaking change must be backwards-compatible or rolled out in lockstep across all services.
2. **Zero Service-Specific Logic**: Never put service-specific business logic, entities, or DTOs into BuildingBlocks. If an entity only belongs to `Catalog` (e.g. `Brand` or `MenuItem`), it stays in `Catalog.API`.
3. **No Domain Aggregates Here**: BuildingBlocks defines interfaces (`IEntity`, `IAggregate`, `ITenantEntity`) and base records, but NOT concrete domain aggregates.
4. **NodaTime is Mandatory**: Any time-related types or properties exposed in shared models must use `NodaTime.Instant` or `NodaTime.LocalDate`. Standard `System.DateTime` is forbidden.
5. **No Direct External Service Calls**: BuildingBlocks provides abstractions and middleware, not outbound HTTP or gRPC client implementations.

## Key Abstractions

- **CQRS Interfaces**:
  - Commands: `ICommand<TResponse>` / `ICommandHandler<TCommand, TResponse>`
  - Unit Commands: `ICommand` (implements `ICommand<Unit>`)
  - Queries: `IQuery<TResponse>` / `IQueryHandler<TQuery, TResponse>`
- **Validation Pipeline**: Handled automatically via `ValidationBehavior<TRequest, TResponse>` which runs FluentValidation validators before handler execution.
- **PCI Redaction**: Mark sensitive command properties or classes with `[PciSensitive]` to ensure `LoggingBehavior` redacts payload details from log sinks.
- **Multitenancy**:
  - Multi-tenant entities implement `ITenantEntity` (`Guid RestaurantId`).
  - Access current tenant via `ICurrentRestaurantProvider`.

## Testing

```powershell
dotnet test BuildingBlocks.Tests
dotnet test BuildingBlocks.Observability.Tests
dotnet test BuildingBlocks.Dev.Tests
```
