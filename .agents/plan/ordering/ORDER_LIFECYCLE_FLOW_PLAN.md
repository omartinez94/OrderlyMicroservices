# Order Lifecycle & Cancellation Flow — Implementation Plan

> Scope: End-to-end order state machine, idempotent kitchen-to-ordering event consumers, Bump-to-Delivered and Recall-to-Ready synchronization, two-way cancellation request/approval workflow across Ordering and Kitchen, per-row permission seeding, and per-phase SLA metrics.

---

## Status

> **Plan version**: `v1.1` (2026-10-24) — comprehensive architecture and audit revision
> **Current state**: 🎉 Complete (Phase 5 Done)

| Phase | Name | Status |
|:-----:|---|:-----:|
| 1 | Idempotent Kitchen Consumers, Bump-to-Delivered & Recall | ✅ Done |
| 2 | Outbox Reliability & Full Order Item Metadata | ✅ Done |
| 3 | Identity Permission Upserts & Ordering Cancellation Workflow | ✅ Done |
| 4 | Kitchen Cancel Request Approval & CancelledPendingAck Queue | ✅ Done |
| 5 | Per-Phase SLA Stats & Stale Order Reconciliation | ✅ Done |

> **Legend**: ✅ Done · 🚧 In progress · ⏸ Pending · 🔒 Blocked

> **Commit messages**: Conventional Commits (`feat:`, `fix:`, `docs:`, `test:`, `refactor:`). Short subject, ≤72 chars, imperative mood, no trailing period.

> **Update rule**: **on every phase completion, the plan MUST be updated in the same commit (or paired plan commit) as the phase work.** The plan is the source of truth for what was decided and what shipped; a phase that ships without a plan update is a phase that drifted.

---

## 0. Skill & documentation conventions

### 0.1 Skill mandate — csharp-developer, add-ordering-feature, add-integration-event & markdown-lint

> **All implementation work on this plan MUST follow `.agents/skills/csharp-developer/SKILL.md`, `.agents/skills/add-ordering-feature/SKILL.md`, `.agents/skills/add-integration-event/SKILL.md`, and `.agents/skills/markdown-lint/SKILL.md`.**

### 0.2 Code-quality guard rails

- **Quality Gate script**: The `phase-guard.ps1` script MUST be executed at the end of every phase with the `-Quick` parameter to verify the build, tests, nullability, Dockerfiles, and dependencies:
  ```powershell
  pwsh ./scripts/phase-guard.ps1 -PhaseName "<Phase Name>" -Quick
  ```
- **NodaTime Mandatory**: Never use `System.DateTime` or `DateTimeOffset` in domain/application models or integration events. Use `Instant` for timestamps and `LocalDate` for calendar dates.
- **Transactional Outbox**: Never invoke raw `IPublishEndpoint.Publish` inside command or domain event handlers; always stage through `IOutboxPublisher`.
- **Carter & MediatR**: All HTTP endpoints use `ICarterModule` with `.RequirePermission(...)` and MediatR `ICommand<T>` / `IQuery<T>`.
- **Explicit C# Types**: Prefer explicit types over `var` outside LINQ, anonymous types, `using var`, and `out var`.

---

## 1. Context

Orders in production/dev environments currently get stuck in `Pending` (19 orders) or `Confirmed` (12 orders) with perpetual "Needs Attention" warnings due to seven interacting root causes discovered during codebase audit:

1. **Non-idempotent and strict state consumers**: `KitchenOrderPrepStartedIntegrationEvent` fires once *per item* (`StartItemPrep.cs:55`), so the 2nd+ item throws `InvalidOrderStateTransitionException` in `Order.MarkPreparing()` (`Order.cs:192`). Similarly, if `KitchenOrderReadyIntegrationEvent` arrives when an order is still `Confirmed` (missing or out-of-order `PrepStarted`), `Order.MarkReady()` (`Order.cs:167`) throws and sends the message to the `_error` queue, leaving the order stuck in `Confirmed` forever.
2. **Bump and Recall do not synchronize with Ordering**: `KitchenOrderBumpedIntegrationEventHandler` (`KitchenOrderBumpedIntegrationEventHandler.cs:25`) is a no-op ("audit only"), so bumping a ready ticket on the KDS never advances the order to `Delivered`. Conversely, `RecallOrder.cs:11-36` (`Bumped -> Ready`) publishes no integration event at all.
3. **Raw publish & lost item metadata on order creation**: `OrderCreatedEventHandler.cs:23` uses raw `IPublishEndpoint.Publish` instead of `IOutboxPublisher`. In addition, `CreateOrderHandler.cs:83` invokes the 3-argument `order.Add(menuItemId, quantity, unitPrice)` overload instead of the 8-argument overload, dropping `MenuItemName`, `SpecialInstructions`, `Customizations`, `SelectedVariations`, and `SeatNumber` before the event reaches Kitchen.
4. **No cancellation request/approval workflow or Kitchen visibility**: Waiters and cashiers cannot cancel `Pending` orders or request cancellation of in-progress orders because `CancelOrder.cs:27` requires `kitchen:update_prep_status`. Furthermore, kitchen staff work exclusively in `/site/kitchen` (backed by `Kitchen.API`) and have no visibility into order-side cancellation requests.
5. **Missing Ordering-to-Kitchen cancel propagation & self-ack loop risk**: Cancelling an order in `Ordering.API` publishes no event, leaving dead tickets active on the KDS. However, if `Ordering` unconditionally publishes `OrderCancelledIntegrationEvent` whenever an order is cancelled, a KDS-initiated cancel (`KitchenOrderCancelledIntegrationEvent`) will loop back to Kitchen and force kitchen staff to acknowledge their own cancellation.
6. **KDS queue drops cancelled tickets**: `KitchenTicketRepository.GetQueueAsync` (`KitchenTicketRepository.cs:21-23`) only queries `New`, `InProgress`, and `Ready`, and `KitchenTicket.Bump()` (`KitchenTicket.cs:204-209`) rejects any status other than `Ready`.
7. **Identity `DataSeeder` short-circuits on existing databases**: `DataSeeder.cs:67-70` and `127-130` exit immediately if `Permissions` or `RolePermissions` has any rows (`if (await dbContext.Permissions.AnyAsync(ct)) return;`), preventing existing databases from receiving `orders:write`, `orders:cancel`, `orders:request_cancel`, or `orders:approve_cancel`.

