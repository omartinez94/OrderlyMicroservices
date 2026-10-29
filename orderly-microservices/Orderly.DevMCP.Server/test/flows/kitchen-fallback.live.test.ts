/**
 * Live-backend test for the kitchen fallback transaction (RES-003).
 *
 * `create_mock_order` publishes `OrderCreatedIntegrationEvent` so the
 * Kitchen service builds the ticket itself. When the broker is not
 * reachable the tool mirrors the order into Kitchendb DIRECTLY — the
 * ticket AND its items in ONE transaction, so a half-written ticket is
 * never left behind for the kitchen to act on.
 *
 * This test forces that path by registering the seeding tools WITHOUT a
 * RabbitMQ handle, and asserts against the real OrderDb + Kitchendb that
 * the ticket and every line item landed together.
 *
 * Gated on `MCP_LIVE_TEST=1` exactly like `checkout.live.test.ts`, so CI
 * stays hermetic.
 *
 * Run:
 *   MCP_LIVE_TEST=1 docker compose up -d
 *   node --env-file=.env --test test/flows/kitchen-fallback.live.test.ts
 */

import { test } from 'node:test';
import assert from 'node:assert/strict';
import { McpServer } from '@modelcontextprotocol/sdk/server/mcp.js';
import { Client } from '@modelcontextprotocol/sdk/client/index.js';
import { InMemoryTransport } from '@modelcontextprotocol/sdk/inMemory.js';
import sql from 'mssql';

import { ACME_RESTAURANT_ID, registerDataSeedingTools } from '../../src/tools/data-seeding.ts';
import { logger } from '../../src/logger.ts';
import { createAndConnectMssql } from '../../src/db/mssql-client.ts';
import { createPostgresPool } from '../../src/db/postgres-client.ts';
import type { ToolContext } from '../../src/tools/types.ts';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, resolve } from 'node:path';

const __dirname = dirname(fileURLToPath(import.meta.url));
const SEEDS_DIR = resolve(__dirname, '../../resources/seeds');

interface CreatedOrder {
  orderId: string;
  orderNumber: string;
  eventPublished: boolean;
  kitchenMirrored: boolean;
  kitchenTicketId: string | null;
  itemCount: number;
  items: Array<{ menuItemId: string }>;
}

