import { describe, expect, it } from "vitest";
import {
  checkGrowthDraft, checkSpawningDraft, growthInput, latestGrowth, lengthInCentimeters, moveTargets, parseLength, spawningInput, toFishPage,
} from "./fish.js";
import type { FishDto } from "./generated/types.gen.js";

const page = (patch: Partial<FishDto> = {}): FishDto => ({
  id: "9", name: "Bruno", species: { id: "4", slug: "tropheus", genus: "Tropheus", name: "moorii", displayName: "Tropheus moorii" },
  owner: { id: 1, username: "member-1", displayName: "Member", avatarUrl: null }, isOwner: true,
  tank: { id: "3", title: "Lake", deleted: false }, movedInOn: "2026-09-01", removedOn: null, removalReason: null, memorial: false,
  photos: [{ mediaItemId: "11", width: "800", height: null, image: { thumb: "/t.jpg", small: null, medium: null, large: null }, createdAt: "2026-10-01T10:00:00Z" }],
  videos: [],
  timeline: [
    { kind: "moved", on: "2026-10-10", eventId: "5", growth: null, spawning: null, year: null, reason: null,
      move: { fromTank: { id: "2", title: "Old", deleted: false }, toTank: null } },
    { kind: "growth", on: "2026-10-01", eventId: "4", spawning: null, move: null, year: null, reason: null,
      growth: { lengthCm: "8.89", inputValue: "3.5", inputUnit: "inch" } },
    { kind: "spawning", on: "2026-09-20", eventId: 3, growth: null, move: null, year: null, reason: null, spawning: { generation: "2", hybrid: false } },
    { kind: "anniversary", on: "2025-09-01", eventId: null, growth: null, spawning: null, move: null, year: "1", reason: null },
  ],
  ...patch,
});

describe("the page of a named fish", () => {
  it("maps ids and numbers and keeps the timeline order", () => {
    const mapped = toFishPage(page());
    expect(mapped.id).toBe(9);
    expect(mapped.species?.id).toBe(4);
    expect(mapped.tank).toEqual({ id: 3, title: "Lake", deleted: false });
    expect(mapped.photos[0]).toMatchObject({ mediaItemId: 11, width: 800, height: null });
    expect(mapped.timeline.map(item => item.kind)).toEqual(["moved", "growth", "spawning", "anniversary"]);
    expect(mapped.timeline[0]!.move).toEqual({ fromTank: { id: 2, title: "Old", deleted: false }, toTank: null });
    expect(mapped.timeline[1]!.growth).toEqual({ lengthCm: 8.89, inputValue: 3.5, inputUnit: "inch" });
    expect(mapped.timeline[2]!.spawning).toEqual({ generation: 2, hybrid: false });
    expect(mapped.timeline[3]).toMatchObject({ eventId: null, year: 1 });
  });

  it("finds the newest growth entry", () => {
    expect(latestGrowth(toFishPage(page()))).toEqual({ lengthCm: 8.89, inputValue: 3.5, inputUnit: "inch" });
    expect(latestGrowth(toFishPage(page({ timeline: [] })))).toBeNull();
  });

  it("lists the tanks a fish can move to without the one it lives in", () => {
    expect(moveTargets([{ id: 1 }, { id: 3 }, { id: 4 }], 3)).toEqual([{ id: 1 }, { id: 4 }]);
  });
});

describe("the growth form", () => {
  it("reads decimals typed with a point or a comma", () => {
    expect(parseLength("7.5")).toBe(7.5);
    expect(parseLength(" 7,5 ")).toBe(7.5);
    expect(parseLength("12")).toBe(12);
    for (const text of ["", "abc", "-2", "1e3", "1.2.3", "7,5cm"]) expect(parseLength(text)).toBeNaN();
  });

  it("accepts a positive length up to 300 cm in either unit", () => {
    expect(checkGrowthDraft({ length: "7,5", unit: "cm" })).toBeNull();
    expect(checkGrowthDraft({ length: "118", unit: "inch" })).toBeNull();
    expect(checkGrowthDraft({ length: "119", unit: "inch" })).toBe("length_too_large");
    expect(checkGrowthDraft({ length: "300", unit: "cm" })).toBeNull();
    expect(checkGrowthDraft({ length: "301", unit: "cm" })).toBe("length_too_large");
    expect(checkGrowthDraft({ length: "0", unit: "cm" })).toBe("length");
    expect(checkGrowthDraft({ length: "", unit: "inch" })).toBe("length");
    expect(lengthInCentimeters(3.5, "inch")).toBeCloseTo(8.89, 10);
  });

  it("builds the request with the typed unit and leaves an empty day out", () => {
    expect(growthInput({ length: "3,5", unit: "inch" })).toEqual({ length: 3.5, unit: "inch" });
    expect(growthInput({ length: "8", unit: "cm" }, " 2026-10-01 ")).toEqual({ length: 8, unit: "cm", on: "2026-10-01" });
  });
});

describe("the spawning form", () => {
  it("needs a generation from 1 to 99 unless the offspring is a hybrid", () => {
    expect(checkSpawningDraft({ hybrid: false, generation: "1" })).toBeNull();
    expect(checkSpawningDraft({ hybrid: false, generation: "99" })).toBeNull();
    for (const generation of ["", "0", "100", "1.5", "x"]) expect(checkSpawningDraft({ hybrid: false, generation })).toBe("generation");
    expect(checkSpawningDraft({ hybrid: true, generation: "" })).toBeNull();
  });

  it("builds the request", () => {
    expect(spawningInput({ hybrid: false, generation: "2" })).toEqual({ generation: 2 });
    expect(spawningInput({ hybrid: true, generation: "5" }, "2026-10-01")).toEqual({ hybrid: true, on: "2026-10-01" });
  });
});
