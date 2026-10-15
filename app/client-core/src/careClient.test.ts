import { expect, it } from "vitest";
import { ApiError, createCichlidsClient } from "./client.js";
it("sends care requests with auth, paging and coded refusals", async () => {
  const requests: Request[] = [];
  const client = createCichlidsClient({ baseUrl: "http://care.test", getAccessToken: async () => "care-token", fetch: async request => {
    const req = request as Request; requests.push(req);
    if (req.method === "GET") return Response.json({ total: 1, items: [] });
    if (req.method === "DELETE") return Response.json({ code: "care_not_found", detail: "Private" }, { status: 404 });
    return Response.json({ id: 1 });
  } });
  await client.care.log(42);
  expect(await requests[0].json()).toEqual({});
  expect(requests[0].headers.get("Authorization")).toBe("Bearer care-token");
  await client.care.update(42, 1, { value: 55, unit: "us_gallon" });
  expect(requests[1].method).toBe("PATCH");
  expect(await requests[1].json()).toEqual({ amount: { value: 55, unit: "us_gallon" } });
  await client.care.list(42, { offset: 20, limit: 10 });
  expect(requests[2].url).toBe("http://care.test/api/tanks/42/care?offset=20&limit=10");
  await expect(client.care.remove(42, 1)).rejects.toEqual(new ApiError("Private", 404, "care_not_found"));
});
it("sends measurement values with auth and reads a series with its range", async () => {
  const requests: Request[] = [];
  const client = createCichlidsClient({ baseUrl: "http://care.test", getAccessToken: async () => "care-token", fetch: async request => {
    const req = request as Request; requests.push(req);
    return Response.json(req.url.includes("/series") ? { parameter: "ph", unit: "none", points: [] } : { id: 1 });
  } });
  await client.care.log(42, { kind: "measurement", values: [{ parameter: "ph", value: 7.2, origin: "meter" }] });
  expect(await requests[0].json()).toEqual({ kind: "measurement", values: [{ parameter: "ph", value: 7.2, origin: "meter" }] });
  await client.care.updateValues(42, 1, [{ parameter: "ph", value: 7.4 }]);
  expect(requests[1].method).toBe("PATCH");
  expect(await requests[1].json()).toEqual({ amount: null, values: [{ parameter: "ph", value: 7.4 }] });
  const series = await client.care.series(42, "ph", { from: "2026-10-01T00:00:00Z", to: "2026-10-31T00:00:00Z" });
  expect(requests[2].url).toBe("http://care.test/api/tanks/42/care/series?parameter=ph&from=2026-10-01T00%3A00%3A00Z&to=2026-10-31T00%3A00%3A00Z");
  expect(series).toEqual({ parameter: "ph", unit: "none", points: [] });
});
