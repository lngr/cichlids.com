import { describe, expect, it } from "vitest";
import { createCichlidsClient } from "./client";

describe("Species community", () => {
  it("requests the community sections of a species through the generated SDK", async () => {
    let requested = "";
    const community = {
      photos: [{ storyId: 4, slug: "bruno", title: "Bruno", image: { thumb: "t", small: "s", medium: "m", large: "l" }, viewCount: 12, ratingAverage: 4.5, ratingCount: 3 }],
      tankCount: 5,
      tanks: [{ id: 9, title: "Rock tank", category: null, image: null }],
      stockingPartners: [{ species: { id: 2, slug: "neolamprologus-pulcher", genus: "Neolamprologus", name: "pulcher", displayName: "Neolamprologus pulcher" }, tankCount: 3 }],
    };
    const client = createCichlidsClient({ baseUrl: "https://api.test", fetch: async input => {
      requested = (input as Request).url;
      return Response.json(community);
    } });

    expect(await client.species.community("tropheus-duboisi")).toEqual(community);
    expect(requested).toBe("https://api.test/api/species/tropheus-duboisi/community");
  });
});
