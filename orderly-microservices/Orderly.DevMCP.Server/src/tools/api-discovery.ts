/**
 * tools/api-discovery.ts — `get_api_schema(serviceName)` (§6.1).
 *
 * Fetches the live OpenAPI/Swagger document for a microservice and
 * returns a compact, LLM-friendly shape:
 *
 *   { serviceName, schemaVersion, endpointCount, endpoints: [{method, path, summary, tags, requestSchema?, responseSchema?}] }
 *
 * Strategy:
 *   - Fetch `http://localhost:{port}/swagger/v1/swagger.json` via `fetch`.
 *   - Normalise: strip `servers[]`, `x-*` extension noise, drop empty
 *     descriptions, collapse path parameters to `{paramName}` form.
 *   - Cache results in an LRU keyed by serviceName, TTL 5 min, max 50.
 *   - Generate a `schemaVersion` from the OpenAPI version + fetched-at
 *     timestamp so the AI can detect contract changes between calls.
 *
 * §10.3 notes that payloads >256 KB (Catalog) should normalise in a
 * worker thread. We use inline normalisation for now; if Catalog's
 * swagger.json proves slow to normalise, move it to a worker.
 */

import { LRUCache } from 'lru-cache';
import { z } from 'zod';
import type { McpServer } from '@modelcontextprotocol/sdk/server/mcp.js';

import type { Logger } from '../logger.ts';
import { SWAGGER_SERVICES, serviceSchemaUrls, serviceRequiresHttp2, type ServiceName } from '../config/services.ts';
import { ToolInputError, ConnectionError } from '../errors/DevMCPError.ts';

const CACHE_TTL_MS = 5 * 60 * 1000;
const CACHE_MAX = 50;
const FETCH_TIMEOUT_MS = 5_000;

interface Endpoint {
  method: string;
  path: string;
  summary?: string;
  tags?: string[];
  requestSchema?: unknown;
  responseSchema?: unknown;
}

interface NormalizedSchema {
  serviceName: ServiceName;
  schemaVersion: string;
  fetchedAt: string;
  endpointCount: number;
  endpoints: Endpoint[];
}

/**
 * Strip `servers[]`, `x-*`, empty descriptions; collapse operation shape.
 * Pure function — easy to move to a worker thread if needed.
 */
function normalizeSwagger(serviceName: ServiceName, raw: unknown): NormalizedSchema {
  if (typeof raw !== 'object' || raw === null) {
    throw new ToolInputError('swagger response is not an object');
  }
  const doc = raw as Record<string, unknown>;
  const openapiVersion = typeof doc.openapi === 'string' ? doc.openapi : (typeof doc.swagger === 'string' ? doc.swagger : 'unknown');
  const paths = (doc.paths as Record<string, unknown> | undefined) ?? {};
  const components = (doc.components as { schemas?: Record<string, unknown> } | undefined)?.schemas ?? {};

  const endpoints: Endpoint[] = [];
  for (const [path, pathItemRaw] of Object.entries(paths)) {
    if (typeof pathItemRaw !== 'object' || pathItemRaw === null) continue;
    const pathItem = pathItemRaw as Record<string, unknown>;
    for (const [method, opRaw] of Object.entries(pathItem)) {
      const lowerMethod = method.toLowerCase();
      if (!['get', 'post', 'put', 'patch', 'delete', 'options', 'head'].includes(lowerMethod)) continue;
      if (typeof opRaw !== 'object' || opRaw === null) continue;
      const op = opRaw as Record<string, unknown>;
      const summary = typeof op.summary === 'string' ? op.summary : undefined;
      const tags = Array.isArray(op.tags) ? op.tags.filter((t): t is string => typeof t === 'string') : undefined;
      const requestSchema = extractRequestSchema(op);
      const responseSchema = extractResponseSchema(op, components);
      const entry: Endpoint = { method: lowerMethod, path, ...(summary !== undefined ? { summary } : {}), ...(tags !== undefined && tags.length > 0 ? { tags } : {}), ...(requestSchema !== undefined ? { requestSchema } : {}), ...(responseSchema !== undefined ? { responseSchema } : {}) };
      endpoints.push(entry);
    }
  }

  return {
    serviceName,
    schemaVersion: `openapi=${openapiVersion}@${new Date().toISOString()}`,
    fetchedAt: new Date().toISOString(),
    endpointCount: endpoints.length,
    endpoints,
  };
}

function extractRequestSchema(op: Record<string, unknown>): unknown {
  const rb = op.requestBody as { content?: Record<string, { schema?: unknown }> } | undefined;
  return rb?.content?.['application/json']?.schema;
}

function extractResponseSchema(op: Record<string, unknown>, _components: Record<string, unknown>): unknown {
  const responses = op.responses as Record<string, { content?: Record<string, { schema?: unknown }> }> | undefined;
  const ok = responses?.['200'] ?? responses?.['201'];
  return ok?.content?.['application/json']?.schema;
}

/**
 * Fetch a JSON document over h2c (HTTP/2 cleartext).
 *
 * `discount.grpc` runs a gRPC host whose plain-HTTP listener rejects
 * HTTP/1.1 with "An HTTP/1.x request was sent to an HTTP/2 only
 * endpoint." Node's global `fetch` speaks HTTP/1.1 only, so the h2c
 * client from `node:http2` is required for that host.
 */
