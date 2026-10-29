/**
 * Typed map of microservice URLs. All values come from
 * docker-compose.override.yml (the local dev Compose file).
 *
 * Phase 1 doesn't read these URLs — they are declared here so
 * Phase 2's `get_api_schema` and other HTTP-calling tools can
 * import them without re-deriving ports from YAML.
 */

import { env } from './env.ts';

export type ServiceName =
  | 'catalog'
  | 'basket'
  | 'ordering'
  | 'kitchen'
  | 'identity'
  | 'discount'
  | 'yarp-gateway';

export interface ServiceEndpoint {
  /** Container name as it appears in docker-compose. */
  containerName: string;
  /** Localhost port as published by docker-compose.override.yml. */
  port: number;
  /** HTTPS port (for future use). */
  httpsPort?: number;
  /**
   * Candidate OpenAPI document paths, tried in order.
   *
   * The services do NOT agree on a convention: Basket still uses
   * Swashbuckle (`/swagger/v1/swagger.json`), while the others were
   * migrated to .NET 9's built-in `MapOpenApi()` (`/openapi/v1.json`).
   * Listing the candidates lets one fetch path serve both.
   */
  schemaPaths: string[];
  /**
   * True when the service only speaks HTTP/2 without TLS (h2c).
   * `discount.grpc` is a gRPC host: its plain-HTTP listener rejects
   * HTTP/1.1 requests, so the schema must be fetched over h2c.
   */
  http2Only?: boolean;
}

const ENDPOINTS: Record<ServiceName, ServiceEndpoint> = {
  catalog: { containerName: 'catalog.api', port: 6000, httpsPort: 6060, schemaPaths: ['/openapi/v1.json', '/swagger/v1/swagger.json'] },
  basket: { containerName: 'basket.api', port: 6001, httpsPort: 6061, schemaPaths: ['/swagger/v1/swagger.json', '/openapi/v1.json'] },
  ordering: { containerName: 'ordering.api', port: 6003, httpsPort: 6063, schemaPaths: ['/openapi/v1.json', '/swagger/v1/swagger.json'] },
  kitchen: { containerName: 'kitchen.api', port: 6005, httpsPort: 6065, schemaPaths: ['/openapi/v1.json', '/swagger/v1/swagger.json'] },
  identity: { containerName: 'identity.api', port: 6007, httpsPort: 6067, schemaPaths: ['/openapi/v1.json', '/swagger/v1/swagger.json'] },
  discount: { containerName: 'discount.grpc', port: 6002, httpsPort: 6062, schemaPaths: ['/openapi/v1.json', '/swagger/v1/swagger.json'], http2Only: true },
  'yarp-gateway': { containerName: 'yarpapigateway', port: 6004, httpsPort: 6064, schemaPaths: [] },
};

/**
 * Returns the local URL for a service. Always uses `http://`
 * — the plan §4 spec is HTTP+SSE for the MCP transport, and the
 * ASP.NET services all expose HTTP on the same host.
 */
export function serviceUrl(name: ServiceName): string {
  const e = ENDPOINTS[name];
  return `http://localhost:${e.port}`;
}

/**
 * Returns the candidate OpenAPI document URLs for a service, in the
 * order they should be tried. Empty when the service exposes none.
 */
export function serviceSchemaUrls(name: ServiceName): string[] {
  const e = ENDPOINTS[name];
  return e.schemaPaths.map((p) => `${serviceUrl(name)}${p}`);
}

/**
 * True when the service only accepts HTTP/2 cleartext (h2c) requests.
 * `discount.grpc` is one of them — a plain HTTP/1.1 fetch gets a 400.
 */
export function serviceRequiresHttp2(name: ServiceName): boolean {
  return ENDPOINTS[name].http2Only === true;
}

/**
 * List of service names that expose an OpenAPI document. Used by
 * `get_api_schema` to validate the `serviceName` parameter and by
 * `get_recent_logs` to validate the container name.
 */
export const SWAGGER_SERVICES: readonly ServiceName[] = (
  Object.keys(ENDPOINTS) as ServiceName[]
).filter((n) => ENDPOINTS[n].schemaPaths.length > 0);

/** Convenience: returns the MCP server's own public URL. */
export function selfUrl(): string {
  return `http://${env.HOST}:${env.PORT}`;
}
