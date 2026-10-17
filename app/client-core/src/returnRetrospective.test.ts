import { describe, expect, it } from "vitest";
import { createCichlidsClient } from "./client";
import { opensOnStart, returnCards } from "./returnRetrospective";
import type { ReturnRetrospective } from "./returnRetrospective";

const owner = { id: 7, username: "keeper", displayName: "Keeper", avatarUrl: null };

function clientFor(respond: (request: Request) => Response) {
  const requests: Request[] = [];
  const client = createCichlidsClient({ baseUrl: "https://api.test", getAccessToken: () => "member-token", fetch: async input => {
    requests.push(input as Request);
    return respond(input as Request);
  } });
  return { client, requests };
}

const empty: ReturnRetrospective = {
  seen: false, since: "2026-09-01T00:00:00+00:00", overdueCare: null, responses: null, followedTanks: null, regionNews: null, nextSteps: null,
};

describe("return retrospective client", () => {
  it("maps the cards, numbers and private image paths", async () => {
    const body = {
      seen: false,
      since: "2026-09-01T00:00:00+00:00",
      overdueCare: { items: [{ tankId: "3", tankTitle: "Reef", kind: "water_change", daysOverdue: "12" }] },
      responses: { kudos: "4", comments: 2 },
      followedTanks: {
        items: [{ tankId: 5, title: "Followed", owner, update: { kind: "time_lapse_photo", at: "2026-09-20T10:00:00+00:00", careKind: null, story: null,
          image: { thumb: null, small: null, medium: "/api/media/photos/abc?t=1", large: null } } }],
        hasMore: false,
      },
      regionNews: {
        tanks: [{ tankId: "8", title: "Malawi", region: "lake_malawi", owner }],
        stories: [{ id: "9", slug: "spawn", title: "Spawn", author: owner }],
      },
      nextSteps: { careTanks: [{ tankId: "3", title: "Reef" }], timeLapseTanks: [{ tankId: 3, title: "Reef" }] },
    };
    const { client, requests } = clientFor(() => Response.json(body));

    const retrospective = await client.me.returnRetrospective({ date: "2026-10-17", utcOffsetMinutes: 120 });

    expect(requests[0]?.url).toBe("https://api.test/api/me/retrospectives/return?date=2026-10-17&utcOffsetMinutes=120");
    expect(requests[0]?.headers.get("Authorization")).toBe("Bearer member-token");
    expect(retrospective?.overdueCare).toEqual([{ tankId: 3, tankTitle: "Reef", kind: "water_change", daysOverdue: 12 }]);
    expect(retrospective?.responses).toEqual({ kudos: 4, comments: 2 });
    expect(retrospective?.followedTanks?.items[0]?.update?.image?.medium).toBe("https://api.test/api/media/photos/abc?t=1");
    expect(retrospective?.regionNews?.tanks[0]?.tankId).toBe(8);
    expect(retrospective?.regionNews?.stories[0]).toMatchObject({ id: 9, slug: "spawn" });
    expect(retrospective?.nextSteps?.careTanks).toEqual([{ tankId: 3, title: "Reef" }]);
  });

  it("answers null when no retrospective is due", async () => {
    const { client } = clientFor(() => new Response(null, { status: 204 }));
    expect(await client.me.returnRetrospective({ date: "2026-10-17" })).toBeNull();
  });

  it("marks the retrospective as seen", async () => {
    const { client, requests } = clientFor(() => new Response(null, { status: 204 }));
    await client.me.markReturnRetrospectiveSeen();
    expect(requests[0]?.method).toBe("POST");
    expect(requests[0]?.url).toBe("https://api.test/api/me/retrospectives/return/seen");
  });
});

describe("return cards", () => {
  it("lists the cards with data in presentation order and ends with the next steps", () => {
    const cards = returnCards({
      ...empty,
      overdueCare: [{ tankId: 1, tankTitle: "A", kind: "water_change", daysOverdue: 3 }],
      responses: { kudos: 1, comments: 0 },
      regionNews: { tanks: [], stories: [{ id: 1, slug: null, title: null, author: owner }] },
      nextSteps: { careTanks: [{ tankId: 1, title: "A" }], timeLapseTanks: [] },
    });
    expect(cards).toEqual(["overdue_care", "responses", "region_news", "next_steps"]);
  });

  it("leaves out cards without data and the next steps of an empty retrospective", () => {
    expect(returnCards({ ...empty, nextSteps: { careTanks: [{ tankId: 1, title: "A" }], timeLapseTanks: [] } })).toEqual([]);
    expect(returnCards({ ...empty, followedTanks: { items: [], hasMore: false }, regionNews: { tanks: [], stories: [] } })).toEqual([]);
  });

  it("omits next steps without a tank", () => {
    const cards = returnCards({ ...empty, responses: { kudos: 0, comments: 2 }, nextSteps: { careTanks: [], timeLapseTanks: [] } });
    expect(cards).toEqual(["responses"]);
  });
});

describe("opening on start", () => {
  it("opens only a retrospective that is not yet seen", () => {
    expect(opensOnStart(null)).toBe(false);
    expect(opensOnStart({ ...empty, seen: true })).toBe(false);
    expect(opensOnStart(empty)).toBe(true);
  });
});