async function fetchJsonHttp2(url: string, timeoutMs: number): Promise<{ status: number; body: string }> {
  const { connect } = await import('node:http2');
  const target = new URL(url);

  return new Promise<{ status: number; body: string }>((resolve, reject) => {
    const client = connect(`${target.protocol}//${target.host}`);
    let settled = false;

    const timer = setTimeout(() => {
      if (settled) return;
      settled = true;
      client.destroy();
      reject(new Error(`timed out after ${timeoutMs}ms`));
    }, timeoutMs);

    client.on('error', (err) => {
      if (settled) return;
      settled = true;
      clearTimeout(timer);
      reject(err);
    });

    const req = client.request({ ':method': 'GET', ':path': `${target.pathname}${target.search}` });
    let status = 0;
    const chunks: Buffer[] = [];

    req.on('response', (headers) => {
      status = Number(headers[':status'] ?? 0);
    });
    req.on('data', (chunk: Buffer) => chunks.push(chunk));
    req.on('error', (err) => {
      if (settled) return;
      settled = true;
      clearTimeout(timer);
      client.destroy();
      reject(err);
    });
    req.on('end', () => {
      if (settled) return;
      settled = true;
      clearTimeout(timer);
      client.close();
      resolve({ status, body: Buffer.concat(chunks).toString('utf-8') });
    });
    req.end();
  });
}

/**
 * Fetch a JSON document over HTTP/1.1.
 *
 * Uses `node:http` rather than the global `fetch` on purpose: the
 * WHATWG fetch spec blocks a list of ports (6000 is on it — the X11
 * port), so `fetch('http://localhost:6000/...')` fails with
 * "bad port" before a single byte is sent. Catalog is published on
 * 6000, so the global `fetch` cannot reach it at all.
 */
async function fetchJsonHttp1(url: string, timeoutMs: number): Promise<{ status: number; body: string }> {
  const { request } = await import('node:http');
  const target = new URL(url);

  return new Promise<{ status: number; body: string }>((resolve, reject) => {
    const req = request(
      {
        hostname: target.hostname,
        port: target.port,
        path: `${target.pathname}${target.search}`,
        method: 'GET',
        timeout: timeoutMs,
      },
      (res) => {
        const status = res.statusCode ?? 0;
        const chunks: Buffer[] = [];
        res.on('data', (chunk: Buffer) => chunks.push(chunk));
        res.on('end', () => resolve({ status, body: Buffer.concat(chunks).toString('utf-8') }));
        res.on('error', reject);
      },
    );
    req.on('timeout', () => req.destroy(new Error(`timed out after ${timeoutMs}ms`)));
    req.on('error', reject);
    req.end();
  });
}

async function fetchAndNormalize(
  service: ServiceName,
  logger: Logger,
  cache: LRUCache<ServiceName, NormalizedSchema>,
): Promise<NormalizedSchema> {
  const urls = serviceSchemaUrls(service);
  if (urls.length === 0) {
    throw new ToolInputError(`service "${service}" has no OpenAPI endpoint`);
  }

  const http2 = serviceRequiresHttp2(service);
  const failures: string[] = [];

  // The services disagree on the OpenAPI path convention (Swashbuckle
  // vs .NET 9 `MapOpenApi()`), so each candidate is tried in turn and
  // the first one that returns a parsable document wins.
  for (const url of urls) {
    let status: number;
    let body: string;
    try {
      const res = http2
        ? await fetchJsonHttp2(url, FETCH_TIMEOUT_MS)
        : await fetchJsonHttp1(url, FETCH_TIMEOUT_MS);
      status = res.status;
      body = res.body;
    } catch (cause) {
      failures.push(`${url}: ${cause instanceof Error ? cause.message : String(cause)}`);
      continue;
    }

    if (status !== 200) {
      failures.push(`${url}: HTTP ${status}`);
      continue;
    }

    let raw: unknown;
    try {
      raw = JSON.parse(body);
    } catch (cause) {
      failures.push(`${url}: invalid JSON`);
      continue;
    }

    logger.info({ service, url, bytes: body.length }, 'fetched OpenAPI document');
    const normalized = normalizeSwagger(service, raw);
    cache.set(service, normalized);
    return normalized;
  }

  throw new ConnectionError(
    `could not fetch an OpenAPI document for ${service} — tried ${failures.join('; ')}`,
    { context: { service, urls } },
  );
}

export interface ApiDiscoveryDeps {
  logger: Logger;
}

export function registerApiDiscoveryTools(server: McpServer, deps: ApiDiscoveryDeps): void {
  const cache = new LRUCache<ServiceName, NormalizedSchema>({
    max: CACHE_MAX,
    ttl: CACHE_TTL_MS,
  });

  server.registerTool(
    'get_api_schema',
    {
      title: 'Get API schema',
      description:
        'Fetches and normalises the OpenAPI/Swagger definition for a microservice. ' +
        'Returns a compact, LLM-friendly shape (endpoint, method, summary, request/response schemas). ' +
        'Cached for 5 minutes per service.',
      inputSchema: {
        serviceName: z
          .enum(SWAGGER_SERVICES as readonly [ServiceName, ...ServiceName[]])
          .describe(`Service name. One of: ${SWAGGER_SERVICES.join(', ')}.`),
      },
    },
    async (args) => {
      const { serviceName } = args as { serviceName: ServiceName };

      const cached = cache.get(serviceName);
      if (cached !== undefined) {
        return {
          content: [
            {
              type: 'text' as const,
              text: JSON.stringify({ ...cached, cached: true }, null, 2),
            },
          ],
        };
      }

      try {
        const normalized = await fetchAndNormalize(serviceName, deps.logger, cache);
        return {
          content: [
            { type: 'text' as const, text: JSON.stringify({ ...normalized, cached: false }, null, 2) },
          ],
        };
      } catch (err) {
        const message = err instanceof Error ? err.message : String(err);
        const code = (err as { code?: string }).code ?? 'UNKNOWN';
        return {
          content: [{ type: 'text' as const, text: JSON.stringify({ error: { code, message }, service: serviceName }, null, 2) }],
          isError: true,
        };
      }
    },
  );
}
