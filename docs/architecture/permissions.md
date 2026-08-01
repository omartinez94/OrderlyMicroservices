# Permission Catalog

> **Single source of truth** for every permission string used across the
> Orderly microservices. Every JWT carries one-or-more permission claims
> (Identity shape A: one claim per permission) emitted from
> `Identity.API/Services/ClaimsTransformer.cs`. Services consult this
> list when calling `.RequirePermission("...")` (Carter + minimal API)
> or `[Permission(...)]` (gRPC) — they must use the exact string
> defined here, never a hand-typed variant.

> **Why a catalog**: prior to Phase 4 of the Trust Root Hardening plan,
> each service hard-coded its own permission strings. A typo in one
> service silently broke the corresponding policy without surfacing
> anywhere. This file is the agreed spelling for every permission; the
> service-side constants (e.g. `DiscountPermissions.CouponRead`,
> `KitchenPermissions.ConfirmOrder`) and the Identity `DataSeeder`
> permission seeds must all match these values.

## Format

`<resource>:<verb>` — kebab-case resource, snake-case verb. The
`<resource>` matches a domain aggregate (coupon, reward-code,
discount-rule, menu, order, …); the `<verb>` is a coarse-grained
capability (read, create, edit, delete, redeem, …). Verbs do not
encode the *state* a verb mutates — that's a future refinement
(coupons have an `activate` / `deactivate` pair; rewards have
`expire`; both are currently `edit` because the plan chose coarse
verb granularity at seed time).

---

## Catalog (Permissions)

| Permission | Domain | Service | Method / endpoint guarded | Notes |
|---|---|---|---|---|
| `catalog:menu_update` | Restaurant menu (write) | Catalog.API | `POST/PUT/DELETE /api/v1/restaurants/*`, `POST/PUT/DELETE /api/v1/brands/*`, `POST/PUT/DELETE /api/v1/menu-categories/*`, `POST /api/v1/restaurants/{id}/bulk-order-uploads/{id}/approve`, `POST /api/v1/restaurants/{id}/bulk-order-uploads/{id}/reject` | Coarse "menu write" permission. Read endpoints (GET /restaurants, GET /brands, GET /menu-categories) remain anonymous. |
| `orders:write` | Order lifecycle (create / mutate) | Ordering.API | `POST /api/v1/orders`, `PUT /api/v1/orders`, `DELETE /api/v1/orders/{id}` | Mirrors the kitchen `update_prep_status` shape. Granted to Waiter + Manager + RestaurantAdmin via the `RolePermissions` table. |
| `orders:view_own` | Order read | Ordering.API | `GET /api/v1/orders`, `GET /api/v1/orders/{id}`, `GET /api/v1/orders/customer/{customerId}` | Read endpoints. Tenant-scoped via `ICurrentRestaurantProvider` (the global query filter). |
| `orders:create` | Order create | Ordering.API | (legacy seed) | See `orders:write` — kept in seed for back-compat with the v1 role mapping. |
| `orders:view_all` | Order admin read | Ordering.API | (legacy seed) | Manager-only. |
| `orders:modify_ordering` | Mutate in `Ordering` status | Ordering.API | (legacy seed) | KitchenManager + Waiter. |
| `orders:modify_confirmed` | Mutate in `Confirmed` status | Ordering.API | (legacy seed) | Manager + KitchenManager + RestaurantAdmin. |
| `orders:modify_ready` | Mutate in `Ready` status | Ordering.API | (legacy seed) | RestaurantAdmin. |
| `orders:admin` | Cross-account admin | Ordering.API | (legacy seed) | Basket `/api/v1/admin/carts/*` admin tooling. |
| `coupon:read` | Discount coupon read | Discount.Grpc | `GetDiscount`, `ListDiscounts` | Public-facing — the Basket / Order-side auto-apply path needs this. |
| `coupon:create` | Discount coupon create | Discount.Grpc | `CreateDiscount` | |
| `coupon:edit` | Discount coupon edit | Discount.Grpc | `UpdateDiscount` | |
| `coupon:delete` | Discount coupon delete | Discount.Grpc | `DeleteDiscount` | |
| `coupon:redeem` | Discount coupon redeem | Discount.Grpc | `RedeemDiscount` | Hot path — called by Ordering after the basket's apply-surface pass. |
| `reward-code:read` | Reward code read | Discount.Grpc | `GetRewardCode`, `ListRewardCodes` | |
| `reward-code:create` | Reward code create | Discount.Grpc | `CreateRewardCode` | |
| `reward-code:edit` | Reward code edit | Discount.Grpc | `UpdateRewardCode` | |
| `reward-code:delete` | Reward code delete | Discount.Grpc | `DeleteRewardCode` | |
| `reward-code:redeem` | Reward code redeem | Discount.Grpc | `RedeemRewardCode` | |
| `discount-rule:read` | Discount rule read | Discount.Grpc | `GetDiscountRule`, `ListDiscountRules` | |
| `discount-rule:edit` | Discount rule edit | Discount.Grpc | `CreateDiscountRule`, `UpdateDiscountRule`, `DeleteDiscountRule`, `EvaluateDiscountRules` | Edit + evaluate share a permission because the rule is what gets evaluated. |
| `kitchen:view_orders` | Kitchen order read | Kitchen.API | (legacy seed) | |
| `kitchen:update_prep_status` | Kitchen prep transition | Kitchen.API + Ordering.API (ConfirmOrder) | `POST /api/v1/orders/{id}/confirm` + the kitchen prep endpoints | The single permission shared between Kitchen and Ordering. |
| `kitchen:view_activities` | Kitchen activity read | Kitchen.API | (legacy seed) | |
| `kitchen:confirm_order` | Confirm order (alt name) | Kitchen.API | (legacy seed) | Kept for back-compat; see `kitchen:update_prep_status` for the canonical permission. |
| `users:view_all` | User list read | Identity.API | `GET /api/v1/users` | |
| `users:create` | User create | Identity.API | `POST /api/v1/users` | |
| `users:edit` | User edit | Identity.API | `PUT /api/v1/users/{id}` | |
| `users:delete` | User delete | Identity.API | `DELETE /api/v1/users/{id}` | |
| `users:assign_roles` | Assign role to user | Identity.API | `POST /api/v1/users/{id}/roles` | |
| `users:assign_restaurants` | Assign restaurant to user | Identity.API | `POST /api/v1/users/{id}/restaurants` | |
| `roles:view` | Role list read | Identity.API | `GET /api/v1/roles` | |
| `roles:create` | Role create | Identity.API | `POST /api/v1/roles` | |
| `roles:edit` | Role edit | Identity.API | `PUT /api/v1/roles/{id}` | |
| `roles:edit_permissions` | Edit role→permission map | Identity.API | `POST /api/v1/roles/{id}/permissions` | |
| `permissions:view` | Permission list read | Identity.API | `GET /api/v1/permissions` | |
| `menu:view` | Menu read (Identity view) | Identity.API | (legacy seed) | Identity-side mirror of catalog read; not the same as `coupon:read` (different domain). |
| `menu:edit` | Menu edit (Identity view) | Identity.API | (legacy seed) | |
| `reservations:view` | Reservation read | Catalog.API | (legacy seed) | |
| `reservations:create` | Reservation create | Catalog.API | (legacy seed) | |
| `reservations:edit` | Reservation edit / cancel | Catalog.API | (legacy seed) | |
| `payments:process` | Payment process | Ordering.API | (legacy seed) | Cashier-only. |
| `payments:split_bill` | Bill split | Ordering.API | (legacy seed) | |
| `payments:view_reports` | Payment reports | Ordering.API | (legacy seed) | |
| `audit:view` | Audit log read | Identity.API | (legacy seed) | |

