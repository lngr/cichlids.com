// Story: TASK-5.17
// The care heatmap on the web build, from a seeded history: a tank with water changes and measurements
// on several days of the last 16 weeks, one day outside the window and one entry marked only me.
// The care view of the tank marks exactly the cared days, with a dot on the days with a measurement;
// the owner also sees the only-me day, an anonymous visitor of the public tank does not. The grid has
// 16 week columns of 7 days, reads as one summary for a screen reader and has a list fallback with
// 48dp controls. Today shows the own tanks summed. A tank with a private care stream answers a visitor
// with no heatmap at all. Light and dark.
//
// Runs on the isolated write stack only (see e2e/write-stack.sh), since it writes to the database.
// The browser runs in UTC, so its local date is the UTC date.
import assert from "node:assert/strict";
import { execFileSync } from "node:child_process";
import { chromium } from "playwright";
import { API_URL, WEB_URL, byTestId, passwordGrantToken, logInThroughKeycloak, signOutThroughKeycloak } from "./support.mjs";

assert.equal(process.env.E2E_DB, "cichlids_e2e");
const psql = sql => execFileSync("docker", ["compose", "-f", "../stack/compose.yaml", "exec", "-T", "postgres", "psql", "-U", "cichlids", "-d", "cichlids_e2e", "-At", "-v", "ON_ERROR_STOP=1", "-c", sql], { encoding: "utf8" }).trim();
const token = await passwordGrantToken();
const headers = { Authorization: `Bearer ${token}`, "Content-Type": "application/json" };
const createTank = async (body) => {
  const response = await fetch(`${API_URL}/api/tanks`, { method: "POST", headers, body: JSON.stringify(body) });
  assert.equal(response.status, 201);
  return response.json();
};
const tank = await createTank({ title: "Heatmap tank", isPublic: true });
const profileId = Number(psql(`SELECT profile_id FROM tank WHERE id = ${tank.id}`));

const DAY = 24 * 3600 * 1000;
const now = new Date();
const dayStart = new Date(Date.UTC(now.getUTCFullYear(), now.getUTCMonth(), now.getUTCDate()));
const at = daysAgo => new Date(dayStart.getTime() - daysAgo * DAY + 12 * 3600 * 1000).toISOString();
const localDate = daysAgo => new Date(dayStart.getTime() - daysAgo * DAY).toISOString().slice(0, 10);
const addEntry = (tankId, kind, daysAgo, onlyMe = false) => psql(
  `INSERT INTO care_entry (tank_id, author_profile_id, kind, occurred_at, recorded_at, only_me) VALUES (${tankId}, ${profileId}, '${kind}', '${at(daysAgo)}', '${at(daysAgo)}', ${onlyMe})`);

// Water changes 0, 10 and 52 days ago (two on day 10), measurements 3 and 10 days ago, a water change
// 120 days ago outside the window and a measurement only the owner sees 17 days ago.
addEntry(tank.id, "water_change", 0);
addEntry(tank.id, "water_change", 10); addEntry(tank.id, "water_change", 10);
addEntry(tank.id, "water_change", 52);
addEntry(tank.id, "measurement", 3); addEntry(tank.id, "measurement", 10);
addEntry(tank.id, "water_change", 120);
addEntry(tank.id, "measurement", 17, true);
const publicDays = [0, 3, 10, 52].map(localDate).sort();
const ownerDays = [0, 3, 10, 17, 52].map(localDate).sort();

const markedDays = async page => (await page.locator('[data-testid^="care-heatmap-marked-"]').evaluateAll(
  nodes => nodes.map(node => node.getAttribute("data-testid").replace("care-heatmap-marked-", "")))).sort();
const cellCount = page => page.locator('[data-testid^="care-heatmap-marked-"], [data-testid^="care-heatmap-cell-"]').count();
const sizeOk = async (page, id) => {
  const box = await byTestId(page, id).boundingBox();
  assert.ok(box && box.height >= 48 && box.width >= 48, `${id} is at least 48 by 48`);
};

