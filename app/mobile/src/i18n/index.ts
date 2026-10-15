import { I18nManager, Platform } from "react-native";
import * as Localization from "expo-localization";
import i18next from "i18next";
import { initReactI18next } from "react-i18next";
import { localStore } from "../lib/localStore";
import { DEFAULT_LANGUAGE, isSupportedLanguage, resolveLanguage, SUPPORTED_LANGUAGES, type SupportedLanguage } from "./locales";
import { TRANSLATIONS } from "./catalogues";

export { DEFAULT_LANGUAGE, SUPPORTED_LANGUAGES, type SupportedLanguage } from "./locales";
export const LANGUAGE_STORAGE_KEY = "cichlids.lang";
const RTL_STORAGE_KEY = "cichlids.pseudoRTL";
export let languagePreference: SupportedLanguage | null = null;
export let pseudoRTL = false;
const directionListeners = new Set<() => void>();
export function subscribeDirection(listener: () => void): () => void {
  directionListeners.add(listener);
  return () => { directionListeners.delete(listener); };
}
export const getDirection = () => pseudoRTL;

export async function refreshDeviceLanguage(): Promise<void> {
  if (languagePreference === null) await i18next.changeLanguage(detectLanguage());
}

export async function setPseudoRTLPreference(enabled: boolean): Promise<void> {
  await localStore.setItem(RTL_STORAGE_KEY, enabled ? "1" : "0");
  setPseudoRTL(enabled);
}

export function detectLanguage(): SupportedLanguage {
  return resolveLanguage(Localization.getLocales()[0]?.languageTag);
}

function webQuery(): URLSearchParams {
  return new URLSearchParams(Platform.OS === "web" && typeof window !== "undefined" ? window.location.search : "");
}

export function setPseudoRTL(enabled: boolean): void {
  pseudoRTL = enabled;
  directionListeners.forEach((listener) => listener());
  if (Platform.OS === "web") {
    if (typeof document !== "undefined") document.documentElement.dir = enabled ? "rtl" : "ltr";
  } else {
    I18nManager.allowRTL(true);
    I18nManager.forceRTL(enabled);
  }
}

function updateDocument(language: string): void {
  if (Platform.OS === "web" && typeof document !== "undefined") document.documentElement.lang = language;
}

const resources = Object.fromEntries(Object.entries(TRANSLATIONS).map(([language, translation]) => [language, { translation }]));
const ready = i18next.use({
  type: "formatter",
  init() {},
  format(value: unknown, _format: string | undefined, language: string | undefined) {
    return typeof value === "number" ? new Intl.NumberFormat(resolveLanguage(language)).format(value) : String(value ?? "");
  },
}).use(initReactI18next).init({
  resources,
  lng: detectLanguage(),
  fallbackLng: DEFAULT_LANGUAGE,
  supportedLngs: [...SUPPORTED_LANGUAGES],
  load: "currentOnly",
  interpolation: {
    escapeValue: false,
    alwaysFormat: true,
  },
});
i18next.on("languageChanged", updateDocument);

export async function initializeLocalization(): Promise<void> {
  await ready;
  let stored: string | null = null;
  try { stored = await localStore.getItem(LANGUAGE_STORAGE_KEY); } catch { /* Device language is available when storage fails. */ }
  const query = webQuery().get("lang");
  languagePreference = isSupportedLanguage(query) ? query : isSupportedLanguage(stored) ? stored : null;
  if (isSupportedLanguage(query)) {
    try { await localStore.setItem(LANGUAGE_STORAGE_KEY, query); } catch { /* The session can use the language without persistence. */ }
  }
  await i18next.changeLanguage(languagePreference ?? detectLanguage());
  updateDocument(i18next.language);
  let storedRTL: string | null = null;
  try { storedRTL = await localStore.getItem(RTL_STORAGE_KEY); } catch { /* Direction defaults to LTR. */ }
  const queryRTL = webQuery().get("rtl");
  setPseudoRTL(queryRTL !== null ? queryRTL === "1" : storedRTL === "1" || process.env.EXPO_PUBLIC_PSEUDO_RTL === "1");
}

export async function setLanguagePreference(language: SupportedLanguage | null): Promise<void> {
  if (language === null) await localStore.removeItem(LANGUAGE_STORAGE_KEY);
  else await localStore.setItem(LANGUAGE_STORAGE_KEY, language);
  languagePreference = language;
  if (Platform.OS === "web" && typeof window !== "undefined") {
    const url = new URL(window.location.href);
    url.searchParams.delete("lang");
    window.history.replaceState(window.history.state, "", url);
  }
  await i18next.changeLanguage(language ?? detectLanguage());
}

export default i18next;
