/**
 * Orderly.DevMCP.Server — boot.
 *
 * Loads env, boots the logger, opens all DB / cache / broker connections,
 * starts the MCP server over HTTP (StreamableHTTPServerTransport), registers
 * the Phase 2 tool set, and wires graceful shutdown.
 *
 * Sequence on boot:
 *   1. zod env validation (refuses to start on bad config or non-dev NODE_ENV)
 *   2. Logger ready
 *   3. assertDevHost guard inside each connection factory
 *   4. Open all connections (fail-fast — exit 1 if any backend is down)
 *   5. Bind HTTP listener on HOST:PORT
 *   6. McpServer.connect(transport)
 *   7. Register Phase 2 tools (auth, api-discovery, state-inspection, snapshot, log-tracing)
 *   8. Install signal + unhandled-error handlers
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

import { registerAuthTools } from './tools/auth.ts';
import { registerApiDiscoveryTools } from './tools/api-discovery.ts';
import { registerStateInspectionTools } from './tools/state-inspection.ts';
import { registerSnapshotTools } from './tools/snapshot.ts';
import { registerLogTracingTools } from './tools/log-tracing.ts';
import { registerDataSeedingTools } from './tools/data-seeding.ts';
import { registerEventBusTools } from './tools/event-bus.ts';
import { registerInfrastructureTools } from './tools/infrastructure.ts';
import { registerJobsTools } from './tools/jobs.ts';
import { registerFlowTracingTools } from './tools/flow-tracing.ts';

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

// ─── Step 3: Register tools context ──────────────────────────────────────────

const { pool: mssqlPool } = await mssqlConn;
const rabbitHandle = await rabbit;

const toolCtx = {
  logger,
  pg: pgPools as unknown as Record<PostgresService, typeof pgPools.catalog>,
  mssql: mssqlPool,
  redis,
  rabbit: rabbitHandle,
};

// ─── Step 4: Multi-session MCP factory & HTTP transport ──────────────────────

const sessions = new Map<string, { mcp: McpServer; transport: StreamableHTTPServerTransport }>();

function createMcpInstance(): { mcp: McpServer; transport: StreamableHTTPServerTransport } {
  const mcp = new McpServer(
    { name: 'orderly-devmcp', version: '0.1.0' },
    {
      capabilities: {},
      instructions:
        'Local-only MCP server for the Orderly backend. Refuses to run unless NODE_ENV=development. ' +
        'Ships 20 tools across 10 modules: API discovery, auth (HS256 dev tokens), state inspection, ' +
        'system snapshot + watcher, log tracing, data seeding (catalog menu + mock orders), event bus ' +
        '(publish + DLQ inspection), infrastructure (DB reset + flush orders + outage simulation), and jobs ' +
        '(historical sales + scheduled triggers).',
    },
  );

  registerAuthTools(mcp, { logger });
  registerApiDiscoveryTools(mcp, { logger });
  registerStateInspectionTools(mcp, { ...toolCtx, logger });
  registerSnapshotTools(mcp, { ...toolCtx, logger });
  registerLogTracingTools(mcp, { logger });
  registerDataSeedingTools(mcp, { logger, pg: toolCtx.pg, mssql: toolCtx.mssql, rabbit: toolCtx.rabbit });
  registerEventBusTools(mcp, { logger, rabbit: toolCtx.rabbit });
  registerInfrastructureTools(mcp, { logger, pg: toolCtx.pg, mssql: toolCtx.mssql, redis: toolCtx.redis });
  registerJobsTools(mcp, { logger, mssql: toolCtx.mssql });
  registerFlowTracingTools(mcp, { logger, ctx: toolCtx });

  const transport = new StreamableHTTPServerTransport({
    sessionIdGenerator: () => randomUUID(),
    onsessioninitialized: (id: string) => {
      sessions.set(id, { mcp, transport });
      logger.debug({ sessionId: id }, 'MCP session initialized');
    },
    enableDnsRebindingProtection: true,
    allowedHosts: [
      '127.0.0.1',
      'localhost',
      `[::1]`,
      `127.0.0.1:${env.PORT}`,
      `localhost:${env.PORT}`,
      `[::1]:${env.PORT}`,
      env.HOST,
      `${env.HOST}:${env.PORT}`,
    ].filter((h, i, a) => h !== '0.0.0.0' && a.indexOf(h) === i),
    allowedOrigins: [`http://127.0.0.1:${env.PORT}`, `http://localhost:${env.PORT}`],
  });

  transport.onclose = () => {
    if (transport.sessionId) {
      sessions.delete(transport.sessionId);
      logger.debug({ sessionId: transport.sessionId }, 'MCP session closed');
    }
  };

  mcp.connect(transport as unknown as Transport);
  return { mcp, transport };
}

logger.info({ tools: 20 }, 'phase 4 ready — 20 tools registered across modular sessions');

// ─── Step 5: HTTP server ─────────────────────────────────────────────────────

const httpServer = createServer((req: IncomingMessage, res: ServerResponse) => {
  const sessionId = req.headers['mcp-session-id'] as string | undefined;
  let activeTransport: StreamableHTTPServerTransport;

  if (sessionId && sessions.has(sessionId)) {
    activeTransport = sessions.get(sessionId)!.transport;
  } else {
    const instance = createMcpInstance();
    activeTransport = instance.transport;
  }

  activeTransport
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
  'MCP server listening — Inspector and clients can connect seamlessly',
);

// ─── Step 6: Graceful shutdown ───────────────────────────────────────────────

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

  // Drain active sessions.
  for (const session of sessions.values()) {
    try {
      await session.mcp.close();
      await session.transport.close();
    } catch {
      // Ignore session errors during teardown
    }
  }
  sessions.clear();
  logger.info('all mcp sessions closed');

  // Close backend connections in reverse init order.
  try {
    await rabbitHandle.close();
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
    await mssqlPool.close();
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
