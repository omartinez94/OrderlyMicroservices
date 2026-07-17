
---

# Discount Chapter — Mermaid as Source of Truth (post-Phase 6, 2026-07-16)

> **Generated:** 2026-07-16 (Discount plan Phase 7 hardening pass).
> **Scope:** `DISCOUNT_SERVICE_PLAN.md` Phases 1–6. The Discount chapter only.
> **Method:** read this report's companion `db_model_drift_report.md` (code side), then verify the resulting mermaid additions respect every convention documented in the companion doc + this file.

## A — Mermaid additions

| Section / block | Pattern followed | Status |
|---|---|---|
| `Coupons` re-added under Discount ownership (relational SQLite) | Relational-block format (column types + UK + indices); compiles against the §0.3.3 validator; matches `CouponConfiguration` 1:1 | ✅ |
| `RewardCodes` (Phase 3) | UK on `(RestaurantId, Code)` + sweep-friendly index `(RestaurantId, IsActive, ExpirationDate)` + nullable soft-delete columns | ✅ |
| `DiscountRules` (Phase 2) | UK on `(RestaurantId, CouponId)` + filter-friendly index `(RestaurantId, IsActive)` + the `RuleDataJson` TEXT column with rule-kind comment | ✅ |
| `ProcessedInboundevents` (Phase 2 idempotency) | Composite PK `(EventId, ConsumerType)` + diagnostic index `(ConsumerType, ConsumedAt)` | ✅ |
| `OutboxMessages` + `OutboxDeadMessages` (Phase 1 + 1B.a outbox SQLite-flavor) | Column shape mirrors BuildingBlocks.Messaging.Outbox `OutboxMessage` entity; the Phase 6.7 `ClaimId` Guid column + covering index `(ClaimId, OccurredOn)` are present | ✅ |

## B — Convention adherence (mermaid-side)

| Companion-doc rule | Mermaid addition | Status |
|---|---|---|
| Strongly-typed value objects render as primitives (line 26) | Coupon / RewardCode / DiscountRule PKs render as `uuid` behind `StoreCouponId/RewardCodeId/RuleId` wrappers | ✅ |
| Audit-FK edges are flat columns, not navigation (line 139) | `RestaurantId` is rendered flat on `Coupons`, `RewardCodes`, `DiscountRules` — no FK edge to `Restaurants` | ✅ |
| Cascade-delete policy: configured in code, not modelled (line 135) | No cascade edge from `Coupons` to `DiscountRules` despite the code's `DeleteBehavior.Restrict` — companion-doc convention says FK delete-behaviour lives in the configuration files | ✅ |
| Owned types (`OwnsOne` / `ComplexProperty`) rendered as a single column + comment naming the owned fields (line 120-127) | `Order.BillingAddress` etc. retain their existing rendering — Discount's tables carry no `ComplexProperty` columns | ✅ |
| jsonb columns use `jsonb <Field> "<description>"` with optional sub-shape | `DiscountRule.RuleDataJson` is a `TEXT` column (SQLite has no native jsonb); rendered with a discriminator comment block — same pattern as `OrderItem.SelectedVariations` | ✅ |
| Out of scope: `Basket.MenuItemId` TODO comment at line 164 | Unchanged | ✅ |

## C — Relationship vocabulary

| New label | Pattern matched | Status |
|---|---|---|
| `Coupons ||--o{ DiscountRules : "rules"` | Snake-case verb-phrase style ("tracks", "contains", "has", "issues") | ✅ |
| `Restaurants ||--o{ RewardCodes : "issues"` | Same pattern | ✅ |
| `Restaurants ||--o{ Coupons : "issues"` re-emission question | Per the Phase 6.2 cleanup deleted the Catalog-side edge. Phase 7 honors the cleanup; the Discount version of the same edge (re-emitted) is a deliberate choice on a separate rerun | ⚠️ deferred to a future mermaid-convention cycle |

## D — Mermaid convention gaps surfaced for the project

| Gap | Surfaced where | Plan § reference |
|---|---|---|
| Relational (non-Marten) tables that Discount owns live in the same mermaid as Catalog's Marten docs (because the file is single-section under `erDiagram` and the comment dividers are the only structural cue) | New `Discount — relational tables (SQLite)` divider comment after the deleted-Coupons marker | Implicit — §7 Phase 7 says "render Discount tables under the CATALOG block (Discount mermaid lives in that header even though Discount owns the tables — same authoring pattern as Catalog's NotificationLog residue)" |
| `DiscountService` is a gRPC surface, not an HTTP/REST surface | The 3-step reflection / aggregate / FK audit convention at companion-doc §0.4.3 + line 132-142 | Implicit |

## P0 / P1 prioritized list (Discount chapter)

| Priority | Item | Action | Status |
|---|---|---|---|
| **P0-1** | Add the five new Discount tables to the mermaid | Render `Coupons` + `RewardCodes` + `DiscountRules` + `ProcessedInboundevents` + `OutboxMessages` + `OutboxDeadMessages` in lockstep with the code | ✅ **DONE in this pass (Phase 7 hardening)** |
| **P0-2** | Re-emit `Restaurants ||--o{ Coupons : "issues"` (Discount version of the deleted edge) | Deferred per Phase 6.2 cleanup; tracked for §0.4.3 reconciliation cycle | ⚠️ **OPEN** — explicit P1 item |

## Sign-off (Discount chapter)

The Discount code (post-Phase 6, verified 2026-07-16) and the mermaid additions in this pass **match**. Every convention in the companion doc is respected. The single P1 item (`Restaurants ||--o{ Coupons` re-emission) is a deferred mermaid-convention decision — it does not block the Discount schema being accurately reflected in the diagram.

Future mermaid reviewers: the Discount and Ordering chapters are independent. Reviewers concerned with Discount state should read this chapter; reviewers concerned with Ordering state should read the §A–§F above.
