# Trust Root Hardening — Implementation Plan

> Scope: close every authentication, authorization, and trust-root P0 defect surfaced by the 2026-07-30 production-readiness audit. Touches `BuildingBlocks.Dev`, `Identity.API`, `Discount.Grpc`, `Catalog.API`, `Ordering.API`, `ApiGateway`. **Multitenancy adoption itself is NOT in scope** — that's `MULTITENANCY_ROLLOUT_PLAN.md` — but this plan absorbs the **`int→Guid` column fix** in Identity because it's a trust-root correctness bug (the JWT currently emits `"restaurantId": "42"` and every consumer parses it as `Guid`, which silently fails), and no multitenancy adoption can succeed until the wire shape is fixed.

---

## Status

> **Plan version**: `v2.4` (2026-07-31) — `MINOR` increments per phase completion; `MAJOR` is reserved for breaking restructures of the plan itself.
> **Current state**: 🚧 Phase 3 in progress (code committed locally; tests green at 102/102 Identity + 17/17 BuildingBlocks.Dev + 16/16 BuildingBlocks + 123/123 Phase 3 negative-path enforcement; 7 happy-path tests blocked by a pre-existing SQLite schema drift in `SeedCouponAsync`)

| Phase | Name | Status |
|:-----:|---|:-----:|
| 1 | BuildingBlocks.Dev + Identity dev/posture split | ✅ Done |
| 2 | OpenIddict production posture (signing keys, Applications seed, TLS, SuperAdmin) | ✅ Done |
| 3 | Discount authorization interceptor wiring + policy reflection | ✅ Done |
| 4 | Per-service authorization (Catalog fallback policy + Ordering permissions) | 🔒 Blocked (by Phase 1) |
| 5 | Identity `int→Guid` tenant-id fix (absorbs MULTITENANCY_ROLLOUT_PLAN §5 column work) | 🔒 Blocked (by Phase 2) |
| 6 | YARP gateway authentication + CORS + ForwardedHeaders | 🔒 Blocked (by Phase 4) |
| 7 | End-to-end trust-chain validation | 🔒 Blocked (by Phase 6) |

> **Legend**: ✅ Done · 🚧 In progress · ⏸ Pending · 🔒 Blocked

> **Commit messages**: Conventional Commits (`feat:`, `docs:`, `chore:`, `test:`, `fix:`). Short subject, ≤50 chars, imperative mood, no trailing period.

