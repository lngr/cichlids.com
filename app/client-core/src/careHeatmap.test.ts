import { expect, it } from "vitest";
import { createCichlidsClient } from "./client.js";
import { HEATMAP_WEEKS, heatmapIntensity, heatmapListItems, layoutHeatmap } from "./careHeatmap.js";
import type { CareHeatmap } from "./careHeatmap.js";

const heatmap = (days: CareHeatmap["days"], end = "2026-10-15"): CareHeatmap => ({
  start: "2026-06-29", end, weeks: 16, days, activeDays: days.length,
  lastWaterChange: days.filter(d => d.waterChanges > 0).map(d => d.date).at(-1) ?? null,
  lastMeasurement: days.filter(d => d.measurements > 0).map(d => d.date).at(-1) ?? null,
});

it("lays the window out as 16 week columns of 7 weekday rows from Monday", () => {
  const layout = layoutHeatmap(heatmap([]));
  expect(layout.columns).toHaveLength(HEATMAP_WEEKS);
  expect(layout.columns.every(column => column.length === 7)).toBe(true);
  expect(layout.columns[0]![0]!.date).toBe("2026-06-29");
  expect(layout.columns[0]![6]!.date).toBe("2026-07-05");
  expect(layout.columns[15]![0]!.date).toBe("2026-10-12");
  expect(layout.columns[15]![3]!.date).toBe("2026-10-15");
});

it("marks the days after today as outside the window", () => {
  const last = layoutHeatmap(heatmap([])).columns[15]!;
  expect(last.map(cell => cell.inWindow)).toEqual([true, true, true, true, false, false, false]);
  expect(last[4]!.date).toBe("2026-10-16");
});

it("puts the counts and the intensity on exactly the cared days", () => {
  const layout = layoutHeatmap(heatmap([
    { date: "2026-07-01", waterChanges: 1, measurements: 0 },
    { date: "2026-10-15", waterChanges: 2, measurements: 3 },
  ]));
  const cells = layout.columns.flat();
  const marked = cells.filter(cell => cell.level > 0);
  expect(marked.map(cell => cell.date)).toEqual(["2026-07-01", "2026-10-15"]);
  expect(marked[0]).toMatchObject({ waterChanges: 1, measurements: 0, level: 1 });
  expect(marked[1]).toMatchObject({ waterChanges: 2, measurements: 3, level: 4 });
  expect(cells.find(cell => cell.date === "2026-07-02")).toMatchObject({ waterChanges: 0, measurements: 0, level: 0 });
});

it("grades the intensity by the total of the day and caps it", () => {
  expect([0, 1, 2, 3, 4, 9].map(heatmapIntensity)).toEqual([0, 1, 2, 3, 4, 4]);
});

it("tells which kinds of care a day holds", () => {
  const cells = layoutHeatmap(heatmap([
    { date: "2026-07-01", waterChanges: 1, measurements: 0 },
    { date: "2026-07-02", waterChanges: 0, measurements: 1 },
    { date: "2026-07-03", waterChanges: 1, measurements: 1 },
  ])).columns.flat();
  const kinds = ["2026-07-01", "2026-07-02", "2026-07-03", "2026-07-04"].map(date => cells.find(cell => cell.date === date)!.kind);
  expect(kinds).toEqual(["water_change", "measurement", "both", "none"]);
});

it("lists the cared days newest first as the fallback for the grid", () => {
  const items = heatmapListItems(heatmap([
    { date: "2026-07-01", waterChanges: 1, measurements: 0 },
    { date: "2026-10-15", waterChanges: 0, measurements: 2 },
  ]));
  expect(items.map(item => item.date)).toEqual(["2026-10-15", "2026-07-01"]);
  expect(items[0]).toMatchObject({ waterChanges: 0, measurements: 2, kind: "measurement" });
});

it("reads the heatmap of a tank and of the own tanks with the local date and offset", async () => {
  const requests: Request[] = [];
  const client = createCichlidsClient({ baseUrl: "http://heatmap.test", getAccessToken: async () => "token", fetch: async request => {
    requests.push(request as Request);
    return Response.json({ start: "2026-06-29", end: "2026-10-15", weeks: 16, days: [{ date: "2026-10-15", waterChanges: 1, measurements: 0 }],
      activeDays: 1, lastWaterChange: "2026-10-15", lastMeasurement: null });
  } });
  const tank = await client.care.heatmap(7, { date: "2026-10-15", utcOffsetMinutes: 120 });
  expect(requests[0]!.url).toBe("http://heatmap.test/api/tanks/7/care/heatmap?date=2026-10-15&utcOffsetMinutes=120");
  expect(tank.days).toEqual([{ date: "2026-10-15", waterChanges: 1, measurements: 0 }]);
  const own = await client.care.ownHeatmap({ date: "2026-10-15" });
  expect(requests[1]!.url).toBe("http://heatmap.test/api/me/care/heatmap?date=2026-10-15");
  expect(own.lastWaterChange).toBe("2026-10-15");
});
