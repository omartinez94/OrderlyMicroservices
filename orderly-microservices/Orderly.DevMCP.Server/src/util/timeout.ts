/**
 * `withTimeout` — Promise race helper that supports cancellation.
 *
 * Used by `get_system_snapshot` (§6.9) to give each sub-query a 3 s
 * budget without leaking the request on a slow backend.
 *
 * Implementation note: we race the input promise against a delay, and
 * if the delay wins we throw TimeoutError. The caller can pass an
 * `onTimeout` callback to release resources (e.g. abort the underlying
 * pg query) so a stuck backend does not hold handles indefinitely.
 */

import { setTimeout as delay } from 'node:timers/promises';

export class TimeoutError extends Error {
  readonly code = 'TIMEOUT';
  readonly label: string;
  readonly timeoutMs: number;
  constructor(label: string, timeoutMs: number) {
    super(`"${label}" timed out after ${timeoutMs}ms`);
    this.name = 'TimeoutError';
    this.label = label;
    this.timeoutMs = timeoutMs;
    Object.setPrototypeOf(this, new.target.prototype);
  }
}

export interface WithTimeoutOptions {
  /** Called when the timeout fires. Use it to abort the underlying request. */
  onTimeout?: () => void;
}

export async function withTimeout<T>(
  promise: Promise<T>,
  timeoutMs: number,
  label: string,
  opts: WithTimeoutOptions = {},
): Promise<T> {
  let timer: NodeJS.Timeout | undefined;
  const timeout = new Promise<never>((_, reject) => {
    timer = setTimeout(() => {
      opts.onTimeout?.();
      reject(new TimeoutError(label, timeoutMs));
    }, timeoutMs);
    // Don't keep the event loop alive just for this timer.
    timer.unref();
  });

  try {
    return await Promise.race([promise, timeout]);
  } finally {
    if (timer !== undefined) clearTimeout(timer);
  }
}

/**
 * `sleep` — tiny re-export so callers can `import { sleep }` from one place.
 */
export const sleep = delay;
