import { Platform } from "react-native";
import * as SecureStore from "expo-secure-store";
import { parseStoredTokenSet, type TokenSet } from "./tokenLogic";

export const TOKEN_STORAGE_KEY = "cichlids.auth.tokens";

interface StringStore {
  get(key: string): Promise<string | null>;
  set(key: string, value: string): Promise<void>;
  remove(key: string): Promise<void>;
}

const secureStore: StringStore = {
  get: (key) => SecureStore.getItemAsync(key),
  set: (key, value) => SecureStore.setItemAsync(key, value),
  remove: (key) => SecureStore.deleteItemAsync(key),
};

// sessionStorage scopes the session to the browser tab: a reload keeps it, closing the tab ends it.
const webSessionStore: StringStore = {
  get: async (key) => globalThis.sessionStorage?.getItem(key) ?? null,
  set: async (key, value) => globalThis.sessionStorage?.setItem(key, value),
  remove: async (key) => globalThis.sessionStorage?.removeItem(key),
};

function store(): StringStore {
  return Platform.OS === "web" ? webSessionStore : secureStore;
}

/** Persists the token set: expo-secure-store on native, sessionStorage on web. */
export const tokenStorage = {
  async get(): Promise<TokenSet | null> {
    return parseStoredTokenSet(await store().get(TOKEN_STORAGE_KEY));
  },
  async set(tokenSet: TokenSet): Promise<void> {
    await store().set(TOKEN_STORAGE_KEY, JSON.stringify(tokenSet));
  },
  async clear(): Promise<void> {
    await store().remove(TOKEN_STORAGE_KEY);
  },
};
