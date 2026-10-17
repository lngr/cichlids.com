// Story: TASK-6
// A member decommissions a tank with a photo of the empty tank from the app camera and puts it back
// into operation with a photo of the filled tank, on the web build with a fake camera.
// - The app asks what pauses before the camera opens; the continue button waits for the answer.
// - All care paused: the tank has no due care and is not on Today, the care heatmap marks the day,
//   the water change streak is paused, and a visitor sees the decommissioning with the photo.
// - Only the water change paused: measurements stay due, the water change is not due.
// - Putting the tank back into operation takes a photo of the filled tank; the plan runs from that
//   day and the streak continues with the next water change.
// - A tank run for six months, decommissioned for one and run for one more shows seven months.
// Runs on the isolated write stack only (see e2e/write-stack.sh), since it writes to the database
// and the bucket. The browser gets a fake camera device and the camera permission of its context.
import assert from "node:assert/strict";
import { execFileSync } from "node:child_process";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { chromium } from "playwright";
import { API_URL, WEB_URL, byTestId, clearVacations, logInThroughKeycloak, passwordGrantToken, signOutThroughKeycloak } from "./support.mjs";

assert.equal(process.env.E2E_DB, "cichlids_e2e", "Refusing to run outside the cichlids_e2e database");
const COMPOSE_FILE = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../../../stack/compose.yaml");
const sql = statement => execFileSync("docker", ["compose", "-f", COMPOSE_FILE, "exec", "-T", "postgres", "psql", "-U", "cichlids", "-d", "cichlids_e2e", "-v", "ON_ERROR_STOP=1", "-Atc", statement], { encoding: "utf8" }).trim();

const DAY = 24 * 3600 * 1000;
const dateOf = offset => new Date(Date.now() + offset * DAY).toISOString().slice(0, 10);
// The app sends the local date of the browser; the contexts run in UTC so it is the date the spec uses.
const today = dateOf(0);
const token = await passwordGrantToken();
const headers = { Authorization: `Bearer ${token}`, "Content-Type": "application/json" };
const profileId = Number((await (await fetch(`${API_URL}/api/me`, { headers })).json()).profile.id);
clearVacations(profileId);

const newTank = async title => {
  const created = await fetch(`${API_URL}/api/tanks`, { method: "POST", headers, body: JSON.stringify({ title, isPublic: true, volume: { value: 200, unit: "liter" } }) });
  assert.equal(created.status, 201);
  return created.json();
};
const log = async (tank, daysAgo, body = { kind: "water_change" }) => {
  const response = await fetch(`${API_URL}/api/tanks/${tank.id}/care`, { method: "POST", headers, body: JSON.stringify({ ...body, occurredAt: new Date(Date.now() - daysAgo * DAY).toISOString() }) });
  assert.equal(response.status, 201);
};
const get = async url => (await fetch(`${API_URL}${url}`, { headers })).json();
const streakOf = async tank => (await get(`/api/tanks/${tank.id}/streaks?date=${today}&utcOffsetMinutes=0`)).waterChange;
const dueOf = async tank => (await get(`/api/me/care/due?date=${today}&utcOffsetMinutes=0`)).items.filter(item => String(item.tankId) === String(tank.id));
const heatmapOf = async tank => get(`/api/tanks/${tank.id}/care/heatmap?date=${today}&utcOffsetMinutes=0`);
const operationOf = async (tank, withToken = true) =>
  (await fetch(`${API_URL}/api/tanks/${tank.id}/decommissioning?date=${today}&utcOffsetMinutes=0`, withToken ? { headers } : {})).json();

// A tank with a weekly rhythm: water changes 12 and 5 days ago, a measurement rule of 3 days with a
// measurement 4 days ago.
const prepare = async title => {
  const tank = await newTank(title);
  await log(tank, 12);
  await log(tank, 5);
  const rule = await fetch(`${API_URL}/api/tanks/${tank.id}/care/plan/measurement?date=${today}`, { method: "PATCH", headers, body: JSON.stringify({ rhythmDays: 3 }) });
  assert.equal(rule.status, 200);
  await log(tank, 4, { kind: "measurement", values: [{ parameter: "ph", value: 7.2 }] });
  return tank;
};

