/**
 * tools/jobs.ts (§6.7).
 *
 * Two tools:
 *   - seed_historical_sales(restaurantId, daysBack) — bulk-inserts
 *     synthetic completed orders into OrderDb spanning the past
 *     `daysBack` days. Each day gets a deterministic but pseudo-random
 *     volume of orders (seed = restaurantId + daysBack, so the same
 *     input always produces the same output). Uses `mssql`'s bulk
 *     copy (sa is db_owner on the dev container per §10.4).
 *   - trigger_scheduled_jobs(jobName) — HTTP POSTs to a dev-only
 *     endpoint on the relevant service. The endpoint must be gated on
 *     `ASPNETCORE_ENVIRONMENT=Development` plus a shared dev-secret
 *     header per §10.4.
 *
 * Deterministic PRNG: mulberry32 (CC0, single-state, fast, no deps).
 */

import { createHash } from 'node:crypto';
import { z } from 'zod';
import type { McpServer } from '@modelcontextprotocol/sdk/server/mcp.js';

import type { Logger } from '../logger.ts';
import type { ToolContext } from './types.ts';

const KNOWN_JOBS = ['clear-abandoned-baskets', 'daily-reconciliation', 'outbox-relay'] as const;
type KnownJob = (typeof KNOWN_JOBS)[number];

const JOB_ENDPOINTS: Record<KnownJob, string> = {
  'clear-abandoned-baskets': 'http://basket.api:8080/_dev/trigger/clear-abandoned-baskets',
  'daily-reconciliation': 'http://ordering.api:8080/_dev/trigger/daily-reconciliation',
  'outbox-relay': 'http://ordering.api:8080/_dev/trigger/outbox-relay',
};

