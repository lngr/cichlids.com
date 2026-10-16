// Story: TASK-5.2
import assert from "node:assert/strict";
import { chromium } from "playwright";
import { API_URL, WEB_URL, byTestId, passwordGrantToken, logInThroughKeycloak, signOutThroughKeycloak } from "./support.mjs";

const token = await passwordGrantToken();
const headers = { Authorization: `Bearer ${token}`, "Content-Type": "application/json" };
const create = await fetch(`${API_URL}/api/tanks`, { method: "POST", headers, body: JSON.stringify({ title: "Measurement graph path", isPublic: true, volume: { value: 55, unit: "us_gallon" } }) });
assert.equal(create.status, 201);
const tank = await create.json();
const day = 24 * 3600 * 1000;
const phValues = [7.0, 7.3, 7.6];
const nitrateValues = [10, 20, 30];
for (const [index, ph] of phValues.entries()) {
  const response = await fetch(`${API_URL}/api/tanks/${tank.id}/care`, {
    method: "POST", headers,
    body: JSON.stringify({ kind: "measurement", occurredAt: new Date(Date.now() - (3 - index) * day).toISOString(),
      values: [{ parameter: "ph", value: ph, origin: "test_kit" }, { parameter: "no3", value: nitrateValues[index], unit: "mg_per_liter", origin: "test_kit" }] }),
  });
  assert.equal(response.status, 201);
}
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

  // AC #1: pH and nitrate on three days show as a graph per parameter.
  const phGraph = byTestId(page, "care-graph-ph");
  const nitrateGraph = byTestId(page, "care-graph-no3");
  await phGraph.waitFor();
  await nitrateGraph.waitFor();
  await page.waitForFunction(() => document.querySelectorAll('[data-testid="care-graph-value-ph"]').length === 3
    && document.querySelectorAll('[data-testid="care-graph-value-no3"]').length === 3);
  assert.equal(await byTestId(page, "care-graph-temperature").count(), 0);
  const plot = await byTestId(page, "care-graph-plot-ph").boundingBox();
  assert.ok(plot.height >= 100 && plot.width >= 200);
  const phText = await phGraph.innerText();
  for (const value of ["7", "7.3", "7.6"]) assert.ok(phText.includes(value), `pH graph lists ${value}`);
  assert.match(await nitrateGraph.innerText(), /30 mg\/l/);
  assert.equal(await byTestId(page, "care-graph-plot-ph").getAttribute("aria-label"), "pH: latest value 7.6, number of values 3");
  assert.equal(await phGraph.locator('[role="list"]').count(), 1);
  assert.equal(await phGraph.locator('[role="listitem"]').count(), 3);
  await byTestId(page, "care-graphs").screenshot({ path: "/tmp/task-5.2-graphs-light.png" });

  // The form: common parameters first, more on demand, units of the member (US: Fahrenheit).
  const measure = byTestId(page, "care-measure");
  const bounds = await measure.boundingBox();
  assert.ok(bounds.height >= 48 && bounds.width >= 48);
  await measure.click();
  assert.equal(await byTestId(page, "measure-input-kh").count(), 0);
  await byTestId(page, "measure-input-ph").fill("7,8");
  await byTestId(page, "measure-input-no3").fill("40");
  await byTestId(page, "measure-input-temperature").fill("999");
  await page.getByText("This value is outside the plausible range.").waitFor();
  assert.equal(await byTestId(page, "measure-save").isDisabled(), true);
  assert.ok((await byTestId(page, "measure-input-temperature").getAttribute("aria-label")).endsWith("(°F)"));
  await byTestId(page, "measure-input-temperature").fill("77");
  await byTestId(page, "measure-more").click();
  await byTestId(page, "measure-input-kh").waitFor();
  assert.ok((await byTestId(page, "measure-input-kh").getAttribute("aria-label")).endsWith("(ppm)"));
  await byTestId(page, "measure-input-kh").fill("53.544");
  await byTestId(page, "measure-origin-meter").click();
  await page.screenshot({ path: "/tmp/task-5.2-form-light.png" });
  await byTestId(page, "measure-save").click();
  await page.waitForFunction(() => document.querySelectorAll('[data-testid="care-graph-value-ph"]').length === 4);
  await byTestId(page, "care-graph-temperature").waitFor();
  assert.match(await byTestId(page, "care-graph-temperature").innerText(), /77 °F/);
  assert.match(await byTestId(page, "care-graph-kh").innerText(), /53\.54 ppm/);
  const list = await (await fetch(`${API_URL}/api/tanks/${tank.id}/care`, { headers })).json();
  assert.equal(list.total, 4);
  const logged = list.items[0];
  assert.equal(logged.kind, "measurement");
  const byParameter = Object.fromEntries(logged.values.map(value => [value.parameter, value]));
  assert.equal(byParameter.temperature.unit, "fahrenheit");
  assert.equal(Number(byParameter.temperature.value), 77);
  assert.equal(Number(byParameter.temperature.normalizedValue), 25);
  assert.equal(byParameter.kh.unit, "ppm");
  assert.equal(Number(byParameter.kh.normalizedValue), 3);
  assert.equal(byParameter.ph.origin, "meter");
  await page.locator('[aria-modal="true"]').waitFor({ state: "hidden" });
  await byTestId(page, `care-entry-${logged.id}`).waitFor();
  assert.ok((await byTestId(page, `care-entry-${logged.id}`).innerText()).includes("Temperature: 77 °F (Meter)"));

  // The series endpoint serves the canonical unit, ordered by time.
  const series = await (await fetch(`${API_URL}/api/tanks/${tank.id}/care/series?parameter=ph`, { headers })).json();
  assert.deepEqual(series.points.map(point => Number(point.value)), [7, 7.3, 7.6, 7.8]);

  // Units follow the preference: the same temperature in Celsius.
  await page.goto(`${WEB_URL}/me`, { waitUntil: "networkidle" });
  await byTestId(page, "units-settings-toggle").click();
  await byTestId(page, "units-metric").click();
  await page.goto(`${WEB_URL}/tanks/${tank.id}?view=care`, { waitUntil: "networkidle" });
  await byTestId(page, "care-graph-temperature").waitFor();
  assert.match(await byTestId(page, "care-graph-temperature").innerText(), /25 °C/);
  assert.match(await byTestId(page, "care-graph-kh").innerText(), /3 °dKH/);

  // Correct and delete the measurement.
  await byTestId(page, `care-edit-${logged.id}`).click();
  assert.equal(await byTestId(page, "measure-input-temperature").inputValue(), "25");
  await byTestId(page, "measure-input-ph").fill("7.9");
  await byTestId(page, "measure-save").click();
  await page.locator('[aria-modal="true"]').waitFor({ state: "hidden" });
  await page.getByText("pH: 7.9 (Meter)").waitFor();
  await page.emulateMedia({ colorScheme: "dark" });
  await byTestId(page, "care-graph-ph").waitFor();
  await byTestId(page, "care-graphs").screenshot({ path: "/tmp/task-5.2-graphs-dark.png" });
  await byTestId(page, `care-edit-${logged.id}`).click();
  await byTestId(page, "measure-delete").click();
  await page.locator('[aria-modal="true"]').waitFor({ state: "hidden" });
  await page.waitForFunction(() => document.querySelectorAll('[data-testid="care-graph-value-ph"]').length === 3);
  assert.equal(await byTestId(page, "care-graph-temperature").count(), 0);

  // Coded refusals: implausible values and foreign tanks.
  const bad = await fetch(`${API_URL}/api/tanks/${tank.id}/care`, { method: "POST", headers, body: JSON.stringify({ kind: "measurement", values: [{ parameter: "ph", value: 15 }] }) });
  assert.equal(bad.status, 400);
  assert.equal((await bad.json()).code, "care_value_invalid");
  const otherToken = await passwordGrantToken("dev-mod");
  const refused = await fetch(`${API_URL}/api/tanks/${tank.id}/care/series?parameter=ph`, { headers: { Authorization: `Bearer ${otherToken}` } });
  assert.equal(refused.status, 200, "the series of a public stream is readable by other members");
  await page.goto(`${WEB_URL}/me`, { waitUntil: "networkidle" });
  await signOutThroughKeycloak(page);
  assert.deepEqual(errors, []);
  console.log("TASK-5.2: pH and nitrate on three days chart per parameter in the member's units, progressive form, correction and deletion passed (light/dark).");
} catch (error) {
  console.error(errors, await page?.locator("body").innerText());
  await page?.screenshot({ path: "/tmp/task-5.2-measurement-failure.png" });
  throw error;
} finally {
  await browser.close();
}
