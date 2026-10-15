import type { MeasurementUnit, ScaledPoint } from "@cichlids/client-core";
import { TRANSLATIONS } from "../i18n/catalogues";
import { resolveLanguage } from "../i18n/locales";

/** The symbol of a measurement unit in the given language; pH has none. */
export function measurementUnitSymbol(unit: MeasurementUnit, language: string): string {
  return unit === "none" ? "" : TRANSLATIONS[resolveLanguage(language)].care.measure.units[unit];
}

export function formatMeasurementValue(value: number, unit: MeasurementUnit, language: string): string {
  const number = new Intl.NumberFormat(resolveLanguage(language), { maximumFractionDigits: 2 }).format(value);
  const symbol = measurementUnitSymbol(unit, language);
  return symbol ? `${number} ${symbol}` : number;
}

export type ChartSegment = { key: string; left: number; top: number; length: number; angle: number };

/**
 * One bar per pair of consecutive points. The bar is laid out horizontally with its center at
 * the midpoint of the pair (left is the position of its left edge, top the y of its center),
 * then rotated by the angle around that center.
 */
export function chartSegments(points: ScaledPoint[]): ChartSegment[] {
  return points.slice(1).map((end, index) => {
    const start = points[index];
    const length = Math.hypot(end.x - start.x, end.y - start.y);
    return {
      key: `${start.entryId}-${end.entryId}`,
      left: (start.x + end.x) / 2 - length / 2,
      top: (start.y + end.y) / 2,
      length,
      angle: Math.atan2(end.y - start.y, end.x - start.x),
    };
  });
}
