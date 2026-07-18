# Worst-Case Catalog

> Scope: an exhaustive inventory of failure modes, edge cases, abuse scenarios, and catastrophic situations that the Orderly restaurant platform would need to be aware of. **This is a catalog, not a treatment plan** — no mitigations, solutions, or mitigations are proposed. The document's job is to ensure that nothing obvious is missed when future plans address reliability, resilience, compliance, or risk. Future plans reference scenarios by their §number for traceability.

---

## Status

> **Plan version**: `v1.0` (2026-07-17) — initial draft.

> **Current state**: ⏸ Open-ended catalog. No phases. No deliverables to mark done. The document grows by addition, not by version.

> **Update rule**: append scenarios as they surface. Bump `Plan version` (`v{{MAJOR}}.{{MINOR}}`) when a new category is added or when an existing scenario is materially rewritten. Append a Changelog entry with the bump.

> **Commit messages**: `docs(plan): add worst-case scenarios [section]` for additions, `docs(plan): clarify worst-case [section] [id]` for rewrites. Short subject, ≤72 chars, imperative mood, no trailing period.

---

## 0. How to use this document

- **Read it as a checklist, not as a narrative.** Each scenario is a standalone item a future plan can pull on.
- **Do NOT read this for solutions.** None are given. If a section feels like it's begging for a solution, that's the signal that a future plan should exist.
- **Append freely.** New scenarios land at the end of their category, in the order they're thought of. Order within a section is not meaningful.
- **Stable identifiers.** Each scenario gets an ID in the form `<CATEGORY>-<N>` (e.g. `CONCURRENCY-3`, `MONEY-2`). Future plans reference these IDs in their own scope lists.
- **No solutions, no mitigations, no "we should…" prose.** If you find yourself writing any of those, stop — open a future plan and reference the scenario from there.

---

## 1. Catalog

### 1.1 Concurrency & race conditions

- **CONCURRENCY-1** — Two orders for the last portion of halibut in the same millisecond. Both see "1 in stock." Both go through. Restaurant owes two halibuts, has one.
- **CONCURRENCY-2** — Same customer, two devices, two baskets, two checkout buttons pressed simultaneously. Two orders, two charges, two fulfillments.
- **CONCURRENCY-3** — Cashier rings table 12 while server rings table 12 — two order tickets for the same table, kitchen sees them as separate.
- **CONCURRENCY-4** — Auto-86 fires while a customer is mid-checkout with the item in their cart. The state machine is in a half-flipped position.
- **CONCURRENCY-5** — Loyalty points redemption races with the order placement — points spent on a failed order, or points earned twice on a retried order.
- **CONCURRENCY-6** — "First 100 customers" coupon — at 99 redemptions, 50 orders hit the endpoint at once. Hundreds of orders get the discount when only one should.
- **CONCURRENCY-7** — Same waiter adds an appetizer at the same instant another waiter removes the drink. Last write wins, but which one is right?
- **CONCURRENCY-8** — Inventory restock notification arrives while an auto-86 transaction is in flight. Stock flips from 0 → 50 mid-menu-availability-recalculation.

### 1.2 Data integrity & corruption

- **DATA-1** — Postgres primary fails over mid-transaction. Did the order commit? Was payment captured? Was the kitchen ticket emitted?
- **DATA-2** — Read replica serves the owner their live P&L 30 seconds stale — they make a decision (raise price) based on wrong margin.
- **DATA-3** — Schema migration partially applied — Catalog has the new column, Ordering doesn't, the integration event blows up.
- **DATA-4** — Soft-deleted menu item still appears in a customer's saved basket from last week. They order it. Kitchen can't make it.
- **DATA-5** — Timezone drift between services — kitchen local time says "Friday 11pm close," server UTC says "Saturday 4am open." Online orders accepted for a closed restaurant.
- **DATA-6** — Floating-point money math — `0.1 + 0.2 != 0.3`. Over thousands of transactions, the books are off by hundreds of dollars.
- **DATA-7** — Integer overflow on order number after ~7 years of operation. Order #2,147,483,648. Schema breaks.
- **DATA-8** — Foreign key violation during a scheduled cleanup job that deletes the wrong rows.

### 1.3 Network & connectivity failures

