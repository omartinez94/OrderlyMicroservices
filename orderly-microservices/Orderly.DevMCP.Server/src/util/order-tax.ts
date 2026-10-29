/**
 * The tax rate every seeding tool applies to the orders it writes.
 *
 * `ORDER_TAX_RATE` is a FRACTION: 0.16 means 16%.
 *
 * Scale warning — the rate lands in two different columns, and BOTH
 * hold the fraction (not a percent):
 *   - `Catalogdb."Restaurants"."TaxRate"` — decimal(5,2); the Catalog
 *     domain validator caps it at 1
 *     (`Catalog.API/Features/Restaurants/UpdateRestaurant`).
 *   - `OrderDb."Orders"."TaxRate"` — decimal(18,2); the Ordering DTO
 *     validator caps it at 1
 *     (`Ordering.Application/Dtos/Validators/OrderDtoValidator`).
 *
 * The seeded Acme restaurant row in `CatalogDbContext` uses `0.16m` for
 * the same reason. A seeded value of `16` would mean 1600% and would be
 * rejected by the API that owns the column.
 *
 * Keep this the single source of truth: `data-seeding.ts` and `jobs.ts`
 * both import it instead of hard-coding their own copy.
 */

export const ORDER_TAX_RATE = 0.16;
