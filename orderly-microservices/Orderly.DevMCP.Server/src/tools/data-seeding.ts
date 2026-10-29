/**
 * tools/data-seeding.ts (§6.2).
 *
 * Two tools:
 *   - seed_test_menu(restaurantId, dryRun?) — inserts the canonical
 *     menu from `resources/seeds/catalog-seed.json` into Catalogdb.
 *     Idempotent (uses ON CONFLICT … DO UPDATE). Supports `dryRun`
 *     to return the SQL without executing.
 *   - create_mock_order(restaurantId, status?, orderType?, itemCount?, seed?) —
 *     inserts a fake order into OrderDb Orders + OrderItems + Customers.
 *     OrderAddress is a ComplexProperty on Orders (not a separate table —
 *     the plan §6.2 was wrong on this; corrected here). The order picks
 *     1-12 distinct menu items from the catalog seed (quantity
 *     MIN_ITEM_QUANTITY..MAX_ITEM_QUANTITY per line); the pick is
 *     reproducible when a `seed` string is supplied.
 *
 * Notes on the corrections:
 *   - OrderStatus enum (BuildingBlocks/Enums/OrderEnums.cs) is
 *     { Ordering, Pending, Confirmed, Preparing, Ready, Delivered,
 *     Completed, Cancelled, OnHold }, NOT the plan's { Pending,
 *     Processing, Completed, Cancelled }.
 *   - DeliveryAddress + BillingAddress are owned ComplexProperty columns
 *     (DeliveryAddress_Street, …), not a separate OrderAddresses table.
 *   - A line carries AT MOST ONE variation. The catalog's variations are
 *     mutually exclusive options (size, doneness, with/without cheese),
 *     so the picker chooses exactly one and folds its price delta into
 *     the line price. Carrying every variation on the line claimed the
 *     customer chose mutually exclusive options at once and never moved
 *     the price delta onto the bill.
 *
 * §10.1 — restaurantId is sanitised to a sha256 bucket before any
 * interpolation into seed string artifacts.
 *
 * ShortId: Brands, Restaurants and MenuItems all carry a `ShortId`
 * column with a UNIQUE index (migration 20261023185527_AddShortIdToCatalogEntities).
 * The column is NOT NULL DEFAULT '', so any INSERT that omits it lands
 * on '' — the first new row succeeds and the second one violates the
 * unique index, which rolls back the whole seed transaction. Every
 * seeded row therefore writes an explicit, stable ShortId.
 */

import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, resolve } from 'node:path';
import { z } from 'zod';
import sql from 'mssql';
import type { McpServer } from '@modelcontextprotocol/sdk/server/mcp.js';

import type { Logger } from '../logger.ts';
import type { ToolContext } from './types.ts';
import { restaurantBucket } from '../util/sanitize.ts';
import { generateShortId } from '../util/short-id.ts';
import { mulberry32, randomInt, randomSeed, seedFromString } from '../util/random.ts';
import { ORDER_TAX_RATE } from '../util/order-tax.ts';

export { ORDER_TAX_RATE };

const __dirname = dirname(fileURLToPath(import.meta.url));
const SEEDS_DIR = resolve(__dirname, '../../resources/seeds');

const ORDER_STATUSES = ['Ordering', 'Pending', 'Confirmed', 'Preparing', 'Ready', 'Delivered', 'Completed', 'Cancelled', 'OnHold'] as const;
const ORDER_TYPES = ['DineIn', 'Takeout', 'Delivery'] as const;

/** Minimum / maximum number of distinct line items in a mock order. */
export const MIN_ORDER_ITEMS = 1;
export const MAX_ORDER_ITEMS = 12;
/** Minimum / maximum quantity rolled for a single line item. */
export const MIN_ITEM_QUANTITY = 1;
export const MAX_ITEM_QUANTITY = 3;

/**
 * Shape of `resources/seeds/catalog-seed.json`.
 *
 * NOT PERSISTED BY THE SEEDER — the ids below are authoring aids only:
 *   - `categories[].id` — the seeder assigns `MenuCategories.Id = idx + 1`
 *     (a global integer key) and never writes the seed's category id.
 *   - `items[].categoryId` — there is no category FK on `MenuItems`; the
 *     seeder writes `MenuItems.SubCategoryId = NULL` and creates no
 *     `MenuSubCategories` rows, so the category link is lost on write.
 *   - `variations[].id` — `MenuItemVariations` has no id column in the
 *     seeded shape; only `Name` / `VariationValue` / `PriceModifier` are
 *     written.
 *
 * `brand.id`, `restaurant.shortId` and `items[].id` / `items[].shortId`
 * ARE persisted.
 */
export interface CatalogSeed {
  brand: { id: string; shortId: string; name: string; cuisineType: number; description: string; contactEmail: string; contactPhone: string; logoUrl: string; websiteUrl: string };
  restaurant: { shortId: string };
  categories: Array<{ id: string; name: string; description: string; displayOrder: number }>;
  items: Array<{
    id: string;
    shortId: string;
    categoryId: string;
    name: string;
    description: string;
    basePrice: number;
    prepTimeMinutes: number;
    prepTimeMaxMinutes: number;
    variations?: Array<{ id: string; name: string; priceDelta: number }>;
    customizations?: Array<{ ingredientName: string; action: string }>;
  }>;
}

export interface OrderSeed {
  customerId: string;
  restaurantId: string;
  userName: string;
  orderType: string;
  deliveryAddress: { street: string; city: string; state: string; zipCode: string; country: string };
  billingAddress: { street: string; city: string; state: string; zipCode: string; country: string };
  payment: { cardName: string; cardNumber: string; expiration: string; ccv: string; paymentMethod: string };
  orderItems: Array<{
    menuItemId: string;
    menuItemName: string;
    quantity: number;
    unitPrice: number;
    selectedVariations?: Array<{ name: string; price: number }>;
    customizations?: Array<{ label: string; value: string; price: number }>;
  }>;
}

/** A line item of a mock order — the shape OrderDb and the event envelope expect. */
export interface SelectedOrderItem {
  menuItemId: string;
  menuItemName: string;
  quantity: number;
  unitPrice: number;
  selectedVariations?: Array<{ name: string; price: number }>;
  customizations?: Array<{ label: string; value: string; price: number }>;
}

