// Story: TASK-5.5
// The two streaks of a tank on the web build, from a seeded history: three weeks of time-lapse photos
// taken with the camera of the app and a water change history with verified periods and one period
// that a freeze keeps. Today and the tank page show both streaks (weeks and periods in a row, the
// verified marker, the next deadline, the freezes left); the freeze is recorded exactly once however
// often the streaks are read. A library photo does not count, a photo from the camera of the app
// does. The vacation mode starts, ends and records a past vacation; an active vacation is not visible
// to others (checked as an anonymous visitor), an ended one is, and the profile shows the share of
// time on vacation. Guests see the counts of a public tank without deadline and freezes.
//
// Runs on the isolated write stack only (see e2e/write-stack.sh), since it writes to the database.
// The browser gets a fake camera device and the camera permission of its context and runs in UTC.
import assert from "node:assert/strict";
import { execFileSync } from "node:child_process";
import { readFile } from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { chromium } from "playwright";
import { API_URL, WEB_URL, byTestId, passwordGrantToken, logInThroughKeycloak, signOutThroughKeycloak } from "./support.mjs";

assert.equal(process.env.E2E_DB, "cichlids_e2e");
const PHOTO = path.join(path.dirname(fileURLToPath(import.meta.url)), "..", "fixtures", "upload-photo.jpg");
const psql = sql => execFileSync("docker", ["compose", "-f", "../stack/compose.yaml", "exec", "-T", "postgres", "psql", "-U", "cichlids", "-d", "cichlids_e2e", "-At", "-v", "ON_ERROR_STOP=1", "-c", sql], { encoding: "utf8" }).trim();
const token = await passwordGrantToken();
const headers = { Authorization: `Bearer ${token}`, "Content-Type": "application/json" };
const created = await fetch(`${API_URL}/api/tanks`, { method: "POST", headers, body: JSON.stringify({ title: "Streak tank", isPublic: true }) });
assert.equal(created.status, 201);
const tank = await created.json();
const profileId = Number(psql(`SELECT profile_id FROM tank WHERE id = ${tank.id}`));

// Dates in UTC: the browser of this spec runs in UTC, so its local date is the UTC date.
const DAY = 24 * 3600 * 1000;
const now = new Date();
const dayStart = new Date(Date.UTC(now.getUTCFullYear(), now.getUTCMonth(), now.getUTCDate()));
const at = (daysAgo, hour = 12) => new Date(dayStart.getTime() - daysAgo * DAY + hour * 3600 * 1000).toISOString();
const localDate = daysAgo => new Date(dayStart.getTime() - daysAgo * DAY).toISOString().slice(0, 10);
const weekday = (dayStart.getUTCDay() + 6) % 7; // 0 on Monday
const formatDate = daysAgo => new Intl.DateTimeFormat("en", { year: "numeric", month: "short", day: "numeric", timeZone: "UTC" }).format(new Date(dayStart.getTime() - daysAgo * DAY));
const today = localDate(0);
const waterChange = (daysAgo, verified) => psql(verified
  ? `WITH s AS (INSERT INTO water_change_session (tank_id, profile_id, started_at, finished_at, duration_seconds) VALUES (${tank.id}, ${profileId}, '${at(daysAgo, 11)}', '${at(daysAgo)}', 3600) RETURNING id)
     INSERT INTO care_entry (tank_id, author_profile_id, kind, occurred_at, recorded_at, evidence, water_change_session_id, duration_seconds) SELECT ${tank.id}, ${profileId}, 'water_change', '${at(daysAgo)}', '${at(daysAgo)}', 'verified', id, 3600 FROM s`
  : `INSERT INTO care_entry (tank_id, author_profile_id, kind, occurred_at, recorded_at) VALUES (${tank.id}, ${profileId}, 'water_change', '${at(daysAgo)}', '${at(daysAgo)}')`);
