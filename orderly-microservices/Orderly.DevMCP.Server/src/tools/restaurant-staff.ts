/**
 * tools/restaurant-staff.ts
 *
 * Staff provisioning and multitenant restaurant binding bridge:
 *   - manage_restaurant_staff(action, restaurantId, ...)
 * Supports:
 *   - list_staff: lists users assigned to the restaurant with their roles
 *   - create_staff: provisions a new staff user via Identity.API with proper PBKDF2 hash
 *   - assign_role: assigns or modifies roles for a user
 *   - link_restaurant: maps user to restaurant in UserRestaurants
 *   - remove_staff: unlinks user from restaurant (or deletes account)
 */

import { z } from 'zod';
import type { McpServer } from '@modelcontextprotocol/sdk/server/mcp.js';

import type { Logger } from '../logger.ts';
import type { ToolContext } from './types.ts';
import { IdentityApiClient } from '../util/identity-api-client.ts';
import { buildToolErrorPayload } from './data-seeding.ts';

export const STAFF_ROLES = [
  'SuperAdmin',
  'RestaurantAdmin',
  'Manager',
  'KitchenManager',
  'Waiter',
  'KitchenStaff',
  'Host',
  'Cashier',
] as const;

export interface RestaurantStaffDeps {
  logger: Logger;
  pg: ToolContext['pg'];
}

