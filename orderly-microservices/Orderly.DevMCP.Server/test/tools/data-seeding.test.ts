/**
 * Unit tests for the data-seeding tools.
 *
 * Pure tests — no live database, no MCP_LIVE_TEST gate. They cover the
 * deterministic item picker used by `create_mock_order`, the money
 * arithmetic, the shape of the canonical catalog seed and the SQL the
 * seed builder emits.
 *
 * Run: `pnpm test`
 */

import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, resolve } from 'node:path';

import {
  ACME_RESTAURANT_ID,
  buildCustomizationsJson,
  buildEnvelopeItems,
  buildOrderResponse,
  buildSelectedVariationsJson,
  buildSeedMenuSql,
  computeOrderTotals,
  pickOrderItems,
  planMockOrder,
  resolveItemCount,
  selectOrderItems,
  sqlText,
  withSeedErrorBoundary,
  MAX_ITEM_QUANTITY,
  MAX_ORDER_ITEMS,
  MIN_ITEM_QUANTITY,
  MIN_ORDER_ITEMS,
  ORDER_TAX_RATE,
  type CatalogSeed,
  type OrderSeed,
  type SelectedOrderItem,
} from '../../src/tools/data-seeding.ts';
import { mulberry32, randomInt, seedFromString } from '../../src/util/random.ts';

const __dirname = dirname(fileURLToPath(import.meta.url));
const SEEDS_DIR = resolve(__dirname, '../../resources/seeds');

const catalog = JSON.parse(
  readFileSync(resolve(SEEDS_DIR, 'catalog-seed.json'), 'utf-8'),
) as CatalogSeed;

const orderSeed = JSON.parse(
  readFileSync(resolve(SEEDS_DIR, 'order-seed.json'), 'utf-8'),
) as OrderSeed;

function rngFor(seed: string): () => number {
  return mulberry32(seedFromString(seed));
}

function pickKey(items: SelectedOrderItem[]): string {
  return items.map((i) => `${i.menuItemId}#${i.quantity}`).join('|');
}

// ─── The catalog seed itself ───────────────────────────────────────────────

test('catalog seed has 5 categories and 25 items', () => {
  assert.equal(catalog.categories.length, 5);
  assert.equal(catalog.items.length, 25);
});

test('every catalog item carries a unique, non-empty shortId', () => {
  const shortIds = catalog.items.map((i) => i.shortId);
  assert.ok(shortIds.every((s) => typeof s === 'string' && s.length > 0), 'every item needs a shortId');
  assert.equal(new Set(shortIds).size, shortIds.length, 'shortIds must be unique');
  assert.equal(new Set(catalog.items.map((i) => i.id)).size, catalog.items.length, 'item GUIDs must be unique');
});

test('brand and restaurant shortIds are the canonical values', () => {
  assert.equal(catalog.brand.shortId, 'brd11111111');
  assert.equal(catalog.restaurant.shortId, 'rst22222222');
});

test('the 11 pre-existing items kept their live shortIds', () => {
  const expected: Record<string, string> = {
    'Chips & Guac': 'itm00000001',
    'Queso Fundido': 'itm00000002',
    'Shrimp Ceviche': 'itm00000003',
    'Carne Asada Tacos (3)': 'itm00000004',
    'Chicken Enchiladas': 'itm00000005',
    'Veggie Bowl': 'itm00000006',
    'Grilled Salmon': 'itm00000007',
    "Burger 'Orderly'": 'itm00000008',
    Flan: 'itm00000009',
    Churros: 'itm00000010',
    'Tres Leches': 'itm00000011',
  };
  for (const [name, shortId] of Object.entries(expected)) {
    const item = catalog.items.find((i) => i.name === name);
    assert.ok(item, `item "${name}" must exist in the catalog seed`);
    assert.equal(item.shortId, shortId, `item "${name}" must keep shortId ${shortId}`);
  }
});

