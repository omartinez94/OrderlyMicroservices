/**
 * Pino logger with secret redaction.
 *
 * Redacted paths:
 *   - JWT_SECRET / Jwt:Secret            — Phase 2's signing key
 *   - password / *.password / *.Password — every database password
 *   - connectionString                   — pg / mssql conn strings
 *   - Authorization                      — every HTTP request header
 *
 * The redact list uses pino's `*.foo` wildcard syntax so any object
 * field named `password` (e.g. inside a request body) is also masked.
 */

import pino from 'pino';
import { env } from './config/env.ts';

export const logger = pino({
  level: env.LOG_LEVEL,
  redact: {
    paths: [
      'JWT_SECRET',
      'Jwt:Secret',
      'password',
      '*.password',
      '*.Password',
      'connectionString',
      'ConnectionString',
      'Authorization',
      'headers.Authorization',
      'req.headers.authorization',
      'res.headers.authorization',
    ],
    censor: '[REDACTED]',
  },
  base: {
    app: 'orderly-devmcp',
    pid: process.pid,
  },
  timestamp: pino.stdTimeFunctions.isoTime,
});

export type Logger = typeof logger;
