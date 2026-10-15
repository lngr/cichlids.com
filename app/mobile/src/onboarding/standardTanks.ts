import type { CreateTankRequest } from "@cichlids/client-core";
import type { UnitSystem } from "../units/format";

export const selectableRegions = ["lake_malawi", "lake_tanganyika", "lake_victoria", "africa_other", "central_america", "south_america", "mixed"] as const;
export type SelectableRegion = typeof selectableRegions[number];
export const interestRegions = selectableRegions.filter(region => region !== "mixed");
export type StandardTank = { id: string; system: UnitSystem; volume: number; width: number; depth: number; height: number; variant?: "long" | "breeder" };

// Common market sizes; nominal volume and length x depth x height describe the sold tank size.
export const standardTanks: readonly StandardTank[] = [
  { id: "us-10", system: "us", volume: 10, width: 20, depth: 10, height: 12 },
  { id: "us-20-long", system: "us", volume: 20, width: 30, depth: 12, height: 12, variant: "long" },
  { id: "us-29", system: "us", volume: 29, width: 30, depth: 12, height: 18 },
  { id: "us-40-breeder", system: "us", volume: 40, width: 36, depth: 18, height: 16, variant: "breeder" },
  { id: "us-55", system: "us", volume: 55, width: 48, depth: 13, height: 21 },
  { id: "us-75", system: "us", volume: 75, width: 48, depth: 18, height: 21 },
  { id: "us-90", system: "us", volume: 90, width: 48, depth: 18, height: 24 },
  { id: "us-125", system: "us", volume: 125, width: 72, depth: 18, height: 21 },
  { id: "metric-54", system: "metric", volume: 54, width: 60, depth: 30, height: 30 },
  { id: "metric-112", system: "metric", volume: 112, width: 80, depth: 35, height: 40 },
  { id: "metric-160", system: "metric", volume: 160, width: 100, depth: 40, height: 40 },
  { id: "metric-240", system: "metric", volume: 240, width: 120, depth: 50, height: 40 },
  { id: "metric-375", system: "metric", volume: 375, width: 150, depth: 50, height: 50 },
  { id: "metric-450", system: "metric", volume: 450, width: 150, depth: 60, height: 50 },
  { id: "metric-540", system: "metric", volume: 540, width: 180, depth: 60, height: 50 },
  { id: "metric-720", system: "metric", volume: 720, width: 200, depth: 60, height: 60 },
];

export function standardTankRequest(tank: StandardTank, title: string, region: SelectableRegion): CreateTankRequest {
  return {
    title, region,
    volume: { value: tank.volume, unit: tank.system === "us" ? "us_gallon" : "liter" },
    dimensions: { width: tank.width, depth: tank.depth, height: tank.height, unit: tank.system === "us" ? "inch" : "cm" },
  };
}
