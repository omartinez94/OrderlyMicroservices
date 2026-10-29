/**
 * test/wire/mcp-wire.test.ts
 *
 * End-to-end wire tests against a RUNNING DevMCP server.
 *
 * These tests talk the MCP protocol over Streamable HTTP — the same
 * path an AI assistant takes. They never import the tool modules, so
 * they catch the failures that matter in production but that unit
 * tests structurally cannot:
 *
 *   - a tool that typechecks and unit-tests fine but throws when the
 *     JSON-RPC envelope is applied;
 *   - a tool that returns a raw exception string instead of the
 *     structured `{ error: { code, message } }` payload;
 *   - a tool that is registered but whose arguments are not actually
 *     accepted by the schema;
 *   - a tool that never got registered on the server at all.
 *
 * Run:
 *   pnpm dev                       # server on :8080
 *   node --env-file=.env --test test/wire/mcp-wire.test.ts
 *
 * Or via the package script:
 *   pnpm test:wire
 *
 * The suite SKIPS (rather than fails) when no server is reachable,
 * so `pnpm test` stays green on a machine without the dev stack.
 */

import { test, describe, before, after } from 'node:test';
import assert from 'node:assert/strict';

import {
  McpTestClient,
  DEFAULT_MCP_URL,
  EXPECTED_TOOLS,
  detectRuntimeErrors,
  isMcpServerUp,
} from '../support/mcp-client.ts';

/** A restaurant that exists in the seeded dev data. */
const SEEDED_RESTAURANT_ID = '22222222-2222-2222-2222-222222222222';

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

/** Skip the whole suite when no MCP server is running. */
function requireServer(t: { skip: (msg: string) => void }): McpTestClient {
  if (!serverUp || !client) {
    t.skip(
      `No MCP server reachable at ${DEFAULT_MCP_URL}. Start it with \`pnpm dev\` (and the dev stack via \`docker compose up -d\`) to run the wire tests.`,
    );
    throw new Error('skipped');
  }
  return client;
}

/**
 * Assert a tool call degraded gracefully.
 *
 * "Graceful" means: the server answered with a well-formed JSON
 * payload and did not leak a raw runtime exception. A backend being
 * down is acceptable — that is what the `error` envelope is FOR.
 */
function assertGraceful(tool: string, result: { text: string; data: unknown; isError: boolean }): void {
  const leaked = detectRuntimeErrors(result.text);
  assert.deepEqual(
    leaked,
    [],
    `${tool} leaked a raw runtime error instead of degrading gracefully: ${leaked.join(', ')}\n--- response ---\n${result.text.slice(0, 600)}`,
  );

  // A successful call must return a JSON payload, not prose.
  if (!result.isError) {
    assert.notEqual(
      result.data,
      undefined,
      `${tool} returned non-JSON content on success: ${result.text.slice(0, 300)}`,
    );
  }
}

describe('MCP protocol surface', () => {
  test('server completes the MCP handshake and lists every tool', async (t) => {
    const c = requireServer(t);

    const names = await c.toolNames();
    assert.ok(names.length > 0, 'server advertised no tools');

    // Every expected tool must be reachable over the wire. A tool
    // that exists in code but not on the wire is invisible to every
    // real client.
    const missing = EXPECTED_TOOLS.filter((n) => !names.includes(n));
    assert.deepEqual(missing, [], `tools missing from the wire surface: ${missing.join(', ')}`);
  });

  test('every tool advertises a usable input schema', async (t) => {
    const c = requireServer(t);

    for (const tool of await c.toolDescriptors()) {
      const schema = tool.inputSchema as { type?: string; properties?: Record<string, unknown> };
      assert.equal(schema.type, 'object', `${tool.name} has a non-object input schema`);
      assert.equal(
        typeof schema.properties,
        'object',
        `${tool.name} has no properties in its input schema`,
      );
      assert.ok(tool.description, `${tool.name} has no description`);
    }
  });

  test('calling an unknown tool returns an error, not a crash', async (t) => {
    const c = requireServer(t);

    // The MCP SDK surfaces an unknown tool as an error RESULT
    // (`isError: true`) rather than a thrown protocol error, so
    // accept either shape — what matters is that it is a clean,
    // descriptive error and not a crash.
    try {
      const res = await c.callTool('definitely_not_a_real_tool', {});
      assertGraceful('unknown tool', res);
      assert.equal(res.isError, true, 'an unknown tool should have been rejected');
      assert.match(res.text, /not found|unknown tool/i, `unexpected error text: ${res.text.slice(0, 200)}`);
    } catch (err) {
      // A protocol-level error is acceptable too, as long as it is
      // a clean MCP error rather than a runtime crash.
      const message = (err as Error).message;
      assert.deepEqual(
        detectRuntimeErrors(message),
        [],
        `the unknown-tool path leaked a runtime error: ${message}`,
      );
      assert.match(message, /not.*found|unknown|invalid/i, `unexpected error: ${message}`);
    }
  });
});

