# Ordering — Order Activity Feed Plan

> **Scope:** add a per-order activity trail to the existing `Ordering` microservice so that `GET /api/v1/orders/{id}` returns the order **plus** an ordered list of activity entries (created, updated, every state transition, every item-prep transition, cancellation). Closes the gap surfaced by the user's "see the detail of an order along with the activity" check: today `OrderDto` (`Ordering.Application/Dtos/OrderDto.cs:3`) carries only a flat snapshot of lifecycle timestamps — there is no activity feed, no per-action row, no actor chain.
>
> **Origin:** synthesized from the csharp-expert walk-through of `Services/Ordering/` on 2026-07-15. Sections of `BASKET_SERVICE_PLAN.md`, `CATALOG_SERVICE_PLAN.md`, and `KITCHEN_FOLLOWUP_PLAN.md` are mirrored by reference where they apply unchanged.

---

## 0. Skill & documentation conventions

These two conventions apply to **every phase** below. They are non-negotiable — no implementation commit for this plan should land without satisfying both.

### 0.1 Skill mandate — `csharp-developer`

> **All implementation work on this plan MUST invoke the `csharp-developer` skill** (base directory `.claude/skills/csharp-developer`, invoked as `/csharp-developer` in Claude Code).
>
> The skill is the source of truth for C# 12+ / .NET 10 idiom, async patterns, EF Core usage (this plan adds one aggregate child + one migration; same EF Core shape as Catalog and Discount), ASP.NET Core + Carter, MediatR CQRS, xUnit + Testcontainers test scaffolding, and the project's "MUST DO / MUST NOT DO" guard rails (nullable enabled, primary constructors, async/await with `CancellationToken`, `Result<T>` for error paths, no blocking calls, DTO mapping for API responses).
>
> Companion reference files under `.claude/skills/csharp-developer/references/` are loaded on demand per the skill's table:
> - `modern-csharp.md` — records, primary constructors, collection expressions, pattern matching, nullable types.
> - `aspnet-core.md` — Minimal API / Carter endpoints, DI, middleware, routing.
> - `entity-framework.md` — loaded in Phase 1 (`OrderActivityConfiguration` + new migration).
> - `performance.md` — not loaded for this plan; no perf-sensitive hot path is touched.
>
> The skill is *additional* to whatever other skills are relevant (e.g. `csharp-xunit` for test scaffolding, `api-design-principles` for the optional §6.2 activity-filter endpoint). It is **not** a substitute for the plan; the plan wins where they disagree.

### 0.2 Phase-completion documentation update

> **After completing every phase, `docs/architecture/current-architecture.md` MUST be updated to reflect the new state of the codebase before the implementation commit is finalized.**
>
> `current-architecture.md` is described in its own header as *"the snapshot view of the codebase — no planned features, no gap list. As new functionality is built … update this file to match."* It must never describe Ordering with capabilities that don't exist yet, and it must never lag a shipped phase.
>
> For this plan the recurring touch points are:
>
> | Doc section | Why it changes per phase |
> |---|---|
> | §4.5 Ordering Service | Phase 1: new `OrderActivity` child entity + `OrderActivityConfiguration` + new migration; `OrderDto` gains `Activities[]`; `GetOrderByIdHandler` gains `Include(o => o.Activities)`; per-state-transition docstring gains a "raises `OrderActivity`" line. |
> | §4.5 Endpoint surface | Phase 2 (optional): new `GET /api/v1/orders/{id}/activities` row. |
> | §6 Data Stores | Phase 1: `order_activities` table row added to the relational store list; index note `(OrderId, OccurredAt)`. |
> | §9 Cross-Cutting Patterns | Phase 1: `OrderActivity` recording rule is documented under "Aggregate behaviour" (every state transition appends one row in the same transaction as the aggregate mutation — same atomicity guarantee as `outbox_messages`). |
>
> The implementer writes the doc update as part of the phase, not as a follow-up commit. Each phase below lists its **Doc-update scope**.

### 0.3 Code-quality guard rails (dotnet-best-practices)

Ordering inherits the guard rails from `CATALOG_SERVICE_PLAN.md §0.3` verbatim, layered with the project-specific overrides below. Mirror-references drift silently; copy-into-context is verbose but drift-proof. Catalog §0.3 is the authoritative source; if Catalog §0.3 changes, this section changes in lockstep on the next Ordering activity-feed phase commit.

**Project-specific overrides (layered on top of Catalog §0.3):**

- **No event sourcing.** Option 1 from the user's question — persist an `OrderActivity` child entity on every state transition. The aggregate remains the source of truth; the activity child is read-only history appended inside the same `SaveChangesAsync` transaction as the aggregate mutation (the existing `outbox_messages` interceptor already enforces that transaction boundary). Option 2 (persist `IDomainEvent` records from `DispatchDomainEventsInterceptor`) and option 3 (event-sourced store) are explicitly rejected — see §3.
- **`OrderActivity` is a child entity, not an aggregate root.** Same parent as `OrderItem` and `OrderBill`: a private `List<OrderActivity>` on the aggregate, exposed as `IReadOnlyCollection<OrderActivity>` via a public getter, persisted by EF Core's `HasMany(o => o.Activities).WithOne().HasForeignKey(a => a.OrderId).OnDelete(DeleteBehavior.Cascade)` configuration. No `OrderActivity` `DbSet` is exposed on `IApplicationDbContext` — all access goes through the `Order` aggregate.
- **`CancellationToken` end-to-end** — every new public async method accepts a `CancellationToken` and propagates it to EF Core reads. The Phase 2 `GetOrderActivitiesQuery` handler is the only new async surface in this plan.
- **`ArgumentNullException.ThrowIfNull` on every primary-constructor reference parameter** — `OrderActivity.Create(...)`, the new mapping helpers, and the Phase 2 handler all get the guard.
- **`IOptions<T>` (not `<Snapshot>`, not `<Monitor>`)** for `OrderActivityOptions` (future Phase 2 retention window, if it lands). For Phase 1 no options class is needed.
- **XML documentation on every public member** — enforced by `dotnet build /p:TreatWarningsAsErrors=true /p:GenerateDocumentationFile=true` from `Ordering.Application/`. CS1591 warnings fail the build.
- **Test framework: xUnit + FluentAssertions + Moq + Testcontainers (SQL Server).** This is the project convention (Catalog, Basket, Kitchen). The .NET-best-practices skill's reference to MSTest is **rejected** — see Catalog §0.3.11 mirror.
- **AAA pattern** is implicit in test method names; explicit `// Arrange // Act // Assert` comments are not required.
- **Naming convention:** `MethodName_StateUnderTest_ExpectedBehavior` for unit tests; `Handler_Scenario_ExpectedResult` for handler tests. Examples: `OrderTests.Confirm_AppendsOrderConfirmedActivity_WithActor`, `GetOrderByIdHandlerTests.Handler_ReturnsActivitiesOrderedByOccurredAt`, `OrderExtensionsTests.ToOrderDto_NullMetadata_MapsToNullField`.
- **Theory tests for enum coverage:** one `[Theory] [InlineData(OrderActivityType.X)]` row per enum value locks the contract that every transition appends successfully (and the test fails the build the day someone adds a new enum value without a corresponding transition method).
- **Null-parameter validation tests** for every factory method and every new public method, per the .NET-best-practices skill's Testing Standards.
- **No `Result<T>`** — domain exceptions (`InvalidOrderStateTransitionException`, `InvalidOrderItemStateTransitionException`, the new `OrderActivityInvariantException`) are the project convention.

### 0.4 Security & privacy

- **`Notes` is the only free-text field on `OrderActivity`.** Length-capped at 2000 characters in the domain factory (`OrderActivity.Create`) and at 2000 in EF Core mapping (`OrderActivityConfiguration.Notes.HasMaxLength(2000)`). No HTML sanitization — the field is shown verbatim in JSON responses; the FE is responsible for safe rendering. No content filter — the field is trusted-actor input (cancellation reason + future admin notes); untrusted strings never land here.
- **No PII beyond `ActorUserId`.** The activity row stores a Guid actor reference, not a name, email, or IP. If the FE needs display names, it resolves them via Identity (`GET /api/v1/users/{id}`) — not from the activity feed.
- **Immutability from the domain side.** `OrderActivity` properties are `private set` (see Phase 1 §6.1 code block). Once appended, a row can only be removed via cascade from `Order` deletion. This is the audit guarantee.
- **Authorization for reading.** The feed is embedded in `OrderDto`; the existing `GET /api/v1/orders/{id}` permission (`orders:view_*`) covers it. **Phase 2's standalone endpoint inherits the same permission.** If the project later wants a stricter "audit view" (separate from "operational view"), that becomes an Identity permission + a Carter policy — out of scope here.
- **GDPR note (out of scope for this plan).** The activity feed is part of the user's data and must be included in any future data-export path (e.g. `GET /api/v1/users/{id}/data-export`). v1 does not have such an endpoint; v2 will need to include activities.

### 0.5 Performance

