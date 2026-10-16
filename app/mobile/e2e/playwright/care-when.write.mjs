// Story: TASK-5.16
import assert from "node:assert/strict";
import { chromium } from "playwright";
import { API_URL, WEB_URL, byTestId, passwordGrantToken, logInThroughKeycloak, signOutThroughKeycloak } from "./support.mjs";

const ZONE = "Europe/Berlin";
const DAY = 24 * 3600 * 1000;
const token = await passwordGrantToken();
const headers = { Authorization: `Bearer ${token}`, "Content-Type": "application/json" };
const create = await fetch(`${API_URL}/api/tanks`, { method: "POST", headers, body: JSON.stringify({ title: "Recorded afterwards", isPublic: true, volume: { value: 55, unit: "us_gallon" } }) });
assert.equal(create.status, 201);
const tank = await create.json();
const dateOf = instant => new Intl.DateTimeFormat("en-US", { dateStyle: "medium", timeZone: ZONE }).format(instant);
const localParts = instant => Object.fromEntries(new Intl.DateTimeFormat("en-CA", { timeZone: ZONE, hourCycle: "h23", year: "numeric", month: "2-digit", day: "2-digit", hour: "2-digit", minute: "2-digit" })
  .formatToParts(instant).filter(part => part.type !== "literal").map(part => [part.type, part.value]));
const listEntries = async () => (await (await fetch(`${API_URL}/api/tanks/${tank.id}/care?limit=50`, { headers })).json()).items;
const near = (iso, expected, tolerance = 3600 * 1000 + 61000) => Math.abs(Date.parse(iso) - expected) <= tolerance;