Reference specification: `docs/architecture/order-lifecycle.md`.

---

## 2. Goal

- Guarantee resilient, idempotent state progression: `Pending -> Confirmed -> Preparing -> Ready -> Delivered`, plus `Delivered -> Ready` when Kitchen recalls a bumped ticket.
- Route `OrderCreatedIntegrationEvent` through `IOutboxPublisher` and preserve full item metadata (`MenuItemName`, `SpecialInstructions`, `Customizations`, `SelectedVariations`, `SeatNumber`).
- Allow Waiters and Cashiers to cancel `Pending` orders directly (`orders:cancel`) and request cancellation (`OrderStatus.CancelRequested` via `orders:request_cancel`) for `Confirmed`, `Preparing`, or `Ready` orders while recording `PreviousStatus`, `CancelRequestedAt`, `CancelRequestedByUserId`, and `CancelRequestReason`.
- Propagate cancellation requests and approvals between `Ordering.API` and `Kitchen.API` so either a Manager (in Ordering) or Kitchen staff (on KDS) can approve or reject a cancellation request (`orders:approve_cancel`), with `CancelRequested` taking precedence over kitchen prep progress events.
- Transition KDS tickets cancelled externally (by Waiter/Cashier/Manager) to `KitchenTicketStatus.CancelledPendingAck` so they remain visible on the KDS until kitchen staff bump/acknowledge them, while KDS-initiated cancellations transition directly to `KitchenTicketStatus.Cancelled` with no self-acknowledgement loop.
- Refactor `Identity.API/Data/DataSeeder.cs` to upsert permissions and role-permission links idempotently per row.
- Align backend `GetLiveOrdersHandler` and `GetOrdersStatsHandler` with `CancelRequested` and per-phase SLA thresholds.

---

## 3. Out of scope

- Automated payment gateway refunds or voiding paid `OrderBill` records on cancellation (documented as future billing work in `docs/architecture/order-lifecycle.md`).
- Automated inventory waste ledger deductions for cancelled `Preparing` or `Ready` meals.
- Advancing orders past `Delivered` to `Completed` or publishing `OrderCompletedIntegrationEvent` to Catalog.

---

## 4. Tech decisions

| Decision | Choice | Reason |
| :--- | :--- | :--- |
| Happy-path terminal state | `OrderStatus.Delivered` via `KitchenOrderBumpedIntegrationEvent` | Matches KDS bump workflow without requiring a separate waiter/cashier close step. |
| Accidental bump recovery | `KitchenOrderRecalledIntegrationEvent` (`Delivered -> Ready`) | Keeps Ordering and Kitchen synchronized when a cook recalls a bumped ticket on the KDS. |
| Precedence during `CancelRequested` | Cancel request wins over prep events | Prevents race conditions where kitchen item start/ready events overwrite a pending cancellation request. |
| Self-ack prevention on cancel | Origin-aware cancel transitions | Cancels initiated or approved on KDS go straight to `KitchenTicketStatus.Cancelled`; cancels from Ordering go to `CancelledPendingAck`. |
| Outbox staging in `OrderCreatedEventHandler` | Inject `IOutboxPublisher` into `OrderCreatedEventHandler` | `DispatchDomainEventsInterceptor` runs inside `SavingChangesAsync` before `base.SavingChangesAsync`, so `OutboxMessages.AddAsync` commits in the same SQL transaction. |
| Permission seeding strategy | Per-row idempotent check in `DataSeeder.cs` | Mirrors commit `97766e7` so existing Docker volumes receive new permissions on startup without dropping tables. |

---

## 5. Folder layout

