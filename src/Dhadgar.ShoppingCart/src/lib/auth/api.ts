import { authClient } from './client';
import { tokenStorage } from './storage';

const GATEWAY_URL = import.meta.env.PUBLIC_GATEWAY_URL || 'http://localhost:5000';

export interface ApiResponse<T> {
  data?: T;
  error?: string;
  status: number;
}

export interface ApiRequestOptions extends RequestInit {
  requireAuth?: boolean;
}

/**
 * API client with automatic JWT injection and token refresh
 */
export async function apiClient<T>(
  path: string,
  options: ApiRequestOptions = {}
): Promise<ApiResponse<T>> {
  const { requireAuth = true, ...fetchOptions } = options;

  const headers = new Headers(fetchOptions.headers);
  headers.set('Content-Type', 'application/json');

  if (requireAuth) {
    const accessToken = await authClient.getAccessToken();
    if (!accessToken) {
      return { error: 'Not authenticated', status: 401 };
    }
    headers.set('Authorization', `Bearer ${accessToken}`);
  }

  try {
    const response = await fetch(`${GATEWAY_URL}${path}`, {
      ...fetchOptions,
      headers,
    });

    // Handle 401 - try to refresh and retry once
    if (response.status === 401 && requireAuth) {
      const refreshed = await authClient.refreshTokens();
      if (refreshed) {
        headers.set('Authorization', `Bearer ${refreshed.accessToken}`);
        const retryResponse = await fetch(`${GATEWAY_URL}${path}`, {
          ...fetchOptions,
          headers,
        });
        return handleResponse<T>(retryResponse);
      }
      tokenStorage.clear();
      return { error: 'Session expired', status: 401 };
    }

    return handleResponse<T>(response);
  } catch (error) {
    console.error('API request error:', error);
    return {
      error: error instanceof Error ? error.message : 'Network error',
      status: 0,
    };
  }
}

async function handleResponse<T>(response: Response): Promise<ApiResponse<T>> {
  const status = response.status;

  if (!response.ok) {
    let error = 'Request failed';
    try {
      const errorData = await response.json();
      error = errorData.detail || errorData.message || errorData.error || error;
    } catch {
      // Response wasn't JSON
    }
    return { error, status };
  }

  // Handle 204 No Content
  if (status === 204) {
    return { data: undefined as T, status };
  }

  try {
    const data = await response.json();
    return { data, status };
  } catch {
    return { data: undefined as T, status };
  }
}

// Identity API types
export interface AuthProvider {
  provider: string;
  displayName: string | null;
}

export interface UserProfile {
  id: string;
  email: string;
  displayName: string | null;
  emailVerified: boolean;
  preferredOrganizationId: string | null;
  hasPasskeysRegistered: boolean;
  createdAt: string;
  lastAuthenticatedAt: string;
  authProviders: AuthProvider[];
}

export interface Organization {
  id: string;
  name: string;
  slug: string;
  role: string;
  joinedAt: string;
  isPreferred: boolean;
}

export interface LinkedAccount {
  id: string;
  provider: string;
  providerDisplayName: string | null;
  linkedAt: string;
  lastUsedAt: string | null;
}

// Convenience methods
export const api = {
  get<T>(path: string, options?: ApiRequestOptions) {
    return apiClient<T>(path, { ...options, method: 'GET' });
  },

  post<T>(path: string, body?: unknown, options?: ApiRequestOptions) {
    return apiClient<T>(path, {
      ...options,
      method: 'POST',
      body: body ? JSON.stringify(body) : undefined,
    });
  },

  put<T>(path: string, body?: unknown, options?: ApiRequestOptions) {
    return apiClient<T>(path, {
      ...options,
      method: 'PUT',
      body: body ? JSON.stringify(body) : undefined,
    });
  },

  patch<T>(path: string, body?: unknown, options?: ApiRequestOptions) {
    return apiClient<T>(path, {
      ...options,
      method: 'PATCH',
      body: body ? JSON.stringify(body) : undefined,
    });
  },

  delete<T>(path: string, options?: ApiRequestOptions) {
    return apiClient<T>(path, { ...options, method: 'DELETE' });
  },
};

// Identity API helpers
export const identityApi = {
  async getProfile(): Promise<UserProfile | null> {
    const result = await api.get<UserProfile>('/api/v1/identity/me');
    return result.data || null;
  },

  async getOrganizations(): Promise<Organization[]> {
    const result = await api.get<{ organizations: Organization[] }>('/api/v1/identity/me/organizations');
    return result.data?.organizations || [];
  },

  async getLinkedAccounts(): Promise<LinkedAccount[]> {
    const result = await api.get<{ linkedAccounts: LinkedAccount[] }>('/api/v1/identity/me/linked-accounts');
    return result.data?.linkedAccounts || [];
  },

  async getPermissions(): Promise<string[]> {
    const result = await api.get<{ permissions: string[] }>('/api/v1/identity/me/permissions');
    return result.data?.permissions || [];
  },
};
