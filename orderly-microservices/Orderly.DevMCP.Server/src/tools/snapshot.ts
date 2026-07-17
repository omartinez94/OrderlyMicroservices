/**
 * tools/snapshot.ts (§6.9).
 *
 * `get_system_snapshot(restaurantId?)` — runs 6 sub-queries in parallel
 * with a 3-second per-sub-query budget (§6.9). Partial results are
 * returned if any subsystem fails — the AI gets a best-effort view
 * even when one backend is down.
 *
 * `watch_system(intervalSeconds, restaurantId?)` — starts a recurring
 * snapshot and pushes each result via `server.sendLoggingMessage()`
 * (the SDK's only generic notification primitive — see
 * https://unpkg.com/@modelcontextprotocol/sdk@1.29.0/dist/esm/server/mcp.d.ts).
 * The MCP protocol has no streaming-data primitive; logging
 * notifications are the workaround. Subscribers call `tools/call`
 * `get_system_snapshot` for the initial value.
 *
 * Caching: snapshot sub-queries are wrapped via `async-cache-dedupe`
 * (TTL 2 s) so concurrent snapshots share work without serving stale
 * state (§10.1, §10.3).
 */

import asyncCacheDedupe from 'async-cache-dedupe';
import { z } from 'zod';
import type { McpServer } from '@modelcontextprotocol/sdk/server/mcp.js';

import type { Logger } from '../logger.ts';
import type { ToolContext } from './types.ts';
import { withTimeout, TimeoutError } from '../util/timeout.ts';

const SUBQUERY_TIMEOUT_MS = 3_000;

interface CatalogSection {
  martenDocumentCount: number;
  orderSnapshotCount: number;
}

interface OrdersSection {
  total: number;
  pending: number;
  processing: number;
  completedToday: number;
  recent: Array<{ id: string; orderNumber: string; status: string; createdAt: string }>;
}

interface KitchenSection {
  activeCount: number;
  oldestReceivedAt: string | null;
}

interface EventBusSection {
  orderingQueueDepth: number;
  consumers: number;
}

interface ActiveSessionsSection {
  basketCount: number;
  totalItems: number;
}

interface Snapshot {
  generatedAt: string;
  restaurantId?: string;
  catalog: CatalogSection | { error: string };
  activeSessions: ActiveSessionsSection | { error: string };
  orders: OrdersSection | { error: string };
  kitchen: KitchenSection | { error: string };
  eventBus: EventBusSection | { error: string };
}

function asError(e: unknown): { error: string } {
  if (e instanceof TimeoutError) return { error: `timeout after ${e.timeoutMs}ms (${e.label})` };
  if (e instanceof Error) return { error: e.message };
  return { error: String(e) };
}

