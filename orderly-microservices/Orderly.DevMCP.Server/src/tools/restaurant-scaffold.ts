/**
 * tools/restaurant-scaffold.ts
 *
 * One-shot composite restaurant scaffolding tool:
 *   - scaffold_restaurant(name, preset, tableCount, createStaff, ...)
 * Provisions:
 *   1. Brand + Restaurant with unique ShortIds in Catalogdb
 *   2. Complete floorplan of N tables with coordinates
 *   3. Complete menu hierarchy with categories, items, coursing, and variations from bundled preset
 *   4. Standard staff accounts (Manager, KitchenManager, Waiter, Cashier) via IdentityApiClient
 *   5. Automatic Redis cache eviction
 */

import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, resolve } from 'node:path';
import { randomUUID } from 'node:crypto';
import { z } from 'zod';
import type { McpServer } from '@modelcontextprotocol/sdk/server/mcp.js';
import type { Redis } from 'ioredis';

import type { Logger } from '../logger.ts';
import type { ToolContext } from './types.ts';
import { generateShortId } from '../util/short-id.ts';
import { invalidateCatalogCache } from '../util/cache-invalidation.ts';
import { IdentityApiClient } from '../util/identity-api-client.ts';
import { buildToolErrorPayload } from './data-seeding.ts';

const __dirname = dirname(fileURLToPath(import.meta.url));
const PRESETS_DIR = resolve(__dirname, '../../resources/seeds/presets');

interface PresetVariation {
  name: string;
  variationValue: string;
  priceModifier: number;
  isDefault?: boolean;
}

interface PresetItem {
  name: string;
  categoryName: string;
  description: string;
  basePrice: number;
  prepTimeMinutes: number;
  defaultCourseNumber?: number;
  imageUrl?: string;
  variations?: PresetVariation[];
}

interface PresetFile {
  name: string;
  categories: Array<{ name: string; displayOrder: number }>;
  items: PresetItem[];
}

export interface RestaurantScaffoldDeps {
  logger: Logger;
  pg: ToolContext['pg'];
  redis: Redis;
}

