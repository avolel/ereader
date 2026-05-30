import axios, {
  AxiosError,
  AxiosHeaders,
  InternalAxiosRequestConfig,
} from 'axios';

import tokenStorage, { StoredTokens } from './tokenStorage';

const BASE_URL = process.env.EXPO_PUBLIC_API_URL ?? 'http://localhost:5214';

export const api = axios.create({
  baseURL: BASE_URL,
  timeout: 15_000,
});

// Subscribers notified when a session ends (refresh failed or no refresh token).
// AuthProvider registers itself so it can navigate the user to /login.
type UnauthorizedListener = () => void;
const unauthorizedListeners = new Set<UnauthorizedListener>();

export function onUnauthorized(listener: UnauthorizedListener): () => void {
  unauthorizedListeners.add(listener);
  return () => unauthorizedListeners.delete(listener);
}

function notifyUnauthorized() {
  for (const listener of unauthorizedListeners) listener();
}

// Single-flight refresh: if multiple requests 401 concurrently, they all await
// the same refresh promise rather than racing. Family-session refresh tokens
// rotate on every use, so parallel refreshes would invalidate each other.
let refreshInFlight: Promise<StoredTokens | null> | null = null;

async function performRefresh(): Promise<StoredTokens | null> {
  const current = await tokenStorage.get();
  if (!current?.refreshToken) return null;

  try {
    // Bypass the interceptor to avoid recursion on this exact call.
    const response = await axios.post<StoredTokens>(
      `${BASE_URL}/api/v1/auth/refresh`,
      { refreshToken: current.refreshToken },
      { timeout: 15_000 },
    );
    const next: StoredTokens = {
      accessToken: response.data.accessToken,
      refreshToken: response.data.refreshToken,
      accessExpiresAt: response.data.accessExpiresAt,
      refreshExpiresAt: response.data.refreshExpiresAt,
    };
    await tokenStorage.set(next);
    return next;
  } catch {
    await tokenStorage.clear();
    return null;
  }
}

function getRefreshPromise(): Promise<StoredTokens | null> {
  if (!refreshInFlight) {
    refreshInFlight = performRefresh().finally(() => {
      refreshInFlight = null;
    });
  }
  return refreshInFlight;
}

api.interceptors.request.use(async (config: InternalAxiosRequestConfig) => {
  const tokens = await tokenStorage.get();
  if (tokens?.accessToken) {
    const headers = AxiosHeaders.from(config.headers);
    headers.set('Authorization', `Bearer ${tokens.accessToken}`);
    config.headers = headers;
  }
  return config;
});

type RetriableConfig = InternalAxiosRequestConfig & { _retry?: boolean };

api.interceptors.response.use(
  (response) => response,
  async (error: AxiosError) => {
    const original = error.config as RetriableConfig | undefined;
    const status = error.response?.status;

    // Bail conditions: no config to retry, not a 401, already retried once, or
    // the failing request *is* the refresh call.
    const isRefreshCall = original?.url?.includes('/api/v1/auth/refresh');
    if (status !== 401 || !original || original._retry || isRefreshCall) {
      if (status === 401 && isRefreshCall) notifyUnauthorized();
      return Promise.reject(error);
    }

    original._retry = true;
    const next = await getRefreshPromise();
    if (!next) {
      notifyUnauthorized();
      return Promise.reject(error);
    }

    const headers = AxiosHeaders.from(original.headers);
    headers.set('Authorization', `Bearer ${next.accessToken}`);
    original.headers = headers;
    return api.request(original);
  },
);