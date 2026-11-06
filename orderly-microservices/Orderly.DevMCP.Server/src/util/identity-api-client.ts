/**
 * util/identity-api-client.ts
 *
 * HTTP client for Identity.API.
 * Since Identity.API enforces OpenIddict local validation, this client authenticates
 * via `POST /connect/token` using the seeded development credentials
 * (`admin@orderly.com` / `Admin@123456`) and caches the resulting bearer token.
 */

import { serviceUrl } from '../config/services.ts';
import type { Logger } from '../logger.ts';

interface TokenCache {
  token: string;
  expiresAt: number; // unix timestamp ms
}

let cachedToken: TokenCache | null = null;

export interface CreateUserPayload {
  email: string;
  password?: string;
  firstName: string;
  lastName: string;
  phoneNumber?: string;
  roles?: string[];
  restaurantIds?: string[];
  defaultRestaurantId?: string;
}

export class IdentityApiClient {
  private readonly baseUrl: string;
  private readonly logger: Logger;

  constructor(logger: Logger) {
    this.baseUrl = serviceUrl('identity');
    this.logger = logger;
  }

  /**
   * Acquires a valid OpenIddict access token using the dev SuperAdmin credentials.
   * Caches the token in memory until 60 seconds before expiration.
   */
  async getAdminToken(): Promise<string> {
    const now = Date.now();
    if (cachedToken && cachedToken.expiresAt > now + 60_000) {
      return cachedToken.token;
    }

    const tokenUrl = `${this.baseUrl}/connect/token`;
    const form = new URLSearchParams({
      grant_type: 'password',
      username: 'admin@orderly.com',
      password: 'Admin@123456',
      scope: 'openid email profile offline_access',
    });

    try {
      const res = await fetch(tokenUrl, {
        method: 'POST',
        headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
        body: form.toString(),
      });

      if (!res.ok) {
        const text = await res.text();
        this.logger.error({ status: res.status, body: text }, 'failed to acquire OpenIddict token from Identity.API');
        throw new Error(`Identity login failed with status ${res.status}: ${text}`);
      }

      const json = (await res.json()) as { access_token?: string; accessToken?: string; expires_in?: number };
      const token = json.access_token || json.accessToken;

      if (!token) {
        throw new Error('Identity.API did not return an access token');
      }

      const expiresIn = json.expires_in ?? 900;
      cachedToken = {
        token,
        expiresAt: now + expiresIn * 1000,
      };

      this.logger.debug({ expiresIn }, 'acquired fresh OpenIddict admin token');
      return token;
    } catch (err) {
      this.logger.error({ err, tokenUrl }, 'network error contacting Identity.API token endpoint');
      throw err;
    }
  }

  private async authFetch(path: string, options: RequestInit = {}): Promise<Response> {
    const token = await this.getAdminToken();
    const headers = new Headers(options.headers);
    headers.set('Authorization', `Bearer ${token}`);
    headers.set('Accept', 'application/json');
    if (options.body && typeof options.body === 'string' && !headers.has('Content-Type')) {
      headers.set('Content-Type', 'application/json');
    }

    const url = `${this.baseUrl}${path}`;
    return fetch(url, { ...options, headers });
  }

  async createUser(payload: CreateUserPayload): Promise<{ userId: string; email: string }> {
    const res = await this.authFetch('/v1/users', {
      method: 'POST',
      body: JSON.stringify({
        email: payload.email,
        password: payload.password ?? 'Password123!',
        firstName: payload.firstName,
        lastName: payload.lastName,
        phoneNumber: payload.phoneNumber ?? '+52-81-0000-0000',
        roles: payload.roles ?? [],
        restaurantIds: payload.restaurantIds ?? [],
        defaultRestaurantId: payload.defaultRestaurantId ?? null,
      }),
    });

    if (!res.ok) {
      const errText = await res.text();
      throw new Error(`Create user failed (${res.status}): ${errText}`);
    }

    return (await res.json()) as { userId: string; email: string };
  }

  async assignRoles(userId: string, roles: string[]): Promise<void> {
    const res = await this.authFetch(`/v1/users/${userId}/roles`, {
      method: 'POST',
      body: JSON.stringify({ userId, roles }),
    });

    if (!res.ok) {
      const errText = await res.text();
      throw new Error(`Assign roles failed (${res.status}): ${errText}`);
    }
  }

  async assignRestaurants(userId: string, restaurants: Array<{ restaurantId: string; isDefault: boolean }>): Promise<void> {
    const res = await this.authFetch(`/v1/users/${userId}/restaurants`, {
      method: 'POST',
      body: JSON.stringify({ userId, restaurants }),
    });

    if (!res.ok) {
      const errText = await res.text();
      throw new Error(`Assign restaurants failed (${res.status}): ${errText}`);
    }
  }

  async deleteUser(userId: string): Promise<void> {
    const res = await this.authFetch(`/v1/users/${userId}`, {
      method: 'DELETE',
    });

    if (!res.ok && res.status !== 404) {
      const errText = await res.text();
      throw new Error(`Delete user failed (${res.status}): ${errText}`);
    }
  }
}
