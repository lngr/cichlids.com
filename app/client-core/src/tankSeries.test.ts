import { describe, expect, it } from "vitest";
import { ApiError, createCichlidsClient } from "./client.js";

describe("tank time-lapse API", () => {
  it("loads the typed series through the generated endpoint", async () => {
    const series = { isShowcase: false, photos: [], stories: [] };
    let requested = "";
    const client = createCichlidsClient({
      baseUrl: "https://api.test",
      fetch: async request => {
        requested = request instanceof Request ? request.url : String(request);
        return Response.json(series);
      },
    });
    expect(await client.tanks.timeLapse(42)).toEqual(series);
    expect(requested).toBe("https://api.test/api/tanks/42/time-lapse");
  });
  it("preserves the coded problem for an unavailable tank", async () => {
    const client = createCichlidsClient({
      baseUrl: "https://api.test",
      fetch: async () => Response.json({ code: "tank_not_found", detail: "Unavailable" }, { status: 404 }),
    });
    await expect(client.tanks.timeLapse(42)).rejects.toMatchObject<ApiError>({
      name: "ApiError", message: "Unavailable", status: 404, code: "tank_not_found",
    });
  });
  it("adds a photo to the series as a multipart upload and returns the typed photo", async () => {
    const photo = { mediaItemId: 9, image: { thumb: "t" }, takenAt: null, publishedAt: "2026-10-16T08:00:00Z" };
    let request: Request | undefined;
    const client = createCichlidsClient({
      baseUrl: "https://api.test",
      fetch: async input => {
        request = input as Request;
        return Response.json(photo, { status: 201 });
      },
    });
    expect(await client.tanks.addTimeLapsePhoto(42, new Blob(["jpeg"], { type: "image/jpeg" }))).toEqual(photo);
    expect(request?.method).toBe("POST");
    expect(request?.url).toBe("https://api.test/api/tanks/42/time-lapse/photos");
    expect(request?.headers.get("content-type")).toMatch(/^multipart\/form-data/);
    expect((await request!.formData()).get("file")).toBeInstanceOf(Blob);
  });
  it("preserves the coded problem of a refused series photo", async () => {
    const client = createCichlidsClient({
      baseUrl: "https://api.test",
      fetch: async () => Response.json({ code: "tank_photo_invalid", detail: "Bad photo" }, { status: 400 }),
    });
    await expect(client.tanks.addTimeLapsePhoto(42, new Blob(["x"]))).rejects.toMatchObject({ status: 400, code: "tank_photo_invalid" });
  });
});
