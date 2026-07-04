---
name: api-design-principles
description: Master REST API design principles to build intuitive, scalable, and maintainable Minimal APIs using Carter, CQRS with MediatR, and C# .NET. Use when designing new APIs, reviewing API specifications, or establishing API design standards within the microservices.
---

# API Design Principles

Master REST API design principles to build intuitive, scalable, and maintainable APIs that align with the project's .NET Microservices architecture, leveraging Carter, MediatR, and FluentValidation.

## When to Use This Skill

- Designing new REST APIs or endpoints
- Structuring Minimal APIs using Carter
- Implementing CQRS via MediatR (Commands and Queries)
- Reviewing API specifications before implementation
- Creating developer-friendly API documentation
- Optimizing APIs for specific use cases (mobile, third-party integrations)

## Core Concepts

### 1. RESTful Design Principles

**Resource-Oriented Architecture**

- Resources are nouns (users, orders, products), not verbs
- Use HTTP methods for actions (GET, POST, PUT, PATCH, DELETE)
- URLs represent resource hierarchies
- Consistent naming conventions

**HTTP Methods Semantics:**

- `GET`: Retrieve resources (idempotent, safe). Handled by MediatR Queries.
- `POST`: Create new resources. Handled by MediatR Commands.
- `PUT`: Replace entire resource (idempotent). Handled by MediatR Commands.
- `DELETE`: Remove resources (idempotent). Handled by MediatR Commands.

### 2. Architectural Guidelines

**Carter Minimal APIs**

- Define endpoints inside `ICarterModule` implementations.
- Group endpoints by feature (Vertical Slice) or resource.
- Keep endpoint definitions clean: delegate logic to MediatR.
- Do NOT use MVC Controllers.

**CQRS with MediatR**

- **Commands**: Modify state. Must return a result or throw. Suffix with `Command`.
- **Queries**: Read state. Must not modify state. Suffix with `Query`.
- Handlers should be completely self-contained.

**Validation**

- Use FluentValidation.
- Define validators for Commands and Queries.
- Validation should be enforced via MediatR pipeline behaviors.

## REST API Design Patterns in .NET

### Pattern 1: Endpoint Definition with Carter

Use Carter to define minimal APIs cleanly.

```csharp
using Carter;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Ordering.API.Endpoints;

public class OrderEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/orders").WithTags("Orders");

        // GET: /api/v1/orders
        group.MapGet("/", async (ISender sender, [AsParameters] GetOrdersQuery query) =>
        {
            var result = await sender.Send(query);
            return Results.Ok(result);
        });

        // GET: /api/v1/orders/{id}
        group.MapGet("/{id:guid}", async (Guid id, ISender sender) =>
        {
            var result = await sender.Send(new GetOrderByIdQuery(id));
            return Results.Ok(result);
        });

        // POST: /api/v1/orders
        group.MapPost("/", async (CreateOrderCommand command, ISender sender) =>
        {
            var result = await sender.Send(command);
            return Results.Created($"/api/v1/orders/{result.Id}", result);
        });

        // PUT: /api/v1/orders/{id}
        group.MapPut("/{id:guid}", async (Guid id, UpdateOrderCommand command, ISender sender) =>
        {
            if (id != command.Id) return Results.BadRequest();
            await sender.Send(command);
            return Results.NoContent();
        });

        // DELETE: /api/v1/orders/{id}
        group.MapDelete("/{id:guid}", async (Guid id, ISender sender) =>
        {
            await sender.Send(new DeleteOrderCommand(id));
            return Results.NoContent();
        });
    }
}
```

### Pattern 2: CQRS Implementation (Commands & Queries)

Isolate the request definition, handler, and return types.

```csharp
using MediatR;
using NodaTime;

namespace Ordering.Application.Features.Orders.Commands.CreateOrder;

// Record for the command. Use NodaTime for dates.
public record CreateOrderCommand(
    Guid CustomerId,
    List<OrderItemDto> Items,
    Instant ExpectedDeliveryAt) : IRequest<CreateOrderResult>;

public record CreateOrderResult(Guid Id);

public class CreateOrderCommandHandler : IRequestHandler<CreateOrderCommand, CreateOrderResult>
{
    // Inject DB context, repositories, etc.
    public CreateOrderCommandHandler()
    {
    }

    public async Task<CreateOrderResult> Handle(CreateOrderCommand request, CancellationToken cancellationToken)
    {
        // 1. Map DTO to Domain Entity
        // 2. Perform business logic
        // 3. Save to database
        
        return new CreateOrderResult(Guid.NewGuid());
    }
}
```

### Pattern 3: Validation Pipeline

Use FluentValidation along with a MediatR Pipeline Behavior (usually implemented in BuildingBlocks). Endpoints shouldn't do manual validation.

```csharp
using FluentValidation;
using NodaTime;

namespace Ordering.Application.Features.Orders.Commands.CreateOrder;

public class CreateOrderCommandValidator : AbstractValidator<CreateOrderCommand>
{
    public CreateOrderCommandValidator(IClock clock)
    {
        RuleFor(x => x.CustomerId).NotEmpty().WithMessage("Customer ID is required.");
        RuleFor(x => x.Items).NotEmpty().WithMessage("Order must contain at least one item.");
        
        // Example: Validate NodaTime Instant
        RuleFor(x => x.ExpectedDeliveryAt)
            .GreaterThan(clock.GetCurrentInstant())
            .WithMessage("Expected delivery date must be in the future.");
    }
}
```

### Pattern 4: Consistent Error Responses

Use Problem Details (`ProblemDetails`) for error responses. The global exception handler (in BuildingBlocks) intercepts exceptions (e.g., `ValidationException`, `NotFoundException`) and returns standardized JSON.

```csharp
using Microsoft.AspNetCore.Http;

// Example Global Exception Handler mapping exceptions to Status Codes
// ValidationException -> 400 Bad Request
// NotFoundException -> 404 Not Found
// DomainException -> 400 Bad Request
```

## Best Practices

1. **Consistent Naming**: Use plural nouns for collections (`/orders`, not `/order`).
2. **Stateless**: Each request contains all necessary information.
3. **Use HTTP Status Codes Correctly**:
   - `200 OK` for successful GETs.
   - `201 Created` for successful POSTs.
   - `204 No Content` for successful PUTs and DELETEs.
   - `400 Bad Request` for validation errors.
   - `404 Not Found` when a resource is not found.
4. **Use NodaTime**: Always use `Instant`, `LocalDate`, etc., for timestamps and dates. Name timestamp properties with the `At` suffix (e.g., `CreatedAt`, `LastModifiedAt`).
5. **Pagination**: Always paginate large collections in queries.
6. **Lean Controllers/Endpoints**: Keep logic inside MediatR handlers, not in Carter modules.

## Common Pitfalls

- **Using MVC Controllers**: Violates the project's minimal API standard. Use Carter.
- **Putting Logic in Endpoints**: Always delegate to MediatR (`ISender`).
- **Ignoring HTTP Semantics**: POST for idempotent operations breaks expectations.
- **Using `DateTime` instead of `NodaTime`**: Leads to timezone bugs. Always use NodaTime `Instant` for exact timestamps.
- **Inconsistent Error Formats**: Rely on the global exception handler instead of returning custom error JSON from endpoints.
