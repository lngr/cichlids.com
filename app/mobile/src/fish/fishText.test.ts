import { beforeAll, describe, expect, it } from "vitest";
import i18next, { type TFunction } from "i18next";
import type { FishPage, FishTimelineItem } from "@cichlids/client-core";
import { TRANSLATIONS } from "../i18n/catalogues";
import {
  deletableItem, generationLabel, growthProblemKey, lengthText, lengthUnitOf, memorialText, tankText, timelineLabel, timelineText,
} from "./fishText";

let en: TFunction;
let de: TFunction;
beforeAll(async () => {
  en = (await i18next.createInstance().init({ lng: "en", resources: { en: { translation: TRANSLATIONS.en } } })) as TFunction;
  de = (await i18next.createInstance().init({ lng: "de", resources: { de: { translation: TRANSLATIONS.de } } })) as TFunction;
});

const item = (patch: Partial<FishTimelineItem>): FishTimelineItem => ({
  kind: "growth", on: "2026-10-01", eventId: 4, growth: null, spawning: null, move: null, year: null, reason: null, ...patch,
});
const page = (patch: Partial<FishPage> = {}): FishPage => ({
  id: 9, name: "Bruno", species: null, owner: { id: 1, username: "member-1", displayName: null, avatarUrl: null }, isOwner: true,
  tank: { id: 3, title: "Lake", deleted: false }, movedInOn: "2026-09-01", removedOn: null, removalReason: null, memorial: false,
  photos: [], videos: [], timeline: [], ...patch,
});

describe("lengths", () => {
  const growth = { lengthCm: 8.89, inputValue: 3.5, inputUnit: "inch" as const };

  it("shows the owner the value and unit typed and everybody else the length in their units", () => {
    expect(lengthText(growth, "metric", "en", true)).toBe("3.5 in");
    expect(lengthText(growth, "metric", "en", false)).toBe("8.89 cm");
    expect(lengthText(growth, "us", "en", false)).toBe("3.5 in");
    expect(lengthText({ lengthCm: 7.5, inputValue: 7.5, inputUnit: "cm" }, "us", "de", false)).toBe("2,95 in");
    expect(lengthText({ lengthCm: 7.5, inputValue: 7.5, inputUnit: "cm" }, "us", "de", true)).toBe("7,5 cm");
  });

  it("proposes the unit of the unit system", () => {
    expect(lengthUnitOf("metric")).toBe("cm");
    expect(lengthUnitOf("us")).toBe("inch");
  });
});

describe("timeline texts", () => {
  const growth = item({ growth: { lengthCm: 8, inputValue: 8, inputUnit: "cm" } });

  it("describes each kind", () => {
    expect(timelineText(item({ kind: "moved_in", eventId: null }), true, "metric", en, "en")).toBe("Moved in");
    expect(timelineText(growth, true, "metric", en, "en")).toBe("Measured: 8 cm");
    expect(timelineText(item({ kind: "spawning", spawning: { generation: 2, hybrid: false } }), true, "metric", en, "en")).toBe("Spawned, offspring F2");
    expect(timelineText(item({ kind: "spawning", spawning: { generation: null, hybrid: true } }), true, "metric", en, "en")).toBe("Spawned, hybrid offspring");
    expect(timelineText(item({ kind: "died" }), true, "metric", en, "en")).toBe("Died");
    expect(timelineText(item({ kind: "left", reason: "given_away", eventId: null }), true, "metric", en, "en")).toBe("Given away");
  });

  it("names the tank of a move, or says another tank when the viewer may not see it", () => {
    const tank = { id: 5, title: "Quarantine", deleted: false };
    expect(timelineText(item({ kind: "moved", move: { fromTank: null, toTank: tank } }), false, "metric", en, "en")).toBe("Moved to Quarantine");
    expect(timelineText(item({ kind: "moved", move: { fromTank: tank, toTank: null } }), false, "metric", en, "en")).toBe("Moved to another tank");
    expect(timelineText(item({ kind: "moved", move: { fromTank: null, toTank: { ...tank, title: "" } } }), false, "metric", en, "en")).toBe("Moved to Untitled");
  });

  it("counts the years of an anniversary with the plural of the language", () => {
    expect(timelineText(item({ kind: "anniversary", year: 1, eventId: null }), true, "metric", en, "en")).toBe("1 year together");
    expect(timelineText(item({ kind: "anniversary", year: 3, eventId: null }), true, "metric", de, "de")).toBe("3 Jahre zusammen");
  });

  it("labels an item for a screen reader with its day", () => {
    expect(timelineLabel(growth, true, "metric", en, "en")).toBe("Oct 1, 2026: Measured: 8 cm");
  });

  it("labels generations F1, F2 and so on", () => {
    expect(generationLabel(1)).toBe("F1");
    expect(generationLabel(12)).toBe("F12");
  });

  it("lets the owner delete growth and spawning entries only", () => {
    expect(deletableItem(growth)).toBe(true);
    expect(deletableItem(item({ kind: "spawning" }))).toBe(true);
    expect(deletableItem(item({ kind: "moved" }))).toBe(false);
    expect(deletableItem(item({ kind: "died" }))).toBe(false);
    expect(deletableItem(item({ kind: "anniversary", eventId: null }))).toBe(false);
  });
});

describe("page texts", () => {
  it("states the dates of a memorial", () => {
    const dead = page({ memorial: true, removedOn: "2026-10-10", removalReason: "died" });
    expect(memorialText(dead, en, "en")).toBe("Sep 1, 2026 to Oct 10, 2026");
    expect(memorialText({ ...dead, movedInOn: null }, en, "en")).toBe("Died on Oct 10, 2026");
    expect(memorialText(page(), en, "en")).toBe("");
  });

  it("names the tank the fish lives in or lived in last", () => {
    expect(tankText(page(), en)).toBe("Lives in Lake");
    expect(tankText(page({ removedOn: "2026-10-10" }), en)).toBe("Lived in Lake");
    expect(tankText(page({ tank: { id: 3, title: "Lake", deleted: true } }), en)).toBe("Lives in Lake (deleted)");
    expect(tankText(page({ tank: { id: 3, title: "", deleted: false } }), en)).toBe("Lives in Untitled");
  });

  it("maps the problems of the growth form to texts", () => {
    expect(growthProblemKey("length")).toBe("fish.errors.length");
    expect(growthProblemKey("length_too_large")).toBe("fish.errors.lengthTooLarge");
  });
});
