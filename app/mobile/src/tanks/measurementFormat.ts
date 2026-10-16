import { valueInSystem, type CareEntry, type MeasurementUnit, type ScaledPoint, type UnitSystem, type WaterChangePair } from "@cichlids/client-core";
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

function fill(template: string, values: Record<string, string>): string {
  return template.replace(/{{\s*(\w+)\s*}}/g, (_, key: string) => values[key] ?? "");
}

/** One text line per value of a measurement, in the member's unit system. */
export function measurementLines(entry: CareEntry, system: UnitSystem, language: string): string[] {
  const measure = TRANSLATIONS[resolveLanguage(language)].care.measure;
  return (entry.values ?? []).map(stored => {
    const shown = valueInSystem(stored, system);
    return fill(stored.origin ? measure.lineWithOrigin : measure.line, {
      parameter: measure.parameters[stored.parameter],
      value: formatMeasurementValue(shown.value, shown.unit, language),
      origin: stored.origin ? measure.origins[stored.origin] : "",
    });
  });
}

/** The date and time of a chart entry in the language. */
export function formatEntryTime(iso: string, language: string): string {
  return new Intl.DateTimeFormat(language, { dateStyle: "medium", timeStyle: "short" }).format(new Date(iso));
}

/**
 * The sentence for the values around a water change, such as nitrate 40 to 20 mg/l after it;
 * null when no measurement lies on either side.
 */
export function waterChangePairLine(pair: WaterChangePair, parameterName: string, language: string): string | null {
  if (!pair.before && !pair.after) return null;
  const marks = TRANSLATIONS[resolveLanguage(language)].care.marks;
  const template = pair.before && pair.after ? marks.pairBoth : pair.before ? marks.pairBefore : marks.pairAfter;
  return fill(template, {
    parameter: parameterName,
    before: pair.before ? formatMeasurementValue(pair.before.value, pair.unit, language) : "",
    after: pair.after ? formatMeasurementValue(pair.after.value, pair.unit, language) : "",
    date: formatEntryTime(pair.occurredAt, language),
  });
}
