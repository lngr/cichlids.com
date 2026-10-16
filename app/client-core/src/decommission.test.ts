import { expect, it } from "vitest";
import { createCichlidsClient } from "./client.js";
import { activeDecommissioning, operatingTime } from "./decommission.js";
import type { TankOperation } from "./decommission.js";

const dto = {
  tankId: 7, operatingDays: 212, operatingMonths: 7, decommissionedDays: 31, decommissioned: false,
  items: [{ id: 3, scope: "all_care", startDate: "2026-07-15", endDate: "2026-08-15", active: false,
    startImage: { thumb: "/api/tank-media/start" }, endImage: { thumb: "https://cdn.test/end.jpg" } }],
};
const operation = (over: Partial<TankOperation> = {}): TankOperation => ({
  tankId: 7, operatingDays: 10, operatingMonths: 0, decommissionedDays: 0, decommissioned: false, items: [], ...over,
});

it("reads the operating time and the history of a tank with the local date", async () => {
  const requests: Request[] = [];
  const client = createCichlidsClient({ baseUrl: "http://deco.test", fetch: async request => {
    requests.push(request as Request);
    return Response.json(dto);
  } });
  const read = await client.tanks.operation(7, { date: "2026-10-15", utcOffsetMinutes: 120 });
  expect(requests[0]!.url).toBe("http://deco.test/api/tanks/7/decommissioning?date=2026-10-15&utcOffsetMinutes=120");
  expect(read.operatingMonths).toBe(7);
  expect(read.items[0]).toMatchObject({ scope: "all_care", startDate: "2026-07-15", endDate: "2026-08-15", active: false });
  expect(read.items[0]!.startImage?.thumb).toBe("http://deco.test/api/tank-media/start");
  expect(read.items[0]!.endImage?.thumb).toBe("https://cdn.test/end.jpg");
});

it("sends the photo, the origin and the scope when decommissioning and the photo when putting the tank back into operation", async () => {
  const forms: Array<{ url: string; form: FormData }> = [];
  const client = createCichlidsClient({ baseUrl: "http://deco.test", fetch: async request => {
    const req = request as Request;
    forms.push({ url: req.url, form: await req.formData() });
    return Response.json(dto, { status: req.url.includes("/end") ? 200 : 201 });
  } });
  const file = new Blob(["x"], { type: "image/jpeg" });
  await client.tanks.decommission(7, file, { scope: "water_change", capturedInApp: true }, { date: "2026-10-15", utcOffsetMinutes: 60 });
  expect(forms[0]!.url).toBe("http://deco.test/api/tanks/7/decommissioning?date=2026-10-15&utcOffsetMinutes=60");
  expect(forms[0]!.form.get("scope")).toBe("water_change");
  expect(forms[0]!.form.get("capturedInApp")).toBe("true");
  expect(forms[0]!.form.get("file")).toBeInstanceOf(File);
  await client.tanks.recommission(7, file, false, { date: "2026-10-16" });
  expect(forms[1]!.url).toBe("http://deco.test/api/tanks/7/decommissioning/end?date=2026-10-16");
  expect(forms[1]!.form.get("capturedInApp")).toBe("false");
  expect(forms[1]!.form.get("scope")).toBeNull();
});

it("names the active decommissioning", () => {
  expect(activeDecommissioning(operation())).toBeNull();
  const active = { id: 1, scope: "all_care" as const, startDate: "2026-10-01", endDate: null, active: true, startImage: null, endImage: null };
  expect(activeDecommissioning(operation({ decommissioned: true, items: [active] }))).toBe(active);
});

it("shows the operating time in days below a month, in months below two years and in years from there", () => {
  expect(operatingTime(operation({ operatingDays: 12, operatingMonths: 0 }))).toEqual({ unit: "days", count: 12 });
  expect(operatingTime(operation({ operatingDays: 212, operatingMonths: 7 }))).toEqual({ unit: "months", count: 7 });
  expect(operatingTime(operation({ operatingDays: 700, operatingMonths: 23 }))).toEqual({ unit: "months", count: 23 });
  expect(operatingTime(operation({ operatingDays: 800, operatingMonths: 26 }))).toEqual({ unit: "years", count: 2 });
});
