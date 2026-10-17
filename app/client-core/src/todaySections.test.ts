import { describe, expect, it } from "vitest";
import { createCichlidsClient } from "./client";
import { daysSince, updateImageUri, updatePath } from "./todaySections";
import type { FollowedTank } from "./todaySections";

const owner = { id: 7, username: "keeper", displayName: "Keeper", avatarUrl: null };
const dto = (tankId: number, update: unknown) => ({ tankId, title: `Tank ${tankId}`, owner, update });

function clientFor(respond: (request: Request) => Response) {
  const requests: Request[] = [];
  const client = createCichlidsClient({ baseUrl: "https://api.test", getAccessToken: () => "member-token", fetch: async input => {
    requests.push(input as Request);
    return respond(input as Request);
  } });
  return { client, requests };
}

describe("followed tanks of Today", () => {
  it("maps the updates and makes private image paths absolute", async () => {
    const body = {
      items: [
        dto(3, { kind: "time_lapse_photo", at: "2026-10-16T10:00:00+00:00", careKind: null, story: null,
          image: { thumb: null, small: null, medium: "/api/media/photos/abc?t=1", large: null } }),
        dto("4", { kind: "story", at: "2026-10-15T10:00:00+00:00", careKind: null, story: { id: "9", slug: "spawn", title: "Spawn" }, image: null }),
      ],
      hasMore: true,
    };
    const { client, requests } = clientFor(() => Response.json(body));
    const followed = await client.me.followed();
    expect(requests[0]?.url).toBe("https://api.test/api/me/today/followed");
    expect(requests[0]?.headers.get("Authorization")).toBe("Bearer member-token");
    expect(followed.hasMore).toBe(true);
    expect(followed.items.map(item => item.tankId)).toEqual([3, 4]);
    expect(followed.items[0]?.update?.image?.medium).toBe("https://api.test/api/media/photos/abc?t=1");
    expect(followed.items[1]?.update?.story).toEqual({ id: 9, slug: "spawn", title: "Spawn" });
  });

  it("lists all followed tanks by page and keeps a missing update as null", async () => {
    const { client, requests } = clientFor(() => Response.json({ total: "2", items: [dto(1, null)] }));
    const page = await client.me.followedTanks({ limit: 1, offset: 1 });
    expect(requests[0]?.url).toBe("https://api.test/api/me/followed-tanks?limit=1&offset=1");
    expect(page.total).toBe(2);
    expect(page.items[0]?.update).toBeNull();
  });
});

describe("milestones of Today", () => {
  it("sends the local time and maps the numbers", async () => {
    const { client, requests } = clientFor(() => Response.json({ items: [
      { key: "kept_streak:5:4", kind: "kept_streak", tankId: "5", tankTitle: "Reef", value: "4", reachedOn: "2026-10-13", inhabitantId: null, inhabitantName: null },
      { key: "fish_anniversary:9:2", kind: "fish_anniversary", tankId: "5", tankTitle: "Reef", value: "2", reachedOn: "2026-10-12", inhabitantId: "9", inhabitantName: "Bruno" },
    ] }));
    const items = await client.me.milestones({ date: "2026-10-15", utcOffsetMinutes: 120 });
    expect(requests[0]?.url).toBe("https://api.test/api/me/today/milestones?date=2026-10-15&utcOffsetMinutes=120");
    expect(items).toEqual([
      { key: "kept_streak:5:4", kind: "kept_streak", tankId: 5, tankTitle: "Reef", value: 4, reachedOn: "2026-10-13", inhabitantId: null, inhabitantName: null },
      { key: "fish_anniversary:9:2", kind: "fish_anniversary", tankId: 5, tankTitle: "Reef", value: 2, reachedOn: "2026-10-12", inhabitantId: 9, inhabitantName: "Bruno" },
    ]);
  });

  it("dismisses a milestone by its key", async () => {
    const { client, requests } = clientFor(() => new Response(null, { status: 204 }));
    await client.me.dismissMilestone("tank_anniversary:5:2");
    expect(requests[0]?.method).toBe("PUT");
    expect(decodeURIComponent(requests[0]!.url)).toBe("https://api.test/api/me/today/milestones/tank_anniversary:5:2/dismissal");
  });
});

describe("update helpers", () => {
  const tank = (update: FollowedTank["update"]): FollowedTank => ({ tankId: 8, title: "T", owner, update });
  const base = { kind: "story" as const, at: "2026-10-10T00:00:00Z", careKind: null, story: null, image: null };

  it("counts whole days and never goes negative", () => {
    expect(daysSince("2026-10-10T12:00:00Z", new Date("2026-10-13T11:00:00Z"))).toBe(2);
    expect(daysSince("2026-10-10T12:00:00Z", new Date("2026-10-10T13:00:00Z"))).toBe(0);
    expect(daysSince("2026-10-12T12:00:00Z", new Date("2026-10-10T13:00:00Z"))).toBe(0);
  });

  it("picks the medium image first", () => {
    const image = { thumb: "t", small: "s", medium: null, large: "l" };
    expect(updateImageUri({ ...base, image })).toBe("s");
    expect(updateImageUri(base)).toBeNull();
  });

  it("opens the story of an update by slug or id, otherwise the tank", () => {
    expect(updatePath(tank({ ...base, story: { id: 4, slug: "spawn", title: null } }))).toBe("/gallery/spawn");
    expect(updatePath(tank({ ...base, story: { id: 4, slug: null, title: null } }))).toBe("/gallery/4");
    expect(updatePath(tank(base))).toBe("/tanks/8");
    expect(updatePath(tank(null))).toBe("/tanks/8");
  });
});
