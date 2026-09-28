import { beforeEach, describe, expect, it, vi } from "vitest";
import type { TokenSet } from "./tokenLogic";

const platform = vi.hoisted(() => ({ OS: "web" as string }));
const secureStore = vi.hoisted(() => new Map<string, string>());

vi.mock("react-native", () => ({ Platform: platform }));
vi.mock("expo-secure-store", () => ({
  getItemAsync: vi.fn(async (key: string) => secureStore.get(key) ?? null),
  setItemAsync: vi.fn(async (key: string, value: string) => void secureStore.set(key, value)),
  deleteItemAsync: vi.fn(async (key: string) => void secureStore.delete(key)),
}));

const { tokenStorage, TOKEN_STORAGE_KEY } = await import("./tokenStorage");

class MemoryStorage {
  private readonly items = new Map<string, string>();
  getItem(key: string) {
    return this.items.get(key) ?? null;
  }
  setItem(key: string, value: string) {
    this.items.set(key, value);
  }
  removeItem(key: string) {
    this.items.delete(key);
  }
}

const tokens: TokenSet = { accessToken: "access", refreshToken: "refresh", idToken: "id", expiresAt: 1_800_000_000_000 };

beforeEach(() => {
  secureStore.clear();
  vi.stubGlobal("sessionStorage", new MemoryStorage());
});

describe("tokenStorage on web", () => {
  beforeEach(() => {
    platform.OS = "web";
  });

  it("stores the serialized token set in sessionStorage", async () => {
    await tokenStorage.set(tokens);
    expect(JSON.parse(sessionStorage.getItem(TOKEN_STORAGE_KEY)!)).toEqual(tokens);
    expect(secureStore.size).toBe(0);
    expect(await tokenStorage.get()).toEqual(tokens);
  });

  it("returns null when nothing or something unreadable is stored", async () => {
    expect(await tokenStorage.get()).toBeNull();
    sessionStorage.setItem(TOKEN_STORAGE_KEY, "{broken");
    expect(await tokenStorage.get()).toBeNull();
  });

  it("clears the stored token set", async () => {
    await tokenStorage.set(tokens);
    await tokenStorage.clear();
    expect(sessionStorage.getItem(TOKEN_STORAGE_KEY)).toBeNull();
    expect(await tokenStorage.get()).toBeNull();
  });
});

describe("tokenStorage on native", () => {
  beforeEach(() => {
    platform.OS = "android";
  });

  it("stores the serialized token set in the secure store", async () => {
    await tokenStorage.set(tokens);
    expect(JSON.parse(secureStore.get(TOKEN_STORAGE_KEY)!)).toEqual(tokens);
    expect(sessionStorage.getItem(TOKEN_STORAGE_KEY)).toBeNull();
    expect(await tokenStorage.get()).toEqual(tokens);
  });

  it("clears the stored token set", async () => {
    await tokenStorage.set(tokens);
    await tokenStorage.clear();
    expect(secureStore.has(TOKEN_STORAGE_KEY)).toBe(false);
    expect(await tokenStorage.get()).toBeNull();
  });
});
