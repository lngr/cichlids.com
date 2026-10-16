import { expect, it } from "vitest";
import { ApiError, createCichlidsClient } from "./client.js";

const waterChange = { state: "running", current: 2, currentVerified: 2, longest: 3, lastWaterChange: "2026-10-14", deadline: "2026-10-24", freezesLeft: 2, freezesPerYear: 2 };
const timeLapse = { current: 1, longest: 1, thisWeekHasPhoto: true, thisWeekOpen: false, weekStart: "2026-10-12", weekEnd: "2026-10-18", paused: false, keepingPhotoIds: [8] };
const vacation = { id: 4, startDate: "2026-10-15", endDate: null, active: true };

it("reads the streaks of a tank and of the member with the local date and offset", async () => {
  const requests: Request[] = [];
  const client = createCichlidsClient({ baseUrl: "http://streaks.test", getAccessToken: async () => "token", fetch: async request => {
    const req = request as Request; requests.push(req);
    return Response.json(req.url.includes("/api/me/") ? { freezesPerYear: 2, vacations: [vacation], tanks: [{ tankId: 7, title: "Reef", waterChange, timeLapse }] }
      : { tankId: 7, waterChange, timeLapse, vacations: [] });
  } });
  const tank = await client.tanks.streaks(7, { date: "2026-10-15", utcOffsetMinutes: 120 });
  expect(requests[0].url).toBe("http://streaks.test/api/tanks/7/streaks?date=2026-10-15&utcOffsetMinutes=120");
  expect(tank.waterChange?.deadline).toBe("2026-10-24");
  const own = await client.me.streaks({ date: "2026-10-15" });
  expect(requests[1].url).toBe("http://streaks.test/api/me/streaks?date=2026-10-15");
  expect(own.tanks[0]!.timeLapse.keepingPhotoIds).toEqual([8]);
  expect(own.vacations[0]!.active).toBe(true);
});

it("starts, records and ends vacations", async () => {
  const requests: Request[] = [];
  const client = createCichlidsClient({ baseUrl: "http://streaks.test", fetch: async request => {
    const req = request as Request; requests.push(req);
    return req.method === "GET" ? Response.json({ items: [vacation] }) : Response.json(vacation, { status: req.url.includes("/end") ? 200 : 201 });
  } });
  expect(await client.me.vacations()).toEqual([vacation]);
  await client.me.startVacation({ date: "2026-10-15", utcOffsetMinutes: 60 });
  expect(requests[1].url).toBe("http://streaks.test/api/me/vacations?date=2026-10-15&utcOffsetMinutes=60");
  expect(await requests[1].json()).toEqual({});
  await client.me.startVacation({ date: "2026-10-15" }, "2026-10-13");
  expect(await requests[2].json()).toEqual({ startDate: "2026-10-13" });
  await client.me.recordVacation({ date: "2026-10-15" }, "2026-09-01", "2026-09-10");
  expect(await requests[3].json()).toEqual({ startDate: "2026-09-01", endDate: "2026-09-10" });
  await client.me.endVacation("2026-10-20");
  expect(requests[4].method).toBe("POST");
  expect(requests[4].url).toBe("http://streaks.test/api/me/vacations/active/end?date=2026-10-20");
});

it("sends whether the camera of the app took a series photo", async () => {
  const forms: FormData[] = [];
  const client = createCichlidsClient({ baseUrl: "http://streaks.test", fetch: async request => {
    forms.push(await (request as Request).formData());
    return Response.json({ mediaItemId: 1, image: {}, takenAt: null, publishedAt: "2026-10-15T00:00:00Z" }, { status: 201 });
  } });
  await client.tanks.addTimeLapsePhoto(7, new Blob(["x"], { type: "image/jpeg" }), true);
  await client.tanks.addTimeLapsePhoto(7, new Blob(["x"], { type: "image/jpeg" }));
  expect(forms.map(form => form.get("capturedInApp"))).toEqual(["true", "false"]);
});

it("passes the coded refusals on", async () => {
  const client = createCichlidsClient({ baseUrl: "http://streaks.test", fetch: async () => Response.json({ code: "vacation_overlap" }, { status: 409 }) });
  await expect(client.me.startVacation({ date: "2026-10-15" })).rejects.toEqual(new ApiError("", 409, "vacation_overlap"));
});
