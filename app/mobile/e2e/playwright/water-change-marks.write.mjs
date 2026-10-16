// Story: TASK-5.14
import assert from "node:assert/strict";
import { chromium } from "playwright";
import { API_URL, WEB_URL, byTestId, passwordGrantToken, logInThroughKeycloak, signOutThroughKeycloak } from "./support.mjs";

const token = await passwordGrantToken();
const headers = { Authorization: `Bearer ${token}`, "Content-Type": "application/json" };
const create = await fetch(`${API_URL}/api/tanks`, { method: "POST", headers, body: JSON.stringify({ title: "Water change marks path", volume: { value: 120, unit: "liter" } }) });
assert.equal(create.status, 201);
const tank = await create.json();
const day = 24 * 3600 * 1000;
const ago = days => new Date(Date.now() - days * day).toISOString();
const post = async body => {
  const response = await fetch(`${API_URL}/api/tanks/${tank.id}/care`, { method: "POST", headers, body: JSON.stringify(body) });
  assert.equal(response.status, 201);
  return response.json();
};
// Nitrate rises between the water changes and drops after each one.
for (const [days, value] of [[6, 20], [4, 40], [3, 15], [1, 38], [0.2, 12]]) {
  await post({ kind: "measurement", occurredAt: ago(days), values: [{ parameter: "no3", value, unit: "mg_per_liter" }] });
}
const first = await post({ kind: "water_change", occurredAt: ago(3.5), amount: { value: 30, unit: "percent" } });
const second = await post({ kind: "water_change", occurredAt: ago(0.5), amount: { value: 25, unit: "percent" } });

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

  // AC #1: each water change is a marker on the nitrate chart and the values around it are visible.
  const graph = byTestId(page, "care-graph-no3");
  await graph.waitFor();
  await page.waitForFunction(() => document.querySelectorAll('[data-testid="care-graph-no3"] [data-testid^="care-mark-"]').length === 2);
  const markers = graph.locator('[data-testid^="care-mark-"]');
  assert.equal(await markers.count(), 2);
  for (let index = 0; index < 2; index++) {
    const bounds = await markers.nth(index).boundingBox();
    assert.ok(bounds.width >= 48 && bounds.height >= 48, "marker touch target is at least 48 by 48");
    assert.match(await markers.nth(index).getAttribute("aria-label"), /^Water change on /);
  }
  const firstBounds = await byTestId(graph, `care-mark-${first.id}`).boundingBox();
  const secondBounds = await byTestId(graph, `care-mark-${second.id}`).boundingBox();
  assert.ok(firstBounds.x < secondBounds.x, "the older water change is further left on the shared time axis");
  const plot = await byTestId(graph, "care-graph-plot-no3").boundingBox();
  assert.ok(firstBounds.x + firstBounds.width / 2 > plot.x && secondBounds.x + secondBounds.width / 2 < plot.x + plot.width + 1);

  const pairs = byTestId(graph, "care-graph-pair-no3");
  assert.equal(await pairs.count(), 2);
  assert.match(await pairs.nth(0).innerText(), /^Nitrate \(NO3\) 40 mg\/l to 15 mg\/l after the water change on /);
  assert.match(await pairs.nth(1).innerText(), /^Nitrate \(NO3\) 38 mg\/l to 12 mg\/l after the water change on /);
  assert.equal(await byTestId(graph, "care-graph-highlight-no3").count(), 4);
  await byTestId(page, "care-graphs").screenshot({ path: "/tmp/task-5.14-marks-light.png" });

  // Tapping a marker opens the water change entry.
  await byTestId(graph, `care-mark-${first.id}`).click();
  const detail = byTestId(page, "care-mark-detail");
  await detail.waitFor();
  assert.match(await detail.innerText(), /30 %/);
  await byTestId(page, "care-mark-detail-close").click();
  await detail.waitFor({ state: "hidden" });

  // The dark theme draws the same markers.
  await page.emulateMedia({ colorScheme: "dark" });
  await graph.waitFor();
  assert.equal(await markers.count(), 2);
  await page.waitForTimeout(500);
  await byTestId(page, "care-graphs").screenshot({ path: "/tmp/task-5.14-marks-dark.png" });

  // A parameter measured once between the water changes shows both markers and the nearest values.
  await post({ kind: "measurement", occurredAt: ago(2), values: [{ parameter: "ph", value: 7.2 }] });
  await page.goto(`${WEB_URL}/tanks/${tank.id}?view=care`, { waitUntil: "networkidle" });
  await byTestId(page, "care-graph-ph").waitFor();
  await page.waitForFunction(() => document.querySelectorAll('[data-testid="care-graph-ph"] [data-testid^="care-mark-"]').length === 2);
  const phPairs = byTestId(page, "care-graph-pair-ph");
  assert.equal(await phPairs.count(), 2);
  assert.match(await phPairs.nth(0).innerText(), /^pH 7\.2 after the water change on /);
  assert.match(await phPairs.nth(1).innerText(), /^pH 7\.2 before the water change on /);

  await page.goto(`${WEB_URL}/me`, { waitUntil: "networkidle" });
  await signOutThroughKeycloak(page);
  assert.deepEqual(errors, []);
  console.log("TASK-5.14: water changes appear as markers on the nitrate chart with the values before and after each one (light/dark).");
} catch (error) {
  console.error(errors, await page?.locator("body").innerText());
  await page?.screenshot({ path: "/tmp/task-5.14-marks-failure.png" });
  throw error;
} finally {
  await browser.close();
}
