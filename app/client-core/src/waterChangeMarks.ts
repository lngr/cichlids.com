import type { CareEntry, MeasurementUnit } from "./client.js";
import type { DisplayPoint, DisplaySeries, ScaledPoint } from "./measurement.js";

/** A water change as the charts need it: the entry id and the time it happened. */
export type WaterChangeMark = { id: number | string; occurredAt: string };
/** A loaded water change entry with the fields the charts use. */
export type LoadedWaterChange = WaterChangeMark & { entry: CareEntry };

/** The measurements nearest to a water change on each side, in the units of the series. */
export type WaterChangePair = {
  waterChangeId: number | string;
  occurredAt: string;
  unit: MeasurementUnit;
  before: DisplayPoint | null;
  after: DisplayPoint | null;
};

/** The window around a water change in which a measurement counts as its before or after value. */
export const PAIRING_WINDOW_MS = 3 * 24 * 3600 * 1000;

/**
 * For each water change the latest measurement at or before it and the first measurement after it,
 * each only when it lies within the window and no other water change lies between the measurement
 * and this one; otherwise that side stays empty. A measurement between two water changes is the
 * after value of the first and the before value of the second. The pairs are ordered by the time
 * of the water change.
 */
export function pairWaterChanges(series: DisplaySeries, waterChanges: WaterChangeMark[], windowMs: number = PAIRING_WINDOW_MS): WaterChangePair[] {
  const points = series.points.map(point => ({ point, time: Date.parse(point.occurredAt) })).sort((a, b) => a.time - b.time);
  const changes = [...waterChanges].sort((a, b) => Date.parse(a.occurredAt) - Date.parse(b.occurredAt));
  return changes.map((change, index) => {
    const time = Date.parse(change.occurredAt);
    const previous = index > 0 ? Date.parse(changes[index - 1].occurredAt) : -Infinity;
    const next = index < changes.length - 1 ? Date.parse(changes[index + 1].occurredAt) : Infinity;
    const before = points.filter(item => item.time <= time && item.time > previous && time - item.time <= windowMs).at(-1);
    const after = points.find(item => item.time > time && item.time <= next && item.time - time <= windowMs);
    return {
      waterChangeId: change.id, occurredAt: change.occurredAt, unit: series.unit,
      before: before?.point ?? null, after: after?.point ?? null,
    };
  });
}

export type PlacedMarker = { id: number | string; occurredAt: string; x: number; lane: number };
export type ChartLayout = {
  range: { from: number; to: number } | null;
  points: ScaledPoint[];
  markers: PlacedMarker[];
  lanes: number;
};
export type ChartLayoutOptions = { width: number; height: number; minMarkerGap: number; maxLanes?: number };

const DEFAULT_MAX_LANES = 3;

/**
 * Positions the points and the water change markers of one chart on a shared time axis. The time
 * range covers both; the oldest time is at the left. Values scale to the box with the highest at the
 * top. Markers closer than the minimum gap go to different lanes, up to the maximum number of lanes.
 */
export function layoutChart(points: DisplayPoint[], marks: WaterChangeMark[], options: ChartLayoutOptions): ChartLayout {
  const times = [...points.map(point => Date.parse(point.occurredAt)), ...marks.map(mark => Date.parse(mark.occurredAt))];
  if (times.length === 0) return { range: null, points: [], markers: [], lanes: 0 };
  const from = Math.min(...times);
  const to = Math.max(...times);
  const xOf = (time: number) => to === from ? options.width / 2 : (time - from) / (to - from) * options.width;
  const values = points.map(point => point.value);
  const [low, high] = [Math.min(...values), Math.max(...values)];
  const scaled = points.map(point => ({
    entryId: point.entryId,
    x: xOf(Date.parse(point.occurredAt)),
    y: high === low ? options.height / 2 : options.height - (point.value - low) / (high - low) * options.height,
  }));
  const maxLanes = options.maxLanes ?? DEFAULT_MAX_LANES;
  const laneEnds: number[] = [];
  const markers = [...marks].sort((a, b) => Date.parse(a.occurredAt) - Date.parse(b.occurredAt)).map(mark => {
    const x = xOf(Date.parse(mark.occurredAt));
    let lane = laneEnds.findIndex(end => x - end >= options.minMarkerGap);
    if (lane < 0) lane = laneEnds.length < maxLanes ? laneEnds.length : maxLanes - 1;
    laneEnds[lane] = x;
    return { id: mark.id, occurredAt: mark.occurredAt, x, lane };
  });
  return { range: { from, to }, points: scaled, markers, lanes: laneEnds.length };
}

export type WaterChangeLoadOptions = { pageSize: number; maxPages: number };

/**
 * Pages through the care entries of a tank, newest first, and returns the water changes that happened
 * at or after the given time, oldest first. Paging stops when a page reaches before that time, at the
 * last page, or after the maximum number of pages.
 */
export async function loadWaterChanges(
  fetchPage: (offset: number, limit: number) => Promise<{ total: number | string; items: CareEntry[] }>,
  since: number,
  options: WaterChangeLoadOptions,
): Promise<LoadedWaterChange[]> {
  const found: LoadedWaterChange[] = [];
  for (let page = 0; page < options.maxPages; page++) {
    const { items } = await fetchPage(page * options.pageSize, options.pageSize);
    for (const entry of items) {
      if (entry.kind === "water_change" && Date.parse(entry.occurredAt) >= since) found.push({ id: entry.id, occurredAt: entry.occurredAt, entry });
    }
    const last = items.at(-1);
    if (items.length < options.pageSize || !last || Date.parse(last.occurredAt) < since) break;
  }
  return found.sort((a, b) => Date.parse(a.occurredAt) - Date.parse(b.occurredAt));
}
