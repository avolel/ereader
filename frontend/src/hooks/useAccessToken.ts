import { useEffect, useState } from 'react';

import tokenStorage from '../services/tokenStorage';

// Reads the current access token from storage for callers that need it inline
// rather than through the axios interceptor — specifically native code that
// builds authenticated media URLs (cover <Image>, in-chapter WebView assets),
// where a subresource request can't carry an Authorization header.
//
// Pass `refreshKey` to re-read when something upstream changes (e.g. a chapter
// re-render) so a freshly-rotated token is picked up. The token is short-lived
// (15 min); this hook does not itself refresh — see the axios interceptor for
// the refresh flow. Returns null until the first read resolves and on failure.
export function useAccessToken(refreshKey?: unknown): string | null {
  const [token, setToken] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    void tokenStorage
      .get()
      .then((stored) => {
        if (!cancelled) setToken(stored?.accessToken ?? null);
      })
      .catch(() => {
        if (!cancelled) setToken(null);
      });
    return () => {
      cancelled = true;
    };
  }, [refreshKey]);

  return token;
}
