/**
 * tools/state-inspection.ts (§6.3).
 *
 * Two tools:
 *   - inspect_basket(userId, restaurantId) — Redis `basket:{userId}:{restaurantId}`
 *     (note: actual cache key format from CachedBasketRepository.cs, NOT
 *     `basket-{basketId}` as the plan §6.3 stated). Returns a structured
 *     view + a diff vs the previous call (in-memory LRU, §10.3).
 *   - inspect_order_pipeline(orderId) — cross-queries:
 *       1. OrderDb (MSSQL): `Orders` table for the row + OrderNumber.
 *       2. RabbitMQ Management API (15672): depth of the ordering queue.
 *       3. Kitchendb (PG): `kitchen_tickets` table by OrderNumber.
 *     Returns a structured lifecycle report.
 */

import { LRUCache } from 'lru-cache';
import { z } from 'zod';
import type { McpServer } from '@modelcontextprotocol/sdk/server/mcp.js';

import type { Logger } from '../logger.ts';
import type { ToolContext } from './types.ts';

const DIFF_CACHE_MAX = 128;

interface BasketSnapshot {
  userId: string;
  restaurantId: string;
  itemCount: number;
  subtotal: number;
  appliedDiscounts: string[];
  items: unknown;
  expiresAt: string | null;
  createdAt: string | null;
  fetchedAt: string;
}

function diffSnapshots(prev: BasketSnapshot | undefined, next: BasketSnapshot): {
  changed: boolean;
  changes: string[];
} {
  if (!prev) return { changed: true, changes: ['first observation'] };
  const changes: string[] = [];
  if (prev.itemCount !== next.itemCount) changes.push(`itemCount: ${prev.itemCount} → ${next.itemCount}`);
  if (prev.subtotal !== next.subtotal) changes.push(`subtotal: ${prev.subtotal} → ${next.subtotal}`);
  if (JSON.stringify(prev.appliedDiscounts) !== JSON.stringify(next.appliedDiscounts)) {
    changes.push(`appliedDiscounts: [${prev.appliedDiscounts.join(',')}] → [${next.appliedDiscounts.join(',')}]`);
  }
  if (JSON.stringify(prev.items) !== JSON.stringify(next.items)) changes.push('items changed');
  if (prev.expiresAt !== next.expiresAt) changes.push(`expiresAt: ${prev.expiresAt} → ${next.expiresAt}`);
  return { changed: changes.length > 0, changes };
}

