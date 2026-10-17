import { describe, expect, it } from "vitest";
import type { SpeciesCommunity } from "@cichlids/client-core";
import { communitySections, partnerRoute, storyRoute, tankRoute } from "./communitySections";

const empty: SpeciesCommunity = { photos: [], tankCount: 0, tanks: [], stockingPartners: [] };

describe("species community sections", () => {
  it("shows no section when the community has no data", () => {
    expect(communitySections(empty)).toEqual({ photos: false, tanks: false, partners: false, any: false });
  });

  it("shows exactly the sections that have data", () => {
    const photo = { storyId: 1, slug: "bruno", title: "Bruno", image: { thumb: null, small: null, medium: null, large: null }, viewCount: 1, ratingAverage: null, ratingCount: 0 };
    expect(communitySections({ ...empty, photos: [photo] })).toEqual({ photos: true, tanks: false, partners: false, any: true });
    expect(communitySections({ ...empty, tankCount: 4, tanks: [{ id: 2, title: "Rock", category: null, image: null }] }))
      .toEqual({ photos: false, tanks: true, partners: false, any: true });
  });

  it("builds the routes of the cards", () => {
    expect(storyRoute({ slug: "bruno" })).toBe("/gallery/bruno");
    expect(tankRoute({ id: 9 })).toBe("/tanks/9");
    expect(partnerRoute({ id: 3, slug: "tropheus-duboisi" })).toBe("/species/tropheus-duboisi");
    expect(partnerRoute({ id: 3, slug: null })).toBe("/species/3");
  });
});
