import { expect, it } from "vitest";
import { ApiError, createCichlidsClient } from "./client.js";
import { libraryQuery, toLibraryItem } from "./library.js";

const urls = { thumb: "/api/media/photos/t1", small: "/api/media/photos/s1", medium: null, large: null };
const item = {
  id: 7, kind: "photo", createdAt: "2026-10-16T10:00:00Z", width: 800, height: 600, image: urls, video: null,
  placeCount: 2, placeKinds: ["tank", "story"],
};
const place = { kind: "tank", name: "Reef", tankId: 3, careEntryId: null, storyId: null, storySlug: null, section: "time_lapse", careKind: null, at: null };

function clientWith(respond: (request: Request) => Response, requests: Request[] = []) {
  return createCichlidsClient({ baseUrl: "http://library.test", getAccessToken: async () => "token", fetch: async request => {
    requests.push(request as Request);
    return respond(request as Request);
  } });
}

it("builds the list query from the kind and the linked state", () => {
  expect(libraryQuery({})).toEqual({});
  expect(libraryQuery({ kind: "video", linked: false, offset: 20, limit: 10 })).toEqual({ kind: "video", filter: "unlinked", offset: 20, limit: 10 });
  expect(libraryQuery({ linked: true })).toEqual({ filter: "linked" });
});

it("lists the library with absolute signed paths and numeric ids", async () => {
  const requests: Request[] = [];
  const client = clientWith(() => Response.json({ total: "1", items: [item] }), requests);
  const page = await client.library.list({ kind: "photo", linked: true, offset: 0, limit: 20 });
  expect(requests[0]!.url).toBe("http://library.test/api/me/media?kind=photo&filter=linked&offset=0&limit=20");
  expect(requests[0]!.headers.get("Authorization")).toBe("Bearer token");
  expect(page.total).toBe(1);
  expect(page.items[0]).toMatchObject({ id: 7, placeCount: 2, placeKinds: ["tank", "story"] });
  expect(page.items[0]!.image?.thumb).toBe("http://library.test/api/media/photos/t1");
});

it("reads the detail with the places and the kept streak weeks", async () => {
  const requests: Request[] = [];
  const client = clientWith(() => Response.json({
    item, originalFilename: "a.jpg", byteSize: 123, capturedInApp: true, receivedAt: "2026-10-16T10:00:00Z", takenAt: null, canDelete: true, places: [place],
    keepsStreak: [{ tankId: "3", tankTitle: "Reef", weekStart: "2026-10-12" }],
  }), requests);
  const detail = await client.library.get(7, { date: "2026-10-16", utcOffsetMinutes: 120 });
  expect(requests[0]!.url).toBe("http://library.test/api/me/media/7?date=2026-10-16&utcOffsetMinutes=120");
  expect(detail.keepsStreak).toEqual([{ tankId: 3, tankTitle: "Reef", weekStart: "2026-10-12" }]);
  expect(detail.canDelete).toBe(true);
  expect(detail.places[0]).toMatchObject({ kind: "tank", name: "Reef", tankId: 3, section: "time_lapse" });
});

it("deletes a file and resolves with the places it was shown in", async () => {
  const requests: Request[] = [];
  const client = clientWith(() => Response.json({ places: [place] }), requests);
  const places = await client.library.remove(7);
  expect(requests[0]!.method).toBe("DELETE");
  expect(requests[0]!.url).toBe("http://library.test/api/me/media/7");
  expect(places.map(p => p.name)).toEqual(["Reef"]);
});

it("keeps the problem code of a refused deletion", async () => {
  const client = clientWith(() => Response.json({ title: "File proves a decommissioning", code: "media_is_proof" }, { status: 409 }));
  await expect(client.library.remove(7)).rejects.toMatchObject({ status: 409, code: "media_is_proof" });
  await expect(client.library.remove(7)).rejects.toBeInstanceOf(ApiError);
});

it("uploads a photo into the library as a multipart file", async () => {
  const requests: Request[] = [];
  const client = clientWith(() => Response.json(item, { status: 201 }), requests);
  const created = await client.library.uploadPhoto(new Blob(["x"], { type: "image/jpeg" }));
  expect(requests[0]!.method).toBe("POST");
  expect(requests[0]!.url).toBe("http://library.test/api/me/media/photos");
  expect(requests[0]!.headers.get("Content-Type")).toContain("multipart/form-data");
  expect(created.id).toBe(7);
});

it("maps a list entry without sizes", () => {
  expect(toLibraryItem({ ...item, width: null, height: null, id: "9", placeCount: "0", placeKinds: [] } as never))
    .toMatchObject({ id: 9, width: null, height: null, placeCount: 0 });
});

it("chooses a library photo as avatar and as header and answers with the profile", async () => {
  const requests: Request[] = [];
  const client = clientWith(() => Response.json({ id: 1, avatarUrl: "http://cdn.test/a.jpg", profileImageUrl: null }), requests);
  const avatar = await client.me.setProfileImage("avatar", 7);
  await client.me.setProfileImage("header", 8);
  expect(requests[0]!.method).toBe("PUT");
  expect(requests[0]!.url).toBe("http://library.test/api/me/avatar");
  expect(await requests[0]!.clone().json()).toEqual({ mediaItemId: 7 });
  expect(requests[1]!.url).toBe("http://library.test/api/me/header");
  expect(avatar.avatarUrl).toBe("http://cdn.test/a.jpg");
});

it("uploads a fresh photo as header and removes the avatar", async () => {
  const requests: Request[] = [];
  const client = clientWith(() => Response.json({ id: 1, avatarUrl: null, profileImageUrl: null }), requests);
  await client.me.uploadProfileImage("header", new Blob(["x"], { type: "image/jpeg" }));
  await client.me.removeProfileImage("avatar");
  expect(requests[0]!.method).toBe("POST");
  expect(requests[0]!.url).toBe("http://library.test/api/me/header/photo");
  expect(requests[1]!.method).toBe("DELETE");
  expect(requests[1]!.url).toBe("http://library.test/api/me/avatar");
});

it("keeps the problem code of a refused avatar", async () => {
  const client = clientWith(() => Response.json({ title: "File unavailable", code: "media_not_found" }, { status: 404 }));
  await expect(client.me.setProfileImage("avatar", 9)).rejects.toMatchObject({ status: 404, code: "media_not_found" });
});
