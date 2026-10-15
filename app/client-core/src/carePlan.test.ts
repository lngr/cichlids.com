import { describe, expect, it } from "vitest";
import type { DueCareItem } from "./client.js";
import { MAX_RHYTHM_DAYS, MIN_RHYTHM_DAYS, RHYTHM_CHOICES, dueCareForToday, dueSummary, isValidRhythm, parseRhythm } from "./carePlan.js";

describe("rhythm", () => {
  it("offers the usual choices", () => {
    expect(RHYTHM_CHOICES).toEqual([3, 5, 7, 10, 14, 21, 28]);
    expect([MIN_RHYTHM_DAYS, MAX_RHYTHM_DAYS]).toEqual([1, 90]);
  });
  it.each([[1, true], [90, true], [0, false], [91, false], [2.5, false], [Number.NaN, false]])("validates %s", (days, valid) => {
    expect(isValidRhythm(days)).toBe(valid);
  });
  it.each([["14", 14], [" 7 ", 7], ["1", 1], ["90", 90], ["0", null], ["91", null], ["", null], ["3.5", null], ["abc", null], ["1e1", null], ["-4", null]])(
    "parses %j", (text, days) => { expect(parseRhythm(text)).toBe(days); });
});

describe("dueSummary", () => {
  it.each([
    [{ state: "due_in", days: 3, dueDate: "2026-10-18" }, { key: "dueIn", count: 3 }],
    [{ state: "due_today", days: 0, dueDate: "2026-10-15" }, { key: "dueToday", count: 0 }],
    [{ state: "overdue", days: 2, dueDate: "2026-10-13" }, { key: "overdue", count: 2 }],
    [{ state: "never_logged", days: 0, dueDate: null }, { key: "neverLogged", count: 0 }],
  ] as const)("maps %j", (due, expected) => { expect(dueSummary(due)).toEqual(expected); });
});

describe("dueCareForToday", () => {
  const item = (tankId: number, kind: "water_change" | "measurement", state: "due_in" | "due_today" | "overdue" | "never_logged", days: number): DueCareItem => ({
    tankId, tankTitle: `Tank ${tankId}`, kind, rhythmDays: 7, reason: { code: "cichlid_default", parameters: {} },
    due: { state, days, dueDate: null },
  });

  it("keeps overdue and due today first and the care due within two days after them", () => {
    const shown = dueCareForToday([
      item(1, "water_change", "due_in", 2), item(2, "water_change", "due_today", 0), item(3, "water_change", "overdue", 1),
      item(4, "water_change", "due_in", 1), item(5, "water_change", "overdue", 4),
    ]);
    expect(shown.map(entry => entry.tankId)).toEqual([5, 3, 2, 4, 1]);
  });

  it("leaves out care that is due later and rules that were never logged", () => {
    const shown = dueCareForToday([item(1, "water_change", "due_in", 3), item(2, "measurement", "never_logged", 0), item(3, "measurement", "due_in", 2)]);
    expect(shown.map(entry => entry.tankId)).toEqual([3]);
  });

  it("keeps the order of the list among equal items and returns nothing when nothing is due", () => {
    expect(dueCareForToday([item(2, "measurement", "overdue", 1), item(1, "water_change", "overdue", 1)]).map(entry => entry.kind)).toEqual(["measurement", "water_change"]);
    expect(dueCareForToday([])).toEqual([]);
  });
});