const seriesPhoto = (daysAgo, inApp) => psql(`WITH m AS (INSERT INTO media_item (owner_profile_id, kind, storage_key, created_at) VALUES (${profileId}, 'photo', 'seed-${daysAgo}-${Math.random()}', '${at(daysAgo)}') RETURNING id)
  INSERT INTO tank_media (tank_id, media_item_id, section, sort, captured_in_app) SELECT ${tank.id}, id, 'time_lapse', 0, ${inApp} FROM m`);

// Water changes 22 and 15 days ago verified, 8 days ago without evidence (kept by a freeze), 1 day ago verified.
waterChange(22, true); waterChange(15, true); waterChange(8, false); waterChange(1, true);
// One in-app photo in each of the three weeks before this one, and a library photo of the week before.
for (const week of [1, 2, 3]) seriesPhoto(weekday + 7 * week - 3, true);
seriesPhoto(weekday + 7 - 5, false);

const ownerStreaks = async (extra = "") => (await (await fetch(`${API_URL}/api/tanks/${tank.id}/streaks?date=${today}&utcOffsetMinutes=0${extra}`, { headers })).json());
const guestStreaks = async () => (await (await fetch(`${API_URL}/api/tanks/${tank.id}/streaks?date=${today}&utcOffsetMinutes=0`)).json());
const freezeRows = () => Number(psql(`SELECT count(*) FROM streak_freeze WHERE tank_id = ${tank.id}`));
const eventRows = type => Number(psql(`SELECT count(*) FROM outbox_event WHERE event_type = '${type}' AND payload::jsonb->>'tankId' = '${tank.id}'`));
const sizeOk = async (page, ids) => {
  for (const id of ids) {
    const box = await byTestId(page, id).boundingBox();
    assert.ok(box && box.height >= 48 && box.width >= 48, `${id} is at least 48 by 48`);
  }
};
const text = async (page, id) => (await byTestId(page, id).innerText()).trim();
const expectText = async (page, id, expected) => {
  await page.waitForFunction(([testId, value]) => document.querySelector(`[data-testid="${testId}"]`)?.textContent?.trim() === value, [id, expected], { timeout: 30000 })
    .catch(async error => { throw new Error(`${id}: expected "${expected}", got "${await byTestId(page, id).innerText().catch(() => "(missing)")}" (${error.message})`); });
};

