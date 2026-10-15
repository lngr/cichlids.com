import { describe, expect, it, vi } from "vitest";
import { ApiError, createCichlidsClient } from "./client.js";

describe("feature flags client", () => {
  it("loads flag names and booleans through the API", async () => {
    const transport = vi.fn<typeof fetch>().mockResolvedValue(Response.json({ "discover-activity": true }));
    const client = createCichlidsClient({ baseUrl: "https://api.test", fetch: transport });
    expect(await client.featureFlags.get()).toEqual({ "discover-activity": true });
    expect((transport.mock.calls[0][0] as Request).cache).toBe("no-cache");
    expect((transport.mock.calls[0][0] as Request).url).toBe("https://api.test/api/feature-flags");
  });
  it("reports endpoint errors to let the app disable flags", async () => {
    const client = createCichlidsClient({ baseUrl: "https://api.test", fetch: vi.fn().mockResolvedValue(new Response("Unavailable", { status: 503 })) });
    await expect(client.featureFlags.get()).rejects.toBeInstanceOf(ApiError);
  });
});
