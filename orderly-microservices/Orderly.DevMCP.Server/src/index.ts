/**
 * Orderly.DevMCP.Server — Phase 1 boot.
 *
 * Loads env, boots the logger, opens all DB / cache / broker connections,
 * starts the MCP server over HTTP (StreamableHTTPServerTransport), and
 * wires graceful shutdown. Zero tools are registered in Phase 1 — the
 * Inspector should connect and list an empty tool array.
 *
 * Sequence on boot:
 *   1. zod env validation (refuses to start on bad config or non-dev NODE_ENV)
 *   2. Logger ready
 *   3. assertDevHost guard inside each connection factory
 *   4. Open all connections (fail-fast — exit 1 if any backend is down)
 *   5. Bind HTTP listener on HOST:PORT
 *   6. McpServer.connect(transport)
 *   7. Install signal + unhandled-error handlers
 *
 * Sequence on SIGTERM / SIGINT:
 *   1. close-with-grace captures signal, waits up to 10s
 *   2. transport.close()
 *   3. http server stops accepting new requests, drains in-flight
 *   4. rabbit.close()
 *   5. redis.quit()
 *   6. pg Pool.end() per service
 *   7. mssql pool.close()
 *   8. process.exit(0)
 */

import { randomUUID } from 'node:crypto';
import { createServer, type IncomingMessage, type ServerResponse } from 'node:http';
import type { Transport } from '@modelcontextprotocol/sdk/shared/transport.js';
import { McpServer } from '@modelcontextprotocol/sdk/server/mcp.js';
import { StreamableHTTPServerTransport } from '@modelcontextprotocol/sdk/server/streamableHttp.js';
import closeWithGrace from 'close-with-grace';

import { env } from './config/env.ts';
import { logger } from './logger.ts';
import { selfUrl } from './config/services.ts';
import {
  createPostgresPool,
  pingPostgres,
  type PostgresService,
} from './db/postgres-client.ts';
import { createAndConnectMssql, pingMssql } from './db/mssql-client.ts';
import { createRedis, pingRedis } from './db/redis-client.ts';
import { createRabbit, pingRabbit } from './db/rabbitmq-client.ts';
import { ConnectionError } from './errors/DevMCPError.ts';

// ─── Step 1: Banner log (logger is initialised above) ─────────────────────────

logger.info(
  { host: env.HOST, port: env.PORT, nodeEnv: env.NODE_ENV },
  'DevMCP starting in development mode — refuses to run otherwise',
);

// ─── Step 2: Open DB connections (fail-fast) ──────────────────────────────────

logger.info('opening backend connections…');

const pgPools = {
  catalog: createPostgresPool({ service: 'catalog' }),
  basket: createPostgresPool({ service: 'basket' }),
  kitchen: createPostgresPool({ service: 'kitchen' }),
  identity: createPostgresPool({ service: 'identity' }),
} as const;

const mssqlConn = createAndConnectMssql();
const redis = createRedis();
const rabbit = createRabbit();

// Open everything in parallel; any failure aborts startup.
try {
  await Promise.all([
    pingPostgres(pgPools.catalog, 'catalog'),
    pingPostgres(pgPools.basket, 'basket'),
    pingPostgres(pgPools.kitchen, 'kitchen'),
    pingPostgres(pgPools.identity, 'identity'),
    mssqlConn.then(({ pool }) => pingMssql(pool)),
    pingRedis(redis),
    rabbit.then((r) => pingRabbit(r)),
  ]);
} catch (err) {
  if (err instanceof ConnectionError) {
    logger.fatal({ err: err.toJSON() }, 'backend connection failed — refusing to start');
  } else {
    logger.fatal({ err }, 'unexpected backend connection error — refusing to start');
  }
  process.exit(1);
}

logger.info('all backends connected');

// ─── Step 3: MCP server + transport ──────────────────────────────────────────

const mcp = new McpServer(
  { name: 'orderly-devmcp', version: '0.1.0' },
  {
    capabilities: {
      // Phase 1 registers zero tools — capability map is intentionally empty.
      // Phase 2 will add `tools: {}` here (the default is also empty, but
      // being explicit makes the intent obvious in logs and Inspector).
    },
    instructions:
      'Local-only MCP server for the Orderly backend. Refuses to run unless NODE_ENV=development. ' +
      'Phase 1 registers zero tools — Phase 2 will add API discovery, auth, state inspection, and snapshots.',
  },
);