- **NETWORK-1** — Customer's phone dies between "place order" and "payment confirmed." They have no idea if the order went through.
- **NETWORK-2** — Payment processor times out — was the card charged? Refund it? Wait for the webhook? Customer is staring at a spinner.
- **NETWORK-3** — Restaurant loses internet for 2 hours during dinner rush. POS offline, online ordering offline, kitchen display offline, payment offline. How do you keep serving?
- **NETWORK-4** — Kitchen display loses wifi — does it queue tickets locally and replay, or does food stop?
- **NETWORK-5** — SMS provider (Twilio) goes down during the order-ready notification window. Customer doesn't know their food is ready.
- **NETWORK-6** — Push notification provider (APNS / FCM) is degraded — staff miss critical alerts during a rush.
- **NETWORK-7** — CDN goes down — menu images 404, online ordering looks broken, customers bounce.

### 1.4 Infrastructure & cloud failures

- **INFRA-1** — AWS region goes down mid-rush. Multi-region failover? Cold standby? Cross-region replication lag?
- **INFRA-2** — RabbitMQ broker down — events pile up in publisher memory. When broker recovers, what replays, what gets lost?
- **INFRA-3** — TLS cert expires at 3am — site is HTTPS-broken, customers see security warnings, cart abandonment spikes.
- **INFRA-4** — DDoS attack on the ordering endpoint — your own customers can't order.
- **INFRA-5** — Cloud storage bucket (menu photos, kitchen display art) becomes unavailable. Menus degrade to text-only.

### 1.5 Third-party / vendor failures

- **VENDOR-1** — DoorDash changes their menu API without notice — auto-86 propagation breaks silently.
- **VENDOR-2** — Stripe outage — no payments can be captured. Orders queue, customers wait.
- **VENDOR-3** — Tax calculation API (Avalara) is down — what tax do you charge? Cached rate from yesterday? Skip tax? Fail closed?
- **VENDOR-4** — Geocoding service returns wrong address — driver goes to a vacant lot 3 miles away.
- **VENDOR-5** — Payment processor declines legitimate cards (false positive from fraud detection) — customer thinks their card is broken.
- **VENDOR-6** — Map provider rate-limited — address autocomplete stops working mid-checkout.
- **VENDOR-7** — Weather API wrong — promised ETA, blizzard hits, driver stuck, food ruined, customer furious.

### 1.6 Operator mistakes

- **OPERATOR-1** — Manager deletes an active menu item by accident. All in-flight orders referencing it now broken.
- **OPERATOR-2** — Owner changes a price mid-shift. Order placed at $42, restaurant rings it at $48. Who eats the difference?
- **OPERATOR-3** — Chef forgets to mark an item 86'd. Sells 30 portions of something they're out of.
- **OPERATOR-4** — Two managers edit the same setting at the same time. Last write wins. Was that the right one?
- **OPERATOR-5** — POS terminal doesn't close a tab at end of night — that revenue doesn't reconcile in tomorrow's books.
- **OPERATOR-6** — Owner fires a staff member but their open orders are still assigned to them.
- **OPERATOR-7** — Restaurant toggles itself "closed" in the dashboard while 3 customers are mid-checkout.

### 1.7 Malicious & abuse scenarios

- **ABUSE-1** — Stolen credit card used for a large order. Restaurant makes the food, delivers it, eats the chargeback.
- **ABUSE-2** — "Dine and dash" — eat in restaurant, leave without paying.
- **ABUSE-3** — Refund abuse — customer claims everything was wrong, demands full refund, system auto-grants.
- **ABUSE-4** — Coupon stacking — applied two coupons when only one is allowed, system didn't validate.
- **ABUSE-5** — Promo code leaks publicly — meant for VIPs, now used by everyone, margin evaporates.
- **ABUSE-6** — Bots scraping menu pricing — competitors see your real-time pricing changes within seconds.
- **ABUSE-7** — Account takeover — customer logs in, places orders, drains loyalty points, makes reservations in someone else's name.
- **ABUSE-8** — Insider fraud — manager comps friends' tabs, adjusts their own bills, deletes the audit trail.
- **ABUSE-9** — Competitor orders out your entire inventory — places 200 orders for items they know are scarce, forces you to 86 your menu and lose real customers.
- **ABUSE-10** — Fake reviews flood in after a competitor pays for them.
- **ABUSE-11** — Brute-force loyalty points redemption — try every code until one works.
- **ABUSE-12** — Chargeback after 90 days — owner already spent the revenue, now owes it back with a $15 fee.

### 1.8 Time & clock issues

