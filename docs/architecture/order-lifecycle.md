# Order Lifecycle (Target Spec)

Status: **agreed, not yet implemented**. This document is the target behavior
agreed with the product owner. Where it differs from the code, the code is
the current state and this document is the goal. It supersedes the lifecycle
notes in `architecture.md` (lines ~298-311).

## Happy path

```text
Pending -> Confirmed -> Preparing -> Ready -> Delivered
```

| Transition | Triggered by | Mechanism |
|---|---|---|
| Pending to Confirmed | Kitchen accepts the ticket | `KitchenOrderAccepted` event |
| Confirmed to Preparing | Kitchen starts the first item | `KitchenOrderPrepStarted` event |
| Preparing to Ready | Kitchen marks the ticket ready | `KitchenOrderReady` event |
| Ready to Delivered | Kitchen bumps the ticket | `KitchenOrderBumped` event (new consumer) |

- Delivered is the end of the flow for now.
- `Order.Complete()` and `OrderCompleted` stay unused. Catalog analytics that
  depend on `OrderCompleted` remain empty until a Completed step is defined.
- Kitchen to Ordering consumers must be idempotent and tolerant of
  out-of-order events: a transition to a status the order has already reached
  is a no-op, and Confirmed to Ready is accepted.
- `OrderCreated` must be published through the outbox, not a raw publish.

## Cancellation

### Who can cancel

| Order status | Waiter / Cashier | Approver |
|---|---|---|
| Pending (kitchen has not accepted) | Cancels directly | None |
| Confirmed, Preparing or Ready | Can only request cancellation | Manager or kitchen |
| Delivered | Not allowed | Not allowed |

- Manager and kitchen can also cancel directly.
- Cancelling a Ready order is allowed. The UI lock and the domain guard that
  block it today must be removed.

### New states

- **Order: `CancelRequested`**. The order waits for approval. It stores the
  previous status (`PreviousStatus`) before entering this state.
- **Kitchen ticket: `CancelledPendingAck`** (working name). The ticket stays
  visible on the kitchen screen after an approved or direct cancel. The kitchen
  clears it with one acknowledge action (bump or confirm). Only then does it
  leave the kitchen list.

### Rules

- **Cancel request wins.** While an order is `CancelRequested`, kitchen
  progress events (Preparing, Ready, Bumped) are ignored for the order status.
- **Denied request.** The order returns to `PreviousStatus`.
- **Approved request or direct cancel.** The order becomes `Cancelled` and
  Ordering publishes `OrderCancelled` through the outbox. Kitchen consumes it
  and moves the ticket to `CancelledPendingAck`.
- Today Ordering publishes no cancel event, so a cancelled order stays on the
  kitchen screen. That is a bug fixed by this spec.

### Permissions

New permissions (names are proposals; update `permissions.md` and the Identity
`DataSeeder`):

- `orders:request_cancel`: Waiter, Cashier.
- `orders:approve_cancel`: Manager, KitchenManager, KitchenStaff.
- Direct cancel of a Pending order: Waiter, Cashier, Manager.

The Identity seed only runs when `RolePermissions` is empty, so existing
databases need a migration or an upsert.

## Needs attention

- Visible to **every role**.
- Configurable time limit **per phase** (Pending, Confirmed, Preparing, Ready).
  Preparing may allow more time than Pending, and Ready has its own limit.
- Delivered and Cancelled do not trigger it.
- Today it is computed only in the frontend (`AttentionFeed.tsx`, with Pending
  at 2 minutes and Ready at 5 minutes, and one overall SLA for the rest).
- `requiresAdminApproval` is a flag that nothing ever clears. Remove it or
  replace it with the cancel-request flow.

## Future work

- **Refunds** for cancelled orders that were already paid or prepared (food
  waste and void records). Out of scope for this spec.
- A Completed step tied to payment (`OrderBill.PaymentStatus`,
  `payments:process`), and publishing `OrderCompleted`.

## Known data issues to reconcile

- Orders stuck in Confirmed or Pending with finished or missing kitchen
  tickets. Advance them when the ticket is Ready or Bumped. If there is no
  ticket, republish `OrderCreated` or cancel the order.
