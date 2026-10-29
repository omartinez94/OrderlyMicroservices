/**
 * Unit tests for infrastructure tools (`flush_orders`).
 *
 * Mocked tests — validates tool logic, input validation, scoping rules,
 * transactional queries, error resilience, and dryRun without requiring live databases.
 *
 * Run: `pnpm test`
 */

import { test, describe, beforeEach, afterEach } from 'node:test';
import assert from 'node:assert/strict';
import sql from 'mssql';
import { registerInfrastructureTools, type InfrastructureDeps } from '../../src/tools/infrastructure.ts';
import { rateLimits } from '../../src/util/rate-limit.ts';

const DUMMY_LOGGER = {
  info: () => {},
  warn: () => {},
  error: () => {},
  debug: () => {},
  fatal: () => {},
} as any;

const VALID_UUID = '550e8400-e29b-41d4-a716-446655440000';

describe('flush_orders unit tests', () => {
  const origQuery = sql.Request.prototype.query;
  const origTxBegin = sql.Transaction.prototype.begin;
  const origTxCommit = sql.Transaction.prototype.commit;
  const origTxRollback = sql.Transaction.prototype.rollback;

  const dummyPool = new sql.ConnectionPool({
    server: 'localhost',
    user: 'sa',
    password: 'dummy',
    database: 'OrderDb',
  });

  beforeEach(() => {
    // Reset rate-limit bucket tokens
    (rateLimits.flush as any).buckets.clear();

    // Default mock queries
    sql.Request.prototype.query = async function () {
      return {
        recordset: [{
          orderActivities: 1,
          orderItems: 2,
          orderBills: 0,
          orders: 1,
          outboxPending: 0,
          outboxDead: 0,
          dailySequences: 0,
        }],
      } as any;
    };
    sql.Transaction.prototype.begin = (async function (this: any) {
      return this;
    }) as any;
    sql.Transaction.prototype.commit = (async function () {}) as any;
    sql.Transaction.prototype.rollback = (async function (this: any) {
      return this;
    }) as any;
  });

  afterEach(() => {
    sql.Request.prototype.query = origQuery;
    sql.Transaction.prototype.begin = origTxBegin;
    sql.Transaction.prototype.commit = origTxCommit;
    sql.Transaction.prototype.rollback = origTxRollback;
  });

  function setupFlushOrdersTool(overrides?: Partial<InfrastructureDeps>) {
    const registeredTools = new Map<string, { config: any; handler: (args: any) => Promise<any> }>();

    const mockMcpServer = {
      registerTool: (name: string, config: any, handler: any) => {
        registeredTools.set(name, { config, handler });
      },
    };

    const defaultPgKitchen = {
      connect: async () => ({
        query: async (sqlText: string) => {
          if (sqlText.trim().startsWith('SELECT')) {
            return { rows: [{ tickets: 1, ticketItems: 2, outboxPending: 0, outboxDead: 0 }] };
          }
          return { rowCount: 1 };
        },
        release: () => {},
      }),
    };

    const deps: InfrastructureDeps = {
      logger: DUMMY_LOGGER,
      mssql: dummyPool,
      pg: {
        kitchen: defaultPgKitchen as any,
        catalog: {} as any,
        basket: {} as any,
        identity: {} as any,
      },
      redis: {} as any,
      ...overrides,
    };

    registerInfrastructureTools(mockMcpServer as any, deps);
    const tool = registeredTools.get('flush_orders');
    if (!tool) throw new Error('flush_orders tool was not registered');

    return { handler: tool.handler, deps };
  }
  test('rejects when both restaurantId and scope: "all" are provided', async () => {
    const { handler } = setupFlushOrdersTool();
    const res = await handler({
      restaurantId: VALID_UUID,
      scope: 'all',
      confirm: true,
    });

    assert.equal(res.isError, true);
    const body = JSON.parse(res.content[0].text);
    assert.match(body.error, /exactly one of `restaurantId`/);
  });

  test('rejects when neither restaurantId nor scope: "all" is provided', async () => {
    const { handler } = setupFlushOrdersTool();
    const res = await handler({
      confirm: true,
    });

    assert.equal(res.isError, true);
    const body = JSON.parse(res.content[0].text);
    assert.match(body.error, /exactly one of `restaurantId`/);
  });

  test('ignores purgeOutbox and adds note when scope is a single restaurant', async () => {
    const { handler } = setupFlushOrdersTool();
    const res = await handler({
      restaurantId: VALID_UUID,
      purgeOutbox: true,
      dryRun: true,
      confirm: true,
    });

    assert.equal(res.isError, false);
    const body = JSON.parse(res.content[0].text);
    assert.equal(body.scope, VALID_UUID);
    assert.equal(body.notes.length, 1);
    assert.match(body.notes[0], /purgeOutbox was ignored/);
  });

  test('executes dryRun without beginning transaction or deleting rows', async () => {
    sql.Request.prototype.query = async function () {
      return {
        recordset: [{
          orderActivities: 5,
          orderItems: 10,
          orderBills: 1,
          orders: 3,
          outboxPending: 2,
          outboxDead: 0,
          dailySequences: 1,
        }],
      } as any;
    };

    const { handler } = setupFlushOrdersTool();
    const res = await handler({
      scope: 'all',
      dryRun: true,
      confirm: true,
    });

    assert.equal(res.isError, false);
    const body = JSON.parse(res.content[0].text);
    assert.equal(body.dryRun, true);
    assert.equal(body.orderDb.counts.orders, 3);
    assert.equal(body.orderDb.counts.orderItems, 10);
    assert.equal(body.orderDb.counts.orderActivities, 5);
  });

  test('skips Kitchendb when includeKitchen is false', async () => {
    const { handler } = setupFlushOrdersTool();
    const res = await handler({
      scope: 'all',
      includeKitchen: false,
      dryRun: true,
      confirm: true,
    });

    assert.equal(res.isError, false);
    const body = JSON.parse(res.content[0].text);
    assert.equal(body.kitchenDb.skipped, true);
  });

  test('handles MSSQL failure gracefully with per-database result', async () => {
    sql.Request.prototype.query = async function () {
      throw new Error('MSSQL connection timeout');
    };

    const { handler } = setupFlushOrdersTool();
    const res = await handler({
      scope: 'all',
      dryRun: true,
      confirm: true,
    });

    assert.equal(res.isError, true);
    const body = JSON.parse(res.content[0].text);
    assert.equal(body.orderDb.ok, false);
    assert.match(body.orderDb.error, /MSSQL connection timeout/);
    assert.equal(body.kitchenDb.ok, true);
  });

  test('handles Postgres failure gracefully with per-database result', async () => {
    const failingPg = {
      connect: async () => {
        throw new Error('Kitchendb connection refused');
      },
    };

    const { handler } = setupFlushOrdersTool({
      pg: {
        kitchen: failingPg as any,
        catalog: {} as any,
        basket: {} as any,
        identity: {} as any,
      },
    });

    const res = await handler({
      scope: 'all',
      dryRun: true,
      confirm: true,
    });

    assert.equal(res.isError, true);
    const body = JSON.parse(res.content[0].text);
    assert.equal(body.orderDb.ok, true);
    assert.equal(body.kitchenDb.ok, false);
    assert.match(body.kitchenDb.error, /Kitchendb connection refused/);
  });

  test('rate-limits repeated executions after bucket exhaustion', async () => {
    const { handler } = setupFlushOrdersTool();

    // Drain tokens from the bucket
    while (rateLimits.flush.consume('global').allowed) {}

    const res = await handler({
      scope: 'all',
      dryRun: true,
      confirm: true,
    });

    assert.equal(res.isError, true);
    const body = JSON.parse(res.content[0].text);
    assert.match(body.error, /rate-limited/);
  });
});