```text
OrderlyMicroservices/
├── docs/architecture/
│   ├── order-lifecycle.md                                                   # Target specification
│   └── permissions.md                                                       # Updated permission catalog
└── orderly-microservices/
    ├── BuildingBlocks/
    │   └── Enums/OrderEnums.cs                                              # Add OrderStatus.CancelRequested & OrderActivityType entries
    ├── BuildingBlocks.Messaging/
    │   └── Events/
    │       ├── KitchenOrderRecalledIntegrationEvent.cs                      # New: Kitchen -> Ordering (Bumped -> Ready)
    │       ├── KitchenOrderCancelRejectedIntegrationEvent.cs                # New: Kitchen -> Ordering (KDS rejects cancel request)
    │       └── Ordering/
    │           ├── OrderCancelRequestedIntegrationEvent.cs                  # New: Ordering -> Kitchen (marks ticket cancel-requested)
    │           ├── OrderCancelRejectedIntegrationEvent.cs                   # New: Ordering -> Kitchen (clears ticket cancel-requested)
    │           └── OrderCancelledIntegrationEvent.cs                        # New: Ordering -> Kitchen (moves ticket to CancelledPendingAck)
    ├── Services/Identity/Identity.API/
    │   └── Data/DataSeeder.cs                                               # Idempotent per-row permission & role-permission seeding
    ├── Services/Kitchen/Kitchen.API/
    │   ├── Domain/
    │   │   ├── Enums/KitchenTicketStatus.cs                                 # Add CancelledPendingAck = 5
    │   │   └── Entities/KitchenTicket.cs                                    # CancelRequested metadata, MarkCancelledPendingAck, AcknowledgeCancel, RejectCancelRequest
    │   ├── Infrastructure/
    │   │   ├── Data/Migrations/                                             # EF migration for KitchenTicket cancel-request columns
    │   │   ├── Repositories/KitchenTicketRepository.cs                      # Include CancelledPendingAck in GetQueueAsync
    │   │   └── SignalR/
    │   │       ├── IKitchenHubClient.cs                                     # Add OrderCancelRequested / OrderCancelRejected / OrderRecalled hub methods
    │   │       └── KitchenTicketBroadcaster.cs                              # Broadcast new cancel/recall events to KDS
    │   ├── Application/
    │   │   ├── KitchenTickets/Commands/
    │   │   │   ├── BumpOrder.cs                                             # Allow bumping CancelledPendingAck -> Cancelled (no KitchenOrderBumped event)
    │   │   │   ├── RecallOrder.cs                                           # Publish KitchenOrderRecalledIntegrationEvent via IOutboxPublisher
    │   │   │   └── RejectCancelRequest.cs                                   # New command: KDS rejects pending cancel request
    │   │   └── EventHandlers/Integration/
    │   │       ├── OrderCancelRequestedIntegrationEventHandler.cs           # New consumer
    │   │       ├── OrderCancelRejectedIntegrationEventHandler.cs            # New consumer
    │   │       └── OrderCancelledIntegrationEventHandler.cs                 # New consumer
    │   └── Endpoints/
    │       └── RejectCancelRequestEndpoint.cs                               # POST /v1/kitchen/tickets/{id}/reject-cancel
    └── Services/Ordering/
        ├── Ordering.Domain/
        │   └── Models/Order.cs                                              # PreviousStatus, CancelRequestedAt/By/Reason, RequestCancel, ApproveCancel, RejectCancel, RecallDelivery, lenient transitions
        ├── Ordering.Application/
        │   ├── Dtos/
        │   │   ├── OrderDto.cs                                              # Expose PreviousStatus, CancelRequestedAt, CancelRequestReason
        │   │   └── OrderSummaryDto.cs                                       # Expose PreviousStatus, CancelRequestedAt, CancelRequestReason
        │   ├── Extensions/OrderExtensions.cs                                # Map new fields & ResolveSpecificStatusTimestamp for CancelRequested
        │   └── Orders/
        │       ├── Commands/
        │       │   ├── CreateOrder/CreateOrderHandler.cs                    # Use 8-arg order.Add(...) to preserve item metadata
        │       │   ├── CancelOrder/CancelOrderHandler.cs                    # Role/state check + publish OrderCancelledIntegrationEvent
        │       │   ├── RequestCancelOrder/                                  # New command + validator + handler
        │       │   ├── ApproveCancelOrder/                                  # New command + validator + handler
        │       │   └── RejectCancelOrder/                                   # New command + validator + handler
        │       ├── Queries/
        │       │   ├── GetLiveOrders/GetLiveOrdersHandler.cs                # Include OrderStatus.CancelRequested in ActiveStatuses
        │       │   └── GetOrdersStats/GetOrdersStatsHandler.cs              # Include CancelRequested & per-phase SLA evaluation
        │       └── EventHandlers/
        │           ├── Domain/OrderCreatedEventHandler.cs                   # Switch IPublishEndpoint -> IOutboxPublisher
        │           └── Integration/
        │               ├── KitchenOrderAcceptedIntegrationEventHandler.cs   # Idempotent + ignore if CancelRequested
        │               ├── KitchenOrderPrepStartedIntegrationEventHandler.cs# Idempotent + allow Pending->Preparing + ignore if CancelRequested
        │               ├── KitchenOrderReadyIntegrationEventHandler.cs      # Idempotent + allow Confirmed->Ready + ignore if CancelRequested
        │               ├── KitchenOrderBumpedIntegrationEventHandler.cs     # Transition Ready -> Delivered + ignore if CancelRequested
        │               ├── KitchenOrderRecalledIntegrationEventHandler.cs   # New: Transition Delivered -> Ready
        │               ├── KitchenOrderCancelledIntegrationEventHandler.cs  # Accept from CancelRequested/Ready without echoing OrderCancelled
        │               └── KitchenOrderCancelRejectedIntegrationEventHandler.cs # New: Restore PreviousStatus when KDS rejects cancel
        ├── Ordering.Infrastructure/
        │   └── Data/
        │       ├── Configurations/OrderConfiguration.cs                     # Map PreviousStatus & CancelRequest* columns
        │       └── Migrations/                                              # EF migration for Order cancel-request columns
        └── Ordering.API/
            └── Endpoints/
                ├── CancelOrder.cs                                           # RequirePermission("orders:cancel")
                ├── RequestCancelOrder.cs                                    # POST /v1/orders/{id}/request-cancel
                ├── ApproveCancelOrder.cs                                    # POST /v1/orders/{id}/approve-cancel
                └── RejectCancelOrder.cs                                     # POST /v1/orders/{id}/reject-cancel
```

