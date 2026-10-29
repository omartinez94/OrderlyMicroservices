/**
 * Deterministic pseudo-random helpers shared by the seeding tools.
 *
 * mulberry32 (CC0, single 32-bit state, no dependencies) is the
 * generator `seed_historical_sales` has always used. It lives here so
 * `create_mock_order` can share the exact same algorithm — the same
 * seed string must keep producing the same output it produced before
 * the extraction.
 */

import { createHash } from 'node:crypto';

/**
 * mulberry32 — a 32-bit PRNG returning floats in [0, 1).
 *
 * Not cryptographically secure; it only has to be fast, tiny and
 * reproducible across processes.
 */
export function mulberry32(seed: number): () => number {
  let a = seed >>> 0;
  return () => {
    a = (a + 0x6D2B79F5) >>> 0;
    let t = a;
    t = Math.imul(t ^ (t >>> 15), t | 1);
    t ^= t + Math.imul(t ^ (t >>> 7), t | 61);
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}

/** Maps an arbitrary string to a 32-bit seed (sha256, little-endian). */
export function seedFromString(s: string): number {
  const h = createHash('sha256').update(s).digest();
  return h.readUInt32LE(0);
}

/**
 * Uniform integer in [minInclusive, maxInclusive].
 *
 * Returns `minInclusive` when the range is empty so callers never have
 * to guard against an inverted range.
 */
export function randomInt(rng: () => number, minInclusive: number, maxInclusive: number): number {
  if (maxInclusive < minInclusive) return minInclusive;
  const span = maxInclusive - minInclusive + 1;
  return minInclusive + Math.floor(rng() * span);
}

/**
 * A fresh 32-bit seed from the platform CSPRNG — used when the caller
 * did not supply an explicit seed and wants an unseeded (non-repeating)
 * stream.
 */
export function randomSeed(): number {
  const buf = new Uint32Array(1);
  crypto.getRandomValues(buf);
  return buf[0]!;
}
