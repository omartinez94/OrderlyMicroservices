/**
 * Live test for `flush_orders` tool.
 *
 * Gated on `MCP_LIVE_TEST=1` and live databases.
 *
 * Run:
 *   MCP_LIVE_TEST=1 node --env-file=.env --test test/tools/flush-orders.live.test.ts
 */

import { test } from 'node:test';
import assert from 'node:assert/strict';

import { createAndConnectMssql, pingMssql } from '../../src/db/mssql-client.ts';
import { createPostgresPool, pingPostgres } from '../../src/db/postgres-client.ts';
import { registerInfrastructureTools, type InfrastructureDeps } from '../../src/tools/infrastructure.ts';
import { logger } from '../../src/logger.ts';

test('flush_orders live database test (MCP_LIVE_TEST=1)', async (t) => {
  if (process.env.MCP_LIVE_TEST !== '1') {
    t.skip('MCP_LIVE_TEST is not set to "1"; live-backend tests are gated. Set MCP_LIVE_TEST=1 and run with `docker compose up -d` to enable.');
    return;
  }

  // Connect live DBs
  let mssqlConn: Awaited<ReturnType<typeof createAndConnectMssql>>;
  const pgKitchen = createPostgresPool({ service: 'kitchen' });

  try {
    mssqlConn = await createAndConnectMssql();
    await pingMssql(mssqlConn.pool);
    await pingPostgres(pgKitchen, 'kitchen');
  } catch (err) {
    t.skip(`Live databases not reachable: ${err}`);
    await pgKitchen.end().catch(() => {});
    return;
  }

  try {
    const registeredTools = new Map<string, { config: any; handler: (args: any) => Promise<any> }>();
    const mockMcpServer = {
      registerTool: (name: string, config: any, handler: any) => {
        registeredTools.set(name, { config, handler });
      },
    };

    const deps: InfrastructureDeps = {
      logger,
      mssql: mssqlConn.pool,
      pg: {
        kitchen: pgKitchen,
        catalog: {} as any,
        basket: {} as any,
        identity: {} as any,
      },
      redis: {} as any,
    };

    registerInfrastructureTools(mockMcpServer as any, deps);
    const tool = registeredTools.get('flush_orders');
    assert.ok(tool, 'flush_orders should be registered');

    // 1. Dry run
    const dryRes = await tool.handler({
      scope: 'all',
      dryRun: true,
      confirm: true,
    });
    assert.equal(dryRes.isError, false);
    const dryBody = JSON.parse(dryRes.content[0].text);
    assert.equal(dryBody.dryRun, true);
    assert.equal(dryBody.orderDb.ok, true);
    assert.equal(dryBody.kitchenDb.ok, true);

    // 2. Real flush
    const realRes = await tool.handler({
      scope: 'all',
      dryRun: false,
      confirm: true,
      purgeOutbox: true,
    });
    assert.equal(realRes.isError, false);
    const realBody = JSON.parse(realRes.content[0].text);
    assert.equal(realBody.dryRun, false);
    assert.equal(realBody.orderDb.ok, true);
    assert.equal(realBody.kitchenDb.ok, true);

    // Verify DB state is clean
    const req = mssqlConn.pool.request();
    const countCheck = await req.query('SELECT COUNT(*) AS c FROM Orders');
    assert.equal(Number(countCheck.recordset[0].c), 0, 'Orders count should be 0');

    const pgClient = await pgKitchen.connect();
    try {
      const pgCheck = await pgClient.query('SELECT COUNT(*) AS c FROM kitchen_tickets');
      assert.equal(Number(pgCheck.rows[0].c), 0, 'kitchen_tickets count should be 0');
    } finally {
      pgClient.release();
    }
  } finally {
    await pgKitchen.end().catch(() => {});
    await mssqlConn.pool.close().catch(() => {});
  }
});
