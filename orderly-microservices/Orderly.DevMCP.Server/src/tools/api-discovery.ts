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
import { SWAGGER_SERVICES, serviceSwaggerUrl, type ServiceName } from '../config/services.ts';
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

async function fetchAndNormalize(
  service: ServiceName,
  logger: Logger,
  cache: LRUCache<ServiceName, NormalizedSchema>,
): Promise<NormalizedSchema> {
  const url = serviceSwaggerUrl(service);
  if (!url) {
    throw new ToolInputError(`service "${service}" has no swagger endpoint`);
  }

  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), FETCH_TIMEOUT_MS);
  let res: Response;
  try {
    res = await fetch(url, { signal: controller.signal });
  } catch (cause) {
    clearTimeout(timer);
    throw new ConnectionError(`failed to fetch swagger from ${url}`, {
      cause,
      context: { service, url },
    });
  }
  clearTimeout(timer);

  if (!res.ok) {
    throw new ConnectionError(
      `swagger for ${service} returned ${res.status} ${res.statusText} — service is probably not in Development mode`,
      { context: { service, url, status: res.status } },
    );
  }

  let raw: unknown;
  try {
    raw = await res.json();
  } catch (cause) {
    throw new ConnectionError(`swagger for ${service} returned invalid JSON`, { cause, context: { service } });
  }

  logger.info({ service, bytes: JSON.stringify(raw).length }, 'fetched swagger');
  const normalized = normalizeSwagger(service, raw);
  cache.set(service, normalized);
  return normalized;
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
