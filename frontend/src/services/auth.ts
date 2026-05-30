import { AuthTokenResponse } from '../types';
import { api } from './api';
import tokenStorage, { StoredTokens } from './tokenStorage';

function persist(response: AuthTokenResponse): StoredTokens {
  const tokens: StoredTokens = {
    accessToken: response.accessToken,
    refreshToken: response.refreshToken,
    accessExpiresAt: response.accessExpiresAt,
    refreshExpiresAt: response.refreshExpiresAt,
  };
  // Fire-and-forget: storage writes shouldn't gate the caller's UI update.
  // If the write fails, the next session start will just see them missing.
  void tokenStorage.set(tokens);
  return tokens;
}

export async function register(username: string, password: string): Promise<AuthTokenResponse> {
  const { data } = await api.post<AuthTokenResponse>('/api/v1/auth/register', {
    username,
    password,
  });
  persist(data);
  return data;
}

export async function login(username: string, password: string): Promise<AuthTokenResponse> {
  const { data } = await api.post<AuthTokenResponse>('/api/v1/auth/login', {
    username,
    password,
  });
  persist(data);
  return data;
}

export async function logout(): Promise<void> {
  const stored = await tokenStorage.get();
  if (stored?.refreshToken) {
    try {
      await api.post('/api/v1/auth/logout', { refreshToken: stored.refreshToken });
    } catch {
      // If the server rejects the refresh token, we still want to clear local
      // state — the user's intent is unambiguous.
    }
  }
  await tokenStorage.clear();
}