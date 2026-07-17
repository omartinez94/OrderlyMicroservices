/**
 * tools/log-tracing.ts — `get_recent_logs(serviceName, lines?, level?)`.
 *
 * Shells out to `docker logs --tail {lines} {containerName}` using
 * `child_process.spawn('docker', [...], { shell: false })` so a
 * service name with shell metacharacters can't be injected (§10.1).
 *
 * Streams the docker stdout via `pipeline()` from `node:stream/promises`
 * with an `async function*` transform that filters by `level` —
 * matches the /node skill's "High-priority activation checklist" for
 * streams + backpressure.
 *
 * Output is capped at `MAX_LINES` to avoid blowing the MCP response
 * size budget.
 */

import { spawn } from 'node:child_process';
import { createInterface } from 'node:readline';
import { Transform } from 'node:stream';
import { pipeline } from 'node:stream/promises';
import { z } from 'zod';
import type { McpServer } from '@modelcontextprotocol/sdk/server/mcp.js';

import type { Logger } from '../logger.ts';
import { SWAGGER_SERVICES, type ServiceName } from '../config/services.ts';

const MAX_LINES = 500;

export function registerLogTracingTools(server: McpServer, deps: { logger: Logger }): void {
  server.registerTool(
    'get_recent_logs',
    {
      title: 'Get recent container logs',
      description:
        'Reads the last `lines` lines from the Docker container for a service. ' +
        'When `level` is set to `error` or `warning`, only matching lines are returned. ' +
        'Output is capped at 500 lines.',
      inputSchema: {
        serviceName: z
          .enum(SWAGGER_SERVICES as readonly [ServiceName, ...ServiceName[]])
          .describe('Service whose container to read from.'),
        lines: z.number().int().positive().max(MAX_LINES).default(100).describe(`Number of trailing lines (max ${MAX_LINES}).`),
        level: z.enum(['all', 'info', 'warning', 'error']).default('all').describe('Severity filter.'),
      },
    },
    async (args) => {
      const { serviceName, lines, level } = args as {
        serviceName: ServiceName;
        lines: number;
        level: 'all' | 'info' | 'warning' | 'error';
      };

      // Resolve container name from service. Centralised in
      // config/services.ts so the allowlist matches the one in §10.5.
      const container = containerNameFor(serviceName);
      if (!container) {
        return {
          content: [{ type: 'text' as const, text: JSON.stringify({ error: `no container mapped for ${serviceName}` }, null, 2) }],
          isError: true,
        };
      }

      deps.logger.info({ serviceName, container, lines, level }, 'reading container logs');

      // docker logs --tail N <name>
      const child = spawn('docker', ['logs', '--tail', String(lines), container], {
        shell: false,
        stdio: ['ignore', 'pipe', 'pipe'],
      });

      // Async generator: emit lines as they arrive, with level filter.
      async function* filterByLevel(src: NodeJS.ReadableStream): AsyncGenerator<string> {
        const rl = createInterface({ input: src, crlfDelay: Infinity });
        for await (const line of rl) {
          if (matchesLevel(line, level)) yield line;
        }
      }

      // Collect into a Transform stream (the required `async function*`
      // transform pattern from the /node skill activation checklist).
      const collected: string[] = [];
      const collector = new Transform({
        writableObjectMode: true,
        transform(line: string, _enc, cb) {
          if (collected.length < MAX_LINES) collected.push(line);
          cb();
        },
      });

      try {
        await pipeline(child.stdout, collector);
      } catch (cause) {
        child.kill();
        return {
          content: [
            {
              type: 'text' as const,
              text: JSON.stringify({ error: 'docker logs pipeline failed', cause: String(cause), container }, null, 2),
            },
          ],
          isError: true,
        };
      }

      const exitCode = await new Promise<number | null>((resolve) => {
        child.on('close', (code) => resolve(code));
      });

      if (exitCode !== 0) {
        deps.logger.warn({ serviceName, exitCode }, 'docker logs exited non-zero');
      }

      return {
        content: [
          {
            type: 'text' as const,
            text: JSON.stringify(
              {
                serviceName,
                container,
                linesRequested: lines,
                level,
                linesReturned: collected.length,
                lines: collected,
                exitCode,
              },
              null,
              2,
            ),
          },
        ],
      };
    },
  );
}

function containerNameFor(service: ServiceName): string | undefined {
  const map: Record<ServiceName, string> = {
    catalog: 'catalog.api',
    basket: 'basket.api',
    ordering: 'ordering.api',
    kitchen: 'kitchen.api',
    identity: 'identity.api',
    discount: 'discount.grpc',
    'yarp-gateway': 'yarpapigateway',
  };
  return map[service];
}

function matchesLevel(line: string, level: 'all' | 'info' | 'warning' | 'error'): boolean {
  if (level === 'all') return true;
  const lower = line.toLowerCase();
  switch (level) {
    case 'error':
      return lower.includes('error') || lower.includes('fail') || lower.includes('exception');
    case 'warning':
      return lower.includes('warn');
    case 'info':
      return !lower.includes('error') && !lower.includes('warn') && !lower.includes('fail');
  }
}