// ─── selectOrderItems ──────────────────────────────────────────────────────

test('the selected count stays within [1, 12] and never exceeds the catalog size', () => {
  for (let requested = 1; requested <= MAX_ORDER_ITEMS; requested++) {
    for (let sample = 0; sample < 25; sample++) {
      const picked = selectOrderItems(catalog, requested, rngFor(`count-${requested}-${sample}`));
      assert.ok(picked.length >= MIN_ORDER_ITEMS, `expected at least ${MIN_ORDER_ITEMS} items`);
      assert.ok(picked.length <= MAX_ORDER_ITEMS, `expected at most ${MAX_ORDER_ITEMS} items`);
      assert.ok(picked.length <= catalog.items.length, 'never more than the catalog holds');
      assert.equal(picked.length, Math.min(requested, catalog.items.length));
    }
  }
});

test('a count larger than the catalog is clamped to the catalog size', () => {
  const picked = selectOrderItems(catalog, 999, rngFor('clamp'));
  assert.equal(picked.length, catalog.items.length);
});

test('the same seed produces the same item ids, quantities and variations', () => {
  for (const seed of ['alpha', 'beta', 'gamma', 'delta']) {
    const a = selectOrderItems(catalog, 7, rngFor(seed));
    const b = selectOrderItems(catalog, 7, rngFor(seed));
    assert.deepEqual(
      a.map((i) => i.menuItemId),
      b.map((i) => i.menuItemId),
      `seed "${seed}" must pick the same items`,
    );
    assert.deepEqual(
      a.map((i) => i.quantity),
      b.map((i) => i.quantity),
      `seed "${seed}" must roll the same quantities`,
    );
    assert.deepEqual(
      a.map((i) => i.selectedVariations),
      b.map((i) => i.selectedVariations),
      `seed "${seed}" must choose the same variations`,
    );
    assert.deepEqual(
      a.map((i) => i.unitPrice),
      b.map((i) => i.unitPrice),
      `seed "${seed}" must produce the same line prices`,
    );
  }
});

test('different seeds produce different picks at least sometimes', () => {
  // The PRNG is fully deterministic, so this is not a flaky assertion —
  // it either always holds or never does. Over 200 seeds the number of
  // distinct picks must be well above one.
  const picks = new Set<string>();
  for (let i = 0; i < 200; i++) {
    picks.add(pickKey(selectOrderItems(catalog, 6, rngFor(`sample-${i}`))));
  }
  assert.ok(picks.size > 1, `expected more than one distinct pick, got ${picks.size}`);
  assert.ok(picks.size > 100, `expected a wide spread of picks, got ${picks.size}`);
});

test('every picked menuItemId exists in the catalog seed', () => {
  const known = new Set(catalog.items.map((i) => i.id));
  for (let i = 0; i < 50; i++) {
    const picked = selectOrderItems(catalog, MAX_ORDER_ITEMS, rngFor(`known-${i}`));
    for (const item of picked) {
      assert.ok(known.has(item.menuItemId), `picked item ${item.menuItemId} is not in the catalog seed`);
    }
  }
});

test('every picked item is distinct', () => {
  const picked = selectOrderItems(catalog, MAX_ORDER_ITEMS, rngFor('distinct'));
  const ids = picked.map((i) => i.menuItemId);
  assert.equal(new Set(ids).size, ids.length, 'line items must be distinct menu items');
});

