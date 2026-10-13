import { beforeEach, describe, expect, it, vi } from "vitest";
import type { TokenSet } from "./tokenLogic";

const secureStore = vi.hoisted(() => new Map<string, string>());

vi.mock("expo-secure-store", () => ({
  getItemAsync: vi.fn(async (key: string) => secureStore.get(key) ?? null),
  setItemAsync: vi.fn(async (key: string, value: string) => void secureStore.set(key, value)),
  deleteItemAsync: vi.fn(async (key: string) => void secureStore.delete(key)),
}));

const { tokenStorage, TOKEN_STORAGE_KEY } = await import("./tokenStorage");

const tokens: TokenSet = { accessToken: "access", refreshToken: "refresh", idToken: "id", expiresAt: 1_800_000_000_000 };

beforeEach(() => {
  secureStore.clear();
});

describe("tokenStorage", () => {
  it("stores the serialized token set in the secure store", async () => {
    await tokenStorage.set(tokens);
    expect(JSON.parse(secureStore.get(TOKEN_STORAGE_KEY)!)).toEqual(tokens);
    expect(await tokenStorage.get()).toEqual(tokens);
  });

  it("returns null when nothing or something unreadable is stored", async () => {
    expect(await tokenStorage.get()).toBeNull();
    secureStore.set(TOKEN_STORAGE_KEY, "{broken");
    expect(await tokenStorage.get()).toBeNull();
  });

  it("clears the stored token set", async () => {
    await tokenStorage.set(tokens);
    await tokenStorage.clear();
    expect(secureStore.has(TOKEN_STORAGE_KEY)).toBe(false);
    expect(await tokenStorage.get()).toBeNull();
  });
});
