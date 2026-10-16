// Story: TASK-5.3
import assert from "node:assert/strict";
import { chromium } from "playwright";
import { API_URL, WEB_URL, byTestId, passwordGrantToken, logInThroughKeycloak, signOutThroughKeycloak } from "./support.mjs";

const token = await passwordGrantToken();
const headers = { Authorization: `Bearer ${token}`, "Content-Type": "application/json" };
const create = await fetch(`${API_URL}/api/tanks`, { method: "POST", headers, body: JSON.stringify({ title: "Care plan path", isPublic: true, volume: { value: 240, unit: "liter" } }) });
assert.equal(create.status, 201);
const tank = await create.json();
const day = 24 * 3600 * 1000;
const logged = await fetch(`${API_URL}/api/tanks/${tank.id}/care`, {
  method: "POST", headers, body: JSON.stringify({ kind: "water_change", occurredAt: new Date(Date.now() - 10 * day).toISOString() }),
});
assert.equal(logged.status, 201);

const browser = await chromium.launch();
const errors = [];
let page;
try {
  page = await browser.newPage({ locale: "en-US", colorScheme: "light", viewport: { width: 390, height: 844 } });
  page.on("pageerror", error => errors.push(String(error)));
  await page.goto(`${WEB_URL}/me?lang=en`, { waitUntil: "networkidle" });
  await logInThroughKeycloak(page);
  await page.goto(`${WEB_URL}/tanks/${tank.id}`, { waitUntil: "networkidle" });
  await byTestId(page, "tank-view-care").click();

  // AC #1: a water change older than the rhythm shows as due, with the reason of the plan.
  const due = byTestId(page, "care-plan-due-water_change");
  await due.waitFor();
  assert.equal(await due.innerText(), "Planned 3 days ago");
  assert.equal(await byTestId(page, "care-plan-reason-water_change").innerText(), "Every 7 days: The usual recommendation for cichlid tanks");
  assert.equal(await byTestId(page, "care-plan-note-water_change").innerText(), "No rush, do it when it suits you.");
  assert.equal(await byTestId(page, "care-plan-state-measurement").innerText(), "No rule yet");
  const overdueColor = await due.evaluate(element => getComputedStyle(element).color);
  assert.notEqual(overdueColor, "rgb(204, 40, 35)", "overdue is not shown in the alarm color");
  const plan = await (await fetch(`${API_URL}/api/tanks/${tank.id}/care/plan?date=${localDate(new Date())}`, { headers })).json();
  const water = plan.rules.find(rule => rule.kind === "water_change");
  assert.equal(water.due.state, "overdue");
  assert.equal(water.reason.code, "cichlid_default");
  await byTestId(page, "care-plan").screenshot({ path: "/tmp/task-5.3-plan-light.png" });

  // The member changes the rhythm with a choice; the reason becomes the member's own.
  const edit = byTestId(page, "care-plan-edit-water_change");
  const bounds = await edit.boundingBox();
  assert.ok(bounds.height >= 48 && bounds.width >= 48);
  await edit.click();
  const choice = byTestId(page, "care-plan-choice-water_change-14");
  const choiceBounds = await choice.boundingBox();
  assert.ok(choiceBounds.height >= 48 && choiceBounds.width >= 48);
  await page.emulateMedia({ colorScheme: "dark" });
  await byTestId(page, "care-plan").screenshot({ path: "/tmp/task-5.3-plan-dark.png" });
  await page.emulateMedia({ colorScheme: "light" });
  await choice.click();
  await page.waitForFunction(() => document.querySelector('[data-testid="care-plan-due-water_change"]')?.textContent === "Next in 4 days");
  assert.equal(await byTestId(page, "care-plan-reason-water_change").innerText(), "Every 14 days: Your choice");
  assert.equal(await byTestId(page, "care-plan-note-water_change").count(), 0);
  assert.equal(await byTestId(page, "care-plan-editor-water_change").count(), 0);

  // A custom rhythm is validated, a measurement rule is added, and a rule can be switched off and on.
  await edit.click();
  await byTestId(page, "care-plan-custom-water_change").fill("120");
  assert.equal(await byTestId(page, "care-plan-custom-save-water_change").isDisabled(), true);
  await page.getByText("Enter a whole number from 1 to 90.").waitFor();
  await byTestId(page, "care-plan-custom-water_change").fill("30");
  await byTestId(page, "care-plan-custom-save-water_change").click();
  await page.waitForFunction(() => document.querySelector('[data-testid="care-plan-due-water_change"]')?.textContent === "Next in 20 days");
  await byTestId(page, "care-plan-edit-measurement").click();
  await byTestId(page, "care-plan-choice-measurement-5").click();
  await byTestId(page, "care-plan-due-measurement").waitFor();
  assert.equal(await byTestId(page, "care-plan-due-measurement").innerText(), "Not logged yet");
  assert.equal(await byTestId(page, "care-plan-reason-measurement").innerText(), "Every 5 days: Your choice");
  await byTestId(page, "care-plan-toggle-water_change").click();
  await byTestId(page, "care-plan-state-water_change").waitFor();
  assert.equal(await byTestId(page, "care-plan-state-water_change").innerText(), "Switched off");
  assert.equal(await byTestId(page, "care-plan-due-water_change").count(), 0);
  await byTestId(page, "care-plan-toggle-water_change").click();
  await byTestId(page, "care-plan-due-water_change").waitFor();

  // Logging a water change moves the due date a full rhythm ahead.
  await byTestId(page, "care-water-change").click();
  await page.waitForFunction(() => document.querySelector('[data-testid="care-plan-due-water_change"]')?.textContent === "Next in 30 days");

  // The member-wide list puts due care first and refuses nothing it does not own.
  const daily = await fetch(`${API_URL}/api/tanks/${tank.id}/care/plan/water_change?date=${localDate(new Date())}`, { method: "PATCH", headers, body: JSON.stringify({ rhythmDays: 1 }) });
  assert.equal(daily.status, 200);
  const dueList = await (await fetch(`${API_URL}/api/me/care/due?date=${localDate(new Date(Date.now() + 3 * day))}`, { headers })).json();
  const mine = dueList.items.filter(item => String(item.tankId) === String(tank.id));
  assert.deepEqual(mine.map(item => [item.kind, item.due.state]), [["water_change", "overdue"], ["measurement", "never_logged"]]);
  const states = dueList.items.map(item => item.due.state);
  assert.deepEqual(states, [...states].sort((a, b) => order(a) - order(b)));

  // Coded refusals for a foreign tank.
  const otherToken = await passwordGrantToken("dev-mod");
  const refused = await fetch(`${API_URL}/api/tanks/${tank.id}/care/plan?date=${localDate(new Date())}`, { headers: { Authorization: `Bearer ${otherToken}` } });
  assert.equal(refused.status, 404);
  assert.equal((await refused.json()).code, "care_not_found");
  const refusedChange = await fetch(`${API_URL}/api/tanks/${tank.id}/care/plan/water_change?date=${localDate(new Date())}`, {
    method: "PATCH", headers: { Authorization: `Bearer ${otherToken}`, "Content-Type": "application/json" }, body: JSON.stringify({ rhythmDays: 3 }),
  });
  assert.equal(refusedChange.status, 404);
  assert.equal((await refusedChange.json()).code, "care_not_found");
  await page.goto(`${WEB_URL}/me`, { waitUntil: "networkidle" });
  await signOutThroughKeycloak(page);
  assert.deepEqual(errors, []);
  console.log("TASK-5.3: an overdue water change shows as due with its reason, rhythm choice and custom rhythm, switching off and on, and the member-wide due list passed (light/dark).");
} catch (error) {
  console.error(errors, await page?.locator("body").innerText());
  await page?.screenshot({ path: "/tmp/task-5.3-care-plan-failure.png" });
  throw error;
} finally {
  await browser.close();
}

function order(state) {
  return { overdue: 0, due_today: 1, due_in: 2, never_logged: 3 }[state];
}

function localDate(date) {
  const pad = value => String(value).padStart(2, "0");
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`;
}
