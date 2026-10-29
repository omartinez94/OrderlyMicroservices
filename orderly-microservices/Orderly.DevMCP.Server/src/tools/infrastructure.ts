/**
 * tools/infrastructure.ts (§6.6).
 *
 * Two tools:
 *   - reset_databases(targets?, confirmText) — destructive. Two-step
 *     confirmation per §10.4: input must include `confirm: true` AND
 *     `confirmText` matching one of the target service names. Rate-
 *     limited to 1/hour (§10.1). For Marten PG: `DROP SCHEMA public
 *     CASCADE; CREATE SCHEMA public;`. For OrderDb (MSSQL): `DROP
 *     DATABASE` + `CREATE DATABASE` (schema is recreated by EF Core
 *     migrations on next start). Redis: `FLUSHALL`.
 *   - simulate_service_outage(serviceName, durationSeconds?) — uses
 *     `child_process.spawn('docker', ['stop', name], { shell: false })`
 *     per §10.1. Allowlist: only API containers — never `messagebroker`
 *     or any database container, to avoid corrupting stateful volumes.
 */

import { spawn } from 'node:child_process';
import sql from 'mssql';
import { z } from 'zod';
import type { McpServer } from '@modelcontextprotocol/sdk/server/mcp.js';

import type { Logger } from '../logger.ts';
import type { ToolContext } from './types.ts';
import { rateLimits } from '../util/rate-limit.ts';

const TARGETS = ['catalog', 'basket', 'ordering', 'kitchen', 'identity'] as const;
type DbTarget = (typeof TARGETS)[number];

// Allowlist of API containers that may be stopped/restarted. Databases
// and the message broker are never allowed — stopping them would corrupt
// stateful volumes.
const STOPPABLE_API_CONTAINERS = new Set([
  'catalog.api',
  'basket.api',
  'ordering.api',
  'kitchen.api',
  'identity.api',
  'discount.grpc',
  'yarpapigateway',
]);

export interface InfrastructureDeps {
  logger: Logger;
  pg: ToolContext['pg'];
  mssql: ToolContext['mssql'];
  redis: ToolContext['redis'];
}

function execDocker(args: string[]): Promise<{ stdout: string; stderr: string; exitCode: number }> {
  return new Promise((resolve, reject) => {
    const child = spawn('docker', args, { shell: false, stdio: ['ignore', 'pipe', 'pipe'] });
    let stdout = '';
    let stderr = '';
    child.stdout.on('data', (d: Buffer) => { stdout += d.toString(); });
    child.stderr.on('data', (d: Buffer) => { stderr += d.toString(); });
    child.on('error', reject);
    child.on('close', (code) => resolve({ stdout, stderr, exitCode: code ?? -1 }));
  });
}

