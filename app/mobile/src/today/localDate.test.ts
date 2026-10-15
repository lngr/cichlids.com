import { describe, expect, it } from "vitest";
import { localCalendarDate } from "./localDate";

describe("Today local calendar date", () => {
  it("pads the member's calendar date and preserves leap day", () => {
    expect(localCalendarDate(new Date(2024, 1, 29, 0, 5))).toBe("2024-02-29");
    expect(localCalendarDate(new Date(2026, 9, 15, 23, 55))).toBe("2026-10-15");
  });
  it("uses local calendar fields across a UTC day boundary", () => {
    const date = new Date("2026-10-15T23:30:00Z");
    const previous = process.env.TZ;
    try {
      process.env.TZ = "Asia/Tokyo";
      expect(localCalendarDate(date)).toBe("2026-10-16");
      process.env.TZ = "America/Los_Angeles";
      expect(localCalendarDate(new Date("2026-10-15T00:30:00Z"))).toBe("2026-10-14");
    } finally {
      if (previous === undefined) delete process.env.TZ;
      else process.env.TZ = previous;
    }
  });
});