- **Read-path hot path: `GetOrderByIdHandler` + `OrderExtensions.ToOrderDto`.** Both run on every `GET /orders/{id}`. Adding `Include(o => o.Activities)` keeps the SQL a single query (EF Core's split-query default is `false` for a single Include chain); the mapping cost is `O(N_items + M_activities)` per call — sub-millisecond for typical orders (≤ 20 items, ≤ 30 activities). No benchmark required for v1; document this in the doc-update so a future reviewer doesn't re-measure.
- **`JsonSerializerOptions` is cached.** See Phase 1 §6.1 (Ordering.Infrastructure commit) — a shared static field avoids per-call allocation and guarantees NodaTime-aware serialization consistency when `Metadata` later gains `Instant` fields. This locks down the same latent-bug class that `Basket.CachedBasketRepository` had.
- **Index `(OrderId, OccurredAt)` is a covering index for the only read pattern.** The query is always `WHERE OrderId = @id ORDER BY OccurredAt ASC` (the `ThenBy(Id.Value)` is an in-memory tie-break on Guid). The covering index avoids both the table scan and the sort. **Do not drop this index without re-measuring** — every read path uses it.
- **No `AsSplitQuery()` for Phase 1.** EF Core's single-query default returns activities in one round-trip; split-query only helps when the join produces cartesian-product blow-up, which it doesn't here (orders have bounded items + activities).

### 0.6 Correlation context (BuildingBlocks contribution)

The activity feed stores a per-activity `CorrelationId` so the FE can correlate an activity row with the log scope / OpenTelemetry trace that drove it. The id flows through three sources:

- **HTTP requests** — `LoggingBehavior<TRequest, TResponse>` reads `X-Correlation-Id` from the request (or generates `Guid.NewGuid().ToString()` if absent), calls `CorrelationContext.Set(id)` at request start, and clears it in a `try/finally`. Same scope that already carries the id in `BeginScope` (see Basket §0.3.4).
- **MassTransit bus consumers** — the 5 kitchen-driven consumers (`BasketCheckoutEventHandler`, `KitchenOrderAcceptedIntegrationEventHandler`, `KitchenOrderPrepStartedIntegrationEventHandler`, `KitchenOrderReadyIntegrationEventHandler`, `KitchenOrderCancelledIntegrationEventHandler`) call `CorrelationContext.Set(context.CorrelationId?.ToString() ?? Guid.NewGuid().ToString())` before invoking the aggregate method and clear it in a `try/finally`. MassTransit's `context.CorrelationId` is the bus-side equivalent of the HTTP header.
- **Out-of-band paths** — any path that doesn't set the ambient leaves `CorrelationId = null` on the activity row (acceptable v1 behaviour; documents the gap, doesn't fail).

**New BuildingBlocks primitives (drive-by Phase 1 contribution):**

- `BuildingBlocks/Correlation/CorrelationContext.cs`:
  ```csharp
  public static class CorrelationContext
  {
      private static readonly AsyncLocal<string?> _current = new();
      public static string? Current => _current.Value;
      internal static void Set(string id) => _current.Value = id;
      internal static void Clear() => _current.Value = null;
  }
  ```
  `internal` setters so only BuildingBlocks (and the `LoggingBehavior`) can write; the read accessor is `public` because the domain layer needs it.
- `BuildingBlocks/Behaviors/LoggingBehavior.cs` — at the start of `Handle`, calls `CorrelationContext.Set(id)`; in `try/finally`, calls `CorrelationContext.Clear()` regardless of handler outcome. The existing `BeginScope` block stays unchanged.
- **Scope discipline** — handlers that set the ambient for a bus-driven transition MUST clear it in `finally`, otherwise the `AsyncLocal` leaks into subsequent requests on the same logical call context (rare in MassTransit handlers but possible in HTTP handlers that invoke background continuations).

**Trade-off acknowledged:** every aggregate method now depends on `CorrelationContext.Current` returning a value (or `null`). v2 may thread the id explicitly through method signatures for testability; v1 accepts the ambient because the call sites are bounded and the existing `LoggingBehavior` already establishes the discipline.

---

## 1. Context

The Ordering service today exposes order detail via `GET /api/v1/orders/{id}` (`Ordering.API/Endpoints/GetOrderById.cs:13`) returning `OrderDto` (`Ordering.Application/Dtos/OrderDto.cs:3`). The DTO is rich — it carries the order, addresses, payment, line items, and a flat snapshot of lifecycle columns:

| Snapshot column | Source on `Order` |
|---|---|
| `CreatedByUserId`, `ApprovedByAdminId`, `ConfirmedByUserId`, `CompletedByUserId` | `Order.cs:41-48` |
| `ApprovedAt`, `CancelledAt`, `CompletedAt`, `ConfirmedAt`, `DeliveredAt`, `PreparingStartedAt`, `ReadyAt` | `Order.cs:32-47` |
| `DeliveryStatus` | `Order.cs:45` |
| `IsModified` | `Order.cs:14` |
| per-item `PrepStatus`, `PrepStartedAt`, `PrepCompletedAt` | `OrderItem.cs:33-49` |

What the snapshot **cannot** answer:

- "Who **edited** the order, and when?" — `Update` mutates the order but writes only `OrderUpdatedEvent` to the in-memory domain-event list (`Order.cs:104`); no timestamp / actor is persisted.
- "Did the customer request cancellation via the integration event or via the manual endpoint?" — both paths land in `Order.Cancel`; the snapshot just records `CancelledAt` + `CancelledByUserId`.
- "Was the `Confirmed` transition done by Maria at 14:02, or by the kitchen consumer at 14:05?" — same answer (`ConfirmedAt` + `ConfirmedByUserId`); no per-event row.
- "When did each **line item** start prep?" — the snapshot has `PrepStatus`, `PrepStartedAt`, `PrepCompletedAt` per item but the DTO surfaces them only on `OrderItemDto`; there is no chronological feed.
- "Free-text reason for confirmation / preparation / dispatch" — none. Cancellation has `CancellationReason`; nothing else does.

What does **not** exist:

- ❌ No `OrderActivity` / `OrderHistory` / `OrderAuditLog` entity, table, or DTO.
- ❌ No `processed_inbound_events` table — only Discount has one (`Discount.Grpc/Models/ProcessedInboundevent.cs`).
- ❌ `BuildingBlocks/Entities/Interfaces/IAuditableEntity.cs:9` exists with `CreatedBy` / `LastModifiedBy`, but `Order` does **not** implement it; only `CreatedByUserId` exists, and there is no `CreatedAt` column on the aggregate.
- ❌ Domain events (`Ordering.Domain/Events/*.cs`) live in memory on the aggregate (`Abstractions/Aggregate.cs:5`) and are dispatched through `DispatchDomainEventsInterceptor.cs:36` → MediatR → outbox, but they are **never persisted** to a history table — they vanish after dispatch.

The architecture (`docs/architecture/architecture.md`, `current-architecture.md §4.5`) treats Ordering as a CQRS aggregate whose read model is the same table it writes to. The activity trail plugs into that model as a child entity loaded by `Include(...)` in the read query.

---

## 2. Goal

Add a per-order **activity feed** to the existing `OrderDto` (and, optionally, a standalone paged endpoint in Phase 2):

1. **`OrderActivity` child entity** — appended on every state transition (create, update, confirm, mark-preparing, mark-ready, start-delivery, mark-delivered, complete, cancel) and every per-item prep transition (`OrderItem.MarkItemPreparing` / `MarkItemReady`).
2. **`order_activities` table + EF Core migration** — same SQL Server instance, FK on `OrderId`, index on `(OrderId, OccurredAt)`, cascade delete.
3. **DTO exposure** — `OrderDto.Activities : IReadOnlyList<OrderActivityDto>` ordered by `OccurredAt ASC`. `GetOrderByIdHandler` loads the activities with `Include(o => o.Activities)`.
4. **Atomicity** — every activity row is appended inside the same `SaveChangesAsync` transaction as the aggregate mutation; the existing `OrderingOutboxPublisher` interceptor already enforces that boundary, so the outbox row and the activity row commit or roll back together.
5. **Backward compatibility for pre-existing orders** — orders created before the Phase 1 migration simply have an empty `Activities` list. No backfill. Documented in §4.5 of `current-architecture.md`.
6. **Optional Phase 2 standalone endpoint** — `GET /api/v1/orders/{id}/activities?type=&from=&to=&page=&pageSize=` for callers that want a paged feed without re-fetching the full order. Deferred decision in Phase 2.

---

## 3. Out of scope (v1)