---

## 6. Specification

### 6.1 `Order` Domain State Machine (`Ordering.Domain/Models/Order.cs`)

New enum value in `BuildingBlocks/Enums/OrderEnums.cs`:

- `OrderStatus.CancelRequested = 9` (appended to preserve existing integer/string values).
- `OrderActivityType.OrderCancelRequested` and `OrderActivityType.OrderCancelRejected` added to `OrderActivityType`.

New properties on `Order`:

- `public OrderStatus? PreviousStatus { get; private set; }`
- `public Instant? CancelRequestedAt { get; private set; }`
- `public Guid? CancelRequestedByUserId { get; private set; }`
- `public string? CancelRequestReason { get; private set; }`

State transition rules:

- **`Confirm(Guid confirmedByUserId, Instant now)`**:
  - If `Status is OrderStatus.Confirmed or OrderStatus.Preparing or OrderStatus.Ready or OrderStatus.Delivered or OrderStatus.CancelRequested`: no-op return (idempotent).
  - If `Status == OrderStatus.Pending`: transitions to `Confirmed`, sets `ConfirmedAt = now`, `StatusChangedAt = now`.
- **`MarkPreparing(Guid staffUserId, Instant now)`**:
  - If `Status is OrderStatus.Preparing or OrderStatus.Ready or OrderStatus.Delivered or OrderStatus.CancelRequested`: no-op return (idempotent; fixes multi-item `StartItemPrep` poison messages).
  - If `Status is OrderStatus.Pending or OrderStatus.Confirmed`: sets `ConfirmedAt ??= now`, `PreparingStartedAt ??= now`, transitions to `Preparing`, updates `StatusChangedAt = now`.
- **`MarkReady(Instant now)`**:
  - If `Status is OrderStatus.Ready or OrderStatus.Delivered or OrderStatus.CancelRequested`: no-op return.
  - If `Status is OrderStatus.Pending or OrderStatus.Confirmed or OrderStatus.Preparing`: sets `ConfirmedAt ??= now`, `PreparingStartedAt ??= now`, `ReadyAt = now`, transitions to `Ready`, updates `StatusChangedAt = now`.
- **`MarkDelivered(Instant now)`**:
  - If `Status is OrderStatus.Delivered or OrderStatus.CancelRequested`: no-op return.
  - If `Status is OrderStatus.Ready or OrderStatus.Preparing or OrderStatus.Confirmed`: sets `ReadyAt ??= now`, `DeliveredAt = now`, transitions to `Delivered`, updates `StatusChangedAt = now`.
- **`RecallDelivery(Instant now)`**:
  - If `Status == OrderStatus.Ready or OrderStatus.CancelRequested`: no-op return.
  - If `Status == OrderStatus.Delivered`: clears `DeliveredAt = null`, transitions back to `OrderStatus.Ready`, updates `StatusChangedAt = now`, logs `OrderActivity`.
- **`RequestCancel(string reason, Guid requestedByUserId, Instant now)`**:
  - If `Status == OrderStatus.CancelRequested`: no-op return.
  - Permitted from `Confirmed`, `Preparing`, or `Ready`. Throws `InvalidOrderStateTransitionException` if `Pending` (use direct cancel) or `Delivered`/`Completed`/`Cancelled`.
  - Sets `PreviousStatus = Status`, `CancelRequestedAt = now`, `CancelRequestedByUserId = requestedByUserId`, `CancelRequestReason = reason`, `Status = OrderStatus.CancelRequested`, `StatusChangedAt = now`, and clears `RequiresAdminApproval = false`.
- **`ApproveCancel(Guid approvedByUserId, string? reason, Instant now)`**:
  - Permitted when `Status == OrderStatus.CancelRequested` (or direct cancel from `Pending`/`Confirmed`/`Preparing`/`Ready`).
  - Sets `CancelledAt = now`, `CancellationReason = reason ?? CancelRequestReason`, `Status = OrderStatus.Cancelled`, `StatusChangedAt = now`.
- **`RejectCancel(Guid rejectedByUserId, string? rejectionNotes, Instant now)`**:
  - Permitted only when `Status == OrderStatus.CancelRequested`.
  - Restores `Status = PreviousStatus ?? OrderStatus.Confirmed`, clears `PreviousStatus = null`, `CancelRequestedAt = null`, `CancelRequestedByUserId = null`, `CancelRequestReason = null`, updates `StatusChangedAt = now`.

### 6.2 `KitchenTicket` State Machine (`Kitchen.API/Domain/Entities/KitchenTicket.cs`)

New enum value in `KitchenTicketStatus.cs`:

- `CancelledPendingAck = 5`

