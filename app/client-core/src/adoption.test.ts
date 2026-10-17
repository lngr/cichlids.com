import { describe, expect, it } from "vitest";
import { ApiError, createCichlidsClient } from "./client.js";
import { ADOPTION_MAX_PHOTOS, adoptionProblemOf, toPhotoSuggestion, toTankAdoption, toggleSuggestion } from "./adoption.js";

const image = { thumb: "/api/media/photos/t", small: null, medium: null, large: null };

describe("toggleSuggestion", () => {
  it("adds a photo at the end and removes a chosen photo", () => {
    expect(toggleSuggestion([], 4)).toEqual([4]);
    expect(toggleSuggestion([4], 2)).toEqual([4, 2]);
    expect(toggleSuggestion([4, 2, 9], 2)).toEqual([4, 9]);
  });

  it("stops growing at the maximum and keeps the choice", () => {
    const full = Array.from({ length: ADOPTION_MAX_PHOTOS }, (_, i) => i + 1);

    expect(toggleSuggestion(full, 999)).toEqual(full);
    expect(toggleSuggestion(full, 1)).toHaveLength(ADOPTION_MAX_PHOTOS - 1);
  });
});

describe("mapping", () => {
  it("maps a suggestion and an adoption to numbers", () => {
    expect(toPhotoSuggestion({ mediaItemId: "12", image, takenAt: null, date: "2008-01-01T00:00:00+00:00" }))
      .toEqual({ mediaItemId: 12, image, takenAt: null, date: "2008-01-01T00:00:00+00:00" });
    expect(toTankAdoption({ tankId: "5", title: "Malawi", badge: null })).toEqual({ tankId: 5, title: "Malawi", badge: null });
  });

  it("names the refusal of an adoption by the problem code", () => {
    expect(adoptionProblemOf(new ApiError("x", 409, "tank_already_adopted"))).toBe("already_adopted");
    expect(adoptionProblemOf(new ApiError("x", 404, "media_not_found"))).toBe("photo_not_found");
    expect(adoptionProblemOf(new ApiError("x", 404, "legacy_tank_not_found"))).toBe("tank_not_found");
    expect(adoptionProblemOf(new ApiError("x", 400, "adoption_photos_invalid"))).toBe("invalid");
    expect(adoptionProblemOf(new ApiError("x", 500, "other"))).toBeNull();
    expect(adoptionProblemOf(new Error("x"))).toBeNull();
  });
});

describe("me adoption routes", () => {
  it("reads a page of suggestions with absolute image urls and posts the chosen ids in order", async () => {
    const requests: { url: string; method: string; body: string }[] = [];
    const fetchImpl = async (input: Request | URL | string) => {
      const request = input as Request;
      requests.push({ url: request.url, method: request.method, body: await request.text() });
      const body = request.method === "GET"
        ? { total: 3, items: [{ mediaItemId: 7, image, takenAt: "2010-05-01T12:00:00+00:00", date: "2010-05-01T12:00:00+00:00" }] }
        : { tankId: 5, title: "Malawi", badge: "returned" };
      return new Response(JSON.stringify(body), { status: 200, headers: { "Content-Type": "application/json" } });
    };
    const client = createCichlidsClient({ baseUrl: "https://api.test", fetch: fetchImpl as never });

    const page = await client.me.photoSuggestions(5, { offset: 20, limit: 10 });
    const adoption = await client.me.adoptLegacyTank(5, [9, 7, 8]);

    expect(page.total).toBe(3);
    expect(page.items[0]).toMatchObject({ mediaItemId: 7, takenAt: "2010-05-01T12:00:00+00:00" });
    expect(page.items[0].image?.thumb).toBe("https://api.test/api/media/photos/t");
    expect(adoption).toEqual({ tankId: 5, title: "Malawi", badge: "returned" });
    expect(requests[0].url).toBe("https://api.test/api/me/legacy-tanks/5/photo-suggestions?offset=20&limit=10");
    expect(requests[1]).toMatchObject({ method: "POST", url: "https://api.test/api/me/legacy-tanks/5/adopt" });
    expect(JSON.parse(requests[1].body)).toEqual({ mediaItemIds: [9, 7, 8] });
  });
});
