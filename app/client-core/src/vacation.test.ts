import { describe, expect, it } from "vitest";
import { checkVacationDates } from "./vacation.js";

describe("checkVacationDates", () => {
  const today = "2026-10-15";
  it.each([
    ["2026-09-01", "2026-09-14", "ok"],
    ["2026-09-01", "2026-09-01", "ok"],
    ["2026-10-15", "2026-10-15", "ok"],
    ["2026-09-14", "2026-09-01", "order"],
    ["2026-10-16", "2026-10-16", "future"],
    ["2026-09-01", "2026-10-16", "future"],
    ["2026-9-1", "2026-09-14", "invalid"],
    ["2026-02-30", "2026-03-01", "invalid"],
    ["", "2026-09-14", "invalid"],
    ["2026-09-01", "soon", "invalid"],
    [" 2026-09-01 ", "2026-09-14", "ok"],
  ] as const)("checks %j to %j", (start, end, expected) => {
    expect(checkVacationDates(start, end, today)).toBe(expected);
  });
});
