/**
 * tools/data-seeding.ts (§6.2).
 *
 * Two tools:
 *   - seed_test_menu(restaurantId, dryRun?) — inserts the canonical
 *     menu from `resources/seeds/catalog-seed.json` into Catalogdb.
 *     Idempotent (uses ON CONFLICT … DO UPDATE). Supports `dryRun`
 *     to return the SQL without executing.
 *   - create_mock_order(restaurantId, status?) — inserts a fake order
 *     into OrderDb Orders + OrderItems + Customers. OrderAddress is
 *     a ComplexProperty on Orders (not a separate table — the plan §6.2
 *     was wrong on this; corrected here).
 *
 * Notes on the corrections:
 *   - OrderStatus enum (BuildingBlocks/Enums/OrderEnums.cs) is
 *     { Ordering, Pending, Confirmed, Preparing, Ready, Delivered,
 *     Completed, Cancelled, OnHold }, NOT the plan's { Pending,
 *     Processing, Completed, Cancelled }.
 *   - DeliveryAddress + BillingAddress are owned ComplexProperty columns
 *     (DeliveryAddress_Street, …), not a separate OrderAddresses table.
 *
 * §10.1 — restaurantId is sanitised to a sha256 bucket before any
 * interpolation into seed string artifacts.
 */

import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, resolve } from 'node:path';
import { z } from 'zod';
import type { McpServer } from '@modelcontextprotocol/sdk/server/mcp.js';

import type { Logger } from '../logger.ts';
import type { ToolContext } from './types.ts';
import { restaurantBucket } from '../util/sanitize.ts';

const __dirname = dirname(fileURLToPath(import.meta.url));
const SEEDS_DIR = resolve(__dirname, '../../resources/seeds');

const ORDER_STATUSES = ['Ordering', 'Pending', 'Confirmed', 'Preparing', 'Ready', 'Delivered', 'Completed', 'Cancelled', 'OnHold'] as const;
const ORDER_TYPES = ['DineIn', 'Takeout', 'Delivery'] as const;