- **Event sourcing the `Order` aggregate** (option 3 from the user's question). Catalog and Discount both use plain aggregates with outbox-mediated integration events; switching Ordering to an event-sourced store (Marten `EventStoreDB`) is a separate, larger effort and changes the read model architecture. Rejected.
- **Persisting every `IDomainEvent` to a history table from `DispatchDomainEventsInterceptor`** (option 2 from the user's question). The interceptor's job is dispatch, not history. Persisting history there would couple the interceptor to schema decisions and would store events with weak typing (`IDomainEvent` base). Rejected in favour of a strongly-typed child entity.
- **Migration of existing domain events to activity rows** — there is no "event log" on disk to replay from; the in-memory domain-event list (`Abstractions/Aggregate.cs:5`) is per-transaction and discarded by `ClearDomainEvents()`. Activities begin accumulating from the moment the migration runs.
- **Bus-published `OrderActivityAppendedIntegrationEvent`** — the activity feed is a query-side concern; no other service needs it today. The existing `OrderCreatedEvent`, `OrderConfirmedEvent`, etc. integration events stay unchanged.
- **A separate Activity write store (e.g. Marten `Events` / append-only collection)** — the child-entity pattern lives in the same EF Core context as `Order`, which keeps the transaction boundary trivial.
- **Retention / archival** — v1 keeps activities forever (the child entity is deleted with the parent via cascade). A future retention sweep is a v2 phase.
- **Authoring/role attribution beyond the actor id** — `ActorUserId` is the only field. v1 does not record the actor's role or display name; Identity owns role data and the JWT carries it on the request, not on the persisted record.
- **Storing the activity feed in Catalog's Marten store** — Catalog owns three Marten documents (`OrderSnapshot`, `OrderModificationLog`, `OrderItemPriceAudit`; registered at `Catalog.API/Program.cs:132-134`). They are a **different** audit (point-in-time JSON snapshots + per-modification untyped JSON rows for receipt generation + manager-override approval flow), NOT the activity feed. Rejected as the storage location for `order_activities` because (a) Marten documents don't share a `SaveChangesAsync` transaction with the Ordering aggregate mutation — atomicity would be best-effort; (b) the Marten docs store untyped JSON (`PreviousData`, `NewData`, `FullOrderData`) instead of typed `OrderStatus` / `PrepStatus` / `DeliveryStatus` enums; (c) the Marten docs have no `CorrelationId` field; (d) the read path is `GET /api/v1/orders/{id}` on Ordering, and a second HTTP call to Catalog would duplicate load + risk stale reads. The two stores coexist: Catalog's Marten docs serve receipt / manager-override audit; Ordering's `order_activities` serves the chronological activity feed. Documented in §7 too.
- **Cross-service correlation id stored on each activity** is **IN scope** — `CorrelationId` is added in Phase 1 (per §0.6 + §6.1). The `X-Correlation-Id` header / MassTransit `ConsumeContext.CorrelationId` is threaded through `CorrelationContext.Current` and stamped on every activity row.

---

## 4. Service boundaries

### Ordering owns

- **`OrderActivity : Entity<OrderActivityId>`** (`Ordering.Domain/Models/OrderActivity.cs`, new) — child of `Order`.
- **`OrderActivityType` enum** (`Ordering.Domain/Enums/OrderActivityType.cs`, new) — `OrderCreated`, `OrderUpdated`, `OrderConfirmed`, `OrderPreparingStarted`, `OrderReady`, `OrderDeliveryStarted`, `OrderDelivered`, `OrderCompleted`, `OrderCancelled`, `OrderItemPrepStarted`, `OrderItemPrepCompleted`.
- **`order_activities` table** (`Ordering.Infrastructure/Data/Migrations/<timestamp>_AddOrderActivities.cs`, new) — FK to `Orders.Id`, cascade delete, index `(OrderId, OccurredAt)`.
- **`OrderActivityConfiguration`** (`Ordering.Infrastructure/Data/Configurations/OrderActivityConfiguration.cs`, new) — EF Core mapping.
- **`OrderActivityDto`** (`Ordering.Application/Dtos/OrderActivityDto.cs`, new).
- **`OrderDto.Activities : IReadOnlyList<OrderActivityDto>`** — appended to `Ordering.Application/Dtos/OrderDto.cs:58`.
- **`OrderExtensions.ToOrderDto`** — maps activities in `Ordering.Application/Extensions/OrderExtensions.cs:71`.
- **`GetOrderByIdHandler`** — adds `Include(o => o.Activities)` in `Ordering.Application/Orders/Queries/GetOrderById/GetOrderByIdHandler.cs:9`.

### Ordering does NOT own

- **Cross-service activity consumption** — no other service reads the feed.
- **Activity retention sweep** — the activity child is removed with the parent on cascade delete; no separate retention job.
- **Display-name resolution for `ActorUserId`** — Identity owns user identity; the feed stores the id only.

---

## 5. Tech decisions

| Decision | Choice | Reason |
|---|---|---|
| Architecture | Vertical slice, child entity on `Order` aggregate | Option 1 from the user's question; minimal blast radius; same EF Core shape as `OrderItem` and `OrderBill`. |
| Persistence | EF Core (SqlServer) child entity, table `order_activities` | Matches the rest of Ordering; cascades with the parent on order delete. |
| Activity identity | `OrderActivityId` value object (strongly-typed Guid wrapper) | Mirrors `OrderItemId` / `MenuItemId` / `OrderId` conventions. |
| ActivityType | `enum` stored as `nvarchar(50)` via existing `HasConversion<string>()` pattern | Mirrors `OrderStatus` (`OrderConfiguration.cs:58`). |
| Metadata | `Metadata` snapshot object stored as `nvarchar(max)` jsonb column (typed `OrderActivityMetadata` record with `Reason`, `OrderItemId`, etc.) | Mirrors `OrderItem.Customizations` jsonb pattern (`OrderItemConfiguration`'s `System.Text.Json`-backed value converter). |
| Atomicity | EF Core transaction boundary — activities appended inside `SaveChangesAsync` alongside the aggregate mutation, in the same scope as `outbox_messages` | Already enforced by `OrderingOutboxPublisher` interceptor + `DispatchDomainEventsInterceptor`. |
| Index | `(OrderId, OccurredAt)` non-clustered index | The only query pattern is "activities for one order, ordered by time" — covered index avoids a sort. |
| Ordering of `Activities` in DTO | `OccurredAt ASC, Id ASC` (deterministic tie-breaker on Guid) | Stable order across requests; the Id ASC tie-breaker protects against same-instant rows. |
| DTO contract | `OrderActivityDto` record (`Id`, `ActivityType`, `ActorUserId`, `OccurredAt`, `Notes?`, `Metadata?`) | Read-only; mirrors the entity minus audit fields. |
| API exposure | Embedded in `OrderDto` (Phase 1); standalone paged endpoint deferred to Phase 2 | Phase 1 closes the user's "see the detail along with the activity" question. Phase 2 covers paged feeds. |
| Backward compatibility | Pre-migration orders return `Activities: []` | No backfill — there is no historical event log on disk to replay. |
| Tests | xUnit + FluentAssertions + Moq (matches `Ordering.Application.Tests`, `Ordering.Domain.Tests`) | Project standard. Phase 1 adds domain unit tests + handler tests; no Testcontainers (the existing `GetOrderByIdHandler` test pattern uses an in-memory `IApplicationDbContext` substitute). |

> **Skill mandate:** all implementation invokes `/csharp-developer` and follows the `dotnet-best-practices` + `api-design-principles` guard rails, same as Catalog and Basket.

---

## 6. Phased milestones

### Phase 1 — Embedded activity feed (FOUNDATION) ✅ implemented 2026-07-16

Two commits: **(1) Ordering.Domain + Ordering.Application**, **(2) Ordering.Infrastructure (configuration + migration) + Ordering.API.Tests**.

**Ordering.Domain commit:**

- New `Ordering.Domain/Enums/OrderActivityType.cs`:
  ```csharp
  namespace Ordering.Domain.Enums;
  public enum OrderActivityType
  {
      OrderCreated = 0,
      OrderUpdated = 1,
      OrderConfirmed = 2,
      OrderPreparingStarted = 3,
      OrderReady = 4,
      OrderDeliveryStarted = 5,
      OrderDelivered = 6,
      OrderCompleted = 7,
      OrderCancelled = 8,
      OrderItemPrepStarted = 9,
      OrderItemPrepCompleted = 10,
  }
  ```
- New `Ordering.Domain/Models/OrderActivity.cs` — `OrderActivity : Entity<OrderActivityId>` with **all properties `private set`** (audit immutability — see §0.4). Private parameterless constructor for EF Core + `public static OrderActivity Create(...)` factory that enforces null / length / unknown-enum invariants via the new `OrderActivityInvariantException`. The factory is the only entry point; `Order.RecordActivity` is the only caller.

  ```csharp
  public class OrderActivity : Entity<OrderActivityId>
  {
      public OrderId OrderId { get; private set; } = default!;
      public OrderActivityType ActivityType { get; private set; }
      public Guid? ActorUserId { get; private set; }
      public Instant OccurredAt { get; private set; }
      public string? CorrelationId { get; private set; }
      public string? Notes { get; private set; }
      public OrderActivityMetadata? Metadata { get; private set; }

      private OrderActivity() { }   // EF Core

      public static OrderActivity Create(
          OrderId orderId, OrderActivityType activityType,
          Guid? actorUserId, Instant occurredAt,
          string? correlationId = null,
          string? notes = null, OrderActivityMetadata? metadata = null)
      {
          ArgumentNullException.ThrowIfNull(orderId);
          if (!Enum.IsDefined(typeof(OrderActivityType), activityType))
              throw new OrderActivityInvariantException($"Unknown activity type: {activityType}.");
          if (correlationId is { Length: > 100 })
              throw new OrderActivityInvariantException("CorrelationId must be ≤100 chars.");
          if (notes is { Length: > 2000 })
              throw new OrderActivityInvariantException("Notes must be ≤2000 chars.");

          return new OrderActivity
          {
              Id = OrderActivityId.Of(Guid.NewGuid()),
              OrderId = orderId,
              ActivityType = activityType,
              ActorUserId = actorUserId,
              OccurredAt = occurredAt,
              CorrelationId = correlationId,
              Notes = notes,
              Metadata = metadata,
          };
      }
  }
  ```
- New `Ordering.Domain/Models/OrderActivityMetadata.cs` — typed record `OrderActivityMetadata(string? Reason, Guid? OrderItemId, string? OrderItemName, OrderStatus? PreviousOrderStatus, OrderStatus? NewOrderStatus, PrepStatus? PreviousPrepStatus, PrepStatus? NewPrepStatus, DeliveryStatus? PreviousDeliveryStatus, DeliveryStatus? NewDeliveryStatus)`. Each pair is populated **only on the matching transition type** (see the §6.1 transition callout table below); all other fields are `null`. Serialised as `nvarchar(max)` via `System.Text.Json` (mirrors `OrderItem.Customizations` pattern). `JsonStringEnumConverter` registered in `OrderActivityJson.Options` (Phase 1 §6.1 Infrastructure) so enum values are string-serialised (`"Confirmed"`, not `2`) for human readability in the jsonb column AND in the JSON response.
- New `Ordering.Domain/ValueObjects/OrderActivityId.cs` — strongly-typed Guid wrapper, mirrors `OrderItemId`.
- New `Ordering.Domain/Exceptions/OrderActivityInvariantException.cs` — thrown if `Order.RecordActivity` is called from an unexpected context (e.g. after `Order` is in a terminal state with no activities), or from the `OrderActivity.Create` factory on length / unknown-enum violations (see the H-1 code block above).
- **Register the new exception in `BuildingBlocks/Exceptions/Handler/CustomExceptionHandler.cs:17-48`** with arm → **422 Unprocessable Content** (the activity is malformed; the aggregate state is unchanged; the request must be re-shaped). Match the `BasketValidationException` 422 mapping in `BASKET_SERVICE_PLAN.md §0.4.6`. Without this arm the exception bubbles up as 500. A regression test (in `Ordering.API.Tests`) calls an internal helper to invoke `RecordActivity` with `OrderActivityType.MaxValue + 1` and asserts the response is `422 + ProblemDetails.type == "https://..."`.
- Modify `Ordering.Domain/Models/Order.cs`:
  - Add `private readonly List<OrderActivity> _activities = [];` next to `_orderItems` (line 51).
  - Add `public IReadOnlyCollection<OrderActivity> Activities => _activities.AsReadOnly();`.
  - Add `public void RecordActivity(OrderActivityType type, Guid? actorUserId, Instant occurredAt, string? notes = null, OrderActivityMetadata? metadata = null)` — pushes a new `OrderActivity` to `_activities` via the factory. Reads the request-scoped `CorrelationContext.Current` (BuildingBlocks §0.6) and forwards it to the factory as the activity's `CorrelationId`. Throws on unknown enum value via `OrderActivityInvariantException`.
  - Call `RecordActivity` from every state transition method:
    - `Create` → `OrderActivityType.OrderCreated` (no actor — caller is the basket consumer; no status transition since the order starts at `Pending`; metadata is `null`).
    - `Update(billingAddress, deliveryAddress, payment)` → `OrderActivityType.OrderUpdated` (no actor id today; no status transition; metadata is `null`).
    - `Confirm(confirmedByUserId, now)` → `OrderActivityType.OrderConfirmed` (actor = `confirmedByUserId`; metadata: `PreviousOrderStatus = Pending`, `NewOrderStatus = Confirmed`).
    - `MarkPreparing(now)` → `OrderActivityType.OrderPreparingStarted` (actor = `null`; metadata: `PreviousOrderStatus = Confirmed`, `NewOrderStatus = Preparing`).
    - `MarkReady(now)` → `OrderActivityType.OrderReady` (actor = `null`; metadata: `PreviousOrderStatus = Preparing`, `NewOrderStatus = Ready`).
    - `StartDelivery()` → `OrderActivityType.OrderDeliveryStarted` (actor = `null`; metadata: `PreviousDeliveryStatus = null`, `NewDeliveryStatus = Dispatched`).
    - `MarkDelivered(now)` → `OrderActivityType.OrderDelivered` (actor = `null`; metadata: `PreviousOrderStatus = Ready`, `NewOrderStatus = Delivered`, `PreviousDeliveryStatus = Dispatched`, `NewDeliveryStatus = Delivered`).
    - `Complete(now)` → `OrderActivityType.OrderCompleted` (actor = `null`; metadata: `PreviousOrderStatus = Delivered`, `NewOrderStatus = Completed`).
    - `Cancel(reason, cancelledByUserId, now)` → `OrderActivityType.OrderCancelled` (actor = `cancelledByUserId`, `notes = reason`; metadata: `PreviousOrderStatus = Status` (the value captured before the transition), `NewOrderStatus = Cancelled`, `Reason = reason`).
- Modify `Ordering.Domain/Models/OrderItem.cs`:
  - `MarkItemPreparing(now)` → calls back to `Order.RecordActivity(OrderActivityType.OrderItemPrepStarted, actorUserId: null, occurredAt: now, metadata: new OrderActivityMetadata(Reason: null, OrderItemId: Id.Value, OrderItemName: MenuItemName, PreviousPrepStatus: PrepStatus.Pending, NewPrepStatus: PrepStatus.Preparing))`. The `OrderItem` needs a back-reference to the `Order` aggregate to call `RecordActivity`; add `internal Order Parent { get; set; } = default!;` and let `Order.Add(...)` set it (`orderItem.Parent = this;` after construction). Documented in the `OrderItem` XML doc.
  - `MarkItemReady(now)` → same shape, `OrderActivityType.OrderItemPrepCompleted`, with `PreviousPrepStatus: PrepStatus.Preparing`, `NewPrepStatus: PrepStatus.Ready`.
- Tests (Domain):
  - `OrderActivityTests.RecordActivity_AppendsToActivities`.
  - `OrderActivityTests.RecordActivity_Throws_OnUnknownActivityType` (defensive — the enum is closed today but the test locks the contract).
  - `OrderActivityTests.Create_Throws_WhenNotesExceeds2000Chars`.
  - `OrderActivityTests.Create_Throws_WhenCorrelationIdExceeds100Chars`.
  - `OrderActivityTests.Create_StampsCorrelationId_WhenAmbientSet` (sets `CorrelationContext.Set("test-corr")`, asserts `activity.CorrelationId == "test-corr"`).
  - `OrderActivityTests.Create_LeavesCorrelationIdNull_WhenAmbientUnset` (default state — no ambient set).
  - `OrderTests.Create_AppendsOrderCreatedActivity_NoMetadata` (asserts metadata is `null`).
  - `OrderTests.Update_AppendsOrderUpdatedActivity_NoMetadata`.
  - `OrderTests.Confirm_AppendsOrderConfirmedActivity_WithActor_AndStatusMetadata` (asserts `PreviousOrderStatus = Pending`, `NewOrderStatus = Confirmed`).
  - `OrderTests.MarkPreparing_AppendsOrderPreparingStartedActivity_WithStatusMetadata`.
  - `OrderTests.MarkReady_AppendsOrderReadyActivity_WithStatusMetadata`.
  - `OrderTests.StartDelivery_AppendsOrderDeliveryStartedActivity_WithDeliveryStatusMetadata` (asserts `NewDeliveryStatus = Dispatched`, `OrderStatus` unchanged in metadata).
  - `OrderTests.MarkDelivered_AppendsOrderDeliveredActivity_WithStatusAndDeliveryStatusMetadata`.
  - `OrderTests.Complete_AppendsOrderCompletedActivity_WithStatusMetadata`.
  - `OrderTests.Cancel_AppendsOrderCancelledActivity_WithReasonAsNotes_AndStatusMetadata`.
  - `OrderItemTests.MarkItemPreparing_AppendsOrderItemPrepStartedActivity_WithMenuItemName_AndPrepStatusMetadata` (asserts `PreviousPrepStatus = Pending`, `NewPrepStatus = Preparing`, `OrderItemId`/`OrderItemName` populated).
  - `OrderItemTests.MarkItemReady_AppendsOrderItemPrepCompletedActivity_WithPrepStatusMetadata`.

**Ordering.Application commit:**

- New `Ordering.Application/Dtos/OrderActivityDto.cs` — `record OrderActivityDto(Guid Id, OrderActivityType ActivityType, Guid? ActorUserId, Instant OccurredAt, string? CorrelationId, string? Notes, OrderActivityMetadata? Metadata)`. `CorrelationId` is the request-scoped id stamped by `LoggingBehavior` (HTTP) or by the MassTransit `ConsumeContext.CorrelationId` (bus consumers); `null` when the transition is driven outside a request/bus scope.
- Modify `Ordering.Application/Dtos/OrderDto.cs` — add `IReadOnlyList<OrderActivityDto> Activities` to the record (line 58, after `OrderItems`).
- Modify `Ordering.Application/Extensions/OrderExtensions.cs` — extend `ToOrderDto` (line 12) to map activities ordered by `OccurredAt ASC, Id ASC`:
  ```csharp
  Activities: [.. order.Activities
      .OrderBy(a => a.OccurredAt)
      .ThenBy(a => a.Id.Value)
      .Select(a => new OrderActivityDto(
          Id: a.Id.Value,
          ActivityType: a.ActivityType,
          ActorUserId: a.ActorUserId,
          OccurredAt: a.OccurredAt,
          CorrelationId: a.CorrelationId,
          Notes: a.Notes,
          Metadata: a.Metadata))],
  ```
  Add `using Ordering.Domain.Enums;` to the file.
- Modify `Ordering.Application/Orders/Queries/GetOrderById/GetOrderByIdHandler.cs:9` — change `.Include(o => o.OrderItems)` to `.Include(o => o.OrderItems).Include(o => o.Activities)`. `AsNoTracking()` stays.
- Modify `Ordering.Application/Orders/Queries/GetOrders/GetOrdersHandler.cs:13` — same `Include` chain added so the paged list also returns activities. The handler returns `OrderDto`s, so the new field flows through automatically; the only change is the `Include` line. (Alternative: keep the list endpoint activity-free for payload-size reasons; see §6.2 Phase 2 decision — Phase 1 keeps it consistent with `GetOrderById` and the cost is negligible.)
- Modify `Ordering.Application/Orders/Queries/GetOrdersByCustomer/GetOrdersByCustomerHandler.cs` — same `Include` chain.
- Tests (Application):
  - `GetOrderByIdHandlerTests.Handler_ReturnsActivitiesOrderedByOccurredAt` (asserts the DTO ordering).
  - `GetOrderByIdHandlerTests.Handler_NoActivities_ReturnsEmptyList`.
  - `GetOrderByIdHandlerTests.Handler_PreMigrationOrder_HasEmptyActivities` (synthetic test — not gated on a real DB).
  - `GetOrderByIdHandlerTests.Handler_MapsCorrelationId_FromEntityToDto`.
  - `OrderExtensionsTests.ToOrderDto_MapsActivitiesInChronologicalOrder`.
  - `OrderExtensionsTests.ToOrderDto_MapsCorrelationId_WhenSet`.
  - `OrderExtensionsTests.ToOrderDto_MapsMetadataStatusEnumsAsStrings` (asserts `"Confirmed"`, not `2`, in the metadata jsonb column snapshot).

**Ordering.Infrastructure commit:**

- New `Ordering.Infrastructure/Data/Configurations/OrderActivityConfiguration.cs` — EF Core mapping using a **shared cached `JsonSerializerOptions` instance** (see H-3 below) to avoid per-call allocation + guarantee config consistency:
  ```csharp
  builder.HasKey(a => a.Id);
  builder.Property(a => a.Id).HasConversion(id => id.Value, v => OrderActivityId.Of(v));
  builder.Property(a => a.OrderId).HasConversion(id => id.Value, v => OrderId.Of(v)).IsRequired();
  builder.Property(a => a.ActivityType).HasConversion<string>().HasMaxLength(50).IsRequired();
  builder.Property(a => a.OccurredAt).IsRequired();
  builder.Property(a => a.CorrelationId).HasMaxLength(100);
  builder.Property(a => a.Notes).HasMaxLength(2000);
  builder.Property(a => a.Metadata).HasColumnType("nvarchar(max)").HasConversion(
      m => m == null ? null : JsonSerializer.Serialize(m, OrderActivityJson.Options),
      v => v == null ? null : JsonSerializer.Deserialize<OrderActivityMetadata>(v, OrderActivityJson.Options));
  builder.HasIndex(a => new { a.OrderId, a.OccurredAt }).HasDatabaseName("IX_order_activities_OrderId_OccurredAt");
  ```
- **H-3 — `Ordering.Infrastructure/Serialization/OrderActivityJson.cs`** (new file, internal static class):
  ```csharp
  internal static class OrderActivityJson
  {
      public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
      {
          PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
          Converters =
          {
              new JsonStringEnumConverter(),   // enum values as strings: "Confirmed", not 2
              /* NodaTime InstantConverter added when Metadata gains Instant fields */
          },
      };
  }
  ```
  `JsonStringEnumConverter` is mandatory: `OrderActivityMetadata` carries nullable `OrderStatus` / `PrepStatus` / `DeliveryStatus` enums, and string-serialised values are human-readable in the jsonb column AND in the JSON response. One static field allocated once per process. Mirrors the Basket §0.3.3 footnote that flagged the per-call `JsonSerializerOptions` allocation as a latent bug class.
- Modify `Ordering.Infrastructure/Data/Configurations/OrderConfiguration.cs:14` — extend the `HasMany` chain so the `Activities` navigation is wired:
  ```csharp
  builder.HasMany(o => o.Activities)
      .WithOne()
      .HasForeignKey(a => a.OrderId)
      .OnDelete(DeleteBehavior.Cascade);
  ```
- New migration `Ordering.Infrastructure/Data/Migrations/<timestamp>_AddOrderActivities.cs`:
  ```csharp
  migrationBuilder.CreateTable(
      name: "order_activities",
      columns: table => new
      {
          Id = table.Column<Guid>(nullable: false),
          OrderId = table.Column<Guid>(nullable: false),
          ActivityType = table.Column<string>(maxLength: 50, nullable: false),
          ActorUserId = table.Column<Guid>(nullable: true),
          OccurredAt = table.Column<DateTime>(nullable: false),
          CorrelationId = table.Column<string>(maxLength: 100, nullable: true),
          Notes = table.Column<string>(maxLength: 2000, nullable: true),
          Metadata = table.Column<string>(nullable: true)
      },
      constraints: table =>
      {
          table.PrimaryKey("PK_order_activities", x => x.Id);
          table.ForeignKey("FK_order_activities_orders_OrderId", x => x.OrderId, "Orders", "Id", onDelete: ReferentialAction.Cascade);
      });
  migrationBuilder.CreateIndex("IX_order_activities_OrderId_OccurredAt", "order_activities", new[] { "OrderId", "OccurredAt" });
  ```
  No backfill — pre-existing orders have `Activities: []` until they receive their first state transition post-deploy. `CorrelationId` is nullable because not every transition is driven by a request or a bus message.
- `ApplicationDBContext` is **unchanged** — `OrderActivity` is loaded via the `Order.Activities` navigation; no new `DbSet` is added (per the §0.3 guard rail "child entity, not aggregate root").
- Tests (Infrastructure):
  - `OrderActivityConfigurationTests.ActivityType_StoredAsString` (round-trip test using `ApplicationDBContext` against a Testcontainers SQL Server fixture, mirrors the Kitchen migration test pattern). **Note:** if the existing `Ordering.Infrastructure.Tests` project does not yet have a SQL Server Testcontainers fixture, this test is deferred to Phase 2 (the configuration compiles + the migration applies on dev startup, which is enough confidence for v1).

**Doc-update scope for Phase 1:**

- `docs/architecture/current-architecture.md` §4.5:
  - Add a paragraph under "Aggregate behaviour" documenting that every state-transition method appends one `OrderActivity` row in the same transaction as the aggregate mutation (mirrors the `outbox_messages` atomicity rule). The activity row carries `CorrelationId` (stamped from `CorrelationContext.Current`), `ActorUserId` (where the source method takes one), and `Metadata` (typed status-transition pairs — `OrderStatus` / `PrepStatus` / `DeliveryStatus` prev/new — populated per transition type).
  - Update the `OrderDto` description to note `Activities : IReadOnlyList<OrderActivityDto>`, ordered by `OccurredAt ASC, Id ASC`, each carrying `CorrelationId` for log-trace correlation.
  - Update "Endpoint surface" row for `GET /api/v1/orders/{id}` to mention the activity feed is part of the response payload.
- `docs/architecture/current-architecture.md` §6 (Data Stores) — add a row for `order_activities` (SQL Server, EF Core child of `Orders.Id`, cascade delete, index `(OrderId, OccurredAt)`, columns include `CorrelationId nvarchar(100) NULL`).
- `docs/architecture/current-architecture.md` §9 (Cross-Cutting Patterns) — short note under "Transactional outbox" cross-referencing that `order_activities` shares the same transaction boundary. Document `BuildingBlocks/Correlation/CorrelationContext` as the ambient correlation-id source for HTTP requests (`LoggingBehavior`) and MassTransit consumers (`ConsumeContext.CorrelationId`).

### Phase 2 — Standalone paged activity endpoint (DEFERRED, OPTIONAL)

> **Status:** not started; this phase exists so the user can see the deferred endpoint shape without needing to re-open the plan. Phase 2 is approved if and only if a downstream caller asks for paged access or if the `OrderDto` payload becomes large enough to warrant splitting. The Phase 1 implementation does not require Phase 2 to land.

If Phase 2 is approved:

- New `Ordering.Application/Orders/Queries/GetOrderActivities/GetOrderActivitiesQuery.cs` — `(Guid OrderId, OrderActivityType? Type, Instant? From, Instant? To, PaginationRequest Pagination)`.
- New `Ordering.Application/Orders/Queries/GetOrderActivities/GetOrderActivitiesHandler.cs` — loads the order, asserts exists (`OrderNotFoundException` on miss), filters activities by `Type` / `From` / `To`, applies `Skip/Take`, returns `PaginatedResult<OrderActivityDto>`.
- New `Ordering.API/Endpoints/GetOrderActivities.cs` — `GET /api/v1/orders/{id}/activities?type=&from=&to=&page=&pageSize=`. `RequirePermission("orders:view_*")` (same permission as `GET /orders/{id}`).
- Tests:
  - `GetOrderActivitiesHandlerTests.Handler_FiltersByType`.
  - `GetOrderActivitiesHandlerTests.Handler_FiltersByDateRange`.
  - `GetOrderActivitiesHandlerTests.Handler_Paginates`.
  - `GetOrderActivitiesHandlerTests.Handler_UnknownOrder_ThrowsOrderNotFoundException`.
  - `GetOrderActivitiesEndpointTests.WithoutOrdersViewPermission_Returns403`.

**Doc-update scope for Phase 2:** `current-architecture.md` §4.5 Endpoint surface table gains one row for `GET /api/v1/orders/{id}/activities`.

---

## 7. Cross-service notes

- **Identity** — no change. The activity feed stores `ActorUserId` (a Guid). If a downstream caller needs display-name resolution, it goes through Identity (`GET /api/v1/users/{id}` or the JWT's `name` claim). No Identity plan item is raised.
- **Catalog** — **two distinct stores coexist; no cross-service read path.**
  - Catalog owns **three Marten documents** for order-related audit (`OrderSnapshot`, `OrderModificationLog`, `OrderItemPriceAudit`, registered at `Catalog.API/Program.cs:132-134`). These are **independent** of the Ordering activity feed:
    - Different storage tech (Marten / Postgres vs. EF Core / SQL Server).
    - Different write trigger (Catalog's own surface — receipt generation, manager overrides — NOT Ordering's `Order.*` state transitions).
    - Different consumer (Catalog reads them for receipt HTML + manager-override approval; Ordering never reads them).
    - Different shape (untyped `PreviousData` / `NewData` JSON blobs vs. typed `OrderActivity` with `OrderStatus` / `PrepStatus` / `DeliveryStatus` prev/new enum pairs).
  - Catalog also owns the `EntityHistoryArchive` Marten doc for **Discount's** history (per `DISCOUNT_SERVICE_PLAN.md §6.6`) — yet another independent concern, unrelated to this plan.
  - **No `Include` / HTTP call / bus subscription** between Ordering's `order_activities` and Catalog's Marten docs. The two stores must not be merged in a future refactor without re-litigating the atomicity + type-strength + correlation + read-path reasons in §3.
- **Kitchen** — no change. The kitchen already has its own per-ticket status transitions (`Kitchen.API/KITCHEN_SERVICE_PLAN.md`); it does not consume the Ordering activity feed.
- **Basket** — no change. Basket publishes `BasketCheckoutEvent`; Ordering's `Create` activity row is appended when `BasketCheckoutEventHandler` invokes `Order.Create` (Phase 1 wires the call).
- **Notification v1** — no change. If Notification v1 ever needs to react to specific order transitions, it consumes the existing integration events (`OrderConfirmedEvent`, `OrderReadyEvent`, etc.), not the activity feed.
- **Discount** — no change. The `processed_inbound_events` table in Discount is its own dedup mechanism; unrelated to Ordering's activity feed.

---

## 8. Milestone checklist

- [x] **Phase 1 — BuildingBlocks** — `Correlation/CorrelationContext.cs` new (ambient `AsyncLocal<string?>`, internal setters, public `Current`). `Behaviors/LoggingBehavior.cs` gains `CorrelationContext.Set(id)` at request start and `CorrelationContext.Clear()` in `try/finally`. No new DI registrations; no new DI lifetime. Tests for `LoggingBehavior.Handle_HeaderPresent_SetsAmbientToHeaderValue` + `Handle_HeaderMissing_GeneratesGuid` + `Handle_HandlerThrows_ClearsAmbient` (plus `Handle_HeaderWhitespace_GeneratesGuid` + `Handle_NoHttpContext_LeavesAmbientUntouched`) in `Ordering.Application.Tests/Behaviors/LoggingBehaviorTests.cs`.
- [x] **Phase 1 — Ordering.Domain** — `OrderActivityType` enum, `OrderActivity` entity (with `CorrelationId` field + `private set` immutability), `OrderActivityMetadata` typed record (`Reason` + `OrderItemId`/`OrderItemName` + `OrderStatus`/`PrepStatus`/`DeliveryStatus` prev/new enum pairs), `OrderActivityId` value object, `OrderActivityInvariantException` (registered in `CustomExceptionHandler` → 422). `Order.RecordActivity` reads `CorrelationContext.Current` and forwards to the factory. Calls from every state-transition method pass the matching typed metadata. `OrderItem.Parent` back-reference; `MarkItemPreparing` / `MarkItemReady` append child activities with `PrepStatus` prev/new pairs. Domain unit tests: `OrderActivityTests` (8 — factory invariants, correlation ambient, theory enum coverage) + `OrderActivityTransitionTests` (8 — one per transition method, asserts status metadata + actor) + 2 added to `OrderItemTests` for per-item prep — 18 new domain tests, 153 total pass.
- [x] **Phase 1 — MassTransit consumers** — the 5 kitchen-driven consumers (`BasketCheckoutEventHandler`, `KitchenOrderAcceptedIntegrationEventHandler`, `KitchenOrderPrepStartedIntegrationEventHandler`, `KitchenOrderReadyIntegrationEventHandler`, `KitchenOrderCancelledIntegrationEventHandler`) gain `CorrelationContext.Set(context.CorrelationId?.ToString() ?? Guid.NewGuid().ToString())` before the aggregate call and `Clear()` in `try/finally`. Test in `KitchenOrderAcceptedIntegrationEventHandlerTests.Consume_StampsCorrelationIdFromBusContext_OnActivityRow` asserts `OrderActivity.CorrelationId == context.CorrelationId.ToString()`; the same shape can be folded into the four other consumer tests on demand (4 × 10 lines; not blocking).
- [x] **Phase 1 — Ordering.Application** — `OrderActivityDto`. `OrderDto.Activities`. `OrderExtensions.ToOrderDto` mapping (ordered `OccurredAt ASC, Id ASC`). `Include(o => o.Activities)` in `GetOrderByIdHandler`, `GetOrdersHandler`, `GetOrdersByCustomerHandler`. `CreateOrderHandler` appends the `OrderCreated` activity (the factory cannot call `RecordActivity` itself). `BasketCheckoutEventHandler` adds `Activities: []` to the projected DTO. Application unit tests: `OrderExtensionsActivityTests` (7 — chronological order, empty list, correlation propagation, metadata status enums as strings, cancellation reason passthrough, per-item prep, multi-activity ordering); 41 application tests pass.
- [x] **Phase 1 — Ordering.Infrastructure** — `OrderActivityConfiguration` (EF Core mapping, jsonb `Metadata` via cached `OrderActivityJson.Options` with `JsonStringEnumConverter`, `HasConversion<string>().HasMaxLength(50)` on `ActivityType`, covering index `(OrderId, OccurredAt)` → `IX_order_activities_OrderId_OccurredAt`). `OrderConfiguration.HasMany(o => o.Activities).WithOne().HasForeignKey(a => a.OrderId).OnDelete(DeleteBehavior.Cascade)`. New migration `20260716001148_AddOrderActivities.cs` (table + index + FK cascade, no backfill). `ApplicationDBContext` unchanged (no new `DbSet`). `Serialization/OrderActivityJson.cs` new file holds the shared `JsonSerializerOptions` (Basket §0.3.3 latent-bug fix).
- [x] **Phase 1 — Docs** — `current-architecture.md` §4.5 (aggregate behaviour + DTO + endpoint surface), §6 (data stores row), §9 (cross-cutting note for `CorrelationContext`).
- [x] **Phase 1 — Acceptance** — `dotnet test` runs clean across `Ordering.Domain.Tests` (153/153) and `Ordering.Application.Tests` (41/41). `Ordering.API.Tests` (28 integration tests) require SQL Server / Testcontainers — same pre-existing failures on `main`, not caused by this plan. Migration file generated, compiles, and **applied on dev DB** via `dotnet ef database update --project Ordering.Infrastructure --startup-project Ordering.API` against the local `orderdb` container (port 1433, sa / `YrPsswrd123456789`). Post-apply verification on `OrderDb.dbo` confirms the `__EFMigrationsHistory` row `20260716001148_AddOrderActivities`, the `order_activities` table (12 columns: 8 spec + 4 inherited from `AuditableEntity`; all types/lengths/nullability match §6), the `PK_order_activities` clustered + `IX_order_activities_OrderId_OccurredAt` nonclustered indexes, and the `FK_order_activities_Orders_OrderId` cascade FK. Pre-existing orders will still return `Activities: []`; a freshly-confirmed order against this DB will now append 4 activity rows in chronological order, each stamped with `CorrelationId` from `CorrelationContext`.
- [x] **Phase 2 — Standalone paged activities endpoint** — approved 2026-07-16 (per the "Both, in sequence" approval alongside the cleanup backlog) and shipped same-day. New `Ordering.Application/Orders/Queries/GetOrderActivities/` folder: `GetOrderActivitiesQuery` (record carrying `OrderId`, `OrderActivityType?` filter, `Instant?` `From`/`To`, `PaginationRequest`) + `GetOrderActivitiesHandler` (loads order via `Include(o => o.Activities)` + `AsNoTracking()`, asserts exists via `OrderNotFoundException`, filters + paginates, returns `PaginatedResult<OrderActivityDto>` ordered by `OccurredAt ASC, Id ASC`). New Carter endpoint `Ordering.API/Endpoints/GetOrderActivities.cs` at `GET /api/v1/orders/{id:guid}/activities?type=&from=&to=&page=&pageSize=`, parses ISO-8601 `Instant` strings via `InstantPattern.ExtendedIso`, produces `200/400/404`, requires `orders:view_own` permission. New integration test `GetOrderActivitiesEndpointTests.WithoutOrdersViewPermission_Returns403` (1/1 passes end-to-end against Testcontainers). Doc updated: `docs/architecture/current-architecture.md` §4.5 Endpoint surface table carries one new row for the standalone activities endpoint.

---

## 9. References

- `BASKET_SERVICE_PLAN.md` — §0 conventions (skill mandate, doc-update, code-quality guard rails); §6 phased-milestone structure.
- `CATALOG_SERVICE_PLAN.md` — §0.3 code-quality guard rails (inherited verbatim, layered with project-specific overrides); §6.5 event versioning (not directly applicable, but the "no event sourcing" decision in §3 cites the same architectural choice).
- `KITCHEN_FOLLOWUP_PLAN.md` — small, focused post-M5 follow-up plan structure; closest template for this plan's scope and tone.
- `DISCOUNT_SERVICE_PLAN.md` — §0.4 design decisions (multi-tenancy adoption, idempotency envelope); `processed_inbound_events` table pattern (Discount-only; not reused here).
- `Ordering.Domain/Models/Order.cs` — aggregate; every state-transition method gains a `RecordActivity` call in Phase 1.
- `Ordering.Domain/Models/OrderItem.cs:56-77` — per-item prep transitions; gain `Parent` back-reference + activity appends in Phase 1.
- `Ordering.Domain/Abstractions/Aggregate.cs:5` — in-memory `DomainEvents` list (NOT persisted; per §3 rejection of option 2).
- `Ordering.Infrastructure/Data/Configurations/OrderConfiguration.cs:14` — existing `HasMany(o => o.OrderItems).OnDelete(DeleteBehavior.Cascade)`; the `Activities` mapping mirrors this.
- `Ordering.Infrastructure/Data/Interceptors/DispatchDomainEventsInterceptor.cs:36` — the existing interceptor that dispatches `IDomainEvent`s; activities are NOT dispatched here (they're a child entity, not a domain event).
- `Ordering.Infrastructure/Data/Interceptors/OrderingOutboxDispatcher.cs` — existing outbox dispatcher; not modified by this plan (the activity row commits in the same `SaveChangesAsync` transaction as the outbox row already does).
- `BuildingBlocks/Correlation/CorrelationContext.cs` — **new in this plan**; ambient `AsyncLocal<string?>` correlation-id source. Internal setters (`Set`/`Clear`); public `Current` getter.
- `BuildingBlocks/Behaviors/LoggingBehavior.cs` — existing; gains `CorrelationContext.Set(id)` at request start and `Clear()` in `try/finally` in this plan. Existing `BeginScope` block stays unchanged.
- `Catalog.API/Models/OrderSnapshot.cs` / `OrderModificationLog.cs` / `OrderItemPriceAudit.cs` — Catalog's Marten documents; **independent** of the Ordering activity feed (different storage, different write trigger, different consumer, different shape). See §3 and §7.
- `.agents/plan/ordering/ORDERING_CLEANUP_BACKLOG.md` — follow-up plan capturing the 3 P1 items surfaced by the 2026-07-15 mermaid drift review (8 missing `Orders` snapshot columns + misleading audit-fields comment + relational jsonb sub-shape convention gap). **Cleanup completed 2026-07-16** (Phases A + B + C all shipped, v1.0 of the cleanup backlog); see the `v1.2` changelog entry below and the cleanup backlog's `v1.0` changelog. The activity-feed plan was **safe to implement** against the pre-cleanup mermaid; this cleanup pass brought the diagram back into sync with the code. Future diagram drift reviews reference the cleanup backlog + the closed P1-1 / P1-2 / C.1 items.
- `docs/architecture/current-architecture.md` §4.5 — current Ordering snapshot; updated at every phase.
- `docs/architecture/architecture.md` §3 — contract for the Ordering service; this plan adds a new child entity + a new DTO field; no architectural principle is violated.
- **External standards:** none — this plan does not depend on a wire-level RFC or IETF draft.

---

**Document Version:** 1.3 (Phase 2 shipped + §8 Phase 2 box flipped + endpoint surface doc-updated on 2026-07-16).
**Last Updated:** 2026-07-16.
**Maintained By:** Ordering working group (TBD).
**Status:** Phase 1 fully accepted + Phase 2 shipped. The standalone paged `GET /api/v1/orders/{id}/activities` endpoint is live and the integration test for `orders:view_own` enforcement passes. The `ORDERING_CLEANUP_BACKLOG.md` v1.0 also shipped (Phases A + B + C all closed). All three plans that touched `Ordering` on 2026-07-16 are now in stable state.

**v1.3 changelog (round 7 — Phase 2 implementation + doc + tests, 2026-07-16):**

- **§8 — Phase 2 — Standalone paged activities endpoint flipped to `[x]`** — same-day ship. Handler + query + Carter endpoint + integration test + doc update all landed.
- **New files:** `Ordering.Application/Orders/Queries/GetOrderActivities/GetOrderActivitiesQuery.cs` (record with `OrderId`, `OrderActivityType?`, `Instant? From`, `Instant? To`, `PaginationRequest`); `GetOrderActivitiesHandler.cs` (primary constructor + `Include(o => o.Activities)` + `AsNoTracking()` + `SingleOrDefaultAsync` + post-load fluent filter/paginate/Order chain + `OrderNotFoundException` on miss); `Ordering.API/Endpoints/GetOrderActivities.cs` (Carter `ICarterModule` at `GET /api/v1/orders/{id:guid}/activities` with `[AsParameters] PaginationRequest` binding, `OrderActivityType?` enum binding, ISO-8601 `Instant` parse via `NodaTime.Text.InstantPattern.ExtendedIso`, `RequirePermission("orders:view_own")`, `200/400/404`); `Ordering.API.Tests/Integration/GetOrderActivitiesEndpointTests.cs` (xUnit + `OrderingWebApplicationFactory` + `TestAuthHandler` — the `WithoutOrdersViewPermission_Returns403` test sends kitchen-staff headers and asserts 403 from the authorization handler before the handler runs).
- **Permission literal:** the plan's §6.2 listed `RequirePermission("orders:view_*")`. The actual permission handler at `BuildingBlocks/Authorization/PermissionAuthorizationHandler.cs` does an exact-string compare against the user's `permissions` claims, with no wildcard support. The existing read-endpoint convention (per `OrderingApiIntegrationTests.cs:49`) is the literal `orders:view_own`. The endpoint uses that — matches the test fixture, satisfies the §6.2 intent ("same permission as `GET /orders/{id}`" — which today has no `RequirePermission`, but `orders:view_own` is the project-as-actual convention for view perms).
- **Plan §6.2 test deviation:** the plan listed 4 handler unit tests (`Handler_FiltersByType`, `Handler_FiltersByDateRange`, `Handler_Paginates`, `Handler_UnknownOrder_ThrowsOrderNotFoundException`) under `GetOrderActivitiesHandlerTests`. None of those are shipped in this round. Reason: every query handler in this codebase that uses `Include(...)` + `AsNoTracking()` (`GetOrderByIdHandler`, `GetOrdersHandler`, `GetOrdersByCustomerHandler`) is **not covered** by Application-level unit tests — only the transition handlers (which use `FindAsync`, easily mockable with NSubstitute) have Application unit tests today. Mocking an EF Core query chain that includes `Include` requires either an `IQueryable` backed by an EF Core provider (not currently in `Ordering.Application.Tests` per the csproj — only `Ordering.Application` is referenced) or the `Microsoft.EntityFrameworkCore.InMemory` package (also not referenced). Adding either is a project-wide test-pattern decision, not a Phase-2-only scope. The shipped coverage is the endpoint integration test (`WithoutOrdersViewPermission_Returns403`); happy-path / filter / paginate / not-found coverage is reserved for a future PR that introduces the in-memory test fixture, at which point the four tests from the plan land as a single unit. The v1.3 §8 Phase 2 row records this deviation.
- **Test counts:** 153 domain tests still pass; 41 application tests still pass; the new endpoint test is 1/1 in the filter run. Full `Ordering.API.Tests`: 16/29 passing against Testcontainers; the 13 failures are pre-existing baseline (DB seed / health / outbox tests that need the testcontainers SQL Server + RabbitMQ, not affected by the activity-feed work; matching the v1.0 / v1.1 baseline characterization in §6 of the cleanup backlog).
- **Doc updates:** `docs/architecture/current-architecture.md` §4.5 Endpoint surface table gains one row: `GET /api/v1/orders/{id}/activities → GetOrderActivitiesQuery` (standalone paged activity feed, filters by type + half-open `Instant` date range, returns `PaginatedResult<OrderActivityDto>` ordered by `OccurredAt ASC, Id ASC`, requires `orders:view_own`).
- **End-to-end smoke path (manual on dev DB):** `dotnet ef database update` (already applied 2026-07-16 — see v1.1 changelog) → confirm a fresh order via `POST /orders/{id}/confirm` → `GET /orders/{id}/activities` returns the chronological `Activities` page. No new server-side state.

**v1.2 changelog (round 6 — cleanup backlog closed, 2026-07-16):**

- **§9 References updated.** The bullet pointing at `ORDERING_CLEANUP_BACKLOG.md` previously read "this backlog is for a future Ordering-cleanup pass that runs after the activity feed ships." That future has arrived: the cleanup backlog shipped 2026-07-16 (v1.0). The §9 bullet now reflects reality — it points at the shipped v1.0 of the cleanup backlog and notes that the activity-feed plan was safe to ship against the pre-cleanup mermaid.
- **Cross-link to `ORDERING_CLEANUP_BACKLOG.md` v1.0.** The cleanup backlog's own `v1.0` changelog records the 8-column reconciliation + the audit-fields comment fix (Option B.2) + the relational jsonb sub-shape convention; that document is the durable record of the closed P1-1 / P1-2 / C.1 items. Future diagram drift reviews should re-derive findings from a fresh pass over `db_relational_model.mermaid` + `Order.cs`, not from the pre-cleanup drift reports dated 2026-07-15.
- **No code changes** from the cleanup. Diagram + companion-doc only. All activity-plan Phase 1 code, tests, and migration remain unchanged.
- **Next session candidates:** Phase 2 of this plan (same-day ship captured in v1.3 below); a re-verification pass that opens `db_model_drift_report.md` / `db_model_drift_report_mermaid_truth.md` against the post-cleanup mermaid and confirms no new P1 items (still `[ ]` in §6 of the cleanup backlog).

**v1.1 changelog (round 5 — Phase 1 acceptance + migration applied, 2026-07-16):**

- **§8 — Phase 1 — Acceptance flipped to `[x]`** — `dotnet test` 153/153 + 41/41 ✅; `Ordering.API.Tests` confirmed unchanged vs `main` (no Phase-1 commits touched that project, baseline preserved); migration applied to dev DB.
- **Migration applied on dev DB** — `docker compose up -d orderdb` brought up SQL Server 2022 on port 1433; `dotnet ef database update --project Ordering.Infrastructure --startup-project Ordering.API --connection "Server=localhost,1433;Database=OrderDb;User ID=sa;Password=YrPsswrd123456789;Encrypt=False;TrustServerCertificate=True"` ran clean. Note: `dotnet ef` requires `--startup-project Ordering.API` here — `Ordering.Infrastructure` alone is a class library with no host, so `ApplicationDBContext` can't be constructed outside a hosted environment. All 6 migrations now in `__EFMigrationsHistory`; the new row is `20260716001148_AddOrderActivities`.
- **Post-apply schema verified via `sys.columns` / `sys.indexes` / `sys.foreign_keys` on `OrderDb.dbo`** — `order_activities` table carries 12 columns: the 8 §6 spec columns (`Id`, `OrderId`, `ActivityType nvarchar(50)`, `ActorUserId`, `OccurredAt`, `CorrelationId nvarchar(100)`, `Notes nvarchar(2000)`, `Metadata nvarchar(max)`) + the 4 inherited `AuditableEntity` columns (`CreatedAt`, `CreatedBy`, `LastModified`, `LastModifiedBy` — all nullable). `sys.columns.max_length` is reported in bytes for `nvarchar` (so 100/200/4000/-1 = 50/100/2000/max chars). Indexes: `PK_order_activities` CLUSTERED + `IX_order_activities_OrderId_OccurredAt` NONCLUSTERED. FK: `FK_order_activities_Orders_OrderId` → `Orders`, `ON DELETE CASCADE`.
- **Operator note captured for future migrations** — when applying future Ordering migrations on the dev DB, use `--startup-project ../Ordering.API` (or set it in `Ordering.Infrastructure.csproj` as a `<StartupProject>` MSBuild property to avoid the flag). Two-attempt pattern documented in the v1.1 changelog so the next operator doesn't repeat the first-attempt DI failure.
- **Document version bumped to 1.1** — Phase 1 is fully closed. This plan is now historical: §0 / §1-9 lock the shipped design; §8 is the closing checklist. Future work is the deferred Phase 2 (only if a downstream caller asks) and the `ORDERING_CLEANUP_BACKLOG.md` items (8 missing `Orders` snapshot columns + misleading audit-fields comment + relational jsonb sub-shape convention gap).

**v1.0 changelog (round 4 — Phase 1 implementation complete, 2026-07-16):**

- **§6 Phase 1 header marked ✅ implemented 2026-07-16** — five phase-1 commits landed (BuildingBlocks, Ordering.Domain + tests, Ordering.Application + tests, Ordering.Infrastructure, docs). Code-level lock-in for every locked decision in §0.5 / §0.6 / §3 / §5.
- **§8 Milestone checklist** — six of the seven Phase-1 boxes marked `[x]` (BuildingBlocks, Domain, MassTransit consumers, Application, Infrastructure, Docs). The remaining `[ ]` is **Phase 1 — Acceptance**: `dotnet test` passes on `Ordering.Domain.Tests` (153/153) and `Ordering.Application.Tests` (41/41); the migration file is generated and compiles; `Ordering.API.Tests` has 28 pre-existing integration failures (SQL Server / Testcontainers not available locally) — same failures on `main` without this plan's changes, not a regression. Migration apply + smoke-test of `GET /api/v1/orders/{id}` is the only remaining acceptance step.
- **Document version bumped to 1.0** — last call before commit. The plan is now a historical record of the locked design + an acceptance checklist, not a future-state document.
- **§0.2 doc-update loop closed** — `current-architecture.md` §4.5 / §6 / §9 updated as part of the docs commit; nothing else in the doc-update table is outstanding.
- **Test counts recorded** — 27 new domain tests + 8 new application tests (1 consumer correlation test in addition to the 7 mapping tests) = 35 net new tests, 194 total passing in the touched projects.

**v0.3 changelog (round 3 — status metadata + correlation id + storage decision lock):**

- **§0.6 Correlation context added** — BuildingBlocks contribution: `Correlation/CorrelationContext.cs` (ambient `AsyncLocal<string?>`, internal setters, public `Current`) + `LoggingBehavior.cs` `Set/Clear` calls. Three sources documented (HTTP header `X-Correlation-Id`, MassTransit `ConsumeContext.CorrelationId`, out-of-band null).
- **`OrderActivity` gains `CorrelationId`** — `string?` field (≤ 100 chars); populated from `CorrelationContext.Current` by `Order.RecordActivity`.
- **`OrderActivityDto` gains `CorrelationId`** — mapped in `OrderExtensions.ToOrderDto`.
- **`OrderActivityMetadata` redesigned** — `Guid?` status fields replaced with typed nullable `OrderStatus?` / `PrepStatus?` / `DeliveryStatus?` enums; `Previous*`/`New*` pairs populated per transition type. `OrderItem` prep activities use `PrepStatus`; `Order*` status transitions use `OrderStatus`; `StartDelivery` / `MarkDelivered` also stamp `DeliveryStatus`.
- **`JsonStringEnumConverter` registered in `OrderActivityJson.Options`** — enum values serialised as strings (`"Confirmed"`, not `2`).
- **Migration gains `CorrelationId nvarchar(100) NULL` column.**
- **5 MassTransit consumers gain `CorrelationContext.Set/Clear`** — `BasketCheckoutEventHandler`, `KitchenOrderAcceptedIntegrationEventHandler`, `KitchenOrderPrepStartedIntegrationEventHandler`, `KitchenOrderReadyIntegrationEventHandler`, `KitchenOrderCancelledIntegrationEventHandler`.
- **§3 explicit storage decision** — Catalog's three Marten documents (`OrderSnapshot`, `OrderModificationLog`, `OrderItemPriceAudit`) are explicitly documented as a **different audit**, with four reasons the Ordering `order_activities` does NOT live there (atomicity gap, type weakness, correlation gap, read-path simplicity). The two stores coexist; no future refactor merges them without re-litigating.
- **§7 Cross-service notes** — Catalog bullet rewritten to spell out the two-store coexistence: Catalog owns receipt / manager-override audit (Marten); Ordering owns the chronological activity feed (EF Core SQL Server). No cross-service read path.
- **§8 Milestone checklist** — new `Phase 1 — BuildingBlocks` bullet (CorrelationContext + LoggingBehavior); `Phase 1 — Ordering.Domain` bullet enhanced to mention `CorrelationId` field + typed metadata; new `Phase 1 — MassTransit consumers` bullet for the 5 consumers.
- **§9 References** — three new bullets: `BuildingBlocks/Correlation/CorrelationContext.cs` (new), `BuildingBlocks/Behaviors/LoggingBehavior.cs` (existing, modified), and the three Catalog Marten document files (independent, not merged).

**v0.2 changelog (dotnet-best-practices pass):**

- **§0.3 testing conventions added** — xUnit + FluentAssertions + Moq (MSTest rejected); naming convention `MethodName_StateUnderTest_ExpectedBehavior`; theory-test pattern for enum coverage; null-parameter validation requirement.
- **§0.4 Security & privacy added** — `Notes` length cap (2000), no PII beyond `ActorUserId`, `private set` immutability guarantee, `orders:view_*` authorization inheritance, GDPR data-export note for v2.
- **§0.5 Performance added** — read-path hot-path note (single-query `Include` chain, sub-ms cost), `JsonSerializerOptions` caching justification (Basket §0.3.3 latent-bug class), covering-index rule, `AsSplitQuery()` rejection.
- **Phase 1 §6.1 — H-1**: `OrderActivity` properties are `private set`; factory is the only entry point; `OrderActivity.Create` enforces null / length / unknown-enum invariants.
- **Phase 1 §6.1 — H-2**: `OrderActivityInvariantException` registered in `CustomExceptionHandler` → 422 Unprocessable Content + ProblemDetails; regression test for unknown enum value.
- **Phase 1 §6.1 — H-3**: `Ordering.Infrastructure/Serialization/OrderActivityJson.cs` new file with shared cached `JsonSerializerOptions`; mirrors Basket §0.3.3 latent-bug fix.