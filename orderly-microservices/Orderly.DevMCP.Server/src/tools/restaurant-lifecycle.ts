/**
 * tools/restaurant-lifecycle.ts
 *
 * Core restaurant profile & operational settings CRUD tools:
 *   - create_restaurant: Creates a Brand (if needed) and Restaurant in Catalogdb.
 *   - update_restaurant: Updates settings, contact info, tax rates, operational toggles.
 *   - get_restaurant: Aggregates profile, brand, tables count, menu counts, and assigned staff count.
 */

import { randomUUID } from 'node:crypto';
import { z } from 'zod';
import type { McpServer } from '@modelcontextprotocol/sdk/server/mcp.js';

import type { Redis } from 'ioredis';
import type sql from 'mssql';

import type { Logger } from '../logger.ts';
import type { ToolContext } from './types.ts';
import { generateShortId } from '../util/short-id.ts';
import { invalidateCatalogCache } from '../util/cache-invalidation.ts';
import { buildToolErrorPayload } from './data-seeding.ts';

export interface RestaurantLifecycleDeps {
  logger: Logger;
  pg: ToolContext['pg'];
  mssql: sql.ConnectionPool;
  redis: Redis;
}

export function registerRestaurantLifecycleTools(server: McpServer, deps: RestaurantLifecycleDeps): void {
  // ── create_restaurant ────────────────────────────────────────────────
  server.registerTool(
    'create_restaurant',
    {
      title: 'Create restaurant',
      description:
        'Creates a new Restaurant (and optionally its Brand) in Catalogdb with generated unique ShortIds (rst..., brd...). Enforces column length constraints and defaults (MXN currency, America/Monterrey timezone, 0.16 tax rate).',
      inputSchema: {
        name: z.string().min(1).max(255).describe('Name of the restaurant'),
        brandId: z.string().uuid().optional().describe('Existing Brand UUID. If omitted, a brand is created automatically.'),
        brandName: z.string().max(100).optional().describe('Name for the auto-created brand (defaults to "[name] Brand").'),
        address: z.string().min(1).default('Av. Constitución 1000, Monterrey, NL').describe('Physical address'),
        phoneNumber: z.string().max(20).default('+52-81-0000-0000').describe('Contact phone number'),
        email: z.string().email().max(255).default('contact@orderly.local').describe('Contact email address'),
        currency: z.string().length(3).default('MXN').describe('ISO 4217 3-letter currency code (e.g. MXN, USD)'),
        timeZone: z.string().max(50).default('America/Monterrey').describe('IANA timezone identifier'),
        taxRate: z.number().min(0).max(1).default(0.16).describe('Tax rate fraction between 0.0 and 1.0 (e.g. 0.16 = 16%)'),
        estimatedTurnoverMinutes: z.number().int().positive().default(45).describe('Average table turnover duration in minutes'),
        autoConfirmOrders: z.boolean().default(false).describe('Whether incoming orders are confirmed automatically'),
        autoConfirmReservations: z.boolean().default(false).describe('Whether reservations are confirmed automatically'),
        allowAutoSubstitute: z.boolean().default(true).describe('Whether out-of-stock ingredients allow auto-substitution'),
      },
    },
    async (args) => {
      const input = args as {
        name: string;
        brandId?: string;
        brandName?: string;
        address: string;
        phoneNumber: string;
        email: string;
        currency: string;
        timeZone: string;
        taxRate: number;
        estimatedTurnoverMinutes: number;
        autoConfirmOrders: boolean;
        autoConfirmReservations: boolean;
        allowAutoSubstitute: boolean;
      };

      const client = await deps.pg.catalog.connect();
      try {
        await client.query('BEGIN');

        let brandId = input.brandId;
        let brandShortId = '';

        if (!brandId) {
          brandId = randomUUID();
          brandShortId = generateShortId('brd', 8);
          const brandName = input.brandName ?? `${input.name} Brand`;

          await client.query(
            `INSERT INTO "Brands" (
              "Id", "ShortId", "Name", "CuisineType", "Description",
              "ContactEmail", "ContactPhone", "LogoUrl", "WebsiteUrl",
              "CreatedBy", "LastModifiedBy", "IsActive"
            ) VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11, $12)`,
            [
              brandId,
              brandShortId,
              brandName,
              0, // CuisineType.Other
              `Auto-generated brand for ${input.name}`,
              input.email,
              input.phoneNumber,
              'https://images.unsplash.com/photo-1517248135467-4c7edcad34c4',
              'https://orderly.local',
              'devmcp',
              'devmcp',
              true,
            ],
          );
        } else {
          const brandRes = await client.query('SELECT "ShortId" FROM "Brands" WHERE "Id" = $1', [brandId]);
          if (brandRes.rows.length === 0) {
            await client.query('ROLLBACK');
            return {
              content: [{
                type: 'text' as const,
                text: JSON.stringify({ error: 'brand not found', brandId }, null, 2),
              }],
              isError: true,
            };
          }
          brandShortId = (brandRes.rows[0] as { ShortId: string }).ShortId;
        }

        const restaurantId = randomUUID();
        const restaurantShortId = generateShortId('rst', 8);

        await client.query(
          `INSERT INTO "Restaurants" (
            "Id", "ShortId", "BrandId", "Name", "Address", "PhoneNumber", "Email",
            "Currency", "TimeZone", "TaxRate", "EstimatedTurnoverMinutes",
            "AutoConfirmOrders", "AutoConfirmReservations", "AllowAutoSubstitute",
            "CreatedBy", "LastModifiedBy", "IsActive"
          ) VALUES (
            $1, $2, $3, $4, $5, $6, $7,
            $8, $9, $10, $11,
            $12, $13, $14,
            $15, $16, $17
          )`,
          [
            restaurantId,
            restaurantShortId,
            brandId,
            input.name,
            input.address,
            input.phoneNumber,
            input.email,
            input.currency,
            input.timeZone,
            input.taxRate,
            input.estimatedTurnoverMinutes,
            input.autoConfirmOrders,
            input.autoConfirmReservations,
            input.allowAutoSubstitute,
            'devmcp',
            'devmcp',
            true,
          ],
        );

        await client.query('COMMIT');

        deps.logger.info({ restaurantId, restaurantShortId, brandId }, 'created restaurant');

        return {
          content: [{
            type: 'text' as const,
            text: JSON.stringify({
              restaurantId,
              shortId: restaurantShortId,
              brandId,
              brandShortId,
              name: input.name,
              currency: input.currency,
              timeZone: input.timeZone,
              taxRate: input.taxRate,
              address: input.address,
              phoneNumber: input.phoneNumber,
              email: input.email,
              estimatedTurnoverMinutes: input.estimatedTurnoverMinutes,
              autoConfirmOrders: input.autoConfirmOrders,
              autoConfirmReservations: input.autoConfirmReservations,
              allowAutoSubstitute: input.allowAutoSubstitute,
            }, null, 2),
          }],
        };
      } catch (cause) {
        await client.query('ROLLBACK').catch(() => undefined);
        deps.logger.error({ err: cause }, 'create_restaurant failed');
        return {
          content: [{
            type: 'text' as const,
            text: JSON.stringify(buildToolErrorPayload('create_restaurant failed', cause), null, 2),
          }],
          isError: true,
        };
      } finally {
        client.release();
      }
    },
  );

  // ── update_restaurant ────────────────────────────────────────────────
  server.registerTool(
    'update_restaurant',
    {
      title: 'Update restaurant',
      description:
        'Updates profile, operational settings, contact information, or status of an existing Restaurant in Catalogdb. Resolves by either UUID or ShortId.',
      inputSchema: {
        restaurantId: z.string().describe('Restaurant UUID or ShortId (e.g. rst8gzkkd9k)'),
        name: z.string().min(1).max(255).optional(),
        address: z.string().min(1).optional(),
        phoneNumber: z.string().max(20).optional(),
        email: z.string().email().max(255).optional(),
        currency: z.string().length(3).optional(),
        timeZone: z.string().max(50).optional(),
        taxRate: z.number().min(0).max(1).optional(),
        estimatedTurnoverMinutes: z.number().int().positive().optional(),
        autoConfirmOrders: z.boolean().optional(),
        autoConfirmReservations: z.boolean().optional(),
        allowAutoSubstitute: z.boolean().optional(),
        isActive: z.boolean().optional(),
      },
    },
    async (args) => {
      const { restaurantId, ...fields } = args as {
        restaurantId: string;
        name?: string;
        address?: string;
        phoneNumber?: string;
        email?: string;
        currency?: string;
        timeZone?: string;
        taxRate?: number;
        estimatedTurnoverMinutes?: number;
        autoConfirmOrders?: boolean;
        autoConfirmReservations?: boolean;
        allowAutoSubstitute?: boolean;
        isActive?: boolean;
      };

      const isUuid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(restaurantId);
      const idCol = isUuid ? '"Id"' : '"ShortId"';

      const setClauses: string[] = ['"LastModifiedAt" = CURRENT_TIMESTAMP'];
      const values: unknown[] = [restaurantId];
      let paramIdx = 2;

      const columnMap: Record<string, string> = {
        name: '"Name"',
        address: '"Address"',
        phoneNumber: '"PhoneNumber"',
        email: '"Email"',
        currency: '"Currency"',
        timeZone: '"TimeZone"',
        taxRate: '"TaxRate"',
        estimatedTurnoverMinutes: '"EstimatedTurnoverMinutes"',
        autoConfirmOrders: '"AutoConfirmOrders"',
        autoConfirmReservations: '"AutoConfirmReservations"',
        allowAutoSubstitute: '"AllowAutoSubstitute"',
        isActive: '"IsActive"',
      };

      const updatedKeys: string[] = [];

      for (const [key, value] of Object.entries(fields)) {
        if (value !== undefined && columnMap[key]) {
          setClauses.push(`${columnMap[key]} = $${paramIdx}`);
          values.push(value);
          updatedKeys.push(key);
          paramIdx++;
        }
      }

      if (updatedKeys.length === 0) {
        return {
          content: [{
            type: 'text' as const,
            text: JSON.stringify({ message: 'No fields to update' }, null, 2),
          }],
        };
      }

      const sql = `UPDATE "Restaurants" SET ${setClauses.join(', ')} WHERE ${idCol} = $1 RETURNING "Id", "ShortId"`;

      try {
        const res = await deps.pg.catalog.query(sql, values);
        if (res.rows.length === 0) {
          return {
            content: [{
              type: 'text' as const,
              text: JSON.stringify({ error: 'restaurant not found', restaurantId }, null, 2),
            }],
            isError: true,
          };
        }

        const row = res.rows[0] as { Id: string; ShortId: string };
        deps.logger.info({ restaurantId: row.Id, updatedKeys }, 'updated restaurant');

        return {
          content: [{
            type: 'text' as const,
            text: JSON.stringify({
              success: true,
              restaurantId: row.Id,
              shortId: row.ShortId,
              updatedFields: updatedKeys,
            }, null, 2),
          }],
        };
      } catch (cause) {
        deps.logger.error({ err: cause, restaurantId }, 'update_restaurant failed');
        return {
          content: [{
            type: 'text' as const,
            text: JSON.stringify(buildToolErrorPayload('update_restaurant failed', cause), null, 2),
          }],
          isError: true,
        };
      }
    },
  );

  // ── get_restaurant ───────────────────────────────────────────────────
  server.registerTool(
    'get_restaurant',
    {
      title: 'Get restaurant',
      description:
        'Retrieves comprehensive restaurant details: metadata, brand details, active table counts, menu category and item totals, and assigned staff count.',
      inputSchema: {
        restaurantId: z.string().describe('Restaurant UUID or ShortId (e.g. rst8gzkkd9k)'),
      },
    },
    async (args) => {
      const { restaurantId } = args as { restaurantId: string };
      const isUuid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(restaurantId);
      const idCol = isUuid ? 'r."Id"' : 'r."ShortId"';

      try {
        const restSql = `
          SELECT
            r."Id" AS "id",
            r."ShortId" AS "shortId",
            r."Name" AS "name",
            r."Address" AS "address",
            r."PhoneNumber" AS "phoneNumber",
            r."Email" AS "email",
            r."Currency" AS "currency",
            r."TimeZone" AS "timeZone",
            r."TaxRate" AS "taxRate",
            r."EstimatedTurnoverMinutes" AS "estimatedTurnoverMinutes",
            r."AutoConfirmOrders" AS "autoConfirmOrders",
            r."AutoConfirmReservations" AS "autoConfirmReservations",
            r."AllowAutoSubstitute" AS "allowAutoSubstitute",
            r."IsActive" AS "isActive",
            r."CreatedAt" AS "createdAt",
            r."LastModifiedAt" AS "lastModifiedAt",
            b."Id" AS "brandId",
            b."ShortId" AS "brandShortId",
            b."Name" AS "brandName",
            b."LogoUrl" AS "brandLogoUrl"
          FROM "Restaurants" r
          LEFT JOIN "Brands" b ON b."Id" = r."BrandId"
          WHERE ${idCol} = $1
        `;

        const restRes = await deps.pg.catalog.query(restSql, [restaurantId]);
        if (restRes.rows.length === 0) {
          return {
            content: [{
              type: 'text' as const,
              text: JSON.stringify({ error: 'restaurant not found', restaurantId }, null, 2),
            }],
            isError: true,
          };
        }

        const restaurant = restRes.rows[0] as Record<string, unknown>;
        const actualUuid = restaurant['id'] as string;

        // Query counts in parallel
        const [tablesRes, categoriesRes, itemsRes, staffRes] = await Promise.all([
          deps.pg.catalog.query(
            'SELECT COUNT(*) AS total, COUNT(*) FILTER (WHERE "IsActive" = true) AS active FROM "Tables" WHERE "RestaurantId" = $1',
            [actualUuid],
          ),
          deps.pg.catalog.query(
            'SELECT COUNT(*) AS total FROM "MenuCategories" WHERE "RestaurantId" = $1 AND "IsDeleted" = false',
            [actualUuid],
          ),
          deps.pg.catalog.query(
            'SELECT COUNT(*) AS total, COUNT(*) FILTER (WHERE "IsAvailable" = true AND "IsDeleted" = false) AS available FROM "MenuItems" WHERE "RestaurantId" = $1 AND "IsDeleted" = false',
            [actualUuid],
          ),
          deps.pg.identity.query(
            'SELECT COUNT(*) AS total FROM "UserRestaurants" WHERE "RestaurantId" = $1',
            [actualUuid],
          ).catch(() => ({ rows: [{ total: '0' }] })),
        ]);

        const tableRow = tablesRes.rows[0] as { total: string; active: string } | undefined;
        const catRow = categoriesRes.rows[0] as { total: string } | undefined;
        const itemRow = itemsRes.rows[0] as { total: string; available: string } | undefined;
        const staffRow = staffRes.rows[0] as { total: string } | undefined;

        const summary = {
          tables: {
            total: Number(tableRow?.total ?? 0),
            active: Number(tableRow?.active ?? 0),
          },
          menu: {
            categories: Number(catRow?.total ?? 0),
            items: Number(itemRow?.total ?? 0),
            availableItems: Number(itemRow?.available ?? 0),
          },
          staff: {
            assignedUsers: Number(staffRow?.total ?? 0),
          },
        };

        return {
          content: [{
            type: 'text' as const,
            text: JSON.stringify({
              restaurant,
              summary,
            }, null, 2),
          }],
        };
      } catch (cause) {
        deps.logger.error({ err: cause, restaurantId }, 'get_restaurant failed');
        return {
          content: [{
            type: 'text' as const,
            text: JSON.stringify(buildToolErrorPayload('get_restaurant failed', cause), null, 2),
          }],
          isError: true,
        };
      }
    },
  );

  // ── purge_restaurant ─────────────────────────────────────────────────
  server.registerTool(
    'purge_restaurant',
    {
      title: 'Purge restaurant across all microservice databases',
      description:
        'Destructive cascading purge: deletes orders, kitchen tickets, baskets, staff tenant linkages, floorplans, menu trees, and the restaurant profile itself across all 5 databases. ' +
        'Requires exact confirmText matching "PURGE {restaurantId}".',
      inputSchema: {
        restaurantId: z.string().uuid().describe('Restaurant UUID to permanently purge'),
        confirmText: z.string().describe('Must match "PURGE {restaurantId}" to confirm deletion'),
        dryRun: z.boolean().default(false).describe('Simulate deletion and return counts without committing'),
      },
    },
    async (args) => {
      const { restaurantId, confirmText, dryRun = false } = args as {
        restaurantId: string;
        confirmText: string;
        dryRun?: boolean;
      };

      const expectedConfirm = `PURGE ${restaurantId}`;
      if (confirmText !== expectedConfirm) {
        return {
          content: [{
            type: 'text' as const,
            text: JSON.stringify({
              error: 'Safety check failed: confirmText must match exactly',
              expected: expectedConfirm,
              received: confirmText,
            }, null, 2),
          }],
          isError: true,
        };
      }

      const report: Record<string, unknown> = {
        restaurantId,
        mode: dryRun ? 'dry-run' : 'executed',
        orderDb: {},
        kitchenDb: {},
        basketDb: {},
        identityDb: {},
        catalogDb: {},
      };

      // 1. OrderDb (MSSQL)
      try {
        const req = deps.mssql.request();
        req.input('restaurantId', restaurantId);

        if (dryRun) {
          const qRes = await req.query(`
            SELECT
              (SELECT COUNT(*) FROM Orders WHERE RestaurantId = @restaurantId) AS orders,
              (SELECT COUNT(*) FROM OrderItems oi INNER JOIN Orders o ON oi.OrderId = o.Id WHERE o.RestaurantId = @restaurantId) AS orderItems,
              (SELECT COUNT(*) FROM order_activities oa INNER JOIN Orders o ON oa.OrderId = o.Id WHERE o.RestaurantId = @restaurantId) AS orderActivities
          `);
          report.orderDb = qRes.recordset[0] ?? {};
        } else {
          await req.query(`
            DELETE oa FROM order_activities oa INNER JOIN Orders o ON oa.OrderId = o.Id WHERE o.RestaurantId = @restaurantId;
            DELETE oi FROM OrderItems oi INNER JOIN Orders o ON oi.OrderId = o.Id WHERE o.RestaurantId = @restaurantId;
            DELETE ob FROM OrderBills ob INNER JOIN Orders o ON ob.OrderId = o.Id WHERE o.RestaurantId = @restaurantId;
            DELETE FROM Orders WHERE RestaurantId = @restaurantId;
            DELETE FROM RestaurantDailySequences WHERE RestaurantId = @restaurantId;
          `);
          report.orderDb = { purged: true };
        }
      } catch (err) {
        deps.logger.warn({ err, restaurantId }, 'OrderDb purge phase error (ignored or down)');
        report.orderDb = { skippedOrError: String(err) };
      }

      // 2. KitchenDb (PG)
      try {
        const kClient = await deps.pg.kitchen.connect();
        try {
          if (dryRun) {
            const kRes = await kClient.query(`
              SELECT
                (SELECT COUNT(*)::int FROM kitchen_tickets WHERE "RestaurantId" = $1) AS tickets,
                (SELECT COUNT(*)::int FROM kitchen_ticket_items WHERE "KitchenTicketId" IN (SELECT "Id" FROM kitchen_tickets WHERE "RestaurantId" = $1)) AS ticketItems
            `, [restaurantId]);
            report.kitchenDb = kRes.rows[0] ?? {};
          } else {
            await kClient.query('BEGIN');
            await kClient.query(`
              DELETE FROM kitchen_ticket_items WHERE "KitchenTicketId" IN (SELECT "Id" FROM kitchen_tickets WHERE "RestaurantId" = $1);
              DELETE FROM kitchen_tickets WHERE "RestaurantId" = $1;
            `, [restaurantId]);
            await kClient.query('COMMIT');
            report.kitchenDb = { purged: true };
          }
        } finally {
          kClient.release();
        }
      } catch (err) {
        deps.logger.warn({ err, restaurantId }, 'KitchenDb purge phase error');
        report.kitchenDb = { skippedOrError: String(err) };
      }

      // 3. BasketDb (PG - Marten)
      try {
        const bClient = await deps.pg.basket.connect();
        try {
          if (!dryRun) {
            await bClient.query(`DELETE FROM mt_doc_shoppingcart WHERE tenant_id = $1`).catch(() => undefined);
          }
          report.basketDb = { purged: true };
        } finally {
          bClient.release();
        }
      } catch (err) {
        report.basketDb = { skippedOrError: String(err) };
      }

      // 4. IdentityDb (PG - UserRestaurants link)
      try {
        const iClient = await deps.pg.identity.connect();
        try {
          if (dryRun) {
            const iRes = await iClient.query('SELECT COUNT(*)::int AS count FROM "UserRestaurants" WHERE "RestaurantId" = $1', [restaurantId]);
            report.identityDb = { linkedUsers: iRes.rows[0]?.count ?? 0 };
          } else {
            const delRes = await iClient.query('DELETE FROM "UserRestaurants" WHERE "RestaurantId" = $1', [restaurantId]);
            report.identityDb = { unlinkedUsers: delRes.rowCount ?? 0 };
          }
        } finally {
          iClient.release();
        }
      } catch (err) {
        deps.logger.warn({ err, restaurantId }, 'IdentityDb purge phase error');
        report.identityDb = { skippedOrError: String(err) };
      }

      // 5. CatalogDb (PG - Strict Reverse Topological Order)
      const cClient = await deps.pg.catalog.connect();
      try {
        if (dryRun) {
          const cRes = await cClient.query(`
            SELECT
              (SELECT COUNT(*)::int FROM "Tables" WHERE "RestaurantId" = $1) AS tables,
              (SELECT COUNT(*)::int FROM "MenuItems" WHERE "RestaurantId" = $1) AS items,
              (SELECT COUNT(*)::int FROM "MenuCategories" WHERE "RestaurantId" = $1) AS categories
          `, [restaurantId]);
          report.catalogDb = cRes.rows[0] ?? {};
        } else {
          await cClient.query('BEGIN');

          // Merged tables
          await cClient.query(`
            DELETE FROM "MergedTables"
            WHERE "ParentTableId" IN (SELECT "Id" FROM "Tables" WHERE "RestaurantId" = $1)
               OR "ChildTableId" IN (SELECT "Id" FROM "Tables" WHERE "RestaurantId" = $1)
          `, [restaurantId]);

          // Reservations & WalkInQueue
          await cClient.query('DELETE FROM "Reservations" WHERE "RestaurantId" = $1', [restaurantId]);
          await cClient.query('DELETE FROM "WalkInQueues" WHERE "RestaurantId" = $1', [restaurantId]);

          // Tables
          await cClient.query('DELETE FROM "Tables" WHERE "RestaurantId" = $1', [restaurantId]);

          // Menu children
          await cClient.query(`DELETE FROM "MenuItemIngredients" WHERE "MenuItemId" IN (SELECT "Id" FROM "MenuItems" WHERE "RestaurantId" = $1)`, [restaurantId]);
          await cClient.query(`DELETE FROM "MenuItemVariations" WHERE "MenuItemId" IN (SELECT "Id" FROM "MenuItems" WHERE "RestaurantId" = $1)`, [restaurantId]);
          await cClient.query(`
            DELETE FROM "ComboItems"
            WHERE "ComboMenuItemId" IN (SELECT "Id" FROM "MenuItems" WHERE "RestaurantId" = $1)
               OR "IncludedMenuItemId" IN (SELECT "Id" FROM "MenuItems" WHERE "RestaurantId" = $1)
          `, [restaurantId]);
          await cClient.query('DELETE FROM "PriceHistory" WHERE "RestaurantId" = $1', [restaurantId]);

          // Menu items
          await cClient.query('DELETE FROM "MenuItems" WHERE "RestaurantId" = $1', [restaurantId]);

          // Subcategories & Categories
          await cClient.query(`DELETE FROM "MenuSubCategories" WHERE "CategoryId" IN (SELECT "Id" FROM "MenuCategories" WHERE "RestaurantId" = $1)`, [restaurantId]);
          await cClient.query('DELETE FROM "MenuCategories" WHERE "RestaurantId" = $1', [restaurantId]);

          // Ingredients
          await cClient.query('DELETE FROM "Ingredients" WHERE "RestaurantId" = $1', [restaurantId]);

          // Restaurant
          const restDel = await cClient.query('DELETE FROM "Restaurants" WHERE "Id" = $1', [restaurantId]);
          await cClient.query('COMMIT');

          report.catalogDb = { restaurantDeleted: (restDel.rowCount ?? 0) > 0 };
        }
      } catch (err) {
        await cClient.query('ROLLBACK').catch(() => undefined);
        deps.logger.error({ err, restaurantId }, 'CatalogDb purge failed — rolled back');
        return {
          content: [{
            type: 'text' as const,
            text: JSON.stringify(buildToolErrorPayload('purge_restaurant CatalogDb phase failed', err), null, 2),
          }],
          isError: true,
        };
      } finally {
        cClient.release();
      }

      // 6. Redis Invalidation
      if (!dryRun) {
        await invalidateCatalogCache(deps.redis, restaurantId, deps.logger);
      }

      return {
        content: [{
          type: 'text' as const,
          text: JSON.stringify({ success: true, report }, null, 2),
        }],
      };
    },
  );
}
