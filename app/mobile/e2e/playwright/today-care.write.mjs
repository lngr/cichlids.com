// Story: TASK-5.4
import assert from "node:assert/strict";
import { chromium } from "playwright";
import { API_URL, WEB_URL, byTestId, passwordGrantToken, logInThroughKeycloak, signOutThroughKeycloak, ZONE, localDate } from "./support.mjs";

const token = await passwordGrantToken();
const headers = { Authorization: `Bearer ${token}`, "Content-Type": "application/json" };
const day = 24 * 3600 * 1000;
async function createTank(title) {
  const response = await fetch(`${API_URL}/api/tanks`, { method: "POST", headers, body: JSON.stringify({ title, isPublic: true }) });
  assert.equal(response.status, 201);
  return response.json();
}
async function logCare(tank, daysAgo, body = { kind: "water_change" }) {
  const response = await fetch(`${API_URL}/api/tanks/${tank.id}/care`, {
    method: "POST", headers, body: JSON.stringify({ ...body, occurredAt: new Date(Date.now() - daysAgo * day).toISOString() }),
  });
  assert.equal(response.status, 201);
}

// Overdue by 3 days, due in 1 day, due in 6 days (not shown), and an overdue measurement rule.
const overdue = await createTank("Today care overdue");
await logCare(overdue, 10);
const soon = await createTank("Today care soon");
await logCare(soon, 6);
const later = await createTank("Today care later");
await logCare(later, 1);
const measured = await createTank("Today care measure");
await logCare(measured, 4, { kind: "measurement", values: [{ parameter: "ph", value: 7.0 }] });
const rule = await fetch(`${API_URL}/api/tanks/${measured.id}/care/plan/measurement?date=${localDate(new Date())}`, {
  method: "PATCH", headers, body: JSON.stringify({ rhythmDays: 3 }),
});
assert.equal(rule.status, 200);

const item = (tank, kind) => `today-due-${tank.id}-${kind}`;
const browser = await chromium.launch();
const errors = [];
let page;
try {
  page = await browser.newPage({ locale: "en-US", timezoneId: ZONE, colorScheme: "light", viewport: { width: 390, height: 844 } });
  page.on("pageerror", error => errors.push(String(error)));
  await page.goto(`${WEB_URL}/me?lang=en`, { waitUntil: "networkidle" });
  await logInThroughKeycloak(page);
  await page.goto(WEB_URL, { waitUntil: "networkidle" });

  // AC #1: the due care appears above the tanks, ordered by urgency, without care that is due later.
  await byTestId(page, "today-due-care").waitFor({ timeout: 15000 });
  await byTestId(page, item(overdue, "water_change")).waitFor();
  await byTestId(page, item(soon, "water_change")).waitFor();
  await byTestId(page, item(measured, "measurement")).waitFor();
  assert.equal(await byTestId(page, item(later, "water_change")).count(), 0);
  const sections = await page.locator('[data-testid="today-member"] > div').evaluateAll(nodes => nodes.map(node => node.dataset.testid).filter(Boolean));
  assert.ok(sections.indexOf("today-due-care") >= 0 && sections.indexOf("today-due-care") < sections.indexOf("today-tanks"));
  const order = await page.locator('[data-testid^="today-due-"][data-testid$="water_change"], [data-testid^="today-due-"][data-testid$="measurement"]')
    .evaluateAll(nodes => nodes.map(node => node.dataset.testid));
  assert.ok(order.indexOf(item(overdue, "water_change")) < order.indexOf(item(soon, "water_change")));
  const overdueCard = byTestId(page, item(overdue, "water_change"));
  assert.match(await overdueCard.innerText(), /Today care overdue/);
  assert.match(await overdueCard.innerText(), /Planned 3 days ago/);
  assert.match(await byTestId(page, item(soon, "water_change")).innerText(), /Next in 1 day/);
  const logButton = byTestId(page, `today-due-log-${overdue.id}`);
  assert.equal(await logButton.getAttribute("aria-label"), "Log water change for Today care overdue");
  const bounds = await logButton.boundingBox();
  assert.ok(bounds.height >= 48 && bounds.width >= 48);
  const color = await overdueCard.locator("*").evaluateAll(nodes => nodes.map(node => getComputedStyle(node).color));
  assert.ok(!color.includes("rgb(204, 40, 35)"), "due care is not shown in the alarm color");
  await byTestId(page, "today-due-care").screenshot({ path: "/tmp/task-5.4-today-due-light.png" });
  await page.emulateMedia({ colorScheme: "dark" });
  await byTestId(page, "today-due-care").screenshot({ path: "/tmp/task-5.4-today-due-dark.png" });
  await page.emulateMedia({ colorScheme: "light" });

  // One tap logs the water change; the item leaves the list and the calm confirmation offers an undo.
  await logButton.click();
  await byTestId(page, "today-due-confirmation").waitFor();
  await byTestId(page, item(overdue, "water_change")).waitFor({ state: "detached" });
  let list = await (await fetch(`${API_URL}/api/tanks/${overdue.id}/care`, { headers })).json();
  assert.equal(list.total, 2);
  await byTestId(page, "today-due-undo").click();
  await byTestId(page, item(overdue, "water_change")).waitFor();
  list = await (await fetch(`${API_URL}/api/tanks/${overdue.id}/care`, { headers })).json();
  assert.equal(list.total, 1);
  await byTestId(page, `today-due-log-${overdue.id}`).click();
  await byTestId(page, item(overdue, "water_change")).waitFor({ state: "detached" });
  list = await (await fetch(`${API_URL}/api/tanks/${overdue.id}/care`, { headers })).json();
  assert.equal(list.total, 2);

  // A measurement opens the measurement form of that tank; saving it removes the item.
  await byTestId(page, `today-due-measure-${measured.id}`).click();
  await byTestId(page, "measure-input-ph").fill("7.4");
  await byTestId(page, "measure-save").click();
  await byTestId(page, item(measured, "measurement")).waitFor({ state: "detached" });
  list = await (await fetch(`${API_URL}/api/tanks/${measured.id}/care`, { headers })).json();
  assert.equal(list.items.filter(entry => entry.kind === "measurement").length, 2);

  // The section is gone when nothing of the member is due.
  await page.route("**/api/me/care/due?*", route => route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify({ items: [] }) }));
  await byTestId(page, "tab-me").click();
  await byTestId(page, "tab-today").click();
  await byTestId(page, "today-tanks").waitFor();
  await byTestId(page, "today-due-care").waitFor({ state: "detached" });
  await page.unroute("**/api/me/care/due?*");
  await byTestId(page, "tab-me").click();
  await signOutThroughKeycloak(page);
  assert.deepEqual(errors, []);
  console.log("TASK-5.4: due care above the tanks in urgency order, one-tap water change with undo, measurement form and the empty case passed (light/dark).");
} catch (error) {
  console.error(errors, await page?.locator("body").innerText());
  await page?.screenshot({ path: "/tmp/task-5.4-today-care-failure.png" });
  throw error;
} finally {
  await browser.close();
}