test('a line carries AT MOST ONE variation and its price delta reaches the line price', () => {
  for (let i = 0; i < 200; i++) {
    const picked = selectOrderItems(catalog, MAX_ORDER_ITEMS, rngFor(`variation-${i}`));
    for (const line of picked) {
      const source = catalog.items.find((c) => c.id === line.menuItemId);
      assert.ok(source, 'picked item must be in the catalog');
      const declared = source.variations ?? [];
      const carried = line.selectedVariations ?? [];

      assert.ok(carried.length <= 1, 'a line carries at most one variation');
      if (declared.length === 0) {
        assert.equal(carried.length, 0, 'an item that declares no variations must carry none');
        assert.equal(line.unitPrice, source.basePrice, 'without a variation the price is the base price');
        continue;
      }

      assert.equal(carried.length, 1, 'an item that declares variations must carry exactly one');
      const chosen = declared.find((v) => v.name === carried[0]!.name);
      assert.ok(chosen, `variation "${carried[0]!.name}" must belong to "${source.name}"`);
      assert.equal(carried[0]!.price, chosen.priceDelta, 'the variation price is its price delta');
      assert.equal(
        line.unitPrice,
        source.basePrice + chosen.priceDelta,
        'the line price must be basePrice + the chosen variation price delta',
      );
    }
  }
});

test('every declared customization is carried through to the line item', () => {
  const picked = selectOrderItems(catalog, MAX_ORDER_ITEMS, rngFor('customizations'));
  for (const line of picked) {
    const source = catalog.items.find((c) => c.id === line.menuItemId)!;
    assert.equal(line.customizations?.length ?? 0, source.customizations?.length ?? 0);
    for (const [i, c] of (source.customizations ?? []).entries()) {
      assert.deepEqual(line.customizations![i], { label: c.ingredientName, value: c.action, price: 0 });
    }
  }
});

test('quantities are within [MIN_ITEM_QUANTITY, MAX_ITEM_QUANTITY]', () => {
  for (let i = 0; i < 100; i++) {
    const picked = selectOrderItems(catalog, MAX_ORDER_ITEMS, rngFor(`qty-${i}`));
    for (const item of picked) {
      assert.ok(Number.isInteger(item.quantity), 'quantity must be an integer');
      assert.ok(
        item.quantity >= MIN_ITEM_QUANTITY && item.quantity <= MAX_ITEM_QUANTITY,
        `quantity ${item.quantity} out of range`,
      );
    }
  }
});

// ─── The order count ───────────────────────────────────────────────────────

test('the itemCount override wins and stays within [1, 12]', () => {
  for (let n = MIN_ORDER_ITEMS; n <= MAX_ORDER_ITEMS; n++) {
    const count = resolveItemCount(n, rngFor(`fixed-${n}`));
    assert.equal(count, n, 'the caller-supplied count must win');
    assert.ok(count >= MIN_ORDER_ITEMS && count <= MAX_ORDER_ITEMS);
  }
});

test('the random roll stays within [1, 12]', () => {
  for (let i = 0; i < 500; i++) {
    const count = resolveItemCount(undefined, rngFor(`roll-${i}`));
    assert.ok(Number.isInteger(count), 'the rolled count must be an integer');
    assert.ok(count >= MIN_ORDER_ITEMS, `rolled count ${count} below ${MIN_ORDER_ITEMS}`);
    assert.ok(count <= MAX_ORDER_ITEMS, `rolled count ${count} above ${MAX_ORDER_ITEMS}`);
  }
});

// ─── computeOrderTotals ────────────────────────────────────────────────────

test('computeOrderTotals returns the hand-computed subtotal, tax and total', () => {
  // 2 x 100 + 1 x 50 = 250 subtotal; 250 x 16% = 40 tax; 290 total.
  assert.deepEqual(
    computeOrderTotals([
      { unitPrice: 100, quantity: 2 },
      { unitPrice: 50, quantity: 1 },
    ]),
    { subtotal: 250, taxAmount: 40, total: 290 },
  );
});

test('computeOrderTotals rounds to the two decimals of the money columns', () => {
  // 3 x 95.50 = 286.50 and 2 x 12.25 = 24.50 → 311.00 subtotal;
  // 311 x 16% = 49.76 tax; 360.76 total.
  assert.deepEqual(
    computeOrderTotals([
      { unitPrice: 95.5, quantity: 3 },
      { unitPrice: 12.25, quantity: 2 },
    ]),
    { subtotal: 311, taxAmount: 49.76, total: 360.76 },
  );
});

