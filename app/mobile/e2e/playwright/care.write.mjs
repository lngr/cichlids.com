// Story: TASK-5.1
import assert from "node:assert/strict";
import { chromium } from "playwright";
import { API_URL, WEB_URL, byTestId, passwordGrantToken, logInThroughKeycloak, signOutThroughKeycloak } from "./support.mjs";

const token = await passwordGrantToken();
const response = await fetch(`${API_URL}/api/tanks`, {
  method: "POST", headers: { Authorization: `Bearer ${token}`, "Content-Type": "application/json" },
  body: JSON.stringify({ title: "Water change care path", isPublic: true, volume: { value: 55, unit: "us_gallon" } }),
});
assert.equal(response.status, 201);
const tank = await response.json();
const browser = await chromium.launch();
const errors = [];
let page;
try {
  page = await browser.newPage({ locale: "en-US", colorScheme: "light", viewport: { width: 390, height: 844 } });
  page.on("pageerror", error => errors.push(String(error)));
  await page.goto(`${WEB_URL}/me?lang=en`, { waitUntil: "networkidle" });
  await logInThroughKeycloak(page);
  await byTestId(page, "units-settings-toggle").click();
  await byTestId(page, "units-us").click();
  await page.goto(`${WEB_URL}/tanks/${tank.id}`, { waitUntil: "networkidle" });
  await byTestId(page, "tank-view-care").click();
  const tankCare = byTestId(page, "tank-care-stream");
  const button = byTestId(tankCare, "care-water-change");
  await button.waitFor();
  const bounds = await button.boundingBox();
  assert.ok(bounds.height >= 48 && bounds.width >= 48);
  const started = Date.now();
  await button.click();
  await byTestId(page, "care-confirmation").waitFor();
  let list = await (await fetch(`${API_URL}/api/tanks/${tank.id}/care`, { headers: { Authorization: `Bearer ${token}` } })).json();
  assert.equal(list.total, 1);
  const entry = list.items[0];
  assert.ok(Math.abs(Date.parse(entry.occurredAt) - started) < 10000);
  assert.equal(entry.amount, null);
  await byTestId(page, `care-entry-${entry.id}`).waitFor();
  await byTestId(page, `care-edit-${entry.id}`).click();
  await byTestId(page, "care-amount-input").fill("10.5");
  await byTestId(page, "care-amount-save").click();
  await byTestId(page, "care-entry-amount").filter({ hasText: "10.5 US gal" }).waitFor();
  await page.locator('[aria-modal="true"]').waitFor({ state: "hidden" });
  await button.click({ trial: true });
  await page.screenshot({ path: "/tmp/task-5.1-care-light.png" });
  list = await (await fetch(`${API_URL}/api/tanks/${tank.id}/care`, { headers: { Authorization: `Bearer ${token}` } })).json();
  assert.equal(list.items[0].amount.unit, "us_gallon");
  assert.equal(list.items[0].amount.liters, 39.746823732);
  await byTestId(page, "tank-view-story").click();
  await byTestId(page, "tank-story-event").filter({ hasText: "Water change" }).filter({ hasText: "10.5 US gal" }).waitFor();
  await page.emulateMedia({ colorScheme: "dark" });
  await byTestId(page, "tank-view-care").click();
  await button.click();
  const undoUrl = `${API_URL}/api/tanks/${tank.id}/care/*`;
  let refuseUndo = true;
  await page.route(undoUrl, route => {
    if (route.request().method() === "DELETE" && refuseUndo) {
      refuseUndo = false;
      return route.abort("failed");
    }
    return route.continue();
  });
  await byTestId(page, "care-undo").click();
  await page.getByRole("alert").filter({ hasText: "The connection failed." }).waitFor();
  assert.equal(await byTestId(page, "care-amount-input").count(), 0);
  await byTestId(page, "care-undo").click();
  await byTestId(page, "care-confirmation").waitFor({ state: "hidden" });
  await page.unroute(undoUrl);
  await page.waitForFunction(() => [...document.querySelectorAll('[data-testid^="care-entry-"]')].filter(node => /^care-entry-\d+$/.test(node.dataset.testid)).length === 1);
  await page.locator('[aria-modal="true"]').waitFor({ state: "hidden" });
  await button.click({ trial: true });
  await page.screenshot({ path: "/tmp/task-5.1-care-dark.png" });
  await byTestId(page, "tab-create").click();
  await byTestId(page, "create-water-change").click();
  await byTestId(page, `care-choose-${tank.id}`).click();
  const quickLog = byTestId(page, "care-quick-log");
  await byTestId(quickLog, "care-water-change").click();
  await byTestId(quickLog, "care-confirmation").waitFor();
  await byTestId(page, "care-open-tank").click();
  await page.waitForURL(`${WEB_URL}/tanks/${tank.id}?view=care`);
  await button.waitFor();
  const loggedEntries = tankCare.locator('[data-testid^="care-entry-"]').filter({ has: page.locator('[data-testid^="care-edit-"]') });
  await loggedEntries.nth(1).waitFor();
  assert.equal(await loggedEntries.count(), 2);
  const otherToken = await passwordGrantToken("dev-mod");
  const refused = await fetch(`${API_URL}/api/tanks/${tank.id}/care`, { headers: { Authorization: `Bearer ${otherToken}` } });
  assert.equal(refused.status, 404);
  assert.equal((await refused.json()).code, "care_not_found");
  await page.goto(`${WEB_URL}/me`, { waitUntil: "networkidle" });
  await signOutThroughKeycloak(page);
  assert.deepEqual(errors, []);
  console.log("TASK-5.1: tap logs care, amount added in US units, timeline, undo retry and + tank selection passed (light/dark).");
} catch (error) {
  console.error(errors, await page?.locator("body").innerText());
  await page?.screenshot({ path: "/tmp/task-5.1-care-failure.png" });
  throw error;
} finally {
  await browser.close();
}
