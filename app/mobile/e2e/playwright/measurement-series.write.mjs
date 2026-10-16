// Story: TASK-5.23
import assert from "node:assert/strict";
import { chromium } from "playwright";
import { API_URL, WEB_URL, byTestId, passwordGrantToken, logInThroughKeycloak, signOutThroughKeycloak } from "./support.mjs";

const token = await passwordGrantToken();
const headers = { Authorization: `Bearer ${token}`, "Content-Type": "application/json" };
const create = await fetch(`${API_URL}/api/tanks`, { method: "POST", headers, body: JSON.stringify({ title: "Series request path", isPublic: true, volume: { value: 120, unit: "liter" } }) });
assert.equal(create.status, 201);
const tank = await create.json();
const day = 24 * 3600 * 1000;
const post = async (days, values, onlyMe = false) => {
  const response = await fetch(`${API_URL}/api/tanks/${tank.id}/care`, {
    method: "POST", headers, body: JSON.stringify({ kind: "measurement", occurredAt: new Date(Date.now() - days * day).toISOString(), values, onlyMe }),
  });
  assert.equal(response.status, 201);
};
await post(3, [{ parameter: "ph", value: 7.0 }, { parameter: "no3", value: 10, unit: "mg_per_liter" }, { parameter: "kh", value: 4, unit: "dkh" }]);
await post(2, [{ parameter: "ph", value: 7.3 }, { parameter: "no3", value: 20, unit: "mg_per_liter" }]);
await post(1, [{ parameter: "ph", value: 8.5 }], true);

// Another member sees the public measurements only.
const otherToken = await passwordGrantToken("dev-mod");
const seen = await fetch(`${API_URL}/api/tanks/${tank.id}/care/series/all`, { headers: { Authorization: `Bearer ${otherToken}` } });
assert.equal(seen.status, 200);
const visible = (await seen.json()).series;
assert.deepEqual(visible.map(item => item.parameter).sort(), ["kh", "no3", "ph"]);
assert.deepEqual(visible.find(item => item.parameter === "ph").points.map(point => Number(point.value)), [7, 7.3]);
const own = (await (await fetch(`${API_URL}/api/tanks/${tank.id}/care/series/all`, { headers })).json()).series;
assert.deepEqual(own.find(item => item.parameter === "ph").points.map(point => Number(point.value)), [7, 7.3, 8.5]);

const browser = await chromium.launch();
const errors = [];
const seriesRequests = [];
let page;
try {
  page = await browser.newPage({ locale: "en-US", colorScheme: "light", viewport: { width: 390, height: 844 } });
  page.on("pageerror", error => errors.push(String(error)));
  page.on("request", request => { if (request.url().includes("/care/series")) seriesRequests.push(request.url()); });
  await page.goto(`${WEB_URL}/me?lang=en`, { waitUntil: "networkidle" });
  await logInThroughKeycloak(page);
  await page.goto(`${WEB_URL}/tanks/${tank.id}?view=care`, { waitUntil: "networkidle" });
  for (const parameter of ["ph", "no3", "kh"]) await byTestId(page, `care-graph-${parameter}`).waitFor();
  assert.equal(await byTestId(page, "care-graph-value-ph").count(), 3);
  assert.equal(await byTestId(page, "care-graph-value-no3").count(), 2);
  assert.equal(seriesRequests.length, 1, `one series request expected, got ${seriesRequests.join(", ")}`);
  assert.match(seriesRequests[0], /\/api\/tanks\/\d+\/care\/series\/all$/);

  await page.goto(`${WEB_URL}/me`, { waitUntil: "networkidle" });
  await signOutThroughKeycloak(page);
  assert.deepEqual(errors, []);
  console.log("TASK-5.23: the charts of three parameters load with one series request; only-me measurements stay with the owner.");
} catch (error) {
  console.error(errors, seriesRequests, await page?.locator("body").innerText());
  await page?.screenshot({ path: "/tmp/task-5.23-series-failure.png" });
  throw error;
} finally {
  await browser.close();
}
