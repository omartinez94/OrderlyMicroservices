import test from 'node:test';
import assert from 'node:assert/strict';
import { DEFAULT_MCP_URL, McpTestClient } from '../support/mcp-client.ts';

async function isServerRunning(url = DEFAULT_MCP_URL): Promise<boolean> {
  try {
    const res = await fetch(url, {
      method: 'POST',
      headers: {
        'content-type': 'application/json',
        'x-dev-secret': process.env.DEV_TRIGGER_SECRET || 'dev-trigger-secret',
      },
      body: JSON.stringify({ jsonrpc: '2.0', id: 1, method: 'tools/list' }),
      signal: AbortSignal.timeout(1000),
    });
    return res.status < 500;
  } catch {
    return false;
  }
}

test('DevMCP Auth Guard rejects unauthenticated requests with 401', async (t) => {
  const running = await isServerRunning();
  if (!running) {
    t.skip('DevMCP server not running locally at ' + DEFAULT_MCP_URL);
    return;
  }

  // 1. Without x-dev-secret header
  const unauthRes = await fetch(DEFAULT_MCP_URL, {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify({ jsonrpc: '2.0', id: 1, method: 'tools/list' }),
  });
  assert.equal(unauthRes.status, 401, 'expected 401 Unauthorized when x-dev-secret is omitted');

  // 2. With wrong secret
  const badRes = await fetch(DEFAULT_MCP_URL, {
    method: 'POST',
    headers: {
      'content-type': 'application/json',
      'x-dev-secret': 'invalid-secret-xyz',
    },
    body: JSON.stringify({ jsonrpc: '2.0', id: 1, method: 'tools/list' }),
  });
  assert.equal(badRes.status, 401, 'expected 401 Unauthorized with invalid secret');

  // 3. With valid secret via McpTestClient
  const client = await McpTestClient.connect(DEFAULT_MCP_URL);
  const tools = await client.toolNames();
  assert.ok(tools.length >= 20, 'expected tool list from authorized connection');
  await client.close();
});
