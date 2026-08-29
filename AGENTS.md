# Agent Instructions

## Codebase Structure & Boundaries

- **Primary Code Location**: All application code lives inside the `orderly-microservices/` directory.
- **Solution File**: The project uses the `.slnx` format: `orderly-microservices/orderly-microservices.slnx`.
- **Architectural Divergence**: 
  - `Catalog.API` and `Basket.API` use **Vertical Slice Architecture** (features are grouped in folders under a single project).
  - `Ordering` uses **Clean Architecture** (separated into `.API`, `.Application`, `.Domain`, and `.Infrastructure` projects).
- **Shared Library (`BuildingBlocks`)**: Contains all shared kernel code, MediatR behaviors, exceptions, and extension methods. Do not duplicate common logic across microservices; put it here.

## Core Libraries & Patterns

- **Routing & Endpoints**: Use **Carter** for minimal APIs. Do NOT use MVC Controllers.
- **CQRS**: Use **MediatR** for Commands and Queries. Handle validation via **FluentValidation** pipeline behaviors.
- **Date & Time**: Always use **NodaTime** (e.g., `Instant`, `LocalDate`) from `BuildingBlocks` instead of standard .NET `DateTime`.
- **Variables**: Use explicit typed variables instead of `var` when possible. Exceptions: `using var`, `out var`, anonymous types, tuple deconstruction, and LINQ queries are permitted to use `var`.
- **Null Checks**: Prefer `is null` over `== null`, and `is not null` over `!= null`.
  - **Carve-out**: LINQ expression trees (`.Where(...)`, `HasQueryFilter(...)`, `ValueComparer(...)`, `HasConversion(...)` when Roslyn picks the `Expression<Func<>>` overload, `FluentValidation.RuleBuilder.Must(...)`, `FluentAssertions.IEnumerableAssertions.OnlyContain(...)` and similar) **must keep `== null` / `!= null`** because the C# compiler rejects `is null` / `is not null` inside expression trees with `CS8122`. Apply the pattern only in regular C# code (`if`, `return`, ternary, in-memory `Func<>` delegates).
- **Pattern Matching**: Prefer using C# pattern matching when possible.
  - *Example*: Use switch expressions (`obj switch { Type t => ... }`) or property patterns (`if (obj is { Property: value })`).
- **Guard Clauses**: Use early returns/guard clauses to avoid deep nesting of `if` statements.
- **Braces should not be ommited**: Always use curly braces for if and for statements does not matter if it is only online
- **string class**: When dealing with strings prefer working with the built-in string class methods and fields for instance instead of comparing a variable with an empty string using a string template, compare against `string.Empty` or use `string.IsNullOrEmpty(role)`

## Persistence Reality vs Documentation

- **Architecture Docs Warning**: `docs/architecture/architecture.md` has discrepancies with the executable code. Trust the executable code as the source of truth.
  - *Example*: Docs claim the Basket service is "Redis-only (no PostgreSQL)", but the codebase uses **Marten** for persistence alongside Redis for caching.
- **Database Abstractions**: 
  - `Catalog` and `Basket` use **Marten** (Document DB over PostgreSQL).
  - `Discount.Grpc` uses **Entity Framework Core (EF Core)** with SQLite.
- **Schema Reference**: Consult `docs/architecture/db_relational_model.mermaid` for the intended table structures and relationships.

## Developer Commands & Execution Flow

- **Running the Full System**: From the `orderly-microservices/` directory, run `docker-compose up -d --build`. This provisions databases, cache, and all APIs.
- **Running Natively (Debug Mode)**: To run .NET processes natively via Visual Studio or `dotnet run`, spin up the backing services first:
  ```bash
  cd orderly-microservices
  docker-compose up catalogdb basketdb distributedcache -d
  ```
- **Testing**: Test projects (`.Tests.csproj`) exist in the repository. Ensure you run relevant tests and fix any test failures when making changes to the codebase.
