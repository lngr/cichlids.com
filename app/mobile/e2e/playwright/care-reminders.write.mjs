// Story: TASK-5.8
import assert from "node:assert/strict";
import { chromium } from "playwright";
import { API_URL, WEB_URL, byTestId, passwordGrantToken, logInThroughKeycloak, signOutThroughKeycloak } from "./support.mjs";

const token = await passwordGrantToken();
const headers = { Authorization: `Bearer ${token}`, "Content-Type": "application/json" };
const create = await fetch(`${API_URL}/api/tanks`, { method: "POST", headers, body: JSON.stringify({ title: "Reminder path", volume: { value: 240, unit: "liter" } }) });
assert.equal(create.status, 201);
const tank = await create.json();
const day = 24 * 3600 * 1000;
const logged = await fetch(`${API_URL}/api/tanks/${tank.id}/care`, {
  method: "POST", headers, body: JSON.stringify({ kind: "water_change", occurredAt: new Date(Date.now() - 5 * day).toISOString() }),
});
assert.equal(logged.status, 201);

const planOf = async () => (await fetch(`${API_URL}/api/tanks/${tank.id}/care/plan?date=${localDate(new Date())}`, { headers })).json();
const dueItem = async () => (await (await fetch(`${API_URL}/api/me/care/due?date=${localDate(new Date())}`, { headers })).json())
  .items.find(item => String(item.tankId) === String(tank.id) && item.kind === "water_change");

const browser = await chromium.launch();
const errors = [];
let page;
try {
  page = await browser.newPage({ locale: "en-US", colorScheme: "light", viewport: { width: 390, height: 844 } });
  page.on("pageerror", error => errors.push(String(error)));
  await page.goto(`${WEB_URL}/me?lang=en`, { waitUntil: "networkidle" });
  await logInThroughKeycloak(page);
  await openCareView(page);

  // Reminders are on by default for every tank; the browser has no permission flow to show.
  const state = byTestId(page, "care-reminders-state");
  await state.waitFor();
  assert.equal(await state.innerText(), "Reminders are on for this tank. The app on your phone reminds you on the day care is due.");
  assert.equal(await byTestId(page, "care-reminders-toggle").innerText(), "Switch reminders off");
  assert.equal(await byTestId(page, "care-reminders-explain").count(), 0);
  assert.equal(await byTestId(page, "care-reminders-denied").count(), 0);
  assert.equal((await planOf()).remindersEnabled, true);
  assert.equal((await dueItem()).remindersEnabled, true);
  const bounds = await byTestId(page, "care-reminders-toggle").boundingBox();
  assert.ok(bounds.height >= 48 && bounds.width >= 48);
  await byTestId(page, "care-reminders").screenshot({ path: "/tmp/task-5.8-reminders-light.png" });
  await page.emulateMedia({ colorScheme: "dark" });
  await byTestId(page, "care-reminders").screenshot({ path: "/tmp/task-5.8-reminders-dark.png" });
  await page.emulateMedia({ colorScheme: "light" });

  // Switching off is stored on the server and survives a reload.
  await byTestId(page, "care-reminders-toggle").click();
  await page.waitForFunction(() => document.querySelector('[data-testid="care-reminders-state"]')?.textContent === "Reminders are off for this tank.");
  assert.equal(await byTestId(page, "care-reminders-toggle").innerText(), "Switch reminders on");
  assert.equal((await planOf()).remindersEnabled, false);
  assert.equal((await dueItem()).remindersEnabled, false);
  await page.reload({ waitUntil: "networkidle" });
  await openCareView(page);
  await page.waitForFunction(() => document.querySelector('[data-testid="care-reminders-state"]')?.textContent === "Reminders are off for this tank.");
  assert.equal(await byTestId(page, "care-reminders-toggle").innerText(), "Switch reminders on");

  // Switching on again is stored as well, and the other care rules keep their state.
  await byTestId(page, "care-reminders-toggle").click();
  await page.waitForFunction(() => document.querySelector('[data-testid="care-reminders-toggle"]')?.textContent === "Switch reminders off");
  assert.equal((await planOf()).remindersEnabled, true);
  assert.equal(await byTestId(page, "care-plan-due-water_change").innerText(), "Next in 2 days");

  // A stranger neither reads nor changes the switch; anonymous requests are refused with codes.
  const otherToken = await passwordGrantToken("dev-mod");
  const url = `${API_URL}/api/tanks/${tank.id}/care/plan?date=${localDate(new Date())}`;
  const refused = await fetch(url, { method: "PATCH", headers: { Authorization: `Bearer ${otherToken}`, "Content-Type": "application/json" }, body: JSON.stringify({ remindersEnabled: false }) });
  assert.equal(refused.status, 404);
  assert.equal((await refused.json()).code, "care_not_found");
  const anonymous = await fetch(url, { method: "PATCH", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ remindersEnabled: false }) });
  assert.equal(anonymous.status, 401);
  assert.equal((await planOf()).remindersEnabled, true);

  await page.goto(`${WEB_URL}/me`, { waitUntil: "networkidle" });
  await signOutThroughKeycloak(page);
  assert.deepEqual(errors, []);
  console.log("TASK-5.8: the reminder switch of a tank is on by default, persists on the server across a reload, and the browser shows no permission flow (light/dark).");
} catch (error) {
  console.error(errors, await page?.locator("body").innerText());
  await page?.screenshot({ path: "/tmp/task-5.8-care-reminders-failure.png" });
  throw error;
} finally {
  await browser.close();
}

async function openCareView(page) {
  await page.goto(`${WEB_URL}/tanks/${tank.id}`, { waitUntil: "networkidle" });
  await byTestId(page, "tank-view-care").click();
}

function localDate(date) {
  const pad = value => String(value).padStart(2, "0");
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`;
}
