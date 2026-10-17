import { describe, expect, it } from "vitest";
import { layoutShareCard, mirrorForRtl } from "./shareCardLayout";
import { SHARE_CARD_UNITS, SHARE_CARD_UNITS_HEIGHT, buildShareCard } from "./shareCardModel";
import { shareCardTexts } from "./shareCardTexts";

const t = (key: string) => key;
const full = buildShareCard([
  { kind: "member_since", year: 2011, month: 4 },
  { kind: "stats", photos: 1200, comments: 3, stars: 9 },
  { kind: "most_viewed", storyId: 1, slug: "s", title: "T", viewCount: 4500, image: { thumb: "t", small: null, medium: null, large: "l" } },
], { handle: "fish", showHandle: true });

describe("shareCardTexts", () => {
  it("formats the figures for the language and names the app", () => {
    const texts = shareCardTexts(full, t, "de");
    expect(texts.appName).toBe("cichlids");
    expect(texts.memberSince).toEqual({ label: "retrospective.share.memberSince", value: "April 2011" });
    expect(texts.counts).toEqual([
      { label: "retrospective.share.photos", value: "1.200" },
      { label: "retrospective.share.comments", value: "3" },
    ]);
    expect(texts.mostViewed?.views).toEqual({ label: "retrospective.share.views", value: "4.500" });
    expect(texts.handle).toBe("@fish");
  });

  it("has no entry for data the card lacks", () => {
    const texts = shareCardTexts(buildShareCard([], { handle: "fish", showHandle: false }), t, "en");
    expect(texts.memberSince).toBeNull();
    expect(texts.counts).toEqual([]);
    expect(texts.mostViewed).toBeNull();
    expect(texts.handle).toBeNull();
  });
});

describe("layoutShareCard", () => {
  it("fits every item inside the portrait card", () => {
    const items = layoutShareCard(shareCardTexts(full, t, "en"), { hasPhoto: true });
    expect(items.some(item => item.kind === "photo")).toBe(true);
    for (const item of items) {
      expect(item.x).toBeGreaterThanOrEqual(0);
      expect(item.x + item.width).toBeLessThanOrEqual(SHARE_CARD_UNITS);
      expect(item.y + item.height).toBeLessThanOrEqual(SHARE_CARD_UNITS_HEIGHT);
    }
  });

  it("places no photo without one and keeps the views", () => {
    const items = layoutShareCard(shareCardTexts(full, t, "en"), { hasPhoto: false });
    expect(items.some(item => item.kind === "photo")).toBe(false);
    expect(items.some(item => item.kind === "text" && item.text === "4,500 retrospective.share.views")).toBe(true);
  });

  it("mirrors the positions for a right-to-left language", () => {
    const column = (rtl: boolean, label: string) => {
      const items = mirrorForRtl(layoutShareCard(shareCardTexts(full, t, "en"), { hasPhoto: true }), rtl);
      return items.find(item => item.kind === "text" && item.text === label)!.x;
    };
    expect(column(false, "retrospective.share.photos")).toBeLessThan(column(false, "retrospective.share.comments"));
    expect(column(true, "retrospective.share.photos")).toBeGreaterThan(column(true, "retrospective.share.comments"));
  });

  it("leaves out the handle line unless the card has one", () => {
    const withHandle = layoutShareCard(shareCardTexts(full, t, "en"), { hasPhoto: true });
    const without = layoutShareCard(shareCardTexts(buildShareCard([], { handle: "fish", showHandle: false }), t, "en"), { hasPhoto: false });
    expect(withHandle.some(item => item.kind === "text" && item.text === "@fish")).toBe(true);
    expect(without.some(item => item.kind === "text" && item.text.startsWith("@"))).toBe(false);
  });
});
