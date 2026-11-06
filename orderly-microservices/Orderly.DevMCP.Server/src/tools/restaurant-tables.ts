/**
 * tools/restaurant-tables.ts
 *
 * Floorplan and table management tool:
 *   - manage_restaurant_tables(action, restaurantId, ...)
 * Supports: list, create, update, delete, update_status, bulk_layout.
 * Uses integer status matching BuildingBlocks.Enums.TableStatus:
 *   0: Available, 1: Occupied, 2: Reserved, 3: Cleaning, 4: NeedsAttention.
 */

import { randomUUID } from 'node:crypto';
import { z } from 'zod';
import type { McpServer } from '@modelcontextprotocol/sdk/server/mcp.js';

import type { Logger } from '../logger.ts';
import type { ToolContext } from './types.ts';
import { generateShortId } from '../util/short-id.ts';
import { buildToolErrorPayload } from './data-seeding.ts';

export const TABLE_STATUS_MAP: Record<string, number> = {
  Available: 0,
  Occupied: 1,
  Reserved: 2,
  Cleaning: 3,
  NeedsAttention: 4,
  available: 0,
  occupied: 1,
  reserved: 2,
  cleaning: 3,
  needs_attention: 4,
};

export const TABLE_STATUS_REVERSE: Record<number, string> = {
  0: 'Available',
  1: 'Occupied',
  2: 'Reserved',
  3: 'Cleaning',
  4: 'NeedsAttention',
};

export interface RestaurantTablesDeps {
  logger: Logger;
  pg: ToolContext['pg'];
}