const browser = await chromium.launch({ args: ["--use-fake-device-for-media-stream", "--use-fake-ui-for-media-stream"] });
const errors = [];
const sizeOk = async (page, ids) => {
  for (const id of ids) {
    const box = await byTestId(page, id).boundingBox();
    assert.ok(box && box.height >= 48 && box.width >= 48, `${id} is at least 48 by 48`);
  }
};
// Takes the photo with the fake camera and uses it; the screen returns to the tank page.
const photoAndUse = async (page, tank) => {
  await byTestId(page, "decommission-viewfinder").waitFor({ timeout: 30000 });
  await sizeOk(page, ["decommission-capture"]);
  // The fake camera needs a moment before it delivers frames.
  for (let attempt = 0; ; attempt++) {
    await byTestId(page, "decommission-capture").click();
    if (await byTestId(page, "decommission-review").waitFor({ timeout: 2000 }).then(() => true, () => false)) break;
    assert.ok(attempt < 40, "The camera never delivered a picture");
  }
  await sizeOk(page, ["decommission-use", "decommission-retake"]);
  await byTestId(page, "decommission-use").click();
  await page.waitForURL(new RegExp(`/tanks/${tank.id}$`), { timeout: 30000 });
};
const imageLoaded = (page, testId) => page.waitForFunction(id => {
  const image = document.querySelector(`[data-testid="${id}"] img, img[data-testid="${id}"]`);
  return image instanceof HTMLImageElement && image.naturalWidth > 0;
}, testId, { timeout: 30000 });

