// Story: TASK-5.19
// A member turns the streaks off with one switch in the settings of Me, as a member of the web app:
// - With streaks on, Today and the tank page show the streak, a visitor sees it on the public tank,
//   and the due care names the reminder days.
// - With the switch off, Today and the tank page show no streak, the vacation mode is hidden, a
//   visitor gets neither streak of the tank and the due care carries no reminder days, so the member
//   gets the single reminder on the due day.
// - A water change logged while the switch is off counts: with the switch on again the streak shows
//   the state it would have had all along.
//
// Runs on the isolated write stack only (see e2e/write-stack.sh). The switch is on again at the end
// so the other specs of the shared member see streaks.
import assert from "node:assert/strict";
import { chromium } from "playwright";
import { API_URL, WEB_URL, byTestId, logInThroughKeycloak, passwordGrantToken, signOutThroughKeycloak } from "./support.mjs";

assert.equal(process.env.E2E_DB, "cichlids_e2e", "Refusing to run outside the cichlids_e2e database");

const DAY = 24 * 3600 * 1000;
const dateOf = offset => new Date(Date.now() + offset * DAY).toISOString().slice(0, 10);
const today = dateOf(0);

const token = await passwordGrantToken();
const headers = { Authorization: `Bearer ${token}`, "Content-Type": "application/json" };
const setStreaks = async enabled => {
  const response = await fetch(`${API_URL}/api/me/settings`, { method: "PUT", headers, body: JSON.stringify({ streaksEnabled: enabled }) });
  assert.equal(response.status, 200);
};
await setStreaks(true);
const created = await fetch(`${API_URL}/api/tanks`, { method: "POST", headers, body: JSON.stringify({ title: "Streak switch path", isPublic: true, volume: { value: 200, unit: "liter" } }) });
assert.equal(created.status, 201);
const tank = await created.json();
const logWaterChange = async occurredAt => {
  const response = await fetch(`${API_URL}/api/tanks/${tank.id}/care`, { method: "POST", headers, body: JSON.stringify({ kind: "water_change", occurredAt }) });
  assert.equal(response.status, 201);
};
await logWaterChange(new Date(Date.now() - 7 * DAY).toISOString());

const dueItem = async () => (await (await fetch(`${API_URL}/api/me/care/due?date=${today}&utcOffsetMinutes=0`, { headers })).json())
  .items.find(item => String(item.tankId) === String(tank.id) && item.kind === "water_change");
const tankStreaks = async asOwner => (await (await fetch(`${API_URL}/api/tanks/${tank.id}/streaks?date=${today}&utcOffsetMinutes=0`, { headers: asOwner ? headers : {} })).json());
const sizeOk = async (page, id) => {
  const box = await byTestId(page, id).boundingBox();
  assert.ok(box && box.height >= 48 && box.width >= 48, `${id} is at least 48 by 48`);
};
const expectText = async (page, id, expected) => {
  await page.waitForFunction(([testId, value]) => document.querySelector(`[data-testid="${testId}"]`)?.textContent?.trim() === value, [id, expected], { timeout: 30000 })
    .catch(async error => { throw new Error(`${id}: expected "${expected}", got "${await byTestId(page, id).innerText().catch(() => "(missing)")}" (${error.message})`); });
};
/** Opens a page and waits until the streak request of the screen has been answered. */
const openAndRead = async (page, url, requestPath) => {
  const answered = page.waitForResponse(response => response.url().includes(requestPath) && response.status() === 200, { timeout: 30000 });
  await page.goto(url, { waitUntil: "networkidle" });
  await answered;
  await page.waitForLoadState("networkidle");
};