New properties on `KitchenTicket`:

- `public bool IsCancelRequested { get; private set; }`
- `public string? CancelRequestReason { get; private set; }`
- `public Instant? CancelRequestedAt { get; private set; }`

State transition rules:

- **`MarkCancelRequested(string reason, Instant now)`**:
  - If `Status is KitchenTicketStatus.Cancelled or KitchenTicketStatus.CancelledPendingAck or KitchenTicketStatus.Bumped`: no-op.
  - Sets `IsCancelRequested = true`, `CancelRequestReason = reason`, `CancelRequestedAt = now`.
- **`ClearCancelRequest()`**:
  - Sets `IsCancelRequested = false`, `CancelRequestReason = null`, `CancelRequestedAt = null`.
- **`MarkCancelledPendingAck(string reason, Instant now)`** (called when Ordering initiates/approves cancel):
  - If `Status is KitchenTicketStatus.Cancelled or KitchenTicketStatus.CancelledPendingAck`: no-op (prevents self-ack loop!).
  - Clears `IsCancelRequested = false`, sets `CancelReason = reason`, `CancelledAt = now`, `Status = KitchenTicketStatus.CancelledPendingAck`.
- **`Bump(Guid userId, Instant now)`**:
  - If `Status == KitchenTicketStatus.CancelledPendingAck`: transitions to `KitchenTicketStatus.Cancelled`, sets `BumpedAt = now`, `BumpedByUserId = userId`. Does **not** emit `KitchenOrderBumpedIntegrationEvent`.
  - If `Status == KitchenTicketStatus.Ready`: existing transition to `KitchenTicketStatus.Bumped`, emits `KitchenOrderBumpedIntegrationEvent`.

### 6.3 Integration Event Contracts (`BuildingBlocks.Messaging/Events/`)

1. **`KitchenOrderRecalledIntegrationEvent`** (Kitchen $\rightarrow$ Ordering):
   - `Guid OrderId`, `Guid KitchenTicketId`, `Guid RestaurantId`, `Guid RecalledByUserId`, `Instant RecalledAt`.
2. **`OrderCancelRequestedIntegrationEvent`** (Ordering $\rightarrow$ Kitchen):
   - `Guid OrderId`, `Guid RestaurantId`, `Guid RequestedByUserId`, `string Reason`, `Instant RequestedAt`.
3. **`OrderCancelRejectedIntegrationEvent`** (Ordering $\rightarrow$ Kitchen):
   - `Guid OrderId`, `Guid RestaurantId`, `Guid RejectedByUserId`, `Instant RejectedAt`.
4. **`KitchenOrderCancelRejectedIntegrationEvent`** (Kitchen $\rightarrow$ Ordering):
   - `Guid OrderId`, `Guid KitchenTicketId`, `Guid RestaurantId`, `Guid RejectedByUserId`, `Instant RejectedAt`.
5. **`OrderCancelledIntegrationEvent`** (Ordering $\rightarrow$ Kitchen):
   - `Guid OrderId`, `Guid RestaurantId`, `Guid CancelledByUserId`, `string Reason`, `Instant CancelledAt`.

### 6.4 HTTP Endpoints & Permissions

| Service | Method & Route | Permission | Behavior |
|---|---|---|---|
| `Ordering.API` | `POST /v1/orders/{id}/cancel` | `orders:cancel` | If caller only holds waiter/cashier role (no `orders:approve_cancel`), only permitted when `Status == Pending`; if caller also holds `orders:approve_cancel` (Manager/Admin/Kitchen), permitted on any pre-`Delivered` state. Publishes `OrderCancelledIntegrationEvent`. |
| `Ordering.API` | `POST /v1/orders/{id}/request-cancel` | `orders:request_cancel` | Transitions `Confirmed`/`Preparing`/`Ready` $\rightarrow$ `CancelRequested`. Publishes `OrderCancelRequestedIntegrationEvent`. |
| `Ordering.API` | `POST /v1/orders/{id}/approve-cancel` | `orders:approve_cancel` | Transitions `CancelRequested` $\rightarrow$ `Cancelled`. Publishes `OrderCancelledIntegrationEvent`. |
| `Ordering.API` | `POST /v1/orders/{id}/reject-cancel` | `orders:approve_cancel` | Restores `CancelRequested` $\rightarrow$ `PreviousStatus`. Publishes `OrderCancelRejectedIntegrationEvent`. |
| `Kitchen.API` | `POST /v1/kitchen/tickets/{id}/cancel` | `kitchen:update_prep_status` | Approves pending cancel request or directly cancels ticket on KDS (`Status = Cancelled`). Publishes `KitchenOrderCancelledIntegrationEvent` to Ordering. |
| `Kitchen.API` | `POST /v1/kitchen/tickets/{id}/reject-cancel` | `kitchen:update_prep_status` | Clears `IsCancelRequested` on ticket and publishes `KitchenOrderCancelRejectedIntegrationEvent` to Ordering. |
| `Kitchen.API` | `POST /v1/kitchen/tickets/{id}/bump` | `kitchen:update_prep_status` | If `Ready` $\rightarrow$ `Bumped` (publishes `KitchenOrderBumpedIntegrationEvent`); if `CancelledPendingAck` $\rightarrow$ `Cancelled` (dismisses from KDS). |

