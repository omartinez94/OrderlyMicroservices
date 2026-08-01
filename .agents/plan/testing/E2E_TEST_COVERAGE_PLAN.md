# E2E & Integration Test Coverage — Implementation Plan

> Scope: close every significant test-coverage gap across all microservices — Catalog, Identity, Ordering, Kitchen, Basket, and Discount — surfaced by the 2026-08-01 test audit. Adds ~200+ new test methods across 7 phases, targeting untested CRUD endpoints, missing happy-path integration flows, handler unit tests, and cross-service lifecycle validation. All existing ~490 tests remain untouched.

---

## Status

> **Plan version**: `v1.1` (2026-08-01) — `MINOR` increments per phase completion; `MAJOR` is reserved for breaking restructures of the plan itself.
> **Current state**: ⏸ Not started

| Phase | Name | Status |
|:-----:|---|:-----:|
| 1 | Catalog CRUD happy-path integration tests | ⏸ Pending |
| 2 | Identity integration tests & auth flow coverage | 🔒 Blocked (shared infra) |
| 3 | Ordering CRUD + query handler tests | 🔒 Blocked |
| 4 | Kitchen lifecycle & missing handler tests | 🔒 Blocked |
| 5 | Basket admin endpoints + Discount RPC gaps | 🔒 Blocked |
| 6 | Ordering Infrastructure & Domain event tests | 🔒 Blocked |
| 7 | Cross-service E2E lifecycle validation | 🔒 Blocked |

> **Legend**: ✅ Done · 🚧 In progress · ⏸ Pending · 🔒 Blocked

> **Commit messages**: Conventional Commits (`test:`, `docs:`, `chore:`, `fix:`). Short subject, ≤50 chars, imperative mood, no trailing period.

