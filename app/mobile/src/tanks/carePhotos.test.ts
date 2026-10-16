import { describe, expect, it } from "vitest";
import type { CareEntry, CarePhoto } from "@cichlids/client-core";
import { canAddPhoto, photoPath, remainingPhotoSlots } from "./carePhotos";

const photo = (urls: Partial<CarePhoto["urls"]>): CarePhoto => ({ id: 1, width: 800, height: 600, urls: { thumb: null, small: null, medium: null, large: null, ...urls } });
const entry = (photos: number): CareEntry => ({ id: 1, tankId: 2, authorProfileId: 3, kind: "water_change", occurredAt: "2026-10-15T12:00:00Z",
  recordedAt: "2026-10-15T12:00:00Z", amount: null, values: null, note: null, todos: [], photos: Array.from({ length: photos }, (_, i) => ({ ...photo({ thumb: `/t${i}` }), id: i })) });

describe("photoPath", () => {
  it("prefers the requested size and falls back to the nearest variant that exists", () => {
    const all = photo({ thumb: "/t", small: "/s", medium: "/m", large: "/l" });
    expect(photoPath(all, "thumb")).toBe("/t");
    expect(photoPath(all, "large")).toBe("/l");
    expect(photoPath(photo({ thumb: "/t", small: "/s" }), "large")).toBe("/s");
    expect(photoPath(photo({ thumb: "/t" }), "large")).toBe("/t");
    expect(photoPath(photo({ small: "/s", large: "/l" }), "thumb")).toBe("/s");
    expect(photoPath(photo({}), "thumb")).toBeNull();
  });
});

describe("photo slots", () => {
  it("allows photos up to six per entry", () => {
    expect(canAddPhoto(entry(0))).toBe(true);
    expect(canAddPhoto(entry(5))).toBe(true);
    expect(canAddPhoto(entry(6))).toBe(false);
    expect(remainingPhotoSlots(entry(4))).toBe(2);
    expect(remainingPhotoSlots(entry(6))).toBe(0);
  });
});
