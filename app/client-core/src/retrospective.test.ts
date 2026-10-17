import { describe, expect, it } from "vitest";
import { createActor } from "xstate";
import { createCichlidsClient } from "./client.js";
import { retrospectivePlayerMachine, statEntries, toArchiveRetrospective } from "./retrospective.js";

const image = { thumb: "t", small: "s", medium: "/api/media/photos/abc", large: null };

describe("toArchiveRetrospective", () => {
  it("orders the cards and closes with the question how the tank looks today", () => {
    const retrospective = toArchiveRetrospective({
      memberSince: { year: 2011, month: 4 },
      stats: { photos: 12, comments: 3, stars: 0 },
      mostViewed: { storyId: 7, slug: "blue", title: "Blue", viewCount: 900, image },
      tanks: [{ id: 5, title: "Malawi", image: null }, { id: 6, title: "Victoria", image }],
    });

    expect(retrospective.cards.map(card => card.kind)).toEqual(["member_since", "stats", "most_viewed", "archive_tank", "archive_tank", "tank_today"]);
    expect(retrospective.cards[0]).toEqual({ kind: "member_since", year: 2011, month: 4 });
    expect(retrospective.cards[2]).toEqual({ kind: "most_viewed", storyId: 7, slug: "blue", title: "Blue", viewCount: 900, image });
    expect(retrospective.cards[3]).toEqual({ kind: "archive_tank", tankId: 5, title: "Malawi", image: null });
  });

  it("leaves out the cards without data", () => {
    const retrospective = toArchiveRetrospective({
      memberSince: null, stats: { photos: 0, comments: 0, stars: 0 }, mostViewed: null, tanks: [{ id: 5, title: "Malawi", image: null }],
    });

    expect(retrospective.cards.map(card => card.kind)).toEqual(["archive_tank", "tank_today"]);
  });
});

describe("statEntries", () => {
  it("names the figures that have a value", () => {
    expect(statEntries({ kind: "stats", photos: 12, comments: 0, stars: 30 })).toEqual([{ key: "photos", value: 12 }, { key: "stars", value: 30 }]);
  });
});

describe("retrospectivePlayerMachine", () => {
  it("plays through the cards and pauses on the last one", () => {
    const actor = createActor(retrospectivePlayerMachine, { input: { count: 3 } }).start();
    expect(actor.getSnapshot().matches("playing")).toBe(true);
    actor.send({ type: "TICK" });
    actor.send({ type: "TICK" });
    expect(actor.getSnapshot().context.index).toBe(2);
    expect(actor.getSnapshot().matches("playing")).toBe(true);
    actor.send({ type: "TICK" });
    expect(actor.getSnapshot().context.index).toBe(2);
    expect(actor.getSnapshot().matches("paused")).toBe(true);
    actor.stop();
  });

  it("steps and seeks within the cards and keeps the pause", () => {
    const actor = createActor(retrospectivePlayerMachine, { input: { count: 4 } }).start();
    actor.send({ type: "PAUSE" });
    actor.send({ type: "NEXT" });
    actor.send({ type: "NEXT" });
    expect(actor.getSnapshot().context.index).toBe(2);
    actor.send({ type: "PREV" });
    expect(actor.getSnapshot().context.index).toBe(1);
    actor.send({ type: "SEEK", index: 99 });
    expect(actor.getSnapshot().context.index).toBe(3);
    actor.send({ type: "NEXT" });
    expect(actor.getSnapshot().context.index).toBe(3);
    actor.send({ type: "SEEK", index: -4 });
    actor.send({ type: "PREV" });
    expect(actor.getSnapshot().context.index).toBe(0);
    expect(actor.getSnapshot().matches("paused")).toBe(true);
    actor.stop();
  });

  it("starts again from the first card when playing resumes on the last", () => {
    const actor = createActor(retrospectivePlayerMachine, { input: { count: 2 } }).start();
    actor.send({ type: "TICK" });
    actor.send({ type: "TICK" });
    expect(actor.getSnapshot().matches("paused")).toBe(true);
    actor.send({ type: "PLAY" });
    expect(actor.getSnapshot().context.index).toBe(0);
    expect(actor.getSnapshot().matches("playing")).toBe(true);
    actor.stop();
  });
});

describe("me.archiveRetrospective", () => {
  it("maps the cards and makes private image paths absolute", async () => {
    const client = createCichlidsClient({ baseUrl: "http://retro.test", fetch: async () =>
      Response.json({ memberSince: null, stats: { photos: 2, comments: 0, stars: 0 }, mostViewed: null, tanks: [{ id: 5, title: "Malawi", image }] }) });

    const retrospective = await client.me.archiveRetrospective();

    expect(retrospective?.cards).toEqual([
      { kind: "stats", photos: 2, comments: 0, stars: 0 },
      { kind: "archive_tank", tankId: 5, title: "Malawi", image: { ...image, medium: "http://retro.test/api/media/photos/abc" } },
      { kind: "tank_today" },
    ]);
  });

  it("answers null for a member without a retrospective", async () => {
    const requests: Request[] = [];
    const client = createCichlidsClient({ baseUrl: "http://retro.test", fetch: async request => {
      requests.push(request as Request);
      return new Response(null, { status: 204 });
    } });

    expect(await client.me.archiveRetrospective()).toBeNull();
    expect(requests[0]!.url).toBe("http://retro.test/api/me/retrospectives/archive");
  });
});

describe("me.markArchiveRetrospectiveSeen", () => {
  it("posts to the seen route", async () => {
    const requests: Request[] = [];
    const client = createCichlidsClient({ baseUrl: "http://retro.test", fetch: async request => {
      requests.push(request as Request);
      return new Response(null, { status: 204 });
    } });

    await client.me.markArchiveRetrospectiveSeen();

    expect(requests[0]!.method).toBe("POST");
    expect(requests[0]!.url).toBe("http://retro.test/api/me/retrospectives/archive/seen");
  });
});
