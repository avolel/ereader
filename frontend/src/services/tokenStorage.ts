// Default export resolved by Metro's platform extensions (tokenStorage.web.ts /
// tokenStorage.native.ts). This file is the type contract and the fallback for
// any environment that doesn't match a platform suffix.

export type StoredTokens = {
  accessToken: string;
  refreshToken: string;
  accessExpiresAt: string;
  refreshExpiresAt: string;
};

export interface TokenStorage {
  get(): Promise<StoredTokens | null>;
  set(tokens: StoredTokens): Promise<void>;
  clear(): Promise<void>;
}

// Fallback (should never be hit in practice — Metro picks .web or .native).
const fallback: TokenStorage = {
  async get() {
    return null;
  },
  async set() {
    throw new Error('No tokenStorage implementation for this platform.');
  },
  async clear() {
    // no-op
  },
};

export default fallback;