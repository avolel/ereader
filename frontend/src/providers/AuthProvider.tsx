import { useQueryClient } from '@tanstack/react-query';
import {
  ReactNode,
  createContext,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useState,
} from 'react';

import { onUnauthorized } from '../services/api';
import * as authService from '../services/auth';
import tokenStorage from '../services/tokenStorage';
import { UserProfile } from '../types';

type AuthStatus = 'loading' | 'authed' | 'unauthed';

type AuthContextValue = {
  status: AuthStatus;
  user: UserProfile | null;
  login: (username: string, password: string) => Promise<void>;
  register: (username: string, password: string) => Promise<void>;
  logout: () => Promise<void>;
};

const AuthContext = createContext<AuthContextValue | null>(null);

type Props = { children: ReactNode };

export default function AuthProvider({ children }: Props) {
  const [status, setStatus] = useState<AuthStatus>('loading');
  const [user, setUser] = useState<UserProfile | null>(null);
  const queryClient = useQueryClient();

  // Bootstrap from storage. If a refresh token exists we optimistically treat
  // the session as authed; the first API call will refresh if the access token
  // has expired. We don't have a "current user" endpoint hit here on purpose —
  // it adds an extra round trip on every cold start.
  useEffect(() => {
    let cancelled = false;
    (async () => {
      const stored = await tokenStorage.get();
      if (cancelled) return;
      if (stored?.refreshToken) {
        // We don't have the User object cached. Phase 3 work: add /users/me
        // call here and stash it. For now an authed session with null user is
        // fine — screens that need the username can request it lazily.
        setStatus('authed');
      } else {
        setStatus('unauthed');
      }
    })();
    return () => {
      cancelled = true;
    };
  }, []);

  // When axios's refresh path gives up, drop client-side session state.
  useEffect(() => {
    return onUnauthorized(() => {
      setUser(null);
      setStatus('unauthed');
      queryClient.clear();
    });
  }, [queryClient]);

  const login = useCallback(
    async (username: string, password: string) => {
      const tokens = await authService.login(username, password);
      setUser(tokens.user);
      setStatus('authed');
    },
    [],
  );

  const register = useCallback(
    async (username: string, password: string) => {
      const tokens = await authService.register(username, password);
      setUser(tokens.user);
      setStatus('authed');
    },
    [],
  );

  const logout = useCallback(async () => {
    await authService.logout();
    setUser(null);
    setStatus('unauthed');
    queryClient.clear();
  }, [queryClient]);

  const value = useMemo<AuthContextValue>(
    () => ({ status, user, login, register, logout }),
    [status, user, login, register, logout],
  );

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth(): AuthContextValue {
  const ctx = useContext(AuthContext);
  if (!ctx) throw new Error('useAuth must be used inside AuthProvider.');
  return ctx;
}