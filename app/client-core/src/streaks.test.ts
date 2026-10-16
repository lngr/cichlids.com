import { describe, expect, it } from "vitest";
import {
  activeVacation, daysUntilDeadline, hasStreakHistory, seriesPhotoKeepsStreak, toMeStreaks, toTankStreaks, toVacationShare,
} from "./streaks.js";
import type { TimeLapseStreak, WaterChangeStreak } from "./streaks.js";

const water = (patch: Partial<WaterChangeStreak> = {}): WaterChangeStreak => ({
  state: "running", current: 0, currentVerified: 0, longest: 0, lastWaterChange: null, deadline: null, freezesLeft: null, freezesPerYear: null, ...patch,
});
const timeLapse = (patch: Partial<TimeLapseStreak> = {}): TimeLapseStreak => ({
  current: 0, longest: 0, thisWeekHasPhoto: false, thisWeekOpen: true, weekStart: "2026-10-12", weekEnd: "2026-10-18", paused: null, keepingPhotoIds: null, ...patch,
});

describe("normalizing the API answers", () => {
  it("turns the numeric strings and the id lists of a tank answer into numbers", () => {
    const streaks = toTankStreaks({
      tankId: "7",
      waterChange: { state: "running", current: "3", currentVerified: 2, longest: "5", lastWaterChange: "2026-10-14", deadline: "2026-10-24", freezesLeft: "1", freezesPerYear: 2 },
      timeLapse: { current: 4, longest: "4", thisWeekHasPhoto: true, thisWeekOpen: false, weekStart: "2026-10-12", weekEnd: "2026-10-18", paused: false, keepingPhotoIds: ["11", 12] },
      vacations: [{ id: "3", startDate: "2026-09-01", endDate: "2026-09-10", active: false }],
    });
    expect(streaks.tankId).toBe(7);
    expect(streaks.waterChange).toMatchObject({ current: 3, currentVerified: 2, longest: 5, freezesLeft: 1, freezesPerYear: 2 });
    expect(streaks.timeLapse).toMatchObject({ current: 4, longest: 4, keepingPhotoIds: [11, 12] });
    expect(streaks.vacations).toEqual([{ id: 3, startDate: "2026-09-01", endDate: "2026-09-10", active: false }]);
  });

  it("keeps the parts a viewer does not see as null", () => {
    const streaks = toTankStreaks({
      tankId: 7, waterChange: null,
      timeLapse: { current: 1, longest: 1, thisWeekHasPhoto: true, thisWeekOpen: false, weekStart: "2026-10-12", weekEnd: "2026-10-18", paused: null, keepingPhotoIds: null },
      vacations: [],
    });
    expect(streaks.waterChange).toBeNull();
    expect(streaks.timeLapse?.paused).toBeNull();
    expect(streaks.timeLapse?.keepingPhotoIds).toBeNull();
  });

  it("normalizes the overview of the member", () => {
    const overview = toMeStreaks({
      enabled: true,
      freezesPerYear: "2",
      vacations: [{ id: 1, startDate: "2026-10-10", endDate: null, active: true }],
      tanks: [{
        tankId: "5", title: "Reef",
        waterChange: { state: "paused", current: 1, currentVerified: 1, longest: 1, lastWaterChange: "2026-10-01", deadline: null, freezesLeft: 2, freezesPerYear: 2 },
        timeLapse: { current: 0, longest: 0, thisWeekHasPhoto: false, thisWeekOpen: false, weekStart: "2026-10-12", weekEnd: "2026-10-18", paused: true, keepingPhotoIds: [] },
      }],
    });
    expect(overview.enabled).toBe(true);
    expect(overview.freezesPerYear).toBe(2);
    expect(overview.tanks[0]).toMatchObject({ tankId: 5, title: "Reef" });
    expect(activeVacation(overview.vacations)?.startDate).toBe("2026-10-10");
  });

  it("normalizes the overview of a member with streaks off", () => {
    const overview = toMeStreaks({ enabled: false, freezesPerYear: 2, vacations: [], tanks: [] });
    expect(overview).toEqual({ enabled: false, freezesPerYear: 2, vacations: [], tanks: [] });
  });

  it("normalizes the vacation share of a profile", () => {
    expect(toVacationShare({ vacationDays: "10", windowDays: 100, fraction: "0.1", pastVacationCount: 2 })).toEqual({ vacationDays: 10, windowDays: 100, fraction: 0.1, pastVacationCount: 2 });
  });
});

describe("activeVacation", () => {
  it("finds the vacation without an end", () => {
    expect(activeVacation([{ id: 1, startDate: "2026-09-01", endDate: "2026-09-02", active: false }])).toBeNull();
    expect(activeVacation([])).toBeNull();
  });
});

describe("daysUntilDeadline", () => {
  it.each([["2026-10-24", "2026-10-15", 9], ["2026-10-15", "2026-10-15", 0], ["2026-10-14", "2026-10-15", -1], ["2027-01-02", "2026-12-30", 3]])(
    "counts from %s to %s", (deadline, today, expected) => { expect(daysUntilDeadline(deadline, today)).toBe(expected); });
});

describe("seriesPhotoKeepsStreak", () => {
  it("is true only for the owner's photo that is the sole photo of a week of the run", () => {
    expect(seriesPhotoKeepsStreak(timeLapse({ keepingPhotoIds: [4, 9] }), 9)).toBe(true);
    expect(seriesPhotoKeepsStreak(timeLapse({ keepingPhotoIds: [4, 9] }), 5)).toBe(false);
    expect(seriesPhotoKeepsStreak(timeLapse({ keepingPhotoIds: null }), 9)).toBe(false);
    expect(seriesPhotoKeepsStreak(null, 9)).toBe(false);
  });
});

describe("hasStreakHistory", () => {
  it("tells whether a tank has anything to show", () => {
    expect(hasStreakHistory({ waterChange: water({ state: "no_history" }), timeLapse: timeLapse() })).toBe(false);
    expect(hasStreakHistory({ waterChange: water({ state: "running", lastWaterChange: "2026-10-01" }), timeLapse: timeLapse() })).toBe(true);
    expect(hasStreakHistory({ waterChange: water({ state: "no_history" }), timeLapse: timeLapse({ longest: 2 }) })).toBe(true);
  });
});
