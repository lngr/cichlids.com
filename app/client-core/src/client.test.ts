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
  http.post(`${baseUrl}/api/uploads`, async ({ request }) => {
    const authorization = request.headers.get("Authorization");
    if (authorization !== "Bearer test-token") {
      return new HttpResponse(null, { status: 401 });
    }
    const formData = await request.formData();
    const file = formData.get("file");
    if (!(file instanceof Blob)) {
      return new HttpResponse("no file part", { status: 400 });
    }
    if (file.type !== "image/jpeg") {
      return new HttpResponse("not an image", { status: 400 });
    }
    return HttpResponse.json(
      {
        id: 42,
        state: "draft",
        topic: "cichlids",
        createdAt: "2026-01-01T00:00:00Z",
        image: { thumb: "t.jpg", small: "s.jpg", medium: "m.jpg", large: null, original: "o.jpg" },
      },
      { status: 201 },
    );
  }),
  http.get(`${baseUrl}/api/me/drafts`, ({ request }) => {
    const authorization = request.headers.get("Authorization");
    if (authorization !== "Bearer test-token") {
      return new HttpResponse(null, { status: 401 });
    }
    return HttpResponse.json([
      {
        id: 42,
        state: "draft",
        topic: "cichlids",
        createdAt: "2026-01-01T00:00:00Z",
        image: { thumb: "t.jpg", small: "s.jpg", medium: "m.jpg", large: null, original: "o.jpg" },
      },
    ]);
  }),
  http.post(`${baseUrl}/api/posts/:id/publish`, async ({ request, params }) => {
    const body = (await request.json()) as { title: string | null; description: string | null; topic: string | null };
    if (!body.title) {
      return new HttpResponse("title is required", { status: 400 });
    }
    return HttpResponse.json({
      id: Number(params.id),
      slug: "abc123",
      canonicalSlug: "abc123",
      title: body.title,
      description: body.description,
      publishedAt: "2026-01-01T00:00:00Z",
      viewCount: 0,
      ratingAverage: null,
      ratingCount: 0,
      commentCount: 0,
      topic: body.topic ?? "cichlids",
      author: { id: 1, username: "me", displayName: "Me", avatarUrl: null },
      image: { thumb: "t.jpg", small: "s.jpg", medium: "m.jpg", large: null, original: "o.jpg" },
    });
  }),
  http.delete(`${baseUrl}/api/posts/:id`, ({ params }) => {
    if (params.id === "999") {
      return new HttpResponse("not a draft", { status: 409 });
    }
    return new HttpResponse(null, { status: 204 });
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

  it("uploads a Blob as a multipart file part with the bearer token", async () => {
    const client = createCichlidsClient({
      baseUrl,
      getAccessToken: () => "test-token",
    });
    const file = new Blob(["fake jpeg bytes"], { type: "image/jpeg" });

    const draft = await client.uploads.create(file);

    expect(draft.id).toBe(42);
    expect(draft.state).toBe("draft");
    expect(draft.image.thumb).toBe("t.jpg");
  });

  it("uploads a React Native { uri, name, type } object as the multipart file part", async () => {
    let capturedFile: unknown;
    server.use(
      http.post(`${baseUrl}/api/uploads`, async ({ request }) => {
        const formData = await request.formData();
        capturedFile = formData.get("file");
        return HttpResponse.json(
          {
            id: 43,
            state: "draft",
            topic: "cichlids",
            createdAt: "2026-01-01T00:00:00Z",
            image: { thumb: null, small: null, medium: null, large: null, original: "o.jpg" },
          },
          { status: 201 },
        );
      }),
    );
    const client = createCichlidsClient({
      baseUrl,
      getAccessToken: () => "test-token",
    });

    const draft = await client.uploads.create({ uri: "file:///photo.jpg", name: "photo.jpg", type: "image/jpeg" });

    expect(draft.id).toBe(43);
    expect(capturedFile).toBeDefined();
  });

  it("rejects an upload with an ApiError carrying the status and message on a 400", async () => {
    const client = createCichlidsClient({
      baseUrl,
      getAccessToken: () => "test-token",
    });
    const file = new Blob(["not an image"], { type: "text/plain" });

    await expect(client.uploads.create(file)).rejects.toMatchObject({
      name: "ApiError",
      status: 400,
      message: "not an image",
    });
  });

  it("lists the caller's drafts", async () => {
    const client = createCichlidsClient({
      baseUrl,
      getAccessToken: () => "test-token",
    });

    const drafts = await client.me.drafts();

    expect(drafts).toHaveLength(1);
    expect(drafts[0].id).toBe(42);
  });

  it("publishes a draft with a JSON body and returns the picture detail", async () => {
    const client = createCichlidsClient({
      baseUrl,
      getAccessToken: () => "test-token",
    });

    const detail = await client.posts.publish(42, { title: "My tank", description: null, topic: "tanks" });

    expect(detail.id).toBe(42);
    expect(detail.slug).toBe("abc123");
    expect(detail.title).toBe("My tank");
    expect(detail.topic).toBe("tanks");
  });

  it("discards a draft with DELETE", async () => {
    const client = createCichlidsClient({
      baseUrl,
      getAccessToken: () => "test-token",
    });

    await expect(client.posts.discard(42)).resolves.toBeUndefined();
  });

  it("rejects a discard with an ApiError carrying the status and message on a 409", async () => {
    const client = createCichlidsClient({
      baseUrl,
      getAccessToken: () => "test-token",
    });

    await expect(client.posts.discard(999)).rejects.toMatchObject({
      name: "ApiError",
      status: 409,
      message: "not a draft",
    });
  });
});
