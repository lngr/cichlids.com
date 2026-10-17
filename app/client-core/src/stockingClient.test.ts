import { expect, it } from "vitest";
import { ApiError, createCichlidsClient } from "./client.js";

const dto = (patch: object = {}) => ({
  id: 5, species: null, count: 1, name: "Bruno", movedInOn: "2026-10-15", removedOn: null, removalReason: null, ...patch,
});

it("reads the stocking as numbers with the current inhabitants and the history", async () => {
  const requests: Request[] = [];
  const client = createCichlidsClient({ baseUrl: "http://stocking.test", fetch: async request => {
    requests.push(request as Request);
    return Response.json({ current: [dto({ id: "5" })], history: [dto({ id: 6, removedOn: "2026-10-01", removalReason: "died" })] });
  } });
  const stocking = await client.tanks.stocking(7);
  expect(requests[0]!.url).toBe("http://stocking.test/api/tanks/7/inhabitants");
  expect(stocking.current[0]!.id).toBe(5);
  expect(stocking.history[0]).toMatchObject({ removedOn: "2026-10-01", removalReason: "died" });
});

it("adds, changes and removes an inhabitant with the local date", async () => {
  const requests: Request[] = [];
  const client = createCichlidsClient({ baseUrl: "http://stocking.test", getAccessToken: async () => "token", fetch: async request => {
    const req = request as Request;
    requests.push(req);
    return Response.json(dto(), { status: req.method === "POST" && req.url.endsWith("/inhabitants?date=2026-10-15") ? 201 : 200 });
  } });
  await client.tanks.addInhabitant(7, { name: "Bruno" }, { date: "2026-10-15" });
  expect(requests[0]!.method).toBe("POST");
  expect(requests[0]!.url).toBe("http://stocking.test/api/tanks/7/inhabitants?date=2026-10-15");
  expect(await requests[0]!.json()).toEqual({ speciesId: null, count: null, name: "Bruno", movedInOn: null });

  await client.tanks.updateInhabitant(7, 5, { speciesId: 4, count: 12, movedInOn: "2026-09-01" }, { date: "2026-10-15", utcOffsetMinutes: 120 });
  expect(requests[1]!.method).toBe("PUT");
  expect(requests[1]!.url).toBe("http://stocking.test/api/tanks/7/inhabitants/5?date=2026-10-15&utcOffsetMinutes=120");
  expect(await requests[1]!.json()).toEqual({ speciesId: 4, count: 12, name: null, movedInOn: "2026-09-01" });

  await client.tanks.removeInhabitant(7, 5, { reason: "given_away" }, { date: "2026-10-15" });
  expect(requests[2]!.url).toBe("http://stocking.test/api/tanks/7/inhabitants/5/removal?date=2026-10-15");
  expect(await requests[2]!.json()).toEqual({ reason: "given_away", removedOn: null });
  expect(requests[2]!.headers.get("Authorization")).toBe("Bearer token");
});

it("raises the problem code of a refusal", async () => {
  const client = createCichlidsClient({ baseUrl: "http://stocking.test", fetch: async () =>
    Response.json({ title: "Refused", status: 404, code: "inhabitant_not_found" }, { status: 404, headers: { "Content-Type": "application/problem+json" } }) });
  const error = await client.tanks.addInhabitant(7, { name: "Bruno" }, { date: "2026-10-15" }).catch((e: unknown) => e);
  expect(error).toBeInstanceOf(ApiError);
  expect((error as ApiError).status).toBe(404);
  expect((error as ApiError).code).toBe("inhabitant_not_found");
});