try {
  const all = await prepare("Decommission all care");
  const waterOnly = await prepare("Decommission water change only");
  assert.deepEqual((await dueOf(all)).map(item => item.kind).sort(), ["measurement", "water_change"]);
  assert.equal((await streakOf(all)).state, "running");

  const context = await browser.newContext({ locale: "en-US", timezoneId: "UTC", colorScheme: "light", viewport: { width: 390, height: 844 } });
  await context.grantPermissions(["camera"], { origin: new URL(WEB_URL).origin });
  // The fake camera shows the same first frame every time it opens, and a picture that served as
  // proof once is refused. A mark in a random color makes every shot a picture of its own.
  await context.addInitScript(() => {
    const original = HTMLCanvasElement.prototype.toDataURL;
    HTMLCanvasElement.prototype.toDataURL = function (...args) {
      const canvas = this.getContext("2d");
      if (canvas) {
        canvas.fillStyle = `rgb(${Math.floor(Math.random() * 256)}, ${Math.floor(Math.random() * 256)}, ${Math.floor(Math.random() * 256)})`;
        canvas.fillRect(0, 0, 48, 48);
      }
      return original.apply(this, args);
    };
  });
  const page = await context.newPage();
  page.on("pageerror", error => errors.push(String(error)));
  await page.goto(`${WEB_URL}/me?lang=en`, { waitUntil: "networkidle" });
  await logInThroughKeycloak(page);

  // The member decommissions the first tank: the app asks what pauses before the camera opens.
  await page.goto(`${WEB_URL}/tanks/${all.id}`, { waitUntil: "networkidle" });
  await byTestId(page, "tank-operation").waitFor({ timeout: 30000 });
  await sizeOk(page, ["tank-decommission"]);
  await byTestId(page, "tank-decommission").click();
  await byTestId(page, "decommission-scope").waitFor();
  assert.equal(await byTestId(page, "decommission-continue").isDisabled(), true, "the member has to answer first");
  await sizeOk(page, ["decommission-scope-all_care", "decommission-scope-water_change"]);
  await byTestId(page, "decommission-scope-all_care").click();
  await page.waitForSelector('[data-testid="decommission-scope-all_care"][aria-checked="true"]', { timeout: 10000 });
  await byTestId(page, "decommission-continue").click();
  assert.match(await byTestId(page, "decommission-hint").textContent(), /empty tank/);
  await photoAndUse(page, all);

  // All care is paused: no due care, a paused streak, a marked heatmap day, the badge on the list.
  await byTestId(page, "tank-operation-active").waitFor({ timeout: 30000 });
  assert.match(await byTestId(page, "tank-operation-active").textContent(), /All care is paused\./);
  await imageLoaded(page, `tank-operation-empty-${(await operationOf(all)).items[0].id}`);
  assert.deepEqual(await dueOf(all), [], "a decommissioned tank has no due care");
  assert.equal((await streakOf(all)).state, "paused");
  assert.ok((await heatmapOf(all)).decommissionedDays.includes(today));
  const listed = await get(`/api/tanks?userId=${(await get("/api/me")).profile.id}`);
  assert.equal(listed.items.find(item => String(item.id) === String(all.id)).decommissioned, true);
  await page.goto(`${WEB_URL}/tanks/${all.id}?view=care`, { waitUntil: "networkidle" });
  await byTestId(page, `care-heatmap-rest-${today}`).waitFor({ timeout: 30000 });
  assert.match(await byTestId(page, "care-heatmap-summary").textContent(), /Decommissioned days: 1/);
  await byTestId(page, "care-plan-paused-water_change").waitFor();
  await page.goto(`${WEB_URL}/today`, { waitUntil: "networkidle" });
  await byTestId(page, "today-due-care").waitFor({ timeout: 15000 }).catch(() => {});
  assert.equal(await byTestId(page, `today-due-${all.id}-water_change`).count(), 0, "the tank is not on Today");
  assert.equal(await byTestId(page, `today-due-${all.id}-measurement`).count(), 0);

  // A visitor sees the decommissioning with the photo.
  const visitor = await browser.newContext({ locale: "en-US", timezoneId: "UTC", colorScheme: "light", viewport: { width: 390, height: 844 } });
  const visitorPage = await visitor.newPage();
  visitorPage.on("pageerror", error => errors.push(String(error)));
  await visitorPage.goto(`${WEB_URL}/tanks/${all.id}`, { waitUntil: "networkidle" });
  await byTestId(visitorPage, "tank-operation-active").waitFor({ timeout: 30000 });
  await imageLoaded(visitorPage, `tank-operation-empty-${(await operationOf(all, false)).items[0].id}`);
  assert.equal(await byTestId(visitorPage, "tank-decommission").count(), 0, "a visitor cannot decommission");
  assert.equal((await operationOf(all, false)).decommissioned, true);

  // Putting the tank back into operation takes a photo of the filled tank.
  await page.goto(`${WEB_URL}/tanks/${all.id}`, { waitUntil: "networkidle" });
  await byTestId(page, "tank-recommission").waitFor({ timeout: 30000 });
  await byTestId(page, "tank-recommission").click();
  assert.match(await byTestId(page, "decommission-hint").textContent(), /filled tank/);
  await photoAndUse(page, all);
  await byTestId(page, "tank-operation-history").waitFor({ timeout: 30000 });
  const ended = (await operationOf(all)).items[0];
  assert.equal(ended.active, false);
  assert.equal(ended.endDate, today);
  await imageLoaded(page, `tank-operation-empty-${ended.id}`);
  await imageLoaded(page, `tank-operation-filled-${ended.id}`);
  // The plan runs from today and the streak continues with the next water change.
  const water = (await dueOf(all)).find(item => item.kind === "water_change");
  assert.equal(water.due.dueDate, dateOf(7));
  const streak = await streakOf(all);
  assert.equal(streak.state, "running");
  assert.equal(streak.deadline, dateOf(10));
  assert.equal(streak.current, 1);
  await log(all, 0);
  assert.equal((await streakOf(all)).current, 2, "the next water change continues the streak");
  assert.equal(sql(`select count(*) from media_link l join media_item m on m.id = l.media_item_id where l.tank_id = ${all.id} and l.role in ('decommission', 'recommission') and m.captured_in_app`), "2");

  // Six months of operation, one month decommissioned, one more month of operation: seven months.
  const [year, month, day] = today.split("-").map(Number);
  const anchor = Math.min(day, 28);
  const at = (months) => new Date(Date.UTC(year, month - 1 - months, anchor)).toISOString().slice(0, 10);
  const [created, began, resumed] = [at(8), at(2), at(1)];
  sql(`update tank set created_at = '${created}T08:00:00Z' where id = ${all.id}`);
  sql(`update tank_decommissioning set started_at = '${began}T10:00:00Z', start_date = '${began}', ended_at = '${resumed}T10:00:00Z', end_date = '${resumed}' where tank_id = ${all.id}`);
  await visitorPage.goto(`${WEB_URL}/tanks/${all.id}`, { waitUntil: "networkidle" });
  await byTestId(visitorPage, "tank-operation-time").waitFor({ timeout: 30000 });
  assert.match(await byTestId(visitorPage, "tank-operation-time").textContent(), /In operation for 7 months/);
  const days = Math.round((Date.parse(resumed) - Date.parse(began)) / DAY);
  assert.match(await byTestId(visitorPage, "tank-operation-rest").textContent(), new RegExp(`Decommissioned time: ${days} days`));

  // Only the water change is paused for the second tank: measurements stay due.
  await page.goto(`${WEB_URL}/tanks/${waterOnly.id}`, { waitUntil: "networkidle" });
  await byTestId(page, "tank-decommission").click();
  await byTestId(page, "decommission-scope-water_change").click();
  await byTestId(page, "decommission-continue").click();
  await photoAndUse(page, waterOnly);
  await byTestId(page, "tank-operation-active").waitFor({ timeout: 30000 });
  assert.match(await byTestId(page, "tank-operation-active").textContent(), /Measurements stay due/);
  assert.deepEqual((await dueOf(waterOnly)).map(item => item.kind), ["measurement"]);
  assert.equal((await streakOf(waterOnly)).state, "paused");
  assert.deepEqual((await heatmapOf(waterOnly)).decommissionedDays, []);

  // The dark theme keeps the card readable and the actions large enough.
  await page.emulateMedia({ colorScheme: "dark" });
  await page.goto(`${WEB_URL}/tanks/${waterOnly.id}`, { waitUntil: "networkidle" });
  await byTestId(page, "tank-recommission").waitFor({ timeout: 30000 });
  await sizeOk(page, ["tank-recommission"]);

  await page.goto(`${WEB_URL}/me?lang=en`, { waitUntil: "networkidle" });
  await signOutThroughKeycloak(page);
  assert.deepEqual(errors, []);
  console.log("decommission spec passed");
} finally {
  await browser.close();
  sql(`delete from tank_decommissioning where tank_id in (select id from tank where profile_id = ${profileId})`);
  clearVacations(profileId);
}
