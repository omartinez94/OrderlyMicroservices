# Identity Service Architecture Specification

## Overview

The **Identity Service** is a dedicated microservice responsible for authentication, authorization, and identity management across the Orderly Microservices ecosystem. It implements a standards-based OAuth2/OpenID Connect (OIDC) server using OpenIddict and ASP.NET Core Identity.

---

## Technology Stack

| Component              | Technology                         |
| ---------------------- | ---------------------------------- |
| **Framework**          | .NET 10 (ASP.NET Core)             |
| **Identity Framework** | ASP.NET Core Identity              |
| **OAuth2/OIDC Server** | OpenIddict                         |
| **Database**           | PostgreSQL (via EF Core + Npgsql)  |
| **API Routing**        | Carter (Minimal APIs)              |
| **Validation**         | FluentValidation                   |
| **CQRS**               | MediatR                            |
| **Object Mapping**     | Mapster                            |
| **Date/Time**          | NodaTime                           |
| **Logging**            | Serilog                            |

---

## Architecture Pattern

### OpenIddict + ASP.NET Core Identity

The service combines two complementary frameworks:

- **ASP.NET Core Identity**: Manages users, passwords, roles, lockouts, and account lifecycle.
- **OpenIddict**: Handles OAuth2/OIDC protocol logic (token issuance, refresh tokens, discovery endpoints, JWT signing).

This approach provides standards compliance (OIDC/OAuth2) while maintaining full control over the multi-tenant schema and custom permission model required by the system.

---

## Database Schema Design

### PostgreSQL Database (via EF Core)

#### Core Identity Tables (ASP.NET Identity)

- `AspNetUsers` — Base user storage
- `AspNetRoles` — Role definitions (SuperAdmin, RestaurantAdmin, Manager, KitchenManager, Waiter, KitchenStaff, Host, Cashier)
- `AspNetUserRoles` — User-to-Role junction (supports multiple roles per user)
- `AspNetUserClaims`, `AspNetUserLogins`, `AspNetUserTokens`, `AspNetRoleClaims`

#### Custom Multi-Tenant Tables

- `UserRestaurants` — Junction table linking users to one or more restaurants (multi-restaurant access)
  - `UserId` (FK → AspNetUsers.Id)
  - `RestaurantId` (FK → Catalog.Restaurants.Id)
  - `IsDefault` (boolean)

#### Permission System Tables

- `Permissions` — Granular permission definitions
  - `Id` (PK)
  - `Name` (e.g., `orders:create`, `menu:edit`, `orders:view_own`)
  - `Description`
  - `Resource` (e.g., `orders`, `menu`, `kitchen`, `users`)
  - `Action` (e.g., `create`, `edit`, `view_own`, `view_all`)
- `RolePermissions` — Role-to-Permission junction
  - `RoleId` (FK → AspNetRoles.Id)
  - `PermissionId` (FK → Permissions.Id)

#### Audit & Security Tables

- `RefreshTokens` — Refresh token metadata (managed by OpenIddict)
- `LoginAuditLog` — Authentication event tracking
  - `Id` (PK)
  - `UserId` (FK → AspNetUsers.Id, nullable)
  - `EventType` (LoginSuccess, LoginFailure, AccountLocked, Logout, TokenRefresh)
  - `IpAddress`
  - `UserAgent`
  - `Timestamp` (NodaTime Instant)
  - `Details` (JSONB for additional context)

#### OpenIddict Tables (Auto-created)

- `OpenIddictApplications` — Registered client applications
- `OpenIddictAuthorizations` — Authorization records
- `OpenIddictScopes` — OAuth2 scopes
- `OpenIddictTokens` — Token storage (for revocation/rotation)

---

## Security Patterns

### Password Management

- **Hashing Algorithm**: ASP.NET Core Identity default `PasswordHasher<TUser>` (PBKDF2 with HMAC-SHA256, 128-bit salt, 256-bit subkey, 100,000 iterations).
- **Password Policy**:
  - Minimum length: 8 characters
  - Requires at least one digit
  - Requires at least one non-alphanumeric character
  - Requires at least one uppercase letter
  - Requires at least one lowercase letter
- **Account Lockout**:
  - Trigger: 5 consecutive failed login attempts
  - Duration: 30-minute lockout
  - Lockout resets on successful login after lockout expires

