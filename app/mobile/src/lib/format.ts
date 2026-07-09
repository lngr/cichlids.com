const LOCALE_TAGS: Record<string, string> = {
  en: "en-US",
  de: "de-DE",
};

function resolveLocaleTag(language: string): string {
  return LOCALE_TAGS[language] ?? LOCALE_TAGS.en;
}

/** Formats a count with a locale-appropriate compact notation (e.g. "1.2k" in en-US, "1,2 Tsd." in de-DE). */
export function formatCount(n: number, language: string): string {
  return new Intl.NumberFormat(resolveLocaleTag(language), { notation: "compact", maximumFractionDigits: 1 }).format(n);
}

export function formatDate(iso: string | null, language: string): string {
  if (!iso) return "";
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) return "";
  return new Intl.DateTimeFormat(resolveLocaleTag(language), { year: "numeric", month: "short", day: "numeric" }).format(date);
}

/** The generated schema types int32/int64 as number|string; the API always sends numbers. */
export function asNumber(value: number | string | null | undefined): number {
  if (value === null || value === undefined) return 0;
  return typeof value === "number" ? value : Number(value);
}
