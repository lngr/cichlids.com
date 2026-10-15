import { createActor, waitFor } from "xstate";
import { describe, expect, it, vi } from "vitest";
import {
  COMMON_PARAMETERS, MEASUREMENT_PARAMETERS, createDraft, editDraft, fromCanonical, inputUnit, measurementMachine,
  scaleSeries, seriesForDisplay, setField, setOrigin, showMore, validateDraft, visibleParameters,
} from "./measurement.js";
import type { CareEntry } from "./client.js";

describe("units follow the member's system", () => {
  it("enters temperature in Fahrenheit and hardness in ppm for the US system only", () => {
    expect(inputUnit("temperature", "us")).toBe("fahrenheit");
    expect(inputUnit("temperature", "metric")).toBe("celsius");
    expect(inputUnit("kh", "us")).toBe("ppm");
    expect(inputUnit("gh", "us")).toBe("ppm");
    expect(inputUnit("kh", "metric")).toBe("dkh");
    expect(inputUnit("gh", "metric")).toBe("dgh");
    expect(inputUnit("ph", "us")).toBe("none");
    expect(inputUnit("no3", "us")).toBe("mg_per_liter");
    expect(inputUnit("conductivity", "metric")).toBe("microsiemens_per_cm");
    expect(inputUnit("tds", "metric")).toBe("ppm");
  });
  it("converts canonical values into the entry unit", () => {
    expect(fromCanonical("temperature", 25, "fahrenheit")).toBe(77);
    expect(fromCanonical("temperature", 25, "celsius")).toBe(25);
    expect(fromCanonical("kh", 3, "ppm")).toBeCloseTo(53.544, 6);
    expect(fromCanonical("gh", 3, "dgh")).toBe(3);
    expect(fromCanonical("no3", 12.5, "mg_per_liter")).toBe(12.5);
  });
});

describe("measurement draft", () => {
  it("shows pH, nitrate and temperature first and every parameter on demand", () => {
    expect(COMMON_PARAMETERS).toEqual(["ph", "no3", "temperature"]);
    expect(MEASUREMENT_PARAMETERS).toHaveLength(10);
    const draft = createDraft("metric");
    expect(visibleParameters(draft)).toEqual(["ph", "no3", "temperature"]);
    expect(visibleParameters(showMore(draft))).toEqual(MEASUREMENT_PARAMETERS);
  });
  it("builds a request from the filled fields in the member's units with localized decimals", () => {
    let draft = createDraft("us");
    draft = setField(draft, "ph", "7,2");
    draft = setField(draft, "temperature", "77");
    draft = setField(draft, "no3", "  ");
    draft = setOrigin(draft, "test_kit");
    expect(validateDraft(draft)).toEqual({
      values: [
        { parameter: "ph", value: 7.2, unit: "none", origin: "test_kit" },
        { parameter: "temperature", value: 77, unit: "fahrenheit", origin: "test_kit" },
      ],
      errors: {},
    });
  });
  it("leaves the origin out when none is chosen", () => {
    const { values } = validateDraft(setField(createDraft("metric"), "no3", "20"));
    expect(values).toEqual([{ parameter: "no3", value: 20, unit: "mg_per_liter", origin: null }]);
  });
  it("refuses an empty draft, unparsable text and values outside the plausible range", () => {
    expect(validateDraft(createDraft("metric"))).toEqual({ values: null, errors: {} });
    for (const text of ["abc", "1e3", "-1", "1,2,3"]) {
      expect(validateDraft(setField(createDraft("metric"), "ph", text)).errors).toEqual({ ph: "invalid" });
    }
    expect(validateDraft(setField(createDraft("metric"), "ph", "14.5"))).toEqual({ values: null, errors: { ph: "range" } });
    expect(validateDraft(setField(createDraft("us"), "temperature", "120")).errors).toEqual({ temperature: "range" });
    expect(validateDraft(setField(createDraft("metric"), "temperature", "120")).errors).toEqual({ temperature: "range" });
    expect(validateDraft(setField(createDraft("us"), "temperature", "113")).errors).toEqual({});
    const mixed = validateDraft(setField(setField(createDraft("metric"), "ph", "7"), "no3", "x"));
    expect(mixed.values).toBeNull();
    expect(mixed.errors).toEqual({ no3: "invalid" });
  });
  it("expands automatically for a value outside the common parameters when editing", () => {
    const entry = {
      id: 4, tankId: 1, authorProfileId: 1, kind: "measurement" as const, occurredAt: "2026-10-01T10:00:00Z", recordedAt: "2026-10-01T10:00:00Z", amount: null,
      values: [
        { parameter: "ph" as const, value: 7, unit: "none" as const, normalizedValue: 7, canonicalUnit: "none" as const, origin: "meter" as const },
        { parameter: "kh" as const, value: 3, unit: "dkh" as const, normalizedValue: 3, canonicalUnit: "dkh" as const, origin: "meter" as const },
        { parameter: "temperature" as const, value: 77, unit: "fahrenheit" as const, normalizedValue: 25, canonicalUnit: "celsius" as const, origin: null },
      ],
    } satisfies CareEntry;
    const metric = editDraft("metric", entry);
    expect(metric.expanded).toBe(true);
    expect(metric.origin).toBe("meter");
    expect(metric.fields).toEqual({ ph: "7", kh: "3", temperature: "25" });
    const us = editDraft("us", entry);
    expect(us.fields.temperature).toBe("77");
    expect(us.fields.kh).toBe("53.544");
  });
});