test('computeOrderTotals of an empty line list is all zeroes', () => {
  assert.deepEqual(computeOrderTotals([]), { subtotal: 0, taxAmount: 0, total: 0 });
});

test('the effective tax rate is exactly 16%', () => {
  assert.equal(ORDER_TAX_RATE, 0.16, 'the shared tax constant must stay at 16%');
  const { subtotal, taxAmount, total } = computeOrderTotals([{ unitPrice: 100, quantity: 1 }]);
  assert.equal(taxAmount, 16, 'a 100.00 subtotal must be taxed 16.00');
  assert.equal(total, 116);
});

// ─── buildSeedMenuSql ──────────────────────────────────────────────────────

const built = buildSeedMenuSql(ACME_RESTAURANT_ID, catalog);
const emitted = built.sql;

function insertsFor(table: string): string[] {
  return emitted.filter((stmt) => stmt.startsWith(`INSERT INTO "${table}"`));
}

test('the emitted SQL writes "ShortId" on Brands, Restaurants and MenuItems', () => {
  for (const table of ['Brands', 'Restaurants', 'MenuItems']) {
    const inserts = insertsFor(table);
    assert.ok(inserts.length > 0, `expected at least one INSERT for ${table}`);
    for (const stmt of inserts) {
      const columnList = stmt.slice(0, stmt.indexOf('VALUES'));
      assert.ok(columnList.includes('"ShortId"'), `the ${table} INSERT must list the "ShortId" column`);
    }
  }
});

test('every catalog shortId reaches the emitted SQL', () => {
  const all = emitted.join('\n');
  for (const item of catalog.items) {
    assert.ok(all.includes(item.shortId), `the emitted SQL must carry item shortId ${item.shortId}`);
  }
  assert.ok(all.includes(catalog.brand.shortId), 'the emitted SQL must carry the brand shortId');
  assert.ok(all.includes(catalog.restaurant.shortId), 'the emitted SQL must carry the restaurant shortId');
});

test('the MenuItemVariations cleanup is emitted only for items that declare variations', () => {
  const withVariations = catalog.items.filter((i) => (i.variations?.length ?? 0) > 0);
  const withoutVariations = catalog.items.filter((i) => (i.variations?.length ?? 0) === 0);
  assert.ok(withoutVariations.length > 0, 'the catalog must contain items without variations');
  assert.ok(withVariations.length > 0, 'the catalog must contain items with variations');

  const deletes = emitted.filter((stmt) => stmt.startsWith('DELETE FROM "MenuItemVariations"'));
  assert.equal(deletes.length, withVariations.length, 'one cleanup per item that declares variations');

  for (const stmt of deletes) {
    const id = /WHERE "MenuItemId" = '([^']*)'/.exec(stmt)?.[1];
    assert.ok(id, `the cleanup statement must name a menu item: ${stmt}`);
    const item = catalog.items.find((i) => i.id === id);
    assert.ok(item, `the cleanup statement must target a seeded item: ${id}`);
    assert.ok(
      (item.variations?.length ?? 0) > 0,
      `item "${item.name}" declares no variations — it must not be cleaned up`,
    );
  }
});

test('the seeded restaurant TaxRate derives from the shared tax constant', () => {
  const stmt = insertsFor('Restaurants')[0];
  assert.ok(stmt, 'expected a Restaurants INSERT');
  assert.ok(stmt.includes('"TaxRate"'), 'the Restaurants INSERT must carry the TaxRate column');
  assert.ok(
    stmt.includes(`, ${ORDER_TAX_RATE},`),
    'the TaxRate column must carry the shared ORDER_TAX_RATE value',
  );
});

