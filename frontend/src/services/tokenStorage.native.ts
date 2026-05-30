import * as SecureStore from 'expo-secure-store';
import type { StoredTokens, TokenStorage } from './tokenStorage';

// SecureStore values are capped at ~2KB; JWTs comfortably fit.
const STORAGE_KEY = 'ereader_auth_tokens_v1';

const nativeTokenStorage: TokenStorage = {
  async get() {
    const raw = await SecureStore.getItemAsync(STORAGE_KEY);
    if (!raw) return null;
    try {
      return JSON.parse(raw) as StoredTokens;
    } catch {
      await SecureStore.deleteItemAsync(STORAGE_KEY);
      return null;
    }
  },
  async set(tokens) {
    await SecureStore.setItemAsync(STORAGE_KEY, JSON.stringify(tokens));
  },
  async clear() {
    await SecureStore.deleteItemAsync(STORAGE_KEY);
  },
};

export default nativeTokenStorage;