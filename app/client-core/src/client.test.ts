import { afterAll, afterEach, beforeAll, describe, expect, it } from "vitest";
import { http, HttpResponse } from "msw";
import { setupServer } from "msw/node";
import { createCichlidsClient } from "./client.js";

const baseUrl = "http://cichlids-mock.test";

const server = setupServer(
  http.get(`${baseUrl}/api/pictures`, ({ request }) => {
    const url = new URL(request.url);
    return HttpResponse.json({
      total: 143351,
      items: [
        {
          id: 529665,
          slug: "RRKaG",
          title: "Fin nipper - Jewel Cichlid",
          description: null,
          publishedAt: "2023-07-05T23:52:56+00:00",
          viewCount: 15,
          ratingAverage: 5,
          ratingCount: 1,
          commentCount: 0,
          topic: url.searchParams.get("topic") ?? "cichlids",
          author: { id: 6446, username: "bkrogers1965", displayName: "Byron Rogers", avatarUrl: null },
          image: null,
        },
      ],
    });
  }),
  http.get(`${baseUrl}/api/tanks/:id`, ({ params }) => {
    return HttpResponse.json({
      id: Number(params.id),
      title: "My new new world tank",
      category: "american",
      description: "Growing out",
      gravel: null,
      plants: null,
      decoration: null,
      light: null,
      lightDuration: null,
      filtration: null,
      technic: null,
      waterValues: { ph: null, kh: null, gh: null, no2: null, no3: null, po4: null, notes: null },
      food: null,
      notes: null,
      dimensions: null,
      mainImage: null,
      sections: { showcase: [], decoration: [], technic: [] },
      inhabitants: [],
      author: { id: 6433, username: "squarebody85chevy", displayName: "Steve P", avatarUrl: null },
    });
  }),
  http.post(`${baseUrl}/api/pictures/:slug/comments`, async ({ request, params }) => {
    const authorization = request.headers.get("Authorization");
    if (authorization !== "Bearer test-token") {
      return new HttpResponse(null, { status: 401 });
    }
    const body = (await request.json()) as { body: string; stars: number };
    return HttpResponse.json(
      {
        comment: {
          id: 1,
          body: body.body,
          createdAt: "2026-01-01T00:00:00Z",
          score: 0,
          author: { id: 1, username: "me", displayName: "Me", avatarUrl: null },
          posterName: null,
        },
        rating: body.stars ? { id: 1, stars: body.stars } : null,
      },
      { status: 201 },
    );
  }),
);

beforeAll(() => server.listen({ onUnhandledRequest: "error" }));
afterEach(() => server.resetHandlers());
afterAll(() => server.close());

describe("createCichlidsClient", () => {
  it("lists pictures with query params and a numeric total", async () => {
    const client = createCichlidsClient({ baseUrl });
    const result = await client.pictures.list({ topic: "tanks", limit: 1 });

    expect(result.total).toBe(143351);
    expect(typeof result.total).toBe("number");
    expect(result.items).toHaveLength(1);
    expect(result.items[0].topic).toBe("tanks");
  });

  it("fetches a tank by id via a path parameter", async () => {
    const client = createCichlidsClient({ baseUrl });
    const tank = await client.tanks.get(14970);

    expect(tank.id).toBe(14970);
    expect(tank.title).toBe("My new new world tank");
  });

  it("attaches the Authorization header from getAccessToken when creating a comment", async () => {
    const client = createCichlidsClient({
      baseUrl,
      getAccessToken: () => "test-token",
    });

    const created = await client.pictures.createComment("RRKaG", { body: "Nice fish!", stars: 5 });

    expect(created.comment?.body).toBe("Nice fish!");
    expect(created.rating?.stars).toBe(5);
  });

  it("rejects with an ApiError when no access token is supplied for an authorized endpoint", async () => {
    const client = createCichlidsClient({ baseUrl });

    await expect(client.pictures.createComment("RRKaG", { body: "Nice fish!", stars: 5 })).rejects.toThrow();
  });
});
