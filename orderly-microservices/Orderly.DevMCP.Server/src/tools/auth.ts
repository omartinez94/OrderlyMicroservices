/**
 * tools/auth.ts — dev token minting + verification (§6.4).
 *
 * Tokens are HS256-signed with `JWT_SECRET` from `.env`. They are
 * **not** accepted by the real Identity service (which uses OpenIddict
 * certificate signing). To make dev tokens actually work against the
 * running APIs, the .NET services need a fallback dev-secret handler
 * that accepts tokens signed with the same `JWT_SECRET`. That wiring
 * is tracked separately as a Phase 2 follow-up.
 *
 * Algorithm is pinned explicitly to `HS256` so the jsonwebtoken
 * library's default doesn't fall back to `none` (§10.3).
 *
 * `verify_token` results are cached for 30 s, keyed by `sha256(token)`
 * so the raw token never lands in logs (§10.1).
 */

import { createHash, randomUUID } from 'node:crypto';
import { LRUCache } from 'lru-cache';
import jwt from 'jsonwebtoken';
import { z } from 'zod';
import type { McpServer } from '@modelcontextprotocol/sdk/server/mcp.js';

import { getSecret } from '../config/env.ts';
import type { Logger } from '../logger.ts';
import { ROLES, type Role } from './types.ts';

const ISSUER = 'orderly-devmcp';
const AUDIENCE = 'OrderlyMicroservices';
const VERIFY_CACHE_TTL_MS = 30_000;
const VERIFY_CACHE_MAX = 256;

interface TokenClaims {
  sub: string;
  role: Role;
  restaurantId?: string;
  iss: string;
  aud: string;
  iat: number;
  exp: number;
}

/** sha256(token) — used as the cache key so the raw JWT never lands in logs. */
function tokenHash(token: string): string {
  return createHash('sha256').update(token).digest('hex');
}

export interface AuthToolsDeps {
  logger: Logger;
}

export function registerAuthTools(server: McpServer, deps: AuthToolsDeps): void {
  const verifyCache = new LRUCache<string, TokenClaims>({
    max: VERIFY_CACHE_MAX,
    ttl: VERIFY_CACHE_TTL_MS,
  });

  // ── generate_dev_token ────────────────────────────────────────────────

  server.registerTool(
    'generate_dev_token',
    {
      title: 'Generate dev JWT',
      description:
        'Signs an HS256 dev token with claims {sub, role, restaurantId, iss, aud, iat, exp}. ' +
        'Algorithm is pinned to HS256 explicitly. ' +
        'NOTE: dev tokens are NOT accepted by the real Identity service (which uses OpenIddict). ' +
        'Use only against APIs that have been wired with a fallback dev-secret handler.',
      inputSchema: {
        role: z.enum(ROLES).describe('Role claim — Admin, Manager, Staff, or Customer.'),
        restaurantId: z.string().uuid().optional().describe('Restaurant scope. Omit for system-wide tokens.'),
        userId: z.string().uuid().optional().describe('Subject (user id). Defaults to a fresh UUID when omitted.'),
        ttlSeconds: z
          .number()
          .int()
          .positive()
          .max(24 * 60 * 60)
          .default(3600)
          .describe('Token lifetime in seconds. Default 3600 (1 h). Max 24 h.'),
      },
    },
    async (args) => {
      const { role, restaurantId, userId, ttlSeconds } = args as {
        role: Role;
        restaurantId?: string;
        userId?: string;
        ttlSeconds: number;
      };

      const nowSeconds = Math.floor(Date.now() / 1000);
      const claims: Omit<TokenClaims, 'iat' | 'exp'> = {
        sub: userId ?? randomUUID(),
        role,
        iss: ISSUER,
        aud: AUDIENCE,
        ...(restaurantId !== undefined ? { restaurantId } : {}),
      };

      const token = jwt.sign(claims, getSecret('JWT_SECRET'), {
        algorithm: 'HS256',
        expiresIn: ttlSeconds,
        // Note: `iss` and `aud` are already in the claims payload above,
        // so we don't pass `issuer` / `audience` here. jsonwebtoken
        // rejects having both.
      });

      deps.logger.info({ role, restaurantId, ttlSeconds, sub: claims.sub }, 'generated dev token');

      return {
        content: [
          {
            type: 'text' as const,
            text: JSON.stringify(
              {
                token,
                claims: { ...claims, iat: nowSeconds, exp: nowSeconds + ttlSeconds },
                algorithm: 'HS256',
                issuer: ISSUER,
                audience: AUDIENCE,
              },
              null,
              2,
            ),
          },
        ],
      };
    },
  );

  // ── verify_token ──────────────────────────────────────────────────────

  server.registerTool(
    'verify_token',
    {
      title: 'Verify dev JWT',
      description:
        'Decodes and validates a JWT without making any API call. ' +
        'Checks signature (HS256 with JWT_SECRET), issuer, audience, and expiry. ' +
        'Returns the decoded claims or a descriptive error.',
      inputSchema: {
        token: z.string().min(1).describe('JWT to decode and verify.'),
      },
    },
    async (args) => {
      const { token } = args as { token: string };

      const cacheKey = tokenHash(token);
      const cached = verifyCache.get(cacheKey);
      if (cached !== undefined) {
        return {
          content: [
            { type: 'text' as const, text: JSON.stringify({ valid: true, claims: cached, cached: true }, null, 2) },
          ],
        };
      }

      try {
        const decoded = jwt.verify(token, getSecret('JWT_SECRET'), {
          algorithms: ['HS256'],
          issuer: ISSUER,
          audience: AUDIENCE,
        }) as TokenClaims;

        verifyCache.set(cacheKey, decoded);
        return {
          content: [
            { type: 'text' as const, text: JSON.stringify({ valid: true, claims: decoded, cached: false }, null, 2) },
          ],
        };
      } catch (err) {
        const message = err instanceof Error ? err.message : String(err);
        return {
          content: [{ type: 'text' as const, text: JSON.stringify({ valid: false, error: message }, null, 2) }],
          isError: true,
        };
      }
    },
  );
}
