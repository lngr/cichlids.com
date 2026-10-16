import type { CareHeatmapDto } from "./generated/types.gen.js";

export const HEATMAP_WEEKS = 16;
const MAX_LEVEL = 4;

/** The care of one local calendar day: how many water changes and measurements were logged. */
export type CareHeatmapDay = { date: string; waterChanges: number; measurements: number };

/** The care days of the last 16 ISO weeks, from a Monday (start) to today (end); days lists only days with care. */
export type CareHeatmap = {
  start: string;
  end: string;
  weeks: number;
  days: CareHeatmapDay[];
  activeDays: number;
  lastWaterChange: string | null;
  lastMeasurement: string | null;
  /** The days of the window on which a decommissioning pauses all care. */
  decommissionedDays: string[];
};

export type HeatmapKind = "none" | "water_change" | "measurement" | "both";

/** One day of the grid. Level runs from 0 (no care) to 4; inWindow is false for the days after today; decommissioned marks a day of rest. */
export type HeatmapCell = {
  date: string; waterChanges: number; measurements: number; level: number; kind: HeatmapKind; inWindow: boolean; decommissioned: boolean;
};

/** Week columns from the oldest to the current week, each with seven cells from Monday to Sunday. */
export type HeatmapLayout = { columns: HeatmapCell[][] };

export const toCareHeatmap = (dto: CareHeatmapDto): CareHeatmap => ({
  start: dto.start, end: dto.end, weeks: Number(dto.weeks),
  days: dto.days.map(day => ({ date: day.date, waterChanges: Number(day.waterChanges), measurements: Number(day.measurements) })),
  activeDays: Number(dto.activeDays), lastWaterChange: dto.lastWaterChange, lastMeasurement: dto.lastMeasurement,
  decommissionedDays: dto.decommissionedDays ?? [],
});

/** The intensity step of a day from the number of care entries on it: 0 to 3 as they are, 4 for four or more. */
export const heatmapIntensity = (total: number): number => Math.min(Math.max(total, 0), MAX_LEVEL);

const kindOf = (waterChanges: number, measurements: number): HeatmapKind =>
  waterChanges > 0 && measurements > 0 ? "both" : waterChanges > 0 ? "water_change" : measurements > 0 ? "measurement" : "none";

const dayNumber = (date: string): number => {
  const [year, month, day] = date.split("-").map(Number);
  return Math.round(Date.UTC(year!, month! - 1, day!) / 86_400_000);
};

const dateOf = (days: number): string => new Date(days * 86_400_000).toISOString().slice(0, 10);

/** Arranges the window as week columns of seven weekday rows. */
export function layoutHeatmap(heatmap: CareHeatmap): HeatmapLayout {
  const byDate = new Map(heatmap.days.map(day => [day.date, day]));
  const resting = new Set(heatmap.decommissionedDays);
  const first = dayNumber(heatmap.start);
  const last = dayNumber(heatmap.end);
  const columns = Array.from({ length: HEATMAP_WEEKS }, (_, week) => Array.from({ length: 7 }, (_, row): HeatmapCell => {
    const number = first + week * 7 + row;
    const date = dateOf(number);
    const day = byDate.get(date);
    const waterChanges = day?.waterChanges ?? 0;
    const measurements = day?.measurements ?? 0;
    return { date, waterChanges, measurements, level: heatmapIntensity(waterChanges + measurements), kind: kindOf(waterChanges, measurements), inWindow: number <= last, decommissioned: resting.has(date) };
  }));
  return { columns };
}

/** The days with care, newest first: the list that reads as an alternative to the grid. */
export const heatmapListItems = (heatmap: CareHeatmap): Array<CareHeatmapDay & { kind: HeatmapKind }> =>
  [...heatmap.days].sort((a, b) => (a.date < b.date ? 1 : a.date > b.date ? -1 : 0))
    .map(day => ({ ...day, kind: kindOf(day.waterChanges, day.measurements) }));