### 6.5 Per-Phase SLA Evaluation (`GetOrdersStatsHandler.cs`)

Update `ComputeActiveSlaMetrics` in `GetOrdersStatsHandler.cs:105-144` to evaluate elapsed time in the **current phase** (`now - order.StatusChangedAt`):

- `OrderStatus.CancelRequested`: immediately counts as `LateCount` (`0s` threshold).
- `OrderStatus.Pending`: late at `>= 120s` (2 min), at-risk at `>= 90s`.
- `OrderStatus.Confirmed`: late at `>= 300s` (5 min), at-risk at `>= 225s`.
- `OrderStatus.Preparing`: late at `>= (EstimatedPrepTimeMinutes ?? 15) * 60s`, at-risk at `>= 75%` of threshold.
- `OrderStatus.Ready`: late at `>= 300s` (5 min), at-risk at `>= 225s`.

---

## 7. Cross-Repository Communication

1. **`OrdersHub` (`/hubs/orders` in `Ordering.API`)**:
   - `OrderChangeNotificationInterceptor` broadcasts `OrderStatusChanged` with `OrderSummaryDto` (now including ` status: "CancelRequested"`, `previousStatus`, `cancelRequestedAt`, `cancelRequestReason`).
2. **`KitchenHub` (`/hubs/kitchen` in `Kitchen.API`)**:
   - `IKitchenHubClient` adds `OrderCancelRequested(Guid ticketId, Guid orderId, string reason, Instant timestamp)` and `OrderCancelRejected(Guid ticketId, Guid orderId, Instant timestamp)`.
   - Existing `OrderCancelled(Guid ticketId, string reason)` is emitted when a ticket enters `CancelledPendingAck` or `Cancelled`.
3. **DTO Parity with `OrderlyWeb`**:
   - `OrderStatus` JSON serialization uses `JsonStringEnumConverter` (`"CancelRequested"`).
   - `KitchenTicketStatus` integer/string serialization adds `CancelledPendingAck = 5` and `isCancelRequested`, `cancelRequestReason` on KDS ticket DTOs.

---

## 8. Security guardrails

> [!CAUTION]
> Waiters and cashiers MUST NOT be able to bypass manager/kitchen approval by calling `POST /v1/orders/{id}/cancel` once an order has reached `Confirmed`, `Preparing`, or `Ready`.

| Risk | Mitigation |
|---|---|
| Waiter/Cashier calling direct `/cancel` on an in-progress order | `CancelOrderHandler` inspects `Status`: if `Status != Pending`, it verifies the caller principal holds `orders:approve_cancel` (or throws `ForbiddenAccessException`). |
| Cross-tenant event spoofing in MassTransit consumers | Every consumer attaches tenant scope via `ICurrentRestaurantProvider.Attach` using `message.RestaurantId`, matching commit `82cef33`. |
| Missing permissions on existing Docker databases | `DataSeeder.SeedPermissionsAsync` and `SeedRolePermissionsAsync` upsert missing rows individually on every startup. |

---

## 9. Development Phases

### Phase overview

| Phase | Name | Deliverables | Goal |
|:---:|---|---|---|
| **1** | Idempotent Kitchen Consumers, Bump-to-Delivered & Recall | `Order.cs`, `KitchenOrder*EventHandler`, `RecallOrder.cs` | Stop poison messages and synchronize `Bump -> Delivered` and `Recall -> Ready`. |
| **2** | Outbox Reliability & Full Order Item Metadata | `OrderCreatedEventHandler.cs`, `CreateOrderHandler.cs` | Guarantee atomic outbox delivery of `OrderCreatedIntegrationEvent` with complete item metadata. |
| **3** | Identity Permission Upserts & Ordering Cancellation Workflow | `DataSeeder.cs`, `Order.cs`, `Request/Approve/RejectCancelOrder`, EF migration | Enable direct Pending cancel and `CancelRequested` approval lifecycle in Ordering. |
| **4** | Kitchen Cancel Request Approval & CancelledPendingAck Queue | `KitchenTicket.cs`, `KitchenTicketRepository.cs`, Kitchen consumers & endpoints | Enable KDS staff to approve/reject cancel requests and acknowledge externally cancelled tickets. |
| **5** | Per-Phase SLA Stats & Stale Order Reconciliation | `GetLiveOrdersHandler.cs`, `GetOrdersStatsHandler.cs`, reconciliation tests | Align live queries/SLA metrics with `CancelRequested` and reconcile stuck orders. |

### Phase 1 — Idempotent Kitchen Consumers, Bump-to-Delivered & Recall

**Goal**: Make all Kitchen-to-Ordering integration event handlers idempotent and out-of-order tolerant, wire `KitchenOrderBumpedIntegrationEvent` to transition orders to `Delivered`, and wire `RecallOrder` to revert `Delivered` back to `Ready`.

**Status**: ✅ Done

**Deliverables**:

