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
