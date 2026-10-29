# Order Lifecycle (Target Spec)

Status: **implemented**. This document is the target behavior
agreed with the product owner. Where it differs from the code, the code is
the current state and this document is the goal. It supersedes the lifecycle
notes in `architecture.md` (lines ~298-311).

## Happy path

```text
Pending -> Confirmed -> Preparing -> Ready -> Delivered
                                       ^          |
                                       +-- Recall +
```

| Transition | Triggered by | Mechanism |
|---|---|---|
| Pending to Confirmed | Kitchen accepts the ticket | `KitchenOrderAcceptedIntegrationEvent` |
| Confirmed to Preparing | Kitchen starts the first item | `KitchenOrderPrepStartedIntegrationEvent` |
| Preparing to Ready | Kitchen marks the ticket ready | `KitchenOrderReadyIntegrationEvent` |
| Ready to Delivered | Kitchen bumps the ticket | `KitchenOrderBumpedIntegrationEvent` (updated consumer) |
| Delivered to Ready | Kitchen recalls a bumped ticket | `KitchenOrderRecalledIntegrationEvent` (new event + consumer) |

- Delivered is the end of the flow for now.
- Recalling a bumped ticket on the KDS (`Bumped -> Ready`) emits
  `KitchenOrderRecalledIntegrationEvent` and reverts the order in Ordering from
  `Delivered` back to `Ready`.
- `Order.Complete()` and `OrderCompletedIntegrationEvent` stay unused. Catalog
  analytics that depend on `OrderCompletedIntegrationEvent` remain empty until
  a Completed step is defined.
- Kitchen to Ordering consumers must be idempotent and tolerant of
  out-of-order events: a transition to a status the order has already reached
  is a no-op, and Confirmed to Ready is accepted.
- `OrderCreatedIntegrationEvent` must be published through `IOutboxPublisher`,
  not a raw `IPublishEndpoint.Publish`, and `CreateOrderHandler` must pass full
  item metadata (`MenuItemName`, `SpecialInstructions`, `Customizations`,
  `SelectedVariations`, `SeatNumber`).

## Cancellation

### Who can cancel

| Order status | Waiter / Cashier | Approver |
|---|---|---|
| Pending (kitchen has not accepted) | Cancels directly | None |
| Confirmed, Preparing or Ready | Can only request cancellation | Manager or kitchen |
| Delivered | Not allowed | Not allowed |

- Manager and kitchen can also cancel directly from any pre-Delivered state.
- Cancelling a Ready order is allowed. The UI lock and the domain guard that
  block it today must be removed.

### New states

- **Order: `CancelRequested`**. The order waits for approval. It stores
  `PreviousStatus`, `CancelRequestedAt`, `CancelRequestedByUserId`, and
  `CancelRequestReason` before entering this state.
- **Kitchen ticket: `CancelRequestPending` flag (or metadata)** while awaiting
  decision so KDS staff can see and approve/reject the request directly from
  `/site/kitchen`.
- **Kitchen ticket: `CancelledPendingAck`**. The ticket stays visible on the
  kitchen screen after an external cancel (Waiter/Cashier direct cancel on
  `Pending`, or Manager approving `CancelRequested`). The kitchen clears it
  with one acknowledge/bump action, which transitions the ticket to
  `Cancelled` and removes it from the active KDS queue.
- **Kitchen-initiated cancel (no self-ack)**. When kitchen staff cancel a
  ticket directly on the KDS or approve a `CancelRequested` ticket on the KDS,
  the ticket goes straight to `Cancelled` (no second acknowledgement needed)
  and publishes `KitchenOrderCancelledIntegrationEvent` to Ordering without
  Ordering echoing `OrderCancelledIntegrationEvent` back.

### Rules

- **Cancel request wins.** While an order is `CancelRequested`, kitchen
  progress events (Preparing, Ready, Bumped, Recalled) are ignored for the
  order status.
- **Denied request.** Manager or kitchen rejects the request. The order returns
  to `PreviousStatus` and notifies Kitchen (`OrderCancelRejectedIntegrationEvent`)
  so the KDS clears the pending cancel request banner.
- **Approved request or direct cancel.** The order becomes `Cancelled`. If
  initiated or approved in Ordering, Ordering publishes
  `OrderCancelledIntegrationEvent` through the outbox and Kitchen moves the
  ticket to `CancelledPendingAck`.

### Permissions

Update `permissions.md` and `Identity.API/Data/DataSeeder.cs` (using per-row
idempotent upserts so existing databases receive new permissions):

- `orders:write`: Waiter, Cashier, Manager, RestaurantAdmin.
- `orders:cancel`: Waiter, Cashier, Manager, RestaurantAdmin, KitchenManager,
  KitchenStaff (Waiter/Cashier restricted in handler to `Pending` orders;
  Manager/Admin/Kitchen allowed on any pre-`Delivered` state).
- `orders:request_cancel`: Waiter, Cashier, Host.
- `orders:approve_cancel`: Manager, RestaurantAdmin, KitchenManager,
  KitchenStaff.

## Needs attention

- Visible to **every role** (Waiter, Cashier, Host, Manager, RestaurantAdmin
  on `/site/restaurant/orders`, and Kitchen roles on KDS).
- Evaluated using **time spent in the current phase** (`now - StatusChangedAt`),
  not total order age:
  - `Pending`: > 2 minutes.
  - `Confirmed`: > 5 minutes.
  - `Preparing`: > `EstimatedPrepTimeMinutes` (default 15 minutes).
  - `Ready`: > 5 minutes.
  - `CancelRequested`: immediate (`0s` — always needs attention).
- `Delivered` and `Cancelled` do not trigger it.
- Legacy `requiresAdminApproval` flag is replaced by `CancelRequested`.

## Future work

- **Refunds** for cancelled orders that were already paid or prepared (food
  waste and void records). Out of scope for this spec.
- A Completed step tied to payment (`OrderBill.PaymentStatus`,
  `payments:process`), and publishing `OrderCompletedIntegrationEvent`.

## Known data issues to reconcile

- Orders stuck in Confirmed or Pending with finished or missing kitchen
  tickets. Advance them when the ticket is Ready or Bumped. If there is no
  ticket, republish `OrderCreatedIntegrationEvent` or cancel the order.
