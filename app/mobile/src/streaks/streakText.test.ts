import i18next, { type TFunction } from "i18next";
import { beforeAll, describe, expect, it } from "vitest";
import type { TimeLapseStreak, Vacation, WaterChangeStreak } from "@cichlids/client-core";
import { TRANSLATIONS } from "../i18n/catalogues";
import {
  formatLocalDate, freezesText, longestText, pastVacationLines, timeLapseHeadline, timeLapseStatus, vacationShareText,
  verifiedText, waterChangeHeadline, waterChangeStatus,
} from "./streakText";

const water = (patch: Partial<WaterChangeStreak> = {}): WaterChangeStreak => ({
  state: "running", current: 3, currentVerified: 2, longest: 5, lastWaterChange: "2026-10-14", deadline: "2026-10-24",
  freezesLeft: 2, freezesPerYear: 2, ...patch,
});
const timeLapse = (patch: Partial<TimeLapseStreak> = {}): TimeLapseStreak => ({
  current: 3, longest: 3, thisWeekHasPhoto: false, thisWeekOpen: true, weekStart: "2026-10-12", weekEnd: "2026-10-18",
  paused: false, keepingPhotoIds: [], ...patch,
});
let en: TFunction;
let de: TFunction;
beforeAll(async () => {
  en = (await i18next.createInstance().init({ lng: "en", resources: { en: { translation: TRANSLATIONS.en } } })) as TFunction;
  de = (await i18next.createInstance().init({ lng: "de", resources: { de: { translation: TRANSLATIONS.de } } })) as TFunction;
});

describe("dates", () => {
  it("formats a local calendar date in the language without moving it to another day", () => {
    expect(formatLocalDate("2026-10-24", "en")).toBe("Oct 24, 2026");
    expect(formatLocalDate("2026-10-24", "de")).toBe("24. Okt. 2026");
    expect(formatLocalDate("2026-01-01", "en")).toBe("Jan 1, 2026");
  });
});

describe("time-lapse wording", () => {
  it("counts the weeks in a row", () => {
    expect(timeLapseHeadline(timeLapse(), en)).toBe("Weeks in a row: 3");
    expect(timeLapseHeadline(timeLapse(), de)).toBe("Wochen in Folge: 3");
    expect(timeLapseHeadline(timeLapse({ current: 0 }), en)).toBe("No streak yet");
  });
  it("says what this week needs without pressure", () => {
    expect(timeLapseStatus(timeLapse(), en, "en")).toBe("A photo by Oct 18, 2026 keeps it going.");
    expect(timeLapseStatus(timeLapse({ current: 0 }), en, "en")).toBe("A photo by Oct 18, 2026 starts a streak.");
    expect(timeLapseStatus(timeLapse({ thisWeekHasPhoto: true, thisWeekOpen: false }), en, "en")).toBe("This week's photo is in.");
    expect(timeLapseStatus(timeLapse({ paused: true, thisWeekOpen: false }), en, "en")).toBe("Paused while you are on vacation.");
  });
  it("has no status for a viewer whose week is open and for a week that does not need a photo", () => {
    expect(timeLapseStatus(timeLapse({ thisWeekOpen: false }), en, "en")).toBeNull();
  });
  it("shows the best run only when it beats the current one", () => {
    expect(longestText(5, 3, en)).toBe("Best so far: 5");
    expect(longestText(3, 3, en)).toBeNull();
  });
});

describe("water change wording", () => {
  it("counts the periods in a row and marks the verified ones", () => {
    expect(waterChangeHeadline(water(), en)).toBe("Periods in a row: 3");
    expect(waterChangeHeadline(water({ state: "no_history", current: 0 }), en)).toBe("No streak yet");
    expect(verifiedText(water(), en)).toBe("Verified by the guided mode: 2 of 3");
    expect(verifiedText(water({ currentVerified: 0 }), en)).toBeNull();
  });
  it("names the next deadline by date and says today on the last day", () => {
    expect(waterChangeStatus(water(), "2026-10-15", en, "en")).toBe("Next water change by Oct 24, 2026");
    expect(waterChangeStatus(water({ deadline: "2026-10-15" }), "2026-10-15", en, "en")).toBe("Next water change: today at the latest");
  });
  it("explains a paused, empty or ended streak calmly", () => {
    expect(waterChangeStatus(water({ state: "paused", deadline: null }), "2026-10-15", en, "en")).toBe("Paused while you are on vacation.");
    expect(waterChangeStatus(water({ state: "no_history", current: 0, deadline: null }), "2026-10-15", en, "en")).toBe("Your streak starts with your next water change.");
    expect(waterChangeStatus(water({ state: "broken", current: 0, deadline: null }), "2026-10-15", en, "en")).toBe("The last streak ended. Your next water change starts a new one.");
  });
  it("has no deadline line for a viewer", () => {
    expect(waterChangeStatus(water({ deadline: null, freezesLeft: null }), "2026-10-15", en, "en")).toBeNull();
  });
  it("tells the owner how many freezes are left", () => {
    expect(freezesText(water(), en)).toBe("Freezes left this year: 2");
    expect(freezesText(water({ freezesLeft: null }), en)).toBeNull();
  });
});

describe("vacations", () => {
  const vacation = (start: string, end: string | null): Vacation => ({ id: 1, startDate: start, endDate: end, active: end === null });
  it("lists the newest ended vacations and skips the active one", () => {
    const lines = pastVacationLines([vacation("2026-08-01", "2026-08-09"), vacation("2026-10-10", null), vacation("2026-09-01", "2026-09-05")], en, "en", 5);
    expect(lines).toEqual(["Sep 1, 2026 to Sep 5, 2026", "Aug 1, 2026 to Aug 9, 2026"]);
    expect(pastVacationLines([vacation("2026-08-01", "2026-08-09"), vacation("2026-09-01", "2026-09-05")], en, "en", 1)).toEqual(["Sep 1, 2026 to Sep 5, 2026"]);
  });
  it("states the share as a percentage of the last year or since joining", () => {
    expect(vacationShareText({ vacationDays: 18, windowDays: 365, fraction: 18 / 365, pastVacationCount: 2 }, en, "en")).toBe("Time on vacation, last 12 months: 5%");
    expect(vacationShareText({ vacationDays: 5, windowDays: 100, fraction: 0.05, pastVacationCount: 1 }, en, "en")).toBe("Time on vacation since joining: 5%");
    expect(vacationShareText({ vacationDays: 0, windowDays: 100, fraction: 0, pastVacationCount: 0 }, en, "en")).toBeNull();
    expect(vacationShareText({ vacationDays: 18, windowDays: 365, fraction: 18 / 365, pastVacationCount: 2 }, de, "de")).toBe("Zeit im Urlaub, letzte 12 Monate: 5\u00a0%");
  });
});
