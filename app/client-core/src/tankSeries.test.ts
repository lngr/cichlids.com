import { describe, expect, it } from "vitest";
import { ApiError, createCichlidsClient } from "./client.js";

describe("tank time-lapse API", () => {
  it("loads the typed series through the generated endpoint", async () => {
    const series = { isShowcase: false, photos: [], posts: [] };
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
});
