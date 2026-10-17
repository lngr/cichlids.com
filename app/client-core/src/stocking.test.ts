import { describe, expect, it } from "vitest";
import { checkInhabitantDraft, checkRemovalDay, draftOf, inhabitantInput, inhabitantLabel, toStocking } from "./stocking.js";
import type { InhabitantDraft, StockedInhabitant } from "./stocking.js";

const today = "2026-10-15";
const group = (patch: Partial<InhabitantDraft> = {}): InhabitantDraft => ({ kind: "group", speciesId: 4, count: "12", name: "", movedInOn: "", ...patch });
const named = (patch: Partial<InhabitantDraft> = {}): InhabitantDraft => ({ kind: "named", speciesId: null, count: "", name: "Bruno", movedInOn: "", ...patch });

describe("checkInhabitantDraft", () => {
  it.each([
    [group(), null],
    [group({ movedInOn: "2026-10-15" }), null],
    [group({ movedInOn: " 2020-01-31 " }), null],
    [group({ speciesId: null }), "species"],
    [group({ count: "" }), "count"],
    [group({ count: "0" }), "count"],
    [group({ count: "1.5" }), "count"],
    [group({ count: "10001" }), "count"],
    [group({ count: "10000" }), null],
    [group({ movedInOn: "2026-10-16" }), "moved_in_future"],
    [group({ movedInOn: "2026-02-30" }), "moved_in_invalid"],
    [group({ movedInOn: "yesterday" }), "moved_in_invalid"],
    [named(), null],
    [named({ speciesId: 4 }), null],
    [named({ name: "  " }), "name"],
    [named({ name: "x".repeat(80) }), null],
    [named({ name: "x".repeat(81) }), "name_too_long"],
    [named({ count: "9" }), null],
    [named({ movedInOn: "2027-01-01" }), "moved_in_future"],
  ] as const)("checks %j", (draft, expected) => {
    expect(checkInhabitantDraft(draft, today)).toBe(expected);
  });
});

describe("inhabitantInput", () => {
  it("sends species and count for a group and leaves an empty day out", () => {
    expect(inhabitantInput(group())).toEqual({ speciesId: 4, count: 12 });
    expect(inhabitantInput(group({ movedInOn: " 2026-09-30 " }))).toEqual({ speciesId: 4, count: 12, movedInOn: "2026-09-30" });
  });
  it("sends the trimmed name of a named fish without a count", () => {
    expect(inhabitantInput(named({ name: " Bruno " }))).toEqual({ name: "Bruno", speciesId: null });
    expect(inhabitantInput(named({ speciesId: 7, movedInOn: "2026-09-30" }))).toEqual({ name: "Bruno", speciesId: 7, movedInOn: "2026-09-30" });
  });
});

describe("checkRemovalDay", () => {
  it.each([
    ["", "2026-10-01", "ok"],
    ["2026-10-01", "2026-10-01", "ok"],
    ["2026-10-15", "2026-10-01", "ok"],
    ["2026-09-30", "2026-10-01", "before_moved_in"],
    ["2026-10-16", "2026-10-01", "future"],
    ["2026-13-01", "2026-10-01", "invalid"],
    ["2026-09-30", null, "ok"],
  ] as const)("checks %j after %j", (removedOn, movedInOn, expected) => {
    expect(checkRemovalDay(removedOn, movedInOn, today)).toBe(expected);
  });
});

describe("mapping", () => {
  const dto = {
    id: "5", species: { id: "4", slug: "tropheus", genus: "Tropheus", name: "moorii", displayName: "Tropheus moorii" },
    count: "6", name: null, movedInOn: null, removedOn: null, removalReason: null,
  };
  it("turns numeric strings into numbers", () => {
    const stocking = toStocking({ current: [dto as never], history: [{ ...dto, id: 6, count: null, name: "Bruno", removedOn: "2026-10-01", removalReason: "died" } as never] });
    expect(stocking.current[0]).toMatchObject({ id: 5, count: 6, species: { id: 4, displayName: "Tropheus moorii" } });
    expect(stocking.history[0]).toMatchObject({ id: 6, count: null, name: "Bruno", removalReason: "died" });
  });
  it("makes a form of an inhabitant and a label for a list", () => {
    const fish = toStocking({ current: [{ ...dto, name: "Bruno", count: 1, movedInOn: "2026-09-30" } as never], history: [] }).current[0] as StockedInhabitant;
    expect(draftOf(fish)).toEqual({ kind: "named", speciesId: 4, count: "1", name: "Bruno", movedInOn: "2026-09-30" });
    expect(inhabitantLabel(fish)).toBe("Bruno");
    const migrated = toStocking({ current: [dto as never], history: [] }).current[0] as StockedInhabitant;
    expect(draftOf(migrated)).toEqual({ kind: "group", speciesId: 4, count: "6", name: "", movedInOn: "" });
    expect(inhabitantLabel(migrated)).toBe("Tropheus moorii");
    expect(inhabitantLabel({ ...migrated, species: null })).toBeNull();
  });
});
