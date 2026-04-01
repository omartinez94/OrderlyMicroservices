# Agents
## AI Assistant Context & Project Rules
Whenever you are asked to implement a new feature, endpoint, or make changes to this codebase, you MUST adhere to the following architectural guidelines, libraries, and best practices:

### 1. Architectural Idea
- **Project idea**: The project is a multi-tenant restaurant management system. All the ideas for the project are in the `docs/architecture/architecture.md` file. Would be useful to read it to understand the project idea before making any changes.

### 2. Architectural Model (Single Source of Truth)
- **Database Schema**: Always read the database relational model diagram at `docs/architecture/db_relational_model.mermaid` to understand table structures, fields, and relationships before making logic or DB changes.

### 3. Core Libraries & Patterns
- **CQRS Pattern**: Use **MediatR** for defining and handling all Commands and Queries. Keep the handlers clean, separated, and focused on single responsibilities.
- **Routing & Endpoints**: Use **Carter** for minimal API endpoint definitions. Do not use traditional MVC Controllers.
- **Data Access & Persistence**: Use **Marten** as the Document DB abstraction. Understand that this uses PostgreSQL beneath the surface.
- **Validation**: Use **FluentValidation** for validating Commands and Queries before they reach the MediatR handlers. Assume validation is executed via a MediatR pipeline behavior.
- **Date & Time**: Always use **NodaTime** (e.g., `Instant`, `LocalDate`, `ZonedDateTime`) for all date and time operations, instead of standard .NET `DateTime` or `DateTimeOffset`.

### 3. Shared Library (BuildingBlocks)
- **Reusability**: All shared kernel code, standard entity contracts (like `AuditableEntity`), interface definitions, MediatR behaviors, custom exceptions, and extension methods MUST go into the `BuildingBlocks` project (`orderly-microservices/BuildingBlocks/`). 
- **No Duplication**: Do not duplicate common logic across individual microservices. If it can be shared, put it in BuildingBlocks.