test('every interpolated string value is escaped through sqlText', () => {
  assert.equal(sqlText("Burger 'Orderly'"), "Burger ''Orderly''");

  const all = emitted.join('\n');
  for (const item of catalog.items) {
    assert.ok(
      all.includes(`'${sqlText(item.name)}'`),
      `the item name must be escaped: ${item.name}`,
    );
  }
  // The catalog item "Burger 'Orderly'" carries an apostrophe — it must
  // reach the SQL as a doubled quote, never as a bare one.
  assert.ok(all.includes("'Burger ''Orderly'''"), 'the apostrophe must be doubled');
  assert.ok(!all.includes("'Burger 'Orderly'"), 'a bare apostrophe must never reach the SQL');
});

// ─── PRNG helpers ──────────────────────────────────────────────────────────

test('randomInt stays inside the inclusive range', () => {
  const rng = rngFor('int-range');
  for (let i = 0; i < 1000; i++) {
    const n = randomInt(rng, 1, 3);
    assert.ok(n >= 1 && n <= 3, `${n} out of range`);
    assert.ok(Number.isInteger(n));
  }
  assert.equal(randomInt(rng, 5, 5), 5);
  assert.equal(randomInt(rng, 9, 2), 9, 'an inverted range falls back to the minimum');
});

test('mulberry32 is reproducible and stays inside [0, 1)', () => {
  const a = mulberry32(seedFromString('repeat'));
  const b = mulberry32(seedFromString('repeat'));
  for (let i = 0; i < 100; i++) {
    const v = a();
    assert.equal(v, b());
    assert.ok(v >= 0 && v < 1);
  }
});

// ─── Serialisation contracts (REL-003) ────────────────────────────────────
//
// The two payloads are DELIBERATELY different shapes:
//   * the OrderDb columns are PASCALCASE — the Ordering EF value
//     converter (`OrderItemConfiguration.cs`) deserialises them with a
//     bare, case-sensitive `JsonSerializer.Deserialize<List<T>>`, so a
//     camelCase payload binds `Name = null, Price = 0` and the
//     variation silently disappears from the order;
//   * the RabbitMQ envelope stays camelCase — MassTransit serialises it
//     with its camelCase policy and the Kitchen consumer binds camelCase.

const lineWithEverything: SelectedOrderItem = {
  menuItemId: 'a0000002-0000-0000-0000-000000000001',
  menuItemName: 'Carne Asada Tacos (3)',
  quantity: 1,
  unitPrice: 245,
  selectedVariations: [{ name: 'Spicy', price: 10 }],
  customizations: [{ label: 'Cilantro', value: 'extra', price: 0 }],
};

test('the SelectedVariations column payload is exactly PASCALCASE JSON', () => {
  assert.equal(
    buildSelectedVariationsJson(lineWithEverything),
    '[{"Name":"Spicy","Price":10}]',
  );
});

test('the Customizations column payload is exactly PASCALCASE JSON', () => {
  assert.equal(
    buildCustomizationsJson(lineWithEverything),
    '[{"Label":"Cilantro","Value":"extra","Price":0}]',
  );
});

test('the column payloads never carry camelCase keys', () => {
  const variations = buildSelectedVariationsJson(lineWithEverything);
  const customizations = buildCustomizationsJson(lineWithEverything);

  assert.ok(!variations.includes('"name"'), 'SelectedVariations must not carry a camelCase "name"');
  assert.ok(!variations.includes('"price"'), 'SelectedVariations must not carry a camelCase "price"');
  assert.ok(!customizations.includes('"label"'), 'Customizations must not carry a camelCase "label"');
  assert.ok(!customizations.includes('"value"'), 'Customizations must not carry a camelCase "value"');
  assert.ok(!customizations.includes('"price"'), 'Customizations must not carry a camelCase "price"');
});

