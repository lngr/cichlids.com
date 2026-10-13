import { beforeEach, describe, expect, it, vi } from "vitest";

const secureStore = vi.hoisted(() => new Map<string, string>());

vi.mock("expo-secure-store", () => ({
  getItemAsync: vi.fn(async (key: string) => secureStore.get(key) ?? null),
  setItemAsync: vi.fn(async (key: string, value: string) => void secureStore.set(key, value)),
  deleteItemAsync: vi.fn(async (key: string) => void secureStore.delete(key)),
}));

const { localStore } = await import("./localStore");

beforeEach(() => {
  secureStore.clear();
});

describe("localStore on native", () => {
  it("keeps a long value with multibyte characters in chunks the secure store accepts", async () => {
    const value = "Malawibuntbarsch mit Grüntönen ".repeat(170) + "a" + "🐟".repeat(1200);

    await localStore.setItem("draft", value);

    expect(await localStore.getItem("draft")).toBe(value);
    const loneSurrogate = /[\uD800-\uDBFF](?![\uDC00-\uDFFF])|(?<![\uD800-\uDBFF])[\uDC00-\uDFFF]/;
    for (const stored of secureStore.values()) {
      expect(new TextEncoder().encode(stored).length).toBeLessThanOrEqual(2048);
      expect(stored).not.toMatch(loneSurrogate);
    }
  });

  it("removes the chunks a shorter value no longer needs", async () => {
    await localStore.setItem("draft", "x".repeat(2000));
    await localStore.setItem("draft", "short");

    expect(await localStore.getItem("draft")).toBe("short");
    expect([...secureStore.keys()].sort()).toEqual(["draft.0", "draft.count"]);
  });

  it("returns null for a missing value or a missing chunk and removes every chunk", async () => {
    expect(await localStore.getItem("draft")).toBeNull();
    await localStore.setItem("draft", "y".repeat(1500));
    secureStore.delete("draft.1");
    expect(await localStore.getItem("draft")).toBeNull();

    await localStore.removeItem("draft");
    expect(secureStore.size).toBe(0);
  });

  it("keeps an empty value", async () => {
    await localStore.setItem("draft", "");
    expect(await localStore.getItem("draft")).toBe("");
  });
});