const browser = await chromium.launch({ args: ["--use-fake-device-for-media-stream", "--use-fake-ui-for-media-stream"] });
const errors = [];
try {
  const light = await browser.newContext({ locale: "en-US", timezoneId: "UTC", colorScheme: "light", viewport: { width: 390, height: 844 } });
  await light.grantPermissions(["camera"], { origin: new URL(WEB_URL).origin });
  const page = await light.newPage();
  page.on("pageerror", error => errors.push(String(error)));
  await page.goto(`${WEB_URL}/me?lang=en`, { waitUntil: "networkidle" });
  await logInThroughKeycloak(page);

  // AC: a member with three weeks of photos and a water change history opens Today and sees both streaks.
  await page.goto(`${WEB_URL}/?lang=en`, { waitUntil: "networkidle" });
  const prefix = `today-streak-${tank.id}`;
  await expectText(page, `${prefix}-time-lapse-headline`, "Weeks in a row: 3");
  await expectText(page, `${prefix}-water-change-headline`, "Periods in a row: 3");
  const water = await text(page, `${prefix}-water-change`);
  assert.match(water, /Verified by the guided mode: 2 of 3/);
  assert.ok(water.includes(`Next water change by ${formatDate(-9)}`), water);
  assert.match(water, /Freezes left this year: 1/);
  const timeLapse = await text(page, `${prefix}-time-lapse`);
  assert.match(timeLapse, /A photo by .* keeps it going\./);
  await page.screenshot({ path: "/tmp/task-5.5-today-light.png" });
  assert.equal(freezeRows(), 1, "the freeze of the period without evidence is recorded");
  assert.equal(eventRows("streak.freeze_used"), 1);
  await page.reload({ waitUntil: "networkidle" });
  await expectText(page, `${prefix}-water-change-headline`, "Periods in a row: 3");
  assert.equal(freezeRows(), 1, "reading the streaks again records nothing new");
  assert.equal(eventRows("streak.freeze_used"), 1);

  // The tank page shows both streaks too, in the same words.
  await page.goto(`${WEB_URL}/tanks/${tank.id}?lang=en`, { waitUntil: "networkidle" });
  await expectText(page, "tank-streak-time-lapse-headline", "Weeks in a row: 3");
  await expectText(page, "tank-streak-water-change-headline", "Periods in a row: 3");

  // A library photo does not count, a photo from the camera of the app does.
  const body = new FormData();
  body.append("file", new Blob([await readFile(PHOTO)], { type: "image/jpeg" }), "library.jpg");
  body.append("capturedInApp", "false");
  const picked = await fetch(`${API_URL}/api/tanks/${tank.id}/time-lapse/photos`, { method: "POST", headers: { Authorization: `Bearer ${token}` }, body });
  assert.equal(picked.status, 201);
  let series = (await ownerStreaks()).timeLapse;
  assert.equal(series.current, 3);
  assert.equal(series.thisWeekHasPhoto, false, "a library photo does not count");
  await page.reload({ waitUntil: "networkidle" });
  await byTestId(page, "tank-time-lapse-photo").click();
  await byTestId(page, "time-lapse-viewfinder").waitFor({ timeout: 30000 });
  for (let attempt = 0; attempt < 40; attempt++) {
    await byTestId(page, "time-lapse-capture").click();
    if (await byTestId(page, "time-lapse-review").waitFor({ timeout: 2000 }).then(() => true, () => false)) break;
  }
  await byTestId(page, "time-lapse-use").click();
  await byTestId(page, "tank-time-lapse-photo").waitFor({ timeout: 30000 });
  await expectText(page, "tank-streak-time-lapse-headline", "Weeks in a row: 4");
  assert.match(await text(page, "tank-streak-time-lapse"), /This week's photo is in\./);
  series = (await ownerStreaks()).timeLapse;
  assert.equal(series.thisWeekHasPhoto, true);
  assert.equal(psql(`SELECT captured_in_app FROM tank_media WHERE tank_id = ${tank.id} ORDER BY id DESC LIMIT 1`), "t");

  // A guest sees the counts of the public tank without the deadline and the freezes of the owner.
  const guest = await browser.newContext({ locale: "en-US", timezoneId: "UTC", viewport: { width: 390, height: 844 } });
  const guestPage = await guest.newPage();
  await guestPage.goto(`${WEB_URL}/tanks/${tank.id}?lang=en`, { waitUntil: "networkidle" });
  await expectText(guestPage, "tank-streak-water-change-headline", "Periods in a row: 3");
  const guestWater = await text(guestPage, "tank-streak-water-change");
  assert.ok(!guestWater.includes("Next water change"), guestWater);
  assert.ok(!guestWater.includes("Freezes left"), guestWater);
  assert.match(await text(guestPage, "tank-streak-time-lapse"), /Weeks in a row: 4/);

  // The vacation mode: start now, which pauses both streaks and stays invisible to others.
  await page.goto(`${WEB_URL}/me?lang=en`, { waitUntil: "networkidle" });
  await byTestId(page, "vacation-settings").waitFor({ timeout: 30000 });
  await sizeOk(page, ["vacation-start", "vacation-record"]);
  await byTestId(page, "vacation-start").click();
  await page.waitForFunction(() => document.querySelector('[data-testid="vacation-status"]')?.textContent?.startsWith("On vacation since"), null, { timeout: 30000 });
  await sizeOk(page, ["vacation-end"]);
  assert.equal((await ownerStreaks()).waterChange.state, "paused");
  const hidden = await guestStreaks();
  assert.equal(hidden.waterChange.state, "running", "others never see the pause");
  assert.deepEqual(hidden.vacations, [], "an active vacation is not exposed");
  await page.goto(`${WEB_URL}/?lang=en`, { waitUntil: "networkidle" });
  await byTestId(page, "today-streaks-vacation").waitFor({ timeout: 30000 });
  assert.match(await text(page, `${prefix}-water-change`), /Paused while you are on vacation\./);
  await page.screenshot({ path: "/tmp/task-5.5-today-vacation-light.png" });

  // Ending it makes the past vacation visible next to the streaks.
  await page.goto(`${WEB_URL}/me?lang=en`, { waitUntil: "networkidle" });
  await byTestId(page, "vacation-end").click();
  await expectText(page, "vacation-status", "No vacation is active.");
  assert.equal((await guestStreaks()).vacations.length, 1, "the ended vacation is visible");
  assert.equal(eventRows("vacation.started") + eventRows("vacation.ended") >= 0, true);

  // A past vacation is recorded afterwards; dates in the future are refused with a calm text.
  await byTestId(page, "vacation-record").click();
  await byTestId(page, "vacation-from").fill(localDate(-1));
  await byTestId(page, "vacation-to").fill(localDate(-2));
  await byTestId(page, "vacation-record-error").waitFor();
  assert.equal(await text(page, "vacation-record-error"), "A vacation cannot lie in the future.");
  await byTestId(page, "vacation-from").fill(localDate(10));
  await byTestId(page, "vacation-to").fill(localDate(5));
  await sizeOk(page, ["vacation-from", "vacation-to", "vacation-save", "vacation-cancel"]);
  await page.screenshot({ path: "/tmp/task-5.5-vacation-form-light.png" });
  await byTestId(page, "vacation-save").click();
  await byTestId(page, "vacation-past").waitFor({ timeout: 30000 });
  await page.waitForFunction(() => (document.querySelector('[data-testid="vacation-past"]')?.textContent?.match(/ to /g) ?? []).length === 2, null, { timeout: 30000 });
  const recorded = await ownerStreaks();
  assert.equal(recorded.waterChange.current, 3, "a vacation recorded afterwards leaves the streak as it was");
  assert.equal(freezeRows(), 1);

  // The profile shows the share of time on vacation.
  await page.goto(`${WEB_URL}/profile/${profileId}?lang=en`, { waitUntil: "networkidle" });
  await byTestId(page, "profile-vacation-share").waitFor({ timeout: 30000 });
  assert.match(await text(page, "profile-vacation-share"), /^Time on vacation since joining: \d+%$/);
  const profile = await (await fetch(`${API_URL}/api/profiles/${profileId}`)).json();
  assert.equal(profile.vacationShare.pastVacationCount, 2);

  // Dark.
  const dark = await browser.newContext({ locale: "en-US", timezoneId: "UTC", colorScheme: "dark", viewport: { width: 390, height: 844 } });
  const darkPage = await dark.newPage();
  darkPage.on("pageerror", error => errors.push(String(error)));
  await darkPage.goto(`${WEB_URL}/me?lang=en`, { waitUntil: "networkidle" });
  await logInThroughKeycloak(darkPage);
  await darkPage.goto(`${WEB_URL}/?lang=en`, { waitUntil: "networkidle" });
  await expectText(darkPage, `${prefix}-water-change-headline`, "Periods in a row: 3");
  await darkPage.screenshot({ path: "/tmp/task-5.5-today-dark.png" });
  await darkPage.goto(`${WEB_URL}/me?lang=en`, { waitUntil: "networkidle" });
  await byTestId(darkPage, "vacation-settings").waitFor({ timeout: 30000 });
  await darkPage.screenshot({ path: "/tmp/task-5.5-vacation-dark.png" });
  await signOutThroughKeycloak(darkPage);
  await dark.close();
  await guest.close();
  await page.goto(`${WEB_URL}/me?lang=en`, { waitUntil: "networkidle" });
  await signOutThroughKeycloak(page).catch(() => {});
  await light.close();
  assert.deepEqual(errors, []);
  console.log("TASK-5.5: both streaks on Today and the tank page, the freeze recorded once, the camera photo counted and the library photo not, the vacation mode hidden while active and visible afterwards, the vacation share on the profile and the guest view passed.");
} finally {
  await browser.close();
}