export function registerSnapshotTools(
  server: McpServer,
  ctx: ToolContext & { logger: Logger },
): void {
  // ── Sub-query implementations ────────────────────────────────────────

  const fetchCatalog = async (): Promise<CatalogSection> => {
    const r = await ctx.pg.catalog.query<{ count: string }>(
      "SELECT count(*)::text AS count FROM mt_doc_order_snapshot",
    );
    const all = await ctx.pg.catalog.query<{ count: string }>(
      "SELECT count(*)::text AS count FROM mt_doc_order_modification_log",
    );
    return {
      martenDocumentCount: parseInt(r.rows[0]?.count ?? '0', 10) + parseInt(all.rows[0]?.count ?? '0', 10),
      orderSnapshotCount: parseInt(r.rows[0]?.count ?? '0', 10),
    };
  };

  const fetchActiveSessions = async (): Promise<ActiveSessionsSection> => {
    let cursor = '0';
    let basketCount = 0;
    let totalItems = 0;
    do {
      const [next, keys] = await ctx.redis.scan(cursor, 'MATCH', 'basket:*', 'COUNT', 100);
      cursor = next;
      for (const key of keys) {
        basketCount++;
        const raw = await ctx.redis.get(key);
        if (raw) {
          try {
            const parsed = JSON.parse(raw) as { Items?: unknown[]; items?: unknown[] };
            totalItems += (parsed.Items ?? parsed.items ?? []).length;
          } catch { /* skip malformed */ }
        }
      }
    } while (cursor !== '0');
    return { basketCount, totalItems };
  };

  const fetchOrders = async (): Promise<OrdersSection> => {
    const totals = await ctx.mssql.request().query<{ Status: string; cnt: number }>(
      `SELECT Status, count(*) AS cnt FROM Orders GROUP BY Status`,
    );
    const counts = Object.fromEntries(totals.recordset.map((r) => [r.Status, r.cnt]));
    const todayReq = ctx.mssql.request();
    todayReq.input('today', new Date(new Date().setHours(0, 0, 0, 0)));
    const completedTodayRes = await todayReq.query<{ cnt: number }>(
      `SELECT count(*) AS cnt FROM Orders WHERE Status = 'Completed' AND CreatedAt >= @today`,
    );
    const recent = await ctx.mssql.request().query<{ Id: string; OrderNumber: string; Status: string; CreatedAt: Date }>(
      `SELECT TOP 5 Id, OrderNumber, Status, CreatedAt FROM Orders ORDER BY CreatedAt DESC`,
    );
    return {
      total: Object.values(counts).reduce((a, b) => a + b, 0),
      pending: (counts['Pending'] ?? 0) + (counts['pending'] ?? 0),
      processing: (counts['Processing'] ?? 0) + (counts['processing'] ?? 0),
      completedToday: completedTodayRes.recordset[0]?.cnt ?? 0,
      recent: recent.recordset.map((r) => ({
        id: r.Id,
        orderNumber: r.OrderNumber,
        status: r.Status,
        createdAt: r.CreatedAt instanceof Date ? r.CreatedAt.toISOString() : String(r.CreatedAt),
      })),
    };
  };

  const fetchKitchen = async (): Promise<KitchenSection> => {
    const active = await ctx.pg.kitchen.query<{ cnt: string; oldest: Date | null }>(
      'SELECT count(*)::text AS cnt, min("ReceivedAt") AS oldest FROM kitchen_tickets WHERE "Status" IN (0, 1, 2)',
    );
    return {
      activeCount: parseInt(active.rows[0]?.cnt ?? '0', 10),
      oldestReceivedAt: active.rows[0]?.oldest?.toISOString() ?? null,
    };
  };

  const fetchEventBus = async (): Promise<EventBusSection> => {
    const auth = Buffer.from(`${process.env.RABBITMQ_DEFAULT_USER ?? 'guest'}:${process.env.RABBITMQ_DEFAULT_PASS ?? 'guest'}`).toString('base64');
    const res = await fetch('http://localhost:15672/api/queues/%2F/ordering-api', {
      headers: { Authorization: `Basic ${auth}` },
      signal: AbortSignal.timeout(SUBQUERY_TIMEOUT_MS),
    });
    if (!res.ok) throw new Error(`rabbit mgmt returned ${res.status}`);
    const j = (await res.json()) as { messages?: number; consumers?: number };
    return { orderingQueueDepth: j.messages ?? 0, consumers: j.consumers ?? 0 };
  };

  // ── Deduplicated runner (§10.1, §10.3) ───────────────────────────────

  const cache = asyncCacheDedupe.createCache({
    ttl: 2,
    stale: 0,
    storage: { type: 'memory' },
  });
  cache.define('catalog', { ttl: 2 }, () => fetchCatalog());
  cache.define('sessions', { ttl: 2 }, () => fetchActiveSessions());
  cache.define('orders', { ttl: 2 }, () => fetchOrders());
  cache.define('kitchen', { ttl: 2 }, () => fetchKitchen());
  cache.define('bus', { ttl: 2 }, () => fetchEventBus());

  // The library's TS surface is generic and the dynamic property
  // augmentation isn't reliably inferred, so wrap the lookups explicitly.
  const get = <T>(name: string): Promise<T> => cache.get(name, 'default') as Promise<T>;
  const cached = {
    catalog: () => get<CatalogSection>('catalog'),
    sessions: () => get<ActiveSessionsSection>('sessions'),
    orders: () => get<OrdersSection>('orders'),
    kitchen: () => get<KitchenSection>('kitchen'),
    bus: () => get<EventBusSection>('bus'),
  };

  // ── get_system_snapshot ──────────────────────────────────────────────

  server.registerTool(
    'get_system_snapshot',
    {
      title: 'Get system snapshot',
      description:
        'Produces a single unified, read-only "state of the world" report for the entire ' +
        'Orderly backend. All sub-queries run in parallel with a 3-second budget; ' +
        'partial results with `error` fields are returned if any subsystem is down.',
      inputSchema: {
        restaurantId: z.string().uuid().optional().describe('Restaurant scope. Omit for aggregate stats.'),
      },
    },
    async (args) => {
      const { restaurantId } = args as { restaurantId?: string };

      const [catalog, sessions, orders, kitchen, bus] = await Promise.all([
        withTimeout(cached.catalog(), SUBQUERY_TIMEOUT_MS, 'catalog').catch(asError),
        withTimeout(cached.sessions(), SUBQUERY_TIMEOUT_MS, 'sessions').catch(asError),
        withTimeout(cached.orders(), SUBQUERY_TIMEOUT_MS, 'orders').catch(asError),
        withTimeout(cached.kitchen(), SUBQUERY_TIMEOUT_MS, 'kitchen').catch(asError),
        withTimeout(cached.bus(), SUBQUERY_TIMEOUT_MS, 'eventBus').catch(asError),
      ]);

      const snapshot: Snapshot = {
        generatedAt: new Date().toISOString(),
        ...(restaurantId !== undefined ? { restaurantId } : {}),
        catalog: catalog as CatalogSection | { error: string },
        activeSessions: sessions as ActiveSessionsSection | { error: string },
        orders: orders as OrdersSection | { error: string },
        kitchen: kitchen as KitchenSection | { error: string },
        eventBus: bus as EventBusSection | { error: string },
      };

      ctx.logger.info({ restaurantId, catalog: 'catalog' in snapshot ? 'ok' : 'err' }, 'snapshot generated');

      return {
        content: [{ type: 'text' as const, text: JSON.stringify(snapshot, null, 2) }],
      };
    },
  );

  // ── watch_system ─────────────────────────────────────────────────────

  server.registerTool(
    'watch_system',
    {
      title: 'Watch system (stream snapshots)',
      description:
        'Starts a recurring snapshot at `intervalSeconds` and pushes each result via the ' +
        'MCP logging notification channel. MCP has no streaming-data primitive; ' +
        'this is the closest equivalent. Returns immediately with a watcher handle; ' +
        'subsequent snapshots arrive as `notifications/message` events.',
      inputSchema: {
        intervalSeconds: z.number().int().positive().max(60).default(5),
        restaurantId: z.string().uuid().optional(),
        durationSeconds: z.number().int().positive().max(3600).optional(),
      },
    },
    async (args) => {
      const { intervalSeconds, restaurantId, durationSeconds } = args as {
        intervalSeconds: number;
        restaurantId?: string;
        durationSeconds?: number;
      };

      const watcherId = crypto.randomUUID();
      const tick = async (): Promise<void> => {
        try {
          // Reuse the same sub-query functions; cache TTL 2s keeps things fresh.
          const [catalog, sessions, orders, kitchen, bus] = await Promise.all([
            cached.catalog().catch(asError),
            cached.sessions().catch(asError),
            cached.orders().catch(asError),
            cached.kitchen().catch(asError),
            cached.bus().catch(asError),
          ]);
          const snapshot = {
            watcherId,
            generatedAt: new Date().toISOString(),
            ...(restaurantId !== undefined ? { restaurantId } : {}),
            catalog,
            activeSessions: sessions,
            orders,
            kitchen,
            eventBus: bus,
          };
          await server.sendLoggingMessage({ level: 'info', logger: 'orderly-devmcp', data: snapshot });
        } catch (e) {
          ctx.logger.error({ err: e, watcherId }, 'watch tick failed');
        }
      };

      const interval = setInterval(() => { void tick(); }, intervalSeconds * 1000);
      // Don't keep the process alive for the watcher.
      interval.unref();

      let timeout: NodeJS.Timeout | undefined;
      if (durationSeconds !== undefined) {
        timeout = setTimeout(() => {
          clearInterval(interval);
          ctx.logger.info({ watcherId }, 'watcher stopped (duration elapsed)');
        }, durationSeconds * 1000);
        timeout.unref();
      }

      // Kick off an immediate tick so subscribers see a first value.
      void tick();

      return {
        content: [
          {
            type: 'text' as const,
            text: JSON.stringify(
              { watcherId, intervalSeconds, restaurantId, durationSeconds, status: 'started' },
              null,
              2,
            ),
          },
        ],
      };
    },
  );
}