describe('read-only tools return real data over the wire', () => {
  test('get_system_snapshot returns every section populated', async (t) => {
    const c = requireServer(t);

    const res = await c.callTool('get_system_snapshot', {});
    assertGraceful('get_system_snapshot', res);
    assert.equal(res.isError, false, `get_system_snapshot failed: ${res.text.slice(0, 300)}`);

    const snap = res.data as Record<string, unknown>;
    for (const section of ['catalog', 'activeSessions', 'orders', 'kitchen', 'eventBus']) {
      assert.ok(section in snap, `snapshot is missing the "${section}" section`);
      const value = snap[section] as Record<string, unknown>;
      assert.ok(
        value && typeof value === 'object' && !('error' in value),
        `snapshot section "${section}" returned an error: ${JSON.stringify(value)}`,
      );
    }
  });

  test('get_api_schema resolves a schema for every service', async (t) => {
    const c = requireServer(t);

    for (const service of ['catalog', 'basket', 'ordering', 'kitchen', 'identity', 'discount']) {
      const res = await c.callTool('get_api_schema', { serviceName: service });
      assertGraceful(`get_api_schema(${service})`, res);
      assert.equal(res.isError, false, `get_api_schema(${service}) failed: ${res.text.slice(0, 300)}`);

      const schema = res.data as { endpointCount?: number; endpoints?: unknown[] };
      assert.ok(
        typeof schema.endpointCount === 'number' && schema.endpointCount > 0,
        `get_api_schema(${service}) returned ${schema.endpointCount} endpoints`,
      );
    }
  });

  test('generate_dev_token and verify_token round-trip', async (t) => {
    const c = requireServer(t);

    const gen = await c.callTool('generate_dev_token', {
      role: 'Manager',
      restaurantId: SEEDED_RESTAURANT_ID,
    });
    assertGraceful('generate_dev_token', gen);
    assert.equal(gen.isError, false, `generate_dev_token failed: ${gen.text.slice(0, 300)}`);

    const token = (gen.data as { token?: string }).token;
    assert.ok(token, 'generate_dev_token returned no token');

    const verified = await c.callTool('verify_token', { token });
    assertGraceful('verify_token', verified);
    assert.equal(verified.isError, false, `verify_token failed: ${verified.text.slice(0, 300)}`);

    const payload = verified.data as { valid?: boolean; claims?: Record<string, unknown> };
    assert.equal(payload.valid, true, 'the token did not verify');
    assert.equal(payload.claims?.role, 'Manager');
    assert.equal(payload.claims?.restaurantId, SEEDED_RESTAURANT_ID);
  });

  test('verify_token rejects a tampered token without crashing', async (t) => {
    const c = requireServer(t);

    const gen = await c.callTool('generate_dev_token', { role: 'Staff' });
    const token = (gen.data as { token?: string }).token ?? '';
    const tampered = `${token.slice(0, -4)}AAAA`;

    const res = await c.callTool('verify_token', { token: tampered });
    assertGraceful('verify_token(tampered)', res);
    // Either an error envelope or a `valid: false` verdict — both are
    // graceful. A thrown exception would be neither.
    if (!res.isError) {
      assert.equal((res.data as { valid?: boolean }).valid, false, 'a tampered token verified as valid');
    }
  });

  test('get_recent_logs returns container output', async (t) => {
    const c = requireServer(t);

    const res = await c.callTool('get_recent_logs', { serviceName: 'catalog', lines: 5 });
    assertGraceful('get_recent_logs', res);
    assert.equal(res.isError, false, `get_recent_logs failed: ${res.text.slice(0, 300)}`);
    assert.ok(Array.isArray((res.data as { lines?: unknown[] }).lines));
  });

  test('inspect_dead_letters returns the broker queue state', async (t) => {
    const c = requireServer(t);

    const res = await c.callTool('inspect_dead_letters', { limitPerQueue: 1 });
    assertGraceful('inspect_dead_letters', res);
    assert.equal(res.isError, false, `inspect_dead_letters failed: ${res.text.slice(0, 300)}`);
    assert.ok(Array.isArray((res.data as { deadLetterQueues?: unknown[] }).deadLetterQueues));
  });

  test('get_flow_architecture returns a diagram for every flow', async (t) => {
    const c = requireServer(t);

    for (const flowName of ['checkout', 'kitchen-order-lifecycle', 'discount-application']) {
      const res = await c.callTool('get_flow_architecture', { flowName });
      assertGraceful(`get_flow_architecture(${flowName})`, res);
      assert.equal(res.isError, false, `get_flow_architecture(${flowName}) failed: ${res.text.slice(0, 300)}`);

      const diagram = (res.data as { diagram?: string }).diagram ?? '';
      assert.match(diagram, /sequenceDiagram|stateDiagram/, `flow "${flowName}" has no Mermaid diagram`);
    }
  });
});

