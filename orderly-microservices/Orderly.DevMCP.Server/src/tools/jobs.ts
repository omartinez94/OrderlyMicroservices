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
 * Deterministic PRNG: mulberry32 (CC0, single-state, fast, no deps),
 * shared with the data-seeding tools via `util/random.ts`.
 */

import sql from 'mssql';
import { z } from 'zod';
import type { McpServer } from '@modelcontextprotocol/sdk/server/mcp.js';

import type { Logger } from '../logger.ts';
import type { ToolContext } from './types.ts';
import { generateShortId } from '../util/short-id.ts';
import { mulberry32, seedFromString } from '../util/random.ts';
import { ORDER_TAX_RATE } from '../util/order-tax.ts';

const KNOWN_JOBS = ['clear-abandoned-baskets', 'daily-reconciliation', 'outbox-relay'] as const;
type KnownJob = (typeof KNOWN_JOBS)[number];

// The MCP server runs on the HOST, not inside the compose network, so
// the Docker-internal service names (`basket.api:8080`) do not resolve
// from here. Use the ports the dev compose override publishes
// (basket.api -> 6001, ordering.api -> 6003).
const JOB_ENDPOINTS: Record<KnownJob, string> = {
  'clear-abandoned-baskets': 'http://localhost:6001/_dev/trigger/clear-abandoned-baskets',
  'daily-reconciliation': 'http://localhost:6003/_dev/trigger/daily-reconciliation',
  'outbox-relay': 'http://localhost:6003/_dev/trigger/outbox-relay',
};

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

      // We need a real Customer row to satisfy the FK on
      // Orders.CustomerId. The customer is keyed by a fixed email
      // ('historical@seed.local') so repeated seeds reuse it, and its
      // Id is READ BACK from the database rather than generated —
      // a fresh UUID each run would not match the row that already
      // exists, and the FK insert would fail.
      const syntheticCustomerId = crypto.randomUUID();

      // Pre-compute all rows so we can wrap in a single transaction.
      const rows: Array<{
        id: string;
        shortId: string;
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
          const tax = Math.round(subtotal * ORDER_TAX_RATE * 100) / 100;
          const totalAmt = Math.round((subtotal + tax) * 100) / 100;
          const ts = new Date(baseTime - (d * 24 + i) * 60 * 60 * 1000);
          rows.push({
            id: crypto.randomUUID(),
            shortId: generateShortId('ord'),
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
      //
      // `sql.Transaction` / `sql.Request` are the real mssql
      // constructors — the previous shape read `Transaction` off the
      // POOL object, which has no such member, so every call threw
      // "deps.mssql.Transaction is not a constructor". The pool is
      // the transaction's connection source, not the transaction.
      const transaction = new sql.Transaction(deps.mssql);
      try {
        await transaction.begin();

        // 1. Insert the synthetic customer.
        //
        // The guard must check EMAIL, not Id: `IX_Customers_Email`
        // is a unique index and the seed reuses the fixed address
        // 'historical@seed.local' on every run. Checking only Id
        // (a fresh random UUID each time) let the second run collide
        // with "Cannot insert duplicate key row in object
        // 'dbo.Customers'".
        const custReq = new sql.Request(transaction);
        custReq.input('id', syntheticCustomerId);
        custReq.input('email', 'historical@seed.local');
        await custReq.query(
          `IF NOT EXISTS (SELECT 1 FROM Customers WHERE Email = @email) ` +
          `INSERT INTO Customers (Id, Email, Name, Phone, CreatedBy, CreatedAt, LastModifiedBy, LastModifiedAt, IsActive) ` +
          `VALUES (@id, @email, 'Historical Seed', '+52-81-0000-0001', 'devmcp-seed', GETUTCDATE(), 'devmcp-seed', GETUTCDATE(), 1)`,
        );

        // Read the customer's actual Id back. On a re-run the row
        // already exists with the Id from the FIRST run, so the
        // generated UUID is not the Id the Orders rows must point
        // at — using it broke the FK on Orders.CustomerId.
        const custIdReq = new sql.Request(transaction);
        custIdReq.input('email', 'historical@seed.local');
        const custIdRes = await custIdReq.query<{ Id: string }>(
          `SELECT Id FROM Customers WHERE Email = @email`,
        );
        const customerId = custIdRes.recordset[0]?.Id;
        if (!customerId) {
          throw new Error('historical customer row missing after insert');
        }
        // Re-point every pre-computed row at the persisted customer.
        for (const row of rows) {
          row.customerId = customerId;
        }

        // 2. Insert orders in batches. Multi-row VALUES + explicit
        // column list keeps the SQL static (no string interpolation of
        // user data — only $1..$N placeholders).
        const BATCH = 50;
        // The column list must cover every NOT NULL column on
        // `Orders` (33 of them). The previous list carried only 23,
        // so the address / payment / prep-time columns were left to
        // their defaults and the insert failed with
        // "Cannot insert the value NULL into column
        // 'EstimatedPrepTimeMinutes'". The shape below mirrors the
        // working INSERT in `data-seeding.ts` (`create_mock_order`).
        const COLUMNS = ['Id', 'ShortId', 'CustomerId', 'RestaurantId', 'OrderNumber', 'Status', 'OrderType',
          'Subtotal', 'TaxAmount', 'TaxRate', 'TotalAmount', 'Currency',
          'DeliveryAddress_Street', 'DeliveryAddress_City', 'DeliveryAddress_State', 'DeliveryAddress_ZipCode', 'DeliveryAddress_Country',
          'BillingAddress_Street', 'BillingAddress_City', 'BillingAddress_State', 'BillingAddress_ZipCode', 'BillingAddress_Country',
          'Payment_Method', 'Payment_Brand', 'Payment_LastFour',
          'ActualPrepTimeMinutes', 'EstimatedPrepTimeMinutes', 'DiscountAmount', 'DiscountCode', 'DeliveryNotes', 'Notes',
          'IsModified', 'RequiresAdminApproval',
          'CreatedByUserId', 'CreatedAt', 'LastModified', 'LastModifiedBy'] as const;
        const STATUS = 'Completed';
        const ORDER_TYPE = 'DineIn';
        // Orders."TaxRate" is a FRACTION (decimal(18,2); the ordering DTO
        // validator caps it at 1) — the shared constant already is one.
        const TAX_RATE = ORDER_TAX_RATE;
        const CURRENCY = 'MXN';
        // Fixed address / payment values for the synthetic history.
        const SEED_ADDRESS = {
          street: '1 Test St',
          city: 'Monterrey',
          state: 'NL',
          zipCode: '64000',
          country: 'MX',
        } as const;

        for (let i = 0; i < rows.length; i += BATCH) {
          const batch = rows.slice(i, i + BATCH);
          // Build the VALUES clause: each row has one placeholder per
          // column, prefixed with @p{j*COLS+k} for uniqueness.
          const valuesSql = batch.map((_, j) => {
            const cells = COLUMNS.map((_, k) => `@p${j * COLUMNS.length + k}`);
            return `(${cells.join(', ')})`;
          }).join(', ');
          const orderReq = new sql.Request(transaction);
          for (let j = 0; j < batch.length; j++) {
            const r = batch[j]!;
            const placeholders = [
              r.id, r.shortId, r.customerId, restaurantId, r.orderNumber, STATUS, ORDER_TYPE,
              r.subtotal, r.tax, TAX_RATE, r.total, CURRENCY,
              SEED_ADDRESS.street, SEED_ADDRESS.city, SEED_ADDRESS.state, SEED_ADDRESS.zipCode, SEED_ADDRESS.country,
              SEED_ADDRESS.street, SEED_ADDRESS.city, SEED_ADDRESS.state, SEED_ADDRESS.zipCode, SEED_ADDRESS.country,
              1, 'Visa', '1111',
              0, 20,
              0, '', '', '', 0, 0,
              // `CreatedByUserId` and `LastModifiedBy` are
              // uniqueidentifier columns — the literal
              // 'devmcp-seed' could not convert and failed the whole
              // insert with "Conversion failed when converting from
              // a character string to uniqueidentifier".
              r.customerId, r.ts, r.ts, r.customerId,
            ];
            for (let k = 0; k < placeholders.length; k++) {
              orderReq.input(`p${j * COLUMNS.length + k}`, placeholders[k]);
            }
          }
          await orderReq.query(
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
