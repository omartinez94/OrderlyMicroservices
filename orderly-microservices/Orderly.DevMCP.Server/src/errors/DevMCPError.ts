/**
 * Shared error hierarchy.
 *
 * Every error has a stable `code` string that AI consumers (and our
 * pino logger) can match on without parsing English. Subclasses keep
 * the throw sites readable and let `instanceof` checks work in callers
 * that don't want to branch on `code`.
 */

export type DevMCPErrorCode =
  | 'CONNECTION_FAILED'
  | 'HOST_VIOLATION'
  | 'TOOL_INPUT_INVALID'
  | 'DESTRUCTIVE_OP_REJECTED'
  | 'NOT_IMPLEMENTED'
  | 'INTERNAL';

export interface DevMCPErrorOptions {
  code: DevMCPErrorCode;
  message: string;
  statusCode?: number;
  recoverable?: boolean;
  cause?: unknown;
  /** Optional structured context for logs / MCP error responses. */
  context?: Record<string, unknown>;
}

export class DevMCPError extends Error {
  readonly code: DevMCPErrorCode;
  readonly statusCode: number;
  readonly recoverable: boolean;
  readonly context: Record<string, unknown> | undefined;

  constructor(opts: DevMCPErrorOptions) {
    super(opts.message, { cause: opts.cause });
    this.name = 'DevMCPError';
    this.code = opts.code;
    this.statusCode = opts.statusCode ?? 500;
    this.recoverable = opts.recoverable ?? false;
    this.context = opts.context;
    // Restore the prototype chain after super() for `instanceof` to work
    // when transpiled to ES5; harmless on modern targets.
    Object.setPrototypeOf(this, new.target.prototype);
  }

  /** JSON-friendly representation safe to send back to an AI client. */
  toJSON(): Record<string, unknown> {
    return {
      name: this.name,
      code: this.code,
      message: this.message,
      statusCode: this.statusCode,
      recoverable: this.recoverable,
      ...(this.context !== undefined ? { context: this.context } : {}),
    };
  }
}

/** A network / connection attempt failed. Usually recoverable on retry. */
export class ConnectionError extends DevMCPError {
  constructor(message: string, opts: { cause?: unknown; context?: Record<string, unknown> } = {}) {
    super({ code: 'CONNECTION_FAILED', message, statusCode: 503, recoverable: true, ...opts });
    Object.setPrototypeOf(this, new.target.prototype);
  }
}

/** A non-whitelisted hostname was passed to a connection factory. */
export class HostViolationError extends DevMCPError {
  constructor(host: string, allowed: readonly string[]) {
    super({
      code: 'HOST_VIOLATION',
      message: `refusing to connect to "${host}"; allowed hosts: ${allowed.join(', ')}`,
      statusCode: 403,
      context: { host, allowedHosts: allowed },
    });
    Object.setPrototypeOf(this, new.target.prototype);
  }
}

/** A tool was called with invalid arguments (zod failed, etc.). */
export class ToolInputError extends DevMCPError {
  constructor(message: string, opts: { cause?: unknown; context?: Record<string, unknown> } = {}) {
    super({ code: 'TOOL_INPUT_INVALID', message, statusCode: 400, ...opts });
    Object.setPrototypeOf(this, new.target.prototype);
  }
}

/** A destructive op (reset_databases, etc.) was rejected by policy. */
export class DestructiveOpError extends DevMCPError {
  constructor(message: string, opts: { context?: Record<string, unknown> } = {}) {
    super({ code: 'DESTRUCTIVE_OP_REJECTED', message, statusCode: 409, ...opts });
    Object.setPrototypeOf(this, new.target.prototype);
  }
}

/** Placeholder for tools not yet implemented (Phase 1 may use this). */
export class NotImplementedError extends DevMCPError {
  constructor(tool: string) {
    super({
      code: 'NOT_IMPLEMENTED',
      message: `tool "${tool}" is not implemented in this phase`,
      statusCode: 501,
      context: { tool },
    });
    Object.setPrototypeOf(this, new.target.prototype);
  }
}

/** Type guard — useful for callers that want to branch on `code`. */
export function isDevMCPError(e: unknown): e is DevMCPError {
  return e instanceof DevMCPError;
}