- [x] Update `Order.Confirm`, `Order.MarkPreparing`, `Order.MarkReady`, and `Order.MarkDelivered` in `Ordering.Domain/Models/Order.cs` to be idempotent no-ops when the order is already at or past the target status (or in `CancelRequested`), and allow `Pending -> Preparing`, `Confirmed -> Ready`, and `Confirmed/Preparing -> Delivered`.
- [x] Add `Order.RecallDelivery(Instant now)` to transition `Delivered -> Ready`.
- [x] Update `KitchenOrderBumpedIntegrationEventHandler.cs` to load the `Order`, call `order.MarkDelivered(clock.GetCurrentInstant())`, and save changes.
- [x] Create `KitchenOrderRecalledIntegrationEvent.cs` in `BuildingBlocks.Messaging/Events/`, publish it via `IOutboxPublisher` in `Kitchen.API/Application/KitchenTickets/Commands/RecallOrder.cs`, and add `KitchenOrderRecalledIntegrationEventHandler.cs` in `Ordering.Application`.
- [x] Add unit tests in `Ordering.Domain.Tests` and `Ordering.Application.Tests` covering duplicate `PrepStarted` events, out-of-order `Ready` before `PrepStarted`, `Bump -> Delivered`, and `Recall -> Ready`.

**Exit criteria**: `pwsh ./scripts/phase-guard.ps1 -PhaseName "Phase 1" -Quick` passes with all Ordering and Kitchen unit tests green.

---

### Phase 2 — Outbox Reliability & Full Order Item Metadata

**Goal**: Eliminate lost `OrderCreatedIntegrationEvent` messages and blank item names by switching `OrderCreatedEventHandler` to `IOutboxPublisher` and passing all item fields in `CreateOrderHandler`.

**Status**: ✅ Done

**Deliverables**:

- [x] Refactor `Ordering.Application/Orders/EventHandlers/Domain/OrderCreatedEventHandler.cs` to publish `OrderCreatedIntegrationEvent` via `IOutboxPublisher` instead of `IPublishEndpoint` and remove the `OrderFullfilment` feature-flag gate so tickets are always staged atomically.
- [x] Fix `Ordering.Application/Orders/Commands/CreateOrder/CreateOrderHandler.cs:83` to call the 8-argument `order.Add(...)` overload passing `item.MenuItemName`, `item.SpecialInstructions`, `item.Customizations`, `item.SelectedVariations`, and `item.SeatNumber`.
- [x] Add unit and integration tests verifying `OutboxMessage` creation on `CreateOrder` and `BasketCheckoutEventHandler`, including full item metadata serialization.

**Exit criteria**: `pwsh ./scripts/phase-guard.ps1 -PhaseName "Phase 2" -Quick` passes and `CreateOrderEndpointTests` verifies `OutboxMessages` row persistence.

---

### Phase 3 — Identity Permission Upserts & Ordering Cancellation Workflow

**Goal**: Seed `orders:write`, `orders:cancel`, `orders:request_cancel`, and `orders:approve_cancel` idempotently in Identity, and implement the `CancelRequested` state machine, commands, endpoints, and EF Core migration in Ordering.

**Status**: ✅ Done

**Deliverables**:

- [x] Refactor `Identity.API/Data/DataSeeder.cs` (`SeedPermissionsAsync` and `SeedRolePermissionsAsync`) to check and insert missing permissions and role-permission mappings individually per row; add `orders:write`, `orders:cancel`, `orders:request_cancel`, and `orders:approve_cancel` and document them in `docs/architecture/permissions.md`.
- [x] Add `OrderStatus.CancelRequested` and `OrderActivityType` values in `BuildingBlocks/Enums/OrderEnums.cs`.
- [x] Add `PreviousStatus`, `CancelRequestedAt`, `CancelRequestedByUserId`, `CancelRequestReason`, `RequestCancel`, `ApproveCancel`, and `RejectCancel` to `Order.cs`; clear legacy `RequiresAdminApproval` usage.
- [x] Add `OrderCancelRequestedIntegrationEvent`, `OrderCancelRejectedIntegrationEvent`, and `OrderCancelledIntegrationEvent` to `BuildingBlocks.Messaging/Events/Ordering/`.
- [x] Implement `RequestCancelOrderCommand`, `ApproveCancelOrderCommand`, `RejectCancelOrderCommand`, update `CancelOrderCommand` (enforcing `Pending`-only for callers without `orders:approve_cancel`), and expose Carter endpoints in `Ordering.API/Endpoints/`.
- [x] Generate EF Core migration in `Ordering.Infrastructure` for the new `Order` columns and update `OrderDto`, `OrderSummaryDto`, and `OrderExtensions`.

**Exit criteria**: `pwsh ./scripts/phase-guard.ps1 -PhaseName "Phase 3" -Quick` passes with integration tests covering direct cancel, request cancel, manager approve, and manager reject.

---

### Phase 4 — Kitchen Cancel Request Approval & CancelledPendingAck Queue

**Goal**: Synchronize cancellation requests and cancellations with `Kitchen.API` so KDS staff can approve/reject `CancelRequested` tickets and must bump/acknowledge externally cancelled tickets (`CancelledPendingAck`) before they leave the KDS queue.

**Status**: ✅ Done

**Deliverables**:

