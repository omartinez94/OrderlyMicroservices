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
  /** Convention for the swagger JSON path. */
  swaggerPath: string;
}

const ENDPOINTS: Record<ServiceName, ServiceEndpoint> = {
  catalog: { containerName: 'catalog.api', port: 6000, httpsPort: 6060, swaggerPath: '/swagger/v1/swagger.json' },
  basket: { containerName: 'basket.api', port: 6001, httpsPort: 6061, swaggerPath: '/swagger/v1/swagger.json' },
  ordering: { containerName: 'ordering.api', port: 6003, httpsPort: 6063, swaggerPath: '/swagger/v1/swagger.json' },
  kitchen: { containerName: 'kitchen.api', port: 6005, httpsPort: 6065, swaggerPath: '/swagger/v1/swagger.json' },
  identity: { containerName: 'identity.api', port: 6007, httpsPort: 6067, swaggerPath: '/swagger/v1/swagger.json' },
  discount: { containerName: 'discount.grpc', port: 6002, httpsPort: 6062, swaggerPath: '/swagger/v1/swagger.json' },
  'yarp-gateway': { containerName: 'yarpapigateway', port: 6004, httpsPort: 6064, swaggerPath: '' },
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
 * Returns the URL for a service's swagger JSON.
 */
export function serviceSwaggerUrl(name: ServiceName): string | undefined {
  const e = ENDPOINTS[name];
  if (!e.swaggerPath) return undefined;
  return `${serviceUrl(name)}${e.swaggerPath}`;
}

/**
 * List of service names that expose swagger. Used by Phase 2's
 * `get_api_schema` tool to validate the `serviceName` parameter.
 */
export const SWAGGER_SERVICES: readonly ServiceName[] = (
  Object.keys(ENDPOINTS) as ServiceName[]
).filter((n) => ENDPOINTS[n].swaggerPath !== '');

/** Convenience: returns the MCP server's own public URL. */
export function selfUrl(): string {
  return `http://${env.HOST}:${env.PORT}`;
}