function mulberry32(seed: number): () => number {
  let a = seed >>> 0;
  return () => {
    a = (a + 0x6D2B79F5) >>> 0;
    let t = a;
    t = Math.imul(t ^ (t >>> 15), t | 1);
    t ^= t + Math.imul(t ^ (t >>> 7), t | 61);
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}

function seedFromString(s: string): number {
  const h = createHash('sha256').update(s).digest();
  return h.readUInt32LE(0);
}

export interface JobsDeps {
  logger: Logger;
  mssql: ToolContext['mssql'];
}

export function registerJobsTools(server: McpServer, deps: JobsDeps): void {
  // ── seed_historical_sales ─────────────────────────────────────────────

  server.registerTool(
    'seed_historical_sales',
    {
      title: 'Seed historical sales',
      description:
        'Bulk-inserts synthetic completed orders into OrderDb spanning the past `daysBack` days. ' +
        'Each day gets a deterministic but pseudo-random volume of orders. ' +
        'Seed = restaurantId + daysBack for reproducibility.',
      inputSchema: {
        restaurantId: z.string().uuid(),
        daysBack: z.number().int().positive().max(365).default(30),
        ordersPerDay: z.number().int().positive().max(500).default(20),
      },
    },
    async (args) => {
      const { restaurantId, daysBack, ordersPerDay } = args as { restaurantId: string; daysBack: number; ordersPerDay: number };

      const seed = seedFromString(`${restaurantId}:${daysBack}`);
      const rng = mulberry32(seed);
      const baseTime = Date.now();

      // Build a CustomerId list — synthetic but stable per (restaurantId, day, i).
      // We need a real Customer row to satisfy the FK; create one synthetic
      // customer per restaurant and reuse it.
      const syntheticCustomerId = crypto.randomUUID();

      // Pre-compute all rows so we can wrap in a single transaction.
      const rows: Array<{
        id: string;
        customerId: string;
        orderNumber: string;
        subtotal: number;
        tax: number;
        total: number;
        ts: Date;
      }> = [];
      for (let d = 0; d < daysBack; d++) {
        const dayCount = Math.max(1, Math.round(ordersPerDay * (0.5 + rng())));
        for (let i = 0; i < dayCount; i++) {
          const subtotal = Math.round((100 + rng() * 800) * 100) / 100;
          const tax = Math.round(subtotal * 0.16 * 100) / 100;
          const totalAmt = Math.round((subtotal + tax) * 100) / 100;
          const ts = new Date(baseTime - (d * 24 + i) * 60 * 60 * 1000);
          rows.push({
            id: crypto.randomUUID(),
            customerId: syntheticCustomerId,
            orderNumber: `HIST-${ts.getTime().toString(36)}-${i.toString(36)}`,
            subtotal,
            tax,
            total: totalAmt,
            ts,
          });
        }
      }

      // Open a transaction. We use parameterized multi-row INSERTs in
      // batches of 50 to keep the SQL small.
      const transaction = new (deps.mssql as unknown as { Transaction: new (c?: unknown) => { begin: () => Promise<void>; commit: () => Promise<void>; rollback: () => Promise<void> } }).Transaction();
      try {
        await transaction.begin();

        // 1. Insert the synthetic customer.
        const custReq = new (deps.mssql as unknown as { Request: new (t?: unknown) => unknown }).Request(transaction);
        (custReq as { input: (k: string, v: unknown) => unknown }).input('id', syntheticCustomerId);
        await (custReq as { query: (s: string) => Promise<unknown> }).query(
          `IF NOT EXISTS (SELECT 1 FROM Customers WHERE Id = @id) ` +
          `INSERT INTO Customers (Id, Email, Name, Phone, CreatedBy, CreatedAt, LastModifiedBy, LastModifiedAt, IsActive) ` +
          `VALUES (@id, 'historical@seed.local', 'Historical Seed', '+52-81-0000-0001', 'devmcp-seed', GETUTCDATE(), 'devmcp-seed', GETUTCDATE(), 1)`,
        );

        // 2. Insert orders in batches. Multi-row VALUES + explicit
        // column list keeps the SQL static (no string interpolation of
        // user data — only $1..$N placeholders).
        const BATCH = 50;
        const COLUMNS = ['Id', 'CustomerId', 'RestaurantId', 'OrderNumber', 'Status', 'OrderType',
          'Subtotal', 'TaxAmount', 'TaxRate', 'TotalAmount', 'Currency', 'ActualPrepTimeMinutes',
          'DiscountAmount', 'DiscountCode', 'DeliveryNotes', 'Notes', 'IsModified', 'RequiresAdminApproval',
          'CreatedByUserId', 'CreatedAt', 'LastModified', 'LastModifiedBy'] as const;
        const STATUS = 'Completed';
        const ORDER_TYPE = 'DineIn';
        const TAX_RATE = 0.16;
        const CURRENCY = 'MXN';

        for (let i = 0; i < rows.length; i += BATCH) {
          const batch = rows.slice(i, i + BATCH);
          // Build the VALUES clause: each row has one placeholder per
          // column, prefixed with @p{j*COLS+k} for uniqueness.
          const valuesSql = batch.map((_, j) => {
            const cells = COLUMNS.map((_, k) => `@p${j * COLUMNS.length + k}`);
            return `(${cells.join(', ')})`;
          }).join(', ');
          const orderReq = new (deps.mssql as unknown as { Request: new (t?: unknown) => unknown }).Request(transaction);
          for (let j = 0; j < batch.length; j++) {
            const r = batch[j]!;
            const placeholders = [
              r.id, r.customerId, restaurantId, r.orderNumber, STATUS, ORDER_TYPE,
              r.subtotal, r.tax, TAX_RATE, r.total, CURRENCY, 0,
              0, '', '', '', 0, 0,
              r.customerId, r.ts, r.ts, 'devmcp-seed',
            ];
            for (let k = 0; k < placeholders.length; k++) {
              (orderReq as { input: (k: string, v: unknown) => unknown }).input(`p${j * COLUMNS.length + k}`, placeholders[k]);
            }
          }
          await (orderReq as { query: (s: string) => Promise<unknown> }).query(
            `INSERT INTO Orders (${COLUMNS.join(', ')}) VALUES ${valuesSql}`,
          );
        }

        await transaction.commit();
        deps.logger.info({ restaurantId, daysBack, total: rows.length }, 'seeded historical sales');
        return {
          content: [{
            type: 'text' as const,
            text: JSON.stringify({ mode: 'executed', total: rows.length, daysBack, ordersPerDay }, null, 2),
          }],
        };
      } catch (cause) {
        await transaction.rollback().catch(() => undefined);
        deps.logger.error({ err: cause, restaurantId }, 'seed_historical_sales failed');
        return {
          content: [{ type: 'text' as const, text: JSON.stringify({ error: 'seed failed', cause: String(cause) }, null, 2) }],
          isError: true,
        };
      }
    },
  );

  // ── trigger_scheduled_jobs ────────────────────────────────────────────

  server.registerTool(
    'trigger_scheduled_jobs',
    {
      title: 'Trigger scheduled job',
      description:
        'HTTP POSTs to a dev-only endpoint on the relevant service to immediately run a ' +
        'scheduled background job. Endpoints must be gated on `ASPNETCORE_ENVIRONMENT=Development` ' +
        'and require a shared dev-secret header per §10.4. The header is loaded from `DEV_TRIGGER_SECRET` env.',
      inputSchema: {
        jobName: z.enum(KNOWN_JOBS),
      },
    },
    async (args) => {
      const { jobName } = args as { jobName: KnownJob };
      const url = JOB_ENDPOINTS[jobName];
      const secret = process.env.DEV_TRIGGER_SECRET;
      if (!secret) {
        return {
          content: [{
            type: 'text' as const,
            text: JSON.stringify({ error: 'DEV_TRIGGER_SECRET not set on the MCP server' }, null, 2),
          }],
          isError: true,
        };
      }

      try {
        const res = await fetch(url, {
          method: 'POST',
          headers: {
            'X-Dev-Trigger-Secret': secret,
            'X-Dev-Trigger-Source': 'orderly-devmcp',
          },
          signal: AbortSignal.timeout(10_000),
        });
        const body = await res.text();
        deps.logger.info({ jobName, status: res.status }, 'triggered scheduled job');
        return {
          content: [{
            type: 'text' as const,
            text: JSON.stringify({ jobName, url, status: res.status, body: body.slice(0, 2048) }, null, 2),
          }],
        };
      } catch (cause) {
        deps.logger.error({ err: cause, jobName }, 'trigger_scheduled_jobs failed');
        return {
          content: [{ type: 'text' as const, text: JSON.stringify({ error: 'trigger failed', cause: String(cause) }, null, 2) }],
          isError: true,
        };
      }
    },
  );
}
