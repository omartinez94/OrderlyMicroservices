/**
 * test/wire/restaurant-lifecycle.wire.test.ts
 *
 * Wire tests for restaurant lifecycle, floorplan, menu, and staff MCP tools.
 * Tests JSON-RPC schema compliance, graceful error boundaries, and input validation.
 */

import { test, describe, before, after } from 'node:test';
import assert from 'node:assert/strict';

import {
  McpTestClient,
  DEFAULT_MCP_URL,
  detectRuntimeErrors,
  isMcpServerUp,
} from '../support/mcp-client.ts';

let client: McpTestClient | null = null;
let serverUp = false;

before(async () => {
  serverUp = await isMcpServerUp(DEFAULT_MCP_URL);
  if (!serverUp) return;
  client = await McpTestClient.connect(DEFAULT_MCP_URL);
});

after(async () => {
  await client?.close();
});

function requireServer(t: { skip: (msg: string) => void }): McpTestClient | null {
  if (!serverUp || !client) {
    t.skip(
      `No MCP server reachable at ${DEFAULT_MCP_URL}. Start it with \`pnpm dev\` to run the wire tests.`,
    );
    return null;
  }
  return client;
}

function assertGraceful(tool: string, result: { text: string; data: unknown; isError: boolean }): void {
  const leaked = detectRuntimeErrors(result.text);
  assert.deepEqual(
    leaked,
    [],
    `${tool} leaked a raw runtime error instead of degrading gracefully: ${leaked.join(', ')}\n--- response ---\n${result.text.slice(0, 600)}`,
  );

  if (!result.isError) {
    assert.notEqual(
      result.data,
      undefined,
      `${tool} returned non-JSON content on success: ${result.text.slice(0, 300)}`,
    );
  }
}

describe('Restaurant lifecycle tools respond to JSON-RPC over Streamable HTTP', () => {
  test('all 8 restaurant lifecycle tools are listed on the server', async (t) => {
    const c = requireServer(t);
    if (!c) return;
    const tools = await c.toolNames();
    const toolNames = new Set(tools);

    const expected = [
      'create_restaurant',
      'update_restaurant',
      'get_restaurant',
      'purge_restaurant',
      'manage_restaurant_tables',
      'manage_restaurant_menu',
      'manage_restaurant_staff',
      'scaffold_restaurant',
    ];

    for (const name of expected) {
      assert.ok(toolNames.has(name), `tool "${name}" was not found in server listTools`);
    }
  });

  test('get_restaurant returns structured error on nonexistent restaurant', async (t) => {
    const c = requireServer(t);
    if (!c) return;
    const res = await c.callTool('get_restaurant', {
      restaurantId: '00000000-0000-0000-0000-000000000000',
    });
    assertGraceful('get_restaurant(nonexistent)', res);
  });

  test('purge_restaurant fails safe when confirmText does not match', async (t) => {
    const c = requireServer(t);
    if (!c) return;
    const dummyId = '11111111-2222-3333-4444-555555555555';
    const res = await c.callTool('purge_restaurant', {
      restaurantId: dummyId,
      confirmText: 'WRONG_CONFIRM',
    });
    assertGraceful('purge_restaurant(bad confirm)', res);
    assert.equal(res.isError, true, 'purge_restaurant should fail when confirmText is incorrect');
  });

  test('manage_restaurant_tables rejects create action without tableNumber', async (t) => {
    const c = requireServer(t);
    if (!c) return;
    const res = await c.callTool('manage_restaurant_tables', {
      restaurantId: '22222222-2222-2222-2222-222222222222',
      action: 'create',
      capacity: 4,
    });
    assertGraceful('manage_restaurant_tables(missing tableNumber)', res);
    assert.equal(res.isError, true);
  });

  test('manage_restaurant_menu rejects create_category without categoryName', async (t) => {
    const c = requireServer(t);
    if (!c) return;
    const res = await c.callTool('manage_restaurant_menu', {
      restaurantId: '22222222-2222-2222-2222-222222222222',
      action: 'create_category',
    });
    assertGraceful('manage_restaurant_menu(missing categoryName)', res);
    assert.equal(res.isError, true);
  });
});