export function registerRestaurantTablesTools(server: McpServer, deps: RestaurantTablesDeps): void {
  server.registerTool(
    'manage_restaurant_tables',
    {
      title: 'Manage restaurant tables and floorplan',
      description:
        'CRUD and batch operations for restaurant dining tables and floorplans. Supports actions: "list", "create", "update", "delete", "update_status", and "bulk_layout". ' +
        'TableStatus enum: Available (0), Occupied (1), Reserved (2), Cleaning (3), NeedsAttention (4).',
      inputSchema: {
        restaurantId: z.string().describe('Restaurant UUID or ShortId'),
        action: z.enum(['list', 'create', 'update', 'delete', 'update_status', 'bulk_layout']),
        tableId: z.string().describe('Table UUID or ShortId (required for update, delete, update_status)').optional(),
        tableNumber: z.string().max(10).optional(),
        capacity: z.number().int().positive().optional(),
        positionX: z.number().int().optional(),
        positionY: z.number().int().optional(),
        shape: z.enum(['round', 'square', 'rectangle']).default('rectangle'),
        status: z.enum(['Available', 'Occupied', 'Reserved', 'Cleaning', 'NeedsAttention']).optional(),
        isActive: z.boolean().optional(),
        tables: z.array(
          z.object({
            tableNumber: z.string().max(10),
            capacity: z.number().int().positive(),
            positionX: z.number().int().default(0),
            positionY: z.number().int().default(0),
            shape: z.enum(['round', 'square', 'rectangle']).default('rectangle'),
          }),
        ).optional().describe('List of tables for "bulk_layout" action'),
      },
    },
    async (args) => {
      const input = args as {
        restaurantId: string;
        action: 'list' | 'create' | 'update' | 'delete' | 'update_status' | 'bulk_layout';
        tableId?: string;
        tableNumber?: string;
        capacity?: number;
        positionX?: number;
        positionY?: number;
        shape: string;
        status?: string;
        isActive?: boolean;
        tables?: Array<{
          tableNumber: string;
          capacity: number;
          positionX: number;
          positionY: number;
          shape: string;
        }>;
      };

      const client = await deps.pg.catalog.connect();
      try {
        // Resolve restaurant UUID if shortId was provided
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

        // ── Action: list ────────────────────────────────────────────────
        if (input.action === 'list') {
          const listRes = await client.query(
            `SELECT
              "Id" AS "id",
              "ShortId" AS "shortId",
              "TableNumber" AS "tableNumber",
              "Capacity" AS "capacity",
              "PositionX" AS "positionX",
              "PositionY" AS "positionY",
              "Shape" AS "shape",
              "Status" AS "statusCode",
              "CurrentOrderId" AS "currentOrderId",
              "IsActive" AS "isActive"
            FROM "Tables"
            WHERE "RestaurantId" = $1
            ORDER BY "TableNumber" ASC`,
            [restaurantUuid],
          );

          const tables = listRes.rows.map((row: any) => ({
            ...row,
            status: TABLE_STATUS_REVERSE[row.statusCode as number] ?? 'Available',
          }));

          return {
            content: [{
              type: 'text' as const,
              text: JSON.stringify({ restaurantId: restaurantUuid, count: tables.length, tables }, null, 2),
            }],
          };
        }

        // ── Action: create ──────────────────────────────────────────────
        if (input.action === 'create') {
          if (!input.tableNumber || !input.capacity) {
            return {
              content: [{
                type: 'text' as const,
                text: JSON.stringify({ error: 'tableNumber and capacity are required for "create"' }, null, 2),
              }],
              isError: true,
            };
          }

          const existing = await client.query(
            'SELECT "Id" FROM "Tables" WHERE "RestaurantId" = $1 AND "TableNumber" = $2',
            [restaurantUuid, input.tableNumber],
          );
          if (existing.rows.length > 0) {
            return {
              content: [{
                type: 'text' as const,
                text: JSON.stringify({
                  error: 'table number already exists in this restaurant',
                  tableNumber: input.tableNumber,
                }, null, 2),
              }],
              isError: true,
            };
          }

          const tableId = randomUUID();
          const shortId = generateShortId('tbl', 8);
          const statusCode = input.status ? (TABLE_STATUS_MAP[input.status] ?? 0) : 0;

          await client.query(
            `INSERT INTO "Tables" (
              "Id", "ShortId", "RestaurantId", "TableNumber", "Capacity",
              "PositionX", "PositionY", "Shape", "Status", "IsActive",
              "CreatedBy", "LastModifiedBy"
            ) VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10, 'devmcp', 'devmcp')`,
            [
              tableId,
              shortId,
              restaurantUuid,
              input.tableNumber,
              input.capacity,
              input.positionX ?? 0,
              input.positionY ?? 0,
              input.shape,
              statusCode,
              input.isActive ?? true,
            ],
          );

          return {
            content: [{
              type: 'text' as const,
              text: JSON.stringify({
                success: true,
                tableId,
                shortId,
                restaurantId: restaurantUuid,
                tableNumber: input.tableNumber,
                capacity: input.capacity,
                positionX: input.positionX ?? 0,
                positionY: input.positionY ?? 0,
                shape: input.shape,
                status: TABLE_STATUS_REVERSE[statusCode],
              }, null, 2),
            }],
          };
        }

        // ── Action: update / update_status ──────────────────────────────
        if (input.action === 'update' || input.action === 'update_status') {
          if (!input.tableId) {
            return {
              content: [{
                type: 'text' as const,
                text: JSON.stringify({ error: 'tableId is required for update' }, null, 2),
              }],
              isError: true,
            };
          }

          const isTableUuid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(input.tableId);
          const tableIdCol = isTableUuid ? '"Id"' : '"ShortId"';

          const setClauses: string[] = ['"LastModifiedAt" = CURRENT_TIMESTAMP'];
          const values: unknown[] = [input.tableId, restaurantUuid];
          let paramIdx = 3;

          if (input.tableNumber !== undefined) {
            setClauses.push(`"TableNumber" = $${paramIdx++}`);
            values.push(input.tableNumber);
          }
          if (input.capacity !== undefined) {
            setClauses.push(`"Capacity" = $${paramIdx++}`);
            values.push(input.capacity);
          }
          if (input.positionX !== undefined) {
            setClauses.push(`"PositionX" = $${paramIdx++}`);
            values.push(input.positionX);
          }
          if (input.positionY !== undefined) {
            setClauses.push(`"PositionY" = $${paramIdx++}`);
            values.push(input.positionY);
          }
          if (input.shape !== undefined) {
            setClauses.push(`"Shape" = $${paramIdx++}`);
            values.push(input.shape);
          }
          if (input.status !== undefined) {
            const code = TABLE_STATUS_MAP[input.status] ?? 0;
            setClauses.push(`"Status" = $${paramIdx++}`);
            values.push(code);
          }
          if (input.isActive !== undefined) {
            setClauses.push(`"IsActive" = $${paramIdx++}`);
            values.push(input.isActive);
          }

          const updateSql = `
            UPDATE "Tables"
            SET ${setClauses.join(', ')}
            WHERE ${tableIdCol} = $1 AND "RestaurantId" = $2
            RETURNING "Id", "ShortId", "TableNumber", "Status"
          `;

          const updateRes = await client.query(updateSql, values);
          if (updateRes.rows.length === 0) {
            return {
              content: [{
                type: 'text' as const,
                text: JSON.stringify({ error: 'table not found in restaurant', tableId: input.tableId }, null, 2),
              }],
              isError: true,
            };
          }

          const row = updateRes.rows[0] as { Id: string; ShortId: string; TableNumber: string; Status: number };
          return {
            content: [{
              type: 'text' as const,
              text: JSON.stringify({
                success: true,
                tableId: row.Id,
                shortId: row.ShortId,
                tableNumber: row.TableNumber,
                status: TABLE_STATUS_REVERSE[row.Status],
              }, null, 2),
            }],
          };
        }

        // ── Action: delete ──────────────────────────────────────────────
        if (input.action === 'delete') {
          if (!input.tableId) {
            return {
              content: [{
                type: 'text' as const,
                text: JSON.stringify({ error: 'tableId is required for delete' }, null, 2),
              }],
              isError: true,
            };
          }

          const isTableUuid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(input.tableId);
          const tableIdCol = isTableUuid ? '"Id"' : '"ShortId"';

          // First find the table's UUID
          const tRes = await client.query(
            `SELECT "Id", "TableNumber" FROM "Tables" WHERE ${tableIdCol} = $1 AND "RestaurantId" = $2`,
            [input.tableId, restaurantUuid],
          );
          if (tRes.rows.length === 0) {
            return {
              content: [{
                type: 'text' as const,
                text: JSON.stringify({ error: 'table not found in restaurant', tableId: input.tableId }, null, 2),
              }],
              isError: true,
            };
          }

          const resolvedTableUuid = (tRes.rows[0] as { Id: string }).Id;

          await client.query('BEGIN');
          // Clean merged tables if any
          await client.query(
            'DELETE FROM "MergedTables" WHERE "ParentTableId" = $1 OR "ChildTableId" = $1',
            [resolvedTableUuid],
          );
          // Delete table
          await client.query('DELETE FROM "Tables" WHERE "Id" = $1', [resolvedTableUuid]);
          await client.query('COMMIT');

          return {
            content: [{
              type: 'text' as const,
              text: JSON.stringify({
                success: true,
                deletedTableId: resolvedTableUuid,
                tableNumber: (tRes.rows[0] as { TableNumber: string }).TableNumber,
              }, null, 2),
            }],
          };
        }

        // ── Action: bulk_layout ─────────────────────────────────────────
        if (input.action === 'bulk_layout') {
          const tableList = input.tables ?? [];
          if (tableList.length === 0) {
            return {
              content: [{
                type: 'text' as const,
                text: JSON.stringify({ error: 'tables array is required and must not be empty for "bulk_layout"' }, null, 2),
              }],
              isError: true,
            };
          }

          await client.query('BEGIN');

          const createdTables: Array<{ id: string; shortId: string; tableNumber: string; capacity: number }> = [];

          for (const tbl of tableList) {
            const tableId = randomUUID();
            const shortId = generateShortId('tbl', 8);

            await client.query(
              `INSERT INTO "Tables" (
                "Id", "ShortId", "RestaurantId", "TableNumber", "Capacity",
                "PositionX", "PositionY", "Shape", "Status", "IsActive",
                "CreatedBy", "LastModifiedBy"
              ) VALUES ($1, $2, $3, $4, $5, $6, $7, $8, 0, true, 'devmcp', 'devmcp')
              ON CONFLICT ("RestaurantId", "TableNumber")
              DO UPDATE SET
                "Capacity" = EXCLUDED."Capacity",
                "PositionX" = EXCLUDED."PositionX",
                "PositionY" = EXCLUDED."PositionY",
                "Shape" = EXCLUDED."Shape",
                "LastModifiedAt" = CURRENT_TIMESTAMP`,
              [
                tableId,
                shortId,
                restaurantUuid,
                tbl.tableNumber,
                tbl.capacity,
                tbl.positionX,
                tbl.positionY,
                tbl.shape,
              ],
            );

            createdTables.push({
              id: tableId,
              shortId,
              tableNumber: tbl.tableNumber,
              capacity: tbl.capacity,
            });
          }

          await client.query('COMMIT');

          return {
            content: [{
              type: 'text' as const,
              text: JSON.stringify({
                success: true,
                restaurantId: restaurantUuid,
                count: createdTables.length,
                tables: createdTables,
              }, null, 2),
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
        await client.query('ROLLBACK').catch(() => undefined);
        deps.logger.error({ err: cause, restaurantId: input.restaurantId, action: input.action }, 'manage_restaurant_tables failed');
        return {
          content: [{
            type: 'text' as const,
            text: JSON.stringify(buildToolErrorPayload('manage_restaurant_tables failed', cause), null, 2),
          }],
          isError: true,
        };
      } finally {
        client.release();
      }
    },
  );
}
