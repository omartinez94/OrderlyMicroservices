/**
 * Live-backend end-to-end test for the checkout flow.
 *
 * Differs from `checkout.test.ts`: this test is gated on the
 * `MCP_LIVE_TEST=1` env var and runs the full flow against a live
 * stack (`docker compose up -d`). Default-off so CI stays
 * hermetic.
 *
 * Run:
 *   MCP_LIVE_TEST=1 docker compose up -d
 *   node --env-file=.env --test test/flows/checkout.live.test.ts
 *
 * Skips with a clear message when `MCP_LIVE_TEST !== '1'` or when
 * the basket API is unreachable.
 */

import { test } from 'node:test';
import assert from 'node:assert/strict';
import { McpServer } from '@modelcontextprotocol/sdk/server/mcp.js';
import { Client } from '@modelcontextprotocol/sdk/client/index.js';
import { InMemoryTransport } from '@modelcontextprotocol/sdk/inMemory.js';

import { registerFlowTracingTools } from '../../src/tools/flow-tracing.ts';
import { logger } from '../../src/logger.ts';
import type { ToolContext } from '../../src/tools/types.ts';

function makeStubCtx(): ToolContext {
  return {
    logger,
    pg: {} as ToolContext['pg'],
    mssql: {} as ToolContext['mssql'],
    redis: {} as ToolContext['redis'],
    rabbit: { connection: {} as never, channel: { publish: () => true } as never, close: async () => undefined },
  };
}

test('checkout flow end-to-end against live backends (MCP_LIVE_TEST=1)', async (t) => {
  // Gate 1: MCP_LIVE_TEST must be set. Operators opt in explicitly so
  // CI doesn't accidentally spin up the full backend stack.
  if (process.env.MCP_LIVE_TEST !== '1') {
    t.skip('MCP_LIVE_TEST is not set to "1"; live-backend tests are gated. Set MCP_LIVE_TEST=1 and run with `docker compose up -d` to enable.');
    return;
  }

  // Gate 2: confirm the basket API is reachable before running the
  // flow. Without this gate, the test fails when Docker is down,
  // which is the wrong signal — Docker might simply be off.
  try {
    const ping = await fetch('http://localhost:6001/basket', { signal: AbortSignal.timeout(2_000) });
    void ping;
  } catch {
    t.skip('basket.api not reachable on localhost:6001 — Docker stack is not up. Run `docker compose up -d` to enable.');
    return;
  }

  const server = new McpServer({ name: 'flow-live-test', version: '0' });
  registerFlowTracingTools(server, { logger, ctx: makeStubCtx() });
  const [clientT, serverT] = InMemoryTransport.createLinkedPair();
  const client = new Client({ name: 'flow-live-client', version: '0' }, { capabilities: {} });
  await Promise.all([client.connect(clientT), server.connect(serverT)]);

  const result = await client.callTool({ name: 'trace_business_flow', arguments: { flowName: 'checkout' } });
  const content = result.content as Array<{ type: string; text: string }>;
  const text = content[0]!.text;
  const doc = JSON.parse(text) as {
    runId: string;
    flowName: string;
    pass: boolean;
    steps: Array<{ kind: string; status?: number; step: string }>;
  };

  assert.equal(doc.flowName, 'checkout');
  assert.ok(doc.runId, 'runId should be set');
  assert.ok(Array.isArray(doc.steps) && doc.steps.length > 0, 'at least one step must run');

  // Live test asserts `pass` end-to-end (not just 2xx on HTTP steps):
  // the live path includes a downstream order-projection verification
  // that fails fast if the order didn't actually land in OrderDb.
  assert.equal(doc.pass, true, `flow did not pass: ${JSON.stringify(doc.steps)}`);

  for (const s of doc.steps) {
    if (s.kind === 'http') {
      assert.ok(
        s.status !== undefined && s.status >= 200 && s.status < 300,
        `step "${s.step}" expected 2xx, got ${s.status}`,
      );
    }
  }

  await client.close();
  await server.close();
});