import { expect, it, vi } from "vitest";
import { ApiError, createCichlidsClient } from "./client.js";

const json = (status: number, body: unknown, headers: Record<string, string> = {}) =>
  new Response(JSON.stringify(body), { status, headers: { "Content-Type": status >= 400 ? "application/problem+json" : "application/json", ...headers } });

function clientWith(handler: (request: Request) => Response | Promise<Response>) {
  const calls: Request[] = [];
  const fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const request = new Request(input as never, init);
    calls.push(request);
    return handler(request);
  });
  return { calls, client: createCichlidsClient({ baseUrl: "http://api.test", getAccessToken: () => "t", fetch: fetchMock as never }) };
}

it("sends a chunk with its offset and returns the offset the server confirms", async () => {
  const { client, calls } = clientWith(() => new Response(null, { status: 204, headers: { "Upload-Offset": "8" } }));

  const next = await client.videos.uploadTransport.append(7, 0, new Blob(["12345678"]));

  expect(next).toBe(8);
  expect(calls[0].method).toBe("PATCH");
  expect(new URL(calls[0].url).pathname).toBe("/api/video-uploads/7");
  expect(calls[0].headers.get("Upload-Offset")).toBe("0");
  expect(calls[0].headers.get("Content-Type")).toBe("application/offset+octet-stream");
  expect(calls[0].headers.get("Authorization")).toBe("Bearer t");
  expect((await calls[0].arrayBuffer()).byteLength).toBe(8);
});

it("takes the end of the chunk as the offset when the browser hides the confirming header", async () => {
  const { client } = clientWith(() => new Response(null, { status: 204 }));

  expect(await client.videos.uploadTransport.append(7, 8, new Blob(["12345678"]))).toBe(16);
});

it("reports the code of a refused chunk", async () => {
  const { client } = clientWith(() => json(409, { code: "video_upload_offset_mismatch", offset: 8 }));

  await expect(client.videos.uploadTransport.append(7, 0, new Blob(["x"]))).rejects.toMatchObject({
    status: 409, code: "video_upload_offset_mismatch",
  });
});

it("reports a lost connection as an error with status 0", async () => {
  const { client } = clientWith(() => { throw new TypeError("Failed to fetch"); });

  const error = await client.videos.uploadTransport.append(7, 0, new Blob(["x"])).catch((e: unknown) => e);

  expect(error).toBeInstanceOf(ApiError);
  expect((error as ApiError).status).toBe(0);
});

it("opens a session, reads its status and completes it", async () => {
  const { client, calls } = clientWith(request => {
    if (request.method === "POST" && request.url.endsWith("/complete")) return json(201, { id: 5, state: "processing" });
    if (request.method === "POST") return json(201, { id: 7, offset: 0, size: 20, chunkSize: 8, expiresAt: "2026-10-17T00:00:00Z" });
    return json(200, { id: 7, offset: 8, size: 20, chunkSize: 8, expiresAt: "2026-10-17T00:00:00Z" });
  });

  const created = await client.videos.uploadTransport.create({ target: "tank", tankId: 1, size: 20, fileName: "a.mp4" });
  const status = await client.videos.uploadTransport.status(7);
  const video = await client.videos.uploadTransport.complete(7);

  expect([created.id, Number(status.offset), video.id]).toEqual([7, 8, 5]);
  expect(JSON.parse(await calls[0].text())).toEqual({ target: "tank", tankId: 1, size: 20, fileName: "a.mp4" });
});

it("lists the videos of a tank and removes one", async () => {
  const { client, calls } = clientWith(request => request.method === "GET" ? json(200, [{ id: 5, state: "ready" }]) : new Response(null, { status: 204 }));

  expect(await client.videos.listForTank(3)).toEqual([{ id: 5, state: "ready" }]);
  await client.videos.removeFromTank(3, 5);

  expect(new URL(calls[1].url).pathname).toBe("/api/tanks/3/videos/5");
  expect(client.videos.careVideoUrl("/api/care-videos/x/master.m3u8")).toBe("http://api.test/api/care-videos/x/master.m3u8");
});
