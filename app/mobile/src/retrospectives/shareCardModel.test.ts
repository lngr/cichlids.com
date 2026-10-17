import { describe, expect, it } from "vitest";
import type { RetrospectiveCard } from "@cichlids/client-core";
import { SHARE_CARD_HEIGHT, SHARE_CARD_WIDTH, buildShareCard, shareCardFileName } from "./shareCardModel";

const image = { thumb: "t", small: "s", medium: "m", large: "https://media.test/large" };
const cards: RetrospectiveCard[] = [
  { kind: "member_since", year: 2011, month: 4 },
  { kind: "stats", photos: 12, comments: 3, stars: 7 },
  { kind: "most_viewed", storyId: 7, slug: "blue", title: "Blue", viewCount: 900, image },
  { kind: "archive_tank", tankId: 5, title: "Malawi", image: null },
  { kind: "tank_today" },
];

describe("buildShareCard", () => {
  it("takes member since, the photo and comment counts and the most viewed photo from the cards", () => {
    expect(buildShareCard(cards, { handle: "fish", showHandle: false })).toEqual({
      memberSince: { year: 2011, month: 4 },
      photos: 12,
      comments: 3,
      mostViewed: { imageUrl: "https://media.test/large", viewCount: 900 },
      handle: null,
    });
  });

  it("adds the handle only when the member switches it on", () => {
    expect(buildShareCard(cards, { handle: "fish", showHandle: true }).handle).toBe("fish");
    expect(buildShareCard(cards, { handle: "fish", showHandle: false }).handle).toBeNull();
  });

  it("has no handle to show when the profile has none", () => {
    expect(buildShareCard(cards, { handle: null, showHandle: true }).handle).toBeNull();
    expect(buildShareCard(cards, { handle: "  ", showHandle: true }).handle).toBeNull();
  });

  it("leaves out figures the retrospective does not show", () => {
    const model = buildShareCard([{ kind: "stats", photos: 0, comments: 4, stars: 0 }, { kind: "tank_today" }], { handle: null, showHandle: false });
    expect(model).toEqual({ memberSince: null, photos: null, comments: 4, mostViewed: null, handle: null });
  });

  it("keeps the view count of a most viewed photo without an image", () => {
    const model = buildShareCard([{ kind: "most_viewed", storyId: 1, slug: "s", title: null, viewCount: 5, image: null }], { handle: null, showHandle: false });
    expect(model.mostViewed).toEqual({ imageUrl: null, viewCount: 5 });
  });

  it("holds only the fields of the card: nothing of the archive tanks or the stars reaches it", () => {
    const model = buildShareCard(cards, { handle: null, showHandle: false });
    expect(Object.keys(model).sort()).toEqual(["comments", "handle", "memberSince", "mostViewed", "photos"]);
  });
});

describe("share card size", () => {
  it("is portrait with 9:16 proportions", () => {
    expect(SHARE_CARD_WIDTH / SHARE_CARD_HEIGHT).toBeCloseTo(9 / 16, 6);
  });

  it("names the image file after the app", () => {
    expect(shareCardFileName()).toBe("cichlids-retrospective.png");
  });
});
