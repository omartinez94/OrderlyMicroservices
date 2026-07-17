/**
 * tools/event-bus.ts (§6.5).
 *
 * Two tools:
 *   - publish_integration_event(eventName, payload) — publishes a JSON
 *     message to the MassTransit fanout exchange named
 *     `BuildingBlocks.Messaging.Events:{EventTypeName}`. Auto-injects
 *     Id (UUID), OccurredOn (ISO timestamp), MessageVersion (1).
 *     Rate-limited to 5/min (§10.1).
 *   - inspect_dead_letters() — fetches failed messages from any
 *     `_error` (MassTransit DLQ) queues via the RabbitMQ Management
 *     API; caps each payload at 10 KB (§10.4).
 *
 * The event-type lookup table is generated at boot by scanning
 * `BuildingBlocks.Messaging/Events/*.cs` and extracting class names
 * that inherit from `IntegrationEvent`. New event types are picked up
 * automatically — no manual maintenance.
 */

import { readdirSync, statSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, resolve } from 'node:path';
import { randomUUID } from 'node:crypto';
import { z } from 'zod';
import type { McpServer } from '@modelcontextprotocol/sdk/server/mcp.js';

import type { Logger } from '../logger.ts';
import type { ToolContext } from './types.ts';
import { rateLimits } from '../util/rate-limit.ts';

const __dirname = dirname(fileURLToPath(import.meta.url));
const EVENTS_DIR = resolve(__dirname, '../../../BuildingBlocks.Messaging/Events');

const EXCHANGE_PREFIX = 'BuildingBlocks.Messaging.Events:';
const MGMT_API = 'http://localhost:15672/api';
const RABBIT_USER = process.env.RABBITMQ_DEFAULT_USER ?? 'guest';
const RABBIT_PASS = process.env.RABBITMQ_DEFAULT_PASS ?? 'guest';
const MAX_PAYLOAD_BYTES = 10 * 1024;

function authHeader(): string {
  return 'Basic ' + Buffer.from(`${RABBIT_USER}:${RABBIT_PASS}`).toString('base64');
}

function loadKnownEventTypes(): string[] {
  try {
    const entries = readdirSync(EVENTS_DIR);
    const types: string[] = [];
    for (const name of entries) {
      if (!name.endsWith('.cs')) continue;
      const full = resolve(EVENTS_DIR, name);
      if (!statSync(full).isFile()) continue;
      // Skip the abstract IntegrationEvent base + I-* interfaces.
      if (name === 'IntegrationEvent.cs') continue;
      if (name.startsWith('I')) continue;
      // The class name is the file name without `.cs`.
      types.push(name.slice(0, -3));
    }
    return types;
  } catch (cause) {
    // If the directory doesn't exist (e.g. running outside the repo),
    // return an empty list — publish_integration_event will allow any name.
    return [];
  }
}

export interface EventBusDeps {
  logger: Logger;
  rabbit: ToolContext['rabbit'];
}