describe('mutating tools are safe and well-behaved over the wire', () => {
  test('seed_test_menu is idempotent', async (t) => {
    const c = requireServer(t);

    const first = await c.callTool('seed_test_menu', {});
    assertGraceful('seed_test_menu', first);
    assert.equal(first.isError, false, `seed_test_menu failed: ${first.text.slice(0, 300)}`);

    const second = await c.callTool('seed_test_menu', {});
    assertGraceful('seed_test_menu(2nd)', second);
    assert.equal(second.isError, false, `seed_test_menu re-run failed: ${second.text.slice(0, 300)}`);
  });

  test('seed_historical_sales is idempotent', async (t) => {
    const c = requireServer(t);

    const args = { restaurantId: SEEDED_RESTAURANT_ID, daysBack: 2, ordersPerDay: 2 };
    const first = await c.callTool('seed_historical_sales', args);
    assertGraceful('seed_historical_sales', first);
    assert.equal(first.isError, false, `seed_historical_sales failed: ${first.text.slice(0, 400)}`);

    // The second run is the interesting one: it must reuse the
    // existing customer row instead of colliding on the unique
    // email index.
    const second = await c.callTool('seed_historical_sales', args);
    assertGraceful('seed_historical_sales(2nd)', second);
    assert.equal(second.isError, false, `seed_historical_sales re-run failed: ${second.text.slice(0, 400)}`);
  });

  test('publish_integration_event accepts a valid event', async (t) => {
    const c = requireServer(t);

    const res = await c.callTool('publish_integration_event', {
      eventName: 'KitchenOrderAcceptedIntegrationEvent',
      payload: { OrderId: '00000000-0000-0000-0000-0000000000ff' },
    });
    assertGraceful('publish_integration_event', res);
    assert.equal(res.isError, false, `publish_integration_event failed: ${res.text.slice(0, 300)}`);
  });

  test('an unknown event type is rejected gracefully', async (t) => {
    const c = requireServer(t);

    const res = await c.callTool('publish_integration_event', {
      eventName: 'NoSuchIntegrationEvent',
      payload: {},
    });
    assertGraceful('publish_integration_event(unknown)', res);
    assert.equal(res.isError, true, 'an unknown event type should have been rejected');
  });

  test('trigger_scheduled_jobs reaches the dev trigger endpoint', async (t) => {
    const c = requireServer(t);

    const res = await c.callTool('trigger_scheduled_jobs', { jobName: 'outbox-relay' });
    assertGraceful('trigger_scheduled_jobs', res);
    assert.equal(res.isError, false, `trigger_scheduled_jobs failed: ${res.text.slice(0, 300)}`);
  });
});

describe('destructive tools refuse without proper confirmation', () => {
  test('reset_databases refuses a partial confirmation', async (t) => {
    const c = requireServer(t);

    // `targets` defaults to ALL five databases. Confirming a single
    // one must NOT be enough — that would wipe the whole stack on a
    // stray "yes".
    const res = await c.callTool('reset_databases', {
      confirm: true,
      confirmText: 'catalog',
    });
    assertGraceful('reset_databases(partial)', res);
    assert.equal(res.isError, true, 'reset_databases accepted a partial confirmation');
    assert.match(res.text, /confirmText/i, `unexpected error shape: ${res.text.slice(0, 300)}`);
  });

  test('reset_databases refuses a missing confirmation', async (t) => {
    const c = requireServer(t);

    const res = await c.callTool('reset_databases', { targets: ['catalog'] });
    assertGraceful('reset_databases(no-confirm)', res);
    assert.equal(res.isError, true, 'reset_databases ran without confirmation');
  });

  test('simulate_service_outage refuses a non-allowlisted container', async (t) => {
    const c = requireServer(t);

    // The broker and the databases must never be stoppable.
    for (const container of ['messagebroker', 'catalogdb', 'orderdb']) {
      const res = await c.callTool('simulate_service_outage', {
        serviceName: container,
        durationSeconds: 1,
      });
      assertGraceful(`simulate_service_outage(${container})`, res);
      assert.equal(res.isError, true, `simulate_service_outage allowed "${container}"`);
    }
  });
});