export function registerStateInspectionTools(
  server: McpServer,
  ctx: ToolContext & { logger: Logger },
): void {
  const basketDiff = new LRUCache<string, BasketSnapshot>({ max: DIFF_CACHE_MAX });

  // ── inspect_basket ────────────────────────────────────────────────────

  server.registerTool(
    'inspect_basket',
    {
      title: 'Inspect basket',
      description:
        'Reads the cached basket from Redis (key `basket:{userId}:{restaurantId}`) and returns ' +
        'a structured view: items, subtotal, applied discounts, expiry. ' +
        'When called twice with the same (userId, restaurantId), the response includes a `diff` ' +
        'against the previous observation so mutations are obvious.',
      inputSchema: {
        userId: z.string().uuid().describe('User id (Marten [Identity] on Basket).'),
        restaurantId: z.string().uuid().describe('Restaurant id.'),
      },
    },
    async (args) => {
      const { userId, restaurantId } = args as { userId: string; restaurantId: string };

      const key = `basket:${userId}:${restaurantId}`;
      let raw: string | null;
      try {
        raw = await ctx.redis.get(key);
      } catch (cause) {
        return {
          content: [{ type: 'text' as const, text: JSON.stringify({ error: 'redis GET failed', cause: String(cause), key }, null, 2) }],
          isError: true,
        };
      }

      if (raw === null) {
        return {
          content: [{ type: 'text' as const, text: JSON.stringify({ error: 'basket not found', key }, null, 2) }],
          isError: true,
        };
      }

      let parsed: Record<string, unknown>;
      try {
        parsed = JSON.parse(raw) as Record<string, unknown>;
      } catch (cause) {
        return {
          content: [{ type: 'text' as const, text: JSON.stringify({ error: 'basket JSON malformed', cause: String(cause), key }, null, 2) }],
          isError: true,
        };
      }

      const items = parsed.Items ?? parsed.items ?? [];
      const appliedDiscounts = (parsed.AppliedDiscounts ?? parsed.appliedDiscounts ?? []) as string[];
      const subtotal = typeof parsed.Subtotal === 'number'
        ? parsed.Subtotal
        : Array.isArray(items)
          ? (items as Array<{ TotalPrice?: number; totalPrice?: number; Quantity?: number; quantity?: number; UnitPrice?: number; unitPrice?: number }>)
              .reduce((acc, it) => acc + ((it.TotalPrice ?? it.totalPrice ?? 0) || ((it.UnitPrice ?? it.unitPrice ?? 0) * (it.Quantity ?? it.quantity ?? 1))), 0)
          : 0;

      const next: BasketSnapshot = {
        userId,
        restaurantId,
        itemCount: Array.isArray(items) ? items.length : 0,
        subtotal,
        appliedDiscounts: appliedDiscounts.map(String),
        items,
        expiresAt: typeof parsed.ExpiresAt === 'string' ? parsed.ExpiresAt : null,
        createdAt: typeof parsed.CreatedAt === 'string' ? parsed.CreatedAt : null,
        fetchedAt: new Date().toISOString(),
      };

      const cacheKey = `${userId}:${restaurantId}`;
      const prev = basketDiff.get(cacheKey);
      const diff = diffSnapshots(prev, next);
      basketDiff.set(cacheKey, next);

      ctx.logger.info({ userId, restaurantId, itemCount: next.itemCount, changed: diff.changed }, 'inspected basket');

      return {
        content: [
          { type: 'text' as const, text: JSON.stringify({ ...next, diff }, null, 2) },
        ],
      };
    },
  );

  // ── inspect_order_pipeline ────────────────────────────────────────────

  server.registerTool(
    'inspect_order_pipeline',
    {
      title: 'Inspect order pipeline',
      description:
        'Cross-queries OrderDb (MSSQL), RabbitMQ Management API, and Kitchendb to show the full ' +
        'lifecycle of an order: row in Orders + queue depth + kitchen ticket status. ' +
        'Returns partial results with error flags if any subsystem is unreachable.',
      inputSchema: {
        orderId: z.string().uuid().describe('Order id (Id column in Orders).'),
      },
    },
    async (args) => {
      const { orderId } = args as { orderId: string };

      const [orderResult, queueResult, kitchenResult] = await Promise.allSettled([
        // OrderDb.Orders lookup by Id
        (async () => {
          const r = await ctx.mssql.request()
            .input('orderId', orderId)
            .query('SELECT Id, OrderNumber, CustomerId, Status, OrderType, DeliveryStatus, CreatedAt FROM Orders WHERE Id = @orderId');
          const row = r.recordset[0];
          if (!row) throw new Error(`order ${orderId} not found`);
          return row as Record<string, unknown>;
        })(),
        // RabbitMQ Management API — depth of ordering queue (default vhost '/')
        (async () => {
          const auth = Buffer.from(`${process.env.RABBITMQ_DEFAULT_USER ?? 'guest'}:${process.env.RABBITMQ_DEFAULT_PASS ?? 'guest'}`).toString('base64');
          const res = await fetch('http://localhost:15672/api/queues/%2F/ordering-api', {
            headers: { Authorization: `Basic ${auth}` },
            signal: AbortSignal.timeout(3_000),
          });
          if (!res.ok) return { error: `rabbit mgmt returned ${res.status}` };
          const j = (await res.json()) as { messages?: number; messages_ready?: number; consumers?: number };
          return { messages: j.messages ?? 0, messagesReady: j.messages_ready ?? 0, consumers: j.consumers ?? 0 };
        })(),
        // Kitchendb kitchen_tickets by OrderNumber — we need the OrderNumber from step 1,
        // but run in parallel anyway; will resolve with the order row below if available.
        (async () => ({ skipped: true as const, reason: 'resolved after Orders lookup' }))(),
      ]);

      // Now that we have OrderNumber (or an order error), do the Kitchen lookup.
      let kitchenTicket: { value: unknown } | { reason: unknown } = { reason: 'order lookup failed' };
      if (orderResult.status === 'fulfilled') {
        const orderNumber = orderResult.value.OrderNumber;
        if (typeof orderNumber === 'string' && orderNumber.length > 0) {
          try {
            const r = await ctx.pg.kitchen.query(
              'SELECT "Id", "RestaurantId", "CustomerId", "OrderNumber", "Status", "ReceivedAt", "StartedAt", "ReadyAt", "BumpedAt", "CancelledAt" FROM kitchen_tickets WHERE "OrderNumber" = $1',
              [orderNumber],
            );
            kitchenTicket = { value: r.rows[0] ?? null };
          } catch (cause) {
            kitchenTicket = { reason: String(cause) };
          }
        } else {
          kitchenTicket = { value: null, reason: 'OrderNumber missing from Order row' };
        }
      }

      const result = {
        orderId,
        order: orderResult.status === 'fulfilled' ? orderResult.value : { error: String(orderResult.reason) },
        messageBroker: queueResult.status === 'fulfilled' ? queueResult.value : { error: String(queueResult.reason) },
        kitchenTicket,
        inspectedAt: new Date().toISOString(),
      };

      ctx.logger.info({ orderId, ok: orderResult.status }, 'inspected order pipeline');

      return {
        content: [{ type: 'text' as const, text: JSON.stringify(result, null, 2) }],
      };
    },
  );
}
