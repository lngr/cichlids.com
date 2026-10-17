import { expect, it } from "vitest";
import { ApiError, createCichlidsClient } from "./client.js";

const body = (patch: object = {}) => ({
  id: 9, name: "Bruno", species: null, owner: { id: 1, username: "member-1", displayName: null, avatarUrl: null }, isOwner: true,
  tank: { id: 3, title: "Lake", deleted: false }, movedInOn: "2026-09-01", removedOn: null, removalReason: null, memorial: false,
  photos: [{ mediaItemId: 11, width: 800, height: 600, image: { thumb: "/api/media/photos/abc", small: null, medium: null, large: null }, createdAt: "2026-10-01T10:00:00Z" }],
  videos: [{ id: 12, state: "ready", durationSeconds: 3, width: 1, height: 1, posterUrl: "/api/media/videos/x/poster.jpg", playlistUrl: "/api/media/videos/x/master.m3u8", createdAt: "2026-10-01T10:00:00Z", errorCode: null }],
  timeline: [], ...patch,
});

function recording() {
  const requests: Request[] = [];
  const client = createCichlidsClient({ baseUrl: "http://fish.test", getAccessToken: async () => "token", fetch: async request => {
    requests.push(request as Request);
    return Response.json(body());
  } });
  return { client, requests };
}

it("reads the page with absolute media URLs and the date of the viewer", async () => {
  const { client, requests } = recording();
  const page = await client.fish.get(9, { date: "2026-10-15", utcOffsetMinutes: 120 });
  expect(requests[0]!.url).toBe("http://fish.test/api/fish/9?date=2026-10-15");
  expect(page.photos[0]!.image.thumb).toBe("http://fish.test/api/media/photos/abc");
  expect(page.videos[0]!.playlistUrl).toBe("http://fish.test/api/media/videos/x/master.m3u8");
  await client.fish.get(9);
  expect(requests[1]!.url).toBe("http://fish.test/api/fish/9");
});

it("records growth and spawning with the local date", async () => {
  const { client, requests } = recording();
  await client.fish.recordGrowth(9, { length: 3.5, unit: "inch" }, { date: "2026-10-15" });
  expect(requests[0]!.method).toBe("POST");
  expect(requests[0]!.url).toBe("http://fish.test/api/fish/9/growth?date=2026-10-15");
  expect(await requests[0]!.json()).toEqual({ length: 3.5, unit: "inch", on: null });
  expect(requests[0]!.headers.get("Authorization")).toBe("Bearer token");

  await client.fish.recordSpawning(9, { generation: 2, on: "2026-10-01" }, { date: "2026-10-15" });
  expect(requests[1]!.url).toBe("http://fish.test/api/fish/9/spawning?date=2026-10-15");
  expect(await requests[1]!.json()).toEqual({ generation: 2, hybrid: false, on: "2026-10-01" });
  await client.fish.recordSpawning(9, { hybrid: true }, { date: "2026-10-15" });
  expect(await requests[2]!.json()).toEqual({ generation: null, hybrid: true, on: null });
});

it("moves, buries and manages events and media", async () => {
  const { client, requests } = recording();
  const local = { date: "2026-10-15", utcOffsetMinutes: 60 };
  await client.fish.move(9, 4, "2026-10-10", local);
  expect(requests[0]!.url).toBe("http://fish.test/api/fish/9/move?date=2026-10-15");
  expect(await requests[0]!.json()).toEqual({ toTankId: 4, on: "2026-10-10" });
  await client.fish.recordDeath(9, null, local);
  expect(requests[1]!.url).toBe("http://fish.test/api/fish/9/death?date=2026-10-15");
  expect(await requests[1]!.json()).toEqual({ on: null });
  await client.fish.deleteEvent(9, 5, local);
  expect(requests[2]!.method).toBe("DELETE");
  expect(requests[2]!.url).toBe("http://fish.test/api/fish/9/events/5?date=2026-10-15");
  await client.fish.linkMedia(9, 11, local);
  expect(requests[3]!.url).toBe("http://fish.test/api/fish/9/media?date=2026-10-15");
  expect(await requests[3]!.json()).toEqual({ mediaItemId: 11 });
  await client.fish.unlinkMedia(9, 11, local);
  expect(requests[4]!.method).toBe("DELETE");
  expect(requests[4]!.url).toBe("http://fish.test/api/fish/9/media/11?date=2026-10-15");
});

it("raises the problem code of a refusal", async () => {
  const client = createCichlidsClient({ baseUrl: "http://fish.test", fetch: async () =>
    Response.json({ title: "Refused", status: 404, code: "fish_not_found" }, { status: 404, headers: { "Content-Type": "application/problem+json" } }) });
  const error = await client.fish.get(9).catch((e: unknown) => e);
  expect(error).toBeInstanceOf(ApiError);
  expect((error as ApiError).status).toBe(404);
  expect((error as ApiError).code).toBe("fish_not_found");
});
