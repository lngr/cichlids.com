import { expect, it } from "vitest";
import { toMetric, fromMetric, formatMeasurement, unitsForRegion } from "./format";

it("uses the country independently of the language", () => {
  expect(unitsForRegion("US")).toBe("us");
  for (const region of ["GB", "DE", "CA", null, undefined]) expect(unitsForRegion(region)).toBe("metric");
});
it("converts US liquid gallons, inches, Fahrenheit and hobby hardness", () => {
  expect(toMetric(55, "volume", "us")).toBeCloseTo(208.19764812, 10);
  expect(toMetric(48.125, "length", "us")).toBe(122.2375);
  expect(toMetric(77, "temperature", "us")).toBe(25);
  expect(toMetric(178.48, "hardness", "us")).toBeCloseTo(10, 12);
  for (const kind of ["volume", "length", "temperature", "hardness"] as const) {
    expect(toMetric(12.345, kind, "metric")).toBe(12.345);
    expect(fromMetric(toMetric(12.345, kind, "us"), kind, "us")).toBeCloseTo(12.345, 10);
  }
});
it("formats in the viewer's units and language and preserves the owner's input precision", () => {
  const input = { value: 55, system: "us" as const };
  expect(formatMeasurement(208.19764812, "volume", "metric", "de", input, false)).toBe("208,2 L");
  expect(formatMeasurement(208.19764812, "volume", "metric", "en", input, true)).toBe("55 US gal");
  expect(formatMeasurement(0, "temperature", "us", "en")).toBe("32 °F");
  expect(formatMeasurement(10, "hardness", "us", "en")).toBe("178.48 ppm");
  expect(formatMeasurement(122.2375, "length", "metric", "en", { value: 48.123456789, system: "us" }, true)).toBe("48.123456789 in");
});
