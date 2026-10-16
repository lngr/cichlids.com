import i18next, { type TFunction } from "i18next";
import { beforeAll, describe, expect, it } from "vitest";
import type { CareHeatmap } from "@cichlids/client-core";
import { TRANSLATIONS } from "../i18n/catalogues";
import { heatmapItemText, heatmapSummary } from "./heatmapText";

const heatmap = (patch: Partial<CareHeatmap> = {}): CareHeatmap => ({
  start: "2026-06-29", end: "2026-10-15", weeks: 16, days: [], activeDays: 0, lastWaterChange: null, lastMeasurement: null, decommissionedDays: [], ...patch,
});
let en: TFunction;
let de: TFunction;
beforeAll(async () => {
  en = (await i18next.createInstance().init({ lng: "en", resources: { en: { translation: TRANSLATIONS.en } } })) as TFunction;
  de = (await i18next.createInstance().init({ lng: "de", resources: { de: { translation: TRANSLATIONS.de } } })) as TFunction;
});

describe("heatmap summary for a screen reader", () => {
  it("names the decommissioned days after the care days", () => {
    expect(heatmapSummary(heatmap({ decommissionedDays: ["2026-10-01", "2026-10-02"] }), en, "en"))
      .toBe("No care logged in the last 16 weeks. Decommissioned days: 2");
    expect(heatmapSummary(heatmap({ activeDays: 3, lastWaterChange: "2026-10-01", decommissionedDays: ["2026-10-05"] }), de, "de"))
      .toContain("Stillgelegte Tage: 1");
  });

  it("names the care days and the last water change", () => {
    const summary = heatmapSummary(heatmap({ activeDays: 12, lastWaterChange: "2026-10-01" }), en, "en");
    expect(summary).toBe("Care days in the last 16 weeks: 12. Last water change: Oct 1, 2026");
  });
  it("speaks German with a German date", () => {
    expect(heatmapSummary(heatmap({ activeDays: 3, lastWaterChange: "2026-10-01" }), de, "de"))
      .toBe("Pflegetage in den letzten 16 Wochen: 3. Letzter Wasserwechsel: 1. Okt. 2026");
  });
  it("says so calmly when no care was logged", () => {
    expect(heatmapSummary(heatmap(), en, "en")).toBe("No care logged in the last 16 weeks");
  });
  it("leaves the water change out when only measurements exist", () => {
    expect(heatmapSummary(heatmap({ activeDays: 1, lastMeasurement: "2026-10-05" }), en, "en")).toBe("Care days in the last 16 weeks: 1");
  });
});

describe("heatmap list items", () => {
  it("lists the counts of the day by kind", () => {
    expect(heatmapItemText({ date: "2026-10-15", waterChanges: 2, measurements: 1 }, en, "en"))
      .toBe("Oct 15, 2026: Water changes: 2, Measurements: 1");
    expect(heatmapItemText({ date: "2026-10-15", waterChanges: 0, measurements: 1 }, en, "en"))
      .toBe("Oct 15, 2026: Measurements: 1");
  });
});
