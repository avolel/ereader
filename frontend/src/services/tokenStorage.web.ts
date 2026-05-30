import type { StoredTokens, TokenStorage } from './tokenStorage';

const STORAGE_KEY = 'ereader.auth.tokens.v1';

const webTokenStorage: TokenStorage = {
  async get() {
    const raw = window.localStorage.getItem(STORAGE_KEY);
    if (!raw) return null;
    try {
      return JSON.parse(raw) as StoredTokens;
    } catch {
      // Corrupted entry — wipe so we don't keep failing.
      window.localStorage.removeItem(STORAGE_KEY);
      return null;
    }
  },
  async set(tokens) {
    window.localStorage.setItem(STORAGE_KEY, JSON.stringify(tokens));
  },
  async clear() {
    window.localStorage.removeItem(STORAGE_KEY);
  },
};

export default webTokenStorage;