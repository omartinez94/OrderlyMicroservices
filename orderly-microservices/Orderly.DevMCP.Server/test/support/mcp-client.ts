/**
 * test/support/mcp-client.ts
 *
 * A REAL MCP client that talks to a RUNNING DevMCP server over
 * Streamable HTTP.
 *
 * Why this exists: the unit tests import the tool modules and call
 * the handlers directly. That bypasses everything that actually
 * breaks in practice — the JSON-RPC framing, the session
 * negotiation, the HTTP transport, the per-session tool
 * registration, and the error envelope. A tool can typecheck, pass
 * a unit test, and still blow up the moment a client calls it
 * through the protocol.
 *
 * So these tests do exactly what an AI assistant does: connect,
 * initialise, list tools, and call them. Nothing is stubbed.
 *
 * The server must already be running (see test/support/README.md).
 */

import { Client } from '@modelcontextprotocol/sdk/client/index.js';
import { StreamableHTTPClientTransport } from '@modelcontextprotocol/sdk/client/streamableHttp.js';
import type { Tool } from '@modelcontextprotocol/sdk/types.js';

export const DEFAULT_MCP_URL = 'http://localhost:8080/mcp';

/** Every tool the server is expected to expose. */
export const EXPECTED_TOOLS = [
  'generate_dev_token',
  'verify_token',
  'get_api_schema',
  'inspect_basket',
  'inspect_order_pipeline',
  'get_system_snapshot',
  'watch_system',
  'get_recent_logs',
  'seed_test_menu',
  'create_mock_order',
  'publish_integration_event',
  'inspect_dead_letters',
  'reset_databases',
  'flush_orders',
  'simulate_service_outage',
  'seed_historical_sales',
  'trigger_scheduled_jobs',
  'trace_business_flow',
  'get_flow_architecture',
  'verify_flow_state',
] as const;

export interface CallResult {
  /** Parsed JSON payload the tool returned. */
  data: unknown;
  /** Raw text of the first content block. */
  text: string;
  /** True when the tool reported an error through the MCP envelope. */
  isError: boolean;
  /** Structured error object, when the tool returned one. */
  error: { code?: string; message?: string } | undefined;
}

/**
 * A tool descriptor as advertised by the server.
 *
 * Uses the SDK's own `Tool` type verbatim: under
 * `exactOptionalPropertyTypes` the SDK declares `description?:
 * string | undefined`, which is not assignable to a locally-declared
 * `description?: string`. Re-declaring the shape here just recreates
 * the same conflict, so the SDK type is the source of truth.
 */
export type ToolDescriptor = Tool;

export class McpTestClient {
  // NOTE: no TypeScript parameter properties here. Node's
  // type-stripping loader runs in "strip-only" mode and rejects
  // `constructor(private readonly x: T)` with
  // ERR_UNSUPPORTED_TYPESCRIPT_SYNTAX at import time. Declare the
  // fields explicitly and assign them in the constructor body.
  private readonly client: Client;
  private readonly transport: StreamableHTTPClientTransport;

  private constructor(client: Client, transport: StreamableHTTPClientTransport) {
    this.client = client;
    this.transport = transport;
  }

  /**
   * Connect to a running MCP server and complete the handshake.
   * Throws if the server is unreachable or does not speak MCP.
   */
  static async connect(url: string = DEFAULT_MCP_URL): Promise<McpTestClient> {
    const client = new Client({ name: 'devmcp-wire-test', version: '1.0.0' });
    const transport = new StreamableHTTPClientTransport(new URL(url));
    // The SDK's `Transport` interface declares `sessionId?: string`, but
    // `exactOptionalPropertyTypes` is on in this project, so the
    // transport's `string | undefined` is not assignable. The two
    // types describe the same runtime shape; assert it once here
    // rather than loosening the project-wide compiler setting.
    await client.connect(transport as unknown as Parameters<Client['connect']>[0]);
    return new McpTestClient(client, transport);
  }

  /** Tool names the server advertises over the wire. */
  async toolNames(): Promise<string[]> {
    const { tools } = await this.client.listTools();
    return tools.map((t) => t.name);
  }

  /** Full tool descriptors, including their JSON schemas. */
  async toolDescriptors(): Promise<ToolDescriptor[]> {
    const { tools } = await this.client.listTools();
    return tools;
  }

  /**
   * Call a tool through the MCP protocol.
   *
   * A tool that throws inside its handler comes back as an MCP result
   * with `isError: true` and the exception text as the content — NOT
   * as a protocol-level error. Both shapes are surfaced here so the
   * tests can assert on whichever one occurred.
   */
  async callTool(name: string, args: Record<string, unknown> = {}): Promise<CallResult> {
    const result = await this.client.callTool({ name, arguments: args });

    const text = (result.content as Array<{ type: string; text?: string }> | undefined)
      ?.filter((c) => c.type === 'text')
      .map((c) => c.text ?? '')
      .join('\n') ?? '';

    let data: unknown;
    try {
      data = JSON.parse(text);
    } catch {
      data = undefined;
    }

    const error =
      data && typeof data === 'object' && 'error' in data
        ? (data as { error: { code?: string; message?: string } }).error
        : undefined;

    return { data, text, isError: result.isError === true, error };
  }

  async close(): Promise<void> {
    await this.client.close().catch(() => undefined);
  }
}

/**
 * True when the server is reachable and speaks MCP.
 * Used to skip wire tests instead of failing them when the dev stack
 * is simply not running.
 */
export async function isMcpServerUp(url: string = DEFAULT_MCP_URL): Promise<boolean> {
  const client = await McpTestClient.connect(url).catch(() => null);
  if (!client) return false;
  await client.close();
  return true;
}

/**
 * Error text that proves a tool crashed rather than degrading.
 *
 * The server is expected to catch backend failures and return a
 * structured `{ error: { code, message } }` payload. When it does
 * NOT, the raw exception text leaks through and the caller sees
 * things like "Cannot read properties of undefined" — a just-in-time
 * failure the typechecker could never have caught.
 */
export const RUNTIME_ERROR_PATTERNS: ReadonlyArray<{ pattern: RegExp; label: string }> = [
  { pattern: /is not a constructor/i, label: 'not-a-constructor' },
  { pattern: /is not a function/i, label: 'not-a-function' },
  { pattern: /Cannot read propert(?:y|ies) of (?:undefined|null)/i, label: 'read-of-undefined' },
  { pattern: /Cannot access .* before initialization/i, label: 'temporal-dead-zone' },
  { pattern: /\bundefined is not\b/i, label: 'undefined-value' },
  { pattern: /\bnull is not\b/i, label: 'null-value' },
  { pattern: /Maximum call stack size exceeded/i, label: 'stack-overflow' },
  { pattern: /\[object Object\]/i, label: 'object-stringified' },
  { pattern: /\bECONNREFUSED\b/i, label: 'econnrefused' },
  { pattern: /\bENOENT\b/i, label: 'enoent' },
  { pattern: /\bEADDRINUSE\b/i, label: 'eaddrinuse' },
  { pattern: /TypeError:/i, label: 'typeerror' },
  { pattern: /ReferenceError:/i, label: 'referenceerror' },
  { pattern: /SyntaxError:/i, label: 'syntaxerror' },
  { pattern: /\bSyntaxError\b/i, label: 'syntaxerror' },
  { pattern: /RangeError:/i, label: 'rangeerror' },
];

/** Every runtime-error label found in the text, if any. */
export function detectRuntimeErrors(text: string): string[] {
  return RUNTIME_ERROR_PATTERNS.filter((p) => p.pattern.test(text)).map((p) => p.label);
}
