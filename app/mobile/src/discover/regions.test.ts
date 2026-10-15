import { describe, expect, it } from "vitest";
import { orderDiscoverRegions } from "./regions";

describe("Discover regions", () => {
  it("puts member interests first and includes every selectable region once", () => {
    expect(orderDiscoverRegions(["south_america", "lake_malawi", "south_america", "invalid"])).toEqual([
      "south_america", "lake_malawi", "lake_tanganyika", "lake_victoria", "africa_other", "central_america", "mixed",
    ]);
    expect(orderDiscoverRegions([])).toHaveLength(7);
  });
});
