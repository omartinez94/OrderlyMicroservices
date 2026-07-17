/**
 * PostgreSQL pool factory for the four Marten-backed services:
 * Catalogdb (5433), Basketdb (5434), Identitydb (5435), Kitchendb (5436).
 *
 * All connections go through `assertDevHost` before any I/O so a
 * misconfigured tool cannot bypass the dev-host allow-list (§10.1).
 */

import pg from 'pg';
import { env, isDevHost } from '../config/env.ts';
import { ConnectionError, HostViolationError } from '../errors/DevMCPError.ts';

export type PostgresService = 'catalog' | 'basket' | 'kitchen' | 'identity';

const PORTS: Record<PostgresService, number> = {
  catalog: 5433,
  basket: 5434,
  kitchen: 5436,
  identity: 5435,
};

const DATABASES: Record<PostgresService, string> = {
  catalog: 'Catalogdb',
  basket: 'Basketdb',
  kitchen: 'Kitchendb',
  identity: 'Identitydb',
};

export interface CreatePostgresPoolOptions {
  service: PostgresService;
  /** Defaults to 'localhost'. Pass a hostname on the dev allow-list. */
  host?: string;
  max?: number;
}

export function createPostgresPool(opts: CreatePostgresPoolOptions): pg.Pool {
  const host = opts.host ?? 'localhost';
  if (!isDevHost(host)) {
    throw new HostViolationError(host, [host]);
  }

  return new pg.Pool({
    host,
    port: PORTS[opts.service],
    database: DATABASES[opts.service],
    user: env.POSTGRES_USER,
    password: env.POSTGRES_PASSWORD,
    max: opts.max ?? 10,
    idleTimeoutMillis: 30_000,
    connectionTimeoutMillis: 5_000,
  });
}

/**
 * Cheap health check used at startup. Throws a `ConnectionError`
 * so the boot script can fail-fast with a structured error.
 */
export async function pingPostgres(pool: pg.Pool, service: PostgresService): Promise<void> {
  try {
    await pool.query('SELECT 1');
  } catch (cause) {
    throw new ConnectionError(`postgres ping failed for ${service}`, { cause, context: { service } });
  }
}
