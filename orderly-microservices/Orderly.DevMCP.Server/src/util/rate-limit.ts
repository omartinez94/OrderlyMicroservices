/**
 * In-memory token-bucket rate limiter.
 *
 * Per §10.1: `publish_integration_event` should be rate-limited at 5/min,
 * `reset_databases` at 1/hour. In-memory is fine — the MCP server is
 * a single process. For multi-instance deployments, swap the store out.
 */

export interface RateLimitResult {
  allowed: boolean;
  remaining: number;
  resetMs: number;
}

interface Bucket {
  tokens: number;
  lastRefill: number; // epoch ms
}

export class TokenBucket {
  private readonly buckets = new Map<string, Bucket>();
  private readonly capacity: number;
  private readonly refillIntervalMs: number;

  constructor(capacity: number, refillIntervalMs: number) {
    this.capacity = capacity;
    this.refillIntervalMs = refillIntervalMs;
  }

  /**
   * Try to consume one token for `key`. Returns whether it's allowed
   * plus the remaining count and ms until the next refill.
   */
  consume(key: string, now: number = Date.now()): RateLimitResult {
    let bucket = this.buckets.get(key);
    if (!bucket) {
      bucket = { tokens: this.capacity, lastRefill: now };
      this.buckets.set(key, bucket);
    }

    // Refill: every `refillIntervalMs`, gain 1 token (capped at capacity).
    const elapsed = now - bucket.lastRefill;
    if (elapsed >= this.refillIntervalMs) {
      const gained = Math.floor(elapsed / this.refillIntervalMs);
      bucket.tokens = Math.min(this.capacity, bucket.tokens + gained);
      bucket.lastRefill = now;
    }

    if (bucket.tokens <= 0) {
      return { allowed: false, remaining: 0, resetMs: this.refillIntervalMs - (now - bucket.lastRefill) };
    }

    bucket.tokens -= 1;
    return { allowed: true, remaining: bucket.tokens, resetMs: this.refillIntervalMs };
  }
}

/**
 * Process-wide singletons. Configured per §10.1.
 * - `publish`: 5 tokens, refilled every 12s = 5/min.
 * - `reset`:   1 token,  refilled every 3600_000ms = 1/hour.
 */
export const rateLimits = {
  publish: new TokenBucket(5, 12_000),
  reset: new TokenBucket(1, 3_600_000),
};
