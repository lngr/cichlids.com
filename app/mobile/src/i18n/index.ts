import { Platform } from "react-native";
import * as Localization from "expo-localization";
import i18next from "i18next";
import { initReactI18next } from "react-i18next";
import en from "./en.json";
import de from "./de.json";

export const SUPPORTED_LANGUAGES = ["en", "de"] as const;
export type SupportedLanguage = (typeof SUPPORTED_LANGUAGES)[number];
export const DEFAULT_LANGUAGE: SupportedLanguage = "en";

const LANG_OVERRIDE_STORAGE_KEY = "cichlids:lang";

function isSupportedLanguage(value: string | null | undefined): value is SupportedLanguage {
  return value === "en" || value === "de";
}

/**
 * On web, a `?lang=` query param forces a language for that session (used by
 * the Playwright smoke test and for manual QA) and is remembered in
 * localStorage so it survives client-side navigation. Native builds have no
 * query string, so this branch is a no-op there.
 */
function readWebLanguageOverride(): SupportedLanguage | null {
  if (Platform.OS !== "web" || typeof window === "undefined") return null;
  try {
    const queryLang = new URLSearchParams(window.location.search).get("lang");
    if (isSupportedLanguage(queryLang)) {
      window.localStorage?.setItem(LANG_OVERRIDE_STORAGE_KEY, queryLang);
      return queryLang;
    }
    const stored = window.localStorage?.getItem(LANG_OVERRIDE_STORAGE_KEY);
    if (isSupportedLanguage(stored)) return stored;
  } catch {
    // localStorage can throw in locked-down/private browsing contexts; fall
    // through to the device locale in that case.
  }
  return null;
}

/** Resolves the app's active language: web override, then device locale, then the English fallback. */
export function detectLanguage(): SupportedLanguage {
  const override = readWebLanguageOverride();
  if (override) return override;
  const deviceLanguageCode = Localization.getLocales()[0]?.languageCode;
  return isSupportedLanguage(deviceLanguageCode) ? deviceLanguageCode : DEFAULT_LANGUAGE;
}

void i18next.use(initReactI18next).init({
  resources: {
    en: { translation: en },
    de: { translation: de },
  },
  lng: detectLanguage(),
  fallbackLng: DEFAULT_LANGUAGE,
  supportedLngs: SUPPORTED_LANGUAGES,
  interpolation: { escapeValue: false },
});

export default i18next;