export function registerRestaurantScaffoldTools(server: McpServer, deps: RestaurantScaffoldDeps): void {
  const identityClient = new IdentityApiClient(deps.logger);

  server.registerTool(
    'scaffold_restaurant',
    {
      title: 'Scaffold complete restaurant with floorplan, menu and staff',
      description:
        'One-shot high-level provisioning tool: creates a complete, ready-to-test restaurant with a Brand, Restaurant, ' +
        'table floorplan, full menu hierarchy with variations and kitchen coursing from an archetype preset (bistro, pizzeria, taqueria, minimal), ' +
        'and initial staff accounts (Manager, KitchenManager, Waiter, Cashier) with Password123!.',
      inputSchema: {
        name: z.string().min(1).max(255).describe('Name of the restaurant'),
        preset: z.enum(['bistro', 'pizzeria', 'taqueria', 'minimal']).default('bistro').describe('Menu archetype preset to load'),
        tableCount: z.number().int().min(1).max(50).default(12).describe('Number of dining tables to generate in the floorplan'),
        createStaff: z.boolean().default(true).describe('Whether to provision initial staff accounts via Identity.API'),
        taxRate: z.number().min(0).max(1).default(0.16).describe('Tax rate fraction (default 0.16 = 16%)'),
        currency: z.string().length(3).default('MXN').describe('Currency code (e.g. MXN, USD)'),
        timeZone: z.string().default('America/Monterrey').describe('IANA timezone'),
      },
    },
    async (args) => {
      const input = args as {
        name: string;
        preset: 'bistro' | 'pizzeria' | 'taqueria' | 'minimal';
        tableCount: number;
        createStaff: boolean;
        taxRate: number;
        currency: string;
        timeZone: string;
      };

      const restaurantId = randomUUID();
      const restaurantShortId = generateShortId('rst', 8);
      const brandId = randomUUID();
      const brandShortId = generateShortId('brd', 8);

      const client = await deps.pg.catalog.connect();
      try {
        await client.query('BEGIN');

        // 1. Create Brand
        await client.query(
          `INSERT INTO "Brands" (
            "Id", "ShortId", "Name", "CuisineType", "Description",
            "ContactEmail", "ContactPhone", "LogoUrl", "WebsiteUrl",
            "CreatedBy", "LastModifiedBy", "IsActive"
          ) VALUES ($1, $2, $3, 0, $4, $5, '+52-81-0000-0000', $6, 'https://orderly.local', 'devmcp', 'devmcp', true)`,
          [
            brandId,
            brandShortId,
            `${input.name} Brand`,
            `Brand for ${input.name}`,
            `contact@${restaurantShortId}.orderly.local`,
            'https://images.unsplash.com/photo-1517248135467-4c7edcad34c4',
          ],
        );

        // 2. Create Restaurant
        await client.query(
          `INSERT INTO "Restaurants" (
            "Id", "ShortId", "BrandId", "Name", "Address", "PhoneNumber", "Email",
            "Currency", "TimeZone", "TaxRate", "EstimatedTurnoverMinutes",
            "AutoConfirmOrders", "AutoConfirmReservations", "AllowAutoSubstitute",
            "CreatedBy", "LastModifiedBy", "IsActive"
          ) VALUES (
            $1, $2, $3, $4, 'Av. Revolución 1234, Monterrey, NL', '+52-81-0000-0000', $5,
            $6, $7, $8, 45,
            true, true, true,
            'devmcp', 'devmcp', true
          )`,
          [
            restaurantId,
            restaurantShortId,
            brandId,
            input.name,
            `contact@${restaurantShortId}.orderly.local`,
            input.currency,
            input.timeZone,
            input.taxRate,
          ],
        );

        // 3. Create Tables (Floorplan grid)
        const tablesCreated: Array<{ id: string; shortId: string; tableNumber: string; capacity: number }> = [];
        for (let i = 1; i <= input.tableCount; i++) {
          const tId = randomUUID();
          const tShortId = generateShortId('tbl', 8);
          const tNum = `T${i < 10 ? '0' : ''}${i}`;
          // Mixed capacities: 2, 4, 6
          const capacity = i % 4 === 0 ? 6 : i % 2 === 0 ? 4 : 2;
          const col = (i - 1) % 4;
          const row = Math.floor((i - 1) / 4);

          await client.query(
            `INSERT INTO "Tables" (
              "Id", "ShortId", "RestaurantId", "TableNumber", "Capacity",
              "PositionX", "PositionY", "Shape", "Status", "IsActive",
              "CreatedBy", "LastModifiedBy"
            ) VALUES ($1, $2, $3, $4, $5, $6, $7, 'rectangle', 0, true, 'devmcp', 'devmcp')`,
            [tId, tShortId, restaurantId, tNum, capacity, col * 120, row * 120],
          );

          tablesCreated.push({ id: tId, shortId: tShortId, tableNumber: tNum, capacity });
        }

        // 4. Menu Presets
        let presetData: PresetFile;
        if (input.preset === 'minimal') {
          presetData = {
            name: 'Minimal Archetype',
            categories: [
              { name: 'Featured Items', displayOrder: 1 },
              { name: 'Beverages', displayOrder: 2 },
            ],
            items: [
              {
                name: 'House Special Platter',
                categoryName: 'Featured Items',
                description: 'Chef daily selection served with house sides.',
                basePrice: 195.00,
                prepTimeMinutes: 15,
                defaultCourseNumber: 2,
                imageUrl: 'https://images.unsplash.com/photo-1546069901-ba9599a7e63c',
                variations: [
                  { name: 'Portion', variationValue: 'Regular', priceModifier: 0.00, isDefault: true },
                  { name: 'Portion', variationValue: 'Grande', priceModifier: 60.00, isDefault: false },
                ],
              },
              {
                name: 'Artisan Lemonade',
                categoryName: 'Beverages',
                description: 'Fresh pressed lemons with sparkling water and mint.',
                basePrice: 55.00,
                prepTimeMinutes: 4,
                defaultCourseNumber: 1,
                imageUrl: 'https://images.unsplash.com/photo-1513558161293-cdaf765ed2fd',
                variations: [],
              },
            ],
          };
        } else {
          const filePath = resolve(PRESETS_DIR, `${input.preset}.json`);
          presetData = JSON.parse(readFileSync(filePath, 'utf-8')) as PresetFile;
        }

        const categoryIdMap = new Map<string, number>();
        for (const cat of presetData.categories) {
          const catRes = await client.query(
            `INSERT INTO "MenuCategories" ("RestaurantId", "Name", "DisplayOrder", "IsActive", "IsDeleted", "CreatedBy", "LastModifiedBy")
             VALUES ($1, $2, $3, true, false, 'devmcp', 'devmcp')
             RETURNING "Id"`,
            [restaurantId, cat.name, cat.displayOrder],
          );
          categoryIdMap.set(cat.name, (catRes.rows[0] as { Id: number }).Id);
        }

        let itemsCount = 0;
        let variationsCount = 0;

        for (const itm of presetData.items) {
          const itemId = randomUUID();
          const itemShortId = generateShortId('itm', 8);
          const defaultImage = itm.imageUrl ?? 'https://images.unsplash.com/photo-1546069901-ba9599a7e63c';

          await client.query(
            `INSERT INTO "MenuItems" (
               "Id", "ShortId", "RestaurantId", "SubCategoryId", "Name", "Description",
               "BasePrice", "ImageUrl", "PrepTimeMinutes", "default_course_number",
               "ItemType", "AvailabilityStatus", "IsAvailable", "IsActive", "IsDeleted",
               "DisplayOrder", "CreatedBy", "LastModifiedBy"
             ) VALUES (
               $1, $2, $3, NULL, $4, $5,
               $6, $7, $8, $9,
               'Regular', 'Available', true, true, false,
               0, 'devmcp', 'devmcp'
             )`,
            [
              itemId,
              itemShortId,
              restaurantId,
              itm.name,
              itm.description,
              itm.basePrice,
              defaultImage,
              itm.prepTimeMinutes,
              itm.defaultCourseNumber ?? 1,
            ],
          );
          itemsCount++;

          if (itm.variations && itm.variations.length > 0) {
            for (let vIdx = 0; vIdx < itm.variations.length; vIdx++) {
              const v = itm.variations[vIdx]!;
              await client.query(
                `INSERT INTO "MenuItemVariations" (
                   "MenuItemId", "Name", "VariationValue", "PriceModifier", "IsDefault", "DisplayOrder", "IsDeleted", "CreatedBy", "LastModifiedBy"
                 ) VALUES ($1, $2, $3, $4, $5, $6, false, 'devmcp', 'devmcp')`,
                [itemId, v.name, v.variationValue, v.priceModifier, v.isDefault ?? false, vIdx],
              );
              variationsCount++;
            }
          }
        }

        await client.query('COMMIT');

        // 5. Invalidate Redis
        await invalidateCatalogCache(deps.redis, restaurantId, deps.logger);

        // 6. Provision Staff via Identity API Bridge
        const staffAccounts: Array<{ role: string; email: string; userId?: string; error?: string }> = [];

        if (input.createStaff) {
          const rolesToCreate = [
            { role: 'Manager', prefix: 'manager', first: 'General', last: 'Manager' },
            { role: 'KitchenManager', prefix: 'kitchen', first: 'Head', last: 'Chef' },
            { role: 'Waiter', prefix: 'waiter', first: 'Floor', last: 'Server' },
            { role: 'Cashier', prefix: 'cashier', first: 'Front', last: 'Cashier' },
          ];

          for (const s of rolesToCreate) {
            const email = `${restaurantShortId}.${s.prefix}@orderly.local`;
            try {
              const res = await identityClient.createUser({
                email,
                password: 'Password123!',
                firstName: s.first,
                lastName: s.last,
                roles: [s.role],
                restaurantIds: [restaurantId],
                defaultRestaurantId: restaurantId,
              });
              staffAccounts.push({ role: s.role, email, userId: res.userId });
            } catch (staffErr) {
              deps.logger.warn({ staffErr, email, role: s.role }, 'could not auto-provision staff user');
              staffAccounts.push({ role: s.role, email, error: String(staffErr) });
            }
          }
        }

        return {
          content: [{
            type: 'text' as const,
            text: JSON.stringify({
              success: true,
              restaurant: {
                id: restaurantId,
                shortId: restaurantShortId,
                name: input.name,
                currency: input.currency,
                timeZone: input.timeZone,
                taxRate: input.taxRate,
              },
              brand: {
                id: brandId,
                shortId: brandShortId,
              },
              floorplan: {
                tablesCount: tablesCreated.length,
                tables: tablesCreated,
              },
              menu: {
                preset: input.preset,
                categoriesCount: presetData.categories.length,
                itemsCount,
                variationsCount,
              },
              staff: {
                defaultPassword: 'Password123!',
                accounts: staffAccounts,
              },
            }, null, 2),
          }],
        };
      } catch (cause) {
        await client.query('ROLLBACK').catch(() => undefined);
        deps.logger.error({ err: cause, restaurantName: input.name }, 'scaffold_restaurant failed');
        return {
          content: [{
            type: 'text' as const,
            text: JSON.stringify(buildToolErrorPayload('scaffold_restaurant failed', cause), null, 2),
          }],
          isError: true,
        };
      } finally {
        client.release();
      }
    },
  );
}
