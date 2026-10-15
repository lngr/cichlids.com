import { beforeEach, expect, it, vi } from "vitest";
const state = vi.hoisted(() => ({ stored: null as string | null, locale: "pt-BR", rtl: false, storedRTL: null as string | null, failStorage: false }));
vi.mock("react-native", () => ({ Platform: { OS: "ios" }, I18nManager: { allowRTL: vi.fn(), forceRTL: (rtl: boolean) => { state.rtl = rtl; } } }));
vi.mock("expo-localization", () => ({ getLocales: () => [{ languageTag: state.locale }] }));
vi.mock("../lib/localStore", () => ({ localStore: {
  getItem: async (key: string) => { if (state.failStorage) throw new Error("Storage unavailable"); return key === "cichlids.lang" ? state.stored : state.storedRTL; },
  setItem: async (key: string, value: string) => { if (state.failStorage) throw new Error("Storage unavailable"); if (key === "cichlids.lang") state.stored = value; else state.storedRTL = value; },
  removeItem: async () => { state.stored = null; },
} }));
import i18n, { initializeLocalization, setLanguagePreference, setPseudoRTL, setPseudoRTLPreference, refreshDeviceLanguage } from "./index";

beforeEach(() => { state.stored = null; state.locale = "pt-BR"; state.failStorage = false; state.storedRTL = null; });
it("starts in device Portuguese, persists German across initialization and returns to the device", async () => {
  await initializeLocalization();
  expect(i18n.language).toBe("pt-BR");
  expect(i18n.t("me.login")).toBe("Entrar");
  await setLanguagePreference("de");
  expect(state.stored).toBe("de");
  await initializeLocalization();
  expect(i18n.t("me.login")).toBe("Anmelden");
  await setLanguagePreference(null);
  expect(state.stored).toBe(null);
  expect(i18n.language).toBe("pt-BR");
});
it("ignores invalid stored preferences and falls back to English for unsupported devices", async () => {
  state.stored = "invalid";
  state.locale = "ja-JP";
  await initializeLocalization();
  expect(i18n.language).toBe("en");
  expect(i18n.t("common.retry", { lng: "xx" })).toBe("Try again");
});
it("forces native layout direction for pseudo RTL", () => {
  setPseudoRTL(true);
  expect(state.rtl).toBe(true);
  setPseudoRTL(false);
  expect(state.rtl).toBe(false);
});

it("follows changed device language only when the preference is automatic", async () => {
  await initializeLocalization();
  state.locale = "fr-CA";
  await refreshDeviceLanguage();
  expect(i18n.language).toBe("fr");
  await setLanguagePreference("de");
  state.locale = "es-MX";
  await refreshDeviceLanguage();
  expect(i18n.language).toBe("de");
});
it("formats interpolated counts and falls back for an absent translation", async () => {
  await initializeLocalization();
  expect(i18n.t("profile.pictures", { count: 1234 })).toBe("1.234 fotos");
  expect(i18n.t("profile.pictures", { count: 1.5 })).toBe("1,5 foto");
  i18n.addResource("en", "translation", "test.fallback", "English fallback");
  expect(i18n.t("test.fallback")).toBe("English fallback");
});

it("restores native pseudo RTL after a restart", async () => {
  await initializeLocalization();
  await setPseudoRTLPreference(true);
  expect(state.storedRTL).toBe("1");
  setPseudoRTL(false);
  await initializeLocalization();
  expect(state.rtl).toBe(true);
  await setPseudoRTLPreference(false);
  await initializeLocalization();
  expect(state.rtl).toBe(false);
});
it("uses the device when storage is inaccessible and reports a failed preference save", async () => {
  state.failStorage = true;
  await initializeLocalization();
  expect(i18n.language).toBe("pt-BR");
  await expect(setLanguagePreference("de")).rejects.toThrow("Storage unavailable");
  expect(i18n.language).toBe("pt-BR");
});
