# Ordering Service — Agent Instructions

Supplement to root `OrderlyMicroservices/AGENTS.md`. Rules here are Ordering-specific.

## Architecture: Clean Architecture

Ordering is the ONLY service in the solution that uses **Clean Architecture** (the rest use Vertical Slice). Strict layer dependencies apply:

```
Ordering.Domain           ← Pure domain model (Aggregates, Entities, ValueObjects, Events, Exceptions)
      ▲
Ordering.Application      ← MediatR Commands/Queries, Handlers, Validators, DTOs, Event Handlers
      ▲
Ordering.Infrastructure   ← EF Core DbContext, MSSQL configurations, Interceptors, Outbox Dispatcher
      ▲
Ordering.API              ← Carter Minimal API endpoints, Program.cs, Swagger configuration
```

- **Ordering.Domain** has ZERO dependencies on EF Core, ASP.NET Core, MediatR, or external libraries (except `NodaTime` and `BuildingBlocks` contracts).
- **Ordering.Application** depends only on `Ordering.Domain` and `BuildingBlocks`.
- **Ordering.Infrastructure** implements interfaces declared in `Ordering.Application` / `Ordering.Domain`.
- **Ordering.API** references `Ordering.Application` and `Ordering.Infrastructure` for DI registration, but business logic lives strictly in `Ordering.Application`.

## Database: SQL Server (UNIQUE TO ORDERING)

> [!IMPORTANT]
> **Ordering is the ONLY service backed by SQL Server (`orderdb` on port 1433).**
> All other relational services use PostgreSQL. Do NOT write Postgres-specific SQL or EF Core provider extensions here.

- DbContext: `OrderingDbContext` in `Ordering.Infrastructure/Data/`.
- Connection string key: `ConnectionStrings:OrderingDb`.
- Value converters, table mappings, and shadow properties are in `Ordering.Infrastructure/Data/Configurations/`.

## Domain Model Rules

- **Aggregate Root**: `Order` (`Ordering.Domain/Models/Order.cs`).
  - Mutations happen exclusively through domain methods (e.g., `AddOrderItem`, `Confirm`, `StartPreparing`, `MarkAsReady`, `Cancel`, etc.).
  - Never mutate child collections directly (`_orderItems` is private/encapsulated).
  - State transitions raise `IDomainEvent`s internally.
- **Value Objects**: Must be immutable records or extend `ValueObject`. Examples: `Address`, `Payment`, `OrderItemPrice`.
- **NodaTime**: All timestamps (`CreatedAt`, `LastModifiedAt`, etc.) use `Instant`. Never use `DateTime`.

## Domain Events & Outbox Pattern

- Aggregate mutations enqueue domain events into `Order.DomainEvents`.
- `DispatchDomainEventsInterceptor` (EF Core SaveChanges interceptor) pulls domain events and publishes them in-memory via MediatR before commit.
- Integration events destined for other services (e.g. `OrderCreatedIntegrationEvent`, `OrderCompletedIntegrationEvent`) are written to the `outbox_messages` SQL table within the same transaction using `IOutboxPublisher`.
- `OrderingOutboxDispatcher` (background hosted service) polls `outbox_messages` and publishes to RabbitMQ via MassTransit.
- **NEVER** call MassTransit's `IPublishEndpoint.Publish` directly from application handlers.

## Endpoints (Carter Modules)

- Carter modules live in `Ordering.API/Endpoints/`.
- All routes prefixed with `/v1`.
- Follow kebab-case URL naming.
- Carter modules map requests directly to MediatR Commands/Queries:
  ```csharp
  app.MapPost("/v1/orders", async (CreateOrderRequest request, ISender sender, CancellationToken ct) =>
  {
      CreateOrderCommand command = request.Adapt<CreateOrderCommand>();
      CreateOrderResult result = await sender.Send(command, ct);
      return Results.Created($"/v1/orders/{result.OrderId}", result);
  })
  .RequireAuthorization("orders:create")
  .WithTags("Orders");
  ```

## Hard Constraints

- Do NOT add MVC Controllers. Carter Minimal APIs only.
- Do NOT bypass `Ordering.Domain` business rules by doing direct EF Core updates from Handlers.
- Do NOT use PostgreSQL types, extensions, or syntax in migrations.
- Always validate commands using FluentValidation behaviors in `Ordering.Application`.

## Testing

Ordering contains multiple test suites across layers:
- `Ordering.Domain.Tests` — Unit tests for state transitions and invariants.
- `Ordering.Application.Tests` — Handler unit tests with NSubstitute.
- `Ordering.Infrastructure.Tests` — Integration tests for EF mappings and Outbox.
- `Ordering.API.Tests` — WebApplicationFactory tests with Testcontainers (MSSQL + RabbitMQ).

```powershell
dotnet test Services/Ordering/Ordering.Domain.Tests
dotnet test Services/Ordering/Ordering.Application.Tests
dotnet test Services/Ordering/Ordering.API.Tests
```
