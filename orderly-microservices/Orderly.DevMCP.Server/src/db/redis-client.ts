/**
 * ioredis factory for the distributed cache (port 6379).
 *
 * The basket key shape used by `inspect_basket` in Phase 2 is
 * `basket-{basketId}` — declared in the plan §6.3.
 */

import { Redis } from 'ioredis';
import { env, isDevHost } from '../config/env.ts';
import { ConnectionError, HostViolationError } from '../errors/DevMCPError.ts';

export interface CreateRedisOptions {
  /** Defaults to 'localhost'. */
  host?: string;
  /** Optional override (Phase 3's reset_databases flushes with FLUSHALL). */
  db?: number;
}

export function createRedis(opts: CreateRedisOptions = {}): Redis {
  const host = opts.host ?? 'localhost';
  if (!isDevHost(host)) {
    throw new HostViolationError(host, [host]);
  }

  return new Redis({
    host,
    port: 6379,
    password: env.REDIS_PASSWORD,
    db: opts.db ?? 0,
    lazyConnect: false,
    connectTimeout: 5_000,
    maxRetriesPerRequest: 3,
    enableReadyCheck: true,
  });
}

/** Cheap PING-based health check. */
export async function pingRedis(redis: Redis): Promise<void> {
  try {
    const reply = await redis.ping();
    if (reply !== 'PONG') {
      throw new Error(`unexpected ping reply: ${reply}`);
    }
  } catch (cause) {
    throw new ConnectionError('redis ping failed', { cause });
  }
}
