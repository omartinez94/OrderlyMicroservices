# Owner Intelligence — Investigation Plan

> Scope: investigate what the Orderly platform would need — data, services, events, infrastructure — to ship the three "kill-shot" features that define the owner-intelligence positioning: (1) **live per-dish P&L**, (2) **kitchen-load-aware ETAs**, (3) **inventory-aware auto-86**. The output of this plan is a recommendation memo + per-feature architecture sketches, **not** working code. A follow-on implementation plan will be authored from the investigation's findings.

---

## Status

> **Plan version**: `v1.0` (2026-07-17) — initial draft. `MINOR` increments per phase completion; `MAJOR` is reserved for breaking restructures of the plan itself.

> **Current state**: ⏸ Not started (Phase 1 is the next action).

| Phase | Name | Status |
|:-----:|---|:-----:|
| 1 | Data source audit | ⏸ Pending |
| 2 | Per-feature gap analysis | 🔒 Blocked (waits on Phase 1) |
| 3 | Architecture sketches + open questions | 🔒 Blocked (waits on Phase 2) |
| 4 | Recommendation memo + hand-off to implementation plan | 🔒 Blocked (waits on Phase 3) |

> **Legend**: ✅ Done · 🚧 In progress · ⏸ Pending · 🔒 Blocked

> **Commit messages**: Conventional Commits (`feat:`, `docs:`, `chore:`, `test:`, `fix:`, `docs(plan):`). Short subject, ≤72 chars, imperative mood, no trailing period.

