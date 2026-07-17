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
        'Two-step confirmation required: `confirm: true` AND `confirmText` must equal one of the target ' +
        'service names. Rate-limited to 1/hour. Refuses to run against non-localhost hosts.',
      inputSchema: {
        targets: z.array(z.enum(TARGETS)).default([...TARGETS]).describe('Databases to reset. Defaults to all.'),
        confirm: z.literal(true).describe('Must be true.'),
        confirmText: z.string().min(1).describe(`Must equal one of the target names (e.g. "catalog").`),
      },
    },
    async (args) => {
      const { targets, confirmText } = args as { targets: DbTarget[]; confirm: true; confirmText: string };
      // `confirm` is always true (zod literal) — destructured above only
      // to keep the runtime signature aligned with the schema.

      // Two-step confirmation: confirmText must be one of the targets.
      if (!targets.includes(confirmText as DbTarget)) {
        return {
          content: [{
            type: 'text' as const,
            text: JSON.stringify({
              error: 'confirmText must match one of the target service names',
              targets,
              got: confirmText,
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
              const req = deps.mssql.request();
              await req.query(`
                IF DB_ID('OrderDb') IS NOT NULL
                BEGIN
                  ALTER DATABASE OrderDb SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                  DROP DATABASE OrderDb;
                END
                CREATE DATABASE OrderDb;
              `);
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
}