export function registerRestaurantStaffTools(server: McpServer, deps: RestaurantStaffDeps): void {
  const apiClient = new IdentityApiClient(deps.logger);

  server.registerTool(
    'manage_restaurant_staff',
    {
      title: 'Manage restaurant staff accounts and role assignments',
      description:
        'Provisions staff users, assigns roles, and manages restaurant tenant linkages via the Identity.API bridge. ' +
        'Supported actions: "list_staff", "create_staff", "assign_role", "link_restaurant", "remove_staff". ' +
        'Roles: SuperAdmin, RestaurantAdmin, Manager, KitchenManager, Waiter, KitchenStaff, Host, Cashier.',
      inputSchema: {
        restaurantId: z.string().describe('Restaurant UUID or ShortId'),
        action: z.enum(['list_staff', 'create_staff', 'assign_role', 'link_restaurant', 'remove_staff']),
        userId: z.string().uuid().optional().describe('User UUID (required for assign_role, link_restaurant, remove_staff)'),
        email: z.string().email().optional(),
        password: z.string().min(8).default('Password123!').optional(),
        firstName: z.string().min(1).default('Staff').optional(),
        lastName: z.string().min(1).default('User').optional(),
        phoneNumber: z.string().default('+52-81-0000-0000').optional(),
        role: z.enum(STAFF_ROLES).optional().describe('Role to assign to the user'),
        isDefault: z.boolean().default(true).optional().describe('Whether this restaurant is the default tenant for the user'),
        deleteAccount: z.boolean().default(false).optional().describe('If true during remove_staff, completely deletes the user account'),
      },
    },
    async (args) => {
      const input = args as {
        restaurantId: string;
        action: 'list_staff' | 'create_staff' | 'assign_role' | 'link_restaurant' | 'remove_staff';
        userId?: string;
        email?: string;
        password?: string;
        firstName?: string;
        lastName?: string;
        phoneNumber?: string;
        role?: typeof STAFF_ROLES[number];
        isDefault?: boolean;
        deleteAccount?: boolean;
      };

      // Resolve restaurant UUID
      let restaurantUuid = input.restaurantId;
      if (!/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(restaurantUuid)) {
        const rRes = await deps.pg.catalog.query('SELECT "Id" FROM "Restaurants" WHERE "ShortId" = $1', [restaurantUuid]);
        if (rRes.rows.length === 0) {
          return {
            content: [{
              type: 'text' as const,
              text: JSON.stringify({ error: 'restaurant not found', restaurantId: input.restaurantId }, null, 2),
            }],
            isError: true,
          };
        }
        restaurantUuid = (rRes.rows[0] as { Id: string }).Id;
      }

      try {
        // ── Action: list_staff ──────────────────────────────────────────
        if (input.action === 'list_staff') {
          const listSql = `
            SELECT
              u."Id" AS "id",
              u."Email" AS "email",
              u."FirstName" AS "firstName",
              u."LastName" AS "lastName",
              u."PhoneNumber" AS "phoneNumber",
              u."IsActive" AS "isActive",
              ur."IsDefault" AS "isDefault",
              COALESCE(
                (
                  SELECT json_agg(r."Name")
                  FROM "AspNetUserRoles" ur2
                  JOIN "AspNetRoles" r ON r."Id" = ur2."RoleId"
                  WHERE ur2."UserId" = u."Id"
                ),
                '[]'::json
              ) AS "roles"
            FROM "UserRestaurants" ur
            JOIN "AspNetUsers" u ON u."Id" = ur."UserId"
            WHERE ur."RestaurantId" = $1
            ORDER BY u."Email" ASC
          `;

          const listRes = await deps.pg.identity.query(listSql, [restaurantUuid]);

          return {
            content: [{
              type: 'text' as const,
              text: JSON.stringify({
                restaurantId: restaurantUuid,
                staffCount: listRes.rows.length,
                staff: listRes.rows,
              }, null, 2),
            }],
          };
        }

        // ── Action: create_staff ────────────────────────────────────────
        if (input.action === 'create_staff') {
          if (!input.email) {
            return {
              content: [{
                type: 'text' as const,
                text: JSON.stringify({ error: 'email is required for create_staff' }, null, 2),
              }],
              isError: true,
            };
          }

          const rolesToAssign = input.role ? [input.role] : ['Waiter'];
          const password = input.password ?? 'Password123!';

          const result = await apiClient.createUser({
            email: input.email,
            password,
            firstName: input.firstName ?? 'Staff',
            lastName: input.lastName ?? 'User',
            phoneNumber: input.phoneNumber ?? '+52-81-0000-0000',
            roles: rolesToAssign,
            restaurantIds: [restaurantUuid],
            defaultRestaurantId: restaurantUuid,
          });

          deps.logger.info({ userId: result.userId, email: input.email, restaurantId: restaurantUuid }, 'provisioned staff user');

          return {
            content: [{
              type: 'text' as const,
              text: JSON.stringify({
                success: true,
                userId: result.userId,
                email: input.email,
                temporaryPassword: password,
                roles: rolesToAssign,
                restaurantId: restaurantUuid,
                isDefault: true,
              }, null, 2),
            }],
          };
        }

        // ── Action: assign_role ─────────────────────────────────────────
        if (input.action === 'assign_role') {
          if (!input.userId || !input.role) {
            return {
              content: [{
                type: 'text' as const,
                text: JSON.stringify({ error: 'userId and role are required for assign_role' }, null, 2),
              }],
              isError: true,
            };
          }

          await apiClient.assignRoles(input.userId, [input.role]);

          return {
            content: [{
              type: 'text' as const,
              text: JSON.stringify({ success: true, userId: input.userId, role: input.role }, null, 2),
            }],
          };
        }

        // ── Action: link_restaurant ─────────────────────────────────────
        if (input.action === 'link_restaurant') {
          if (!input.userId) {
            return {
              content: [{
                type: 'text' as const,
                text: JSON.stringify({ error: 'userId is required for link_restaurant' }, null, 2),
              }],
              isError: true,
            };
          }

          await apiClient.assignRestaurants(input.userId, [
            { restaurantId: restaurantUuid, isDefault: input.isDefault ?? true },
          ]);

          return {
            content: [{
              type: 'text' as const,
              text: JSON.stringify({
                success: true,
                userId: input.userId,
                restaurantId: restaurantUuid,
                isDefault: input.isDefault ?? true,
              }, null, 2),
            }],
          };
        }

        // ── Action: remove_staff ────────────────────────────────────────
        if (input.action === 'remove_staff') {
          if (!input.userId) {
            return {
              content: [{
                type: 'text' as const,
                text: JSON.stringify({ error: 'userId is required for remove_staff' }, null, 2),
              }],
              isError: true,
            };
          }

          // Unlink from restaurant
          await deps.pg.identity.query(
            'DELETE FROM "UserRestaurants" WHERE "UserId" = $1 AND "RestaurantId" = $2',
            [input.userId, restaurantUuid],
          );

          if (input.deleteAccount) {
            await apiClient.deleteUser(input.userId);
          }

          return {
            content: [{
              type: 'text' as const,
              text: JSON.stringify({
                success: true,
                userId: input.userId,
                unlinkedFromRestaurant: restaurantUuid,
                accountDeleted: input.deleteAccount ?? false,
              }, null, 2),
            }],
          };
        }

        return {
          content: [{
            type: 'text' as const,
            text: JSON.stringify({ error: `unsupported action "${input.action}"` }, null, 2),
          }],
          isError: true,
        };
      } catch (cause) {
        deps.logger.error({ err: cause, restaurantId: input.restaurantId, action: input.action }, 'manage_restaurant_staff failed');
        return {
          content: [{
            type: 'text' as const,
            text: JSON.stringify(buildToolErrorPayload('manage_restaurant_staff failed', cause), null, 2),
          }],
          isError: true,
        };
      }
    },
  );
}