### Token Management

#### Access Tokens (JWT)

- **Lifespan**: 15 minutes
- **Signing**: Asymmetric (RSA or ECDSA), keys managed by OpenIddict
- **Format**: JWT (JSON Web Token)
- **Validation**: Stateless validation by downstream microservices via OIDC discovery endpoint (`/.well-known/openid-configuration`)

#### Refresh Tokens

- **Lifespan**: 7 days
- **Storage**: Persisted in database via OpenIddict
- **Rotation**: Enabled — each refresh token use issues a new access token AND a new refresh token; the old refresh token is revoked
- **Chain Revocation**: If a revoked refresh token is reused, the entire token chain is revoked (detects potential token theft)

### Authentication Flow

#### SPA Frontend (React)

- **Flow**: Authorization Code Flow with PKCE (Proof Key for Code Exchange)
- **Steps**:
  1. React app redirects user to Identity Service login page
  2. User authenticates via Identity Service (secure cookie session)
  3. OpenIddict redirects back to React app with authorization code
  4. React app exchanges code for access token + refresh token via `/connect/token` endpoint
  5. React app stores tokens securely (in-memory or secure storage, NOT localStorage)
  6. React app includes access token in `Authorization: Bearer <token>` header for all API requests

#### Token Refresh

- When access token expires (401 response), React app uses refresh token to request new tokens
- OpenIddict validates refresh token, issues new access token + new refresh token
- Old refresh token is invalidated

### Authorization Pattern

#### Claims-Based Authorization

When OpenIddict issues a token, the following custom claims are embedded:

```json
{
  "sub": "user-uuid-123",
  "email": "waiter@restaurant.com",
  "name": "John Doe",
  "roles": ["Waiter"],
  "restaurantId": 1,
  "permissions": [
    "orders:create",
    "orders:view_own",
    "orders:modify_ordering"
  ],
  "iss": "IdentityService",
  "aud": "OrderlyMicroservices",
  "exp": 1234567890
}
```

#### Permission Naming Convention

Format: `{resource}:{action}_{scope}`

Examples:

- `orders:create`
- `orders:view_own` (only orders created by this user)
- `orders:view_all` (all orders in restaurant)
- `orders:modify_ordering` (can modify orders in "Ordering" status)
- `orders:modify_confirmed` (managers only)
- `orders:modify_ready` (requires admin approval)
- `menu:edit`
- `kitchen:update_prep_status`
- `users:assign_roles`

#### Microservice Authorization

- **Policy-Based Authorization**: Implemented in `BuildingBlocks` shared library
- **Custom Attribute**: `[RequirePermission("resource:action")]` for endpoint-level permission checks
- **Row-Level Security**: All services enforce data isolation via global query filters based on `restaurantId` claim from JWT
- **Stateless Validation**: Microservices validate JWTs without contacting Identity Service per request (using cached public keys from OIDC discovery)

### Security Infrastructure

#### HTTPS Enforcement

- All communication enforced over HTTPS (TLS 1.2+)
- Handled by Aspire/AppHost in development, reverse proxy/load balancer in production

#### Rate Limiting

- **Login Endpoints**: 5 attempts per 15 minutes per IP address
- **Token Endpoint**: Rate-limited to prevent brute-force token enumeration
- **Implementation**: `Microsoft.AspNetCore.RateLimiting` middleware

#### Audit Logging

- All authentication events logged:
  - Successful logins
  - Failed login attempts
  - Account lockouts
  - Logouts
  - Token refresh events
- Includes: User ID (if available), IP address, User-Agent, timestamp, event type
- Logged to `LoginAuditLog` table and Serilog structured logging sink

---

## User Roles

| Role                | Description                                                 |
| ------------------- | ----------------------------------------------------------- |
| **SuperAdmin**      | System-wide control, manage all restaurants                 |
| **RestaurantAdmin** | Full control within assigned restaurant(s)                  |
| **Manager**         | Operational management, approve modifications, view reports |
| **KitchenManager**  | Kitchen oversight, manage kitchen staff                     |
| **Waiter/Server**   | Create/modify orders (limited by status)                    |
| **KitchenStaff**    | View orders, update prep status                             |
| **Host/Hostess**    | Manage reservations, assign tables, walk-in queue           |
| **Cashier**         | Process payments, split bills                               |

