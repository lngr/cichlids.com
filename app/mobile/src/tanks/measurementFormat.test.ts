import { describe, expect, it } from "vitest";
import { API_ERROR_CODES } from "../lib/apiErrors";
import { TRANSLATIONS } from "../i18n/catalogues";
import { SUPPORTED_LANGUAGES } from "../i18n/locales";
import { chartSegments, formatMeasurementValue, measurementUnitSymbol } from "./measurementFormat";

describe("measurement formatting", () => {
  it("formats values with the unit symbol of the language", () => {
    expect(formatMeasurementValue(77, "fahrenheit", "en")).toBe("77 °F");
    expect(formatMeasurementValue(25.5, "celsius", "de")).toBe("25,5 °C");
    expect(formatMeasurementValue(20, "mg_per_liter", "en")).toBe("20 mg/l");
    expect(formatMeasurementValue(550, "microsiemens_per_cm", "en")).toBe("550 µS/cm");
    expect(formatMeasurementValue(53.544, "ppm", "en")).toBe("53.54 ppm");
    expect(formatMeasurementValue(3, "dkh", "en")).toBe("3 °dKH");
  });
  it("shows pH without a unit", () => {
    expect(measurementUnitSymbol("none", "en")).toBe("");
    expect(formatMeasurementValue(7.2, "none", "en")).toBe("7.2");
    expect(formatMeasurementValue(7.2, "none", "de")).toBe("7,2");
  });
  it("has a symbol for every unit and a name for every parameter and origin in every language", () => {
    for (const language of SUPPORTED_LANGUAGES) {
      const measure = TRANSLATIONS[language].care.measure;
      for (const unit of ["celsius", "fahrenheit", "dgh", "dkh", "ppm", "mg_per_liter", "microsiemens_per_cm"]) expect(measure.units[unit as keyof typeof measure.units], `${language} ${unit}`).toBeTruthy();
      for (const parameter of ["temperature", "ph", "kh", "gh", "no2", "no3", "nh4", "po4", "conductivity", "tds"]) expect(measure.parameters[parameter as keyof typeof measure.parameters], `${language} ${parameter}`).toBeTruthy();
      for (const origin of ["test_kit", "meter", "probe"]) expect(measure.origins[origin as keyof typeof measure.origins], `${language} ${origin}`).toBeTruthy();
    }
  });
  it("translates every API error code in every language", () => {
    for (const language of SUPPORTED_LANGUAGES) {
      const errors = TRANSLATIONS[language].errors as Record<string, string>;
      for (const code of API_ERROR_CODES) expect(errors[code], `${language} ${code}`).toBeTruthy();
    }
    expect(API_ERROR_CODES).toEqual(expect.arrayContaining(["care_measurement_invalid", "care_parameter_invalid", "care_unit_invalid", "care_value_invalid", "care_origin_invalid", "care_range_invalid"]));
  });
});

describe("chart segments", () => {
  it("connects consecutive points with a centered, rotated bar", () => {
    const [flat, rising] = chartSegments([{ entryId: 1, x: 0, y: 20 }, { entryId: 2, x: 30, y: 20 }, { entryId: 3, x: 60, y: 0 }]);
    expect(flat).toEqual({ key: "1-2", left: 15 - 15, top: 20, length: 30, angle: 0 });
    expect(rising.key).toBe("2-3");
    expect(rising.length).toBeCloseTo(Math.hypot(30, 20), 6);
    expect(rising.angle).toBeCloseTo(Math.atan2(-20, 30), 6);
    expect(rising.left).toBeCloseTo(45 - rising.length / 2, 6);
    expect(rising.top).toBeCloseTo(10, 6);
  });
  it("has no segment for fewer than two points", () => {
    expect(chartSegments([])).toEqual([]);
    expect(chartSegments([{ entryId: 1, x: 5, y: 5 }])).toEqual([]);
  });
});