> **Update rule**: **on every phase completion, the plan MUST be updated in the same commit as the phase work.** The plan is the source of truth for what was decided and what shipped; a phase that ships without a plan update is a phase that drifted. See [How to use this template](#how-to-use-this-template) for the workflow.

---

## 0. Skill & documentation conventions

### 0.1 Skill mandate — `csharp-xunit` + `csharp-developer`
> **All implementation work on this plan MUST follow the `csharp-xunit` skill (`.claude/skills/csharp-xunit/SKILL.md`) for test structure, assertions, and data-driven patterns, AND the `csharp-developer` skill (`.claude/skills/csharp-developer/SKILL.md`) for C# coding standards, async patterns, and architectural conventions.** Additional reference material lives in `.claude/skills/csharp-developer/references/` (`modern-csharp.md`, `aspnet-core.md`, `entity-framework.md`).

### 0.2 Code-quality guard rails
- **xUnit + FluentAssertions**: all test projects use `xunit 2.9.3`, `FluentAssertions 6.12.2`, `NSubstitute 5.3.0`, `Microsoft.NET.Test.Sdk 17.12.0`. Match these exact versions in any new test file.
- **Naming convention**: `MethodName_Scenario_ExpectedBehavior` (e.g., `CreateBrand_ValidRequest_Returns201AndPersists`).
- **AAA pattern**: every test follows Arrange-Act-Assert with clear separation.
- **Integration tests use Testcontainers**: PostgreSQL (`Testcontainers.PostgreSql`), MSSQL (`Testcontainers.MsSql`), Redis (`Testcontainers.Redis`), RabbitMQ (`Testcontainers.RabbitMq`) — match the existing `WebApplicationFactory` pattern per service.
- **Unit tests use NSubstitute**: mock interfaces, never concrete classes. Use `Arg.Any<T>()` for loose matching, `Arg.Is<T>()` for strict.
- **No new NuGet packages** unless explicitly justified in the phase deliverables.
- **CancellationToken**: every async test must forward `CancellationToken` via `TestContext` or `CancellationToken.None`.
- **Test isolation**: every test is independent and idempotent. Use `IAsyncLifetime` for setup/teardown. No shared mutable state across tests.
- **Serial execution**: integration tests with Testcontainers use `xunit.runner.json` with `"parallelizeTestCollections": false` where needed — follow existing patterns per project.

---

## 0.3 Improved Practices

- **Shared Test Infrastructure**: Introduce a common `OrderlyTestFactory<TProgram>` that configures Testcontainers, authentication helpers, and logging. All integration tests should inherit from this base class to ensure consistent environment and simplify container lifecycle management.
- **Centralised Data Seeding**: Provide a static `TestDataSeeder` used by `IAsyncLifetime.InitializeAsync` to seed required entities per test class. Guarantees deterministic state and eliminates hidden cross‑test dependencies.
- **Auth Token Helper**: Expose `GetTestJwtAsync(string[] permissions = null)` that uses the shared factory to generate JWTs for all services, avoiding hard‑coded secrets.
- **Data‑Driven Tests**: Collapse repetitive CRUD happy‑path/validation checks into `[Theory]` + `[MemberData]` patterns to keep test files concise while retaining full coverage.
- **Edge‑Case Coverage**: Add tests for malformed payloads (422), account lockout, concurrency conflicts (409), idempotent operations, and gRPC deadline handling.
- **Flaky‑Test Mitigation**: Use Polly retry with exponential back‑off for any polling of async state (e.g., waiting for RabbitMQ events). Employ `FakeClock` for deterministic timestamps. Ensure Testcontainers are torn down via `IAsyncLifetime.DisposeAsync`.
- **CI Integration**: Enforce ≥ 80 % line/branch coverage per project, gate E2E tests behind a `RUN_E2E` variable, and publish test results and coverage artifacts. Use `dotnet test --filter "Category=E2E"` for explicit runs.
- **Documentation**: Add a README to each test project with Docker start‑up instructions, environment variable list, and example test‑run commands. Link these READMEs from the plan.
- **Security Guardrails**: Mask `Authorization` headers in test logs, ensure tokens are generated per‑run and never persisted, and configure Testcontainers to clean up automatically.
- **Phase Dependency Clarification**: Phase 2 depends only on the shared test infrastructure (Phase 1), not on functional completion of Phase 1. Update the block status accordingly.

## 1. Context

The 2026-08-01 audit of all 11 test projects revealed ~490+ existing tests with strong coverage in some areas (Basket, Discount, Ordering.Domain) but critical gaps in others:

1. **Catalog.API** has ~60+ CRUD endpoints with **zero happy-path integration tests** — only auth enforcement and infrastructure tests exist.
2. **Identity.API** has comprehensive handler unit tests but **zero WebApplicationFactory/HTTP pipeline integration tests** — all tests use EF InMemory. Login/logout/token auth flows are completely untested.
3. **Ordering.API/Application** has auth + state transition tests but **no happy-path tests for CreateOrder, UpdateOrder, DeleteOrder**, and 7 Application handlers have zero unit tests.
4. **Kitchen.API** is missing handler tests for `RecallOrder`, `MarkItemReady`, full lifecycle integration, and consumer tests.
5. **Basket.API** has excellent coverage but 3 admin endpoints (`/api/v1/admin/carts/*`) are untested.
6. **Discount.Grpc** is missing tests for `EvaluateDiscountRules`, `UpdateDiscount`, and pagination on list RPCs.
7. **Ordering.Infrastructure** has only 2 tests — interceptors, `DailyReconciliationRunner`, and `MigratorHostedService` are uncovered.
8. **No cross-service E2E tests** exist anywhere in the solution.

---

## 2. Goal

- Add ~200+ new test methods across 7 phases, bringing all services to a consistent level of integration + unit test coverage.
- Every public endpoint has at minimum one happy-path test and one error-path test.
- Every MediatR handler (command + query) has at minimum one unit test.
- Domain model state machines have event-emission tests verifying which domain events fire on each transition.
- Infrastructure services (interceptors, background jobs, outbox) have unit tests.
- One cross-service E2E test validates the full order lifecycle: Cart → Checkout → Order → Kitchen Ticket → Delivered.

---

## 3. Out of scope

- **Performance/load testing** — not in scope; this plan covers functional correctness only.
- **UI/frontend tests** — no frontend exists in this repository.
- **Contract/Pact testing** — cross-service contract testing is desirable but deferred to a future plan.
- **Refactoring production code** — this plan only adds tests. If a test reveals a bug, the bug fix is scoped to the phase but not tracked as a separate deliverable.
- **CI/CD pipeline changes** — integrating `dotnet test` into GitHub Actions is a separate concern.
- **Mutation testing** — tools like Stryker.NET are out of scope.

---

## 4. Tech decisions

| Decision | Choice | Reason |
|:---|:---|:---|
| Test framework | xUnit 2.9.3 | Already used across all 11 test projects; consistency. |
| Assertion library | FluentAssertions 6.12.2 | Already adopted in all test projects; readable assertions. |
| Mocking library | NSubstitute 5.3.0 | Already adopted; simpler syntax than Moq for this codebase. |
| Integration test host | `WebApplicationFactory<T>` | Standard ASP.NET Core test host; already used by Basket, Catalog, Discount, Kitchen, Ordering. |
| Container orchestration | Testcontainers 4.1.0 | Already adopted; provides real Postgres/MSSQL/Redis/RabbitMQ for integration tests. |
| Identity integration tests | `WebApplicationFactory` + EF Core PostgreSQL via Testcontainers | Current tests use EF InMemory which doesn't test the HTTP pipeline or real database behavior. |
| Cross-service E2E | Docker Compose + HTTP clients | The only way to test true cross-service flows; uses the existing `docker-compose.yml`. |
| gRPC test client | `Grpc.Net.Client` + `GrpcChannel` | Already used in Discount.Grpc.Tests; matches existing pattern. |

---

## 5. Folder layout

```
orderly-microservices/
├── Services/
│   ├── Catalog/Catalog.API.Tests/
│   │   └── Integration/
│   │       ├── Endpoints/                  # ← Phase 1: new CRUD endpoint tests
│   │       │   ├── BrandEndpointTests.cs
│   │       │   ├── RestaurantEndpointTests.cs
│   │       │   ├── MenuItemEndpointTests.cs
│   │       │   ├── MenuCategoryEndpointTests.cs
│   │       │   ├── MenuSubCategoryEndpointTests.cs
│   │       │   ├── IngredientEndpointTests.cs
│   │       │   ├── IngredientAlternativeEndpointTests.cs
│   │       │   ├── MenuItemIngredientEndpointTests.cs
│   │       │   ├── MenuItemVariationEndpointTests.cs
│   │       │   ├── ComboItemEndpointTests.cs
│   │       │   ├── TableEndpointTests.cs
│   │       │   ├── MergedTableEndpointTests.cs
│   │       │   ├── ReservationEndpointTests.cs
│   │       │   ├── WalkInQueueEndpointTests.cs
│   │       │   ├── CustomerFeedbackEndpointTests.cs
│   │       │   ├── MenuItemAnalyticsEndpointTests.cs
│   │       │   └── PriceHistoryEndpointTests.cs
│   │       └── ...                         # existing tests
│   ├── Identity/Identity.API.Tests/
│   │   ├── Integration/                    # ← Phase 2: NEW directory
│   │   │   ├── IdentityWebApplicationFactory.cs
│   │   │   ├── AuthFlowIntegrationTests.cs
│   │   │   ├── UserEndpointIntegrationTests.cs
│   │   │   ├── RoleEndpointIntegrationTests.cs
│   │   │   ├── PermissionEndpointIntegrationTests.cs
│   │   │   ├── AuditLogEndpointIntegrationTests.cs
│   │   │   └── IdentityAuthorizationEnforcementTests.cs
│   │   └── ...                             # existing unit tests
│   ├── Ordering/
│   │   ├── Ordering.API.Tests/
│   │   │   └── Integration/
│   │   │       ├── CreateOrderEndpointTests.cs       # ← Phase 3
│   │   │       ├── UpdateOrderEndpointTests.cs
│   │   │       ├── DeleteOrderEndpointTests.cs
│   │   │       ├── GetOrdersByCustomerEndpointTests.cs
│   │   │       ├── GetOrderActivitiesEndpointTests.cs  # extend existing
│   │   │       ├── ItemTransitionEndpointTests.cs
│   │   │       └── OrderLifecycleEndpointTests.cs
│   │   ├── Ordering.Application.Tests/
│   │   │   └── Commands/
│   │   │       ├── CreateOrderHandlerTests.cs        # ← Phase 3
│   │   │       ├── UpdateOrderHandlerTests.cs
│   │   │       ├── DeleteOrderHandlerTests.cs
│   │   │   └── Queries/                              # ← Phase 3: NEW directory
│   │   │       ├── GetOrdersHandlerTests.cs
│   │   │       ├── GetOrderByIdHandlerTests.cs
│   │   │       ├── GetOrdersByCustomerHandlerTests.cs
│   │   │       └── GetOrderActivitiesHandlerTests.cs
│   │   ├── Ordering.Domain.Tests/
│   │   │   └── Models/
│   │   │       └── OrderDomainEventTests.cs          # ← Phase 6
│   │   └── Ordering.Infrastructure.Tests/
│   │       ├── Interceptors/                         # ← Phase 6: NEW directory
│   │       │   ├── AuditableEntityInterceptorTests.cs
│   │       │   └── DispatchDomainEventsInterceptorTests.cs
│   │       └── Services/                             # ← Phase 6: NEW directory
│   │           └── DailyReconciliationRunnerTests.cs
│   ├── Kitchen/Kitchen.API.Tests/
│   │   ├── Commands/
│   │   │   ├── RecallOrderHandlerTests.cs            # ← Phase 4
│   │   │   └── MarkItemReadyHandlerTests.cs          # ← Phase 4
│   │   ├── Queries/                                  # ← Phase 4: NEW directory
│   │   │   ├── GetKitchenQueueQueryHandlerTests.cs
│   │   │   └── GetTicketByIdQueryHandlerTests.cs
│   │   └── Integration/
│   │       └── KitchenLifecycleIntegrationTests.cs   # ← Phase 4
│   ├── Basket/Basket.API.Tests/
│   │   └── Integration/Endpoints/
│   │       └── AdminCartEndpointTests.cs             # ← Phase 5
│   └── Discount/Discount.Grpc.Tests/
│       └── Integration/
│           ├── EvaluateDiscountRulesTests.cs          # ← Phase 5
│           ├── UpdateDiscountRpcTests.cs               # ← Phase 5
│           └── ListPaginationTests.cs                 # ← Phase 5
└── E2E.Tests/                                        # ← Phase 7: NEW project
    ├── E2E.Tests.csproj
    ├── OrderLifecycleE2ETests.cs
    ├── DiscountApplicationE2ETests.cs
    └── docker-compose.e2e.yml
```

---

## 6. Test Specification

> The most important section — describes *what gets built* at a level the implementer can act on. One subsection per service.

### 6.1 Catalog.API Integration Tests (Phase 1)

Each test file targets one feature group. All tests use the existing `CatalogWebApplicationFactory` with Testcontainers (Postgres, Redis, RabbitMQ). Auth tokens are generated using the existing `TestAuthHelper` pattern.

*   **`BrandEndpointTests.cs`** — 5 tests:
    - `CreateBrand_ValidRequest_Returns201WithLocationHeader()` — POST `/api/v1/brands`, verify 201 + Location header + body matches.
    - `GetBrands_SeededData_Returns200WithPaginatedList()` — GET `/api/v1/brands`, verify default pagination.
    - `GetBrandById_ExistingId_Returns200()` — GET `/api/v1/brands/{id}`, verify body matches seed.
    - `UpdateBrand_ExistingId_Returns200WithUpdatedFields()` — PUT `/api/v1/brands/{id}`, verify fields changed.
    - `DeleteBrand_ExistingId_Returns204_ThenGetReturns404()` — DELETE + GET roundtrip.

*   **`RestaurantEndpointTests.cs`** — 5 tests: same CRUD pattern as Brands for `/api/v1/restaurants`.

*   **`MenuItemEndpointTests.cs`** — 7 tests:
    - CRUD pattern (5 tests) against `/api/v1/restaurants/{restaurantId}/menu-items` and `/api/v1/menu-items/{id}`.
    - `CreateMenuItem_InvalidRestaurantId_Returns404()` — foreign key guard.
    - `CreateMenuItem_MissingRequiredFields_Returns400()` — FluentValidation guard.

*   **`MenuCategoryEndpointTests.cs`** — 5 tests: CRUD pattern for `/api/v1/restaurants/{restaurantId}/menu-categories` and `/api/v1/menu-categories/{id}`.

*   **`MenuSubCategoryEndpointTests.cs`** — 5 tests: CRUD pattern for `/api/v1/menu-categories/{categoryId}/sub-categories` and `/api/v1/menu-sub-categories/{id}`.

*   **`IngredientEndpointTests.cs`** — 5 tests: CRUD pattern for `/api/v1/restaurants/{restaurantId}/ingredients`.

*   **`IngredientAlternativeEndpointTests.cs`** — 4 tests: CRUD (no GetById) for `/api/v1/restaurants/{restaurantId}/ingredient-alternatives`.

*   **`MenuItemIngredientEndpointTests.cs`** — 3 tests:
    - `AddMenuItemIngredient_ValidRequest_Returns201()` — POST.
    - `GetMenuItemIngredients_Returns200WithList()` — GET.
    - `RemoveMenuItemIngredient_Returns204()` — DELETE.

*   **`MenuItemVariationEndpointTests.cs`** — 4 tests: CRUD pattern for `/api/v1/menu-items/{menuItemId}/variations` and `/api/v1/menu-item-variations/{id}`.

*   **`ComboItemEndpointTests.cs`** — 4 tests: CRUD pattern for `/api/v1/menu-items/{comboMenuItemId}/combo-items` and `/api/v1/combo-items/{id}`.

*   **`TableEndpointTests.cs`** — 5 tests: CRUD for `/api/v1/restaurants/{restaurantId}/tables`.

*   **`MergedTableEndpointTests.cs`** — 3 tests:
    - `MergeTables_TwoTables_Returns201WithMergedGroup()` — POST.
    - `GetMergedTables_Returns200WithList()` — GET.
    - `SplitTables_Returns204_TablesBackToIndependent()` — DELETE roundtrip.

*   **`ReservationEndpointTests.cs`** — 6 tests:
    - `CreateReservation_ValidRequest_Returns201()` — POST.
    - `GetReservations_Returns200()` — GET list.
    - `GetReservationById_Returns200()` — GET single.
    - `ConfirmReservation_Returns200_StatusConfirmed()` — PUT `/confirm`.
    - `SeatReservation_AfterConfirm_Returns200()` — PUT `/seat`.
    - `CancelReservation_Returns200_StatusCancelled()` — PUT `/cancel`.

*   **`WalkInQueueEndpointTests.cs`** — 5 tests:
    - `AddToWalkInQueue_Returns201()` — POST.
    - `GetWalkInQueue_Returns200()` — GET.
    - `NotifyWalkInCustomer_Returns200()` — PUT `/notify`.
    - `SeatWalkInCustomer_Returns200()` — PUT `/seat`.
    - `RemoveFromQueue_Returns204()` — DELETE.

*   **`CustomerFeedbackEndpointTests.cs`** — 3 tests:
    - `SubmitFeedback_Returns201()` — POST.
    - `GetFeedback_Returns200WithList()` — GET.
    - `GetFeedbackById_Returns200()` — GET single.

*   **`MenuItemAnalyticsEndpointTests.cs`** — 3 tests:
    - `GetAnalytics_Returns200()` — GET list.
    - `GetAnalyticsById_Returns200()` — GET single.
    - `RecomputeTodayAnalytics_Returns200()` — POST recompute.

*   **`PriceHistoryEndpointTests.cs`** — 1 test:
    - `GetPriceHistory_Returns200()` — GET.

**Total Phase 1: ~73 tests.**

### 6.2 Identity.API Integration Tests (Phase 2)

New `Integration/` directory. Requires adding `Microsoft.AspNetCore.Mvc.Testing`, `Testcontainers.PostgreSql` to `Identity.API.Tests.csproj`. New `IdentityWebApplicationFactory` bootstrapping Identity.API with real Postgres.

*   **`IdentityWebApplicationFactory.cs`** — custom `WebApplicationFactory<Program>` overriding config to use Testcontainers Postgres, seeding OpenIddict applications and a test user.

*   **`AuthFlowIntegrationTests.cs`** — 6 tests:
    - `Register_ValidPayload_Returns200AndUserCreated()` — POST `/api/auth/register`.
    - `Register_DuplicateEmail_Returns400()` — duplicate detection.
    - `Login_ValidCredentials_Returns200WithTokens()` — POST `/api/auth/login`.
    - `Login_InvalidPassword_Returns401()` — bad credentials.
    - `Token_ValidRefreshToken_ReturnsNewAccessToken()` — POST `/api/auth/token` with refresh grant.
    - `Logout_ValidSession_Returns200()` — POST `/api/auth/logout`.

*   **`UserEndpointIntegrationTests.cs`** — 7 tests:
    - `CreateUser_Returns201()` — POST `/api/users`.
    - `GetUser_Returns200()` — GET `/api/users/{id}`.
    - `ListUsers_Returns200WithPagination()` — GET `/api/users`.
    - `UpdateUser_Returns200()` — PUT `/api/users/{id}`.
    - `DeleteUser_Returns204()` — DELETE `/api/users/{id}`.
    - `AssignRoles_Returns200()` — PUT `/api/users/{id}/roles`.
    - `AssignRestaurants_Returns200()` — PUT `/api/users/{id}/restaurants`.

*   **`RoleEndpointIntegrationTests.cs`** — 5 tests:
    - CRUD pattern (4 tests) for `/api/roles`.
    - `AssignPermissions_Returns200()` — PUT `/api/roles/{id}/permissions`.

*   **`PermissionEndpointIntegrationTests.cs`** — 2 tests:
    - `ListPermissions_Returns200()` — GET `/api/permissions`.
    - `AssignPermissionsToRole_Returns200()` — POST `/api/permissions/assign-to-role`.

*   **`AuditLogEndpointIntegrationTests.cs`** — 2 tests:
    - `GetAuditLogs_Returns200()` — GET `/api/audit-logs`.
    - `GetAuditLogs_WithFilters_Returns200()` — GET with query params.

*   **`IdentityAuthorizationEnforcementTests.cs`** — 8 tests:
    - `CreateUser_NoAuth_Returns401()`, `GetUser_NoAuth_Returns401()`, `DeleteUser_NoAuth_Returns401()`, etc.
    - `CreateRole_WrongPermission_Returns403()`, `AssignPermissions_WrongPermission_Returns403()`.

**Total Phase 2: ~30 tests.**

### 6.3 Ordering CRUD & Query Handler Tests (Phase 3)

*   **`CreateOrderEndpointTests.cs`** (Integration) — 4 tests:
    - `CreateOrder_ValidPayload_Returns201WithOrderId()` — POST `/api/v1/orders`.
    - `CreateOrder_MissingCustomer_Returns400()` — validation.
    - `CreateOrder_EmptyItems_Returns400()` — validation.
    - `CreateOrder_WithDiscount_PersistsDiscountOnBill()` — discount integration.

*   **`UpdateOrderEndpointTests.cs`** (Integration) — 3 tests:
    - `UpdateOrder_ValidPayload_Returns200()` — PUT `/api/v1/orders`.
    - `UpdateOrder_NonExistent_Returns404()` — not found.
    - `UpdateOrder_InvalidPayload_Returns400()` — validation.

*   **`DeleteOrderEndpointTests.cs`** (Integration) — 3 tests:
    - `DeleteOrder_ExistingOrder_Returns204()` — DELETE `/api/v1/orders/{id}`.
    - `DeleteOrder_NonExistent_Returns404()` — not found.
    - `DeleteOrder_AlreadyDeleted_Returns404()` — idempotency.

*   **`GetOrdersByCustomerEndpointTests.cs`** (Integration) — 2 tests:
    - `GetOrdersByCustomer_WithOrders_Returns200()` — happy path.
    - `GetOrdersByCustomer_NoOrders_Returns200EmptyList()` — empty result.

*   **`GetOrderActivitiesEndpointTests.cs`** (Integration, extend existing) — 2 tests:
    - `GetOrderActivities_WithActivities_Returns200()` — happy path.
    - `GetOrderActivities_UnknownOrder_Returns404()` — not found.

*   **`ItemTransitionEndpointTests.cs`** (Integration) — 4 tests:
    - `StartItemPrep_ValidItem_Returns204()` — POST `.../items/{itemId}/start-prep`.
    - `MarkItemReady_AfterPrep_Returns204()` — POST `.../items/{itemId}/mark-ready`.
    - `StartItemPrep_AlreadyPreparing_Returns400()` — invalid state.
    - `MarkItemReady_BeforePrep_Returns400()` — invalid state.

*   **`OrderLifecycleEndpointTests.cs`** (Integration) — 3 tests:
    - `FullLifecycle_Pending_To_Delivered()` — Confirm → StartPrep → MarkReady → MarkDelivered.
    - `MarkOrderDelivered_FromReady_Returns204()` — single transition.
    - `CancelOrder_AfterConfirm_Returns204()` — cancel mid-lifecycle.

*   **`CreateOrderHandlerTests.cs`** (Unit) — 3 tests:
    - `Handle_ValidCommand_CreatesOrderAndReturnsId()` — happy path.
    - `Handle_RaisesOrderCreatedDomainEvent()` — event emission.
    - `Handle_PersistsAllOrderItems()` — item count match.

*   **`UpdateOrderHandlerTests.cs`** (Unit) — 2 tests:
    - `Handle_ExistingOrder_UpdatesAndSaves()` — happy path.
    - `Handle_UnknownOrder_ThrowsNotFound()` — not found.

*   **`DeleteOrderHandlerTests.cs`** (Unit) — 2 tests:
    - `Handle_ExistingOrder_RemovesAndSaves()` — happy path.
    - `Handle_UnknownOrder_ThrowsNotFound()` — not found.

*   **`GetOrdersHandlerTests.cs`** (Unit) — 3 tests:
    - `Handle_ReturnsAllOrders()` — happy path.
    - `Handle_WithPagination_ReturnsCorrectSlice()` — pagination.
    - `Handle_EmptyStore_ReturnsEmptyResult()` — empty.

*   **`GetOrderByIdHandlerTests.cs`** (Unit) — 2 tests:
    - `Handle_ExistingOrder_ReturnsOrderDto()` — happy path.
    - `Handle_UnknownOrder_ThrowsNotFound()` — not found.

*   **`GetOrdersByCustomerHandlerTests.cs`** (Unit) — 2 tests:
    - `Handle_WithOrders_ReturnsFilteredList()` — happy path.
    - `Handle_NoOrders_ReturnsEmptyResult()` — empty.

*   **`GetOrderActivitiesHandlerTests.cs`** (Unit) — 2 tests:
    - `Handle_WithActivities_ReturnsOrderedList()` — happy path.
    - `Handle_UnknownOrder_ThrowsNotFound()` — not found.

**Total Phase 3: ~37 tests.**

### 6.4 Kitchen Lifecycle & Missing Handler Tests (Phase 4)

*   **`RecallOrderHandlerTests.cs`** (Unit) — 3 tests:
    - `Handle_FromBumped_TransitionsToReady()` — happy path.
    - `Handle_FromReady_Throws()` — invalid state.
    - `Handle_PublishesKitchenOrderRecalledIntegrationEvent()` — event emission.

*   **`MarkItemReadyHandlerTests.cs`** (Unit) — 3 tests:
    - `Handle_PreparingItem_MarksReady()` — happy path.
    - `Handle_PendingItem_Throws()` — invalid state.
    - `Handle_AllItemsReady_RaisesAllItemsReadyFlag()` — aggregate state.

*   **`GetKitchenQueueQueryHandlerTests.cs`** (Unit) — 3 tests:
    - `Handle_ReturnsTicketsSortedByPriority()` — ordering.
    - `Handle_WithPagination_ReturnsCorrectSlice()` — pagination.
    - `Handle_EmptyQueue_ReturnsEmptyResult()` — empty.

*   **`GetTicketByIdQueryHandlerTests.cs`** (Unit) — 2 tests:
    - `Handle_ExistingTicket_ReturnsDto()` — happy path.
    - `Handle_UnknownTicket_ThrowsNotFound()` — not found.

*   **`KitchenLifecycleIntegrationTests.cs`** (Integration) — 5 tests:
    - `FullLifecycle_NewToBumped()` — Accept → StartItemPrep → MarkItemReady → MarkReady → Bump. Verifies each state transition returns success.
    - `Recall_FromBumped_BackToReady()` — Bump → Recall roundtrip.
    - `Cancel_FromAnyActiveState_Returns200()` — Cancel with reason.
    - `SignalR_TicketAccepted_BroadcastReceived()` — verify SignalR hub broadcasts on accept (if WAF supports SignalR test client).
    - `CreateTicketFromOrderCreated_IntegrationEvent()` — publish `OrderCreatedIntegrationEvent` to RabbitMQ, verify ticket appears in queue.

**Total Phase 4: ~16 tests.**

### 6.5 Basket Admin + Discount RPC Gaps (Phase 5)

*   **`AdminCartEndpointTests.cs`** (Integration) — 6 tests:
    - `ListCarts_WithAdminPermission_Returns200()` — GET `/api/v1/admin/carts`.
    - `ListCarts_WithoutAdminPermission_Returns403()` — forbidden.
    - `ListCarts_Anonymous_Returns401()` — auth check.
    - `UpsertCartAdmin_Returns200()` — PUT `/api/v1/admin/carts/{userId}`.
    - `DeleteCartAdmin_Returns204()` — DELETE `/api/v1/admin/carts/{userId}`.
    - `DeleteCartAdmin_NonExistent_Returns204()` — idempotent delete.

*   **`EvaluateDiscountRulesTests.cs`** (Integration) — 4 tests:
    - `EvaluateRules_MatchingOrder_ReturnsApplicableDiscounts()` — happy path.
    - `EvaluateRules_NoMatchingRules_ReturnsEmptyList()` — no match.
    - `EvaluateRules_ExpiredRule_ExcludedFromResults()` — expiry filtering.
    - `EvaluateRules_CrossTenant_Isolated()` — tenant isolation.

*   **`UpdateDiscountRpcTests.cs`** (Integration) — 3 tests:
    - `UpdateDiscount_ValidRequest_ReturnsUpdatedCoupon()` — happy path.
    - `UpdateDiscount_NonExistent_ReturnsNotFoundStatus()` — not found.
    - `UpdateDiscount_InvalidFields_ReturnsInvalidArgument()` — validation.

*   **`ListPaginationTests.cs`** (Integration) — 4 tests:
    - `ListDiscountRules_DefaultPagination_ReturnsFirstPage()` — happy path.
    - `ListDiscountRules_Page2_ReturnsSecondSlice()` — offset.
    - `ListRewardCodes_DefaultPagination_ReturnsFirstPage()` — happy path.
    - `ListRewardCodes_FilterByKind_ReturnsFilteredList()` — filtering.

**Total Phase 5: ~17 tests.**

### 6.6 Ordering Infrastructure & Domain Event Tests (Phase 6)

*   **`OrderDomainEventTests.cs`** (Unit) — 9 tests:
    - `Confirm_RaisesOrderConfirmedEvent()` — verify `OrderConfirmedEvent` in `DomainEvents`.
    - `StartPrep_RaisesOrderPreparingEvent()` — verify `OrderPreparingEvent`.
    - `MarkReady_RaisesOrderReadyEvent()` — verify `OrderReadyEvent`.
    - `MarkDelivered_RaisesOrderDeliveredEvent()` — verify `OrderDeliveredEvent`.
    - `Cancel_RaisesOrderCancelledEvent()` — verify `OrderCancelledEvent`.
    - `Create_RaisesOrderCreatedEvent()` — verify `OrderCreatedEvent` on aggregate creation.
    - `Update_RaisesOrderUpdatedEvent()` — verify `OrderUpdatedEvent`.
    - `MultipleTransitions_CumulateDomainEvents()` — chaining transitions accumulates all events.
    - `ClearDomainEvents_PurgesAccumulatedEvents()` — clear after dispatch.

*   **`AuditableEntityInterceptorTests.cs`** (Unit) — 4 tests:
    - `SavingChanges_NewEntity_SetsCreatedAtAndCreatedBy()` — creation audit trail.
    - `SavingChanges_ModifiedEntity_SetsLastModifiedAtAndLastModifiedBy()` — update audit trail.
    - `SavingChanges_DeletedEntity_SetsDeletedAtAndDeletedBy()` — soft-delete audit trail (if `IDeletableEntity`).
    - `SavingChanges_NoChanges_NoAuditFieldsSet()` — no-op guard.

*   **`DispatchDomainEventsInterceptorTests.cs`** (Unit) — 3 tests:
    - `SavingChanges_WithDomainEvents_DispatchesAllViaMediator()` — verify all events published.
    - `SavingChanges_NoDomainEvents_NoDispatch()` — no-op.
    - `SavingChanges_AfterDispatch_EventsAreCleared()` — verify clearing.

*   **`DailyReconciliationRunnerTests.cs`** (Unit) — 3 tests:
    - `ExecuteAsync_RunsReconciliation_AndLogsResult()` — happy path.
    - `ExecuteAsync_Disabled_ExitsCleanly()` — configuration off.
    - `ExecuteAsync_CancellationRequested_StopsGracefully()` — cancellation.

**Total Phase 6: ~19 tests.**

### 6.7 Cross-Service E2E Lifecycle Tests (Phase 7)

New `E2E.Tests` project at the solution root. Uses `docker-compose up -d --build` to spin up the full system. HTTP clients call real endpoints. Tests are `[Trait("Category", "E2E")]` so CI can opt in/out.

*   **`OrderLifecycleE2ETests.cs`** — 3 tests:
    - `FullOrderLifecycle_CartToDelivered()`:
      1. Register user via Identity → get token.
      2. PUT cart via Basket.
      3. POST checkout via Basket → verify outbox publishes `BasketCheckoutEvent`.
      4. Verify Order created in Ordering (poll GET `/api/v1/orders`).
      5. Confirm order, start prep, mark ready, mark delivered.
      6. Verify final order status is `Delivered`.
    - `OrderCancellation_MidLifecycle()`:
      1. Create order via cart checkout.
      2. Confirm order.
      3. Cancel order with reason.
      4. Verify order status is `Cancelled` and activity log has cancel entry.
    - `KitchenTicketCreatedOnOrderConfirm()`:
      1. Create order via cart checkout.
      2. Confirm order.
      3. Verify kitchen ticket appears via GET `/api/v1/kitchen/queue`.

*   **`DiscountApplicationE2ETests.cs`** — 2 tests:
    - `DiscountAppliedToCart_ReflectedInOrder()`:
      1. Create discount via Discount.Grpc.
      2. Add items + coupon to cart.
      3. Checkout → verify order total reflects discount.
    - `FeedbackRewardRedemption()`:
      1. Submit feedback via Catalog.
      2. Verify reward code created in Discount.
      3. Apply reward code to next cart → verify discount applied.

**Total Phase 7: ~5 tests.**

---

## 7. Integration Points

| Source Service | Target Service | Integration Mechanism | Tests Covering It |
|:---|:---|:---|:---|
| Basket → Ordering | `BasketCheckoutEvent` via RabbitMQ | Outbox + MassTransit consumer | Phase 7 E2E |
| Ordering → Kitchen | `OrderCreatedIntegrationEvent` via RabbitMQ | Outbox + MassTransit consumer | Phase 4 Integration, Phase 7 E2E |
| Catalog → Discount | `FeedbackSubmittedIntegrationEvent` via RabbitMQ | Outbox + MassTransit consumer | ✅ Already covered in Discount.Grpc.Tests |
| Catalog → Ordering | `OrderCompletedIntegrationEvent` via RabbitMQ | MassTransit consumer | ✅ Already covered in Catalog.API.Tests |
| Discount → Basket | gRPC `GetDiscount`/`RedeemDiscount` | Synchronous gRPC call | ✅ Already covered in Basket unit tests |

---

## 8. Security guardrails

> [!CAUTION]
> Integration tests that generate auth tokens MUST use the existing `TestAuthHelper` / `DevJwtBearerFallback` pattern — never hardcode real credentials or production-like secrets in test code.

| Risk | Mitigation |
|---|---|
| Test tokens leaking to production | All test tokens use `JWT_SECRET` env var scoped to test process via `WebApplicationFactory` config override; never written to disk. |
| Testcontainers leaving orphaned containers | `IAsyncLifetime.DisposeAsync()` tears down containers; xUnit `[Collection]` groups share a single container lifecycle. |
| Cross-test state leakage | Each integration test class uses a fresh database (Testcontainers) or `Respawn` to reset state between tests. |
| E2E tests running accidentally in CI | Phase 7 tests use `[Trait("Category", "E2E")]` and require explicit opt-in via `dotnet test --filter Category=E2E`. |

---

## 9. Development Phases

### Phase overview

| Phase | Name | Tests delivered | Goal |
|:---:|---|:---:|---|
| **1** | Catalog CRUD happy-path integration tests | ~73 | Every Catalog CRUD endpoint has at least one happy-path + one error-path integration test. |
| **2** | Identity integration tests & auth flow coverage | ~30 | Identity.API gets WebApplicationFactory integration tests; login/register/token flows covered. |
| **3** | Ordering CRUD + query handler tests | ~37 | CreateOrder/UpdateOrder/DeleteOrder happy paths; all 7 Application handlers have unit tests. |
| **4** | Kitchen lifecycle & missing handler tests | ~16 | Full ticket lifecycle integration test; RecallOrder + MarkItemReady handlers covered. |
| **5** | Basket admin endpoints + Discount RPC gaps | ~17 | Admin cart endpoints; EvaluateDiscountRules + UpdateDiscount + list pagination covered. |
| **6** | Ordering Infrastructure & Domain event tests | ~19 | Domain event emission verified; EF interceptors and DailyReconciliationRunner covered. |
| **7** | Cross-service E2E lifecycle validation | ~5 | Full cart-to-delivered lifecycle; discount application E2E. |

---

### Phase 1 — Catalog CRUD Happy-Path Integration Tests

**Goal**: every one of the ~60 Catalog CRUD endpoints has at least one integration test verifying the HTTP pipeline, auth, persistence, and response shape.

**Status**: ⏸ Pending

**Deliverables**:

- [ ] `BrandEndpointTests.cs` — 5 tests (Create, GetAll, GetById, Update, Delete)
- [ ] `RestaurantEndpointTests.cs` — 5 tests
- [ ] `MenuItemEndpointTests.cs` — 7 tests (CRUD + validation + FK guard)
- [ ] `MenuCategoryEndpointTests.cs` — 5 tests
- [ ] `MenuSubCategoryEndpointTests.cs` — 5 tests
- [ ] `IngredientEndpointTests.cs` — 5 tests
- [ ] `IngredientAlternativeEndpointTests.cs` — 4 tests
- [ ] `MenuItemIngredientEndpointTests.cs` — 3 tests
- [ ] `MenuItemVariationEndpointTests.cs` — 4 tests
- [ ] `ComboItemEndpointTests.cs` — 4 tests
- [ ] `TableEndpointTests.cs` — 5 tests
- [ ] `MergedTableEndpointTests.cs` — 3 tests
- [ ] `ReservationEndpointTests.cs` — 6 tests
- [ ] `WalkInQueueEndpointTests.cs` — 5 tests
- [ ] `CustomerFeedbackEndpointTests.cs` — 3 tests
- [ ] `MenuItemAnalyticsEndpointTests.cs` — 3 tests
- [ ] `PriceHistoryEndpointTests.cs` — 1 test

**Exit criteria**: `dotnet test --project orderly-microservices/Services/Catalog/Catalog.API.Tests/ --filter "FullyQualifiedName~Integration.Endpoints"` runs all ~73 new tests green.

---

### Phase 2 — Identity Integration Tests & Auth Flow Coverage

**Goal**: Identity.API has real HTTP pipeline tests with Testcontainers Postgres; login/register/token OAuth2 flows are covered end-to-end within the service.

**Status**: 🔒 Blocked (by Phase 1)

**Deliverables**:

- [ ] Add `Microsoft.AspNetCore.Mvc.Testing` and `Testcontainers.PostgreSql` to `Identity.API.Tests.csproj`
- [ ] `IdentityWebApplicationFactory.cs` — custom WAF with Testcontainers Postgres
- [ ] `AuthFlowIntegrationTests.cs` — 6 tests (register, login, token refresh, logout, error paths)
- [ ] `UserEndpointIntegrationTests.cs` — 7 tests
- [ ] `RoleEndpointIntegrationTests.cs` — 5 tests
- [ ] `PermissionEndpointIntegrationTests.cs` — 2 tests
- [ ] `AuditLogEndpointIntegrationTests.cs` — 2 tests
- [ ] `IdentityAuthorizationEnforcementTests.cs` — 8 tests

**Exit criteria**: `dotnet test --project orderly-microservices/Services/Identity/Identity.API.Tests/ --filter "FullyQualifiedName~Integration"` runs all ~30 new tests green.

---

### Phase 3 — Ordering CRUD + Query Handler Tests

**Goal**: CreateOrder/UpdateOrder/DeleteOrder have happy-path integration tests; all Application layer handlers have unit tests.

**Status**: 🔒 Blocked (by Phase 2)

**Deliverables**:

- [ ] `CreateOrderEndpointTests.cs` — 4 integration tests
- [ ] `UpdateOrderEndpointTests.cs` — 3 integration tests
- [ ] `DeleteOrderEndpointTests.cs` — 3 integration tests
- [ ] `GetOrdersByCustomerEndpointTests.cs` — 2 integration tests
- [ ] `GetOrderActivitiesEndpointTests.cs` — 2 integration tests (extend existing)
- [ ] `ItemTransitionEndpointTests.cs` — 4 integration tests
- [ ] `OrderLifecycleEndpointTests.cs` — 3 integration tests
- [ ] `CreateOrderHandlerTests.cs` — 3 unit tests
- [ ] `UpdateOrderHandlerTests.cs` — 2 unit tests
- [ ] `DeleteOrderHandlerTests.cs` — 2 unit tests
- [ ] `GetOrdersHandlerTests.cs` — 3 unit tests
- [ ] `GetOrderByIdHandlerTests.cs` — 2 unit tests
- [ ] `GetOrdersByCustomerHandlerTests.cs` — 2 unit tests
- [ ] `GetOrderActivitiesHandlerTests.cs` — 2 unit tests

**Exit criteria**: `dotnet test --project orderly-microservices/Services/Ordering/Ordering.API.Tests/` and `dotnet test --project orderly-microservices/Services/Ordering/Ordering.Application.Tests/` both green with all new tests passing.

---

### Phase 4 — Kitchen Lifecycle & Missing Handler Tests

**Goal**: Full ticket lifecycle integration test; missing handler unit tests filled; consumer test for `OrderCreatedIntegrationEvent`.

**Status**: 🔒 Blocked (by Phase 3)

**Deliverables**:

- [ ] `RecallOrderHandlerTests.cs` — 3 unit tests
- [ ] `MarkItemReadyHandlerTests.cs` — 3 unit tests
- [ ] `GetKitchenQueueQueryHandlerTests.cs` — 3 unit tests
- [ ] `GetTicketByIdQueryHandlerTests.cs` — 2 unit tests
- [ ] `KitchenLifecycleIntegrationTests.cs` — 5 integration tests

**Exit criteria**: `dotnet test --project orderly-microservices/Services/Kitchen/Kitchen.API.Tests/` green with all new tests passing.

---

### Phase 5 — Basket Admin Endpoints + Discount RPC Gaps

**Goal**: Admin cart endpoints covered; `EvaluateDiscountRules`, `UpdateDiscount`, and list pagination RPCs tested.

**Status**: 🔒 Blocked (by Phase 4)

**Deliverables**:

- [ ] `AdminCartEndpointTests.cs` — 6 integration tests
- [ ] `EvaluateDiscountRulesTests.cs` — 4 integration tests
- [ ] `UpdateDiscountRpcTests.cs` — 3 integration tests
- [ ] `ListPaginationTests.cs` — 4 integration tests

**Exit criteria**: `dotnet test --project orderly-microservices/Services/Basket/Basket.API.Tests/` and `dotnet test --project orderly-microservices/Services/Discount/Discount.Grpc.Tests/` both green.

---

### Phase 6 — Ordering Infrastructure & Domain Event Tests

**Goal**: Domain events verified per state transition; EF interceptors tested; DailyReconciliationRunner covered.

**Status**: 🔒 Blocked (by Phase 5)

**Deliverables**:

- [ ] `OrderDomainEventTests.cs` — 9 unit tests
- [ ] `AuditableEntityInterceptorTests.cs` — 4 unit tests
- [ ] `DispatchDomainEventsInterceptorTests.cs` — 3 unit tests
- [ ] `DailyReconciliationRunnerTests.cs` — 3 unit tests

**Exit criteria**: `dotnet test --project orderly-microservices/Services/Ordering/Ordering.Domain.Tests/` and `dotnet test --project orderly-microservices/Services/Ordering/Ordering.Infrastructure.Tests/` both green.

---

### Phase 7 — Cross-Service E2E Lifecycle Validation

**Goal**: one end-to-end test proves the full order lifecycle across Basket → Ordering → Kitchen; one test proves discount application across Discount → Basket → Ordering.

**Status**: 🔒 Blocked (by Phase 6)

**Deliverables**:

- [ ] `E2E.Tests.csproj` — new test project with HTTP client + Docker Compose orchestration
- [ ] `docker-compose.e2e.yml` — E2E-specific compose override (fixed ports, test seeding)
- [ ] `OrderLifecycleE2ETests.cs` — 3 E2E tests
- [ ] `DiscountApplicationE2ETests.cs` — 2 E2E tests
- [ ] Add `E2E.Tests` to `orderly-microservices.slnx`

**Exit criteria**: `docker-compose -f docker-compose.yml -f E2E.Tests/docker-compose.e2e.yml up -d --build && dotnet test --project orderly-microservices/E2E.Tests/ --filter Category=E2E` all green.

---

## 10. Technical considerations

### 10.1 Cross-cutting

**Testcontainers Docker requirement** — `[pending]` All integration tests (Phases 1–6) require Docker Desktop running locally. CI runners must have Docker-in-Docker or a Docker socket available. Document this prerequisite in the test project READMEs.

**Shared `WebApplicationFactory` pattern** — `[pending]` Each service already has its own WAF (`BasketWebApplicationFactory`, `CatalogWebApplicationFactory`, etc.). New tests MUST reuse the existing WAF, not create parallel ones. Identity is the exception (Phase 2 creates a new one).

**Test data seeding** — `[pending]` Integration tests should seed their own data in Arrange, not rely on shared seed data that could create ordering dependencies. Use `IAsyncLifetime.InitializeAsync()` for per-class seeding.

**Parallel execution** — `[pending]` Testcontainer-based tests should be serial within a collection (shared container) but parallelizable across collections. Follow the existing `xunit.runner.json` patterns per project.

### 10.2 Phase 7 — E2E specifics

- **[pending]** E2E tests are slow (minutes) and require the full Docker Compose stack. They should be `[Trait("Category", "E2E")]` and excluded from default `dotnet test` runs.
- **[pending]** E2E tests need a retry/polling mechanism for async flows (e.g., waiting for RabbitMQ message to create an order). Use `Polly` retry with exponential backoff, max 30 seconds.
- **[pending]** E2E tests should create their own test users via the Identity API, not rely on seed data.

---

## Changelog

### v1.0 (2026-08-01) — initial draft
- Created plan with 7 phases covering ~197 new tests.
- Sections 0–10 drafted.
- Gap analysis cross-referenced against API endpoints audit and existing test audit.
- Skills assigned: `csharp-xunit` (primary), `csharp-developer` (supporting).

### **v1.1 (2026-08-01)**: Added Improved Practices subsection, clarified Phase 2 dependency, updated Phase 2 status to “Blocked (shared infra)”.