---

## Service Configuration

### Port Assignment

- **Port**: 5007 (as specified in architecture.md)

### Docker Compose Service

```yaml
identityapi:
  build:
    context: .
    dockerfile: Services/Identity/Identity.API/Dockerfile
  ports:
    - "5007:8080"
  environment:
    - ASPNETCORE_ENVIRONMENT=Development
    - ConnectionStrings__DefaultConnection=Host=identitydb;Database=identitydb;Username=postgres;Password=postgres
  depends_on:
    - identitydb
  networks:
    - orderly-network

identitydb:
  image: postgres:16
  environment:
    - POSTGRES_DB=identitydb
    - POSTGRES_USER=postgres
    - POSTGRES_PASSWORD=postgres
  volumes:
    - identitydb_data:/var/lib/postgresql/data
  ports:
    - "5435:5432"
  networks:
    - orderly-network
```

### Environment Variables

| Variable                               | Description                                           |
| -------------------------------------- | ----------------------------------------------------- |
| `ConnectionStrings__DefaultConnection` | PostgreSQL connection string                          |
| `ASPNETCORE_ENVIRONMENT`               | Development / Staging / Production                    |
| `OpenIddict__IssuerUri`                | OIDC issuer URI (e.g., `https://localhost:5007`)      |
| `Jwt__AccessTokenLifetimeMinutes`      | Access token lifetime (default: 15)                   |
| `Jwt__RefreshTokenLifetimeDays`        | Refresh token lifetime (default: 7)                   |

---

## Integration with Other Microservices

### Downstream Service Configuration

Each microservice (Catalog, Basket, Ordering, etc.) must be configured to validate JWTs issued by the Identity Service:

```csharp
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = "https://localhost:5007";
        options.Audience = "OrderlyMicroservices";
        options.RequireHttpsMetadata = true;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            NameClaimType = OpenIdConnectConstants.Claims.Name,
            RoleClaimType = OpenIdConnectConstants.Claims.Role,
        };
    });
```

### BuildingBlocks Shared Library

The following components will be added to `BuildingBlocks`:

- `RequirePermissionAttribute` — Custom authorization attribute for permission checks
- `PermissionAuthorizationHandler` — Handler that validates `permissions` claim in JWT
- `JwtClaimExtensions` — Helper methods to extract claims (UserId, RestaurantId, Permissions)
- `TenantQueryFilter` — EF Core global query filter for multi-tenant data isolation

---

## Caching Strategy

| Cache Key                              | TTL      | Purpose                                                     |
| -------------------------------------- | -------- | ----------------------------------------------------------- |
| `identity:user:{userId}:permissions`   | 1 hour   | User permissions (avoid DB lookup on every token refresh)   |
| `identity:oidc:discovery`              | 24 hours | OIDC discovery document (cached by downstream services)     |
| `identity:role:{roleId}:permissions`   | 1 hour   | Role-to-permission mapping                                  |

**Cache Invalidation**: When roles or permissions change, explicitly invalidate the affected user/role permission cache keys. Consider Redis pub/sub for cross-instance cache invalidation in production.

---

## API Endpoints

### OIDC Standard Endpoints (OpenIddict)

| Endpoint                            | Method   | Description                                               |
| ----------------------------------- | -------- | --------------------------------------------------------- |
| `/.well-known/openid-configuration` | GET      | OIDC discovery document                                   |
| `/.well-known/jwks.json`            | GET      | JSON Web Key Set (public signing keys)                    |
| `/connect/authorize`                | GET/POST | Authorization endpoint (for Authorization Code flow)      |
| `/connect/token`                    | POST     | Token endpoint (exchange code for tokens, refresh tokens) |
| `/connect/logout`                   | POST     | End session endpoint                                      |
| `/connect/userinfo`                 | GET      | User info endpoint                                        |

### Custom Management Endpoints (Carter)

