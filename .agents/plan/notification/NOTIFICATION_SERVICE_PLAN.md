# Notification.API — Service Plan (v1)

> Scope: **green-field v1** for a new `Notification.API` microservice. This service does not exist yet — `Services/` currently holds only Basket / Catalog / Discount / Identity / Kitchen / Ordering. This plan stands up the skeleton and the first delivery paths.
>
> **Why now:** Catalog already *publishes* two integration events that have **no consumer** — `FeedbackSubmittedIntegrationEvent` (Catalog Phase 4) and `ReservationReminderDueIntegrationEvent` (Catalog Phase 5) — and `OrderCompletedIntegrationEvent` is available for receipt generation. The bus retains these undelivered until a consumer exists. This plan is the consumer.
>
> **Origin:** promoted out of `CATALOG_SERVICE_PLAN.md` **Phase 6.1** (out-of-plan prerequisite) and **§6.7** (NotificationLog ownership decision). Those sections are the authoritative source for the `NotificationLog` merge + backfill; this plan owns the destination.

---

## 1. Context

There is no notification capability in the system today. Several flows that should notify a customer or operator currently dead-end:

- **Feedback rewards** — Catalog issues a reward code on `OverallRating ≥ 4` and publishes `FeedbackSubmittedIntegrationEvent`, but nothing sends the reward to the customer.
- **Reservation reminders** — Catalog's `ReservationReminderJob` publishes `ReservationReminderDueIntegrationEvent` on schedule, but nothing sends the WhatsApp/email.
- **Receipts** — `OrderCompletedIntegrationEvent` fires on order completion with no receipt delivery.
- **`NotificationLog`** — a Marten document is registered in Catalog (`Program.cs`) but **never written to**; per Catalog §6.7 the only `NotificationLog` going forward is a **relational table owned by this service**.

The architecture (`docs/architecture/architecture.md` §616) prescribes Twilio / SendGrid integrations for delivery.

---

## 2. Goal

Stand up `Notification.API` with:

1. A **service skeleton** matching the other services (Carter minimal API, JWT auth via Identity, PostgreSQL, MassTransit + RabbitMQ + outbox, health checks).
2. The **relational `notification_log` table** (§6.7 — the single source of truth for notification records).
3. **Consumers** for the three already-available events (`FeedbackSubmittedIntegrationEvent`, `ReservationReminderDueIntegrationEvent`, `OrderCompletedIntegrationEvent`).
4. A **delivery abstraction** (`INotificationSender`) with at least one real channel (email via SendGrid or WhatsApp/SMS via Twilio) and a retry worker.
5. The **`CustomerFeedback` aggregate** move from Catalog (owns the feedback + reward-code flow).
6. The **backfill job** that migrates Catalog's Marten `mt_doc_notification_log` rows into the relational table, after which Catalog drops its Marten document (§6.7).

---

## 3. Out of scope (v1)

- Rich templating engine / per-tenant branded templates — v1 uses simple string templates; a template system is a follow-up.
- Push notifications / in-app notifications — v1 is email + WhatsApp/SMS only.
- The Reservation / WalkInQueue aggregate move (that is the separate Ordering-side plan; this service only *consumes* reservation events).
- Deleting Catalog's Marten `NotificationLog` — that is a Catalog-side change *triggered* by this plan's backfill completing (tracked in Catalog §6.7 doc-update scope).

---

## 4. Service boundaries

### Notification.API owns

- **`NotificationLog`** — the relational delivery record (the only one going forward, per Catalog §6.7). Columns: `id`, `RestaurantId`, `Channel`, `MessageType`, `RecipientType`, `RecipientIdentifier`, `Status` (`Pending | InFlight | RetryPending | Sent | Failed`), `AttemptCount`, `NextAttemptAt?`, `LastError?`, `RelatedOrderId?`, `RelatedReservationId?`, `CreatedAt`, `SentAt?`. Indexed by `(Status, NextAttemptAt)` for the retry worker.
- **`CustomerFeedback`** aggregate + the reward-code generation flow (moved from Catalog; `architecture.md` §411-415).
- The delivery pipeline: `INotificationSender` implementations + the retry worker.
- Publishing (v1, if wired): `NotificationDelivered`, `NotificationFailed`.

### Notification.API does NOT own

- **Order / Reservation / MenuItem data** — consumed via events only; never a local write path to another service's tables.
- **Reservation lifecycle** — Catalog (today) / Ordering (future) owns the reservation state machine; this service only reacts to `ReservationReminderDueIntegrationEvent`.

### Events consumed (v1)

| Event | Source | Action |
|---|---|---|
| `OrderCompletedIntegrationEvent` | Ordering | Generate + send receipt; log a `notification_log` row. |
| `FeedbackSubmittedIntegrationEvent` | Catalog | Send the reward code to the customer; log the delivery. |
| `ReservationReminderDueIntegrationEvent` | Catalog | Send the reservation reminder (WhatsApp/email); log the delivery. |

---

## 5. Tech decisions

