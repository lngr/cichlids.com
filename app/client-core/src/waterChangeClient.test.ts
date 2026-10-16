import { expect, it } from "vitest";
import { ApiError, createCichlidsClient } from "./client.js";

const session = { id: 5, tankId: 42, steps: [] };

it("calls the water change endpoints with auth and treats no open session as null", async () => {
  const requests: Request[] = [];
  const client = createCichlidsClient({ baseUrl: "http://wc.test", getAccessToken: async () => "wc-token", fetch: async request => {
    const req = request as Request; requests.push(req);
    if (req.url.endsWith("/current")) return Response.json({ code: "water_change_not_found" }, { status: 404 });
    if (req.method === "DELETE") return new Response(null, { status: 204 });
    return Response.json(req.url.endsWith("/finish") ? { session, entry: { id: 9 } } : session);
  } });
  expect(await client.waterChange.start(42)).toEqual(session);
  expect(requests[0].method).toBe("POST");
  expect(requests[0].url).toBe("http://wc.test/api/tanks/42/water-changes");
  expect(requests[0].headers.get("Authorization")).toBe("Bearer wc-token");
  expect(await client.waterChange.current(42)).toBeNull();
  await client.waterChange.markStep(42, 5, "draining_started", "done");
  expect(requests[2].method).toBe("PUT");
  expect(requests[2].url).toBe("http://wc.test/api/tanks/42/water-changes/5/steps/draining_started");
  expect(await requests[2].json()).toEqual({ state: "done" });
  await client.waterChange.addPhoto(42, 5, new Blob(["x"], { type: "image/jpeg" }), true);
  expect(requests[3].method).toBe("POST");
  expect(requests[3].url).toBe("http://wc.test/api/tanks/42/water-changes/5/photos");
  const form = await requests[3].formData();
  expect(form.get("capturedInApp")).toBe("true");
  expect(form.get("file")).toBeInstanceOf(Blob);
  const finished = await client.waterChange.finish(42, 5, { value: 30, unit: "percent" });
  expect(await requests[4].json()).toEqual({ amount: { value: 30, unit: "percent" } });
  expect(finished.entry.id).toBe(9);
  await client.waterChange.finish(42, 5, null);
  expect(await requests[5].json()).toEqual({});
  await expect(client.waterChange.cancel(42, 5)).resolves.toBeUndefined();
});

it("passes other refusals of the current session on", async () => {
  const client = createCichlidsClient({ baseUrl: "http://wc.test", fetch: async () => Response.json({ code: "care_auth_required" }, { status: 401 }) });
  await expect(client.waterChange.current(42)).rejects.toEqual(new ApiError("", 401, "care_auth_required"));
});