| Endpoint                          | Method | Permission                 | Description                                  |
| --------------------------------- | ------ | -------------------------- | -------------------------------------------- |
| `/api/auth/register`              | POST   | Public                     | Register new user                            |
| `/api/auth/login`                 | POST   | Public                     | Login (for non-OIDC clients)                 |
| `/api/users`                      | GET    | `users:view_all`           | List users (filtered by restaurant)          |
| `/api/users/{id}`                 | GET    | `users:view_all`           | Get user by ID                               |
| `/api/users`                      | POST   | `users:create`             | Create new user                              |
| `/api/users/{id}`                 | PUT    | `users:edit`               | Update user                                  |
| `/api/users/{id}`                 | DELETE | `users:delete`             | Delete user                                  |
| `/api/users/{id}/roles`           | PUT    | `users:assign_roles`       | Assign roles to user                         |
| `/api/users/{id}/restaurants`     | PUT    | `users:assign_restaurants` | Assign restaurants to user                   |
| `/api/roles`                      | GET    | `roles:view`               | List roles                                   |
| `/api/roles`                      | POST   | `roles:create`             | Create role                                  |
| `/api/roles/{id}/permissions`     | PUT    | `roles:edit_permissions`   | Assign permissions to role                   |
| `/api/permissions`                | GET    | `permissions:view`         | List all permissions                         |
| `/api/audit-log`                  | GET    | `audit:view`               | View authentication audit log                |

---

## Project Structure

``` bash
Services/Identity/
├── Identity.API/
│   ├── Features/
│   │   ├── Auth/
│   │   │   ├── Login/
│   │   │   ├── Register/
│   │   │   └── RefreshToken/
│   │   ├── Users/
│   │   │   ├── CreateUser/
│   │   │   ├── GetUser/
│   │   │   ├── ListUsers/
│   │   │   ├── UpdateUser/
│   │   │   └── DeleteUser/
│   │   ├── Roles/
│   │   │   ├── CreateRole/
│   │   │   ├── GetRole/
│   │   │   ├── ListRoles/
│   │   │   └── UpdateRole/
│   │   └── Permissions/
│   │       ├── ListPermissions/
│   │       └── AssignPermissions/
│   ├── Data/
│   │   ├── IdentityDbContext.cs
│   │   ├── Configurations/
│   │   └── Migrations/
│   ├── Models/
│   │   ├── ApplicationUser.cs
│   │   ├── ApplicationRole.cs
│   │   ├── UserRestaurant.cs
│   │   ├── Permission.cs
│   │   ├── RolePermission.cs
│   │   └── LoginAuditLog.cs
│   ├── Endpoints/
│   │   ├── AuthEndpoints.cs
│   │   ├── UserEndpoints.cs
│   │   ├── RoleEndpoints.cs
│   │   └── PermissionEndpoints.cs
│   ├── Extensions/
│   │   ├── ServiceCollectionExtensions.cs
│   │   └── WebApplicationExtensions.cs
│   ├── OpenIddict/
│   │   ├── OpenIddictConfiguration.cs
│   │   └── ClaimsTransformer.cs
│   ├── GlobalUsings.cs
│   ├── Program.cs
│   ├── appsettings.json
│   └── Identity.API.csproj
└── Identity.API.Tests/ (future)
```

---

## Development Phases

### Phase 1: Foundation (Week 1-2)

- Scaffold `Identity.API` project
- Configure EF Core with PostgreSQL
- Implement ASP.NET Core Identity (Users, Roles)
- Configure OpenIddict server
- Create custom entities (UserRestaurants, Permissions, RolePermissions)
- Database migrations

### Phase 2: Authentication (Week 2-3)

- Implement login/registration endpoints
- Configure Authorization Code Flow with PKCE
- Implement claims transformation (inject custom claims into JWT)
- Configure refresh token rotation
- Implement rate limiting on auth endpoints
- Audit logging

### Phase 3: Authorization (Week 3-4)

- Implement user/role/permission management endpoints
- Build `BuildingBlocks` authorization components
- Configure downstream microservices for JWT validation
- Implement row-level security filters

### Phase 4: Integration & Testing (Week 4-5)

#### Overview

Phase 4 validates the complete Identity Service through integration tests covering authentication flows, authorization enforcement, and microservice interoperability. Tests run against an ephemeral PostgreSQL container to ensure isolation and repeatability.

#### Test Project

