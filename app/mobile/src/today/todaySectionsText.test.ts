import i18next, { type TFunction } from "i18next";
import { beforeAll, describe, expect, it } from "vitest";
import type { FollowedTank, FollowedUpdate, Milestone } from "@cichlids/client-core";
import { TRANSLATIONS } from "../i18n/catalogues";
import { followedLabel, followedLine, milestoneText, updateText, whenText } from "./todaySectionsText";

const owner = { id: 1, username: "keeper", displayName: null, avatarUrl: null };
const update = (patch: Partial<FollowedUpdate> = {}): FollowedUpdate => ({
  kind: "story", at: "2026-10-14T10:00:00Z", careKind: null, story: null, image: null, ...patch,
});
const tank = (patch: Partial<FollowedTank> = {}): FollowedTank => ({ tankId: 4, title: "Reef", owner, update: update(), ...patch });
const milestone = (patch: Partial<Milestone>): Milestone => ({
  key: "kept_streak:4:4", kind: "kept_streak", tankId: 4, tankTitle: "Reef", value: 4, reachedOn: "2026-10-13", inhabitantId: null, inhabitantName: null, ...patch,
});
const now = new Date("2026-10-17T12:00:00Z");

let en: TFunction;
let de: TFunction;
beforeAll(async () => {
  en = (await i18next.createInstance().init({ lng: "en", resources: { en: { translation: TRANSLATIONS.en } } })) as TFunction;
  de = (await i18next.createInstance().init({ lng: "de", resources: { de: { translation: TRANSLATIONS.de } } })) as TFunction;
});

describe("followed tank texts", () => {
  it("names the kind of update and, for care, the kind of care", () => {
    expect(updateText(update(), en)).toBe("New story");
    expect(updateText(update({ kind: "time_lapse_photo" }), en)).toBe("New time-lapse photo");
    expect(updateText(update({ kind: "tank_photo" }), en)).toBe("New tank photo");
    expect(updateText(update({ kind: "care_entry", careKind: "water_change" }), en)).toBe("Water change logged");
    expect(updateText(update({ kind: "care_entry", careKind: "measurement" }), en)).toBe("Measurement logged");
  });

  it("says today or the whole days with the plural of the language", () => {
    expect(whenText(update({ at: "2026-10-17T01:00:00Z" }), now, en)).toBe("today");
    expect(whenText(update({ at: "2026-10-16T11:00:00Z" }), now, en)).toBe("1 day ago");
    expect(whenText(update(), now, en)).toBe("3 days ago");
    expect(whenText(update(), now, de)).toBe("vor 3 Tagen");
    expect(whenText(update({ at: "2026-10-16T11:00:00Z" }), now, de)).toBe("vor 1 Tag");
  });

  it("joins update and time into the line and the spoken label of a card", () => {
    expect(followedLine(tank(), now, en)).toBe("New story, 3 days ago");
    expect(followedLabel(tank(), now, en)).toBe("Reef, New story, 3 days ago");
  });

  it("shows a calm line for a tank without an update", () => {
    expect(followedLine(tank({ update: null }), now, en)).toBe("No update so far");
    expect(followedLabel(tank({ update: null, title: "" }), now, en)).toBe("Untitled, No update so far");
  });
});

describe("milestone texts", () => {
  it("congratulates on the first care week", () => {
    expect(milestoneText(milestone({ kind: "first_care_week", value: 0 }), en)).toBe("Reef met its water change plan for the first time.");
  });

  it("counts the weeks of a kept streak and the years of a tank", () => {
    expect(milestoneText(milestone({ value: 12 }), en)).toBe("Reef: 12 weeks of time-lapse photos in a row.");
    expect(milestoneText(milestone({ kind: "tank_anniversary", value: 1 }), en)).toBe("Reef is 1 year old.");
    expect(milestoneText(milestone({ kind: "tank_anniversary", value: 3 }), de)).toBe("Reef ist 3 Jahre alt.");
  });

  it("congratulates on the year of a named fish", () => {
    const fish = milestone({ kind: "fish_anniversary", key: "fish_anniversary:9:2", value: 2, inhabitantId: 9, inhabitantName: "Bruno" });
    expect(milestoneText(fish, en)).toBe("Bruno has lived with you in Reef for 2 years.");
    expect(milestoneText({ ...fish, value: 1 }, en)).toBe("Bruno has lived with you in Reef for 1 year.");
    expect(milestoneText(fish, de)).toBe("Bruno lebt seit 2 Jahren bei dir in Reef.");
  });

  it("names a tank without a title", () => {
    expect(milestoneText(milestone({ tankTitle: "", kind: "tank_anniversary", value: 2 }), en)).toBe("Untitled is 2 years old.");
  });
});
