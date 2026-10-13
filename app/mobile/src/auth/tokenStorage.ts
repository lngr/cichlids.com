import * as SecureStore from "expo-secure-store";
import { parseStoredTokenSet, type TokenSet } from "./tokenLogic";

export const TOKEN_STORAGE_KEY = "cichlids.auth.tokens";

/**
 * Persists the token set of the native app in expo-secure-store (Keychain on iOS, Keystore-backed
 * storage on Android). The web build keeps no tokens in the browser.
 */
export const tokenStorage = {
  async get(): Promise<TokenSet | null> {
    return parseStoredTokenSet(await SecureStore.getItemAsync(TOKEN_STORAGE_KEY));
  },
  async set(tokenSet: TokenSet): Promise<void> {
    await SecureStore.setItemAsync(TOKEN_STORAGE_KEY, JSON.stringify(tokenSet));
  },
  async clear(): Promise<void> {
    await SecureStore.deleteItemAsync(TOKEN_STORAGE_KEY);
  },
};