```
Services/Identity/
└── Identity.API.Tests/
    ├── Identity.API.Tests.csproj
    ├── Auth/
    │   ├── LoginTests.cs
    │   ├── RegisterTests.cs
    │   ├── TokenEndpointTests.cs
    │   ├── RefreshTokenRotationTests.cs
    │   └── LogoutTests.cs
    ├── Users/
    │   └── UserManagementTests.cs
    ├── Roles/
    │   └── RoleManagementTests.cs
    └── Infrastructure/
        ├── IdentityApiFactory.cs
        └── TestcontainersFixture.cs
```

#### Test Categories

| Category | Tests |
|----------|-------|
| **Auth Flows** | Login (success/failure/lockout), Register, Token endpoint, Refresh rotation, Logout revocation |
| **User Management** | CRUD operations, AssignRoles, AssignRestaurants |
| **Role Management** | CRUD operations, AssignPermissions |
| **Permissions** | ListPermissions |
| **Audit Log** | GetAuditLog with event filtering |

#### Test Infrastructure

- **`IdentityApiFactory<TStartup>`**: Custom `WebApplicationFactory` that bootstraps an isolated `IHost` per test class
- **`Testcontainers.PostgreSql`**: Spins up an ephemeral PostgreSQL container per test class; connection string injected via `IConfiguration`
- **`DataSeeder`**: Runs before each test class to ensure roles, permissions, and default `SuperAdmin` user are seeded
- **FluentAssertions**: Used for readable assertion syntax

#### Test Execution

```bash
cd orderly-microservices/Services/Identity/Identity.API.Tests
dotnet test
```

#### Validation Checklist

- [ ] Access token (15-min lifespan) and refresh token (7-day with rotation) correctly issued
- [ ] Authorization Code + PKCE flow end-to-end
- [ ] JWT contains `roles`, `permissions`, `restaurantId`, and `sub` claims
- [ ] Token refresh invalidates old refresh token and issues new one
- [ ] Logout revokes refresh tokens
- [ ] Rate limiting: 5 failed login attempts triggers 30-minute lockout
- [ ] Audit log entries created for all authentication events
- [ ] Downstream services (`Catalog.API`, `Basket.API`, `Ordering.API`) validate JWTs via OIDC discovery
- [ ] `.RequirePermission(...)` policies enforced in downstream services
- [ ] Row-level security filters applied based on `restaurantId` claim
- [ ] Token issuance latency < 200ms under load
- [ ] Docker Compose service definition finalized

---

## Key Design Decisions

| Decision                             | Rationale                                                                                             |
| ------------------------------------ | ----------------------------------------------------------------------------------------------------- |
| **OpenIddict over custom JWT**       | Standards-compliant (OIDC/OAuth2), handles token lifecycle securely, no custom crypto code            |
| **ASP.NET Identity default hashing** | PBKDF2 with HMAC-SHA256 is NIST-recommended, automatically maintained by .NET team                    |
| **PostgreSQL over SQL Server**       | Aligns with existing services, superior JSONB support, no licensing costs, Marten compatibility       |
| **Authorization Code + PKCE**        | Industry standard for SPAs, prevents authorization code interception attacks                          |
| **Refresh Token Rotation**           | Detects token theft, limits window of compromise                                                      |
| **Stateless JWT Validation**         | Microservices don't need to contact Identity Service per request, improves performance and resilience |
| **Claims-based Authorization**       | Permissions embedded in JWT, no DB lookup needed per request                                          |
| **Carter for API endpoints**         | Consistent with existing microservices (Catalog, Basket), minimal API approach                        |

---

## References

- [OpenIddict Documentation](https://documentation.openiddict.com/)
- [ASP.NET Core Identity Documentation](https://docs.microsoft.com/en-us/aspnet/core/security/authentication/identity)
- [OAuth 2.1 Specification](https://oauth.net/2.1/)
- [OpenID Connect Core 1.0](https://openid.net/specs/openid-connect-core-1_0.html)
- [RFC 7519 - JSON Web Token (JWT)](https://datatracker.ietf.org/doc/html/rfc7519)
- [NIST Digital Identity Guidelines (SP 800-63)](https://pages.nist.gov/800-63-3/)

---

**Document Version**: 1.1
**Last Updated**: May 27, 2026
**Status**: Phase 4 documented, pending implementation