test('the column payloads bind to the EF converter property names', () => {
  // `JsonSerializer.Deserialize<List<T>>` with NO
  // `PropertyNameCaseInsensitive` — a key that does not match the C#
  // property name binds to null / 0 without raising.
  const bound = JSON.parse(buildSelectedVariationsJson(lineWithEverything)) as Array<{ Name: string; Price: number }>;
  assert.equal(bound[0]!.Name, 'Spicy');
  assert.equal(bound[0]!.Price, 10);

  const boundCustomizations = JSON.parse(buildCustomizationsJson(lineWithEverything)) as Array<{
    Label: string;
    Value: string;
    Price: number;
  }>;
  assert.equal(boundCustomizations[0]!.Label, 'Cilantro');
  assert.equal(boundCustomizations[0]!.Value, 'extra');
  assert.equal(boundCustomizations[0]!.Price, 0);
});

test('a line without variations or customizations serialises to an empty array', () => {
  const bare: SelectedOrderItem = { menuItemId: 'm-1', menuItemName: 'Plain', quantity: 1, unitPrice: 50 };
  assert.equal(buildSelectedVariationsJson(bare), '[]');
  assert.equal(buildCustomizationsJson(bare), '[]');
});

test('the RabbitMQ envelope payload stays camelCase', () => {
  const items = buildEnvelopeItems([lineWithEverything], () => 'fixed-order-item-id');

  assert.equal(items.length, 1);
  assert.equal(items[0]!.orderItemId, 'fixed-order-item-id');
  assert.deepEqual(items[0]!.selectedVariations, [{ name: 'Spicy', price: 10 }]);
  assert.deepEqual(items[0]!.customizations, [{ label: 'Cilantro', value: 'extra', price: 0 }]);

  const json = JSON.stringify(items);
  assert.ok(json.includes('"name":"Spicy"'), 'the envelope must carry a camelCase "name"');
  assert.ok(json.includes('"price":10'), 'the envelope must carry a camelCase "price"');
  assert.ok(json.includes('"label":"Cilantro"'), 'the envelope must carry a camelCase "label"');
  assert.ok(json.includes('"value":"extra"'), 'the envelope must carry a camelCase "value"');
  assert.ok(!json.includes('"Name"'), 'the envelope must NOT be PascalCase');
  assert.ok(!json.includes('"Label"'), 'the envelope must NOT be PascalCase');
});

test('the envelope and the DB columns are deliberately different shapes', () => {
  // Nobody may "unify" the two serialisations: the DB column needs
  // PascalCase, the envelope needs camelCase.
  const envelope = buildEnvelopeItems([lineWithEverything], () => 'id');
  const column = JSON.parse(buildSelectedVariationsJson(lineWithEverything)) as Array<Record<string, unknown>>;

  assert.notDeepEqual(
    Object.keys(envelope[0]!.selectedVariations[0]!),
    Object.keys(column[0]!),
    'the envelope variation keys and the column variation keys must stay different',
  );
});

// ─── pickOrderItems / planMockOrder ───────────────────────────────────────

const emptyCatalog: CatalogSeed = { ...catalog, items: [] };

test('the pick source is "catalog" when the catalog holds items', () => {
  const { items, pickSource } = pickOrderItems(catalog, orderSeed, 3, rngFor('pick-catalog'));
  assert.equal(pickSource, 'catalog');
  assert.equal(items.length, 3);
});

test('the pick source is "fallback" when the catalog is empty', () => {
  const { items, pickSource } = pickOrderItems(emptyCatalog, orderSeed, 3, rngFor('pick-fallback'));
  assert.equal(pickSource, 'fallback');
  assert.equal(items.length, orderSeed.orderItems.length);
  assert.deepEqual(
    items.map((i) => i.menuItemId),
    orderSeed.orderItems.map((i) => i.menuItemId),
  );
});