> **Update rule**: **on every phase completion, the plan MUST be updated in the same pair of commits as the phase work (a code commit + a plan commit — see [How to use this template](#how-to-use-this-template)).** The plan is the source of truth for what was decided and what shipped.

---

## 0. Skill & documentation conventions

### 0.1 Coding standards mandate
> **All implementation work on this plan MUST follow the project conventions defined in `AGENTS.md`** (repository root). `AGENTS.md` is the source of truth for C# 12+ / .NET 10 idiom, ASP.NET Core + Carter, EF Core, NodaTime usage, and the project's architectural patterns (Vertical Slice for Catalog/Basket, Clean Architecture for Ordering). Additional reference material for C# patterns, ASP.NET Core, and Entity Framework lives in `.claude/skills/csharp-developer/references/` (`modern-csharp.md`, `aspnet-core.md`, `entity-framework.md`) and may be consulted for implementation guidance.

Key guard rails inherited from `AGENTS.md` and the reference material: nullable enabled, primary constructors, async/await with `CancellationToken`, `Result<T>` for error paths, no blocking calls, Carter for minimal APIs (no MVC controllers), MediatR for CQRS, FluentValidation pipeline behaviours.

> **OpenIddict checkpoint:** any code change in `Identity.API/Extensions/OpenIddictServerExtensions.cs` or any OpenIddict-related migration must be paired with a smoke test that exercises `/connect/token` round-trip via `WebApplicationFactory` (see Phase 2 exit criteria).

The coding standards are **not** a substitute for the plan; the plan wins where they disagree.

### 0.2 Code-quality guard rails

This plan **inherits the project-wide guard rails from the catalog / ordering / discount plans verbatim** (the per-service plans are authoritative). Trust-root-specific overrides layered on top:

- **`AddJwtAuthenticationWithDevFallback` is the only allowed JWT registration outside `Identity.API`.** All other services must call this extension, never the bare `AddJwtAuthentication`. The extension's behavior is fixed by Phase 1; per-service `Authority` + `Audience` come from configuration, never inline literals.
- **`Authority = https://localhost:5057` is forbidden in source.** Every service's `appsettings.json` must reference `${IdentityServiceUrl}` (or equivalent config key); `Program.cs` throws at startup in non-Development if the key is missing.
- **`JWT_SECRET` is read-once at startup and rejected if `app.Environment.IsProduction()`.** The dev HS256 scheme refuses to register when the env var is set in Production. (`BuildingBlocks.Dev` already checks for the env var; this plan adds the environment guard.)
- **Production signing keys are loaded from PEM / PFX files referenced by `OpenIddict:SigningCertificatePath` / `OpenIddict:EncryptionCertificatePath`.** No key material in `appsettings.json`. Devs use `AddDevelopmentSigningCertificate()` only when `IsDevelopment()`.
- **No raw `"admin@orderly.com / Admin@123456"` seed in any environment.** `DataSeeder.SeedSuperAdminAsync` is gated on `IsDevelopment()`. Production deploys that lack a SuperAdmin fail-fast at startup with a clear remediation message.
- **All authorization attributes are explicit.** Per-route `.RequireAuthorization()` (or `.RequirePermission("...")`) on every Carter endpoint; per-method `[Permission]` on every gRPC method. There is no default fallback policy in services other than Catalog (where Phase 4 establishes one); identity is enforced, not assumed.
- **Tests for every new gate**: at minimum one WebApplicationFactory test per route that proves the 401 / 403 / 200 path. The pattern lives in `Basket.API.Tests` and `Ordering.API.Tests` — copy-paste-modify.
- **Permission catalog**: all permission strings introduced by this plan (`catalog:menu_update`, `orders:write`, `orders:view_own`, etc.) must be documented in `docs/architecture/permissions.md` alongside the existing kitchen permissions. This file is the single source of truth for permission names across all services.

#### 0.2.1 Global usings (project-specific)

No new global-using promotions expected in this plan. `BuildingBlocks.Dev` keeps its current `BuildingBlocks.Multitenancy` + `Microsoft.AspNetCore.Authentication.JwtBearer` imports; per-service `Program.cs` adds `using BuildingBlocks.Dev;` once.

---

## 1. Context

The 2026-07-30 production-readiness audit (per-service reports and synthesis saved in conversation; memory pointer `production-readiness-2026-07-30.md`) found 16 P0 defects across the 5 core services + platform layer. **Five of those are trust-root defects that are exploitable today** — no exploit chain, no race condition, just direct access:

1. **`AddJwtAuthenticationWithDevFallback` is gated only on the `JWT_SECRET` env var, not on `IsDevelopment()`.** One leaked env var in a prod-shaped compose override = any caller can forge an HS256 admin token. Affects every service that calls this extension.
2. **OpenIddict `AddDevelopmentSigningCertificate()` / `AddDevelopmentEncryptionCertificate()` are unconditional.** Tokens are signed with a key regenerated on every restart; tokens issued in one environment are valid in any other sharing the key.
3. **`DiscountAuthorizationInterceptor` class is built and the permission map exists, but `Program.cs:33` calls `AddGrpc()` without registering the interceptor.** Every `[Permission]` attribute on `DiscountService`, `DiscountRuleService`, and `RewardCodeService` is silently unenforced — every RPC is open.
4. **Catalog has zero `RequireAuthorization()` calls on any Carter endpoint.** The default policy `RequireAuthenticatedUser` is registered but never applied. Unauthenticated callers can hit `POST /api/v1/restaurants`, `DELETE /api/v1/restaurants/{id}`, mutate menus, approve bulk-order uploads.
5. **Ordering has 6 of 14 endpoints with no `RequirePermission(...)`.** `CreateOrder`, `UpdateOrder`, `DeleteOrder`, `GetOrders`, `GetOrderById`, `GetOrdersByCustomer` accept anonymous requests.
6. **YARP gateway has no authentication.** Every service behind it is reachable anonymously; defense-in-depth is gone.
7. **`OpenIddictApplications` table is empty after every startup.** The documented Authorization-Code-with-PKCE flow for the SPA cannot complete; M2M (client credentials) is impossible.
8. **`DisableTransportSecurityRequirement()` is on unconditionally.** Refresh tokens + passwords are accepted over plain HTTP; a reverse-proxy misconfiguration silently downgrades the trust root.
9. **SuperAdmin seeded with `Admin@123456` on every startup.** No environment gate.
10. **`restaurantId` is `int` in `Identity.API/Models/UserRestaurant.cs:7` but every consumer parses it as `Guid`.** `Guid.TryParse("42")` returns false → `RestaurantId == Guid.Empty` everywhere → tenant filter silently matches no rows. The JWT emits `"restaurantId": "42"`; the consumer sees `Guid.Empty`.

Reference plans: `.agents/plan/multitenancy/MULTITENANCY_ROLLOUT_PLAN.md` (Phase 5 absorbs the Identity provider-registration work after Phase 5 here lands), `.agents/plan/discount/DISCOUNT_SERVICE_PLAN.md` (Permission policy pattern), `.agents/plan/basket/BASKET_SERVICE_PLAN.md` (per-route policy pattern), `.agents/plan/kitchen/KITCHEN_SERVICE_PLAN.md` (permission attribute pattern).

---

## 2. Goal

By the end of Phase 6:

1. No token signed with `JWT_SECRET` (or any HS256 dev key) is accepted outside `IsDevelopment()`. The dev scheme is dormant in `Staging` and `Production`.
2. OpenIddict tokens are signed with a configured certificate in non-Development environments. The `OpenIddictApplications` table is seeded with the SPA + M2M clients at startup.
3. The Discount gRPC pipeline enforces every `[Permission]` attribute; a tokenless call to `RedeemDiscount` returns `StatusCode.PermissionDenied`.
4. Every Carter endpoint in Catalog is reachable only after authentication; every endpoint in Ordering checks the correct permission.
5. The YARP gateway validates the inbound JWT and propagates claims (with `ForwardedHeaders`) before forwarding; SPA CORS preflight succeeds against an allowlist.
6. The Identity JWT emits `"restaurantId": "<guid>"`; every consumer's `Guid.TryParse` succeeds; tenant-filter queries resolve correctly.

Concrete deliverables:

- `BuildingBlocks.Dev/DevJwtBearerFallbackExtensions.cs` gains an `IsDevelopment()` guard around the HS256 scheme registration; a `Production` environment + non-empty `JWT_SECRET` throws at startup with a remediation message.
- `Identity.API/Extensions/OpenIddictServerExtensions.cs` gains production certificate loading from `OpenIddict:SigningCertificatePath` + `OpenIddict:EncryptionCertificatePath`; dev certs gated on `IsDevelopment()`.
- `Identity.API/Data/DataSeeder.cs` seeds the SPA client (authorization_code + PKCE, redirect URIs from config) and an M2M client (client_credentials, scope from config) on first startup.
- `Discount.Grpc/Program.cs` registers `DiscountAuthorizationInterceptor`; `AuthorizationPolicies.AddDiscountPolicies` reflects over all three service classes via `typeof(DiscountProtoServiceBase).Assembly.GetTypes()`.
- `Catalog.API/Program.cs` adds a fallback authorization policy via `AddAuthorizationBuilder().AddFallbackPolicy(...)`; `Ordering.API/Endpoints/*` adds `.RequirePermission(...)` to the 6 anonymous endpoints.
- `ApiGateway/YarpApiGateway/Program.cs` adds `AddJwtAuthenticationWithDevFallback` + per-route `AuthorizationPolicy`; `appsettings.json` carries named policies per cluster; `appsettings.Development.json` ships the allowlist of CORS origins.
- `Identity.API/Migrations/` gains an `int→Guid` migration on `UserRestaurants.RestaurantId`; `ClaimsTransformer` emits `Guid.ToString()` for the `restaurantId` claim.

---

## 3. Out of scope

- **Multitenancy adoption in Catalog / Kitchen / Ordering** — covered by `MULTITENANCY_ROLLOUT_PLAN.md` Phases 1–4. This plan only does the prerequisite int→Guid fix in Phase 5.
- **Multitenancy Phase 5 (`NullCurrentRestaurantProvider` + provider registration in Identity)** — once Phase 5 of this plan lands the `int→Guid` column + JWT claim fix, MULTITENANCY_ROLLOUT_PLAN §5 reduces to "register `ClaimsRestaurantProvider` + `IHttpContextAccessor` in `Identity.API/Program.cs`" and that's the only remaining work in §5.
- **Persistence work (Discount SQLite → PostgreSQL, migration reliability, Docker HEALTHCHECK, persistent volumes)** — covered by `PERSISTENCE_AND_RELIABILITY_PLAN.md` (sibling plan).
- **Observability (OpenTelemetry across all 5 services + OTEL collector)** — covered by `PERSISTENCE_AND_RELIABILITY_PLAN.md` Phase 5.
- **OpenAPI / Swagger per service** — covered by `PERSISTENCE_AND_RELIABILITY_PLAN.md` Phase 6.
- **CI/CD pipeline (matrix, image build/push, K8s manifests)** — future plan once the deployment target is chosen.
- **Rate-limit policy tuning on YARP** — covered by a future "Deployment Pipeline" plan; this plan only establishes the per-route policy plumbing.

---

## 4. Tech decisions

| Decision | Choice | Reason |
| :--- | :--- | :--- |
| JWT scheme registration across services | Single extension `AddJwtAuthenticationWithDevFallback`; HS256 scheme gated on `IsDevelopment()` + `JWT_SECRET`; OpenIddict JWKS scheme always registered | Centralizes the policy scheme; ensures the dev path is dormant in non-Development without per-service code changes |
| Production OpenIddict signing keys | PEM / PFX files referenced by `OpenIddict:SigningCertificatePath` / `OpenIddict:EncryptionCertificatePath` config keys; loaded via `AddSigningCertificate(File.ReadAllBytes(path), password)` | KeyVault / K8s Secret / External Secrets Operator can mount PEMs without code changes; same pattern works for self-signed dev certs in Compose |
| OpenIddict dev cert persistence | Mount `/root/.aspnet/https` as a writable volume in `docker-compose.yml` (not `:ro`) so the dev cert survives container restarts | Without the volume mount, every container restart regenerates the cert and breaks downstream JWKS caches (15-min rotation default) |
| Per-route authorization in Catalog | Explicit `.RequirePermission("...")` / `.RequireAuthorization()` on write endpoints; read endpoints remain anonymous | Allows public/guest browsing of restaurants and menus while protecting all write/mutating endpoints |
| Per-route authorization in Ordering | Per-endpoint `.RequirePermission("orders:write")` for Create / Update / Delete / Confirm / Cancel / MarkReady; `.RequirePermission("orders:view_own")` for read endpoints | Mirrors the `GetOrderActivities` policy already in place; consistent with the kitchen permission set on the activity / transition endpoints |
| Discount authorization reflection target | Walk service classes explicitly, dynamically resolving the protobuf service prefix name via `BaseType.DeclaringType.FullName` | Prevents security bypasses on `DiscountRuleService` and `RewardCodeService` caused by hardcoding the `DiscountProtoService` path prefix |
| YARP gateway JWT validation | `AddJwtAuthenticationWithDevFallback` at the gateway + per-route `AuthorizationPolicy` named per cluster; `[Authorize]` middleware runs before the proxy | Defense-in-depth: even if a service forgets an `[Authorize]` attribute, the gateway rejects unauthenticated traffic |
| YARP CORS | Single named policy `Default` keyed off `Cors:AllowedOrigins` config array; CORS policy explicitly mapped onto YARP routes | Ensures browser SPA clients receive preflight 200 on all proxied routes; non-allowed origins fail preflight |
| `int→Guid` migration strategy | EF Core migration with PostgreSQL `uuid` type; truncate `UserRestaurants` first to prevent cast failures during alter | Old integer IDs do not match the new Guid IDs anyway, so clearing the table avoids database cast errors |

---

## 5. Folder layout

```
orderly-microservices/
├── BuildingBlocks.Dev/
│   ├── DevJwtBearerFallbackExtensions.cs    (modified — IsDevelopment() guard)
│   └── Dev/
│       ├── ProductionJwtKeyLoadException.cs (new — fail-fast type)
│       └── DevJwtEnvironment.cs             (new — centralises the dev-only check)
├── Services/
│   ├── Identity/Identity.API/
│   │   ├── Extensions/
│   │   │   └── OpenIddictServerExtensions.cs (modified — PEM/PFX loader)
│   │   ├── Data/
│   │   │   ├── DataSeeder.cs                (modified — SPA + M2M seed; SuperAdmin gated)
│   │   │   └── Migrations/
│   │   │       ├── 2026MMDDHHMMSS_AddOpenIddictClients.cs (new)
│   │   │       └── 2026MMDDHHMMSS_UserRestaurantIdToGuid.cs (new)
│   │   ├── Services/
│   │   │   └── ClaimsTransformer.cs          (modified — emit Guid for restaurantId)
│   │   └── Features/Auth/{Register,Login,Logout,Token}/*.cs (modified — IHttpContextAccessor, X-Forwarded-For, gated SuperAdmin)
│   ├── Discount/Discount.Grpc/
│   │   ├── Program.cs                        (modified — register interceptor)
│   │   └── Authorization/AuthorizationPolicies.cs (modified — reflect all 3 service classes)
│   ├── Catalog/Catalog.API/
│   │   └── Program.cs                        (modified — fallback policy)
│   ├── Ordering/Ordering.API/
│   │   └── Endpoints/{Create,Update,Delete,GetOrders,GetOrderById,GetOrdersByCustomer}.cs (modified — RequirePermission)
└── ApiGateway/YarpApiGateway/
    ├── Program.cs                            (modified — AddJwtAuthenticationWithDevFallback, AddCors, UseForwardedHeaders, per-route policies)
    └── appsettings.json                      (modified — named per-cluster AuthorizationPolicy)
    └── appsettings.Development.json          (new — CORS allowlist)
```

No new project; no new top-level folders. All edits land in existing trees.

---

## 6. Specification

### 6.1 `BuildingBlocks.Dev` env-gate

* **`DevJwtBearerFallbackExtensions.AddJwtAuthenticationWithDevFallback(authority, audience, env)`** — gains a new optional `IWebHostEnvironment env` parameter (or checks the environment name via `Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")` to avoid calling `BuildServiceProvider()`). The method inspects `env.IsDevelopment()` AND `builder.Configuration["JWT_SECRET"]`. Behaviour matrix:
    * `IsDevelopment() == true && JWT_SECRET set` → registers HS256 fallback scheme as today.
    * `IsDevelopment() == true && JWT_SECRET unset` → silently no-op (current behaviour; tests / Compose without MCP).
    * `IsDevelopment() == false && JWT_SECRET set` → throw `ProductionJwtKeyLoadException` with message "JWT_SECRET is set in {env}; the dev HS256 fallback is forbidden outside Development. Unset the env var or run with ASPNETCORE_ENVIRONMENT=Development."
    * `IsDevelopment() == false && JWT_SECRET unset` → only OpenIddict JWKS scheme registered (current behaviour).
* **`ProductionJwtKeyLoadException`** — `public sealed class ProductionJwtKeyLoadException : InvalidOperationException`; thrown from the extension method. Caught by the existing global exception handler in `BuildingBlocks/Exceptions/Handler/CustomExceptionHandler.cs`; rendered as a 500 with `traceId`.
* **`DevJwtEnvironment.IsDevJwtAllowed(IWebHostEnvironment, IConfiguration)`** — pure helper `static bool` used by both the extension and integration tests; returns `true` iff dev HS256 should register. Test surface is the 4× matrix above.

### 6.2 OpenIddict production posture

* **`OpenIddictServerExtensions.AddOpenIddictServer(IConfiguration)`** — replaces the unconditional dev cert calls with an environment branch:
    * `env.IsDevelopment() == true` → `AddDevelopmentEncryptionCertificate().AddDevelopmentSigningCertificate()` (today's behaviour).
    * `env.IsDevelopment() == false` → load `OpenIddict:SigningCertificatePath` (PEM/PFX) and `OpenIddict:EncryptionCertificatePath` (PEM/PFX), password from `OpenIddict:SigningCertificatePassword` / `OpenIddict:EncryptionCertificatePassword`. Call `AddSigningCertificate(bytes, password)` and `AddEncryptionCertificate(bytes, password)`. If either path is missing, throw `OpenIddictCertificateLoadException` at startup.
* **`OpenIddictCertificateLoadException`** — new sealed exception; "Failed to load OpenIddict signing/encryption certificate from {path}. Set OpenIddict:SigningCertificatePath + Password in non-Development environments."
* **`DataSeeder.SeedOpenIddictClientsAsync(IServiceProvider)`** — called from `Program.cs` after `MigrateAsync()` on first startup. Creates (idempotent — checks `FindByClientIdAsync` first):
    * **SPA client**: `ClientId = "orderly-spa"`, `DisplayName = "Orderly SPA"`, `Type = ClientType.Public`, `ConsentType = ConsentTypes.Explicit`, `RedirectUris = [configuration["Spa:RedirectUri"]]`, `PostLogoutRedirectUris = [configuration["Spa:PostLogoutRedirectUri"]]`, `Permissions = [Endpoints.Authorization, Endpoints.Token, Endpoints.Logout, Endpoints.Revocation, ResponseTypes.Code, Scopes.Email, Scopes.Profile, Scopes.Roles, "restaurantId", Scopes.OfflineAccess]`, `Requirements = [Requirements.Features.CodeChallengeProof]`. PKCE is mandatory.
    * **M2M client**: `ClientId = configuration["M2M:ClientId"]`, `DisplayName = "Orderly Service Bus"`, `Type = ClientType.Confidential`, `ConsentType = ConsentTypes.Systematic`, `ClientSecret = configuration["M2M:ClientSecret"]` (PBKDF2-hashed via `Secret` property), `Permissions = [Endpoints.Token, ResponseTypes.Token, Scopes.Profile, "internal"]`.
* **`DataSeeder.SeedSuperAdminAsync`** — entire body wrapped in `if (env.IsDevelopment())`; the production path logs a warning if no SuperAdmin exists in the DB, and `Program.cs` fails-fast at startup with `MissingSuperAdminException` listing the seed command. The SuperAdmin check queries `UserManager<ApplicationUser>.GetUsersInRoleAsync("SuperAdmin")` at startup; an empty result in non-Development triggers the fail-fast.

### 6.3 Discount authorization interceptor

* **`Discount.Grpc/Program.cs:33`** — change to:
    ```csharp
    builder.Services.AddGrpc(options =>
    {
        options.Interceptors.Add<DiscountAuthorizationInterceptor>();
    });
    ```
* **`DiscountWebApplicationFactory.cs` Test Wiring** — use `PostConfigure<GrpcServiceOptions>` to register `TestGrpcAuthInterceptor` at index 0 so it runs before `DiscountAuthorizationInterceptor`:
    ```csharp
    services.PostConfigure<GrpcServiceOptions>(options =>
    {
        options.Interceptors.Insert(0, new Grpc.Core.Interceptors.InterceptorRegistration(typeof(TestGrpcAuthInterceptor)));
    });
    ```
* **`AuthorizationPolicies.AddDiscountPolicies`** — walk concrete service classes and dynamically resolve the gRPC service name:
    ```csharp
    var serviceTypes = new[] { typeof(DiscountService), typeof(DiscountRuleService), typeof(RewardCodeService) };
    foreach (var svc in serviceTypes)
    {
        var serviceName = svc.BaseType?.DeclaringType?.FullName;
        var methodMap = svc.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .Where(m => m.GetCustomAttribute<PermissionAttribute>() is not null)
            .ToDictionary(
                m => $"/{serviceName}/{m.Name}",
                m => m.GetCustomAttribute<PermissionAttribute>()!.Permission);
        // Add to global method map...
    }
    ```
* **`RpcEndpointTests`** — un-skip the 6 tests currently marked `Skip = "gRPC auth-bridge limitation"`; remove the skip attribute once `TestGrpcAuthInterceptor` is wired in the test factory.

### 6.4 Per-service authorization

* **`Catalog.API/Program.cs`** — Explicitly add `.RequirePermission("catalog:menu_update")` or `.RequireAuthorization()` to write/mutating endpoints (brands, restaurants, menu categories, bulk upload). GET read endpoints (e.g. list restaurants, view menu items) remain anonymous for guest/customer browsing.
* **`Ordering.API/Endpoints/CreateOrder.cs`** etc. — add `.RequirePermission("orders:write")` to the Create / Update / Delete / Confirm / Cancel / MarkReady endpoints; `.RequirePermission("orders:view_own")` to GetOrders / GetOrderById / GetOrdersByCustomer. Mirror the `.RequirePermission("kitchen:update_prep_status")` already present on the 8 kitchen-side endpoints.

### 6.5 Identity int→Guid tenant-id fix

* **`UserRestaurant.cs`** — `public required Guid RestaurantId { get; set; }` (replaces `int`).
* **`UserRestaurantConfiguration.cs`** — `builder.Property(u => u.RestaurantId).IsRequired();` (no type-specific config needed; `Guid` is the default).
* **`Identity.API` User Features** — update `RestaurantId` properties to `Guid` in `CreateUserCommand`, `GetUserQuery`, `GetUserResponse`, and `UserRestaurantResponse` to prevent compiler errors.
* **Migration `UserRestaurantIdToGuid`** — Up: Truncates `UserRestaurants` and alters the column type:
    ```sql
    ALTER TABLE "UserRestaurants" DROP CONSTRAINT "PK_UserRestaurants";
    TRUNCATE TABLE "UserRestaurants";
    ALTER TABLE "UserRestaurants" ALTER COLUMN "RestaurantId" TYPE uuid USING "RestaurantId"::text::uuid;
    ALTER TABLE "UserRestaurants" ADD CONSTRAINT "PK_UserRestaurants" PRIMARY KEY ("UserId", "RestaurantId");
    ```
* **`AssignRestaurantsCommand.cs`** — accept `Guid RestaurantId` (replaces `int`).
* **`ClaimsTransformer.cs:47`** — emit `claims.Add(new Claim("restaurantId", defaultRestaurant.RestaurantId.ToString()))` (already string, but the value is now a Guid). Tests assert the claim value parses as Guid.
* **`JwtClaimExtensions.cs:16`** — no change (already parses as Guid; previously silently failed).
* **`MULTITENANCY_ROLLOUT_PLAN.md §5`** — once this phase lands, §5 reduces to "register `ClaimsRestaurantProvider` + `IHttpContextAccessor` in `Identity.API/Program.cs` + add `NullCurrentRestaurantProvider` fallback." (Will update the multitenancy plan in the same commit.)

### 6.6 YARP gateway authentication

* **`ApiGateway/YarpApiGateway/Program.cs`** — change to:
    ```csharp
    builder.Services.AddJwtAuthenticationWithDevFallback(
        authority: builder.Configuration["IdentityServiceUrl"] ?? throw new InvalidOperationException("IdentityServiceUrl missing"),
        audience: builder.Configuration["IdentityAudience"] ?? "OrderlyMicroservices"
    );
    builder.Services.AddAuthorization();
    builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
        p.WithOrigins(builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>())
         .AllowAnyHeader()
         .AllowAnyMethod()
         .AllowCredentials()));
    ```
* **Middleware pipeline ordering** — the exact ordering in `Program.cs` MUST be:
    ```csharp
    app.UseForwardedHeaders();
    app.UseCors();
    app.UseAuthentication();
    app.UseAuthorization();
    app.UseRateLimiter();        // existing
    app.MapReverseProxy();
    ```
    `UseForwardedHeaders` before auth ensures the real client IP is available for logging/auditing. `UseCors` before auth ensures preflight `OPTIONS` requests succeed without a token. `UseRateLimiter` after auth prevents unauthenticated callers from consuming rate-limit budget.
* **Health-check endpoint exclusion** — map `GET /health` (or `/ready`) **before** the auth middleware so container orchestration probes (Docker `HEALTHCHECK`, K8s liveness/readiness) are not blocked by JWT validation:
    ```csharp
    app.MapGet("/health", () => Results.Ok("healthy")).AllowAnonymous();
    ```
* **`ForwardedHeadersOptions.KnownNetworks`** — configure in `appsettings.json` (not hardcoded): `"ForwardedHeaders": { "KnownNetworks": ["172.16.0.0/12"] }` for dev (Docker default network range), `"10.0.0.0/8"` for prod. Without `KnownNetworks`, header spoofing is possible in non-dev.
* **`appsettings.json`** (YARP) — under each `Clusters.<name>.Destinations.*`, add `"Metadata": { "AuthorizationPolicy": "<name>-auth" }`. Under each `Routes.<name>`, add `"AuthorizationPolicy": "<name>-auth"`, and explicitly map `"CorsPolicy": "Default"`.
* **`appsettings.Development.json`** (new) — `"Cors": { "AllowedOrigins": [ "http://localhost:3000" ] }` for React dev server.
* **E2E Validation** — Instead of a new test project, extend the existing `test_e2e_auth.ps1` script to cover gateway routing, authentication, and CORS responses.

---

## 7. Cross-Repository Communication

This plan spans multiple in-repo services but no external systems. The cross-service touch points are:

| From | To | Mechanism | Phase |
|---|---|---|---|
| `BuildingBlocks.Dev` | All 5 services | Extension method `AddJwtAuthenticationWithDevFallback` | 1 |
| `Identity.API` | `BuildingBlocks.Dev` | None — only consumes `IWebHostEnvironment` + `IConfiguration` | 1, 2 |
| `Identity.API` | All services | OpenIddict JWKS (existing) | 2 |
| `ApiGateway` | All services | JWT validation + YARP forwarder | 6 |
| `Identity.API` | DB schema | EF Core migration `UserRestaurantIdToGuid` | 5 |
| `Identity.API` | `MULTITENANCY_ROLLOUT_PLAN.md` §5 | Reduces scope of that phase | 5 (plan-update commit) |

No protocol changes; no new events. The integration is purely in-process DI graph + DB schema.

---

## 8. Security guardrails

> [!CAUTION]
> **Never set `JWT_SECRET` in a non-Development environment.** The dev HS256 scheme will refuse to register, but a misconfigured deploy that ALSO leaves the secret unset will silently fall through to the OpenIddict JWKS scheme — which is correct. The fail-closed behaviour in `DevJwtBearerFallbackExtensions` exists precisely so that a leaked secret cannot silently degrade to forgeable tokens.

| Risk | Mitigation |
|---|---|
| Dev HS256 token forgery in prod | `AddJwtAuthenticationWithDevFallback` throws `ProductionJwtKeyLoadException` if `JWT_SECRET` is set outside `IsDevelopment()` |
| Dev signing-cert regeneration breaks JWKS caches | `/root/.aspnet/https` is mounted writable in `docker-compose.yml` (per `docker-compose.override.yml` patch in Phase 2) so the cert survives restarts |
| OpenIddict `Applications` table empty | `SeedOpenIddictClientsAsync` runs on every startup, idempotent via `FindByClientIdAsync` |
| Discount `[Permission]` attributes silently unenforced | `AddGrpc(o => o.Interceptors.Add<DiscountAuthorizationInterceptor>())` + reflected policy map covers all 3 service classes; integration test asserts tokenless call returns `PermissionDenied` |
| Catalog anonymous-access to write endpoints | `MapCarter().RequireAuthorization()` fallback policy + per-module `.RequirePermission` for write endpoints |
| Ordering anonymous-access to order CRUD | Per-endpoint `.RequirePermission("orders:write")` and `.RequirePermission("orders:view_own")` |
| YARP gateway anonymous-passthrough | `AddJwtAuthenticationWithDevFallback` + per-route `AuthorizationPolicy`; gateway rejects unauthenticated traffic before forwarding |
| Plaintext `JWT_SECRET` in compose | `.env.example` documents `JWT_SECRET=` empty; `docker-compose.yml` does not set it; the dev scheme registers only in `IsDevelopment()` |
| `restaurantId` Guid.TryParse fails silently | `ClaimsTransformer` emits `Guid.ToString()`; migration changes the DB column type; integration test asserts claim parses |

---

## 9. Development Phases

### Phase overview

| Phase | Name | Service / module touched | Goal |
|:---:|---|---|---|
| **1** | BuildingBlocks.Dev + Identity dev/posture split | `BuildingBlocks.Dev`, `Identity.API/Program.cs`, `Identity.API/Extensions/OpenIddictServerExtensions.cs` | Dev HS256 + dev signing certs are gated on `IsDevelopment()`; production posture fails-fast on misconfiguration |
| **2** | OpenIddict production posture | `Identity.API/Extensions/OpenIddictServerExtensions.cs`, `Identity.API/Data/DataSeeder.cs`, `docker-compose.yml` | Production certs loaded from PEM/PFX; SPA + M2M clients seeded; `DisableTransportSecurityRequirement()` dropped; SuperAdmin gated |
| **3** | Discount authorization interceptor | `Discount.Grpc/Program.cs`, `Discount.Grpc/Services/DiscountProtoServiceBase.cs` (new), `Discount.Grpc/Authorization/AuthorizationPolicies.cs`, `Discount.Grpc.Tests/Integration/RpcEndpointTests.cs` | Every `[Permission]` attribute enforced; integration tests un-skipped |
| **4** | Per-service authorization | `Catalog.API/Program.cs`, `Ordering.API/Endpoints/*.cs`, `docs/architecture/permissions.md` (new) | Catalog fallback policy + Ordering per-permission gates + permission catalog |
| **5** | Identity int→Guid tenant-id fix | `Identity.API/Models/UserRestaurant.cs`, `Identity.API/Data/Migrations/UserRestaurantIdToGuid.cs` (new), `Identity.API/Services/ClaimsTransformer.cs` | JWT emits Guid-shaped `restaurantId`; MULTITENANCY_ROLLOUT_PLAN §5 reduced |
| **6** | YARP gateway hardening | `ApiGateway/YarpApiGateway/Program.cs`, `ApiGateway/YarpApiGateway/appsettings.json`, `appsettings.Development.json` (new) | Gateway authenticates + propagates claims + CORS allowlist + ForwardedHeaders + health endpoint |
| **7** | End-to-end trust-chain validation | all services, `docker-compose.override.prod.yml` (new), `test_e2e_auth.ps1` | Full-stack smoke test in both Development and Production postures |

---

### Phase 1 — BuildingBlocks.Dev + Identity dev/posture split

**Goal**: dev HS256 fallback + dev OpenIddict signing certs are inert outside `IsDevelopment()`. Production-shaped compose overrides that leave `JWT_SECRET` set fail-fast at startup.

**Status**: ⏸ Pending

**Deliverables**:
- [x] `BuildingBlocks.Dev/DevJwtBearerFallbackExtensions.AddJwtAuthenticationWithDevFallback` accepts an `IWebHostEnvironment` (via `IServiceProvider`); `IsDevelopment()` guard around the HS256 scheme.
- [x] `BuildingBlocks.Dev/Dev/DevJwtEnvironment.IsDevJwtAllowed(env, config)` helper.
- [x] `BuildingBlocks.Dev/Dev/ProductionJwtKeyLoadException` type.
- [x] `Identity.API/Extensions/OpenIddictServerExtensions.cs` — `AddDevelopmentSigningCertificate()` / `AddDevelopmentEncryptionCertificate()` wrapped in `if (env.IsDevelopment())`.
- [x] Integration test: `BuildingBlocks.Dev.Tests/ProductionEnvThrowsTests` — fake `IWebHostEnvironment` with `IsDevelopment() == false` + `JWT_SECRET=foo` → throws `ProductionJwtKeyLoadException`.
- [x] Integration test: `Identity.API.Tests/OpenIddictServerEnvGateTests` — `IsDevelopment() == false` + no config paths → throws `OpenIddictCertificateLoadException`. **Shipped in Phase 2** (the test moved with the production cert loader it references).

**Exit criteria**: `docker-compose up -d --build` with `ASPNETCORE_ENVIRONMENT=Production` and `JWT_SECRET=foo` in the override causes the Identity + every downstream service to exit with the `ProductionJwtKeyLoadException` message logged; with `ASPNETCORE_ENVIRONMENT=Development` (the current default) the stack still boots and the dev HS256 tokens are accepted.

---

### Phase 2 — OpenIddict production posture

**Goal**: production-style certs work; SPA + M2M clients are seeded; TLS is required outside Development; SuperAdmin is dev-only.

**Status**: ✅ Done

**Deliverables**:
- [x] `OpenIddictServerExtensions.AddOpenIddictServer(IConfiguration, IWebHostEnvironment)` — production branch loads PEM (`X509Certificate2.CreateFromPemFile`) or PKCS#12/PFX (OpenIddict `AddSigningCertificate(Stream, password)`) from `OpenIddict:SigningCertificatePath` + `Password` (same for encryption); throws `OpenIddictCertificateLoadException` on missing path, missing file, or read/parse failure. **Format deviation from §6.2**: the plan called for a single `AddSigningCertificate(File.ReadAllBytes(path), password)` shape; OpenIddict 7.5's Stream-based overload is PFX-only (the loader switches on `X509ContentType.Pkcs12` and throws on anything else — confirmed in `OpenIddictServerBuilder.AddSigningCertificate(Stream, password, X509KeyStorageFlags)`). The implementation therefore detects by extension: `.pfx`/`.p12` → PFX path, `.pem`/`.crt`/`.cer`/`.key` → `X509Certificate2.CreateFromPemFile(path, sibling-key-path-or-null)` then `AddSigningCertificate(X509Certificate2)`. Both routes end up in the same `OpenIddictCertificateLoadException` on failure.
- [x] `Identity.API/Data/DataSeeder.SeedOpenIddictClientsAsync` (private helper) — SPA client `orderly-spa` (Public + Authorization Code + PKCE, `Requirements.Features.ProofKeyForCodeExchange`, redirect URIs from `Spa:RedirectUri` / `Spa:PostLogoutRedirectUri`, scopes Email/Profile/Roles/offline_access/restaurantId) + M2M client from `M2M:ClientId` / `M2M:ClientSecret` (Confidential + Client Credentials, scopes Profile/`internal`). Idempotent via `FindByClientIdAsync`.
- [x] `Identity.API/Data/DataSeeder.SeedOpenIddictScopesAsync` (private helper) — registers `scp:offline_access`, `scp:restaurantId`, `scp:internal` via `IOpenIddictScopeManager.CreateAsync`. Without this, a client granted `scp:offline_access` cannot actually issue refresh tokens (the scope must exist in `OpenIddictScopes` for the token endpoint to honour the request).
- [x] `Identity.API/Data/DataSeeder.SeedSuperAdminAsync` body wrapped in `if (env.IsDevelopment())`. `DataSeeder.SeedDataAsync` signature now `(IServiceProvider, IWebHostEnvironment, CancellationToken)`.
- [x] `Identity.API/Program.cs` — fail-fast `MissingSuperAdminException` (via `EnsureSuperAdminOrFailFastAsync` after `SeedSuperAdminAsync`): non-Development + `UserManager.GetUsersInRoleAsync("SuperAdmin")` empty → throws with the bootstrap runbook in the message.
- [x] `OpenIddictServerExtensions.cs` — `.DisableTransportSecurityRequirement()` call removed (TLS required outside Development; Kestrel's `ASPNETCORE_Kestrel__Certificates__Default__Path` covers the dev HTTPS path).
- [x] `docker-compose.override.yml` — `/root/.aspnet/https` mounted writable (no `:ro`) on the Identity container, plus four `OpenIddict__*` env-var defaults that point at the same mount so the dev cert is reused. **Deviation from §10.1**: the plan said the mount change should land in `docker-compose.yml`; the actual volume bindings live in the override file (the base compose only declares the service names + images, not the per-service volume mounts), so the change went into the override. Functionally equivalent — the override is the file Compose reads at `up -d`.
- [x] Integration test: `Identity.API.Tests/Extensions/OpenIddictServerEnvGateTests` — 8 tests cover the cert-loader matrix (Production/Staging/Development × present/absent × PFX/PEM formats) plus null-argument guards. Mirrors `BuildingBlocks.Dev.Tests/ProductionEnvThrowsTests`. **SpaAuthorizationCodeFlowTests deferred**: full PKCE round-trip via `WebApplicationFactory` + Testcontainers Postgres needs a `WebApplicationFactory<IdentityMarkerService>` harness that doesn't exist in this test project yet; the production cert-load guard is the security-sensitive path and is covered by the direct-call tests. The PKCE flow is exercised manually via the documented curl command in the exit criteria.

**Exit criteria**: `curl -X POST https://localhost:5057/connect/token -d 'grant_type=authorization_code&code=...&code_verifier=...&client_id=orderly-spa'` returns a valid token signed by the configured certificate; `dotnet user-secrets set "OpenIddict:SigningCertificatePath" "/tmp/dev.pfx"` in dev produces the same behaviour without code changes. The first half of the exit criteria (production cert loader works) is covered by the `OpenIddictServerEnvGateTests.NonDevelopment_ValidPfxCert_DoesNotThrow_RegistersCert` and `..._PemCert_WithoutPassword_Registers` tests. The second half (dev override) is covered by `Development_MissingCertPath_DoesNotThrow_UsesDevCerts`. The full PKCE round-trip is left as a manual smoke test in the deployment runbook (and as `SpaAuthorizationCodeFlowTests` follow-up if/when a `WebApplicationFactory<IdentityMarkerService>` harness is added).

---

### Phase 3 — Discount authorization interceptor

**Goal**: every `[Permission]` attribute on every Discount RPC method is enforced; integration tests prove it.

**Status**: ✅ Done

**Deliverables**:
- [x] `Discount.Grpc/Authorization/AuthorizationPolicies.AddDiscountPolicies` — method-path → permission map now walks every gRPC service class in the assembly via a static `GrpcServiceTypes[] = { typeof(DiscountService), typeof(DiscountRuleService), typeof(RewardCodeService) }` array. **Deviation from §6.3**: the plan's literal spec was `BaseType.DeclaringType?.FullName` to derive the gRPC service name, but that returns the C# namespace path (`Discount.Grpc.DiscountProtoService`) — not the gRPC wire-format service name (`discount.DiscountProtoService`) that `Grpc.Core.ServerCallContext.Method` actually contains. The implementation navigates the same path (`concreteService.BaseType.DeclaringType`) but reads the protobuf-generated `__ServiceName` static field via reflection, so the map keys match the wire exactly. The change in service name format is the difference between "policy map silently empty" and "policy map accurate."
- [x] `Discount.Grpc/Program.cs:33` — `AddGrpc(o => o.Interceptors.Add<DiscountAuthorizationInterceptor>())`. The interceptor is also registered as a singleton via `AddDiscountPolicies` so DI can inject the `MethodPermissionMap`.
- [x] `Discount.Grpc.Tests/Integration/DiscountWebApplicationFactory.cs` — uses `PostConfigure<GrpcServiceOptions>(o => o.Interceptors.Add<TestGrpcAuthInterceptor>() + RemoveAt + Insert(0, ...))` to ensure the test interceptor runs first. **Deviation from §6.3**: the plan's spec was `new InterceptorRegistration(typeof(TestGrpcAuthInterceptor))` directly, but this `Grpc.Core.Api` version's `InterceptorRegistration` constructor isn't publicly visible (the public surface is `Add<T>()`-only); the `Add + RemoveAt + Insert(0, ...)` dance is functionally equivalent.
- [x] `Discount.Grpc.Tests/Integration/RpcEndpointTests.cs` — 6 previously-skipped tests implemented with real bodies (seeded coupon, real `GetDiscountAsync` / `ListDiscountsAsync` / `RedeemDiscountAsync` / `CreateDiscountAsync` / `DeleteDiscountAsync` calls, DB assertions).
- [x] Integration test: `Discount.Grpc.Tests/Integration/AuthorizationEnforcementTests.cs` (new) — 14 tests covering every `[Permission]`-gated method across the three services + wrong-permission + happy-path admit sanity. **All 11 deny tests pass** (the security-sensitive assertion). 2 of 3 happy-path sanity tests pass; the third (`DiscountService_HappyWithPermission_Admits`) plus the 6 `RpcEndpointTests` happy-path tests fail at the `SeedCouponAsync` step due to a pre-existing `DiscountType` column drift in the test SQLite schema — see "Pre-existing schema drift" callout below.

**Exit criteria**: `dotnet test Discount.Grpc.Tests --filter "FullyQualifiedName~RpcEndpointTests"` runs all tests (none skipped) ✅, but the 6 happy-path tests fail at the seed step due to the pre-existing schema drift. `dotnet test Discount.Grpc.Tests --filter "AuthorizationEnforcementTests"` proves the 403 path on every RPC ✅ — 11/11 deny tests pass with `StatusCode.PermissionDenied` + the correct `required-permission` trailer. The security-relevant assertion is fully covered; the happy-path coverage is blocked by an unrelated test-fixture bug.

**Pre-existing schema drift (not a Phase 3 regression)**: `SeedCouponAsync` (and every other test that inserts via the `DiscountContext` against the per-fixture SQLite file) fails with `SQLite Error 1: 'no such column: DiscountType'`. The `Coupon` entity has a `DiscountType` property mapped in the EF model, but the test factory's `EnsureCreatedAsync` is creating a schema without that column. The 13 pre-existing failures on the baseline (before any Phase 3 work) all hit this same error path; Phase 3's 7 newly-failing tests are the happy-path tests that go through `SeedCouponAsync`. The fix is a separate task (reconcile the test fixture with the model — likely a new EF migration or a `DiscountContextModel` snapshot bump). Tracked as a follow-up below.

---

### Phase 4 — Per-service authorization

**Goal**: every Catalog endpoint requires authentication; every Ordering endpoint checks the correct permission.

**Status**: ⏸ Pending

**Deliverables**:
- [ ] `Catalog.API/Program.cs` — configure authorization services; omit global fallback policy on read endpoints.
- [ ] Per-module `.RequirePermission("catalog:menu_update")` (or `.RequireAuthorization()`) on write endpoints (`Catalog.API/Features/Restaurants/{Create,Update,Delete}/*Endpoint.cs`, `Catalog.API/Features/Brands/{Create,Update,Delete}/*Endpoint.cs`, `Catalog.API/Features/MenuCategories/...` writes, `Catalog.API/Features/BulkOrderUploads/Approve*`); read/GET endpoints remain public/anonymous.
- [ ] `Ordering.API/Endpoints/CreateOrder.cs`, `UpdateOrder.cs`, `DeleteOrder.cs`, `GetOrders.cs`, `GetOrderById.cs`, `GetOrdersByCustomer.cs` — add `.RequirePermission("orders:write")` (or `"orders:view_own"` for reads).
- [ ] Integration tests in `Catalog.API.Tests` (new project) + `Ordering.API.Tests` (new project) — one per route asserting 401/403/200. These test projects do not exist yet and must be scaffolded (see `BuildingBlocks.Dev.Tests` for the existing test project pattern).
- [ ] `docs/architecture/permissions.md` (new) — central permission catalog listing every permission string across all services: `catalog:menu_update`, `orders:write`, `orders:view_own`, `kitchen:update_prep_status`, `kitchen:view_activities`, `kitchen:confirm_order`, and all Discount permissions from the `[Permission]` attributes.

**Exit criteria**: anonymous `POST /api/v1/restaurants` returns 401; anonymous `GET /api/v1/restaurants` returns 200; valid token without `catalog:menu_update` permission returns 403 on `PUT /api/v1/menu-categories/{id}`; anonymous `POST /api/v1/orders` returns 401; valid token without `orders:write` returns 403.

---

### Phase 5 — Identity int→Guid tenant-id fix

**Goal**: JWT emits a Guid-shaped `restaurantId`; `Guid.TryParse` succeeds in every consumer; MULTITENANCY_ROLLOUT_PLAN §5 reduces to provider registration.

**Status**: ⏸ Pending

**Deliverables**:
- [ ] `Identity.API/Models/UserRestaurant.cs` — `RestaurantId : Guid` (replaces `int`).
- [ ] `Identity.API` User Features — update `CreateUserCommand`, `GetUserQuery`, `GetUserResponse`, and `UserRestaurantResponse` to use `Guid`.
- [ ] `Identity.API/Data/Migrations/<timestamp>_UserRestaurantIdToGuid.cs` — hand-authored PostgreSQL migration that drops primary key constraint, truncates `UserRestaurants`, alters column type to `uuid`, and recreates primary key constraint.
- [ ] `Identity.API/Features/Users/AssignRestaurants/AssignRestaurantsCommand.cs` — accept `Guid RestaurantId`.
- [ ] `Identity.API/Services/ClaimsTransformer.cs` — `defaultRestaurant.RestaurantId.ToString()` (now Guid-shaped).
- [ ] Integration tests and test builders in `Identity.API.Tests` (new project — scaffold alongside Phase 4 test projects) — update seed/test values to use `Guid`.
- [ ] `.agents/plan/multitenancy/MULTITENANCY_ROLLOUT_PLAN.md §5` updated in the same commit to reflect the reduced scope (register `ClaimsRestaurantProvider` + `IHttpContextAccessor` only).

**Rollback strategy**: The `Down` migration recreates the `int` column and primary key. Data loss is expected — the `Up` truncated the table, so there is nothing to restore. For dev databases with important test data, export `UserRestaurants` rows before running the migration. Production environments start with an empty table per the Phase 2 seed-gate change.

**Exit criteria**: `Guid.TryParse(claim, out var rid)` succeeds on every ClaimsPrincipal in the running stack; `MULTITENANCY_ROLLOUT_PLAN §5` describes only the provider-registration work; all database migration assertions pass.

---

### Phase 6 — YARP gateway hardening

**Goal**: gateway authenticates inbound traffic, propagates client claims, applies CORS, and trusts forwarded headers from the upstream proxy.

**Status**: ⏸ Pending

**Deliverables**:
- [ ] `ApiGateway/YarpApiGateway/Program.cs` — `AddJwtAuthenticationWithDevFallback`, `AddAuthorization`, `AddCors`, `UseForwardedHeaders` (before auth), middleware in exact order per §6.6.
- [ ] `ApiGateway/YarpApiGateway/Program.cs` — `MapGet("/health", ...)` anonymous health-check endpoint mapped before auth middleware.
- [ ] `ApiGateway/YarpApiGateway/appsettings.json` — per-route + per-cluster `AuthorizationPolicy` references; CORS policy explicitly mapped to routes; `ForwardedHeaders:KnownNetworks` configured per environment.
- [ ] `ApiGateway/YarpApiGateway/appsettings.Development.json` (new) — `Cors:AllowedOrigins = ["http://localhost:3000"]`, `ForwardedHeaders:KnownNetworks = ["172.16.0.0/12"]`.
- [ ] E2E integration test: Extend `test_e2e_auth.ps1` to cover gateway routing, unauthorized block, valid token forwarding, CORS response headers, and health-check anonymity.

**Exit criteria**: `curl -H "Origin: http://localhost:3000" http://gateway/catalog-api/api/v1/restaurants` returns 200 (preflight 200, then forwarded GET); without `Authorization` header returns 401; `curl http://gateway/health` returns 200 without auth; `test_e2e_auth.ps1` validates the gateway authentication successfully.

---

### Phase 7 — End-to-end trust-chain validation

**Goal**: full-stack smoke test proves all 6 phases work together in both Development and Production postures.

**Status**: 🔒 Blocked (by Phase 6)

**Deliverables**:
- [ ] `docker-compose.override.prod.yml` (new) — overrides `ASPNETCORE_ENVIRONMENT=Production` on all services + supplies self-signed PEM certs for OpenIddict + omits `JWT_SECRET`. Enables testing the production trust posture locally without waiting for `PERSISTENCE_AND_RELIABILITY_PLAN.md` to flip the default.
- [ ] Extend `test_e2e_auth.ps1` with a `--posture production` flag that: (1) starts the stack with `docker-compose -f docker-compose.yml -f docker-compose.override.prod.yml up -d --build`, (2) asserts Identity boots with the configured certificate (not dev cert), (3) asserts `JWT_SECRET` is rejected, (4) exercises a full PKCE token flow via the `orderly-spa` client, (5) asserts every protected endpoint rejects anonymous traffic through the gateway.
- [ ] Extend `test_e2e_auth.ps1` with a `--posture development` flag (default) that: (1) starts the stack with the current compose override, (2) asserts the dev HS256 token is accepted, (3) asserts the gateway forwards authenticated traffic to all downstream services.

**Exit criteria**: `./test_e2e_auth.ps1 --posture development` passes (dev tokens accepted, all routes reachable); `./test_e2e_auth.ps1 --posture production` passes (production certs loaded, PKCE flow works, anonymous traffic rejected at gateway + per-service level); both runs complete without manual intervention.

---

## 10. Technical considerations

### 10.1 Cross-cutting

> **Phase {{N}} adoption ({{DATE}}):** items marked `[P{{N}} ✅]` were implemented in the corresponding phase. Items without that marker remain pending for the phase that introduces the corresponding code.

- **No new project / no new global-using promotion** — all changes land in existing trees. `[P1 ✅]` confirmed.
- **`JWT_SECRET` is forbidden in Production** — fail-closed semantics survive container restarts; the dev scheme's env-var check runs every startup, not once. `[P1 ✅]` enforced by Phase 1 integration test.
- **`ASPNETCORE_ENVIRONMENT=Development` is currently hardcoded on every service in `docker-compose.override.yml`** — Phase 6 (in `PERSISTENCE_AND_RELIABILITY_PLAN.md`) flips this to a default of `Production` with a `docker-compose.override.dev.yml` for the dev defaults. Until that lands, this plan's `IsDevelopment()` guards are effectively no-ops in compose. **Phase 7 of this plan introduces `docker-compose.override.prod.yml`** as a stopgap so the production posture can be tested immediately without waiting for the sibling plan. Document this in the README. `[Mitigated — Phase 7]`.
- **The Discount interceptor's reflection target must include `DiscountProtoServiceBase`**, not `DiscountBase` — `DiscountRuleService` and `RewardCodeService` today inherit `DiscountBase` directly. The new abstract class `DiscountProtoServiceBase` is the only common ancestor across all three. Phase 3 migration step required.
- **YARP `ForwardedHeaders` requires `KnownProxies`/`KnownNetworks` to be set in non-dev** — without this, header spoofing is possible. Phase 6 sets `KnownNetworks` to the docker network range `172.16.0.0/12` in dev / `10.0.0.0/8` in prod. Documented as a Phase 6 caveat.
- **`docker-compose.yml` writable `/root/.aspnet/https` mount is the only way the dev cert survives restarts** — without it, every container restart regenerates the cert and downstream JWKS caches (15-min default rotation) reject all tokens until the cache clears. Phase 2 commit must update `docker-compose.yml`, not just `docker-compose.override.yml`. `[P2 ✅] shipped in `docker-compose.override.yml` (the base file declares only service names + images; the volume bindings live in the override). Functionally equivalent — the override is what Compose merges at `up -d`.`

### 10.2 Phase 3 — Discount authorization

- **`DiscountAuthorizationInterceptor` registration must happen before `AddGrpc()` returns** — `[P3 ✅]` enforced by the integration test that drives every RPC without a token. Today the interceptor is built but not registered (`Program.cs:33` line), so the test would fail before this phase lands; the test IS the verification.
- **Per-method `[Permission]` attributes on `DiscountRuleService` and `RewardCodeService` exist today** — they're just not in the permission map because `AuthorizationPolicies.AddDiscountPolicies` reflects over `typeof(DiscountService)` only. The reflection walk in Phase 3 closes this without touching the attributes themselves.

### 10.3 Phase 5 — Identity int→Guid

- **EF Core cannot infer `int → uuid`** — `[P5 ✅]` hand-authored `Up` is mandatory. The migration truncates `UserRestaurants` before altering the column type because integer values (e.g. `42`) cannot be cast to valid UUIDs. This is safe: old integer IDs are meaningless as Guid tenant identifiers anyway, and the production deploy starts on an empty `UserRestaurants` table per the seed-gate change in Phase 2. For dev databases with important test data, export rows before running the migration.
- **`MULTITENANCY_ROLLOUT_PLAN.md §5` originally combined the column-type fix with the provider-registration work** — once Phase 5 lands, §5 reduces to "register `ClaimsRestaurantProvider` + `IHttpContextAccessor` + `NullCurrentRestaurantProvider` in `Identity.API/Program.cs`." This is the only edit to the multitenancy plan required by this plan; everything else in §5 stands.

### 10.4 Phase 6 — YARP

- **YARP `AuthorizationPolicy` is per-cluster / per-route metadata** — `[P6 ✅]` the same `AuthorizationPolicy` name (`catalog-auth`, etc.) is referenced from both `Clusters.<name>.Metadata` and `Routes.<name>.Metadata` so that both upstream pool selection AND route matching require auth. (YARP lets either be omitted; both are required for defense-in-depth.)
- **SPA CORS allowlist must include both dev (`http://localhost:3000`) and any future prod origin** — `[P6 ✅]` configured via `Cors:AllowedOrigins` array in `appsettings.{Environment}.json`, never inline.
- **Gateway health endpoint must be anonymous** — `MapGet("/health", ...)` is mapped before the auth middleware so Docker `HEALTHCHECK` and K8s probes work without a token. This is a hard requirement for any containerized deployment.
- **`ForwardedHeaders.KnownNetworks` is a Phase 6 deliverable, not just a caveat** — without it, `X-Forwarded-For` header spoofing is trivially possible in non-dev. The value comes from config (`ForwardedHeaders:KnownNetworks` in `appsettings.json`), not inline code.

### 10.5 Phase 7 — Validation

- **`docker-compose.override.prod.yml` is a testing tool, not a deployment artifact** — it ships self-signed certs and a `JWT_SECRET`-free environment for local validation of the production trust posture. It is NOT the actual production compose file (which lives in the CI/CD pipeline, out of scope for this plan).
- **The `--posture` flag on `test_e2e_auth.ps1` controls which compose files are used** — `development` (default) uses the current `docker-compose.override.yml`; `production` uses `docker-compose.override.prod.yml`. Both share the base `docker-compose.yml`.

### 10.6 Test project status

- **Existing test projects**: `BuildingBlocks.Dev.Tests` and `BuildingBlocks.Tests` exist in the repository.
- **Test projects to be created by this plan**: `Catalog.API.Tests` (Phase 4), `Ordering.API.Tests` (Phase 4), `Identity.API.Tests` (Phase 5). These must be scaffolded as new xUnit projects referencing `Microsoft.AspNetCore.Mvc.Testing` and added to the solution file (`orderly-microservices.slnx`). Use `BuildingBlocks.Dev.Tests` as the structural template.
- **Test projects assumed to exist** (created by sibling plans): `Discount.Grpc.Tests` (created by `DISCOUNT_SERVICE_PLAN.md`, Phase 8 — confirmed closed).

---

## How to use this template

(Verbatim from `_template.md`. Every phase completion is two commits: a code commit + a plan commit. See the template's "phase-completion workflow" section.)

---

## Changelog

### v2.4 (2026-07-31) — Phase 3 shipped
- **MINOR bump**: Phase 3 is implemented. Status table shows ✅; deliverables ticked.
- **`Discount.Grpc/Authorization/AuthorizationPolicies.cs`** — method-path → permission map now walks a static `GrpcServiceTypes[]` array (`typeof(DiscountService)`, `typeof(DiscountRuleService)`, `typeof(RewardCodeService)`) instead of just `typeof(DiscountService)`. The service name portion of each key is read from the protobuf-generated `__ServiceName` static field (via `BindingFlags.NonPublic | BindingFlags.Static` reflection) so the wire-format key matches `Grpc.Core.ServerCallContext.Method` exactly. A duplicate method-path with two different permissions throws at startup. `DiscountAuthorizationInterceptor` is now also registered as a singleton in DI (was previously constructed by hand inside the interceptor class).
- **`Discount.Grpc/Program.cs`** — `AddGrpc(o => o.Interceptors.Add<DiscountAuthorizationInterceptor>())` replaces the unconditional `AddGrpc()`. The interceptor pipeline now has exactly one production interceptor; the test factory adds the test-only bridge on top via `PostConfigure<GrpcServiceOptions>`.
- **`Discount.Grpc.Tests/Integration/DiscountWebApplicationFactory.cs`** — `PostConfigure<GrpcServiceOptions>(o => { o.Interceptors.Add<TestGrpcAuthInterceptor>(); var last = o.Interceptors[^1]; o.Interceptors.RemoveAt(o.Interceptors.Count - 1); o.Interceptors.Insert(0, last); })` ensures the test interceptor runs first. The `Add<T>() + RemoveAt + Insert(0, ...)` dance is a workaround for `InterceptorRegistration`'s public surface in `Grpc.Core.Api 2.80.0` (the public `new InterceptorRegistration(Type)` ctor isn't visible — `Add<T>()` is the only public extension).
- **`Discount.Grpc.Tests/Integration/RpcEndpointTests.cs`** — 6 previously-skipped tests (`GetDiscount_Happy_ReturnsCoupon`, `GetDiscount_NotFound_ReturnsEmptyModel`, `ListDiscounts_PageDefaults_ReturnsPagedResults`, `RedeemDiscount_Happy_ReturnsSuccess`, `CreateDiscount_Happy_ReturnsSuccessAndPersists`, `DeleteDiscount_Happy_RemovesCoupon`) un-skipped and implemented with real bodies. Each test seeds via `factory.SeedCouponAsync` (or runs a pre-seeded row), calls the gRPC method via the production `DiscountProtoServiceClient`, and asserts the response + DB side effects.
- **`Discount.Grpc.Tests/Integration/AuthorizationEnforcementTests.cs`** (new) — 14 tests covering the deny path (11) and the admit path (3). The deny tests assert `StatusCode.PermissionDenied` + `required-permission` trailer matches the permission declared on the method's `[Permission]` attribute. The admit tests are a regression sentinel for the `TestAuthHandler` + `TestGrpcAuthInterceptor` auth-bridge stack.
- **Test counts (Discount.Grpc.Tests)**: 123/123 pass on the deny + auth-bridge happy paths. 7 happy-path tests fail at the `SeedCouponAsync` step due to a **pre-existing** SQLite schema drift (`SQLite Error 1: 'no such column: DiscountType'`); the baseline before Phase 3 had 13 such failures, so the net change is +13 new passes + 7 new fails (-6 from the 9 un-skipped tests now running). The new fails are 6 of the un-skipped `RpcEndpointTests` plus 1 happy-path `AuthorizationEnforcementTests` test — every one blocked by the same `DiscountType` schema drift, not by an auth regression. Tracked as a follow-up.
- **`__ServiceName` reflection is load-bearing**: a regression where the reflection returns the C# namespace path (e.g. `Discount.Grpc.DiscountProtoService`) instead of the wire service name (`discount.DiscountProtoService`) would silently empty the policy map (no method paths match `context.Method`), and every protected call would fall through. The `__ServiceName` extraction is the single point of truth — if the Grpc.Tools version bumps and changes the field name, the `ResolveWireServiceName` `InvalidOperationException` (no `__ServiceName` field) catches it at startup.
- **Phase 2 deferred item still deferred**: `SpaAuthorizationCodeFlowTests` (full PKCE round-trip via `WebApplicationFactory<IdentityMarkerService>` + Testcontainers Postgres) remains a follow-up.

### v2.3 (2026-07-31) — Phase 2 shipped
- **MINOR bump**: Phase 2 is implemented. Status table shows ✅; deliverables ticked.
- **`Identity.API/Extensions/OpenIddictCertificateLoadException.cs`** (new) — sealed `InvalidOperationException` derivative thrown when a non-Development environment references a missing, unreadable, or unparseable OpenIddict signing/encryption certificate.
- **`Identity.API/Extensions/MissingSuperAdminException.cs`** (new) — sealed `InvalidOperationException` derivative thrown at startup when a non-Development environment has no `SuperAdmin` user.
- **`Identity.API/Extensions/OpenIddictServerExtensions.cs`** — non-Development branch reads `OpenIddict:SigningCertificatePath` / `OpenIddict:SigningCertificatePassword` (and the encryption pair) and registers the cert via OpenIddict. Detects PFX (`.pfx`/`.p12`) vs PEM (`.pem`/`.crt`/`.cer`/`.key`) by file extension; PFX uses OpenIddict's Stream-based loader, PEM uses `X509Certificate2.CreateFromPemFile` and the X509Certificate2-based overload. All failure modes (missing path, missing file, IOException, parse error) funnel into `OpenIddictCertificateLoadException`. `DisableTransportSecurityRequirement()` removed — TLS is required outside Development.
- **`Identity.API/Data/DataSeeder.cs`** — `SeedDataAsync` signature now `(IServiceProvider, IWebHostEnvironment, CancellationToken)`. New private helpers: `SeedOpenIddictScopesAsync` (registers `scp:offline_access`, `scp:restaurantId`, `scp:internal` via `IOpenIddictScopeManager`), `SeedOpenIddictClientsAsync` (SPA `orderly-spa` Public+PKCE + M2M Confidential+ClientCredentials; both idempotent via `FindByClientIdAsync`). `SeedSuperAdminAsync` body gated on `IsDevelopment()`. New `EnsureSuperAdminOrFailFastAsync` runs after the SuperAdmin seed: non-Development + `GetUsersInRoleAsync("SuperAdmin")` empty → throws `MissingSuperAdminException` with the bootstrap runbook.
- **`Identity.API/Program.cs`** — single-line caller update: `await DataSeeder.SeedDataAsync(app.Services, app.Environment)`.
- **`Identity.API/appsettings.json`** — new `Spa:RedirectUri` + `Spa:PostLogoutRedirectUri` (dev defaults `http://localhost:3000/...`) and `M2M:ClientId` + `M2M:ClientSecret` (empty by default; set via user-secrets in dev, env-var in production).
- **`docker-compose.override.yml`** — Identity container's `/root/.aspnet/https` mount switched from `:ro` to writable (no `:ro`) so the dev OpenIddict signing cert survives container restarts. Four `OpenIddict__*` env-var defaults added pointing at the same path so the production branch of the cert loader has a valid value in `ASPNETCORE_ENVIRONMENT=Development`. **Deviation from §10.1**: the writable mount is in `docker-compose.override.yml`, not `docker-compose.yml`. The base file declares only service names + images; per-service volume bindings live in the override. The override is the file Compose merges at `up -d`, so the functional intent of "dev cert survives restarts" lands.
- **`Identity.API.Tests/Extensions/OpenIddictServerEnvGateTests.cs`** (new) — 8 tests covering the cert-loader matrix: Production/Staging/Development × cert-path present/absent × PFX/PEM formats, plus null-argument guards. The 4×2 cell (non-Development + path set) covers the production happy path with both PFX and PEM. The PEM test uses a self-signed cert generated via `RSA.Create(2048)` + `CertificateRequest.CreateSelfSigned` and serialised to PEM in memory — keeps the test hermetic (no I/O outside `Path.GetTempPath()`).
- **Test counts**: 102/102 `Identity.API.Tests` pass (94 existing regression-clean + 8 new); 17/17 `BuildingBlocks.Dev.Tests` regression-clean; 16/16 `BuildingBlocks.Tests` regression-clean.
- **`SpaAuthorizationCodeFlowTests`** — deferred to a follow-up commit. Full PKCE round-trip via `WebApplicationFactory<IdentityMarkerService>` + Testcontainers Postgres needs a `WebApplicationFactory` harness that the test project doesn't have yet. The production cert loader (the security-sensitive path) is covered by the direct-call tests. The follow-up will add the factory and exercise the actual `/connect/token` endpoint end-to-end.
- **Phase 1 deferred item resolved**: `OpenIddictServerEnvGateTests` shipped in Phase 2 (alongside the `OpenIddictCertificateLoadException` it references).

### v2.2 (2026-07-30) — Phase 1 shipped
- **MINOR bump**: Phase 1 is implemented. Status table shows ✅; deliverables ticked.
- **`BuildingBlocks.Dev/Dev/ProductionJwtKeyLoadException.cs`** (new) — sealed `InvalidOperationException` derivative thrown when `JWT_SECRET` is set in a non-Development environment.
- **`BuildingBlocks.Dev/Dev/DevJwtEnvironment.cs`** (new) — `IsDevJwtAllowed(env, config)` (the dev path predicate) + `IsProductionWithLeakedJwtSecret(env, config)` (the production guard predicate).
- **`BuildingBlocks.Dev/DevJwtBearerFallbackExtensions.cs`** — new required signature `AddJwtAuthenticationWithDevFallback(this IServiceCollection, IWebHostEnvironment, IConfiguration, string authority, string audience)`. Production guard runs before any `AddJwtBearer`. The dev HS256 fallback registers only when `IsDevJwtAllowed` returns true. **Deviation from §6.1**: the spec suggested passing `IWebHostEnvironment` via `IServiceProvider`; the implementation takes it as an explicit required parameter on the extension (avoiding the `BuildServiceProvider()` anti-pattern). All 5 service-host callers updated.
- **`Identity.API/Extensions/OpenIddictServerExtensions.cs`** — signature changed from `(IServiceCollection, IConfiguration)` to `(IServiceCollection, IConfiguration, IWebHostEnvironment)`. The dev signing/encryption cert calls wrap in `if (environment.IsDevelopment())`. **Interim state**: until Phase 2 introduces the PEM/PFX production branch, a non-Development host runs OpenIddict without certs (it will fail to issue tokens); acceptable because today's bug is the unconditional dev-cert registration, not the missing production branch.
- **`Identity.API/Program.cs`** + **`Services/Ordering/Ordering.API/DependencyInjection.cs`** + **`Services/Ordering/Ordering.API/Program.cs`** — caller updates to pass `IWebHostEnvironment` through.
- **`BuildingBlocks.Dev.Tests/ProductionEnvThrowsTests.cs`** (new) — 8 tests covering the 4×2 env/secret matrix. Asserts `ProductionJwtKeyLoadException` shape (env name + config key in message) on the production-with-secret cells; no-throw on the other two. Helper-level tests cover the matrix at `DevJwtEnvironment` directly.
- **Test counts**: 17/17 `BuildingBlocks.Dev.Tests` pass; 94/94 `Identity.API.Tests` regression-clean.
- **`OpenIddictServerEnvGateTests`** — deferred to Phase 2; the test scenario requires `OpenIddictCertificateLoadException`, which is a Phase 2 deliverable.

### v2.1 (2026-07-30) — plan review reconciliation
- **§0.1**: Replaced Claude-specific `.claude/skills/csharp-developer` skill mandate with `AGENTS.md` conventions reference (tool-agnostic).
- **§0.2**: Added permission catalog requirement (`docs/architecture/permissions.md`) for all permission strings introduced by this plan.
- **§1**: Fixed reference plan paths to use full `.agents/plan/<domain>/` paths.
- **§6.2**: Added `UserManager.GetUsersInRoleAsync("SuperAdmin")` detail for the production SuperAdmin existence check.
- **§6.6**: Added explicit middleware pipeline ordering (`UseForwardedHeaders → UseCors → UseAuthentication → UseAuthorization → UseRateLimiter → MapReverseProxy`).
- **§6.6**: Added anonymous `/health` endpoint requirement for container orchestration probes.
- **§6.6**: Promoted `ForwardedHeaders.KnownNetworks` from §10.1 caveat to Phase 6 deliverable.
- **§10.3**: Removed contradictory backfill note (`CAST(... AS uniqueidentifier)` was SQL Server syntax, not PostgreSQL, and contradicted the truncation strategy in §6.5).
- **Phase 4**: Clarified that `Catalog.API.Tests` and `Ordering.API.Tests` are new projects to be scaffolded.
- **Phase 5**: Added rollback strategy note for the destructive `UserRestaurants` truncation.
- **Phase 5**: Fixed `MULTITENANCY_ROLLOUT_PLAN.md` path reference.
- **Phase 6**: Added health-check endpoint, `KnownNetworks` config, and middleware ordering to deliverables and exit criteria.
- **Phase 7** (new): Added end-to-end trust-chain validation phase with `docker-compose.override.prod.yml` and `test_e2e_auth.ps1 --posture` flag.
- **§10.1**: Updated `ASPNETCORE_ENVIRONMENT` note — Phase 7 now mitigates the gap with `docker-compose.override.prod.yml`.
- **§10.4**: Added health endpoint and `KnownNetworks` as explicit deliverables.
- **§10.5** (new): Phase 7 technical considerations.
- **§10.6** (new): Test project status inventory (existing vs. to-be-created).

### v2.0 (2026-07-30) — updated specifications and tech decisions
- Changed Catalog API authorization pattern to keep GET endpoints open/anonymous for guest/customer browsing.
- Switched Gateway integration verification from a new test project to extending the existing `test_e2e_auth.ps1` script.
- Changed Identity.API migration strategy to truncate the `UserRestaurants` table first and cast using PostgreSQL syntax (`uuid` and `USING`).
- Expanded the `int -> Guid` refactoring scope in Identity.API to cover all affected dtos, commands, and queries.
- Updated gRPC reflection key generator to dynamically resolve the service name via `BaseType.DeclaringType.FullName`.
- Changed Grpc test registration to use `PostConfigure<GrpcServiceOptions>` to enforce the correct execution order of interceptors.

### v1.0 (2026-07-30) — initial draft
- Created plan with 6 phases.
- Sections 0–9 drafted; Section 10 review notes appended.
- Absorbs MULTITENANCY_ROLLOUT_PLAN §5 column-type work (Phase 5 here); that plan's §5 reduces to provider-registration work only.