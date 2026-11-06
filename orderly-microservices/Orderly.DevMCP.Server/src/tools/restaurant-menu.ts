/**
 * tools/restaurant-menu.ts
 *
 * Menu hierarchy, items, variations, coursing, and inventory management tool:
 *   - manage_restaurant_menu(action, restaurantId, ...)
 * Supports:
 *   - create_category, update_category, delete_category
 *   - create_item, update_item, delete_item
 *   - add_variation, delete_variation
 *   - update_availability (86'd status)
 *   - adjust_ingredient_stock
 *   - list_menu
 *
 * Automatically invalidates Redis cache keys:
 *   - catalog:menu:{restaurantId:N}
 *   - catalog:ingredients:{restaurantId:N}
 */

import { randomUUID } from 'node:crypto';
import { z } from 'zod';
import type { McpServer } from '@modelcontextprotocol/sdk/server/mcp.js';
import type { Redis } from 'ioredis';

import type { Logger } from '../logger.ts';
import type { ToolContext } from './types.ts';
import { generateShortId } from '../util/short-id.ts';
import { invalidateCatalogCache } from '../util/cache-invalidation.ts';
import { buildToolErrorPayload } from './data-seeding.ts';

export interface RestaurantMenuDeps {
  logger: Logger;
  pg: ToolContext['pg'];
  redis: Redis;
}

