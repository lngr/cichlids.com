// Story: TASK-5.21
// A vacation moves the deadline of a water change period, as a member of the web app. Vacation days
// count like any other day of the period; the deadline is the later of the regular deadline and the
// last vacation day plus the grace of the plan (weekly plan: grace 3 days, regular deadline day 10).
// - Vacation from day 5 to day 20: the deadline is day 23. On day 23 the streak runs, on day 24 it
//   is broken; a water change on day 23 continues the streak with one period.
// - Vacation from day 8 to day 9, entered afterwards: the deadline is day 12 instead of day 10.
// - After the vacation the due care names the reminder days from the day after it through the
//   new deadline; an active vacation leaves the streak paused without reminder days.
// - Today shows the new deadline and the vacation text explains the rule.
// Runs on the isolated write stack only (see e2e/write-stack.sh).
import assert from "node:assert/strict";
import { execFileSync } from "node:child_process";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { chromium } from "playwright";
import { API_URL, WEB_URL, byTestId, logInThroughKeycloak, passwordGrantToken, signOutThroughKeycloak } from "./support.mjs";

assert.equal(process.env.E2E_DB, "cichlids_e2e", "Refusing to run outside the cichlids_e2e database");
const COMPOSE_FILE = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../../../stack/compose.yaml");
const sql = statement => execFileSync("docker", ["compose", "-f", COMPOSE_FILE, "exec", "-T", "postgres", "psql", "-U", "cichlids", "-d", "cichlids_e2e", "-v", "ON_ERROR_STOP=1", "-Atc", statement], { encoding: "utf8" }).trim();

const DAY = 24 * 3600 * 1000;
const dateOf = offset => new Date(Date.now() + offset * DAY).toISOString().slice(0, 10);
const today = dateOf(0);

const token = await passwordGrantToken();
const headers = { Authorization: `Bearer ${token}`, "Content-Type": "application/json" };
const me = await (await fetch(`${API_URL}/api/me`, { headers })).json();
const profileId = Number(me.profile.id);
const clean = () => {
  sql(`delete from outbox_event where event_type in ('vacation.started', 'vacation.ended') and payload->>'profileId' = '${profileId}'`);
  sql(`delete from vacation where profile_id = ${profileId}`);
};
clean();

const newTank = async title => {
  const created = await fetch(`${API_URL}/api/tanks`, { method: "POST", headers, body: JSON.stringify({ title, isPublic: true, volume: { value: 200, unit: "liter" } }) });
  assert.equal(created.status, 201);
  return (await created.json()).id;
};
const logWaterChange = async (tank, daysAgo) => {
  const response = await fetch(`${API_URL}/api/tanks/${tank}/care`, { method: "POST", headers, body: JSON.stringify({ kind: "water_change", occurredAt: new Date(Date.now() - daysAgo * DAY).toISOString() }) });
  assert.equal(response.status, 201);
};
const streakOn = async (tank, date) => (await (await fetch(`${API_URL}/api/tanks/${tank}/streaks?date=${date}&utcOffsetMinutes=0`, { headers })).json()).waterChange;
const dueStreak = async (tank, date) => (await (await fetch(`${API_URL}/api/me/care/due?date=${date}&utcOffsetMinutes=0`, { headers })).json())
  .items.find(item => String(item.tankId) === String(tank) && item.kind === "water_change")?.streak;
const seedVacation = (firstOffset, lastOffset) => sql(`insert into vacation (profile_id, start_date, end_date, created_at) values (${profileId}, '${dateOf(firstOffset)}', '${dateOf(lastOffset)}', now())`);

const browser = await chromium.launch({ channel: "chromium" });
const errors = [];
let page;
try {
  // Vacation from day 5 to day 20 after a water change on day 0 (today is day 23): the deadline is today.
  const long = await newTank("Long vacation");
  await logWaterChange(long, 23);
  seedVacation(-18, -3);
  let streak = await streakOn(long, today);
  assert.equal(streak.state, "running");
  assert.equal(streak.deadline, today);
  assert.equal((await streakOn(long, dateOf(1))).state, "broken", "without a water change the streak breaks on day 24");
  assert.deepEqual(await dueStreak(long, today), { state: "running", reminderFrom: dateOf(-2), deadline: today }, "the reminders start the day after the vacation");

  // The water change on day 23 continues the streak with one period.
  await logWaterChange(long, 0);
  streak = await streakOn(long, today);
  assert.equal(streak.state, "running");
  assert.equal(streak.current, 1);
  assert.equal(streak.deadline, dateOf(10));
  clean();

  // Vacation from day 8 to day 9, entered afterwards on day 11: the deadline is day 12 instead of day 10.
  const short = await newTank("Short vacation");
  await logWaterChange(short, 11);
  assert.equal((await streakOn(short, today)).state, "broken", "the regular deadline passed yesterday");
  const recorded = await fetch(`${API_URL}/api/me/vacations?date=${today}`, { method: "POST", headers, body: JSON.stringify({ startDate: dateOf(-3), endDate: dateOf(-2) }) });
  assert.equal(recorded.status, 201);
  streak = await streakOn(short, today);
  assert.equal(streak.state, "running");
  assert.equal(streak.deadline, dateOf(1));
  assert.deepEqual(await dueStreak(short, today), { state: "running", reminderFrom: dateOf(-1), deadline: dateOf(1) });

  // Today shows the new deadline, and the vacation text explains the rule.
  const context = await browser.newContext({ locale: "en-US", timezoneId: "UTC", colorScheme: "light", viewport: { width: 390, height: 844 } });
  page = await context.newPage();
  page.on("pageerror", error => errors.push(String(error)));
  await page.goto(`${WEB_URL}/me?lang=en`, { waitUntil: "networkidle" });
  await logInThroughKeycloak(page);
  await page.goto(`${WEB_URL}/?lang=en`, { waitUntil: "networkidle" });
  const expected = `Next water change by ${new Intl.DateTimeFormat("en", { year: "numeric", month: "short", day: "numeric", timeZone: "UTC" }).format(new Date(`${dateOf(1)}T00:00:00Z`))}`;
  await page.waitForFunction(([id, text]) => document.querySelector(`[data-testid="today-streak-${id}-water-change"]`)?.textContent?.includes(text), [short, expected], { timeout: 30000 });
  await page.goto(`${WEB_URL}/me?lang=en`, { waitUntil: "networkidle" });
  await byTestId(page, "vacation-settings").waitFor({ timeout: 30000 });
  assert.match(await byTestId(page, "vacation-settings").innerText(), /A water change can follow within the grace days of your plan after your last vacation day/);

  // An active vacation pauses the streak and leaves no reminder day.
  const active = await fetch(`${API_URL}/api/me/vacations?date=${today}`, { method: "POST", headers, body: JSON.stringify({}) });
  assert.equal(active.status, 201);
  assert.equal((await streakOn(short, today)).state, "paused");
  assert.deepEqual(await dueStreak(short, today), { state: "paused", reminderFrom: null, deadline: null });

  await signOutThroughKeycloak(page);
  await context.close();
  assert.deepEqual(errors, []);
  console.log("TASK-5.21: a vacation from day 5 to day 20 moves the deadline to day 23 and the water change on that day continues the streak, a vacation from day 8 to day 9 moves it to day 12, the reminders start the day after the vacation and an active vacation pauses the streak.");
} catch (error) {
  console.error(errors, await page?.locator("body").innerText().catch(() => ""));
  await page?.screenshot({ path: "/tmp/task-5.21-streak-vacation-deadline-failure.png" }).catch(() => {});
  throw error;
} finally {
  await browser.close();
  clean();
}
