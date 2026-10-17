# Basket Service — Agent Instructions

Supplement to root `OrderlyMicroservices/AGENTS.md`. Rules here are Basket-specific.

## Architecture: Vertical Slice

```
Basket.API/
├── Audit/        ← BasketAuditLogEntry Marten document + IBasketAuditLog
├── Auth/         ← ICurrentUser (Basket-local)
├── Basket/       ← IBasketRepository, BasketRepository (Marten)
├── Behaviors/    ← BasketIdentityGuardBehavior (registered BEFORE ValidationBehavior)
├── Caching/      ← CachedBasketRepository (Scrutor decorator), IBasketCacheLockRegistry
├── Data/         ← Marten store configuration
├── Discount/     ← IDiscountLookup, GrpcDiscountLookup (wraps gRPC client)
├── Dtos/
├── Endpoints/    ← BasketEndpointGroup + Carter modules
├── Exceptions/
├── Idempotency/  ← BasketIdempotencyFilter (Carter IEndpointFilter)
├── Messaging/    ← CheckoutBasketOutboxDispatcher + CheckoutBasketOutboxMessage
├── Models/       ← Basket, BasketItem, BasketItemVariation, CouponSnapshot
├── Observability/
├── Payment/
├── ProblemDetails/
├── RateLimiting/ ← CheckoutRateLimiter
└── Services/     ← BasketExpirySweepService
```

## Storage: Marten + Redis (NOT Redis-only)

- **Marten (Postgres `basketdb`)** is the durable store. Per-tenant database creation via `CreateDatabasesForTenants`. Every cart document carries `RestaurantId` and is registered with `.MultiTenanted()`.
- **Redis** is the cache layer only, applied via `services.Decorate<IBasketRepository, CachedBasketRepository>()`. Cache key: `basket:{userId}:{restaurantId}`, TTL 30 min absolute (empty carts included).
- DO NOT query Redis as the source of truth. The Marten repository is the source; Redis is a read-through cache.

## Multi-Tenancy: The Most Important Rule

**Every** repository call MUST be filtered by `ICurrentRestaurantProvider` (reads `restaurantId` JWT claim). Mismatched tenants throw `ForbiddenException` → 403. There is NO exception to this rule except:

- `BasketExpirySweepService` — explicitly cross-tenant by design. It uses `IDocumentStore.LightweightSession()` directly (bypasses `IBasketRepository`). Do NOT replicate this pattern elsewhere.

Enforce at two levels:
1. **Pipeline**: `BasketIdentityGuardBehavior<TRequest, TResponse>` (registered before `ValidationBehavior`) cross-checks `(UserId, RestaurantId)` from the command against the JWT.
2. **Repository**: `IBasketRepository` calls `AssertTenant(...)` before every Marten query.

Every new command that touches a cart MUST implement `IBasketIdentityRequest { Guid UserId; Guid RestaurantId; }`.

## Outbox: Marten-shaped (not EF Core)

- The checkout outbox is NOT the EF Core `OutboxDispatcher<TContext>` from `BuildingBlocks.Messaging`. It uses Marten documents (`CheckoutBasketOutboxMessage`).
- Publishing `BasketCheckoutEvent`: stage a `CheckoutBasketOutboxMessage` Marten document AND delete the cart in the same `IDocumentSession.SaveChangesAsync()` — these are atomic.
- `CheckoutBasketOutboxDispatcher` (hosted service) polls via Marten LINQ + optimistic concurrency (`mt_version`). Multi-replica safe.
- DO NOT call `IPublishEndpoint.Publish` directly in any handler.

## gRPC Discount Integration

- Inject `IDiscountLookup` — never inject the raw `DiscountProtoServiceClient`.
- `GrpcDiscountLookup` wraps the gRPC client and normalises: `string ExpirationDate` → `Instant`, `double Amount` → `decimal`.
- The lookup call is wrapped by a Polly v8 resilience pipeline: 3-attempt retry + 5-failure circuit breaker + 3s/attempt timeout. Do NOT add retry logic inside `StoreBasketHandler`.
- gRPC failures are **fail-closed**: the whole upsert throws. This is intentional.

## Endpoints

All endpoints are Carter modules registered through `BasketEndpointGroup.MapBasketGroup()`. Call `app.MapBasketGroup()` in every module — do NOT duplicate the `MapGroup("/v1") + RequireAuthorization("Default") + WithTags("Baskets")` chain.

| Route | Notes |
|---|---|
| `GET /v1/cart` | Token-bound. Returns empty cart (never 404). ETag + Last-Modified + 304 support. |
| `PUT /v1/cart` | Token-bound. Body `UserId`/`RestaurantId` MUST be `Guid.Empty` (spoofing guard rejects anything else with 422). Returns 201 (new) or 200 (update). |
| `DELETE /v1/cart` | Token-bound. Idempotent, always 204. |
| `POST /v1/cart/checkout` | Requires `Idempotency-Key` header (UUID v4 strict). Rate-limited: 5 req/min per `(userId, restaurantId)`. |
| `GET/PUT/DELETE /v1/admin/carts` | Requires `orders:admin` permission. Cross-account admin surface. |

## Idempotency (Checkout only)

- `POST /v1/cart/checkout` enforces IETF idempotency-key via `BasketIdempotencyFilter`.
- Missing or malformed key → 400. Replay with body-matching key → 200 + `Idempotent-Replayed: true`. Replay with body-mismatching key → 422 (NOT 409).
- Redis key: `basket:idem:{userId}:{restaurantId}:{idempotencyKey}`, TTL 24h, body fingerprinted via `HMAC-SHA256(Basket:Idempotency:SecretHex, ...)`.
- Fail-closed on Redis errors: GET failure → 503 (do NOT run the handler).

## Rate Limiting (Checkout only)

- `CheckoutRateLimiter` in `Basket.API/RateLimiting/` — `FixedWindowRateLimiter` keyed on `(userId, restaurantId)`.
- 5 requests / 1 minute per partition, `QueueLimit = 0`.
- 429 response includes `Retry-After` + `application/problem+json` body.
- The other three cart endpoints (GET/PUT/DELETE) are NOT rate-limited.

## Hard Constraints

- Do NOT use `DateTime` — `NodaTime` (`Instant`) only.
- Do NOT use `IDistributedCache` directly in feature handlers — always `ICatalogCache` for Catalog, or the Basket-level cache via the Scrutor-decorated repository.
- Do NOT add `[ApiController]`.
- Do NOT bypass `IBasketRepository` for cart reads/writes (except `BasketExpirySweepService`).
- `BasketIdentityGuardBehavior` MUST remain registered before `ValidationBehavior` in MediatR pipeline.
- `[PciSensitive]` attribute on `CheckoutBasketCommand` must stay — `LoggingBehavior` redacts it.

## Test Project

`Basket.API.Tests` — xUnit + FluentAssertions + NSubstitute. Run before committing:

```powershell
dotnet test Services/Basket/Basket.API.Tests
```
