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
it("reads the care plan with the local time and normalizes numbers", async () => {
  const requests: Request[] = [];
  const client = createCichlidsClient({ baseUrl: "http://care.test", getAccessToken: async () => "care-token", fetch: async request => {
    requests.push(request as Request);
    return Response.json({ tankId: "42", rules: [
      { kind: "water_change", rhythmDays: "7", enabled: true, isDefault: true, reason: { code: "cichlid_default", parameters: null }, due: { state: "overdue", days: "3", dueDate: "2026-10-12" } },
      { kind: "measurement", rhythmDays: null, enabled: false, isDefault: true, reason: null, due: null },
    ] });
  } });
  const plan = await client.care.plan(42, { date: "2026-10-15", utcOffsetMinutes: 120 });
  expect(requests[0].url).toBe("http://care.test/api/tanks/42/care/plan?date=2026-10-15&utcOffsetMinutes=120");
  expect(plan.rules[0]).toEqual({ kind: "water_change", rhythmDays: 7, enabled: true, isDefault: true,
    reason: { code: "cichlid_default", parameters: null }, due: { state: "overdue", days: 3, dueDate: "2026-10-12" } });
  expect(plan.rules[1]).toEqual({ kind: "measurement", rhythmDays: null, enabled: false, isDefault: true, reason: null, due: null });
  expect(plan.tankId).toBe(42);
});
it("changes a rule and lists the due care of the member", async () => {
  const requests: Request[] = [];
  const client = createCichlidsClient({ baseUrl: "http://care.test", getAccessToken: async () => "care-token", fetch: async request => {
    const req = request as Request; requests.push(req);
    if (req.url.includes("/me/care/due")) return Response.json({ items: [{ tankId: 4, tankTitle: "Mbuna", kind: "water_change", rhythmDays: 7,
      reason: { code: "cichlid_default", parameters: null }, due: { state: "due_today", days: 0, dueDate: "2026-10-15" }, remindersEnabled: false }] });
    if (req.method === "PATCH" && req.url.includes("/plan/measurement")) return Response.json({ code: "care_plan_invalid", detail: "x" }, { status: 400 });
    return Response.json({ tankId: 42, rules: [] });
  } });
  await client.care.changeRule(42, "water_change", { rhythmDays: 14 }, { date: "2026-10-15" });
  expect(requests[0].method).toBe("PATCH");
  expect(requests[0].url).toBe("http://care.test/api/tanks/42/care/plan/water_change?date=2026-10-15");
  expect(await requests[0].json()).toEqual({ rhythmDays: 14 });
  await client.care.changeRule(42, "water_change", { enabled: false }, { date: "2026-10-15" });
  expect(await requests[1].json()).toEqual({ enabled: false });
  await expect(client.care.changeRule(42, "measurement", { enabled: true }, { date: "2026-10-15" })).rejects.toEqual(new ApiError("x", 400, "care_plan_invalid"));
  const due = await client.care.due({ date: "2026-10-15", utcOffsetMinutes: -300 });
  expect(requests[3].url).toBe("http://care.test/api/me/care/due?date=2026-10-15&utcOffsetMinutes=-300");
  expect(due).toEqual([{ tankId: 4, tankTitle: "Mbuna", kind: "water_change", rhythmDays: 7,
    reason: { code: "cichlid_default", parameters: null }, due: { state: "due_today", days: 0, dueDate: "2026-10-15" }, remindersEnabled: false }]);
});
it("reads and switches the reminders of a tank", async () => {
  const requests: Request[] = [];
  const client = createCichlidsClient({ baseUrl: "http://care.test", getAccessToken: async () => "care-token", fetch: async request => {
    const req = request as Request; requests.push(req);
    return Response.json({ tankId: 42, rules: [], remindersEnabled: req.method === "GET" });
  } });
  expect((await client.care.plan(42, { date: "2026-10-15" })).remindersEnabled).toBe(true);
  const off = await client.care.setReminders(42, false, { date: "2026-10-15", utcOffsetMinutes: 60 });
  expect(requests[1].method).toBe("PATCH");
  expect(requests[1].url).toBe("http://care.test/api/tanks/42/care/plan?date=2026-10-15&utcOffsetMinutes=60");
  expect(await requests[1].json()).toEqual({ remindersEnabled: false });
  expect(off.remindersEnabled).toBe(false);
});
