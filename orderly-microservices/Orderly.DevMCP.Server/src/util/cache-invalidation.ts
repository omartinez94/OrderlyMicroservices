/**
 * util/cache-invalidation.ts
 *
 * Handles targeted cache invalidation against Redis for Catalog read models.
 * In Catalog.API, cache keys strictly use the 32-character GUID in "N" format
 * (no hyphens) as defined in Catalog.API/Caching/CacheKeys.cs:
 *   - catalog:menu:{restaurantId:N}
 *   - catalog:ingredients:{restaurantId:N}
 */

import type { Redis } from 'ioredis';
import type { Logger } from '../logger.ts';

/**
 * Normalises a GUID string into the 32-character "N" format (lowercase, no hyphens)
 * matching C#'s Guid.ToString("N").
 */
export function toGuidN(guid: string): string {
  return guid.replace(/-/g, '').toLowerCase();
}

/**
 * Evicts cached menu and ingredient read models for a given restaurant from Redis.
 */
export async function invalidateCatalogCache(
  redis: Redis,
  restaurantId: string,
  logger?: Logger,
): Promise<{ evictedKeys: string[]; deletedCount: number }> {
  const nId = toGuidN(restaurantId);
  const keys = [
    `catalog:menu:${nId}`,
    `catalog:ingredients:${nId}`,
  ];

  try {
    const deletedCount = await redis.del(...keys);
    logger?.debug({ restaurantId, nId, deletedCount, keys }, 'evicted catalog Redis cache keys');
    return { evictedKeys: keys, deletedCount };
  } catch (err) {
    logger?.warn({ err, restaurantId, keys }, 'failed to evict catalog Redis cache keys');
    return { evictedKeys: keys, deletedCount: 0 };
  }
}