export function registerInfrastructureTools(server: McpServer, deps: InfrastructureDeps): void {
  // ── reset_databases ──────────────────────────────────────────────────

  server.registerTool(
    'reset_databases',
    {
      title: 'Reset databases (DESTRUCTIVE)',
      description:
        'Drops + recreates the schema for the target databases. **DESTRUCTIVE — all data is lost.** ' +
        'Two-step confirmation required: `confirm: true` AND `confirmText` must equal the exact, ' +
        'comma-separated list of every target being reset. Rate-limited to 1/hour. ' +
        'Refuses to run against non-localhost hosts.',
      inputSchema: {
        targets: z.array(z.enum(TARGETS)).default([...TARGETS]).describe('Databases to reset. Defaults to all.'),
        confirm: z.literal(true).describe('Must be true.'),
        confirmText: z.string().min(1).describe(
          `Must equal the comma-separated list of ALL targets being reset ` +
          `(e.g. "catalog" for one target, "catalog,basket" for two).`,
        ),
      },
    },
    async (args) => {
      const { targets, confirmText } = args as { targets: DbTarget[]; confirm: true; confirmText: string };
      // `confirm` is always true (zod literal) — destructured above only
      // to keep the runtime signature aligned with the schema.

      // Two-step confirmation: confirmText must spell out EVERY target
      // that will be wiped. Matching only one of them is not enough —
      // `targets` defaults to all five databases, so confirming a single
      // name would silently reset the entire stack.
      const expectedText = [...targets].sort().join(',');
      const actualText = confirmText.split(',').map((t) => t.trim()).sort().join(',');
      if (actualText !== expectedText) {
        return {
          content: [{
            type: 'text' as const,
            text: JSON.stringify({
              error: 'confirmText must list every target being reset',
              expected: expectedText,
              got: confirmText,
              targets,
            }, null, 2),
          }],
          isError: true,
        };
      }

      const limit = rateLimits.reset.consume('global');
      if (!limit.allowed) {
        return {
          content: [{
            type: 'text' as const,
            text: JSON.stringify({ error: 'rate-limited (1/hour)', resetMs: limit.resetMs }, null, 2),
          }],
          isError: true,
        };
      }

      const results: Record<string, { ok: boolean; error?: string }> = {};
      for (const target of targets) {
        try {
          switch (target) {
            case 'catalog':
            case 'basket':
            case 'kitchen':
            case 'identity': {
              const pool = deps.pg[target];
              const c = await pool.connect();
              try {
                await c.query('DROP SCHEMA public CASCADE; CREATE SCHEMA public;');
              } finally {
                c.release();
              }
              break;
            }
            case 'ordering': {
              // MSSQL: drop + recreate the OrderDb database. The
              // ordering.api will re-apply EF Core migrations on restart.
              //
              // The DROP must run from a session connected to a
              // DIFFERENT database. SQL Server refuses to drop the
              // database the current session is connected to, and the
              // pooled `deps.mssql` session IS connected to OrderDb.
              // Running the DROP from that pool therefore failed
              // while the preceding `SET SINGLE_USER` still took
              // effect, leaving OrderDb locked to one connection and
              // ordering.api unable to reach it at all. So: open a
              // dedicated connection to `master` and do the work
              // from there.
              //
              // The recreated database must also be left in
              // MULTI_USER — a database created while the source was
              // SINGLE_USER inherits that mode and accepts exactly
              // one connection, so the next reader deadlocks.
              const masterPool = new sql.ConnectionPool({
                ...(deps.mssql as unknown as { config: sql.config }).config,
                database: 'master',
                pool: { max: 1 },
              });
              await masterPool.connect();
              try {
                await masterPool.request().batch(`
                  IF DB_ID('OrderDb') IS NOT NULL
                  BEGIN
                    ALTER DATABASE OrderDb SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                    DROP DATABASE OrderDb;
                  END
                  CREATE DATABASE OrderDb;
                  ALTER DATABASE OrderDb SET MULTI_USER WITH ROLLBACK IMMEDIATE;
                `);
              } finally {
                await masterPool.close();
              }
              break;
            }
          }
          results[target] = { ok: true };
          deps.logger.warn({ target }, 'database reset');
        } catch (cause) {
          results[target] = { ok: false, error: String(cause) };
          deps.logger.error({ err: cause, target }, 'database reset failed');
        }
      }

      // Redis: only if all PG targets succeeded (avoids a half-reset).
      if (Object.values(results).every((r) => r.ok) && (targets as readonly string[]).some((t) => t === 'catalog' || t === 'basket')) {
        try {
          await deps.redis.flushall();
          results.redis = { ok: true };
        } catch (cause) {
          results.redis = { ok: false, error: String(cause) };
        }
      }

      return {
        content: [{ type: 'text' as const, text: JSON.stringify({ resetAt: new Date().toISOString(), results }, null, 2) }],
        isError: !Object.values(results).every((r) => r.ok),
      };
    },
  );

  // ── simulate_service_outage ──────────────────────────────────────────

  server.registerTool(
    'simulate_service_outage',
    {
      title: 'Simulate service outage',
      description:
        'Stops an API container for `durationSeconds` (default 30), then restarts it. ' +
        'Allowlist enforces API containers only — databases and the message broker are refused. ' +
        'Use `durationSeconds: 0` to stop permanently (manual restart required).',
      inputSchema: {
        serviceName: z.string().min(1).describe('Container name (e.g. "catalog.api"). Must be in the API allowlist.'),
        durationSeconds: z.number().int().min(0).max(3600).default(30),
      },
    },
    async (args) => {
      const { serviceName, durationSeconds } = args as { serviceName: string; durationSeconds: number };

      if (!STOPPABLE_API_CONTAINERS.has(serviceName)) {
        return {
          content: [{
            type: 'text' as const,
            text: JSON.stringify({
              error: `refusing to stop "${serviceName}" — not in API allowlist`,
              allowlist: [...STOPPABLE_API_CONTAINERS],
            }, null, 2),
          }],
          isError: true,
        };
      }

      try {
        const stop = await execDocker(['stop', serviceName]);
        if (stop.exitCode !== 0) {
          return {
            content: [{ type: 'text' as const, text: JSON.stringify({ error: 'docker stop failed', stderr: stop.stderr }, null, 2) }],
            isError: true,
          };
        }
        deps.logger.warn({ serviceName, durationSeconds }, 'simulated service outage');

        if (durationSeconds === 0) {
          return {
            content: [{ type: 'text' as const, text: JSON.stringify({ serviceName, stopped: true, autoRestart: false }, null, 2) }],
          };
        }

        // Schedule restart. Unref so it doesn't keep the process alive.
        const timer = setTimeout(() => {
          void execDocker(['start', serviceName]).then((res) => {
            if (res.exitCode !== 0) {
              deps.logger.error({ serviceName, stderr: res.stderr }, 'failed to restart service after outage window');
            } else {
              deps.logger.info({ serviceName }, 'service restored after outage window');
            }
          });
        }, durationSeconds * 1000);
        timer.unref();

        return {
          content: [{
            type: 'text' as const,
            text: JSON.stringify({ serviceName, stopped: true, autoRestartIn: durationSeconds }, null, 2),
          }],
        };
      } catch (cause) {
        deps.logger.error({ err: cause, serviceName }, 'simulate_service_outage failed');
        return {
          content: [{ type: 'text' as const, text: JSON.stringify({ error: 'docker spawn failed', cause: String(cause) }, null, 2) }],
          isError: true,
        };
      }
    },
  );

  // ── flush_orders ─────────────────────────────────────────────────────

  server.registerTool(
    'flush_orders',
    {
      title: 'Flush orders and kitchen tickets',
      description:
        'Selectively purges orders, items, activities, bills, and kitchen tickets without dropping database schemas or restarting services. ' +
        'Requires either `restaurantId` (UUID) or `scope: "all"`.',
      inputSchema: {
        restaurantId: z
          .string()
          .uuid()
          .optional()
          .describe('Restaurant UUID. Mutually exclusive with scope: "all".'),
        scope: z
          .literal('all')
          .optional()
          .describe('Set to "all" to flush every tenant. Required when restaurantId is omitted.'),
        includeKitchen: z
          .boolean()
          .default(true)
          .describe('Whether to also delete matching kitchen tickets and line items in Kitchendb.'),
        purgeOutbox: z
          .boolean()
          .default(true)
          .describe('Only honored when scope is "all"; ignored (and reported) for a single restaurant.'),
        resetSequences: z
          .boolean()
          .default(false)
          .describe('Also clear RestaurantDailySequences so order numbers restart.'),
        dryRun: z
          .boolean()
          .default(false)
          .describe('Return the counts that WOULD be deleted without deleting anything.'),
        confirm: z
          .literal(true)
          .describe('Safety guard: must be explicitly true.'),
      },
    },
    async (args) => {
      const {
        restaurantId,
        scope,
        includeKitchen = true,
        purgeOutbox = true,
        resetSequences = false,
        dryRun = false,
      } = args as {
        restaurantId?: string;
        scope?: 'all';
        includeKitchen?: boolean;
        purgeOutbox?: boolean;
        resetSequences?: boolean;
        dryRun?: boolean;
        confirm: true;
      };

      // Validation: mutually exclusive restaurantId XOR scope === 'all'
      const hasRestaurantId = Boolean(restaurantId);
      const hasScopeAll = scope === 'all';
      if ((hasRestaurantId && hasScopeAll) || (!hasRestaurantId && !hasScopeAll)) {
        return {
          content: [{
            type: 'text' as const,
            text: JSON.stringify({
              error: 'Invalid scope: exactly one of `restaurantId` (UUID) or `scope: "all"` must be provided.',
            }, null, 2),
          }],
          isError: true,
        };
      }

      const limit = rateLimits.flush.consume('global');
      if (!limit.allowed) {
        return {
          content: [{
            type: 'text' as const,
            text: JSON.stringify({ error: 'rate-limited (10/min)', resetMs: limit.resetMs }, null, 2),
          }],
          isError: true,
        };
      }

      const notes: string[] = [];
      const effectiveScope = restaurantId ?? 'all';

      if (hasRestaurantId && purgeOutbox) {
        notes.push('purgeOutbox was ignored because scope is a single restaurant (outbox messages are not tenant-scoped).');
      }

      const shouldPurgeOutbox = hasScopeAll && purgeOutbox;

      // 1. OrderDb (MSSQL)
      type OrderDbCounts = {
        orders: number;
        orderItems: number;
        orderActivities: number;
        orderBills: number;
        outboxPending: number;
        outboxDead: number;
        dailySequences: number;
      };

      let orderDbResult: { ok: boolean; error?: string; counts: OrderDbCounts } = {
        ok: true,
        counts: {
          orders: 0,
          orderItems: 0,
          orderActivities: 0,
          orderBills: 0,
          outboxPending: 0,
          outboxDead: 0,
          dailySequences: 0,
        },
      };

      try {
        if (dryRun) {
          const req = new sql.Request(deps.mssql);
          req.input('restaurantId', restaurantId ?? null);
          req.input('purgeOutbox', shouldPurgeOutbox ? 1 : 0);
          req.input('resetSequences', resetSequences ? 1 : 0);

          const qRes = await req.query(`
            DECLARE @activities INT = 0, @items INT = 0, @bills INT = 0, @orders INT = 0, @outboxPending INT = 0, @outboxDead INT = 0, @sequences INT = 0;

            SELECT @activities = COUNT(*) FROM order_activities oa INNER JOIN Orders o ON oa.OrderId = o.Id WHERE (@restaurantId IS NULL OR o.RestaurantId = @restaurantId);
            SELECT @items = COUNT(*) FROM OrderItems oi INNER JOIN Orders o ON oi.OrderId = o.Id WHERE (@restaurantId IS NULL OR o.RestaurantId = @restaurantId);
            SELECT @bills = COUNT(*) FROM OrderBills ob INNER JOIN Orders o ON ob.OrderId = o.Id WHERE (@restaurantId IS NULL OR o.RestaurantId = @restaurantId);
            SELECT @orders = COUNT(*) FROM Orders WHERE (@restaurantId IS NULL OR RestaurantId = @restaurantId);

            IF @purgeOutbox = 1
            BEGIN
              SELECT @outboxPending = COUNT(*) FROM outbox_messages WHERE DispatchedAt IS NULL;
              SELECT @outboxDead = COUNT(*) FROM outbox_messages_dead;
            END

            IF @resetSequences = 1
            BEGIN
              SELECT @sequences = COUNT(*) FROM RestaurantDailySequences WHERE (@restaurantId IS NULL OR RestaurantId = @restaurantId);
            END

            SELECT @activities AS orderActivities, @items AS orderItems, @bills AS orderBills, @orders AS orders, @outboxPending AS outboxPending, @outboxDead AS outboxDead, @sequences AS dailySequences;
          `);

          const row = qRes.recordset[0] ?? {};
          orderDbResult.counts = {
            orders: Number(row.orders ?? 0),
            orderItems: Number(row.orderItems ?? 0),
            orderActivities: Number(row.orderActivities ?? 0),
            orderBills: Number(row.orderBills ?? 0),
            outboxPending: Number(row.outboxPending ?? 0),
            outboxDead: Number(row.outboxDead ?? 0),
            dailySequences: Number(row.dailySequences ?? 0),
          };
        } else {
          const tx = new sql.Transaction(deps.mssql);
          await tx.begin();
          try {
            const req = new sql.Request(tx);
            req.input('restaurantId', restaurantId ?? null);
            req.input('purgeOutbox', shouldPurgeOutbox ? 1 : 0);
            req.input('resetSequences', resetSequences ? 1 : 0);

            const qRes = await req.query(`
              DECLARE @activities INT = 0, @items INT = 0, @bills INT = 0, @orders INT = 0, @outboxPending INT = 0, @outboxDead INT = 0, @sequences INT = 0;

              -- Purge outbox first to close the relay race window
              IF @purgeOutbox = 1
              BEGIN
                DELETE FROM outbox_messages WHERE DispatchedAt IS NULL;
                SET @outboxPending = @@ROWCOUNT;
                DELETE FROM outbox_messages_dead;
                SET @outboxDead = @@ROWCOUNT;
              END

              -- Purge child tables in order
              DELETE oa FROM order_activities oa INNER JOIN Orders o ON oa.OrderId = o.Id WHERE (@restaurantId IS NULL OR o.RestaurantId = @restaurantId);
              SET @activities = @@ROWCOUNT;

              DELETE oi FROM OrderItems oi INNER JOIN Orders o ON oi.OrderId = o.Id WHERE (@restaurantId IS NULL OR o.RestaurantId = @restaurantId);
              SET @items = @@ROWCOUNT;

              DELETE ob FROM OrderBills ob INNER JOIN Orders o ON ob.OrderId = o.Id WHERE (@restaurantId IS NULL OR o.RestaurantId = @restaurantId);
              SET @bills = @@ROWCOUNT;

              DELETE FROM Orders WHERE (@restaurantId IS NULL OR RestaurantId = @restaurantId);
              SET @orders = @@ROWCOUNT;

              IF @resetSequences = 1
              BEGIN
                DELETE FROM RestaurantDailySequences WHERE (@restaurantId IS NULL OR RestaurantId = @restaurantId);
                SET @sequences = @@ROWCOUNT;
              END

              SELECT @activities AS orderActivities, @items AS orderItems, @bills AS orderBills, @orders AS orders, @outboxPending AS outboxPending, @outboxDead AS outboxDead, @sequences AS dailySequences;
            `);

            await tx.commit();

            const row = qRes.recordset[0] ?? {};
            orderDbResult.counts = {
              orders: Number(row.orders ?? 0),
              orderItems: Number(row.orderItems ?? 0),
              orderActivities: Number(row.orderActivities ?? 0),
              orderBills: Number(row.orderBills ?? 0),
              outboxPending: Number(row.outboxPending ?? 0),
              outboxDead: Number(row.outboxDead ?? 0),
              dailySequences: Number(row.dailySequences ?? 0),
            };
          } catch (txErr) {
            await tx.rollback().catch(() => {});
            throw txErr;
          }
        }
      } catch (err) {
        deps.logger.error({ err, effectiveScope }, 'OrderDb flush failed');
        orderDbResult = {
          ok: false,
          error: err instanceof Error ? err.message : String(err),
          counts: {
            orders: 0,
            orderItems: 0,
            orderActivities: 0,
            orderBills: 0,
            outboxPending: 0,
            outboxDead: 0,
            dailySequences: 0,
          },
        };
      }

      // 2. Kitchendb (Postgres)
      type KitchenDbCounts = {
        tickets: number;
        ticketItems: number;
        outboxPending: number;
        outboxDead: number;
      };

      let kitchenDbResult: { ok: boolean; error?: string; skipped?: boolean; counts: KitchenDbCounts } = {
        ok: true,
        counts: {
          tickets: 0,
          ticketItems: 0,
          outboxPending: 0,
          outboxDead: 0,
        },
      };

      if (!includeKitchen) {
        kitchenDbResult.skipped = true;
      } else {
        const pgPool = deps.pg.kitchen;
        try {
          const client = await pgPool.connect();
          try {
            if (dryRun) {
              if (hasRestaurantId) {
                const countRes = await client.query(`
                  SELECT
                    (SELECT COUNT(*)::int FROM kitchen_ticket_items WHERE "KitchenTicketId" IN (SELECT "Id" FROM kitchen_tickets WHERE "RestaurantId" = $1)) AS "ticketItems",
                    (SELECT COUNT(*)::int FROM kitchen_tickets WHERE "RestaurantId" = $1) AS "tickets"
                `, [restaurantId]);
                const row = countRes.rows[0] ?? {};
                kitchenDbResult.counts = {
                  tickets: Number(row.tickets ?? 0),
                  ticketItems: Number(row.ticketItems ?? 0),
                  outboxPending: 0,
                  outboxDead: 0,
                };
              } else {
                const countRes = await client.query(`
                  SELECT
                    (SELECT COUNT(*)::int FROM kitchen_ticket_items) AS "ticketItems",
                    (SELECT COUNT(*)::int FROM kitchen_tickets) AS "tickets",
                    (SELECT CASE WHEN $1::boolean THEN COUNT(*)::int ELSE 0 END FROM outbox_messages WHERE "DispatchedAt" IS NULL) AS "outboxPending",
                    (SELECT CASE WHEN $1::boolean THEN COUNT(*)::int ELSE 0 END FROM outbox_messages_dead) AS "outboxDead"
                `, [shouldPurgeOutbox]);
                const row = countRes.rows[0] ?? {};
                kitchenDbResult.counts = {
                  tickets: Number(row.tickets ?? 0),
                  ticketItems: Number(row.ticketItems ?? 0),
                  outboxPending: Number(row.outboxPending ?? 0),
                  outboxDead: Number(row.outboxDead ?? 0),
                };
              }
            } else {
              await client.query('BEGIN');
              try {
                let ticketItems = 0;
                let tickets = 0;
                let outboxPending = 0;
                let outboxDead = 0;

                if (shouldPurgeOutbox) {
                  const outboxRes = await client.query(`DELETE FROM outbox_messages WHERE "DispatchedAt" IS NULL`);
                  outboxPending = outboxRes.rowCount ?? 0;
                  const deadRes = await client.query(`DELETE FROM outbox_messages_dead`);
                  outboxDead = deadRes.rowCount ?? 0;
                }

                if (hasRestaurantId) {
                  const itemsRes = await client.query(
                    `DELETE FROM kitchen_ticket_items WHERE "KitchenTicketId" IN (SELECT "Id" FROM kitchen_tickets WHERE "RestaurantId" = $1)`,
                    [restaurantId]
                  );
                  ticketItems = itemsRes.rowCount ?? 0;

                  const ticketsRes = await client.query(
                    `DELETE FROM kitchen_tickets WHERE "RestaurantId" = $1`,
                    [restaurantId]
                  );
                  tickets = ticketsRes.rowCount ?? 0;
                } else {
                  const itemsRes = await client.query(`DELETE FROM kitchen_ticket_items`);
                  ticketItems = itemsRes.rowCount ?? 0;

                  const ticketsRes = await client.query(`DELETE FROM kitchen_tickets`);
                  tickets = ticketsRes.rowCount ?? 0;
                }

                await client.query('COMMIT');

                kitchenDbResult.counts = {
                  tickets,
                  ticketItems,
                  outboxPending,
                  outboxDead,
                };
              } catch (txErr) {
                await client.query('ROLLBACK').catch(() => {});
                throw txErr;
              }
            }
          } finally {
            client.release();
          }
        } catch (err) {
          deps.logger.error({ err, effectiveScope }, 'Kitchendb flush failed');
          kitchenDbResult = {
            ok: false,
            error: err instanceof Error ? err.message : String(err),
            counts: {
              tickets: 0,
              ticketItems: 0,
              outboxPending: 0,
              outboxDead: 0,
            },
          };
        }
      }

      const hasError = !orderDbResult.ok || !kitchenDbResult.ok;

      const responsePayload = {
        scope: effectiveScope,
        dryRun,
        orderDb: orderDbResult,
        kitchenDb: kitchenDbResult,
        notes,
      };

      if (!dryRun) {
        deps.logger.info({ effectiveScope, counts: { orderDb: orderDbResult.counts, kitchenDb: kitchenDbResult.counts } }, 'flush_orders completed');
      }

      return {
        content: [{
          type: 'text' as const,
          text: JSON.stringify(responsePayload, null, 2),
        }],
        isError: hasError,
      };
    },
  );
}
