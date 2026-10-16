import { describe, expect, it } from "vitest";
import type { Decommissioning, TankOperation } from "@cichlids/client-core";
import { activeLines, imageOf, operatingLines, pastLine } from "./decommissionText";

const t = (key: string, options?: Record<string, unknown>) => `${key}${options ? JSON.stringify(options) : ""}`;
const operation = (over: Partial<TankOperation> = {}): TankOperation => ({
  tankId: 7, operatingDays: 20, operatingMonths: 0, decommissionedDays: 0, decommissioned: false, items: [], ...over,
});
const item = (over: Partial<Decommissioning> = {}): Decommissioning => ({
  id: 1, scope: "all_care", startDate: "2026-07-15", endDate: "2026-08-15", active: false, startImage: null, endImage: null, ...over,
});

describe("operatingLines", () => {
  it("names the operating time in the unit that fits and leaves out a decommissioned time of zero", () => {
    expect(operatingLines(operation(), t)).toEqual(['decommission.days{"count":20}']);
    expect(operatingLines(operation({ operatingMonths: 7, operatingDays: 212 }), t)).toEqual(['decommission.months{"count":7}']);
    expect(operatingLines(operation({ operatingMonths: 30, operatingDays: 900 }), t)).toEqual(['decommission.years{"count":2}']);
  });

  it("adds the decommissioned time when there is one", () => {
    expect(operatingLines(operation({ operatingMonths: 7, operatingDays: 212, decommissionedDays: 31 }), t))
      .toEqual(['decommission.months{"count":7}', 'decommission.rest{"count":31}']);
  });
});

describe("activeLines", () => {
  it("names the day since which the tank is decommissioned and what is paused", () => {
    expect(activeLines(item({ active: true, endDate: null, scope: "all_care" }), t, "en"))
      .toEqual(['decommission.since{"date":"Jul 15, 2026"}', "decommission.statusAll"]);
    expect(activeLines(item({ active: true, endDate: null, scope: "water_change" }), t, "en")[1]).toBe("decommission.statusWater");
  });
});

describe("pastLine", () => {
  it("names the first and the last day of an ended decommissioning", () => {
    expect(pastLine(item(), t, "en")).toBe('decommission.pastLine{"from":"Jul 15, 2026","to":"Aug 15, 2026"}');
  });
});

describe("imageOf", () => {
  it("takes the most suitable size of an image and nothing when there is none", () => {
    expect(imageOf(null)).toBeNull();
    expect(imageOf({ thumb: "t", small: "s", medium: "m", large: "l" })).toBe("s");
    expect(imageOf({ thumb: "t", small: null, medium: null, large: null })).toBe("t");
    expect(imageOf({ thumb: null, small: null, medium: null, large: null })).toBeNull();
  });
});
