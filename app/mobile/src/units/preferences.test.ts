import { beforeEach, expect, it, vi } from "vitest";
const state = vi.hoisted(() => ({ region: "US" as string | null, stored: null as string | null, fail: false }));
vi.mock("expo-localization", () => ({ getLocales: () => [{ languageTag: "de-US", regionCode: state.region }] }));
vi.mock("../lib/localStore", () => ({ localStore: {
  getItem: async () => { if (state.fail) throw new Error("Unavailable"); return state.stored; },
  setItem: async (_key: string, value: string) => { if (state.fail) throw new Error("Unavailable"); state.stored = value; },
  removeItem: async () => { state.stored = null; },
} }));
import { initializeUnits, getUnitSystem, setUnitPreference, refreshDeviceUnits } from "./preferences";
beforeEach(() => { state.region = "US"; state.stored = null; state.fail = false; });
it("defaults from region and persists independently of language across initialization", async () => {
  await initializeUnits();
  expect(getUnitSystem()).toBe("us");
  await setUnitPreference("metric");
  expect(state.stored).toBe("metric");
  await initializeUnits();
  expect(getUnitSystem()).toBe("metric");
  state.region = "US";
  refreshDeviceUnits();
  expect(getUnitSystem()).toBe("metric");
  await setUnitPreference(null);
  expect(getUnitSystem()).toBe("us");
  state.region = "DE";
  refreshDeviceUnits();
  expect(getUnitSystem()).toBe("metric");
});
it("ignores invalid storage and handles inaccessible storage", async () => {
  state.stored = "invalid";
  await initializeUnits();
  expect(getUnitSystem()).toBe("us");
  state.fail = true;
  state.region = null;
  await initializeUnits();
  expect(getUnitSystem()).toBe("metric");
  await expect(setUnitPreference("us")).rejects.toThrow("Unavailable");
  expect(getUnitSystem()).toBe("metric");
});