/** Where the picked line items came from. */
export type PickSource = 'catalog' | 'fallback';

function readSeed<T>(name: string): T {
  return JSON.parse(readFileSync(resolve(SEEDS_DIR, name), 'utf-8')) as T;
}

function nowMs(): Date { return new Date(); }

/**
 * Escapes a value for interpolation into a single-quoted SQL literal.
 *
 * Every string the seed builder interpolates goes through this helper,
 * so the escaping invariant lives in one place instead of being applied
 * by hand at each interpolation site (where it was easy to forget).
 */
export function sqlText(value: string): string {
  return value.replace(/'/g, "''");
}

/** Rounds to two decimals — the precision of every money column involved. */
function round2(value: number): number {
  return Math.round(value * 100) / 100;
}

/**
 * Money totals for a mock order — pure, no I/O.
 *
 * The tax is `ORDER_TAX_RATE` (16%) applied to the subtotal. Every
 * value is rounded to two decimals because the OrderDb money columns
 * are decimal(18,2) — the bill the customer sees must match the row.
 */
export function computeOrderTotals(
  orderItems: ReadonlyArray<{ unitPrice: number; quantity: number }>,
): { subtotal: number; taxAmount: number; total: number } {
  const subtotal = round2(orderItems.reduce((sum, it) => sum + it.unitPrice * it.quantity, 0));
  const taxAmount = round2(subtotal * ORDER_TAX_RATE);
  const total = round2(subtotal + taxAmount);
  return { subtotal, taxAmount, total };
}

/**
 * ShortId for the restaurant row this seed creates.
 *
 * The canonical Acme restaurant keeps the exact ShortId the EF
 * migration seeded it with. Any other restaurant gets a deterministic
 * value derived from its GUID — `IX_Restaurants_ShortId` is UNIQUE,
 * so a second restaurant carrying `rst22222222` would be rejected.
 */
function restaurantShortId(restaurantId: string, seed: CatalogSeed): string {
  if (restaurantId === ACME_RESTAURANT_ID) return seed.restaurant.shortId;
  return `rst${restaurantBucket(restaurantId)}`;
}

/**
 * Resolves how many distinct line items a mock order gets.
 *
 * `itemCount` overrides the random roll; without it the count is rolled
 * from [MIN_ORDER_ITEMS, MAX_ORDER_ITEMS]. The roll ALWAYS happens — even
 * when `itemCount` is supplied — so the RNG stream (and therefore the
 * picked items) does not depend on whether the caller pinned the count.
 */
export function resolveItemCount(itemCount: number | undefined, rng: () => number): number {
  const rolled = randomInt(rng, MIN_ORDER_ITEMS, MAX_ORDER_ITEMS);
  return itemCount ?? rolled;
}

/**
 * Picks `count` distinct menu items from the catalog and rolls a
 * quantity for each — pure, deterministic given `rng`.
 *
 * A catalog item's `variations` are MUTUALLY EXCLUSIVE options (size,
 * doneness, with/without cheese) — not add-ons. Exactly ONE of them is
 * chosen per line, only that one travels on the line, and its
 * `priceDelta` is added to `basePrice` to produce `unitPrice`. The bill
 * therefore reflects what the customer actually chose.
 *
 * `customizations` are additive ("extra cilantro", "no onion") — all of
 * them travel on the line, at a zero price.
 */
export function selectOrderItems(catalog: CatalogSeed, count: number, rng: () => number): SelectedOrderItem[] {
  const items = catalog.items;
  const wanted = Math.max(0, Math.min(Math.trunc(count), items.length));

  // Partial Fisher-Yates — draws `wanted` distinct indexes without replacement.
  const indexes = items.map((_, i) => i);
  for (let i = 0; i < wanted; i++) {
    const j = i + randomInt(rng, 0, indexes.length - 1 - i);
    const tmp = indexes[i]!;
    indexes[i] = indexes[j]!;
    indexes[j] = tmp;
  }

  return indexes.slice(0, wanted).map((idx) => {
    const item = items[idx]!;
    const variations = item.variations ?? [];
    const chosen = variations.length > 0 ? variations[randomInt(rng, 0, variations.length - 1)]! : undefined;
    return {
      menuItemId: item.id,
      menuItemName: item.name,
      quantity: randomInt(rng, MIN_ITEM_QUANTITY, MAX_ITEM_QUANTITY),
      unitPrice: round2(item.basePrice + (chosen?.priceDelta ?? 0)),
      selectedVariations: chosen ? [{ name: chosen.name, price: chosen.priceDelta }] : [],
      customizations: (item.customizations ?? []).map((c) => ({ label: c.ingredientName, value: c.action, price: 0 })),
    };
  });
}

// ─── Serialisation contracts (pure) ──────────────────────────────────────

/**
 * The exact JSON written to the `OrderItems.SelectedVariations` column.
 *
 * PASCALCASE on purpose. The Ordering EF value converter
 * (`Ordering.Infrastructure/Data/Configurations/OrderItemConfiguration.cs`)
 * reads the column back with a bare
 * `JsonSerializer.Deserialize<List<T>>(json)` — NO
 * `PropertyNameCaseInsensitive` option — so it binds PASCALCASE only.
 * A camelCase payload deserialises to `Name = null, Price = 0` and the
 * variation silently disappears from the order.
 */
export function buildSelectedVariationsJson(item: SelectedOrderItem): string {
  return JSON.stringify((item.selectedVariations ?? []).map((v) => ({ Name: v.name, Price: v.price })));
}

/**
 * The exact JSON written to the `OrderItems.Customizations` column.
 * PascalCase for the same reason as `buildSelectedVariationsJson`.
 */
export function buildCustomizationsJson(item: SelectedOrderItem): string {
  return JSON.stringify((item.customizations ?? []).map((c) => ({ Label: c.label, Value: c.value, Price: c.price })));
}

/** One line of the RabbitMQ `OrderCreatedIntegrationEvent` envelope. */
export interface OrderCreatedEnvelopeItem {
  orderItemId: string;
  menuItemId: string;
  menuItemName: string;
  quantity: number;
  unitPrice: number;
  selectedVariations: Array<{ name: string; price: number }>;
  customizations: Array<{ label: string; value: string; price: number }>;
  specialInstructions: string;
  seatNumber: number;
}

/**
 * The `items` array of the RabbitMQ envelope for
 * `OrderCreatedIntegrationEvent` — camelCase on purpose.
 *
 * MassTransit serialises the envelope with its camelCase policy and the
 * Kitchen consumer binds camelCase (the live kitchen tickets prove the
 * camelCase envelope works). Do NOT "unify" this with the PascalCase DB
 * columns above — the two shapes are deliberately different.
 *
 * `newId` is injected so the payload is testable without a random UUID
 * generator; the tool passes `() => crypto.randomUUID()`.
 */
export function buildEnvelopeItems(
  orderItems: ReadonlyArray<SelectedOrderItem>,
  newId: () => string,
): OrderCreatedEnvelopeItem[] {
  return orderItems.map((it) => ({
    orderItemId: newId(),
    menuItemId: it.menuItemId,
    menuItemName: it.menuItemName,
    quantity: it.quantity,
    unitPrice: it.unitPrice,
    selectedVariations: it.selectedVariations?.map((v) => ({ name: v.name, price: v.price })) ?? [],
    customizations: it.customizations?.map((c) => ({ label: c.label, value: c.value, price: c.price })) ?? [],
    specialInstructions: '',
    seatNumber: 1,
  }));
}

// ─── Tool error shape + seed-read boundary (pure) ─────────────────────────

/** The tool-level failure payload every seeding tool returns. */
export function buildToolErrorPayload(error: string, cause: unknown): { error: string; cause: string } {
  return { error, cause: String(cause) };
}

/** Result of a seed read guarded by the tool error boundary. */
export type SeedBoundaryResult<T> =
  | { ok: true; value: T }
  | { ok: false; payload: { error: string; cause: string }; cause: unknown };

/**
 * Runs a seed read inside the tool's error boundary.
 *
 * A missing or corrupt seed must surface as the tool's documented
 * `{ error, cause }` shape, not as a raw ENOENT / SyntaxError.
 */
export function withSeedErrorBoundary<T>(read: () => T, errorLabel: string): SeedBoundaryResult<T> {
  try {
    return { ok: true, value: read() };
  } catch (cause) {
    return { ok: false, payload: buildToolErrorPayload(errorLabel, cause), cause };
  }
}

// ─── Mock-order planning (pure) ──────────────────────────────────────────

/** A line-item pick that is good to write. */
export interface MockOrderPlan {
  items: SelectedOrderItem[];
  pickSource: PickSource;
  requestedItemCount: number;
}

/** The refusal returned when the pick produced no line items at all. */
export interface EmptyOrderRefusal {
  error: 'cannot create an empty order';
  cause: string;
  restaurantId: string;
  requestedItemCount: number;
  pickSource: PickSource;
}

/** The outcome of planning a mock order. */
export type MockOrderDecision =
  | { refused: true; refusal: EmptyOrderRefusal }
  | { refused: false; plan: MockOrderPlan };

/**
 * Picks the line items for a mock order — pure, no I/O.
 *
 * The catalog is the source of truth; `order-seed.json`'s items are only
 * the fallback for an empty catalog. `pickSource` records which one was
 * used so the response never hides it.
 */
export function pickOrderItems(
  catalog: CatalogSeed,
  orderSeed: OrderSeed,
  wantedCount: number,
  rng: () => number,
): { items: SelectedOrderItem[]; pickSource: PickSource } {
  const pickSource: PickSource = catalog.items.length > 0 ? 'catalog' : 'fallback';
  const items =
    pickSource === 'catalog'
      ? selectOrderItems(catalog, wantedCount, rng)
      : orderSeed.orderItems.map((it) => ({ ...it }));
  return { items, pickSource };
}

/**
 * The refusal payload for a pick that produced no line items.
 *
 * An order with no lines has a zero subtotal, which the ordering domain
 * forbids ("Subtotal must be greater than zero"). Refuse instead of
 * writing a zero-total order the kitchen would act on.
 */
export function buildEmptyOrderRefusal(
  restaurantId: string,
  requestedItemCount: number,
  pickSource: PickSource,
): EmptyOrderRefusal {
  return {
    error: 'cannot create an empty order',
    cause:
      'The catalog seed holds no items and the order-seed fallback produced no line items either. An order without lines has a zero subtotal, which violates the ordering domain rule "Subtotal must be greater than zero".',
    restaurantId,
    requestedItemCount,
    pickSource,
  };
}

/**
 * Decides what `create_mock_order` will write — pure, no I/O.
 *
 * `requestedItemCount` is the UNCLAMPED number the caller asked for: a
 * count larger than the catalog is clamped by the picker and the
 * response echoes the original request, so the clamp is never silent.
 */
export function planMockOrder(
  catalog: CatalogSeed,
  orderSeed: OrderSeed,
  restaurantId: string,
  requestedItemCount: number,
  rng: () => number,
): MockOrderDecision {
  const { items, pickSource } = pickOrderItems(catalog, orderSeed, requestedItemCount, rng);
  if (items.length === 0) {
    return { refused: true, refusal: buildEmptyOrderRefusal(restaurantId, requestedItemCount, pickSource) };
  }
  return { refused: false, plan: { items, pickSource, requestedItemCount } };
}

/** The `create_mock_order` success payload. */
export interface OrderResponsePayload {
  orderId: string;
  shortId: string;
  restaurantId: string;
  status: string;
  orderType: string;
  subtotal: number;
  taxAmount: number;
  total: number;
  currency: 'MXN';
  orderNumber: string;
  eventPublished: boolean;
  kitchenMirrored: boolean;
  kitchenTicketId: string | null;
  requestedItemCount: number;
  pickSource: PickSource;
  itemCount: number;
  items: Array<{
    menuItemId: string;
    menuItemName: string;
    quantity: number;
    unitPrice: number;
    selectedVariations: Array<{ name: string; price: number }>;
  }>;
}

/**
 * The `create_mock_order` response payload — pure.
 *
 * `requestedItemCount` is the UNCLAMPED request, `itemCount` the number
 * of lines actually written: the two differ whenever the caller asked
 * for more items than the catalog holds.
 */
export function buildOrderResponse(params: {
  orderId: string;
  shortId: string;
  restaurantId: string;
  status: string;
  orderType: string;
  subtotal: number;
  taxAmount: number;
  total: number;
  orderNumber: string;
  eventPublished: boolean;
  kitchenMirrored: boolean;
  kitchenTicketId: string | null;
  requestedItemCount: number;
  pickSource: PickSource;
  items: ReadonlyArray<SelectedOrderItem>;
}): OrderResponsePayload {
  return {
    orderId: params.orderId,
    shortId: params.shortId,
    restaurantId: params.restaurantId,
    status: params.status,
    orderType: params.orderType,
    subtotal: params.subtotal,
    taxAmount: params.taxAmount,
    total: params.total,
    currency: 'MXN',
    orderNumber: params.orderNumber,
    eventPublished: params.eventPublished,
    kitchenMirrored: params.kitchenMirrored,
    kitchenTicketId: params.kitchenTicketId,
    requestedItemCount: params.requestedItemCount,
    pickSource: params.pickSource,
    itemCount: params.items.length,
    items: params.items.map((it) => ({
      menuItemId: it.menuItemId,
      menuItemName: it.menuItemName,
      quantity: it.quantity,
      unitPrice: it.unitPrice,
      selectedVariations: it.selectedVariations ?? [],
    })),
  };
}

export function buildSeedMenuSql(restaurantId: string, seed: CatalogSeed): { sql: string[]; summary: Record<string, number>; restaurantId: string; generatedAt: string } {
  const sql: string[] = [];
  const bucket = restaurantBucket(restaurantId);
  const now = nowMs().toISOString();

  // 1. Brand (idempotent via ON CONFLICT)
  sql.push(
    `INSERT INTO "Brands" ("Id", "ShortId", "Name", "CuisineType", "Description", "ContactEmail", "ContactPhone", "LogoUrl", "WebsiteUrl", "CreatedBy", "LastModifiedBy", "IsActive") ` +
    `VALUES ('${sqlText(seed.brand.id)}', '${sqlText(seed.brand.shortId)}', '${sqlText(seed.brand.name)}', ${seed.brand.cuisineType}, '${sqlText(seed.brand.description)}', '${sqlText(seed.brand.contactEmail)}', '${sqlText(seed.brand.contactPhone)}', '${sqlText(seed.brand.logoUrl)}', '${sqlText(seed.brand.websiteUrl)}', 'devmcp', 'devmcp', true) ` +
    `ON CONFLICT ("Id") DO UPDATE SET "Name" = EXCLUDED."Name", "LastModifiedAt" = CURRENT_TIMESTAMP`,
  );

  // 2. Restaurant (FK to Brand)
  // "TaxRate" is a FRACTION in Catalog (decimal(5,2); the Catalog API
  // validator caps it at 1) — ORDER_TAX_RATE is therefore written
  // as-is, exactly like the EF-seeded Acme restaurant does.
  sql.push(
    `INSERT INTO "Restaurants" ("Id", "ShortId", "Address", "AllowAutoSubstitute", "AutoConfirmOrders", "AutoConfirmReservations", "BrandId", "Currency", "Email", "EstimatedTurnoverMinutes", "Name", "PhoneNumber", "TaxRate", "TimeZone", "CreatedBy", "LastModifiedBy", "IsActive") ` +
    `VALUES ('${sqlText(restaurantId)}', '${sqlText(restaurantShortId(restaurantId, seed))}', 'devmcp-seed', false, true, true, '${sqlText(seed.brand.id)}', 'MXN', 'devmcp@test.local', 60, 'DevMCP Test Restaurant (${sqlText(bucket)})', '+52-81-0000-0000', ${ORDER_TAX_RATE}, 'America/Monterrey', 'devmcp', 'devmcp', true) ` +
    `ON CONFLICT ("Id") DO UPDATE SET "Name" = EXCLUDED."Name", "LastModifiedAt" = CURRENT_TIMESTAMP`,
  );

  // 3. MenuCategories (Id is integer)
  for (let idx = 0; idx < seed.categories.length; idx++) {
    const c = seed.categories[idx]!;
    const catId = idx + 1;
    sql.push(
      `INSERT INTO "MenuCategories" ("Id", "RestaurantId", "Name", "Description", "DisplayOrder", "IsDeleted", "CreatedBy", "LastModifiedBy", "IsActive") ` +
      `VALUES (${catId}, '${sqlText(restaurantId)}', '${sqlText(c.name)}', '${sqlText(c.description)}', ${c.displayOrder}, false, 'devmcp', 'devmcp', true) ` +
      `ON CONFLICT ("Id") DO UPDATE SET "Name" = EXCLUDED."Name", "LastModifiedAt" = CURRENT_TIMESTAMP`,
    );
  }

  // 4. MenuItems + variations + customizations
  for (const item of seed.items) {
    sql.push(
      `INSERT INTO "MenuItems" ("Id", "ShortId", "RestaurantId", "SubCategoryId", "Name", "Description", "BasePrice", "ImageUrl", "PrepTimeMinutes", "PrepTimeMaxMinutes", "AvailabilityStatus", "ItemType", "DisplayOrder", "IsAvailable", "IsDeleted", "CreatedBy", "LastModifiedBy", "IsActive") ` +
      `VALUES ('${sqlText(item.id)}', '${sqlText(item.shortId)}', '${sqlText(restaurantId)}', NULL, '${sqlText(item.name)}', '${sqlText(item.description)}', ${item.basePrice}, 'https://placehold.co/500x500/png?text=${sqlText(encodeURIComponent(item.name))}', ${item.prepTimeMinutes}, ${item.prepTimeMaxMinutes}, 0, 0, 0, true, false, 'devmcp', 'devmcp', true) ` +
      `ON CONFLICT ("Id") DO UPDATE SET "Name" = EXCLUDED."Name", "BasePrice" = EXCLUDED."BasePrice", "LastModifiedAt" = CURRENT_TIMESTAMP`,
    );

    // MenuItemVariations has no unique constraint on (MenuItemId, Name),
    // so a plain INSERT would duplicate rows on every re-run. Drop the
    // item's variations first, then re-insert the canonical set.
    //
    // Only for items that DECLARE variations: an item without any has
    // nothing to replace, and deleting rows out of band for it would
    // destroy an operator's hand-added variation for no reason.
    if ((item.variations ?? []).length > 0) {
      sql.push(
        `DELETE FROM "MenuItemVariations" WHERE "MenuItemId" = '${sqlText(item.id)}'`,
      );
    }

    for (const v of item.variations ?? []) {
      sql.push(
        `INSERT INTO "MenuItemVariations" ("MenuItemId", "Name", "VariationValue", "PriceModifier", "IsDefault", "DisplayOrder", "IsDeleted") ` +
        `VALUES ('${sqlText(item.id)}', '${sqlText(v.name)}', '${sqlText(v.name)}', ${v.priceDelta}, false, 0, false)`,
      );
    }

    for (const c of item.customizations ?? []) {
      sql.push(
        `INSERT INTO "Ingredients" ("RestaurantId", "Name", "Unit", "CurrentStock", "MinimumStock", "IsAvailable", "CreatedBy", "LastModifiedBy", "CreatedAt", "LastModifiedAt", "IsActive") ` +
        `VALUES ('${sqlText(restaurantId)}', '${sqlText(c.ingredientName)}', 'portion', 100.00, 10.00, true, 'devmcp', 'devmcp', CURRENT_TIMESTAMP, CURRENT_TIMESTAMP, true) ` +
        `ON CONFLICT ("RestaurantId", "Name") DO NOTHING`,
      );
      sql.push(
        `INSERT INTO "MenuItemIngredients" ("MenuItemId", "IngredientId", "IsOptional", "QuantityRequired") ` +
        `SELECT '${sqlText(item.id)}', "Id", true, 1.00 FROM "Ingredients" WHERE "RestaurantId" = '${sqlText(restaurantId)}' AND "Name" = '${sqlText(c.ingredientName)}' ` +
        `ON CONFLICT DO NOTHING`,
      );
    }
  }

  const summary = {
    brands: 1,
    restaurants: 1,
    categories: seed.categories.length,
    items: seed.items.length,
    variations: seed.items.reduce((n, i) => n + (i.variations?.length ?? 0), 0),
    customizations: seed.items.reduce((n, i) => n + (i.customizations?.length ?? 0), 0),
  };
  return { sql, summary, restaurantId, generatedAt: now };
}

export const ACME_RESTAURANT_ID = '22222222-2222-2222-2222-222222222222';

export interface DataSeedingDeps {
  logger: Logger;
  pg: ToolContext['pg'];
  mssql: ToolContext['mssql'];
  rabbit?: ToolContext['rabbit'];
}

export function registerDataSeedingTools(server: McpServer, deps: DataSeedingDeps): void {
  server.registerTool(
    'seed_test_menu',
    {
      title: 'Seed test menu',
      description:
        'Inserts the canonical test menu (5 categories, 25 items, variations, ingredient customizations) from `resources/seeds/catalog-seed.json` into Catalogdb. Idempotent — every seeded row carries an explicit unique `ShortId`, and re-running replaces the variations of the items that declare them instead of duplicating them. When `dryRun: true`, returns the SQL without executing.',
      inputSchema: {
        restaurantId: z.string().uuid().default(ACME_RESTAURANT_ID).describe('Restaurant ID to seed menu for. Defaults to Acme restaurant (22222222-2222-2222-2222-222222222222).'),
        dryRun: z.boolean().default(false).describe('Return SQL without executing.'),
      },
    },
    async (args) => {
      const { restaurantId, dryRun } = args as { restaurantId: string; dryRun: boolean };

      // The seed file read lives INSIDE the error boundary: a missing or
      // corrupt seed must surface as the tool's documented
      // `{ error, cause }` shape, not as a raw ENOENT / SyntaxError.
      const seeded = withSeedErrorBoundary(
        () => buildSeedMenuSql(restaurantId, readSeed<CatalogSeed>('catalog-seed.json')),
        'seed failed',
      );
      if (!seeded.ok) {
        deps.logger.error({ err: seeded.cause, restaurantId }, 'seed_test_menu failed — could not read the catalog seed');
        return {
          content: [{ type: 'text' as const, text: JSON.stringify(seeded.payload, null, 2) }],
          isError: true,
        };
      }
      const built = seeded.value;

      if (dryRun) {
        return {
          content: [
            {
              type: 'text' as const,
              text: JSON.stringify({ mode: 'dry-run', ...built }, null, 2),
            },
          ],
        };
      }

      const client = await deps.pg.catalog.connect();
      try {
        await client.query('BEGIN');
        for (const stmt of built.sql) {
          await client.query(stmt);
        }
        await client.query('COMMIT');
        deps.logger.info({ restaurantId, ...built.summary }, 'seeded test menu');
        return {
          content: [
            {
              type: 'text' as const,
              text: JSON.stringify({ mode: 'executed', ...built, summary: built.summary }, null, 2),
            },
          ],
        };
      } catch (cause) {
        await client.query('ROLLBACK').catch(() => undefined);
        deps.logger.error({ err: cause, restaurantId }, 'seed_test_menu failed — rolled back');
        return {
          content: [{ type: 'text' as const, text: JSON.stringify(buildToolErrorPayload('seed failed', cause), null, 2) }],
          isError: true,
        };
      } finally {
        client.release();
      }
    },
  );

  server.registerTool(
    'create_mock_order',
    {
      title: 'Create mock order',
      description:
        'Inserts a fake order with the given status into OrderDb Orders + OrderItems tables. Picks 1-12 distinct menu items from the 25-item catalog seed (`resources/seeds/catalog-seed.json`), rolling a quantity in [1, 3] per line. Each line carries AT MOST ONE of the item\'s mutually exclusive variations, and the chosen variation\'s price delta is added to the catalog base price — so `unitPrice` is what the customer actually pays. The count is random when `itemCount` is omitted and the whole pick is reproducible when `seed` is supplied. Publishes OrderCreatedIntegrationEvent so Kitchen generates a ticket and notifies KDS over SignalR; when the broker is unavailable the order is mirrored straight into Kitchendb instead. Defaults to Acme restaurant (22222222-2222-2222-2222-222222222222).',
      inputSchema: {
        restaurantId: z.string().uuid().default(ACME_RESTAURANT_ID).describe('Restaurant ID for the order. Defaults to Acme restaurant (22222222-2222-2222-2222-222222222222).'),
        status: z.enum(ORDER_STATUSES).default('Pending'),
        orderType: z.enum(ORDER_TYPES).default('DineIn'),
        itemCount: z.number().int().min(MIN_ORDER_ITEMS).max(MAX_ORDER_ITEMS).optional().describe('Number of distinct line items in the order (1-12). Random within that range when omitted. Clamped to the number of items the catalog actually holds.'),
        seed: z.string().optional().describe('PRNG seed. The same seed always picks the same items, quantities and variations. Random when omitted.'),
      },
    },
    async (args) => {
      const { restaurantId, status, orderType, itemCount, seed } = args as {
        restaurantId: string;
        status: typeof ORDER_STATUSES[number];
        orderType: typeof ORDER_TYPES[number];
        itemCount?: number;
        seed?: string;
      };

      // The seed file reads live INSIDE the error boundary: a missing or
      // corrupt seed must surface as the tool's documented
      // `{ error, cause }` shape, not as a raw ENOENT / SyntaxError.
      const seeded = withSeedErrorBoundary(
        () => ({
          orderSeed: readSeed<OrderSeed>('order-seed.json'),
          catalog: readSeed<CatalogSeed>('catalog-seed.json'),
        }),
        'create failed',
      );
      if (!seeded.ok) {
        deps.logger.error({ err: seeded.cause, restaurantId }, 'create_mock_order failed');
        return {
          content: [{ type: 'text' as const, text: JSON.stringify(seeded.payload, null, 2) }],
          isError: true,
        };
      }
      const { orderSeed, catalog } = seeded.value;

      // Seeded → reproducible pick; unseeded → a fresh CSPRNG seed per call.
      const rng = seed === undefined ? mulberry32(randomSeed()) : mulberry32(seedFromString(seed));
      const wantedCount = resolveItemCount(itemCount, rng);

      // The catalog is the source of truth; `order-seed.json`'s items are
      // only the fallback for an empty catalog. `planMockOrder` refuses an
      // empty pick: an order with no lines has a zero subtotal, which the
      // ordering domain forbids ("Subtotal must be greater than zero"), so
      // it must not be written — the kitchen would act on it.
      const decision = planMockOrder(catalog, orderSeed, restaurantId, wantedCount, rng);
      if (decision.refused) {
        deps.logger.error(
          { restaurantId, requestedItemCount: wantedCount, pickSource: decision.refusal.pickSource },
          'create_mock_order refused — the pick produced no line items',
        );
        return {
          content: [{ type: 'text' as const, text: JSON.stringify(decision.refusal, null, 2) }],
          isError: true,
        };
      }
      const { items: orderItems, pickSource } = decision.plan;

      try {
        const orderId = crypto.randomUUID();
        const shortId = generateShortId('ord');
        const now = nowMs();

        const transaction = new sql.Transaction(deps.mssql);
        await transaction.begin();
        try {
          // 1. Upsert Customer (idempotent on Id)
          const customerReq = new sql.Request(transaction);
          customerReq.input('id', orderSeed.customerId);
          customerReq.input('email', 'devmcp@test.local');
          customerReq.input('name', orderSeed.userName);
          customerReq.input('phone', '+52-81-0000-0000');
          customerReq.input('street', orderSeed.deliveryAddress.street);
          customerReq.input('city', orderSeed.deliveryAddress.city);
          customerReq.input('state', orderSeed.deliveryAddress.state);
          customerReq.input('zip', orderSeed.deliveryAddress.zipCode);
          customerReq.input('country', orderSeed.deliveryAddress.country);
          const devmcpGuid = '00000000-0000-0000-0000-000000000001';
          customerReq.input('createdBy', devmcpGuid);
          customerReq.input('lastModifiedBy', devmcpGuid);
          await customerReq.query(
            `IF NOT EXISTS (SELECT 1 FROM Customers WHERE Id = @id) ` +
            `INSERT INTO Customers (Id, Email, Name, Phone, Address_Street, Address_City, Address_State, Address_ZipCode, Address_Country, CreatedBy, CreatedAt, LastModifiedBy, LastModifiedAt, IsActive) ` +
            `VALUES (@id, @email, @name, @phone, @street, @city, @state, @zip, @country, @createdBy, GETUTCDATE(), @lastModifiedBy, GETUTCDATE(), 1)`,
          );

          // 2. Compute sequence and order number conforming to Ordering domain convention:
          // {CHANNEL}{YYYYMMDD}-{SEQUENCE:D4} (e.g. DIN20261024-0001)
          const channel = orderType === 'DineIn' ? 'DIN' : orderType === 'Takeout' ? 'TAK' : 'DEL';
          const businessDate = now.toISOString().slice(0, 10);
          const dateCompact = businessDate.replace(/-/g, '');

          const seqReq = new sql.Request(transaction);
          seqReq.input('restaurantId', restaurantId);
          seqReq.input('businessDate', businessDate);
          const seqResult = await seqReq.query<{ CurrentValue: number }>(
            `MERGE [RestaurantDailySequences] WITH (HOLDLOCK) AS target ` +
            `USING (SELECT @restaurantId AS RestaurantId, CAST(@businessDate AS DATE) AS BusinessDate) AS source ` +
            `ON (target.RestaurantId = source.RestaurantId AND target.BusinessDate = source.BusinessDate) ` +
            `WHEN MATCHED THEN UPDATE SET CurrentValue = target.CurrentValue + 1 ` +
            `WHEN NOT MATCHED THEN INSERT (RestaurantId, BusinessDate, CurrentValue) VALUES (source.RestaurantId, source.BusinessDate, 1) ` +
            `OUTPUT inserted.CurrentValue;`,
          );
          const sequence = seqResult.recordset[0]?.CurrentValue ?? 1;
          const orderNumber = `${channel}${dateCompact}-${sequence.toString().padStart(4, '0')}`;

          const { subtotal, taxAmount, total } = computeOrderTotals(orderItems);
          const taxRate = ORDER_TAX_RATE;

          const orderReq = new sql.Request(transaction);
          orderReq.input('id', orderId);
          orderReq.input('shortId', shortId);
          orderReq.input('customerId', orderSeed.customerId);
          orderReq.input('restaurantId', restaurantId);
          orderReq.input('orderNumber', orderNumber);
          orderReq.input('status', status);
          orderReq.input('orderType', orderType);
          orderReq.input('subtotal', subtotal);
          orderReq.input('taxAmount', taxAmount);
          orderReq.input('taxRate', taxRate);
          orderReq.input('total', total);
          orderReq.input('currency', 'MXN');
          // DeliveryAddress (ComplexProperty)
          orderReq.input('daStreet', orderSeed.deliveryAddress.street);
          orderReq.input('daCity', orderSeed.deliveryAddress.city);
          orderReq.input('daState', orderSeed.deliveryAddress.state);
          orderReq.input('daZip', orderSeed.deliveryAddress.zipCode);
          orderReq.input('daCountry', orderSeed.deliveryAddress.country);
          // BillingAddress (ComplexProperty)
          orderReq.input('baStreet', orderSeed.billingAddress.street);
          orderReq.input('baCity', orderSeed.billingAddress.city);
          orderReq.input('baState', orderSeed.billingAddress.state);
          orderReq.input('baZip', orderSeed.billingAddress.zipCode);
          orderReq.input('baCountry', orderSeed.billingAddress.country);
          // Payment (ComplexProperty)
          orderReq.input('payMethod', 1);
          orderReq.input('payBrand', 'Visa');
          orderReq.input('payLastFour', '1111');

          await orderReq.query(
            `INSERT INTO Orders (` +
              `Id, ShortId, CustomerId, RestaurantId, OrderNumber, Status, OrderType, ` +
              `Subtotal, TaxAmount, TaxRate, TotalAmount, Currency, ` +
              `DeliveryAddress_Street, DeliveryAddress_City, DeliveryAddress_State, DeliveryAddress_ZipCode, DeliveryAddress_Country, ` +
              `BillingAddress_Street, BillingAddress_City, BillingAddress_State, BillingAddress_ZipCode, BillingAddress_Country, ` +
              `Payment_Method, Payment_Brand, Payment_LastFour, ` +
              `ActualPrepTimeMinutes, EstimatedPrepTimeMinutes, DiscountAmount, DiscountCode, DeliveryNotes, Notes, IsModified, RequiresAdminApproval, ` +
              `CreatedByUserId, CreatedAt, LastModified, LastModifiedBy` +
            `) VALUES (` +
              `@id, @shortId, @customerId, @restaurantId, @orderNumber, @status, @orderType, ` +
              `@subtotal, @taxAmount, @taxRate, @total, @currency, ` +
              `@daStreet, @daCity, @daState, @daZip, @daCountry, ` +
              `@baStreet, @baCity, @baState, @baZip, @baCountry, ` +
              `@payMethod, @payBrand, @payLastFour, ` +
              `0, 20, 0, '', '', '', 0, 0, ` +
              `@customerId, GETUTCDATE(), GETUTCDATE(), '${devmcpGuid}'` +
            `)`,
          );

          // 3. Upsert MenuItems & Insert OrderItems
          for (const it of orderItems) {
            const menuReq = new sql.Request(transaction);
            menuReq.input('menuItemId', it.menuItemId);
            menuReq.input('menuItemName', it.menuItemName);
            menuReq.input('price', it.unitPrice);
            await menuReq.query(
              `IF NOT EXISTS (SELECT 1 FROM MenuItems WHERE Id = @menuItemId) ` +
              `INSERT INTO MenuItems (Id, Name, Price, CreatedAt, CreatedBy, LastModified, LastModifiedBy) ` +
              `VALUES (@menuItemId, @menuItemName, @price, GETUTCDATE(), '${devmcpGuid}', GETUTCDATE(), '${devmcpGuid}')`,
            );

            const itemReq = new sql.Request(transaction);
            itemReq.input('id', crypto.randomUUID());
            itemReq.input('orderId', orderId);
            itemReq.input('menuItemId', it.menuItemId);
            itemReq.input('menuItemName', it.menuItemName);
            itemReq.input('basePrice', it.unitPrice);
            itemReq.input('unitPrice', it.unitPrice);
            itemReq.input('totalPrice', it.unitPrice * it.quantity);
            itemReq.input('quantity', it.quantity);
            // SERIALISATION WARNING — the two payloads below are NOT the
            // same shape on purpose, do not "unify" them:
            //   * These two OrderDb columns are read back by the Ordering
            //     EF value converter (`OrderItemConfiguration.cs`), which
            //     uses a bare `JsonSerializer.Deserialize` with NO
            //     `PropertyNameCaseInsensitive` option. It therefore binds
            //     PASCALCASE only — a camelCase payload deserialises to
            //     `Name = null, Price = 0` and the variation silently
            //     disappears from the order.
            //   * The RabbitMQ envelope further down stays camelCase:
            //     MassTransit serialises it with its camelCase policy and
            //     the Kitchen consumer binds camelCase (the live kitchen
            //     tickets prove the camelCase envelope works).
            itemReq.input('selectedVariations', buildSelectedVariationsJson(it));
            itemReq.input('customizations', buildCustomizationsJson(it));
            itemReq.input('menuItemDescription', '');
            itemReq.input('menuItemImageUrl', '');
            itemReq.input('specialInstructions', '');
            itemReq.input('seatNumber', 0);
            itemReq.input('prepStatus', 'Pending');
            itemReq.input('createdBy', devmcpGuid);
            await itemReq.query(
              `INSERT INTO OrderItems (Id, OrderId, MenuItemId, MenuItemName, BasePrice, UnitPrice, TotalPrice, Quantity, SelectedVariations, Customizations, MenuItemDescription, MenuItemImageUrl, SpecialInstructions, SeatNumber, PrepStatus, CreatedAt, CreatedBy, LastModified, LastModifiedBy) ` +
              `VALUES (@id, @orderId, @menuItemId, @menuItemName, @basePrice, @unitPrice, @totalPrice, @quantity, @selectedVariations, @customizations, @menuItemDescription, @menuItemImageUrl, @specialInstructions, @seatNumber, @prepStatus, GETUTCDATE(), @createdBy, GETUTCDATE(), @createdBy)`,
            );
          }

          await transaction.commit();
          deps.logger.info({ orderId, orderNumber, restaurantId, status, total, itemCount: orderItems.length }, 'created mock order in OrderDb');

          // 4. Publish OrderCreatedIntegrationEvent so Kitchen service creates KitchenTicket & notifies KDS via SignalR
          let eventPublished = false;
          if (deps.rabbit?.channel) {
            try {
              const exchange = 'BuildingBlocks.Messaging.Events:OrderCreatedIntegrationEvent';
              const envelope = {
                messageId: crypto.randomUUID(),
                conversationId: crypto.randomUUID(),
                sourceAddress: 'loopback://localhost/devmcp',
                destinationAddress: `loopback://localhost/${exchange}`,
                messageType: [`urn:message:${exchange}`],
                message: {
                  orderId,
                  orderNumber,
                  restaurantId,
                  tableId: null,
                  orderType: orderType === 'DineIn' ? 0 : orderType === 'Takeout' ? 1 : 2,
                  customerId: orderSeed.customerId,
                  subtotal,
                  totalAmount: total,
                  taxAmount,
                  discountAmount: 0,
                  currency: 'MXN',
                  discountCode: null,
                  billingAddress: {
                    street: orderSeed.billingAddress.street,
                    city: orderSeed.billingAddress.city,
                    state: orderSeed.billingAddress.state,
                    zipCode: orderSeed.billingAddress.zipCode,
                    country: orderSeed.billingAddress.country,
                  },
                  deliveryAddress: orderSeed.deliveryAddress
                    ? {
                        street: orderSeed.deliveryAddress.street,
                        city: orderSeed.deliveryAddress.city,
                        state: orderSeed.deliveryAddress.state,
                        zipCode: orderSeed.deliveryAddress.zipCode,
                        country: orderSeed.deliveryAddress.country,
                      }
                    : null,
                  items: buildEnvelopeItems(orderItems, () => crypto.randomUUID()),
                  estimatedPrepTimeMinutes: 20,
                  notes: 'Table 1 · Created via DevMCP',
                },
                sentTime: new Date().toISOString(),
                headers: {},
              };
              const body = Buffer.from(JSON.stringify(envelope), 'utf-8');
              deps.rabbit.channel.publish(exchange, '', body, {
                contentType: 'application/vnd.masstransit+json',
                persistent: true,
              });
              eventPublished = true;
              deps.logger.info({ exchange, orderId, orderNumber, restaurantId }, 'published OrderCreatedIntegrationEvent to RabbitMQ');
            } catch (pubErr) {
              deps.logger.warn({ err: pubErr, orderId, orderNumber, restaurantId }, 'failed to publish OrderCreatedIntegrationEvent to RabbitMQ');
            }
          }

          // Fallback: If event was not published to RabbitMQ (e.g. broker offline) and kitchen DB is present, mirror directly.
          // The ticket + its items go in ONE transaction: a half-written
          // ticket would be a partially prepared order the kitchen acts on.
          let kitchenMirrored = false;
          let kitchenTicketId: string | null = null;
          if (!eventPublished && deps.pg?.kitchen) {
            try {
              const kitchenClient = await deps.pg.kitchen.connect();
              try {
                await kitchenClient.query('BEGIN');
                await kitchenClient.query(
                  `INSERT INTO "kitchen_tickets" (
                    "Id", "RestaurantId", "CustomerId", "OrderNumber", "Status",
                    "ReceivedAt", "Notes"
                  ) VALUES (
                    $1, $2, $3, $4, 0,
                    NOW(), $5
                  ) ON CONFLICT ("Id") DO NOTHING`,
                  [orderId, restaurantId, orderSeed.customerId, orderNumber, 'Table 1 · DevMCP Order'],
                );

                for (const it of orderItems) {
                  await kitchenClient.query(
                    `INSERT INTO "kitchen_ticket_items" (
                      "Id", "KitchenTicketId", "OrderItemId", "MenuItemId", "MenuItemName",
                      "Quantity", "UnitPrice", "SelectedVariations", "Customizations",
                      "SpecialInstructions", "SeatNumber", "Status"
                    ) VALUES (
                      $1, $2, $3, $4, $5,
                      $6, $7, $8, $9,
                      $10, 1, 0
                    )`,
                    [
                      crypto.randomUUID(),
                      orderId,
                      crypto.randomUUID(),
                      it.menuItemId,
                      it.menuItemName,
                      it.quantity,
                      it.unitPrice,
                      it.selectedVariations?.map((v) => v.name) ?? [],
                      it.customizations?.map((c) => `${c.label}: ${c.value}`) ?? [],
                      'Created via DevMCP fallback',
                    ],
                  );
                }
                await kitchenClient.query('COMMIT');
                kitchenMirrored = true;
                kitchenTicketId = orderId;
                deps.logger.info({ orderId, orderNumber, restaurantId, itemCount: orderItems.length }, 'mirrored order directly to kitchen_tickets (fallback)');
              } catch (mirrorErr) {
                await kitchenClient.query('ROLLBACK').catch(() => undefined);
                throw mirrorErr;
              } finally {
                kitchenClient.release();
              }
            } catch (mirrorErr) {
              deps.logger.warn({ err: mirrorErr, orderId, orderNumber, restaurantId }, 'failed to mirror order to kitchen_tickets');
            }
          }

          return {
            content: [
              {
                type: 'text' as const,
                text: JSON.stringify(
                  buildOrderResponse({
                    orderId,
                    shortId,
                    restaurantId,
                    status,
                    orderType,
                    subtotal,
                    taxAmount,
                    total,
                    orderNumber,
                    eventPublished,
                    kitchenMirrored,
                    kitchenTicketId,
                    // The number the caller asked for — it can be larger
                    // than the catalog holds, in which case the pick is
                    // clamped. Echoed so the clamp is never silent.
                    requestedItemCount: wantedCount,
                    // Where the lines came from — 'fallback' means the
                    // catalog seed was empty.
                    pickSource,
                    items: orderItems,
                  }),
                  null,
                  2,
                ),
              },
            ],
          };
        } catch (cause) {
          await transaction.rollback().catch(() => undefined);
          throw cause;
        }
      } catch (cause) {
        deps.logger.error({ err: cause, restaurantId }, 'create_mock_order failed');
        return {
          content: [{ type: 'text' as const, text: JSON.stringify(buildToolErrorPayload('create failed', cause), null, 2) }],
          isError: true,
        };
      }
    },
  );
}
