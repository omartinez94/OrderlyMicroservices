/**
 * Shared types for tool modules.
 *
 * Each tool file exports a `register(ctx)` function that wires one or
 * more tools onto the McpServer. The `ToolContext` carries every
 * resource the tools need (DB pools, redis, rabbit, env, logger, services).
 */

import type pg from 'pg';
import type sql from 'mssql';
import type { Redis } from 'ioredis';
import type { McpServer } from '@modelcontextprotocol/sdk/server/mcp.js';

import type { Logger } from '../logger.ts';
import type { PostgresService } from '../db/postgres-client.ts';
import type { RabbitHandle } from '../db/rabbitmq-client.ts';

export type Role = 'Admin' | 'Manager' | 'Staff' | 'Customer';
export const ROLES = ['Admin', 'Manager', 'Staff', 'Customer'] as const;

export interface ToolContext {
  /** Pino logger instance. */
  logger: Logger;
  /** Per-service pg pools. */
  pg: Record<PostgresService, pg.Pool>;
  /** Connected MSSQL pool for OrderDb. */
  mssql: sql.ConnectionPool;
  /** Connected ioredis client. */
  redis: Redis;
  /** amqplib connection + channel. */
  rabbit: RabbitHandle;
}

export type ToolRegistrar = (server: McpServer, ctx: ToolContext) => void;