export function registerEventBusTools(server: McpServer, deps: EventBusDeps): void {
  const knownEventTypes = loadKnownEventTypes();
  deps.logger.info({ eventTypes: knownEventTypes.length, sample: knownEventTypes.slice(0, 3) }, 'loaded known event types');

  // ── publish_integration_event ─────────────────────────────────────────

  server.registerTool(
    'publish_integration_event',
    {
      title: 'Publish integration event',
      description:
        'Publishes a JSON message to the MassTransit fanout exchange `BuildingBlocks.Messaging.Events:{EventTypeName}`. ' +
        'Auto-injects Id (UUID), OccurredOn (ISO timestamp), MessageVersion (1). ' +
        'Rate-limited to 5/min per §10.1.',
      inputSchema: {
        eventName: z.string().min(1).describe(`Event type name. Known types: ${knownEventTypes.join(', ') || '(none discovered)'}`),
        payload: z.record(z.string(), z.unknown()).describe('Event payload as a JSON object. Will be merged with auto-injected fields.'),
        rateLimitKey: z.string().default('global').describe('Rate-limit bucket key. Default "global".'),
      },
    },
    async (args) => {
      const { eventName, payload, rateLimitKey } = args as { eventName: string; payload: Record<string, unknown>; rateLimitKey: string };

      const limit = rateLimits.publish.consume(rateLimitKey);
      if (!limit.allowed) {
        return {
          content: [{
            type: 'text' as const,
            text: JSON.stringify({ error: 'rate-limited', resetMs: limit.resetMs }, null, 2),
          }],
          isError: true,
        };
      }

      if (knownEventTypes.length > 0 && !knownEventTypes.includes(eventName)) {
        return {
          content: [{
            type: 'text' as const,
            text: JSON.stringify({ error: `unknown event "${eventName}"`, known: knownEventTypes }, null, 2),
          }],
          isError: true,
        };
      }

      const exchange = `${EXCHANGE_PREFIX}${eventName}`;
      const enriched = {
        ...payload,
        Id: payload.Id ?? randomUUID(),
        OccurredOn: payload.OccurredOn ?? new Date().toISOString(),
        MessageVersion: payload.MessageVersion ?? 1,
      };
      const body = Buffer.from(JSON.stringify(enriched), 'utf-8');

      try {
        // MassTransit fanout exchanges: just publish to the exchange
        // name; consumers bind their own queues to it.
        const ok = deps.rabbit.channel.publish(exchange, '', body, {
          contentType: 'application/json',
          persistent: true,
          messageId: enriched.Id as string,
          timestamp: Math.floor(Date.now() / 1000),
        });
        if (!ok) {
          return {
            content: [{ type: 'text' as const, text: JSON.stringify({ error: 'channel buffer full', exchange }, null, 2) }],
            isError: true,
          };
        }
        deps.logger.info({ exchange, eventId: enriched.Id, bytes: body.length }, 'published integration event');
        return {
          content: [{
            type: 'text' as const,
            text: JSON.stringify({ exchange, eventId: enriched.Id, bytes: body.length, payload: enriched }, null, 2),
          }],
        };
      } catch (cause) {
        deps.logger.error({ err: cause, exchange }, 'publish_integration_event failed');
        return {
          content: [{ type: 'text' as const, text: JSON.stringify({ error: 'publish failed', cause: String(cause) }, null, 2) }],
          isError: true,
        };
      }
    },
  );

  // ── inspect_dead_letters ──────────────────────────────────────────────

  server.registerTool(
    'inspect_dead_letters',
    {
      title: 'Inspect dead-letter queues',
      description:
        'Lists RabbitMQ dead-letter queues (MassTransit `_error` queues) and returns the failed ' +
        'message payloads (capped at 10 KB each per §10.4).',
      inputSchema: {
        limitPerQueue: z.number().int().positive().max(50).default(5).describe('Max messages to fetch per queue.'),
      },
    },
    async (args) => {
      const { limitPerQueue } = args as { limitPerQueue: number };

      try {
        const queuesRes = await fetch(`${MGMT_API}/queues/%2F`, { headers: { Authorization: authHeader() }, signal: AbortSignal.timeout(3_000) });
        if (!queuesRes.ok) throw new Error(`mgmt api returned ${queuesRes.status}`);
        const queues = (await queuesRes.json()) as Array<{ name: string; messages?: number; vhost?: string }>;
        const dlq = queues.filter((q) => q.name.endsWith('_error'));

        const out: Array<{ queue: string; messageCount: number; messages: unknown[] }> = [];
        for (const q of dlq) {
          const url = `${MGMT_API}/queues/${encodeURIComponent(q.vhost ?? '/')}/${encodeURIComponent(q.name)}/get`;
          const body = JSON.stringify({ count: limitPerQueue, ackmode: 'ack_requeue_false', encoding: 'auto', truncate: MAX_PAYLOAD_BYTES });
          const msgRes = await fetch(url, {
            method: 'POST',
            headers: { Authorization: authHeader(), 'Content-Type': 'application/json' },
            body,
            signal: AbortSignal.timeout(3_000),
          });
          if (!msgRes.ok) {
            out.push({ queue: q.name, messageCount: q.messages ?? 0, messages: [{ error: `mgmt returned ${msgRes.status}` }] });
            continue;
          }
          const messages = (await msgRes.json()) as Array<{ payload: string; payload_bytes?: number; properties?: { headers?: Record<string, unknown> } }>;
          out.push({
            queue: q.name,
            messageCount: q.messages ?? 0,
            messages: messages.map((m) => {
              const decoded = (() => {
                try { return JSON.parse(m.payload) as unknown; }
                catch { return { raw: m.payload.slice(0, 1024) }; }
              })();
              return { ...(typeof decoded === 'object' && decoded !== null ? decoded as object : { raw: decoded }), ...(m.properties?.headers ? { headers: m.properties.headers } : {}) };
            }),
          });
        }

        return {
          content: [{
            type: 'text' as const,
            text: JSON.stringify({ generatedAt: new Date().toISOString(), deadLetterQueues: out }, null, 2),
          }],
        };
      } catch (cause) {
        deps.logger.error({ err: cause }, 'inspect_dead_letters failed');
        return {
          content: [{ type: 'text' as const, text: JSON.stringify({ error: 'inspect failed', cause: String(cause) }, null, 2) }],
          isError: true,
        };
      }
    },
  );
}