- **TIME-1** — Customer's phone is in wrong timezone — orders for "8pm tonight" arrive at 3am server time.
- **TIME-2** — DST transition — spring forward, scheduled orders for "2:30am" don't exist that day.
- **TIME-3** — Customer places order at 11:59:59pm, restaurant flips to new menu at midnight. Old order, new menu, price discrepancy.
- **TIME-4** — Server clock skew across services — events arrive out of order. Kitchen sees "order ready" before "order placed."
- **TIME-5** — Recurring / subscription orders on timezones — owner moves the restaurant across timezones, all recurring orders now fire at wrong times.
- **TIME-6** — "Order for next Tuesday 7pm" placed at 7:01pm Tuesday. Did the customer mean today (impossible) or next week (8 days)?

### 1.9 Scale & load events

- **SCALE-1** — Viral TikTok brings 10,000 concurrent orders to a 4-cook restaurant. Every order confirmed, every ETA lies, every customer gets cold food, every review tanks.
- **SCALE-2** — Friday night rush — 10x normal load. Database connection pool exhausted, request queue times out.
- **SCALE-3** — "We got mentioned on the news" traffic spike. Auto-scaling kicks in but cold starts mean first 100 customers see errors.
- **SCALE-4** — Holiday season — sustained 5x load for 2 weeks. Capacity planning didn't account for it.
- **SCALE-5** — Concert let-out, all nearby restaurants flooded with simultaneous orders to the same area.

### 1.10 Hardware & physical-world failures

- **HARDWARE-1** — Kitchen printer runs out of paper mid-rush. Tickets queue, food piles up, no one knows what's ready.
- **HARDWARE-2** — POS terminal crashes — orders stop. Backup terminal? Manual paper backup?
- **HARDWARE-3** — iPad falls in soup. This is a restaurant, this will happen.
- **HARDWARE-4** — Power outage in the restaurant — what runs on battery, what runs on generator, what's offline-only?
- **HARDWARE-5** — Refrigerator fails — all perishables ruined. Menu items that depended on them now un-orderable, but the auto-86 system doesn't know.
- **HARDWARE-6** — Card reader battery dies mid-transaction. Cash only? Lose the sale?
- **HARDWARE-7** — Cash drawer jams at peak.
- **HARDWARE-8** — Fire, flood, health emergency — restaurant can't operate, but orders are already in flight.

### 1.11 Regulatory & compliance