const browser = await chromium.launch({ channel: "chromium" });
const errors = [];
let page;
try {
  const context = await browser.newContext({ locale: "en-US", timezoneId: "UTC", colorScheme: "light", viewport: { width: 390, height: 844 } });
  page = await context.newPage();
  page.on("pageerror", error => errors.push(String(error)));
  await page.goto(`${WEB_URL}/me?lang=en`, { waitUntil: "networkidle" });
  await logInThroughKeycloak(page);

  // Streaks on: the streak is shown on Today and on the tank page, to a visitor, and in the due care.
  await openAndRead(page, `${WEB_URL}/me?lang=en`, "/api/me/settings");
  await expectText(page, "streak-switch-state", "Streaks are on.");
  await byTestId(page, "vacation-settings").waitFor({ timeout: 30000 });
  await sizeOk(page, "streak-switch-toggle");
  await page.screenshot({ path: "/tmp/task-5.19-me-on-light.png" });
  await openAndRead(page, `${WEB_URL}/?lang=en`, "/api/me/streaks");
  await byTestId(page, `today-streak-${tank.id}`).waitFor({ timeout: 30000 });
  await openAndRead(page, `${WEB_URL}/tanks/${tank.id}?lang=en`, `/api/tanks/${tank.id}/streaks`);
  await byTestId(page, "tank-streaks").waitFor({ timeout: 30000 });
  assert.ok((await tankStreaks(false)).timeLapse, "a visitor sees the time-lapse streak");
  assert.deepEqual((await dueItem()).streak, { state: "running", reminderFrom: today, deadline: dateOf(3) });

  // Streaks off with the switch in Me.
  await openAndRead(page, `${WEB_URL}/me?lang=en`, "/api/me/settings");
  await byTestId(page, "streak-switch-toggle").click();
  await expectText(page, "streak-switch-state", "Streaks are off.");
  await expectText(page, "streak-switch-toggle", "Switch streaks on");
  assert.equal(await byTestId(page, "vacation-settings").count(), 0, "the vacation mode is hidden");
  await page.screenshot({ path: "/tmp/task-5.19-me-off-light.png" });
  await openAndRead(page, `${WEB_URL}/?lang=en`, "/api/me/streaks");
  assert.equal(await byTestId(page, "today-streaks").count(), 0, "Today shows no streak");
  await openAndRead(page, `${WEB_URL}/tanks/${tank.id}?lang=en`, `/api/tanks/${tank.id}/streaks`);
  assert.equal(await byTestId(page, "tank-streaks").count(), 0, "the tank page shows no streak");
  for (const asOwner of [false, true]) {
    const streaks = await tankStreaks(asOwner);
    assert.equal(streaks.waterChange, null);
    assert.equal(streaks.timeLapse, null);
    assert.deepEqual(streaks.vacations, []);
  }
  assert.ok(!(await dueItem()).streak, "the due care names no reminder days, so only the reminder on the due day remains");

  // A visitor of the tank page sees no streak either.
  const guest = await browser.newContext({ locale: "en-US", timezoneId: "UTC", viewport: { width: 390, height: 844 } });
  const guestPage = await guest.newPage();
  await openAndRead(guestPage, `${WEB_URL}/tanks/${tank.id}?lang=en`, `/api/tanks/${tank.id}/streaks`);
  assert.equal(await byTestId(guestPage, "tank-streaks").count(), 0, "a visitor sees no streak");
  await guest.close();

  // A water change logged while the streaks are off counts when they are on again.
  await logWaterChange(new Date().toISOString());
  await openAndRead(page, `${WEB_URL}/me?lang=en`, "/api/me/settings");
  await byTestId(page, "streak-switch-toggle").click();
  await expectText(page, "streak-switch-state", "Streaks are on.");
  await byTestId(page, "vacation-settings").waitFor({ timeout: 30000 });
  assert.deepEqual((await dueItem()).streak, { state: "running", reminderFrom: dateOf(7), deadline: dateOf(10) });
  await openAndRead(page, `${WEB_URL}/?lang=en`, "/api/me/streaks");
  await byTestId(page, `today-streak-${tank.id}`).waitFor({ timeout: 30000 });
  await openAndRead(page, `${WEB_URL}/tanks/${tank.id}?lang=en`, `/api/tanks/${tank.id}/streaks`);
  await byTestId(page, "tank-streaks").waitFor({ timeout: 30000 });
  assert.ok((await tankStreaks(false)).timeLapse, "a visitor sees the streak again");

  await page.goto(`${WEB_URL}/me?lang=en`, { waitUntil: "networkidle" });
  await signOutThroughKeycloak(page);
  await context.close();

  assert.deepEqual(errors, []);
  console.log("TASK-5.19: with the switch off Today, the tank page and visitors show no streak and the due care names no reminder days, with the switch on the streak shows the true state again.");
} catch (error) {
  console.error(errors, await page?.locator("body").innerText().catch(() => ""));
  await page?.screenshot({ path: "/tmp/task-5.19-streak-switch-failure.png" }).catch(() => {});
  throw error;
} finally {
  await browser.close();
  await setStreaks(true);
}