- [x] Add `KitchenTicketStatus.CancelledPendingAck = 5` and `IsCancelRequested`, `CancelRequestReason`, `CancelRequestedAt` properties + domain methods on `KitchenTicket.cs`, with an EF Core migration in `Kitchen.API`.
- [x] Update `KitchenTicketRepository.GetQueueAsync` to include `KitchenTicketStatus.CancelledPendingAck`.
- [x] Update `BumpOrder.cs` to transition `CancelledPendingAck -> Cancelled` without emitting `KitchenOrderBumpedIntegrationEvent`.
- [x] Add `RejectCancelRequest.cs` command and `POST /v1/kitchen/tickets/{id}/reject-cancel` endpoint in `Kitchen.API`, publishing `KitchenOrderCancelRejectedIntegrationEvent` (and add its consumer in `Ordering.Application`).
- [x] Implement `OrderCancelRequestedIntegrationEventHandler`, `OrderCancelRejectedIntegrationEventHandler`, and `OrderCancelledIntegrationEventHandler` (which no-ops if ticket is already `Cancelled` so KDS-initiated cancels never self-ack) in `Kitchen.API`, broadcasting updates via `KitchenTicketBroadcaster`.

**Exit criteria**: `pwsh ./scripts/phase-guard.ps1 -PhaseName "Phase 4" -Quick` passes with `Kitchen.API.Tests` verifying KDS cancel approval, rejection, external cancel `CancelledPendingAck` queue retention, and no self-ack on KDS direct cancel.

---

### Phase 5 — Per-Phase SLA Stats & Stale Order Reconciliation

**Goal**: Include `CancelRequested` in live order queries and stats, switch backend SLA calculation to per-phase elapsed time, and provide a reconciliation routine for existing stuck `Pending` and `Confirmed` orders.

**Status**: ✅ Done

**Deliverables**:

- [x] Add `OrderStatus.CancelRequested` to `ActiveStatuses` in `GetLiveOrdersHandler.cs` and `GetOrdersStatsHandler.cs`.
- [x] Update `ComputeActiveSlaMetrics` in `GetOrdersStatsHandler.cs` to evaluate per-phase thresholds (`CancelRequested`: 0s, `Pending`: 2m, `Confirmed`: 5m, `Preparing`: `EstimatedPrepTimeMinutes ?? 15m`, `Ready`: 5m) using `StatusChangedAt`.
- [x] Add reconciliation endpoint/command or dev trigger to advance stuck `Confirmed` orders whose KDS ticket is `Ready`/`Bumped` and republish or cancel orphan `Pending` orders.

**Exit criteria**: `pwsh ./scripts/phase-guard.ps1 -PhaseName "Phase 5" -Quick` passes with updated `GetLiveOrdersHandlerTests` and `GetOrdersStatsHandlerTests`.

---

## 10. Technical considerations

### 10.1 Cross-cutting

- **Outbox + `DispatchDomainEventsInterceptor` execution order**: `DispatchDomainEventsInterceptor.SavingChangesAsync` (`Ordering.Infrastructure/Data/Interceptors/DispatchDomainEventsInterceptor.cs:15-19`) dispatches MediatR notifications *before* `base.SavingChangesAsync`. Calling `IOutboxPublisher.PublishAsync` inside `OrderCreatedEventHandler` adds the `OutboxMessage` to the ambient `ApplicationDBContext.ChangeTracker` prior to SQL execution, committing atomically in the same transaction.
- **Preventing cancel loops between Ordering and Kitchen**:
  - When Ordering cancels an order (via `CancelOrderHandler` or `ApproveCancelOrderHandler`), it publishes `OrderCancelledIntegrationEvent`. Kitchen's `OrderCancelledIntegrationEventHandler` checks `if (ticket.Status is KitchenTicketStatus.Cancelled or KitchenTicketStatus.CancelledPendingAck) return;` and otherwise calls `ticket.MarkCancelledPendingAck(...)` (which does **not** publish `KitchenOrderCancelledIntegrationEvent`).
  - When Kitchen cancels a ticket on the KDS (`CancelOrder.cs`), it sets `ticket.Status = KitchenTicketStatus.Cancelled` and publishes `KitchenOrderCancelledIntegrationEvent`. Ordering's `KitchenOrderCancelledIntegrationEventHandler` transitions the `Order` to `Cancelled` directly without publishing `OrderCancelledIntegrationEvent`.
- **Legacy `RequiresAdminApproval` deprecation**: `Order.RequiresAdminApproval` had no approval endpoint or command in the codebase. It is superseded by `OrderStatus.CancelRequested` and cleared on transition.

---

## Changelog

### v1.1 (2026-10-24) — audit revision

- Added `KitchenOrderRecalledIntegrationEvent` (`Bumped -> Ready` reverting `Delivered -> Ready`).
- Added two-way Kitchen KDS integration for `CancelRequested` (`OrderCancelRequestedIntegrationEvent`, `OrderCancelRejectedIntegrationEvent`, `KitchenOrderCancelRejectedIntegrationEvent`, and KDS `reject-cancel` endpoint).
- Prevented KDS self-acknowledgement loop by distinguishing Ordering-originated cancels (`CancelledPendingAck`) from KDS-originated cancels (`Cancelled`).
- Added `KitchenTicketRepository.GetQueueAsync` update for `CancelledPendingAck` and `CreateOrderHandler` 8-arg `order.Add` fix.
- Added Identity `DataSeeder.cs` per-row idempotent permission upserts and `GetLiveOrdersHandler` / `GetOrdersStatsHandler` updates.
- Brought plan structure into full compliance with `_template.md` (Sections 0–10, guardrails, checkboxes, and exit criteria).

### v1.0 (2026-10-24) — initial draft

- Defined initial 5-phase backend plan for order lifecycle and cancellation flow.