interface CatalogSeed {
  brand: { id: string; name: string; cuisineType: number; description: string; contactEmail: string; contactPhone: string; logoUrl: string; websiteUrl: string };
  categories: Array<{ id: string; name: string; description: string; displayOrder: number }>;
  items: Array<{
    id: string;
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

interface OrderSeed {
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

function readSeed<T>(name: string): T {
  return JSON.parse(readFileSync(resolve(SEEDS_DIR, name), 'utf-8')) as T;
}

function nowMs(): Date { return new Date(); }

function buildSeedMenuSql(restaurantId: string, seed: CatalogSeed): { sql: string[]; summary: Record<string, number>; restaurantId: string; generatedAt: string } {
  const sql: string[] = [];
  const bucket = restaurantBucket(restaurantId);
  const now = nowMs().toISOString();

  // 1. Brand (idempotent via ON CONFLICT)
  sql.push(
    `INSERT INTO "Brands" ("Id", "Name", "CuisineType", "Description", "ContactEmail", "ContactPhone", "LogoUrl", "WebsiteUrl", "CreatedBy", "LastModifiedBy", "IsActive") ` +
    `VALUES ('${seed.brand.id}', '${seed.brand.name.replace(/'/g, "''")}', ${seed.brand.cuisineType}, '${seed.brand.description.replace(/'/g, "''")}', '${seed.brand.contactEmail}', '${seed.brand.contactPhone}', '${seed.brand.logoUrl}', '${seed.brand.websiteUrl}', 'devmcp', 'devmcp', true) ` +
    `ON CONFLICT ("Id") DO UPDATE SET "Name" = EXCLUDED."Name", "LastModifiedAt" = CURRENT_TIMESTAMP`,
  );

  // 2. Restaurant (FK to Brand)
  sql.push(
    `INSERT INTO "Restaurants" ("Id", "Address", "AllowAutoSubstitute", "AutoConfirmOrders", "AutoConfirmReservations", "BrandId", "Currency", "Email", "EstimatedTurnoverMinutes", "Name", "PhoneNumber", "TaxRate", "TimeZone", "CreatedBy", "LastModifiedBy", "IsActive") ` +
    `VALUES ('${restaurantId}', 'devmcp-seed', false, true, true, '${seed.brand.id}', 'MXN', 'devmcp@test.local', 60, 'DevMCP Test Restaurant (${bucket})', '+52-81-0000-0000', 16.00, 'America/Monterrey', 'devmcp', 'devmcp', true) ` +
    `ON CONFLICT ("Id") DO UPDATE SET "Name" = EXCLUDED."Name", "LastModifiedAt" = CURRENT_TIMESTAMP`,
  );

  // 3. MenuCategories
  for (const c of seed.categories) {
    sql.push(
      `INSERT INTO "MenuCategories" ("Id", "RestaurantId", "Name", "Description", "DisplayOrder", "IsDeleted", "CreatedBy", "LastModifiedBy", "IsActive") ` +
      `VALUES ('${c.id}', '${restaurantId}', '${c.name.replace(/'/g, "''")}', '${c.description.replace(/'/g, "''")}', ${c.displayOrder}, false, 'devmcp', 'devmcp', true) ` +
      `ON CONFLICT ("Id") DO UPDATE SET "Name" = EXCLUDED."Name", "LastModifiedAt" = CURRENT_TIMESTAMP`,
    );
  }

  // 4. MenuItems + variations + customizations
  for (const item of seed.items) {
    sql.push(
      `INSERT INTO "MenuItems" ("Id", "RestaurantId", "SubCategoryId", "Name", "Description", "BasePrice", "ImageUrl", "PrepTimeMinutes", "PrepTimeMaxMinutes", "AvailabilityStatus", "ItemType", "DisplayOrder", "IsAvailable", "IsDeleted", "CreatedBy", "LastModifiedBy", "IsActive") ` +
      `VALUES ('${item.id}', '${restaurantId}', NULL, '${item.name.replace(/'/g, "''")}', '${item.description.replace(/'/g, "''")}', ${item.basePrice}, 'https://placehold.co/500x500/png?text=${encodeURIComponent(item.name)}', ${item.prepTimeMinutes}, ${item.prepTimeMaxMinutes}, 0, 0, 0, true, false, 'devmcp', 'devmcp', true) ` +
      `ON CONFLICT ("Id") DO UPDATE SET "Name" = EXCLUDED."Name", "BasePrice" = EXCLUDED."BasePrice", "LastModifiedAt" = CURRENT_TIMESTAMP`,
    );

    for (const v of item.variations ?? []) {
      sql.push(
        `INSERT INTO "MenuItemVariations" ("Id", "MenuItemId", "Name", "PriceDelta", "IsDeleted", "CreatedBy", "LastModifiedBy", "IsActive") ` +
        `VALUES ('${v.id}', '${item.id}', '${v.name.replace(/'/g, "''")}', ${v.priceDelta}, false, 'devmcp', 'devmcp', true) ` +
        `ON CONFLICT ("Id") DO UPDATE SET "Name" = EXCLUDED."Name", "PriceDelta" = EXCLUDED."PriceDelta"`,
      );
    }

    for (const c of item.customizations ?? []) {
      // Customizations are ingredient toggles. Upsert the ingredient first.
      const ingId = `i-${c.ingredientName.toLowerCase().replace(/[^a-z0-9]/g, '')}-${bucket}`;
      sql.push(
        `INSERT INTO "Ingredients" ("Id", "RestaurantId", "Name", "IsAvailable", "IsDeleted", "CreatedBy", "LastModifiedBy", "IsActive") ` +
        `VALUES ('${ingId}', '${restaurantId}', '${c.ingredientName.replace(/'/g, "''")}', true, false, 'devmcp', 'devmcp', true) ` +
        `ON CONFLICT ("Id") DO NOTHING`,
      );
      sql.push(
        `INSERT INTO "MenuItemIngredients" ("Id", "MenuItemId", "IngredientId", "Action", "CreatedBy", "LastModifiedBy", "IsActive") ` +
        `VALUES (gen_random_uuid(), '${item.id}', '${ingId}', '${c.action.replace(/'/g, "''")}', 'devmcp', 'devmcp', true) ` +
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

export interface DataSeedingDeps {
  logger: Logger;
  pg: ToolContext['pg'];
}

export function registerDataSeedingTools(server: McpServer, deps: DataSeedingDeps): void {
  server.registerTool(
    'seed_test_menu',
    {
      title: 'Seed test menu',
      description:
        'Inserts the canonical test menu (3 categories, 11 items, variations, ingredient customizations) from `resources/seeds/catalog-seed.json` into Catalogdb. Idempotent. When `dryRun: true`, returns the SQL without executing.',
      inputSchema: {
        restaurantId: z.string().uuid(),
        dryRun: z.boolean().default(false).describe('Return SQL without executing.'),
      },
    },
    async (args) => {
      const { restaurantId, dryRun } = args as { restaurantId: string; dryRun: boolean };
      const seed = readSeed<CatalogSeed>('catalog-seed.json');
      const built = buildSeedMenuSql(restaurantId, seed);

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
          content: [{ type: 'text' as const, text: JSON.stringify({ error: 'seed failed', cause: String(cause) }, null, 2) }],
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
        'Inserts a fake order with the given status into OrderDb Orders + OrderItems tables. Returns the new OrderId. OrderAddress is stored as ComplexProperty columns on the Orders row (DeliveryAddress_*, BillingAddress_*) — there is no OrderAddresses table.',
      inputSchema: {
        restaurantId: z.string().uuid(),
        status: z.enum(ORDER_STATUSES).default('Pending'),
        orderType: z.enum(ORDER_TYPES).default('DineIn'),
      },
    },
    async (args) => {
      const { restaurantId, status, orderType } = args as { restaurantId: string; status: typeof ORDER_STATUSES[number]; orderType: typeof ORDER_TYPES[number] };
      const seed = readSeed<OrderSeed>('order-seed.json');
      const orderId = crypto.randomUUID();
      const now = nowMs();

      try {
        const transaction = new mssql.Transaction(await (deps as unknown as { mssql: { pool: import('mssql').ConnectionPool } }).mssql.pool.connect());
        await transaction.begin();
        try {
          // 1. Upsert Customer (idempotent on Id)
          const customerReq = new mssql.Request(transaction);
          customerReq.input('id', seed.customerId);
          customerReq.input('email', 'devmcp@test.local');
          customerReq.input('name', seed.userName);
          customerReq.input('phone', '+52-81-0000-0000');
          customerReq.input('street', seed.deliveryAddress.street);
          customerReq.input('city', seed.deliveryAddress.city);
          customerReq.input('state', seed.deliveryAddress.state);
          customerReq.input('zip', seed.deliveryAddress.zipCode);
          customerReq.input('country', seed.deliveryAddress.country);
          customerReq.input('createdBy', 'devmcp');
          customerReq.input('lastModifiedBy', 'devmcp');
          await customerReq.query(
            `IF NOT EXISTS (SELECT 1 FROM Customers WHERE Id = @id) ` +
            `INSERT INTO Customers (Id, Email, Name, Phone, Address_Street, Address_City, Address_State, Address_ZipCode, Address_Country, CreatedBy, CreatedAt, LastModifiedBy, LastModifiedAt, IsActive) ` +
            `VALUES (@id, @email, @name, @phone, @street, @city, @state, @zip, @country, @createdBy, GETUTCDATE(), @lastModifiedBy, GETUTCDATE(), 1)`,
          );

          // 2. Insert Order with ComplexProperty address + payment columns
          const subtotal = seed.orderItems.reduce((s, it) => s + it.unitPrice * it.quantity, 0);
          const taxRate = 0.16;
          const taxAmount = subtotal * taxRate;
          const total = subtotal + taxAmount;
          const orderNumber = `ORD-${Date.now()}-${restaurantId.slice(0, 8)}`;

          const orderReq = new mssql.Request(transaction);
          orderReq.input('id', orderId);
          orderReq.input('customerId', seed.customerId);
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
          orderReq.input('daStreet', seed.deliveryAddress.street);
          orderReq.input('daCity', seed.deliveryAddress.city);
          orderReq.input('daState', seed.deliveryAddress.state);
          orderReq.input('daZip', seed.deliveryAddress.zipCode);
          orderReq.input('daCountry', seed.deliveryAddress.country);
          // BillingAddress (ComplexProperty)
          orderReq.input('baStreet', seed.billingAddress.street);
          orderReq.input('baCity', seed.billingAddress.city);
          orderReq.input('baState', seed.billingAddress.state);
          orderReq.input('baZip', seed.billingAddress.zipCode);
          orderReq.input('baCountry', seed.billingAddress.country);
          // Payment (ComplexProperty)
          orderReq.input('payCardName', seed.payment.cardName);
          orderReq.input('payCardNumber', seed.payment.cardNumber);
          orderReq.input('payExpiration', seed.payment.expiration);
          orderReq.input('payCcv', seed.payment.ccv);
          orderReq.input('payMethod', seed.payment.paymentMethod);

          await orderReq.query(
            `INSERT INTO Orders (` +
              `Id, CustomerId, RestaurantId, OrderNumber, Status, OrderType, ` +
              `Subtotal, TaxAmount, TaxRate, TotalAmount, Currency, ` +
              `DeliveryAddress_Street, DeliveryAddress_City, DeliveryAddress_State, DeliveryAddress_ZipCode, DeliveryAddress_Country, ` +
              `BillingAddress_Street, BillingAddress_City, BillingAddress_State, BillingAddress_ZipCode, BillingAddress_Country, ` +
              `Payment_CardName, Payment_CardNumber, Payment_Expiration, Payment_Ccv, Payment_PaymentMethod, ` +
              `ActualPrepTimeMinutes, DiscountAmount, DiscountCode, DeliveryNotes, Notes, IsModified, RequiresAdminApproval, ` +
              `CreatedByUserId, CreatedAt, LastModified, LastModifiedBy` +
            `) VALUES (` +
              `@id, @customerId, @restaurantId, @orderNumber, @status, @orderType, ` +
              `@subtotal, @taxAmount, @taxRate, @total, @currency, ` +
              `@daStreet, @daCity, @daState, @daZip, @daCountry, ` +
              `@baStreet, @baCity, @baState, @baZip, @baCountry, ` +
              `@payCardName, @payCardNumber, @payExpiration, @payCcv, @payMethod, ` +
              `0, 0, '', '', '', 0, 0, ` +
              `@customerId, GETUTCDATE(), GETUTCDATE(), 'devmcp'` +
            `)`,
          );

          // 3. Insert OrderItems
          for (const it of seed.orderItems) {
            const itemReq = new mssql.Request(transaction);
            itemReq.input('id', crypto.randomUUID());
            itemReq.input('orderId', orderId);
            itemReq.input('menuItemId', it.menuItemId);
            itemReq.input('menuItemName', it.menuItemName);
            itemReq.input('basePrice', it.unitPrice);
            itemReq.input('unitPrice', it.unitPrice);
            itemReq.input('totalPrice', it.unitPrice * it.quantity);
            itemReq.input('quantity', it.quantity);
            itemReq.input('selectedVariations', JSON.stringify(it.selectedVariations ?? []));
            itemReq.input('customizations', JSON.stringify(it.customizations ?? []));
            itemReq.input('menuItemDescription', '');
            itemReq.input('menuItemImageUrl', '');
            itemReq.input('specialInstructions', '');
            itemReq.input('seatNumber', 0);
            itemReq.input('prepStatus', 'Pending');
            itemReq.input('createdBy', 'devmcp');
            await itemReq.query(
              `INSERT INTO OrderItems (Id, OrderId, MenuItemId, MenuItemName, BasePrice, UnitPrice, TotalPrice, Quantity, SelectedVariations, Customizations, MenuItemDescription, MenuItemImageUrl, SpecialInstructions, SeatNumber, PrepStatus, CreatedAt, CreatedBy, LastModified, LastModifiedBy) ` +
              `VALUES (@id, @orderId, @menuItemId, @menuItemName, @basePrice, @unitPrice, @totalPrice, @quantity, @selectedVariations, @customizations, @menuItemDescription, @menuItemImageUrl, @specialInstructions, @seatNumber, @prepStatus, GETUTCDATE(), @createdBy, GETUTCDATE(), @createdBy)`,
            );
          }

          await transaction.commit();
          deps.logger.info({ orderId, restaurantId, status, total }, 'created mock order');
          return {
            content: [
              {
                type: 'text' as const,
                text: JSON.stringify({ orderId, restaurantId, status, orderType, subtotal, taxAmount, total, currency: 'MXN', orderNumber }, null, 2),
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
          content: [{ type: 'text' as const, text: JSON.stringify({ error: 'create failed', cause: String(cause) }, null, 2) }],
          isError: true,
        };
      }
    },
  );
}

// Late import to avoid a top-level mssql cycle. The mssql types are
// referenced via dynamic require-equivalent in the create_mock_order
// handler above; this import only types the symbol.
import * as mssql from 'mssql';
void mssql;
