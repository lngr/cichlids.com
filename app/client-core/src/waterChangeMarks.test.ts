import { describe, expect, it } from "vitest";
import type { CareEntry } from "./client.js";
import type { DisplaySeries } from "./measurement.js";
import { layoutChart, loadWaterChanges, pairWaterChanges } from "./waterChangeMarks.js";

const DAY = 24 * 3600 * 1000;
const base = Date.parse("2026-10-01T12:00:00Z");
const at = (days: number) => new Date(base + days * DAY).toISOString();
const nitrate = (...pairs: [number, number][]): DisplaySeries => ({
  parameter: "no3", unit: "mg_per_liter",
  points: pairs.map(([days, value], index) => ({ entryId: index + 1, occurredAt: at(days), value })),
});
const change = (id: number, days: number) => ({ id, occurredAt: at(days) });

describe("pairing water changes with the measurements around them", () => {
  it("finds the nearest measurement before and after within the window", () => {
    const series = nitrate([0, 10], [2, 40], [3, 15], [5, 20]);
    const [pair] = pairWaterChanges(series, [change(100, 2.5)], 3 * DAY);
    expect(pair.waterChangeId).toBe(100);
    expect(pair.unit).toBe("mg_per_liter");
    expect(pair.before?.value).toBe(40);
    expect(pair.after?.value).toBe(15);
  });
  it("leaves a side empty when no measurement lies within the window", () => {
    const series = nitrate([0, 40], [9, 12]);
    const [pair] = pairWaterChanges(series, [change(1, 1)], 3 * DAY);
    expect(pair.before?.value).toBe(40);
    expect(pair.after).toBeNull();
  });
  it("has no pair sides without measurements", () => {
    expect(pairWaterChanges(nitrate(), [change(1, 1)], 3 * DAY)).toEqual([
      { waterChangeId: 1, occurredAt: at(1), unit: "mg_per_liter", before: null, after: null },
    ]);
  });
  it("has only an after side when all measurements follow the water change", () => {
    const [pair] = pairWaterChanges(nitrate([1, 8], [2, 9]), [change(1, 0)], 3 * DAY);
    expect(pair.before).toBeNull();
    expect(pair.after?.value).toBe(8);
  });
  it("has only a before side when all measurements precede the water change", () => {
    const [pair] = pairWaterChanges(nitrate([-2, 30], [-1, 35]), [change(1, 0)], 3 * DAY);
    expect(pair.before?.value).toBe(35);
    expect(pair.after).toBeNull();
  });
  it("never pairs across another water change", () => {
    const pairs = pairWaterChanges(nitrate([0, 40], [4, 10]), [change(1, 1), change(2, 2)], 3 * DAY);
    expect(pairs.map(pair => [pair.before?.value ?? null, pair.after?.value ?? null])).toEqual([[40, null], [null, 10]]);
  });
  it("shares a measurement between two water changes as after the first and before the second", () => {
    const pairs = pairWaterChanges(nitrate([0, 40], [1.5, 22], [3, 11]), [change(1, 1), change(2, 2)], 3 * DAY);
    expect(pairs.map(pair => [pair.before?.value ?? null, pair.after?.value ?? null])).toEqual([[40, 22], [22, 11]]);
  });
  it("counts a measurement at the exact time of the water change as before it", () => {
    const [pair] = pairWaterChanges(nitrate([0, 30], [1, 12]), [change(1, 0)], 3 * DAY);
    expect(pair.before?.value).toBe(30);
    expect(pair.after?.value).toBe(12);
  });
  it("keeps the unit of the series in display units", () => {
    const series: DisplaySeries = { parameter: "temperature", unit: "fahrenheit", points: [{ entryId: 1, occurredAt: at(0), value: 77 }] };
    expect(pairWaterChanges(series, [change(1, 1)], DAY)[0].unit).toBe("fahrenheit");
  });
  it("orders the pairs by the time of the water change", () => {
    const pairs = pairWaterChanges(nitrate([0, 1]), [change(2, 3), change(1, 1)], 5 * DAY);
    expect(pairs.map(pair => pair.waterChangeId)).toEqual([1, 2]);
  });
});