export function registerRestaurantMenuTools(server: McpServer, deps: RestaurantMenuDeps): void {
  server.registerTool(
    'manage_restaurant_menu',
    {
      title: 'Manage restaurant menu hierarchy and items',
      description:
        'CRUD operations for categories, items, variations, kitchen coursing, and ingredient stock. Automatically invalidates the restaurant\'s Redis cache. ' +
        'Supported actions: "list_menu", "create_category", "update_category", "delete_category", "create_item", "update_item", "delete_item", ' +
        '"add_variation", "delete_variation", "update_availability", "adjust_ingredient_stock".',
      inputSchema: {
        restaurantId: z.string().describe('Restaurant UUID or ShortId'),
        action: z.enum([
          'list_menu',
          'create_category',
          'update_category',
          'delete_category',
          'create_item',
          'update_item',
          'delete_item',
          'add_variation',
          'delete_variation',
          'update_availability',
          'adjust_ingredient_stock',
        ]),
        // Category fields
        categoryId: z.number().int().optional().describe('MenuCategory ID (integer)'),
        subCategoryId: z.number().int().optional().describe('MenuSubCategory ID (integer)'),
        categoryName: z.string().max(255).optional(),
        displayOrder: z.number().int().optional(),

        // Item fields
        itemId: z.string().describe('MenuItem UUID or ShortId').optional(),
        name: z.string().max(255).optional(),
        description: z.string().optional(),
        basePrice: z.number().min(0).optional(),
        imageUrl: z.string().url().default('https://images.unsplash.com/photo-1546069901-ba9599a7e63c').optional(),
        prepTimeMinutes: z.number().int().positive().default(15).optional(),
        defaultCourseNumber: z.number().int().min(1).max(5).default(1).describe('Course number for kitchen coursing (1: Appetizer, 2: Main, etc.)').optional(),
        itemType: z.enum(['Regular', 'Combo', 'Promo', 'Seasonal']).default('Regular').optional(),
        availabilityStatus: z.enum(['Available', 'Limited', 'Unavailable']).default('Available').optional(),
        isAvailable: z.boolean().optional(),

        // Variation fields
        variationId: z.number().int().optional(),
        variationName: z.string().max(100).optional().describe('Variation group name, e.g. "Size" or "Spice Level"'),
        variationValue: z.string().max(100).optional().describe('Option value, e.g. "Large" or "Medium"'),
        priceModifier: z.number().default(0).optional().describe('Price delta added to base price'),
        isDefaultVariation: z.boolean().default(false).optional(),

        // Inventory fields
        ingredientId: z.number().int().optional(),
        stockDelta: z.number().optional().describe('Stock change to apply (+ or -)'),
      },
    },
    async (args) => {
      const input = args as {
        restaurantId: string;
        action: string;
        categoryId?: number;
        subCategoryId?: number;
        categoryName?: string;
        displayOrder?: number;
        itemId?: string;
        name?: string;
        description?: string;
        basePrice?: number;
        imageUrl?: string;
        prepTimeMinutes?: number;
        defaultCourseNumber?: number;
        itemType?: string;
        availabilityStatus?: string;
        isAvailable?: boolean;
        variationId?: number;
        variationName?: string;
        variationValue?: string;
        priceModifier?: number;
        isDefaultVariation?: boolean;
        ingredientId?: number;
        stockDelta?: number;
      };

      const client = await deps.pg.catalog.connect();
      try {
        // Resolve restaurant UUID
        let restaurantUuid = input.restaurantId;
        if (!/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(restaurantUuid)) {
          const rRes = await client.query('SELECT "Id" FROM "Restaurants" WHERE "ShortId" = $1', [restaurantUuid]);
          if (rRes.rows.length === 0) {
            return {
              content: [{
                type: 'text' as const,
                text: JSON.stringify({ error: 'restaurant not found', restaurantId: input.restaurantId }, null, 2),
              }],
              isError: true,
            };
          }
          restaurantUuid = (rRes.rows[0] as { Id: string }).Id;
        }

        // Helper to invalidate cache after mutations
        const evictCache = async () => {
          await invalidateCatalogCache(deps.redis, restaurantUuid, deps.logger);
        };

        // ── Action: list_menu ───────────────────────────────────────────
        if (input.action === 'list_menu') {
          const catRes = await client.query(
            `SELECT "Id" AS "id", "Name" AS "name", "DisplayOrder" AS "displayOrder"
             FROM "MenuCategories"
             WHERE "RestaurantId" = $1 AND "IsDeleted" = false
             ORDER BY "DisplayOrder" ASC, "Name" ASC`,
            [restaurantUuid],
          );

          const itemsRes = await client.query(
            `SELECT
               mi."Id" AS "id",
               mi."ShortId" AS "shortId",
               mi."SubCategoryId" AS "subCategoryId",
               mi."Name" AS "name",
               mi."Description" AS "description",
               mi."BasePrice" AS "basePrice",
               mi."ImageUrl" AS "imageUrl",
               mi."PrepTimeMinutes" AS "prepTimeMinutes",
               mi."default_course_number" AS "defaultCourseNumber",
               mi."ItemType" AS "itemType",
               mi."AvailabilityStatus" AS "availabilityStatus",
               mi."IsAvailable" AS "isAvailable",
               mi."DisplayOrder" AS "displayOrder"
             FROM "MenuItems" mi
             WHERE mi."RestaurantId" = $1 AND mi."IsDeleted" = false
             ORDER BY mi."DisplayOrder" ASC, mi."Name" ASC`,
            [restaurantUuid],
          );

          const itemIds = itemsRes.rows.map((r: any) => r.id as string);
          let variationsMap: Record<string, any[]> = {};

          if (itemIds.length > 0) {
            const varRes = await client.query(
              `SELECT
                 "Id" AS "id",
                 "MenuItemId" AS "menuItemId",
                 "Name" AS "name",
                 "VariationValue" AS "variationValue",
                 "PriceModifier" AS "priceModifier",
                 "IsDefault" AS "isDefault"
               FROM "MenuItemVariations"
               WHERE "MenuItemId" = ANY($1::uuid[]) AND "IsDeleted" = false
               ORDER BY "DisplayOrder" ASC`,
              [itemIds],
            );

            for (const v of varRes.rows) {
              const mId = (v as any).menuItemId as string;
              if (!variationsMap[mId]) variationsMap[mId] = [];
              variationsMap[mId]!.push(v);
            }
          }

          const menuItems = itemsRes.rows.map((it: any) => ({
            ...it,
            variations: variationsMap[it.id] ?? [],
          }));

          return {
            content: [{
              type: 'text' as const,
              text: JSON.stringify({
                restaurantId: restaurantUuid,
                categoriesCount: catRes.rows.length,
                itemsCount: menuItems.length,
                categories: catRes.rows,
                items: menuItems,
              }, null, 2),
            }],
          };
        }

        // ── Action: create_category ─────────────────────────────────────
        if (input.action === 'create_category') {
          if (!input.categoryName) {
            return {
              content: [{
                type: 'text' as const,
                text: JSON.stringify({ error: 'categoryName is required for create_category' }, null, 2),
              }],
              isError: true,
            };
          }

          const insRes = await client.query(
            `INSERT INTO "MenuCategories" ("RestaurantId", "Name", "DisplayOrder", "IsActive", "IsDeleted", "CreatedBy", "LastModifiedBy")
             VALUES ($1, $2, $3, true, false, 'devmcp', 'devmcp')
             RETURNING "Id" AS "id", "Name" AS "name", "DisplayOrder" AS "displayOrder"`,
            [restaurantUuid, input.categoryName, input.displayOrder ?? 0],
          );

          await evictCache();

          return {
            content: [{
              type: 'text' as const,
              text: JSON.stringify({
                success: true,
                restaurantId: restaurantUuid,
                category: insRes.rows[0],
              }, null, 2),
            }],
          };
        }

        // ── Action: update_category ─────────────────────────────────────
        if (input.action === 'update_category') {
          if (!input.categoryId) {
            return {
              content: [{
                type: 'text' as const,
                text: JSON.stringify({ error: 'categoryId is required for update_category' }, null, 2),
              }],
              isError: true,
            };
          }

          const setClauses: string[] = ['"LastModifiedAt" = CURRENT_TIMESTAMP'];
          const values: unknown[] = [input.categoryId, restaurantUuid];
          let paramIdx = 3;

          if (input.categoryName !== undefined) {
            setClauses.push(`"Name" = $${paramIdx++}`);
            values.push(input.categoryName);
          }
          if (input.displayOrder !== undefined) {
            setClauses.push(`"DisplayOrder" = $${paramIdx++}`);
            values.push(input.displayOrder);
          }

          const upRes = await client.query(
            `UPDATE "MenuCategories"
             SET ${setClauses.join(', ')}
             WHERE "Id" = $1 AND "RestaurantId" = $2 AND "IsDeleted" = false
             RETURNING "Id" AS "id", "Name" AS "name", "DisplayOrder" AS "displayOrder"`,
            values,
          );

          if (upRes.rows.length === 0) {
            return {
              content: [{
                type: 'text' as const,
                text: JSON.stringify({ error: 'category not found', categoryId: input.categoryId }, null, 2),
              }],
              isError: true,
            };
          }

          await evictCache();

          return {
            content: [{
              type: 'text' as const,
              text: JSON.stringify({ success: true, category: upRes.rows[0] }, null, 2),
            }],
          };
        }

        // ── Action: delete_category ─────────────────────────────────────
        if (input.action === 'delete_category') {
          if (!input.categoryId) {
            return {
              content: [{
                type: 'text' as const,
                text: JSON.stringify({ error: 'categoryId is required for delete_category' }, null, 2),
              }],
              isError: true,
            };
          }

          await client.query(
            `UPDATE "MenuCategories"
             SET "IsDeleted" = true, "DeletedAt" = CURRENT_TIMESTAMP
             WHERE "Id" = $1 AND "RestaurantId" = $2`,
            [input.categoryId, restaurantUuid],
          );

          await evictCache();

          return {
            content: [{
              type: 'text' as const,
              text: JSON.stringify({ success: true, deletedCategoryId: input.categoryId }, null, 2),
            }],
          };
        }

        // ── Action: create_item ─────────────────────────────────────────
        if (input.action === 'create_item') {
          if (!input.name || input.basePrice === undefined) {
            return {
              content: [{
                type: 'text' as const,
                text: JSON.stringify({ error: 'name and basePrice are required for create_item' }, null, 2),
              }],
              isError: true,
            };
          }

          const itemId = randomUUID();
          const shortId = generateShortId('itm', 8);
          const defaultImage = input.imageUrl ?? 'https://images.unsplash.com/photo-1546069901-ba9599a7e63c';

          await client.query(
            `INSERT INTO "MenuItems" (
               "Id", "ShortId", "RestaurantId", "SubCategoryId", "Name", "Description",
               "BasePrice", "ImageUrl", "PrepTimeMinutes", "default_course_number",
               "ItemType", "AvailabilityStatus", "IsAvailable", "IsActive", "IsDeleted",
               "DisplayOrder", "CreatedBy", "LastModifiedBy"
             ) VALUES (
               $1, $2, $3, $4, $5, $6,
               $7, $8, $9, $10,
               $11, $12, true, true, false,
               $13, 'devmcp', 'devmcp'
             )`,
            [
              itemId,
              shortId,
              restaurantUuid,
              input.subCategoryId ?? null,
              input.name,
              input.description ?? '',
              input.basePrice,
              defaultImage,
              input.prepTimeMinutes ?? 15,
              input.defaultCourseNumber ?? 1,
              input.itemType ?? 'Regular',
              input.availabilityStatus ?? 'Available',
              input.displayOrder ?? 0,
            ],
          );

          await evictCache();

          return {
            content: [{
              type: 'text' as const,
              text: JSON.stringify({
                success: true,
                itemId,
                shortId,
                name: input.name,
                basePrice: input.basePrice,
                defaultCourseNumber: input.defaultCourseNumber ?? 1,
              }, null, 2),
            }],
          };
        }

        // ── Action: update_item ─────────────────────────────────────────
        if (input.action === 'update_item') {
          if (!input.itemId) {
            return {
              content: [{
                type: 'text' as const,
                text: JSON.stringify({ error: 'itemId is required for update_item' }, null, 2),
              }],
              isError: true,
            };
          }

          const isItemUuid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(input.itemId);
          const itemCol = isItemUuid ? '"Id"' : '"ShortId"';

          const setClauses: string[] = ['"LastModifiedAt" = CURRENT_TIMESTAMP'];
          const values: unknown[] = [input.itemId, restaurantUuid];
          let paramIdx = 3;

          if (input.name !== undefined) {
            setClauses.push(`"Name" = $${paramIdx++}`);
            values.push(input.name);
          }
          if (input.description !== undefined) {
            setClauses.push(`"Description" = $${paramIdx++}`);
            values.push(input.description);
          }
          if (input.basePrice !== undefined) {
            setClauses.push(`"BasePrice" = $${paramIdx++}`);
            values.push(input.basePrice);
          }
          if (input.imageUrl !== undefined) {
            setClauses.push(`"ImageUrl" = $${paramIdx++}`);
            values.push(input.imageUrl);
          }
          if (input.prepTimeMinutes !== undefined) {
            setClauses.push(`"PrepTimeMinutes" = $${paramIdx++}`);
            values.push(input.prepTimeMinutes);
          }
          if (input.defaultCourseNumber !== undefined) {
            setClauses.push(`"default_course_number" = $${paramIdx++}`);
            values.push(input.defaultCourseNumber);
          }
          if (input.itemType !== undefined) {
            setClauses.push(`"ItemType" = $${paramIdx++}`);
            values.push(input.itemType);
          }
          if (input.availabilityStatus !== undefined) {
            setClauses.push(`"AvailabilityStatus" = $${paramIdx++}`);
            values.push(input.availabilityStatus);
          }
          if (input.isAvailable !== undefined) {
            setClauses.push(`"IsAvailable" = $${paramIdx++}`);
            values.push(input.isAvailable);
          }

          const upItemRes = await client.query(
            `UPDATE "MenuItems"
             SET ${setClauses.join(', ')}
             WHERE ${itemCol} = $1 AND "RestaurantId" = $2 AND "IsDeleted" = false
             RETURNING "Id" AS "id", "ShortId" AS "shortId", "Name" AS "name", "BasePrice" AS "basePrice"`,
            values,
          );

          if (upItemRes.rows.length === 0) {
            return {
              content: [{
                type: 'text' as const,
                text: JSON.stringify({ error: 'item not found in restaurant', itemId: input.itemId }, null, 2),
              }],
              isError: true,
            };
          }

          await evictCache();

          return {
            content: [{
              type: 'text' as const,
              text: JSON.stringify({ success: true, item: upItemRes.rows[0] }, null, 2),
            }],
          };
        }

        // ── Action: delete_item ─────────────────────────────────────────
        if (input.action === 'delete_item') {
          if (!input.itemId) {
            return {
              content: [{
                type: 'text' as const,
                text: JSON.stringify({ error: 'itemId is required for delete_item' }, null, 2),
              }],
              isError: true,
            };
          }

          const isItemUuid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(input.itemId);
          const itemCol = isItemUuid ? '"Id"' : '"ShortId"';

          await client.query(
            `UPDATE "MenuItems"
             SET "IsDeleted" = true, "DeletedAt" = CURRENT_TIMESTAMP
             WHERE ${itemCol} = $1 AND "RestaurantId" = $2`,
            [input.itemId, restaurantUuid],
          );

          await evictCache();

          return {
            content: [{
              type: 'text' as const,
              text: JSON.stringify({ success: true, deletedItemId: input.itemId }, null, 2),
            }],
          };
        }

        // ── Action: add_variation ───────────────────────────────────────
        if (input.action === 'add_variation') {
          if (!input.itemId || !input.variationName || !input.variationValue) {
            return {
              content: [{
                type: 'text' as const,
                text: JSON.stringify({ error: 'itemId, variationName, and variationValue are required for add_variation' }, null, 2),
              }],
              isError: true,
            };
          }

          let itemUuid = input.itemId;
          if (!/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(itemUuid)) {
            const iRes = await client.query('SELECT "Id" FROM "MenuItems" WHERE "ShortId" = $1 AND "RestaurantId" = $2', [itemUuid, restaurantUuid]);
            if (iRes.rows.length === 0) {
              return {
                content: [{
                  type: 'text' as const,
                  text: JSON.stringify({ error: 'item not found', itemId: input.itemId }, null, 2),
                }],
                isError: true,
              };
            }
            itemUuid = (iRes.rows[0] as { Id: string }).Id;
          }

          const insVarRes = await client.query(
            `INSERT INTO "MenuItemVariations" (
               "MenuItemId", "Name", "VariationValue", "PriceModifier", "IsDefault", "DisplayOrder", "IsDeleted", "CreatedBy", "LastModifiedBy"
             ) VALUES ($1, $2, $3, $4, $5, 0, false, 'devmcp', 'devmcp')
             RETURNING "Id" AS "id", "Name" AS "name", "VariationValue" AS "variationValue", "PriceModifier" AS "priceModifier"`,
            [itemUuid, input.variationName, input.variationValue, input.priceModifier ?? 0, input.isDefaultVariation ?? false],
          );

          await evictCache();

          return {
            content: [{
              type: 'text' as const,
              text: JSON.stringify({ success: true, menuItemId: itemUuid, variation: insVarRes.rows[0] }, null, 2),
            }],
          };
        }

        // ── Action: delete_variation ────────────────────────────────────
        if (input.action === 'delete_variation') {
          if (!input.variationId) {
            return {
              content: [{
                type: 'text' as const,
                text: JSON.stringify({ error: 'variationId is required for delete_variation' }, null, 2),
              }],
              isError: true,
            };
          }

          await client.query(
            `UPDATE "MenuItemVariations" SET "IsDeleted" = true, "DeletedAt" = CURRENT_TIMESTAMP WHERE "Id" = $1`,
            [input.variationId],
          );

          await evictCache();

          return {
            content: [{
              type: 'text' as const,
              text: JSON.stringify({ success: true, deletedVariationId: input.variationId }, null, 2),
            }],
          };
        }

        // ── Action: update_availability ─────────────────────────────────
        if (input.action === 'update_availability') {
          if (!input.itemId || input.isAvailable === undefined) {
            return {
              content: [{
                type: 'text' as const,
                text: JSON.stringify({ error: 'itemId and isAvailable (boolean) are required for update_availability' }, null, 2),
              }],
              isError: true,
            };
          }

          const isItemUuid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(input.itemId);
          const itemCol = isItemUuid ? '"Id"' : '"ShortId"';

          await client.query(
            `UPDATE "MenuItems"
             SET "IsAvailable" = $1, "LastModifiedAt" = CURRENT_TIMESTAMP
             WHERE ${itemCol} = $2 AND "RestaurantId" = $3`,
            [input.isAvailable, input.itemId, restaurantUuid],
          );

          await evictCache();

          return {
            content: [{
              type: 'text' as const,
              text: JSON.stringify({ success: true, itemId: input.itemId, isAvailable: input.isAvailable }, null, 2),
            }],
          };
        }

        // ── Action: adjust_ingredient_stock ─────────────────────────────
        if (input.action === 'adjust_ingredient_stock') {
          if (!input.ingredientId || input.stockDelta === undefined) {
            return {
              content: [{
                type: 'text' as const,
                text: JSON.stringify({ error: 'ingredientId and stockDelta are required for adjust_ingredient_stock' }, null, 2),
              }],
              isError: true,
            };
          }

          const stockRes = await client.query(
            `UPDATE "Ingredients"
             SET "CurrentStock" = "CurrentStock" + $1, "LastModifiedAt" = CURRENT_TIMESTAMP
             WHERE "Id" = $2 AND "RestaurantId" = $3
             RETURNING "Id" AS "id", "Name" AS "name", "CurrentStock" AS "currentStock", "MinimumStock" AS "minimumStock"`,
            [input.stockDelta, input.ingredientId, restaurantUuid],
          );

          if (stockRes.rows.length === 0) {
            return {
              content: [{
                type: 'text' as const,
                text: JSON.stringify({ error: 'ingredient not found', ingredientId: input.ingredientId }, null, 2),
              }],
              isError: true,
            };
          }

          await evictCache();

          return {
            content: [{
              type: 'text' as const,
              text: JSON.stringify({ success: true, ingredient: stockRes.rows[0] }, null, 2),
            }],
          };
        }

        return {
          content: [{
            type: 'text' as const,
            text: JSON.stringify({ error: `unsupported action "${input.action}"` }, null, 2),
          }],
          isError: true,
        };
      } catch (cause) {
        deps.logger.error({ err: cause, restaurantId: input.restaurantId, action: input.action }, 'manage_restaurant_menu failed');
        return {
          content: [{
            type: 'text' as const,
            text: JSON.stringify(buildToolErrorPayload('manage_restaurant_menu failed', cause), null, 2),
          }],
          isError: true,
        };
      } finally {
        client.release();
      }
    },
  );
}
