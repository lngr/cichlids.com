import { PPM_PER_GERMAN_DEGREE } from "@cichlids/client-core";
import { TRANSLATIONS } from "../i18n/catalogues";
import { resolveLanguage } from "../i18n/locales";

export type UnitSystem = "metric" | "us";
export type MeasurementKind = "volume" | "length" | "temperature" | "hardness";
export type MeasurementInput = { value: number; system: UnitSystem };

// NIST SP 811 Appendix B: 1 inch = 2.54 cm; US liquid gallon = 231 cubic inches,
// exactly 3.785411784 L. Fahrenheit: C = (F - 32) * 5 / 9.
// https://www.nist.gov/pml/special-publication-811/nist-guide-si-appendix-b-conversion-factors
const LITERS_PER_US_GALLON = 3.785411784;
const CENTIMETERS_PER_INCH = 2.54;

export function unitsForRegion(region: string | null | undefined): UnitSystem {
  return region?.toUpperCase() === "US" ? "us" : "metric";
}

export function toMetric(value: number, kind: MeasurementKind, system: UnitSystem): number {
  if (system === "metric") return value;
  switch (kind) {
    case "volume": return value * LITERS_PER_US_GALLON;
    case "length": return value * CENTIMETERS_PER_INCH;
    case "temperature": return (value - 32) * 5 / 9;
    case "hardness": return value / PPM_PER_GERMAN_DEGREE;
  }
}

export function fromMetric(value: number, kind: MeasurementKind, system: UnitSystem): number {
  if (system === "metric") return value;
  switch (kind) {
    case "volume": return value / LITERS_PER_US_GALLON;
    case "length": return value / CENTIMETERS_PER_INCH;
    case "temperature": return value * 9 / 5 + 32;
    case "hardness": return value * PPM_PER_GERMAN_DEGREE;
  }
}

export function formatMeasurement(
  metric: number, kind: MeasurementKind, preference: UnitSystem, language: string,
  input?: MeasurementInput, owner = false,
): string {
  const original = owner ? input : undefined;
  const system = original?.system ?? preference;
  const value = original?.value ?? fromMetric(metric, kind, system);
  const locale = resolveLanguage(language);
  const number = new Intl.NumberFormat(locale, { maximumFractionDigits: original ? 20 : 2 }).format(value);
  return `${number} ${TRANSLATIONS[locale].units.symbols[kind][system]}`;
}