describe("laying out points and markers on one time axis", () => {
  const options = { width: 300, height: 100, minMarkerGap: 48 };
  it("covers both the measurements and the water changes in the time range", () => {
    const layout = layoutChart(nitrate([1, 10], [3, 30]).points, [change(1, 0), change(2, 5)], options);
    expect(layout.range).toEqual({ from: base, to: base + 5 * DAY });
    expect(layout.markers.map(marker => marker.x)).toEqual([0, 300]);
    expect(layout.points.map(point => Math.round(point.x))).toEqual([60, 180]);
  });
  it("scales the values like the points alone: highest at the top", () => {
    const layout = layoutChart(nitrate([0, 10], [1, 30]).points, [], options);
    expect(layout.points.map(point => point.y)).toEqual([100, 0]);
  });
  it("centers a single time", () => {
    const layout = layoutChart(nitrate([0, 10]).points, [change(1, 0)], options);
    expect(layout.points[0].x).toBe(150);
    expect(layout.markers[0].x).toBe(150);
    expect(layout.points[0].y).toBe(50);
  });
  it("lays out nothing without points or markers", () => {
    expect(layoutChart([], [], options)).toEqual({ range: null, points: [], markers: [], lanes: 0 });
  });
  it("lays out markers without points", () => {
    const layout = layoutChart([], [change(1, 0), change(2, 2)], options);
    expect(layout.markers.map(marker => marker.x)).toEqual([0, 300]);
    expect(layout.points).toEqual([]);
  });
  it("puts markers closer than the gap on different lanes", () => {
    const layout = layoutChart(nitrate([0, 1], [30, 2]).points, [change(1, 10), change(2, 10.5), change(3, 20)], options);
    expect(layout.markers.map(marker => marker.lane)).toEqual([0, 1, 0]);
    expect(layout.lanes).toBe(2);
  });
  it("keeps markers that are far enough apart on one lane", () => {
    const layout = layoutChart(nitrate([0, 1], [30, 2]).points, [change(1, 0), change(2, 15), change(3, 30)], options);
    expect(layout.markers.every(marker => marker.lane === 0)).toBe(true);
    expect(layout.lanes).toBe(1);
  });
  it("caps the lanes and shares the last one beyond that", () => {
    const marks = [change(1, 0), change(2, 0.01), change(3, 0.02), change(4, 0.03), change(5, 0.04), change(6, 30)];
    const layout = layoutChart(nitrate([0, 1], [30, 2]).points, marks, options);
    expect(layout.lanes).toBe(3);
    expect(Math.max(...layout.markers.map(marker => marker.lane))).toBe(2);
  });
  it("keeps the order of the markers by time", () => {
    const layout = layoutChart([], [change(2, 5), change(1, 1)], options);
    expect(layout.markers.map(marker => marker.id)).toEqual([1, 2]);
  });
});

describe("loading the water changes of a tank", () => {
  const entry = (id: number, kind: "water_change" | "measurement", days: number) => ({ id, kind, occurredAt: at(days) }) as unknown as CareEntry;
  const pager = (entries: CareEntry[]) => {
    const calls: [number, number][] = [];
    const fetchPage = async (offset: number, limit: number) => {
      calls.push([offset, limit]);
      return { total: entries.length, items: entries.slice(offset, offset + limit) };
    };
    return { calls, fetchPage };
  };
  it("returns only water changes within the range, oldest first", async () => {
    const { fetchPage } = pager([entry(5, "water_change", 9), entry(4, "measurement", 8), entry(3, "water_change", 4), entry(2, "water_change", -3), entry(1, "water_change", -20)]);
    const marks = await loadWaterChanges(fetchPage, base - 5 * DAY, { pageSize: 2, maxPages: 10 });
    expect(marks.map(mark => mark.id)).toEqual([2, 3, 5]);
  });
  it("stops paging once a page reaches before the range", async () => {
    const { fetchPage, calls } = pager([entry(4, "water_change", 9), entry(3, "water_change", 4), entry(2, "water_change", -30), entry(1, "water_change", -40)]);
    await loadWaterChanges(fetchPage, base, { pageSize: 2, maxPages: 10 });
    expect(calls).toEqual([[0, 2], [2, 2]]);
  });
  it("stops at the last page", async () => {
    const { fetchPage, calls } = pager([entry(1, "water_change", 1)]);
    expect(await loadWaterChanges(fetchPage, base - DAY, { pageSize: 2, maxPages: 10 })).toHaveLength(1);
    expect(calls).toEqual([[0, 2]]);
  });
  it("stops at the upper bound of pages", async () => {
    const { fetchPage, calls } = pager(Array.from({ length: 20 }, (_, index) => entry(20 - index, "water_change", 20 - index)));
    const marks = await loadWaterChanges(fetchPage, base - 100 * DAY, { pageSize: 2, maxPages: 3 });
    expect(calls).toHaveLength(3);
    expect(marks).toHaveLength(6);
  });
  it("keeps the amount and ids of the entry for opening it", async () => {
    const full = { ...entry(7, "water_change", 1), amount: { value: 25, unit: "percent" } } as unknown as CareEntry;
    const [mark] = await loadWaterChanges(pager([full]).fetchPage, base, { pageSize: 5, maxPages: 2 });
    expect(mark.entry).toBe(full);
    expect(mark.id).toBe(7);
  });
});