describe('argument validation happens server-side, not as a crash', () => {
  test('invalid arguments produce a validation error, not a stack trace', async (t) => {
    const c = requireServer(t);

    const cases: Array<{ tool: string; args: Record<string, unknown> }> = [
      { tool: 'get_api_schema', args: { serviceName: 'not-a-service' } },
      { tool: 'generate_dev_token', args: { role: 'Wizard' } },
      { tool: 'verify_token', args: {} },
      { tool: 'get_recent_logs', args: { serviceName: 'nope' } },
      { tool: 'get_flow_architecture', args: { flowName: 'nope' } },
      { tool: 'verify_flow_state', args: { entityType: 'nope', entityId: 'x', expectedState: 'y' } },
      { tool: 'simulate_service_outage', args: { serviceName: 'messagebroker' } },
      { tool: 'trace_business_flow', args: { flowName: 'nope' } },
    ];

    for (const { tool, args } of cases) {
      let res;
      try {
        res = await c.callTool(tool, args);
      } catch (err) {
        // A protocol-level error for a schema violation is fine too,
        // as long as it is a clean MCP error rather than a crash.
        const message = (err as Error).message;
        const leaked = detectRuntimeErrors(message);
        assert.deepEqual(leaked, [], `${tool} crashed on invalid args: ${leaked.join(', ')}\n${message}`);
        continue;
      }
      assertGraceful(`${tool}(invalid args)`, res);
    }
  });

  test('a tool called with no arguments still answers', async (t) => {
    const c = requireServer(t);

    // Every tool must either succeed or return a structured error —
    // never hang, never crash the transport.
    const res = await c.callTool('verify_flow_state', {});
    assertGraceful('verify_flow_state(no args)', res);
  });
});

describe('the checkout flow runs end-to-end and reports honestly', () => {
  test('trace_business_flow(checkout) completes and reports a truthful verdict', async (t) => {
    const c = requireServer(t);

    const res = await c.callTool('trace_business_flow', { flowName: 'checkout' });
    assertGraceful('trace_business_flow(checkout)', res);

    // The tool returns the full GoldenPathDoc either way. A failing
    // flow sets `isError` (the handler threw) but still ships the
    // document, so the verdict is read from the payload — not from
    // the envelope.
    const doc = res.data as {
      runId?: string;
      flowName?: string;
      pass?: boolean;
      steps?: Array<{ kind: string; step: string; status?: number; note?: string }>;
    };

    assert.ok(doc, `trace_business_flow returned no document: ${res.text.slice(0, 300)}`);
    assert.equal(doc.flowName, 'checkout');
    assert.ok(doc.runId, 'the flow returned no runId');
    assert.ok(Array.isArray(doc.steps) && doc.steps.length > 0, 'the flow recorded no steps');

    // The HTTP legs must have actually run.
    const addItem = doc.steps?.find((s) => s.step === 'add-item-to-basket');
    const checkout = doc.steps?.find((s) => s.step === 'checkout');
    assert.ok(addItem, 'the flow did not run the add-item-to-basket step');
    assert.ok(checkout, 'the flow did not run the checkout step');
    assert.equal(addItem.status, 201, `adding to the basket failed: ${addItem.status}`);
    assert.equal(checkout.status, 200, `checkout failed: ${checkout.status}`);

    // The verdict must be honest. Today the basket accepts the
    // checkout but the order does not project into OrderDb (a
    // .NET-side int/GUID contract defect), so the flow MUST report
    // a failure — and must say why. If the .NET side is ever fixed,
    // this test flips to the passing branch automatically.
    if (doc.pass === false) {
      const errorStep = doc.steps?.find((s) => s.kind === 'info' && s.note);
      assert.match(
        errorStep?.note ?? '',
        /did not project|no order row/i,
        `the flow failed but did not explain why: ${errorStep?.note ?? '(no note)'}`,
      );
      // A failing flow must not be reported as an MCP-level crash.
      assert.equal(res.isError, true, 'a failed flow should surface as isError');
    } else {
      assert.equal(
        doc.pass,
        true,
        `the flow reported success but the verification did not pass: ${JSON.stringify(doc.steps).slice(0, 400)}`,
      );
    }
  });
});
