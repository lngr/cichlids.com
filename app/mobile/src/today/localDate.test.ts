import { describe, expect, it } from "vitest";
import { localCalendarDate, localTime } from "./localDate";

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

describe("local time for the care plan", () => {
  it("sends the local calendar date and the clock offset from UTC in minutes", () => {
    const previous = process.env.TZ;
    try {
      process.env.TZ = "Asia/Tokyo";
      expect(localTime(new Date("2026-10-15T23:30:00Z"))).toEqual({ date: "2026-10-16", utcOffsetMinutes: 540 });
      process.env.TZ = "America/Los_Angeles";
      expect(localTime(new Date("2026-10-15T00:30:00Z"))).toEqual({ date: "2026-10-14", utcOffsetMinutes: -420 });
      process.env.TZ = "UTC";
      expect(localTime(new Date("2026-10-15T12:00:00Z"))).toEqual({ date: "2026-10-15", utcOffsetMinutes: 0 });
    } finally {
      if (previous === undefined) delete process.env.TZ;
      else process.env.TZ = previous;
    }
  });
});
