/**
 * amqplib connection factory for the messagebroker (port 5672).
 *
 * Phase 3's `publish_integration_event` will reuse the channel exposed
 * here. The MassTransit exchange naming convention
 * (`BuildingBlocks.Messaging.Events:{EventTypeName}`) is enforced at
 * that layer — this file only opens the transport.
 */

import amqp, { type ChannelModel, type Channel } from 'amqplib';
import { env, isDevHost } from '../config/env.ts';
import { ConnectionError, HostViolationError } from '../errors/DevMCPError.ts';

export interface CreateRabbitOptions {
  /** Defaults to 'localhost'. */
  host?: string;
  /** AMQP port (default 5672). */
  port?: number;
}

export interface RabbitHandle {
  connection: ChannelModel;
  channel: Channel;
  /** Closes the channel + connection. Idempotent. */
  close: () => Promise<void>;
}

export async function createRabbit(opts: CreateRabbitOptions = {}): Promise<RabbitHandle> {
  const host = opts.host ?? 'localhost';
  if (!isDevHost(host)) {
    throw new HostViolationError(host, [host]);
  }

  const port = opts.port ?? 5672;

  let connection: ChannelModel;
  try {
    connection = await amqp.connect({
      hostname: host,
      port,
      username: env.RABBITMQ_DEFAULT_USER,
      password: env.RABBITMQ_DEFAULT_PASS,
    });
  } catch (cause) {
    throw new ConnectionError('amqp connect failed', { cause, context: { host, port } });
  }

  let channel: Channel;
  try {
    channel = await connection.createChannel();
  } catch (cause) {
    await connection.close().catch(() => undefined);
    throw new ConnectionError('amqp createChannel failed', { cause });
  }

  let closed = false;
  const close = async (): Promise<void> => {
    if (closed) return;
    closed = true;
    try { await channel.close(); } catch { /* swallow — connection may already be down */ }
    try { await connection.close(); } catch { /* same */ }
  };

  // Surface unexpected connection drops so the operator notices.
  connection.on('error', (err: Error) => {
    // eslint-disable-next-line no-console
    console.error('[rabbitmq] connection error:', err.message);
  });

  return { connection, channel, close };
}

/**
 * Cheap health check — asserts the channel is open. amqplib does not
 * expose a `ping` like Redis, so we just check `channel` is truthy
 * and the connection has not yet emitted `close`.
 */
export async function pingRabbit(handle: RabbitHandle): Promise<void> {
  if (!handle.channel) {
    throw new ConnectionError('rabbitmq channel not initialised');
  }
  if ((handle.connection as unknown as { connection?: { stream?: { destroyed?: boolean } } }).connection?.stream?.destroyed) {
    throw new ConnectionError('rabbitmq underlying socket destroyed');
  }
}