const browser = await chromium.launch();
const errors = [];
let page;
try {
  page = await browser.newPage({ locale: "en-US", timezoneId: ZONE, colorScheme: "light", viewport: { width: 390, height: 844 } });
  page.on("pageerror", error => errors.push(String(error)));
  await page.goto(`${WEB_URL}/me?lang=en`, { waitUntil: "networkidle" });
  await logInThroughKeycloak(page);
  await page.goto(`${WEB_URL}/tanks/${tank.id}`, { waitUntil: "networkidle" });
  await byTestId(page, "tank-view-care").click();
  await byTestId(page, "care-measure").waitFor();

  // AC #1: nitrate measured two days ago, entered now, shows at that date in the entry list and the chart.
  const twoDaysAgo = Date.now() - 2 * DAY;
  await byTestId(page, "care-measure").click();
  const options = byTestId(page, "measure-when");
  await options.waitFor();
  for (const kind of ["now", "yesterday", "twoDaysAgo", "custom"]) {
    const bounds = await byTestId(page, `measure-when-${kind}`).boundingBox();
    assert.ok(bounds.height >= 36 && bounds.width >= 48, `${kind} chip is large enough`);
  }
  await byTestId(page, "measure-input-no3").fill("25");
  await byTestId(page, "measure-when-twoDaysAgo").click();
  await byTestId(page, "measure-when-preview").waitFor();
  assert.ok((await byTestId(page, "measure-when-preview").innerText()).includes(dateOf(twoDaysAgo)));
  await page.screenshot({ path: "/tmp/task-5.16-form-light.png" });
  await byTestId(page, "measure-save").click();
  await page.locator('[aria-modal="true"]').waitFor({ state: "hidden" });
  await page.waitForFunction(() => document.querySelectorAll('[data-testid="care-graph-value-no3"]').length === 1);
  const measured = (await listEntries()).find(entry => entry.kind === "measurement");
  assert.ok(near(measured.occurredAt, twoDaysAgo), `measurement stored at two days ago: ${measured.occurredAt}`);
  assert.ok(Date.now() - Date.parse(measured.recordedAt) < 60000, "recorded now");
  const entryText = await byTestId(page, `care-entry-${measured.id}`).innerText();
  assert.ok(entryText.includes(dateOf(twoDaysAgo)), `entry list shows ${dateOf(twoDaysAgo)}: ${entryText}`);
  const chartText = await byTestId(page, "care-graph-value-no3").first().innerText();
  assert.ok(chartText.includes(dateOf(twoDaysAgo)) && chartText.includes("25 mg/l"), `chart shows the date: ${chartText}`);
  await page.emulateMedia({ colorScheme: "dark" });
  await byTestId(page, "care-graphs").screenshot({ path: "/tmp/task-5.16-graph-dark.png" });
  await page.emulateMedia({ colorScheme: "light" });

  // A chosen date and time in the member's zone, never in the future.
  const threeDaysAgo = Date.now() - 3 * DAY;
  const past = localParts(threeDaysAgo);
  await byTestId(page, "care-measure").click();
  await byTestId(page, "measure-input-ph").fill("7.4");
  await byTestId(page, "measure-when-custom").click();
  const tomorrow = localParts(Date.now() + DAY);
  await byTestId(page, "measure-when-date").fill(`${tomorrow.year}-${tomorrow.month}-${tomorrow.day}`);
  await byTestId(page, "measure-when-time").fill("12:00");
  await byTestId(page, "measure-when-error").waitFor();
  assert.ok((await byTestId(page, "measure-when-error").innerText()).includes("not in the future"));
  assert.equal(await byTestId(page, "measure-save").isDisabled(), true);
  await byTestId(page, "measure-when-date").fill("2026-02-30");
  assert.ok((await byTestId(page, "measure-when-error").innerText()).includes("real date"));
  await byTestId(page, "measure-when-date").fill(`${past.year}-${past.month}-${past.day}`);
  await byTestId(page, "measure-when-time").fill("09:30");
  await page.waitForFunction(() => document.querySelectorAll('[data-testid="measure-when-error"]').length === 0);
  await byTestId(page, "measure-save").click();
  await page.locator('[aria-modal="true"]').waitFor({ state: "hidden" });
  await page.waitForFunction(() => document.querySelectorAll('[data-testid="care-graph-value-ph"]').length === 1);
  const chosen = (await listEntries()).find(entry => entry.values?.some(value => value.parameter === "ph"));
  const stored = localParts(Date.parse(chosen.occurredAt));
  assert.deepEqual([stored.year, stored.month, stored.day, stored.hour, stored.minute], [past.year, past.month, past.day, "09", "30"]);

  // The water change quick log records yesterday; its edit view moves the time.
  const yesterday = Date.now() - DAY;
  await byTestId(page, "care-when-yesterday").click();
  await byTestId(page, "care-water-change").click();
  await byTestId(page, "care-confirmation").waitFor();
  const waterChange = (await listEntries()).find(entry => entry.kind === "water_change");
  assert.ok(near(waterChange.occurredAt, yesterday), `water change stored yesterday: ${waterChange.occurredAt}`);
  await byTestId(page, `care-entry-${waterChange.id}`).waitFor();
  assert.ok((await byTestId(page, `care-entry-${waterChange.id}`).innerText()).includes(dateOf(yesterday)));
  await byTestId(page, `care-edit-${waterChange.id}`).click();
  await byTestId(page, "care-edit-when-keep").waitFor();
  await byTestId(page, "care-edit-when-twoDaysAgo").click();
  await byTestId(page, "care-time-save").click();
  await page.locator('[aria-modal="true"]').waitFor({ state: "hidden" });
  await byTestId(page, `care-entry-${waterChange.id}`).filter({ hasText: dateOf(twoDaysAgo) }).waitFor();
  const moved = (await listEntries()).find(entry => entry.id === waterChange.id);
  assert.ok(near(moved.occurredAt, twoDaysAgo));
  assert.equal(moved.amount, null);

  // The edit view of a measurement corrects its time together with its values.
  await byTestId(page, `care-edit-${measured.id}`).click();
  await byTestId(page, "measure-when-keep").waitFor();
  await byTestId(page, "measure-when-yesterday").click();
  await byTestId(page, "measure-input-no3").fill("30");
  await byTestId(page, "measure-save").click();
  await page.locator('[aria-modal="true"]').waitFor({ state: "hidden" });
  await page.getByText("30 mg/l").first().waitFor();
  const corrected = (await listEntries()).find(entry => entry.id === measured.id);
  assert.ok(near(corrected.occurredAt, yesterday));
  assert.equal(Number(corrected.values[0].value), 30);
  assert.ok((await byTestId(page, "care-graph-value-no3").first().innerText()).includes(dateOf(yesterday)));

  // The quick log of the + tab offers the same choice.
  await page.goto(`${WEB_URL}/care?tank=${tank.id}`, { waitUntil: "networkidle" });
  await byTestId(page, "care-quick-log").waitFor();
  const before = (await listEntries()).length;
  await byTestId(page, "care-when-twoDaysAgo").click();
  await byTestId(page, "care-water-change").click();
  await byTestId(page, "care-confirmation").waitFor();
  const afterEntries = await listEntries();
  assert.equal(afterEntries.length, before + 1);
  assert.ok(afterEntries.some(entry => entry.kind === "water_change" && entry.id !== waterChange.id && near(entry.occurredAt, twoDaysAgo)));

  // The time of an entry from the guided water change is fixed; its amount stays editable.
  const started = await fetch(`${API_URL}/api/tanks/${tank.id}/water-changes`, { method: "POST", headers });
  assert.equal(started.status, 201);
  const session = await started.json();
  const drained = await fetch(`${API_URL}/api/tanks/${tank.id}/water-changes/${session.id}/steps/draining_started`, { method: "PUT", headers, body: JSON.stringify({ state: "done" }) });
  assert.equal(drained.status, 200);
  const finished = await fetch(`${API_URL}/api/tanks/${tank.id}/water-changes/${session.id}/finish`, { method: "POST", headers, body: JSON.stringify({ amount: { value: 30, unit: "percent" } }) });
  assert.equal(finished.status, 200);
  const guided = (await finished.json()).entry;
  await page.goto(`${WEB_URL}/tanks/${tank.id}`, { waitUntil: "networkidle" });
  await byTestId(page, "tank-view-care").click();
  await byTestId(page, `care-edit-${guided.id}`).click();
  await byTestId(page, "care-time-fixed").waitFor();
  assert.equal(await byTestId(page, "care-edit-when").count(), 0, "the guided entry offers no time choice");
  assert.equal(await byTestId(page, "care-time-save").count(), 0);
  await page.screenshot({ path: "/tmp/task-5.16-fixed-light.png" });
  await byTestId(page, "care-amount-input").fill("35");
  await byTestId(page, "care-amount-save").click();
  await page.locator('[aria-modal="true"]').waitFor({ state: "hidden" });
  const amended = (await listEntries()).find(entry => entry.id === guided.id);
  assert.equal(Number(amended.amount.value), 35);
  assert.equal(amended.occurredAt, guided.occurredAt, "the time of the guided entry stays");
  const refused = await fetch(`${API_URL}/api/tanks/${tank.id}/care/${guided.id}`, { method: "PATCH", headers, body: JSON.stringify({ occurredAt: new Date(Date.now() - 2 * DAY).toISOString() }) });
  assert.equal(refused.status, 400);
  assert.equal((await refused.json()).code, "care_time_fixed");

  // The API refuses a future time on a correction with a coded problem.
  const future = await fetch(`${API_URL}/api/tanks/${tank.id}/care/${measured.id}`, { method: "PATCH", headers, body: JSON.stringify({ occurredAt: new Date(Date.now() + 3600 * 1000).toISOString() }) });
  assert.equal(future.status, 400);
  assert.equal((await future.json()).code, "care_time_invalid");

  await page.goto(`${WEB_URL}/me`, { waitUntil: "networkidle" });
  await signOutThroughKeycloak(page);
  assert.deepEqual(errors, []);
  console.log("TASK-5.16: nitrate two days ago, a chosen date and time, yesterday's water change, time corrections and the + tab passed.");
} catch (error) {
  console.error(errors, await page?.locator("body").innerText());
  await page?.screenshot({ path: "/tmp/task-5.16-failure.png" });
  throw error;
} finally {
  await browser.close();
}
