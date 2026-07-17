/**
 * Input sanitisation helpers.
 *
 * Per §10.1: `restaurantId` is interpolated into seed strings / SQL
 * parameters in §6.7. Sanitise to a stable 8-char hash so:
 *   1. The original value never appears in logs, SQL parameters, or
 *      seed string artifacts.
 *   2. Cross-restaurant traces are bucketed rather than personally
 *      identifying.
 */

import { createHash } from 'node:crypto';

/** sha256(restaurantId).slice(0, 8) — stable 8-char bucket per restaurantId. */
export function restaurantBucket(restaurantId: string): string {
  return createHash('sha256').update(restaurantId).digest('hex').slice(0, 8);
}