test('an empty pick is refused instead of writing a zero-subtotal order', () => {
  const emptyOrderSeed: OrderSeed = { ...orderSeed, orderItems: [] };
  const decision = planMockOrder(emptyCatalog, emptyOrderSeed, ACME_RESTAURANT_ID, 3, rngFor('refuse'));

  assert.equal(decision.refused, true, 'the empty pick must be refused');
  if (!decision.refused) throw new Error('expected the empty pick to be refused');
  assert.equal(decision.refusal.error, 'cannot create an empty order');
  assert.match(decision.refusal.cause, /Subtotal must be greater than zero/);
  assert.equal(decision.refusal.pickSource, 'fallback');
  assert.equal(decision.refusal.requestedItemCount, 3);
  assert.equal(decision.refusal.restaurantId, ACME_RESTAURANT_ID);
});

test('a non-empty pick is planned with the catalog source', () => {
  const decision = planMockOrder(catalog, orderSeed, ACME_RESTAURANT_ID, 4, rngFor('planned'));
  assert.equal(decision.refused, false);
  if (decision.refused) throw new Error('expected the pick to be planned');
  assert.equal(decision.plan.pickSource, 'catalog');
  assert.equal(decision.plan.items.length, 4);
  assert.equal(decision.plan.requestedItemCount, 4);
});

test('a requested count larger than the catalog is clamped but the request is echoed', () => {
  const requested = 999;
  const decision = planMockOrder(catalog, orderSeed, ACME_RESTAURANT_ID, requested, rngFor('clamp-request'));

  assert.equal(decision.refused, false);
  if (decision.refused) throw new Error('expected the pick to be planned');
  assert.equal(decision.plan.items.length, catalog.items.length, 'the pick is clamped to the catalog size');
  assert.equal(decision.plan.requestedItemCount, requested, 'the unclamped request is echoed');
});

test('the response echoes the unclamped request next to the clamped line count', () => {
  const payload = buildOrderResponse({
    orderId: 'o-1',
    shortId: 'ord00000001',
    restaurantId: ACME_RESTAURANT_ID,
    status: 'Pending',
    orderType: 'DineIn',
    subtotal: 100,
    taxAmount: 16,
    total: 116,
    orderNumber: 'DIN20261024-0001',
    eventPublished: true,
    kitchenMirrored: false,
    kitchenTicketId: null,
    requestedItemCount: 999,
    pickSource: 'catalog',
    items: [{ menuItemId: 'm-1', menuItemName: 'Chips', quantity: 2, unitPrice: 50 }],
  });

  assert.equal(payload.requestedItemCount, 999, 'the request must be echoed unclamped');
  assert.equal(payload.itemCount, 1, 'the line count is what was actually written');
  assert.equal(payload.currency, 'MXN');
  assert.equal(payload.pickSource, 'catalog');
  assert.equal(payload.kitchenMirrored, false);
  assert.equal(payload.items[0]!.menuItemName, 'Chips');
});

// ─── The tool error boundary ──────────────────────────────────────────────

test('a missing seed file maps to the tool error shape', () => {
  const result = withSeedErrorBoundary(() => {
    throw new Error("ENOENT: no such file or directory, open 'catalog-seed.json'");
  }, 'create failed');

  assert.equal(result.ok, false);
  if (result.ok) throw new Error('expected the seed read to fail');
  assert.equal(result.payload.error, 'create failed');
  assert.match(result.payload.cause, /ENOENT/);
});

test('a corrupt seed file maps to the tool error shape', () => {
  const result = withSeedErrorBoundary(() => JSON.parse('{ not json'), 'create failed');

  assert.equal(result.ok, false);
  if (result.ok) throw new Error('expected the seed read to fail');
  assert.equal(result.payload.error, 'create failed');
  assert.match(result.payload.cause, /SyntaxError/);
});

test('a successful seed read returns the parsed value', () => {
  const result = withSeedErrorBoundary(() => 42, 'create failed');
  assert.equal(result.ok, true);
  if (!result.ok) throw new Error('expected the seed read to succeed');
  assert.equal(result.value, 42);
});
