import { describe, expect, it } from "vitest";
import { cardImageUrl, memberSinceLabel, regionStoryPath, routeOfAction, shouldCheckReturnRetrospective, shouldOpenArchiveRetrospective, tapDirection } from "./retrospectiveView";

describe("memberSinceLabel", () => {
  it("names the month and the year in the language of the member, independent of the time zone", () => {
    expect(memberSinceLabel(2011, 4, "en")).toBe("April 2011");
    expect(memberSinceLabel(2011, 4, "de")).toBe("April 2011");
    expect(memberSinceLabel(2009, 1, "de")).toBe("Januar 2009");
    expect(memberSinceLabel(2012, 12, "en")).toBe("December 2012");
  });
});

describe("cardImageUrl", () => {
  it("prefers the large size and falls back to the smaller ones", () => {
    expect(cardImageUrl({ thumb: "t", small: "s", medium: "m", large: "l" })).toBe("l");
    expect(cardImageUrl({ thumb: "t", small: null, medium: "m", large: null })).toBe("m");
    expect(cardImageUrl({ thumb: "t", small: null, medium: null, large: null })).toBe("t");
  });

  it("has no URL without an image", () => {
    expect(cardImageUrl(null)).toBeUndefined();
    expect(cardImageUrl({ thumb: null, small: null, medium: null, large: null })).toBeUndefined();
  });
});

describe("routeOfAction", () => {
  it("leads to the adoption of the tank, the story and the creation of a tank", () => {
    expect(routeOfAction({ type: "adopt_tank", tankId: 5 })).toBe("/tanks/5/adopt");
    expect(routeOfAction({ type: "open_story", slug: "blue-peacock" })).toBe("/gallery/blue-peacock");
    expect(routeOfAction({ type: "set_up_tank" })).toBe("/onboarding");
  });

  it("leads to the care view for missed care, the guided camera and a given path", () => {
    expect(routeOfAction({ type: "log_care", tankId: 5 })).toBe("/tanks/5?view=care");
    expect(routeOfAction({ type: "take_photo", tankId: 5 })).toBe("/tanks/5/camera");
    expect(routeOfAction({ type: "open_path", path: "/gallery/spawn" })).toBe("/gallery/spawn");
  });
});

describe("shouldOpenArchiveRetrospective", () => {
  const unseen = { available: true, seenAt: null };

  it("opens for a registered member whose retrospective is unseen", () => {
    expect(shouldOpenArchiveRetrospective({ status: unseen, provisional: false, firstTankOffered: false })).toBe(true);
  });

  it.each([
    ["has none", { available: false, seenAt: null }, false, false],
    ["saw it", { available: true, seenAt: "2026-10-17T10:00:00+00:00" }, false, false],
    ["is provisional", unseen, true, false],
    ["is offered the first tank", unseen, false, true],
    ["has no status", null, false, false],
    ["has an absent status", undefined, false, false],
  ])("stays closed for a member who %s", (_, status, provisional, firstTankOffered) => {
    expect(shouldOpenArchiveRetrospective({ status, provisional, firstTankOffered })).toBe(false);
  });
});

describe("tapDirection", () => {
  it("steps back on the leading third and forward on the rest", () => {
    expect(tapDirection(10, 300, false)).toBe("previous");
    expect(tapDirection(150, 300, false)).toBe("next");
    expect(tapDirection(290, 300, false)).toBe("next");
  });

  it("mirrors the thirds for a right-to-left layout", () => {
    expect(tapDirection(290, 300, true)).toBe("previous");
    expect(tapDirection(10, 300, true)).toBe("next");
  });
});

describe("shouldCheckReturnRetrospective", () => {
  it("checks for a registered member who is not otherwise sent away", () => {
    expect(shouldCheckReturnRetrospective({ provisional: false, firstTankOffered: false, archiveOpens: false })).toBe(true);
  });

  it.each([
    ["is provisional", true, false, false],
    ["is offered the first tank", false, true, false],
    ["gets the archive retrospective", false, false, true],
  ])("does not check for a member who %s", (_, provisional, firstTankOffered, archiveOpens) => {
    expect(shouldCheckReturnRetrospective({ provisional, firstTankOffered, archiveOpens })).toBe(false);
  });
});

describe("regionStoryPath", () => {
  it("uses the slug and falls back to the id", () => {
    expect(regionStoryPath({ id: 9, slug: "spawn" })).toBe("/gallery/spawn");
    expect(regionStoryPath({ id: 9, slug: null })).toBe("/gallery/9");
  });
});