> **Update rule**: **on every phase completion, the plan MUST be updated in the same commit as the phase work.** The plan is the source of truth for what was decided and what shipped; a phase that ships without a plan update is a phase that drifted. See [Phase-completion workflow](#phase-completion-workflow) at the bottom.

---

## 0. Skill & documentation conventions

### 0.1 Skill mandate — `grill-with-docs` before Phase 4

> **Before Phase 4 (Recommendation memo) the plan MUST be run through the `grill-with-docs` skill.** It stress-tests the recommended approach against the existing domain model and surfaces terminology drift before the implementation plan inherits it.

### 0.2 Code-quality guard rails

- **Investigation-only output.** No production code lands in this plan's commits. The deliverable per phase is a section in this document + a possible spike in `.agents/spike/` if needed.
- **Evidence-driven claims.** Every claim about what the system already does must cite a file:line (read against current code, never from memory). Drift here would mislead the implementation plan.
- **Spike code is throwaway.** If a phase requires a quick proof-of-concept (e.g. wiring a fake event sink to validate a contract), the spike lives under `.agents/spike/owner-intelligence/<phase>/` with a `README.md` that explains what it was meant to prove and how to delete it.

---

## 1. Context

The three kill-shot features were selected (see the chat that preceded this plan) as the highest-leverage differentiators vs. Toast / Square / Lightspeed / Olo / the delivery aggregators. They share a single positioning thesis — **"the operating system that doesn't lie to you"** — and all three put real-time truth into the hands of the restaurant owner.

Today the platform is primarily a commerce pipeline: Catalog → Basket → Ordering → Discount → Yarp gateway, with Kitchen added in the recent M0–M5 series. There is no first-class concept of:

- **Live margin / contribution** at the dish level (today: only menu price is stored).
- **Kitchen state as a first-class input to ETA** (today: prep events are recorded for audit but do not feed back into the customer-facing ETA).
- **Inventory as a constraint on the menu** (today: `IsActive` is a manual flag, no automatic propagation across channels).

Before any implementation plan is written, we need to answer: **what would the system actually have to change to deliver these three?** This plan drives that investigation.

Reference: [`docs/architecture/current-architecture.md`](../docs/architecture/current-architecture.md) is the baseline snapshot of the system as it stands today.

---

## 2. Goal

Deliver a recommendation memo (per Phase 4) that answers, for each of the three features:

1. **What data we already capture.** Cite the existing entity + field + service.
2. **What data we'd have to add.** New entities / fields / events; integration channels we'd have to instrument.
3. **Where the architectural seams are.** Which services change; which stay untouched; what's the boundary between "real-time stream" and "durable truth."
4. **The open questions we cannot answer without experimentation.** Spike work, vendor conversations, or design reviews.

The memo is the input to a future `OWNER_INTELLIGENCE_IMPLEMENTATION_PLAN.md` (not authored in this plan). The implementation plan's phase ordering, effort estimates, and risk register will be derived from the memo.

---

## 3. Out of scope

- **Implementation.** No production code ships from this plan. Spikes are throwaway.
- **The other 7 features** identified in the differentiation brainstorming (the "compounds" and "moat builders" — Regulars CRM, voice analytics, what-if simulator, menu psychology, demand forecasting, complaint resolution, direct delivery network). Each will get its own future plan if pursued; this plan is scoped to the three kill shots.
- **Vendor selection.** The investigation identifies what we'd need from a supplier-price feed, an SMS provider, etc., but does not pick vendors.
- **UX / design.** The memo identifies surfaces (owner mobile, kitchen display, customer-facing ETA), but does not specify screens.
- **Pricing / packaging.** Whether the three features are sold together, separately, or gated by tier is a product decision outside this plan.

---

## 4. The three features under investigation

### Feature A — Live per-dish P&L

> **One-line pitch:** the owner can answer, on their phone, at any moment, "what's the real margin on the ribeye right now?" — including live ingredient cost, allocated labor, and current discount drag.

**Why it's hard:** requires integrating three data domains that today live in different services and are mostly absent — supplier food cost (we have no supplier pricing), per-dish labor allocation (we have no labor tracking), and discount impact (Discount.Grpc has coupons but no per-dish discount accounting at the aggregate level).

**Investigation questions (resolved by Phase 2):**

- Q.A1. What menu / price data does Catalog own today, and where would plate cost live?
- Q.A2. How are discounts applied per-line-item today (if at all)? Is there enough granularity to allocate the discount drag to specific dishes?
- Q.A3. What labor data exists? Is the kitchen staffing model captured anywhere, or would this need a new service?
- Q.A4. How would supplier price updates enter the system (file upload, vendor API, manual entry)? Each path has different integration cost.

### Feature B — Kitchen-load-aware ETAs

> **One-line pitch:** the customer sees an ETA computed from real-time kitchen station state (queue depth, station load, prep time per dish), not from the cashier's clock. When the kitchen is overloaded, the system stops taking orders it can't honestly fulfill.

**Why it's hard:** the Kitchen service (M0–M5 series, recently shipped) emits prep events for audit but doesn't expose a live "what's the queue look like right now" query surface. The Ordering service computes ETAs today from a static prep-time-per-dish field — there is no feedback loop from kitchen reality.

**Investigation questions (resolved by Phase 2):**

- Q.B1. What prep-state events does Kitchen emit today (started, ready, etc.), and at what granularity?
- Q.B2. Is there a per-station model, or is the kitchen treated as a single logical queue?
- Q.B3. Where does the ETA live today (Ordering API response? a separate ETA service?) and what's the read latency we can tolerate?
- Q.B4. What would an honest "stop taking orders" look like — graceful degradation at the gateway, a 503 from the ordering endpoint, or a slot picker that pushes the customer to the next available window?
- Q.B5. How does this interact with the existing `OrderTimingAnalytics` entity in Catalog (used for historical analytics)?

### Feature C — Inventory-aware auto-86

> **One-line pitch:** when stock of an ingredient drops below threshold, the corresponding menu item disappears across every channel (in-store POS, online, third-party) within 60 seconds, and reappears automatically when stock restocks.

**Why it's hard:** Catalog owns `Ingredient` + `MenuItemIngredient` (the recipe link), but stock counts are not modeled. The "auto-86" propagation across third-party delivery platforms requires integration with each aggregator's menu API — a long tail.

**Investigation questions (resolved by Phase 2):**

- Q.C1. Where would stock counts live? New entity, or a column on `Ingredient`?
- Q.C2. What's the stock-decrement trigger — order placement (which subtracts immediately) or kitchen consumption confirmation (which subtracts when the item is actually made)?
- Q.C3. Which channels matter for v1? Own online ordering is table stakes; in-store POS requires an integration we don't have; aggregators (DoorDash, Uber Eats) are N integrations.
- Q.C4. How does this interact with `MenuItem.IsActive`? Are they the same concept or different (active = owner choice, in-stock = current reality)?
- Q.C5. What's the recovery story — when stock is miscounted and a customer orders an item that's actually out, who's accountable?

---

## 5. Investigation phases

### Phase overview

| Phase | Name | Output | Goal |
|:---:|---|---|---|
| **1** | Data source audit | §6.1 audit table | A single table of "what we have today" with file:line citations, per data domain. |
| **2** | Per-feature gap analysis | §6.2 gap matrix per feature | A list, per feature, of what's missing, where it would live, and the integration boundary. |
| **3** | Architecture sketches + open questions | §6.3 sketches + §7 open questions | One ASCII / Mermaid sketch per feature, with spikes identified for non-obvious risks. |
| **4** | Recommendation memo + hand-off | §8 recommendation + a new `OWNER_INTELLIGENCE_IMPLEMENTATION_PLAN.md` (separate file) | Decision-ready memo: build order, effort estimates, risks, what to spike first. |

---

### Phase 1 — Data source audit

**Goal**: produce a single audit table (one row per data domain) covering what the platform captures today across Catalog, Ordering, Kitchen, Basket, Discount, and Identity.

**Status**: ⏸ Pending

**Deliverables:**

- [ ] §6.1 "Data we have today" — table with columns: domain | entity / field | service / file:line | freshness (real-time? transactional? nightly?) | currently consumed by
- [ ] Inventory of `MenuItem`, `Ingredient`, `MenuItemIngredient`, `IngredientAlternative` — what's actually there
- [ ] Inventory of order-side pricing (`OrderItemPriceAudit`, discount application) — where does price live at order time
- [ ] Inventory of kitchen prep events — what events are emitted, what payload, who's the consumer
- [ ] Inventory of basket-side pricing + customization capture
- [ ] Inventory of any labor / staffing model — confirm absence if none exists

**Exit criteria**: §6.1 table is filled, every row cited with file:line read against current code. The investigation can proceed to gap analysis without re-reading the codebase.

---

### Phase 2 — Per-feature gap analysis

**Goal**: for each of the three features, produce a gap list mapping the missing data / events / services to where they'd live.

**Status**: 🔒 Blocked (waits on Phase 1)

**Deliverables:**

- [ ] §6.2.A — Feature A (Live P&L) gap matrix: data needed | where it lives | new vs. extended | integration boundary
- [ ] §6.2.B — Feature B (Kitchen-aware ETAs) gap matrix: same shape
- [ ] §6.2.C — Feature C (Inventory-aware auto-86) gap matrix: same shape
- [ ] Cross-cutting gap list — anything shared across multiple features (e.g. an event bus topic for kitchen load if all three need it)

**Exit criteria**: each gap is concrete enough that the architecture sketch in Phase 3 can answer "where does this entity live and who writes / reads it."

---

### Phase 3 — Architecture sketches + open questions

**Goal**: one sketch per feature (ASCII or Mermaid, your choice) showing the data flow end-to-end, plus a list of open questions that need a spike or external input.

**Status**: 🔒 Blocked (waits on Phase 2)

**Deliverables:**

- [ ] §6.3.A — Feature A sketch: owner phone → ??? service → Catalog/Ordering/Discount/labor feed → ??? back
- [ ] §6.3.B — Feature B sketch: Ordering ETA computation → Kitchen event subscription → customer-facing response
- [ ] §6.3.C — Feature C sketch: order placed → inventory decrement → Catalog `IsActive` flag → menu propagation → third-party sync
- [ ] §7 — Open questions list with classification: needs spike | needs vendor input | needs design review | needs ADR
- [ ] At least one spike in `.agents/spike/owner-intelligence/phase-3/` if any "needs spike" item is identified (e.g. a 50-line C# console that proves the kitchen event can be subscribed to with sub-second latency)

**Exit criteria**: a senior engineer could read §6.3 sketches and reproduce the data flow on a whiteboard. The open questions list is finite and classified.

---

### Phase 4 — Recommendation memo + hand-off

**Goal**: a decision-ready memo the team can use to green-light the implementation plan, plus the kickoff of that implementation plan as a separate file.

**Status**: 🔒 Blocked (waits on Phase 3)

**Deliverables:**

- [ ] §8 — Recommendation memo: for each feature, recommended approach, effort estimate (S/M/L/XL), risks, dependencies, and the build order across the three
- [ ] §9 — Decision log: any irreversible decisions made in Phase 2/3 and why
- [ ] Run `grill-with-docs` against the recommendation memo (per §0.1)
- [ ] Hand-off commit: author `OWNER_INTELLIGENCE_IMPLEMENTATION_PLAN.md` (separate file under `.agents/plan/`), starting from the memo as input. The implementation plan is **not** part of this plan; it inherits from it.

**Exit criteria**: the implementation plan file exists, has a phase 1 defined, and the recommendation memo is approved (or revised and approved).

---

## 6. Findings (filled in as phases complete)

> Section header is here as a placeholder; content lands here as each phase ships. Per the template convention, findings land in the same commit as the phase work, not retroactively.

### 6.1 Data we have today (Phase 1)

_To be filled in Phase 1._

### 6.2 Per-feature gap analysis (Phase 2)

_To be filled in Phase 2._

#### 6.2.A — Feature A (Live P&L)

_To be filled in Phase 2._

#### 6.2.B — Feature B (Kitchen-aware ETAs)

_To be filled in Phase 2._

#### 6.2.C — Feature C (Inventory-aware auto-86)

_To be filled in Phase 2._

### 6.3 Architecture sketches (Phase 3)

_To be filled in Phase 3._

#### 6.3.A — Feature A

_To be filled in Phase 3._

#### 6.3.B — Feature B

_To be filled in Phase 3._

#### 6.3.C — Feature C

_To be filled in Phase 3._

---

## 7. Open questions

> Section reserved for Phase 3. Items are classified: **S** = needs spike · **V** = needs vendor input · **D** = needs design review · **A** = needs ADR.

_To be filled in Phase 3._

---

## 8. Recommendation memo

> Section reserved for Phase 4. Format: one subsection per feature with **approach**, **effort**, **risks**, **dependencies**; one cross-cutting subsection with the **build order** across all three.

_To be filled in Phase 4._

---

## 9. Decision log

> Any irreversible decision made during the investigation (e.g. "Feature A's labor data will live in a new `Labor.API` service, not in Catalog"). Each entry gets a one-line rationale.

_To be appended as decisions are made._

---

## Phase-completion workflow

> **Every phase completion is two commits, not one.**

1. **Investigation commit** — the work itself (`docs(plan): Phase 1 complete in OWNER_INTELLIGENCE_INVESTIGATION` or `chore(spike): ...` if a spike was needed). Do NOT touch the plan in this commit.
2. **Plan commit** — the plan update only (`docs(plan): mark Phase 1 complete in OWNER_INTELLIGENCE_INVESTIGATION`):
   - Bump `Plan version` from `v1.0` → `v1.1` in the Status section.
   - Mark the phase's `[ ]` → `[x]` and update the table row.
   - Fill in the corresponding §6.x subsection.
   - Append an entry to the [Changelog](#changelog) at the bottom.

> Two commits keeps the diff reviewable: the investigation commit is just findings, the plan commit is just documentation. Mixing them makes both harder to review and easier to forget.

---

## Changelog

### v1.0 (2026-07-17) — initial draft
- Created plan with 4 phases (Data source audit → Gap analysis → Architecture sketches → Recommendation memo).
- Sections 0–9 drafted; placeholder content under §6–§9.
- Hand-off target: future `OWNER_INTELLIGENCE_IMPLEMENTATION_PLAN.md`, not authored in this plan.