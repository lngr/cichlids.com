export const SUPPORTED_LANGUAGES = ["en", "es-419", "pt-BR", "de", "fr", "it", "pl", "nl", "cs", "id", "tr"] as const;
export type SupportedLanguage = (typeof SUPPORTED_LANGUAGES)[number];
export const DEFAULT_LANGUAGE: SupportedLanguage = "en";

export function isSupportedLanguage(value: unknown): value is SupportedLanguage {
  return typeof value === "string" && (SUPPORTED_LANGUAGES as readonly string[]).includes(value);
}

export function resolveLanguage(tag: string | null | undefined): SupportedLanguage {
  const family = tag?.toLowerCase().split(/[-_]/)[0];
  return SUPPORTED_LANGUAGES.find((language) => language.toLowerCase().split("-")[0] === family) ?? DEFAULT_LANGUAGE;
}