| Decision | Choice | Reason |
|---|---|---|
| Architecture | Vertical Slice, single project | Matches Catalog / Basket / Discount. |
| Framework | ASP.NET Core 10 (Carter + minimal API) | Project standard. |
| Language | C# 12+ (records, primary constructors, nullable enabled) | Project standard. |
| Persistence | EF Core 10 + Npgsql, new `notificationdb` | New database owned by this service. |
| Messaging | `MassTransit` via `BuildingBlocks.Messaging.MassTransit.AddMessageBroker` + outbox | Reuse the shared extension. |
| Delivery | `INotificationSender` abstraction; Twilio (WhatsApp/SMS) + SendGrid (email) | `architecture.md` §616. Secrets via env-var placeholders. |
| Retry | Relational retry worker keyed on `(Status, NextAttemptAt)` | Operational fields are inherently relational (Catalog §6.7). |
| Health | `/live` + `/ready` (Postgres, RabbitMQ, outbox DLQ) | Mirror Catalog's split. |
| Time / IDs | NodaTime `Instant`, `Guid` ids | Project convention. |
| Tests | xUnit + FluentAssertions + Moq; Testcontainers (Postgres + RabbitMQ) for consumers/backfill | Project convention. |

> **Skill mandate:** all implementation invokes `/csharp-developer` and follows the `dotnet-best-practices` + `api-design-principles` guard rails, same as the Catalog plan §0.

---

## 6. Phased milestones (skeleton)

### Phase 1 — Service skeleton
- Create `Services/Notification/Notification.API/` (Carter, JWT auth, `NotificationDbContext` on new `notificationdb`, health checks, `AddMessageBroker`).
- Add the YARP route + cluster (`notification-api` → `notification-cluster`) in `ApiGateway/YarpApiGateway/appsettings.json`.
- Register in docker-compose (service + `notificationdb`).

### Phase 2 — `notification_log` table + delivery abstraction
- EF migration for `notification_log` (columns + `(Status, NextAttemptAt)` index per §4).
- `INotificationSender` + one real channel (SendGrid or Twilio); config via `IOptions<T>` + `ValidateOnStart()`.
- Retry worker (`IHostedService`) draining `RetryPending` rows by `NextAttemptAt`.

### Phase 3 — Event consumers
- `OrderCompletedIntegrationEvent` → receipt.
- `FeedbackSubmittedIntegrationEvent` → reward-code delivery.
- `ReservationReminderDueIntegrationEvent` → reminder delivery.
- Each consumer is idempotent and writes a `notification_log` row.

### Phase 4 — `CustomerFeedback` move from Catalog
- Introduce the `CustomerFeedback` aggregate + reward-code flow here.
- Follow the Catalog §6.6 gateway route-migration convention for any HTTP surface that moves.

### Phase 5 — `NotificationLog` backfill (drives Catalog §6.7 cleanup)
- One-shot job (gated `Notification:BackfillNotificationLogs=true`): read Catalog's `mt_doc_notification_log`, map fields (`Marten Guid → OriginalMartenId`; `Status Pending/Sent/Failed → 1:1`; channel/type/recipient/content/related ids/timestamps copy directly), idempotent insert keyed by `OriginalMartenId`.
- Verify `COUNT(*)` parity, then hand off to Catalog to drop its Marten document + schema registration (§6.7 steps).

---

## 7. Cross-service notes (carried from Catalog plan)

- **NotificationLog ownership** — single owner: this service, relational. Rationale + full backfill sequence in `CATALOG_SERVICE_PLAN.md` §6.7. Do not re-introduce a Marten copy.
- **Already-published, unconsumed events** — `FeedbackSubmittedIntegrationEvent` (Catalog Phase 4) and `ReservationReminderDueIntegrationEvent` (Catalog Phase 5) are live on the bus with no consumer; Phase 3 here is what drains them.
- **Event versioning** — honour `int SchemaVersion` on every consumed event; ignore unknown fields (MassTransit default), per Catalog §6.5 migration rules.
- **Catalog-side cleanup this plan triggers** (owned by Catalog, done once Phase 5 backfill verifies):
  1. Delete `Catalog.API/Models/NotificationLog.cs`.
  2. Remove `opt.Schema.For<NotificationLog>();` from `Catalog.API/Program.cs`.
  3. Drop the empty `mt_doc_notification_log` storage.
  4. Update `db_relational_model.mermaid` + `.md` (remove the `NotificationLog` block + its two relationship rows).
  5. Update `current-architecture.md` §4.2 (drop the `NotificationLog` row) and add a new §4.7 Notification Service.

---

## 8. Milestone checklist

- [ ] **Phase 1** — Notification.API skeleton (Carter, JWT, `notificationdb`, health, MassTransit); gateway route + compose entry.
- [ ] **Phase 2** — `notification_log` table + `INotificationSender` (one real channel) + retry worker.
- [ ] **Phase 3** — three event consumers (receipt, reward, reminder), each idempotent + logged.
- [ ] **Phase 4** — `CustomerFeedback` aggregate + reward flow moved from Catalog.
- [ ] **Phase 5** — `NotificationLog` backfill verified; Catalog §6.7 cleanup handed off.
- [ ] **Docs** — new `current-architecture.md` §4.7 Notification Service; `db_relational_model.{mermaid,md}` add `notification_log`, drop the Catalog Marten block.

---

## 9. References

- `CATALOG_SERVICE_PLAN.md` §6.1 (Notification v1 prerequisite), §6.7 (NotificationLog ownership + backfill), §6.5 (event contract matrix).
- `docs/architecture/architecture.md` §411-415 (feedback + reward flow), §616 (Twilio / SendGrid).
- Sibling plans for structural convention: `.agents/plan/catalog/CATALOG_SERVICE_PLAN.md`, `.agents/plan/discount/DISCOUNT_SERVICE_PLAN.md`.

---

**Document Version:** 0.1 (skeleton)
**Last Updated:** 2026-07-13
**Maintained By:** Notification working group (TBD)
**Status:** Not started — blocks Catalog §6.7 cleanup and drains two already-published Catalog events.
