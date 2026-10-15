import { describe, expect, it } from "vitest";
import { standardTanks, standardTankRequest, selectableRegions, interestRegions } from "./standardTanks";

describe("onboarding tank choices", () => {
  it("supplies the market dimensions and original US volume for a 55 gallon tank", () => {
    const tank = standardTanks.find(t => t.id === "us-55")!;
    expect(standardTankRequest(tank, "Malawi", "lake_malawi")).toEqual({
      title: "Malawi", region: "lake_malawi", volume: { value: 55, unit: "us_gallon" },
      dimensions: { width: 48, height: 21, depth: 13, unit: "inch" },
    });
    expect(standardTanks.filter(t => t.system === "us").map(t => t.volume)).toEqual([10, 20, 29, 40, 55, 75, 90, 125]);
  });
  it("keeps European common market dimensions distinct from nominal volume", () => {
    const tank = standardTanks.find(t => t.id === "metric-240")!;
    expect(standardTankRequest(tank, "Tanganyika", "lake_tanganyika")).toEqual({
      title: "Tanganyika", region: "lake_tanganyika", volume: { value: 240, unit: "liter" },
      dimensions: { width: 120, height: 40, depth: 50, unit: "cm" },
    });
    expect(standardTanks.filter(t => t.system === "metric")).toHaveLength(8);
  });
  it("reserves unassigned Americas for legacy tanks and excludes mixed from interests", () => {
    expect(selectableRegions).not.toContain("americas_unassigned");
    expect(selectableRegions).toContain("mixed");
    expect(interestRegions).not.toContain("mixed");
    expect(interestRegions).toHaveLength(6);
  });
});
