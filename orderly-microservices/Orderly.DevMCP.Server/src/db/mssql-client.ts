/**
 * MSSQL pool factory for Ordering's `OrderDb` on port 1433.
 *
 * Mirrors the connection string format used by ordering.api in
 * docker-compose.override.yml (line 183).
 *
 * Phase 1 uses `createAndConnectMssql()` which opens + pings in one call.
 * Phase 2 may add a `createMssqlPool()` variant if it needs the pool
 * to remain disconnected until first use.
 */

import sql from 'mssql';
import { env, isDevHost } from '../config/env.ts';
import { ConnectionError, HostViolationError } from '../errors/DevMCPError.ts';

export interface MssqlConnection {
  pool: sql.ConnectionPool;
  host: string;
}

export interface CreateMssqlPoolOptions {
  /** Defaults to 'localhost'. */
  host?: string;
  database?: string;
  poolMax?: number;
}

/**
 * Factory + connect + ping in one call. Throws on any failure so the
 * boot script can fail-fast with a structured error.
 */
export async function createAndConnectMssql(
  opts: CreateMssqlPoolOptions = {},
): Promise<MssqlConnection> {
  const host = opts.host ?? 'localhost';
  if (!isDevHost(host)) {
    throw new HostViolationError(host, [host]);
  }

  const config: sql.config = {
    server: host,
    port: 1433,
    database: opts.database ?? 'OrderDb',
    user: 'sa',
    password: env.SA_PASSWORD,
    options: {
      encrypt: false,
      trustServerCertificate: true,
    },
    pool: {
      max: opts.poolMax ?? 10,
      idleTimeoutMillis: 30_000,
    },
    connectionTimeout: 5_000,
  };

  const pool = new sql.ConnectionPool(config);
  try {
    await pool.connect();
    const result = await pool.request().query('SELECT 1 AS ok');
    if (result.recordset[0]?.ok !== 1) {
      throw new Error('unexpected ping response');
    }
    return { pool, host };
  } catch (cause) {
    try { await pool.close(); } catch { /* swallow */ }
    if (cause instanceof HostViolationError) throw cause;
    throw new ConnectionError('mssql connect/ping failed', { cause, context: { host } });
  }
}

/** Ping a connected pool. */
export async function pingMssql(pool: sql.ConnectionPool): Promise<void> {
  try {
    await pool.request().query('SELECT 1');
  } catch (cause) {
    throw new ConnectionError('mssql ping failed', { cause });
  }
}
