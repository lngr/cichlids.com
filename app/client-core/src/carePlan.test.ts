import { describe, expect, it } from "vitest";
import { MAX_RHYTHM_DAYS, MIN_RHYTHM_DAYS, RHYTHM_CHOICES, dueSummary, isValidRhythm, parseRhythm } from "./carePlan.js";

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
