import type { CareEntry, CareAmountRequest } from "@cichlids/client-core";
import type { UnitSystem } from "../units/format";
import { TRANSLATIONS } from "../i18n/catalogues";
import { resolveLanguage } from "../i18n/locales";

type Amount = CareEntry["amount"];
export type AmountUnit = "liter" | "us_gallon" | "percent";
export function amountDraft(amount: Amount, system: UnitSystem): { value: string; unit: AmountUnit } {
  return { value: amount ? String(amount.value) : "", unit: amount?.unit ?? (system === "us" ? "us_gallon" : "liter") };
}
export function parseCareAmount(text: string, unit: AmountUnit): CareAmountRequest | null {
  const value = Number(text.trim().replace(",", "."));
  return /^\d+([.,]\d+)?$/.test(text.trim()) && Number.isFinite(value) && value > 0 && value <= (unit === "percent" ? 100 : 1000000)
    ? { value, unit } : null;
}
export function formatCareAmount(amount: NonNullable<Amount>, language: string): string {
  const locale = resolveLanguage(language);
  const number = new Intl.NumberFormat(locale, { maximumFractionDigits: 20 }).format(Number(amount.value));
  const symbol = amount.unit === "percent" ? "%" : TRANSLATIONS[locale].units.symbols.volume[amount.unit === "liter" ? "metric" : "us"];
  return `${number} ${symbol}`;
}