const transport = new StreamableHTTPServerTransport({
  // Stateful mode — sessions get a generated ID returned in the Mcp-Session-Id header.
  sessionIdGenerator: () => randomUUID(),
  // DNS-rebinding protection (deprecated by the SDK in favour of external
  // middleware, but still works and is good enough for Phase 1).
  enableDnsRebindingProtection: true,
  allowedHosts: ['127.0.0.1', 'localhost', `[::1]`, env.HOST].filter(
    (h, i, a) => h !== '0.0.0.0' && a.indexOf(h) === i,
  ),
  allowedOrigins: [`http://127.0.0.1:${env.PORT}`, `http://localhost:${env.PORT}`],
});

// The StreamableHTTPServerTransport declares its optional callbacks with
// `undefined` in the union; the SDK's `Transport` interface uses bare `?:`.
// Cast to bridge the gap under exactOptionalPropertyTypes.
await mcp.connect(transport as unknown as Transport);

// ─── Step 4: HTTP server ─────────────────────────────────────────────────────

const httpServer = createServer((req: IncomingMessage, res: ServerResponse) => {
  // The MCP transport handles one request at a time; everything else gets 404.
  transport
    .handleRequest(req, res)
    .catch((err: unknown) => {
      logger.error({ err, url: req.url, method: req.method }, 'transport.handleRequest failed');
      if (!res.headersSent) {
        res.writeHead(500, { 'Content-Type': 'application/json' });
        res.end(JSON.stringify({ error: { code: 'INTERNAL', message: 'transport failure' } }));
      }
    });
});

await new Promise<void>((resolve) => {
  httpServer.listen(env.PORT, env.HOST, () => resolve());
});

logger.info(
  { url: selfUrl(), transport: 'streamable-http' },
  'MCP server listening — Inspector can connect now',
);

// ─── Step 5: Graceful shutdown ───────────────────────────────────────────────

let isShuttingDown = false;

async function closeAll(): Promise<void> {
  if (isShuttingDown) return;
  isShuttingDown = true;

  // Stop accepting new HTTP requests first.
  try {
    await new Promise<void>((resolve) => httpServer.close(() => resolve()));
    logger.info('http server closed');
  } catch (err) {
    logger.error({ err }, 'http server close failed');
  }

  // Drain in-flight MCP requests.
  try {
    await mcp.close();
    logger.info('mcp server closed');
  } catch (err) {
    logger.error({ err }, 'mcp server close failed');
  }

  try {
    await transport.close();
    logger.info('transport closed');
  } catch (err) {
    logger.error({ err }, 'transport close failed');
  }

  // Close backend connections in reverse init order.
  try {
    const r = await rabbit;
    await r.close();
    logger.info('rabbit closed');
  } catch (err) {
    logger.error({ err }, 'rabbit close failed');
  }

  try {
    await redis.quit();
    logger.info('redis quit');
  } catch (err) {
    logger.error({ err }, 'redis quit failed');
  }

  for (const service of ['identity', 'kitchen', 'basket', 'catalog'] as const) {
    try {
      await pgPools[service].end();
      logger.info({ service }, 'pg pool ended');
    } catch (err) {
      logger.error({ err, service }, 'pg pool end failed');
    }
  }

  try {
    const { pool } = await mssqlConn;
    await pool.close();
    logger.info('mssql pool closed');
  } catch (err) {
    logger.error({ err }, 'mssql pool close failed');
  }
}

closeWithGrace({ delay: 10_000 }, async ({ signal, err }) => {
  if (err) {
    logger.error({ err, signal }, 'shutdown triggered by error');
  } else {
    logger.warn({ signal }, 'shutdown initiated by signal');
  }
  await closeAll();
});

process.on('unhandledRejection', (reason) => {
  logger.fatal({ err: reason }, 'unhandledRejection — exiting');
  void closeAll().finally(() => process.exit(1));
});

process.on('uncaughtException', (err) => {
  logger.fatal({ err }, 'uncaughtException — exiting');
  void closeAll().finally(() => process.exit(1));
});

// Phase 1 explicitly registers zero tools.
logger.info('phase 1 ready — 0 tools registered');
