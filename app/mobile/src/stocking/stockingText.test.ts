import { describe, expect, it } from "vitest";
import type { InhabitantProblem, StockedInhabitant } from "@cichlids/client-core";
import { TRANSLATIONS } from "../i18n/catalogues";
import { countText, inhabitantSummary, leftText, movedInText, problemKey, removalProblemKey } from "./stockingText";

const t = (key: string, options?: Record<string, unknown>) => `${key}${options ? JSON.stringify(options) : ""}`;
const fish = (patch: Partial<StockedInhabitant> = {}): StockedInhabitant => ({
  id: 1, species: null, count: 1, name: "Bruno", movedInOn: "2026-09-30", removedOn: null, removalReason: null, ...patch,
});

describe("movedInText", () => {
  it("names the day, or the creation of the tank for a migrated inhabitant", () => {
    expect(movedInText(fish(), t, "en")).toBe('stocking.since{"date":"Sep 30, 2026"}');
    expect(movedInText(fish({ movedInOn: null }), t, "en")).toBe("stocking.sinceCreated");
  });
});

describe("leftText", () => {
  it("names the reason and the day of a departure", () => {
    expect(leftText(fish({ removedOn: "2026-10-01", removalReason: "died" }), t, "en")).toBe('stocking.left.died{"date":"Oct 1, 2026"}');
    expect(leftText(fish({ removedOn: "2026-10-01", removalReason: "given_away" }), t, "de")).toBe('stocking.left.given_away{"date":"1. Okt. 2026"}');
    expect(leftText(fish(), t, "en")).toBeNull();
  });
});

describe("countText", () => {
  it("counts groups only", () => {
    expect(countText(fish({ name: null, count: 12000 }), "en", "?")).toBe("12,000");
    expect(countText(fish({ name: null, count: null }), "en", "?")).toBe("?");
    expect(countText(fish(), "en", "?")).toBeNull();
  });
});

describe("inhabitantSummary", () => {
  it("joins label, count and day", () => {
    expect(inhabitantSummary(fish({ name: null, count: 12 }), "Tropheus", t, "en", "?")).toBe('Tropheus, 12, stocking.since{"date":"Sep 30, 2026"}');
    expect(inhabitantSummary(fish(), "Bruno", t, "en", "?")).toBe('Bruno, stocking.since{"date":"Sep 30, 2026"}');
  });
});

describe("problem keys", () => {
  const problems: InhabitantProblem[] = ["species", "count", "name", "name_too_long", "moved_in_invalid", "moved_in_future"];
  const keys = [...problems.map(problemKey), ...(["invalid", "future", "before_moved_in"] as const).map(removalProblemKey)];
  it("exist in every catalogue", () => {
    expect(removalProblemKey("ok")).toBeNull();
    for (const [language, resource] of Object.entries(TRANSLATIONS)) {
      for (const key of keys) {
        const value = key!.split(".").reduce<unknown>((node, part) => (node as Record<string, unknown> | undefined)?.[part], resource);
        expect(typeof value, `${language} ${key}`).toBe("string");
      }
    }
  });
  it("has a text for each reason in every catalogue", () => {
    for (const [language, resource] of Object.entries(TRANSLATIONS)) {
      const stocking = (resource as unknown as { stocking: { left: Record<string, string>; reason: Record<string, string> } }).stocking;
      for (const reason of ["died", "given_away", "moved"]) {
        expect(stocking.left[reason], `${language} left ${reason}`).toContain("{{date}}");
        expect(stocking.reason[reason], `${language} reason ${reason}`).toBeTruthy();
      }
    }
  });
});
