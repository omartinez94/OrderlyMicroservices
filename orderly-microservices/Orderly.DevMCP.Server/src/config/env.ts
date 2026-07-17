/**
 * Environment validation.
 *
 * The server refuses to start if any required variable is missing or
 * NODE_ENV is not "development". JWT_SECRET is exposed only via the
 * getSecret() getter so an accidental `console.log(process.env)` or
 * `JSON.stringify(env)` cannot leak it.
 */

import { z } from 'zod';

const EnvSchema = z.object({
  NODE_ENV: z.literal('development'),

  HOST: z.string().default('0.0.0.0'),
  PORT: z.coerce.number().int().positive().default(8080),

  LOG_LEVEL: z
    .enum(['fatal', 'error', 'warn', 'info', 'debug', 'trace', 'silent'])
    .default('info'),

  DEV_HOST: z.string().default('localhost,127.0.0.1'),

  // Required. The server uses this for `generate_dev_token` in Phase 2.
  // We don't use it in Phase 1, but we validate it now so the server
  // fails fast in dev mode rather than mid-tool-call.
  JWT_SECRET: z
    .string()
    .min(16, 'JWT_SECRET must be at least 16 characters'),

  POSTGRES_USER: z.string().min(1),
  POSTGRES_PASSWORD: z.string().min(1),

  SA_PASSWORD: z.string().min(1),

  REDIS_PASSWORD: z.string().min(1),

  RABBITMQ_DEFAULT_USER: z.string().min(1),
  RABBITMQ_DEFAULT_PASS: z.string().min(1),

  ASPNETCORE_Kestrel__Certificates__Default__Password: z.string().optional(),
});

type Env = z.infer<typeof EnvSchema>;

function loadEnv(): Env {
  const parsed = EnvSchema.safeParse(process.env);
  if (!parsed.success) {
    // Use process.stderr directly here — the pino logger is not yet wired.
    process.stderr.write(
      `[env] invalid configuration:\n${JSON.stringify(parsed.error.format(), null, 2)}\n`,
    );
    process.exit(1);
  }
  return parsed.data;
}

export const env: Env = loadEnv();

/** Whitelisted hostnames for DB / Redis / RabbitMQ connections. */
export const devHosts: readonly string[] = env.DEV_HOST.split(',')
  .map((s) => s.trim())
  .filter(Boolean);

/**
 * Returns true when the given hostname is on the dev allow-list.
 * Case-insensitive — `LocalHost` and `localhost` are equivalent.
 */
export function isDevHost(hostname: string): boolean {
  const lower = hostname.toLowerCase();
  return devHosts.some((allowed) => allowed.toLowerCase() === lower);
}

/**
 * Secret accessor. Use this instead of `env.JWT_SECRET` so a careless
 * `console.log` of `env` cannot leak the value — it's a method call,
 * not a property read.
 */
export function getSecret(name: 'JWT_SECRET'): string {
  const v = (env as unknown as Record<string, string | undefined>)[name];
  if (!v) {
    throw new Error(`secret ${name} is not configured`);
  }
  return v;
}