---

## Cross-service source-of-truth

| Service | Constants file | Where used |
|---|---|---|
| Discount.Grpc | `Discount.Grpc/Authorization/DiscountPermissions.cs` | `AuthorizationPolicies.AddDiscountPolicies` registers each as a policy; `DiscountAuthorizationInterceptor` enforces at call time. |
| Catalog.API | (uses inline `RequirePermission("catalog:menu_update")` calls) | `EndpointRouteBuilderExtensions.RequirePermission` resolves the policy via the `Permission:` prefix. |
| Ordering.API | (uses inline `RequirePermission("orders:write")` / `orders:view_own`) | Same as Catalog. |
| Kitchen.API | (uses inline `RequirePermission("kitchen:*")`) | Same. |
| Identity.API | `DataSeeder` seeds the `Permission` + `RolePermission` rows on first startup. The set must match the table above or the policy map is empty for the missing strings. | `DataSeeder.SeedPermissionsAsync`. |

When adding a new permission:

1. Add a row to the table above. Pick `<resource>:<verb>` from the
   format guideline.
2. If the service uses constants: add the constant to its
   `*Permissions.cs` file. Never inline the string at the call site.
3. Update `Identity.API/Data/DataSeeder.SeedPermissionsAsync` so the
   permission is seeded into the DB on first startup. Existing dev
   databases are NOT migrated; the row is added idempotently when
   the table is empty, otherwise a manual
   `INSERT INTO Permissions ...` is required.
4. Add a row to `RolePermissions` mapping in
   `SeedRolePermissionsAsync` for each role that should receive the
   permission.
5. Reference the constant from the gRPC `[Permission]` attribute or
   the API `.RequirePermission(...)` call.
6. Add an entry to the table above.

When renaming or removing a permission:

1. Update the table.
2. Update the service constant.
3. Update `SeedPermissionsAsync`. **Do not delete the row** —
   rename the `Name` column instead; existing role mappings will
   silently fail otherwise.
4. Grep for the old string in the codebase; replace every reference.
5. Update the `RolePermissions` rows to match.

---

## Changelog

### v1.0 (2026-07-31) — initial catalog

Created as Phase 4 of the Trust Root Hardening plan. The catalog is
authoritative for the permission strings; the existing per-service
constants (`DiscountPermissions`, the inline `RequirePermission` calls
in Catalog/Ordering/Kitchen) are still the *implementation* of these
strings, but the catalog is the *contract* between services.