test('the kitchen fallback mirrors the ticket and its items in one transaction (MCP_LIVE_TEST=1)', async (t) => {
  // Gate 1: MCP_LIVE_TEST must be set. Operators opt in explicitly so
  // CI doesn't accidentally spin up the full backend stack.
  if (process.env.MCP_LIVE_TEST !== '1') {
    t.skip('MCP_LIVE_TEST is not set to "1"; live-backend tests are gated. Set MCP_LIVE_TEST=1 and run with `docker compose up -d` to enable.');
    return;
  }

  // Gate 2: the two stores the fallback touches must be reachable.
  let pool: sql.ConnectionPool;
  try {
    pool = (await createAndConnectMssql()).pool;
  } catch {
    t.skip('OrderDb is not reachable on localhost:1433 — Docker stack is not up.');
    return;
  }

  const kitchenPool = createPostgresPool({ service: 'kitchen' });
  try {
    await kitchenPool.query('SELECT 1');
  } catch {
    t.skip('Kitchendb is not reachable on localhost:5436 — Docker stack is not up.');
    await pool.close();
    return;
  }

  // The catalog item ids the seeder may upsert into OrderDb.MenuItems —
  // captured BEFORE the call so the cleanup removes only what this test
  // created.
  const catalog = JSON.parse(readFileSync(resolve(SEEDS_DIR, 'catalog-seed.json'), 'utf-8')) as {
    items: Array<{ id: string }>;
  };
  const menuItemIds = catalog.items.map((i) => i.id);
  const inList = menuItemIds.map((id) => `'${id}'`).join(',');
  const menuItemsBefore = await pool.request().query<{ Id: string }>(
    `SELECT Id FROM MenuItems WHERE Id IN (${inList})`,
  );
  const menuItemsBeforeIds = new Set(menuItemsBefore.recordset.map((r) => r.Id));

  let created: CreatedOrder | undefined;

  try {
    const server = new McpServer({ name: 'kitchen-fallback-live-test', version: '0' });
    // NO `rabbit` handle on purpose: the broker is unreachable from this
    // process, so `create_mock_order` must take the kitchen-mirror
    // fallback path instead of publishing the event.
    registerDataSeedingTools(server, {
      logger,
      pg: { kitchen: kitchenPool } as unknown as ToolContext['pg'],
      mssql: pool,
    });
    const [clientT, serverT] = InMemoryTransport.createLinkedPair();
    const client = new Client({ name: 'kitchen-fallback-live-client', version: '0' }, { capabilities: {} });
    await Promise.all([client.connect(clientT), server.connect(serverT)]);

    const result = await client.callTool({
      name: 'create_mock_order',
      arguments: { restaurantId: ACME_RESTAURANT_ID, itemCount: 3, seed: 'kitchen-fallback-live' },
    });
    const content = result.content as Array<{ type: string; text: string }>;
    const doc = JSON.parse(content[0]!.text) as CreatedOrder;
    created = doc;

    assert.equal(doc.eventPublished, false, 'no broker handle means nothing may be published');
    assert.equal(doc.kitchenMirrored, true, 'the order must be mirrored into Kitchendb');
    assert.equal(doc.kitchenTicketId, doc.orderId, 'the fallback ticket id is the order id');
    assert.equal(doc.itemCount, 3, 'three line items were requested');

    // The ticket landed …
    const ticket = await kitchenPool.query<{ Id: string; OrderNumber: string; Status: number }>(
      'SELECT "Id", "OrderNumber", "Status" FROM kitchen_tickets WHERE "Id" = $1',
      [doc.orderId],
    );
    assert.equal(ticket.rowCount, 1, 'the kitchen ticket must exist');
    assert.equal(ticket.rows[0]!.OrderNumber, doc.orderNumber, 'the ticket carries the order number');

    // … together with EVERY line item — one transaction, all-or-nothing.
    const items = await kitchenPool.query<{ Id: string }>(
      'SELECT "Id" FROM kitchen_ticket_items WHERE "KitchenTicketId" = $1',
      [doc.orderId],
    );
    assert.equal(items.rowCount, doc.itemCount, 'every line item must be mirrored');

    // The OrderDb order exists too, and its variation column is
    // PASCALCASE — the Ordering EF converter is case-sensitive.
    const orderItems = await pool
      .request()
      .input('id', doc.orderId)
      .query<{ SelectedVariations: string; Customizations: string }>(
        'SELECT SelectedVariations, Customizations FROM OrderItems WHERE OrderId = @id',
      );
    assert.ok(orderItems.recordset.length > 0, 'the OrderDb order items must exist');
    for (const row of orderItems.recordset) {
      assert.ok(!row.SelectedVariations.includes('"name"'), 'SelectedVariations must stay PascalCase');
      assert.ok(!row.Customizations.includes('"label"'), 'Customizations must stay PascalCase');
    }

    await client.close();
    await server.close();
  } finally {
    // Leave the dev stack exactly as it was found: remove ONLY the rows
    // this test created.
    if (created) {
      await kitchenPool.query('DELETE FROM kitchen_ticket_items WHERE "KitchenTicketId" = $1', [created.orderId]);
      await kitchenPool.query('DELETE FROM kitchen_tickets WHERE "Id" = $1', [created.orderId]);
      await pool.request().input('id', created.orderId).query('DELETE FROM OrderItems WHERE OrderId = @id');
      await pool.request().input('id', created.orderId).query('DELETE FROM Orders WHERE Id = @id');
    }
    const createdMenuItemIds = menuItemIds.filter((id) => !menuItemsBeforeIds.has(id));
    if (createdMenuItemIds.length > 0) {
      const createdList = createdMenuItemIds.map((id) => `'${id}'`).join(',');
      await pool.request().query(`DELETE FROM MenuItems WHERE Id IN (${createdList})`);
    }
    await kitchenPool.end();
    await pool.close();
  }
});