describe("series for display", () => {
  const series = {
    parameter: "temperature" as const, unit: "celsius" as const,
    points: [
      { entryId: 1, occurredAt: "2026-10-01T10:00:00Z", value: 24, inputValue: 24, inputUnit: "celsius" as const, origin: null },
      { entryId: 2, occurredAt: "2026-10-02T10:00:00Z", value: 25, inputValue: 77, inputUnit: "fahrenheit" as const, origin: "probe" as const },
    ],
  };
  it("converts canonical points to the member's unit and keeps their order", () => {
    const us = seriesForDisplay(series, "us");
    expect(us.unit).toBe("fahrenheit");
    expect(us.points.map(p => p.value)).toEqual([75.2, 77]);
    expect(us.points.map(p => p.entryId)).toEqual([1, 2]);
    expect(seriesForDisplay(series, "metric").points.map(p => p.value)).toEqual([24, 25]);
  });
  it("places points left to right by time and bottom to top by value", () => {
    const scaled = scaleSeries(seriesForDisplay(series, "metric").points, 100, 40);
    expect(scaled[0].x).toBeLessThan(scaled[1].x);
    expect(scaled[0].y).toBeGreaterThan(scaled[1].y);
    expect(Math.min(...scaled.map(p => p.x))).toBeGreaterThanOrEqual(0);
    expect(Math.max(...scaled.map(p => p.x))).toBeLessThanOrEqual(100);
    expect(Math.min(...scaled.map(p => p.y))).toBeGreaterThanOrEqual(0);
    expect(Math.max(...scaled.map(p => p.y))).toBeLessThanOrEqual(40);
  });
  it("centers a single or constant point and returns nothing for an empty series", () => {
    const single = scaleSeries([{ entryId: 1, occurredAt: "2026-10-01T10:00:00Z", value: 7 }], 100, 40);
    expect(single).toEqual([{ entryId: 1, x: 50, y: 20 }]);
    const flat = scaleSeries([
      { entryId: 1, occurredAt: "2026-10-01T10:00:00Z", value: 7 }, { entryId: 2, occurredAt: "2026-10-02T10:00:00Z", value: 7 }], 100, 40);
    expect(flat.map(p => p.y)).toEqual([20, 20]);
    expect(scaleSeries([], 100, 40)).toEqual([]);
  });
});

describe("measurement machine", () => {
  const saved = { id: 9 } as CareEntry;
  const effects = () => ({ log: vi.fn(async () => saved), update: vi.fn(async () => saved), remove: vi.fn(async () => saved) });
  it("logs the validated values and returns to idle", async () => {
    const fx = effects();
    const actor = createActor(measurementMachine, { input: fx }).start();
    actor.send({ type: "OPEN", system: "metric" });
    actor.send({ type: "SAVE" });
    expect(actor.getSnapshot().matches("editing")).toBe(true);
    expect(fx.log).not.toHaveBeenCalled();
    actor.send({ type: "CHANGE", parameter: "ph", text: "7.2" });
    actor.send({ type: "ORIGIN", origin: "meter" });
    actor.send({ type: "SAVE" });
    actor.send({ type: "SAVE" });
    await waitFor(actor, s => s.matches("idle"));
    expect(fx.log).toHaveBeenCalledTimes(1);
    expect(fx.log).toHaveBeenCalledWith([{ parameter: "ph", value: 7.2, unit: "none", origin: "meter" }]);
    expect(actor.getSnapshot().context.revision).toBe(1);
    actor.stop();
  });
  it("keeps the form and error when saving fails, and updates or deletes an existing entry", async () => {
    const fx = effects();
    fx.log.mockRejectedValueOnce(new Error("offline"));
    const actor = createActor(measurementMachine, { input: fx }).start();
    actor.send({ type: "OPEN", system: "metric" });
    actor.send({ type: "CHANGE", parameter: "no3", text: "20" });
    actor.send({ type: "SAVE" });
    await waitFor(actor, s => s.matches("editing") && s.context.error !== null);
    expect(actor.getSnapshot().context.draft.fields.no3).toBe("20");
    actor.send({ type: "CANCEL" });
    expect(actor.getSnapshot().matches("idle")).toBe(true);
    const entry = { id: 5, kind: "measurement", values: [{ parameter: "ph", value: 7, unit: "none", normalizedValue: 7, canonicalUnit: "none", origin: null }] } as unknown as CareEntry;
    actor.send({ type: "OPEN", system: "metric", entry });
    actor.send({ type: "CHANGE", parameter: "ph", text: "7.5" });
    actor.send({ type: "SAVE" });
    await waitFor(actor, s => s.matches("idle"));
    expect(fx.update).toHaveBeenCalledWith(5, [{ parameter: "ph", value: 7.5, unit: "none", origin: null }]);
    actor.send({ type: "OPEN", system: "metric", entry });
    actor.send({ type: "DELETE" });
    await waitFor(actor, s => s.matches("idle"));
    expect(fx.remove).toHaveBeenCalledWith(5);
    actor.stop();
  });
});