const browser = await chromium.launch();
const errors = [];
try {
  const light = await browser.newContext({ locale: "en-US", timezoneId: "UTC", colorScheme: "light", viewport: { width: 390, height: 844 } });
  const page = await light.newPage();
  page.on("pageerror", error => errors.push(String(error)));
  await page.goto(`${WEB_URL}/me?lang=en`, { waitUntil: "networkidle" });
  await logInThroughKeycloak(page);

  // AC #1: the care view of the tank marks exactly the days with care.
  await page.goto(`${WEB_URL}/tanks/${tank.id}?lang=en`, { waitUntil: "networkidle" });
  await byTestId(page, "tank-view-care").click();
  await byTestId(page, "care-heatmap-grid").waitFor({ timeout: 30000 });
  assert.deepEqual(await markedDays(page), ownerDays);
  assert.equal(await cellCount(page), 16 * 7, "16 week columns of 7 days");
  const summary = await byTestId(page, "care-heatmap-grid").getAttribute("aria-label");
  assert.match(summary, /^Care days in the last 16 weeks: 5\. Last water change: /);
  assert.equal((await byTestId(page, "care-heatmap-summary").innerText()).trim(), summary);

  // Server and screen agree: the API answers the same days.
  const own = await (await fetch(`${API_URL}/api/tanks/${tank.id}/care/heatmap?date=${localDate(0)}&utcOffsetMinutes=0`, { headers })).json();
  assert.deepEqual(own.days.map(day => day.date).sort(), ownerDays);
  assert.deepEqual(own.days.find(day => day.date === localDate(10)), { date: localDate(10), waterChanges: 2, measurements: 1 });

  // The list fallback names the days newest first and its controls are 48dp.
  await sizeOk(page, "care-heatmap-toggle");
  await byTestId(page, "care-heatmap-toggle").click();
  const items = await page.locator('[data-testid^="care-heatmap-item-"]').evaluateAll(nodes => nodes.map(node => node.getAttribute("data-testid").replace("care-heatmap-item-", "")));
  assert.deepEqual(items, [...ownerDays].reverse());
  assert.match((await byTestId(page, `care-heatmap-item-${localDate(10)}`).innerText()).trim(), /: Water changes: 2, Measurements: 1$/);
  await page.screenshot({ path: "/tmp/task-5.17-tank-light.png" });

  // Today sums the own tanks: the marked days are the days with care in any own tank.
  await page.goto(`${WEB_URL}/?lang=en`, { waitUntil: "networkidle" });
  await byTestId(page, "care-heatmap-grid").waitFor({ timeout: 30000 });
  const sinceMonday = (dayStart.getUTCDay() + 6) % 7 + 15 * 7;
  const windowStart = localDate(sinceMonday);
  const all = psql(`SELECT DISTINCT to_char(occurred_at AT TIME ZONE 'UTC', 'YYYY-MM-DD') FROM care_entry e JOIN tank t ON t.id = e.tank_id WHERE t.profile_id = ${profileId} AND t.deleted_at IS NULL AND occurred_at AT TIME ZONE 'UTC' >= '${windowStart}' ORDER BY 1`).split("\n").filter(Boolean);
  assert.deepEqual(await markedDays(page), all);
  for (const day of ownerDays) assert.ok(all.includes(day), `${day} is in the sum`);
  await page.screenshot({ path: "/tmp/task-5.17-today-light.png" });

  // A visitor of the public tank sees the days without the only-me entry.
  const guest = await browser.newContext({ locale: "en-US", timezoneId: "UTC", viewport: { width: 390, height: 844 } });
  const guestPage = await guest.newPage();
  guestPage.on("pageerror", error => errors.push(String(error)));
  await guestPage.goto(`${WEB_URL}/tanks/${tank.id}?lang=en`, { waitUntil: "networkidle" });
  await byTestId(guestPage, "tank-view-care").click();
  await byTestId(guestPage, "care-heatmap-grid").waitFor({ timeout: 30000 });
  assert.deepEqual(await markedDays(guestPage), publicDays);
  assert.match(await byTestId(guestPage, "care-heatmap-grid").getAttribute("aria-label"), /^Care days in the last 16 weeks: 4\./);
  const guestApi = await fetch(`${API_URL}/api/tanks/${tank.id}/care/heatmap?date=${localDate(0)}&utcOffsetMinutes=0`);
  assert.deepEqual((await guestApi.json()).days.map(day => day.date).sort(), publicDays);

  // A tank with a private care stream gives a visitor no heatmap, as a missing tank.
  const hidden = await createTank({ title: "Heatmap hidden stream", isPublic: true });
  psql(`UPDATE tank SET stream_visibility = 'private' WHERE id = ${hidden.id}`);
  addEntry(hidden.id, "water_change", 2);
  const refused = await fetch(`${API_URL}/api/tanks/${hidden.id}/care/heatmap?date=${localDate(0)}&utcOffsetMinutes=0`);
  assert.equal(refused.status, 404);
  assert.equal((await refused.json()).code, "care_not_found");
  await guestPage.goto(`${WEB_URL}/tanks/${hidden.id}?lang=en`, { waitUntil: "networkidle" });
  await byTestId(guestPage, "tank-view-care").click();
  assert.equal(await byTestId(guestPage, "care-heatmap-grid").count(), 0);
  await guest.close();

  // Dark.
  const dark = await browser.newContext({ locale: "en-US", timezoneId: "UTC", colorScheme: "dark", viewport: { width: 390, height: 844 } });
  const darkPage = await dark.newPage();
  darkPage.on("pageerror", error => errors.push(String(error)));
  await darkPage.goto(`${WEB_URL}/me?lang=en`, { waitUntil: "networkidle" });
  await logInThroughKeycloak(darkPage);
  await darkPage.goto(`${WEB_URL}/tanks/${tank.id}?lang=en`, { waitUntil: "networkidle" });
  await byTestId(darkPage, "tank-view-care").click();
  await byTestId(darkPage, "care-heatmap-grid").waitFor({ timeout: 30000 });
  assert.deepEqual(await markedDays(darkPage), ownerDays);
  await byTestId(darkPage, "care-heatmap").screenshot({ path: "/tmp/task-5.17-tank-dark.png" });
  await darkPage.goto(`${WEB_URL}/me?lang=en`, { waitUntil: "networkidle" });
  await signOutThroughKeycloak(darkPage);
  await dark.close();
  await page.goto(`${WEB_URL}/me?lang=en`, { waitUntil: "networkidle" });
  await signOutThroughKeycloak(page).catch(() => {});
  await light.close();
  assert.deepEqual(errors, []);
  console.log("TASK-5.17: the care view marks exactly the cared days of the last 16 weeks, Today sums the own tanks, a visitor never sees the only-me day or a private stream, and the list fallback and the 48dp control passed.");
} finally {
  await browser.close();
}