- **COMPLIANCE-1** — Tax rate changes mid-day (state legislation). Already-placed orders taxed at old rate, new orders at new. Reconcile how?
- **COMPLIANCE-2** — New allergen labeling requirement rolls out — every menu item needs updating.
- **COMPLIANCE-3** — GDPR right-to-be-forgotten request from a customer with 5 years of order history. Do you delete it or anonymize? What about financial records (you can't delete those)?
- **COMPLIANCE-4** — Age verification for alcohol — system asks, customer lies, restaurant gets fined.
- **COMPLIANCE-5** — PCI compliance violation — credit card data leaks. Fines, lawsuits, lost payment processing privileges.
- **COMPLIANCE-6** — Food safety incident — allergen claim was wrong, customer has reaction, lawsuit. Audit trail needed for what was communicated.
- **COMPLIANCE-7** — Cross-border data transfer — EU customer, US restaurant, GDPR + Schrems II. Is your hosting region compliant?
- **COMPLIANCE-8** — Local licensing change — restaurant can no longer sell liquor mid-month. All liquor orders must be canceled.
- **COMPLIANCE-9** — Cash-only regulatory change — must accept cards. Old terminals incompatible.

### 1.12 Money & financial edge cases

- **MONEY-1** — Customer charged twice for one order because they double-clicked and the idempotency key wasn't checked.
- **MONEY-2** — Tip calculation wrong — server underpaid by $40 over a weekend. Restaurant gets sued for wage theft.
- **MONEY-3** — Refund issued but supplier already paid for the inventory — cash flow mismatch.
- **MONEY-4** — Multi-currency — customer pays EUR, restaurant needs USD. FX rate changes between charge and settlement.
- **MONEY-5** — Discount applied but supplier cost has gone up — now you're selling at negative margin. Real-time, silently.
- **MONEY-6** — Pre-auth vs capture on credit card — gas station-style hold vs final charge. Customer sees $80 hold, then $62 capture. Confusion, support tickets.
- **MONEY-7** — Gift card balance disputes — system says $50, customer says $80, no audit trail.
- **MONEY-8** — Chargeback 6 months later — owner spent the money, now owes it back with a $15 fee.

### 1.13 Multi-location & multi-tenant

- **MULTI-1** — Two locations share an ingredient, one runs out, system shows "in stock" because the other has it. Wrong location gets orders it can't fulfill.
- **MULTI-2** — Owner changes a setting that should only apply to one location — applies to all. Tax rate change for one state hits all 50.
- **MULTI-3** — Cross-location staff — chef works at 3 locations, shift schedule doesn't track per-location hours properly.
- **MULTI-4** — Restaurant group acquires another restaurant — how do you merge catalogs, customers, loyalty, historical orders?
- **MULTI-5** — Restaurant closes one location — in-flight orders, scheduled orders, customer reservations, loyalty points. Mass-state-migration problem.

### 1.14 State machine & business logic edge cases

- **STATE-1** — Order stuck in "pending payment" because webhook never arrived. Customer's card was charged but order never confirmed. Restaurant sees nothing.
- **STATE-2** — Order ready for pickup, customer never shows. Food wasted. Did they forget? Are they coming? Should kitchen re-make it?
- **STATE-3** — Customer cancels after kitchen already started cooking. Refund policy? Food cost absorbed? Or partial refund?
- **STATE-4** — Restaurant runs out of an ingredient between order placement and prep start. Auto-86 kicks in but the order is already in the kitchen. What now?
- **STATE-5** — Reservation system says table available, actual floor is full — host didn't update the floor plan.
- **STATE-6** — Order requires manager approval (large / comped) but manager doesn't see the request — order sits, customer waits.
- **STATE-7** — Pre-order for next week, restaurant closes next week for renovation. All N orders canceled automatically? Manually?

### 1.15 Ordering & customer-side chaos

- **ORDER-1** — Customer places order, then changes their mind — restaurant already started cooking. Refund fight.
- **ORDER-2** — Customer's phone stolen after placing order — someone else has the order details, can change the address, redirect the food.
- **ORDER-3** — Group order — 20 people, 20 different payment methods — every one needs to authorize, one fails, does the whole order die?
- **ORDER-4** — Split the bill 7 ways with various items + tip + tax + discount. Math errors are the norm.
- **ORDER-5** — Customer adds to an already-cooking order — kitchen gets a "modified" ticket mid-prep, reworks the dish.
- **ORDER-6** — Customer removes from an already-cooking order — what happens to the food already made?
- **ORDER-7** — "No onions" instruction missed by kitchen — wrong dish sent out, customer demands remake.
- **ORDER-8** — Special instruction impossible — "extra-well done sushi" — kitchen can't fulfill but accepted the order.
- **ORDER-9** — Customer puts wrong address — driver can't find them, food wasted, no refund?
- **ORDER-10** — Customer requests delivery to a moving vehicle (their Uber). Tracking nightmare.
- **ORDER-11** — Allergy info changed after order placed — "actually I'm now allergic to shellfish" — kitchen already used shrimp stock.
- **ORDER-12** — Cash on delivery but driver doesn't have change — customer pays anyway, driver underpaid.
- **ORDER-13** — Customer doesn't hear doorbell when delivery arrives — food gets cold on doorstep, customer claims it never arrived.

### 1.16 Restaurant-side human emergencies

- **HUMAN-1** — Head chef quits mid-shift. No one knows the recipes. Menu collapses to whatever's documented.
- **HUMAN-2** — Health inspector shows up unannounced — find every temp log, hand-wash log, allergen record, vendor invoice, in 5 minutes.
- **HUMAN-3** — Fire alarm goes off — restaurant evacuated, orders in flight, customers waiting, deliveries en route.
- **HUMAN-4** — Food poisoning incident — one bad dish, all orders from the same day are now suspect, every customer needs to be notified.
- **HUMAN-5** — Negative review goes viral — 10x normal order volume arrives, half are angry, half are curious.
- **HUMAN-6** — Owner has a medical emergency mid-service. Who has admin access? What's the playbook?
- **HUMAN-7** — Robbery / break-in — POS is offline, registers are open, no cameras on the system.
- **HUMAN-8** — Key employee (only one who knows the system) is out sick. Everyone else is lost.

### 1.17 Data loss & backup

- **BACKUP-1** — Database backups fail silently for 3 weeks, then DB corrupts — recovery point is 21 days ago. Lost data, lost trust.
- **BACKUP-2** — Backup restored but loses today's orders — owner doesn't notice for a week. Books are wrong.
- **BACKUP-3** — Photo backup lost — menu shows placeholder images for every dish.
- **BACKUP-4** — Customer database lost — GDPR violation + competitive damage + lost loyalty value.
- **BACKUP-5** — Audit log lost — can't investigate a dispute, can't defend against a chargeback.

### 1.18 Migration, deployment, versioning

- **DEPLOY-1** — Deploy during dinner rush — service disruption mid-orders.
- **DEPLOY-2** — Schema migration takes longer than expected — table lock contention, queries time out, orders queue.
- **DEPLOY-3** — New code has a bug that 86s the entire menu — every item shows out of stock across every channel.
- **DEPLOY-4** — Feature flag toggled wrong — kitchen sees a new UI mid-rush without training.
- **DEPLOY-5** — Hotfix deployed without testing — fixes one thing, breaks three others.
- **DEPLOY-6** — Rollback needed, rollback fails — stuck on bad version, manual recovery required.
- **DEPLOY-7** — A/B test leaks — half the customers see new prices, half see old. Price inconsistency reports.
- **DEPLOY-8** — Multi-environment config drift — staging has a different tax rate than prod.
- **DEPLOY-9** — Microservice A deployed but B hasn't picked up the new event contract — events dropped, integrations broken.
- **DEPLOY-10** — Two versions of the same client app in the wild — old version talks to new API in unexpected ways.

### 1.19 Customer behavior edge cases

- **CUSTOMER-1** — Customer dies between order and delivery. Someone has to handle it (refund the family, deal with the food, mark the account).
- **CUSTOMER-2** — Customer places order, then changes their mind mid-prep, then changes back. State machine hell.
- **CUSTOMER-3** — Customer moves between order placement and delivery. Tracking shows them at two addresses 10 minutes apart.
- **CUSTOMER-4** — Customer uses the app to send an order to a friend's address as a gift. Friend doesn't know it's coming, doesn't answer door.

---

## 2. The catastrophic tier

> Scenarios that, if mishandled, are **existential** — not just annoying. These are the ones whose blast radius includes lawsuits, regulatory action, sustained reputation damage, or business shutdown. Listed without ranking; the team should decide which are in-scope for "must-handle" treatment vs "accept and mitigate" once a treatment plan exists.

- **CATASTROPHIC-1** — Mass food poisoning event traced to your platform's allergen / supplier data being wrong. Lawsuits, news coverage, platform shutdown.
- **CATASTROPHIC-2** — Mass data breach — credit card numbers, PII, health data leaked. PCI fine + GDPR fine + customer exodus.
- **CATASTROPHIC-3** — Sustained DDoS during a peak business moment (Black Friday for restaurants). Own customers can't order, revenue evaporates, trust lost.
- **CATASTROPHIC-4** — Third-party aggregator (DoorDash) integration corrupts the menu — your restaurant shows items at wrong prices, you take the blame.
- **CATASTROPHIC-5** — Restaurant worker injury / death caused by overwork driven by an uncalibrated kitchen-load signal ("ETA 5 min!" for a 20-min job). Wrongful death suit.
- **CATASTROPHIC-6** — Wage theft from tip miscalculation discovered in an audit. Class action lawsuit.
- **CATASTROPHIC-7** — Chargeback storm — coordinated fraud ring hits hundreds of restaurants at once.
- **CATASTROPHIC-8** — Cloud account compromised — attacker wipes production database, holds it ransom, your entire multi-tenant business goes dark.
- **CATASTROPHIC-9** — Negative press cycle ("This platform auto-86s items to hide restaurants' dishonesty") — viral reputation damage.
- **CATASTROPHIC-10** — Regulatory shutdown — government decides your auto-pricing / auto-margin / real-time labor tracking crosses a legal line and orders you to cease operations.

---

## 3. What's intentionally NOT in this catalog

> Calling out scope so future contributors don't think it's missing.

- **Solutions, mitigations, partial workarounds.** Belongs in a future treatment plan.
- **Severity scoring / risk matrix.** Belongs in a future triage plan; this catalog is exhaustive-by-section, not ranked.
- **Cost estimates for mitigations.** Belongs in a future budget plan.
- **Probability / likelihood assessments.** Belongs in a future risk register. This catalog only asks: "is this a scenario the platform should be aware of?" — not "how likely is it?"
- **Vendor / technology recommendations.** Belongs in a future architecture plan.
- **Per-feature scoping.** Belongs in feature plans (e.g. `OWNER_INTELLIGENCE_INVESTIGATION.md` Phase 2 will reference these IDs).

---

## Changelog

### v1.0 (2026-07-17) — initial draft
- Cataloged 19 categories with 130+ scenarios across Concurrency, Data, Network, Infrastructure, Vendor, Operator, Abuse, Time, Scale, Hardware, Compliance, Money, Multi-location, State machine, Ordering, Human, Backup, Deploy, Customer.
- Added catastrophic tier with 10 existential scenarios.
- Declared scope in §3 (what's intentionally excluded).