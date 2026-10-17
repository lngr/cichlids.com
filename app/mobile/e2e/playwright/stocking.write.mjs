// Story: TASK-9.1
// The stocking of a tank on the web build: the owner adds a group of 12 of a species with a past
// moved-in day and a named fish called Bruno, the tank page lists Bruno first with the days, the
// owner changes the group, a future day is refused, and removing Bruno as died moves it from the
// current stocking into the collapsible history with the day and the reason. Visitors read the same
// stocking of a public tank but cannot write to it.
//
// Runs on the isolated write stack only (see e2e/write-stack.sh), since it writes to the database.
// The spec creates its own tank and species and removes them at the end; it does not depend on the
// order of the other specs.
import assert from "node:assert/strict";
import { chromium } from "playwright";
import { API_URL, WEB_URL, ZONE, byTestId, localDate, logInThroughKeycloak, passwordGrantToken, sql } from "./support.mjs";

assert.equal(process.env.E2E_DB, "cichlids_e2e");
const token = await passwordGrantToken();
const headers = { Authorization: `Bearer ${token}`, "Content-Type": "application/json" };
const created = await fetch(`${API_URL}/api/tanks`, { method: "POST", headers, body: JSON.stringify({ title: "Stocking tank", isPublic: true }) });
assert.equal(created.status, 201);
const tank = Number((await created.json()).id);
const genus = `Stocking${Date.now()}`;
const speciesId = Number(sql(`INSERT INTO species (genus, name, display_name, breeding, aggression, intra_aggression, diet, created_at)
  VALUES ('${genus}', 'testfish', '${genus} testfish', 'unspecified', 'low', 'low', 'omnivore', now()) RETURNING id`).split("\n")[0]);

const formatDay = day => new Intl.DateTimeFormat("en", { year: "numeric", month: "short", day: "numeric", timeZone: "UTC" })
  .format(new Date(`${day}T00:00:00Z`));
const today = localDate();
const pastDay = localDate(new Date(), -10);
const tomorrow = localDate(new Date(), 1);
const listStocking = async (extra = {}) => (await fetch(`${API_URL}/api/tanks/${tank}/inhabitants`, extra));
const text = async (page, id) => (await byTestId(page, id).first().innerText()).trim();
const sizeOk = async (page, ids) => {
  for (const id of ids) {
    const box = await byTestId(page, id).first().boundingBox();
    assert.ok(box && box.height >= 48 && box.width >= 48, `${id} is at least 48 by 48`);
  }
};

const browser = await chromium.launch();
const errors = [];
try {
  const context = await browser.newContext({ locale: "en-US", timezoneId: ZONE, colorScheme: "light", viewport: { width: 390, height: 844 } });
  const page = await context.newPage();
  page.on("pageerror", error => errors.push(String(error)));
  await page.goto(`${WEB_URL}/me?lang=en`, { waitUntil: "networkidle" });
  await logInThroughKeycloak(page);
  await page.goto(`${WEB_URL}/tanks/${tank}?lang=en`, { waitUntil: "networkidle" });

  // AC1: a group of 12 with a past day and a named fish; the tank page lists Bruno first with the days.
  await byTestId(page, "stocking-add").click();
  await byTestId(page, "stocking-species-search").fill(genus);
  await byTestId(page, `stocking-species-option-${speciesId}`).click();
  await byTestId(page, "stocking-count").fill("12");
  await byTestId(page, "stocking-moved-in").fill(pastDay);
  await byTestId(page, "stocking-save").click();
  await byTestId(page, "stocking-item").first().waitFor();

  await byTestId(page, "stocking-add").click();
  await byTestId(page, "stocking-kind-named").click();
  await byTestId(page, "stocking-name").fill("Bruno");
  await byTestId(page, "stocking-save").click();
  await page.waitForFunction(() => document.querySelectorAll('[data-testid="stocking-item"]').length === 2);
  const items = await byTestId(page, "stocking-item").allInnerTexts();
  assert.match(items[0], /Bruno/);
  assert.ok(items[0].includes(`Since ${formatDay(today)}`), items[0]);
  assert.ok(items[1].includes(`${genus} testfish`), items[1]);
  assert.ok(items[1].includes("12"), items[1]);
  assert.ok(items[1].includes(`Since ${formatDay(pastDay)}`), items[1]);
  await sizeOk(page, ["stocking-add"]);
  await page.screenshot({ path: "/tmp/task-9.1-stocking-light.png" });

  const stocking = await (await listStocking()).json();
  assert.equal(stocking.current.length, 2);
  assert.equal(stocking.current[0].name, "Bruno");
  assert.equal(stocking.current[0].movedInOn, today);
  assert.equal(stocking.current[1].movedInOn, pastDay);
  const bruno = Number(stocking.current[0].id);
  const group = Number(stocking.current[1].id);
  await sizeOk(page, [`stocking-edit-${group}`, `stocking-remove-${bruno}`]);

  // A change of the group.
  await byTestId(page, `stocking-edit-${group}`).click();
  await byTestId(page, "stocking-count").fill("14");
  await byTestId(page, "stocking-save").click();
  await page.waitForFunction(() => [...document.querySelectorAll('[data-testid="stocking-item"]')].some(item => item.textContent.includes("14")));
  assert.equal((await (await listStocking()).json()).current[1].count, 14);

  // A day in the future is refused in the form and nothing is stored.
  await byTestId(page, "stocking-add").click();
  await byTestId(page, "stocking-kind-named").click();
  await byTestId(page, "stocking-name").fill("Emma");
  await byTestId(page, "stocking-moved-in").fill(tomorrow);
  await byTestId(page, "stocking-save").click();
  assert.equal(await text(page, "stocking-form-error"), "The day cannot lie in the future.");
  await byTestId(page, "stocking-cancel").click();
  assert.equal((await (await listStocking()).json()).current.length, 2);

  // AC2: Bruno died; it leaves the current stocking and shows in the collapsed history with day and reason.
  await byTestId(page, `stocking-remove-${bruno}`).click();
  await byTestId(page, "stocking-reason-died").click();
  await byTestId(page, "stocking-remove-confirm").click();
  await page.waitForFunction(() => document.querySelectorAll('[data-testid="stocking-item"]').length === 1);
  assert.equal(await byTestId(page, "stocking-history").count(), 0, "the history is collapsed");
  const toggle = byTestId(page, "stocking-history-toggle");
  await sizeOk(page, ["stocking-history-toggle"]);
  await toggle.click();
  await byTestId(page, "stocking-history").waitFor();
  const history = await text(page, "stocking-history-item");
  assert.match(history, /Bruno/);
  assert.ok(history.includes(`Died on ${formatDay(today)}`), history);

  const after = await (await listStocking()).json();
  assert.equal(after.current.length, 1);
  assert.equal(after.history[0].removalReason, "died");
  assert.equal(after.history[0].removedOn, today);

  // AC3: a visitor reads the stocking of the public tank and cannot write to it.
  const visitor = await browser.newContext({ locale: "en-US", timezoneId: ZONE, colorScheme: "dark", viewport: { width: 390, height: 844 } });
  const visitorPage = await visitor.newPage();
  visitorPage.on("pageerror", error => errors.push(String(error)));
  await visitorPage.goto(`${WEB_URL}/tanks/${tank}?lang=en`, { waitUntil: "networkidle" });
  await byTestId(visitorPage, "stocking-item").first().waitFor();
  assert.equal(await byTestId(visitorPage, "stocking-add").count(), 0);
  assert.equal(await byTestId(visitorPage, `stocking-remove-${group}`).count(), 0);
  await byTestId(visitorPage, "stocking-history-toggle").click();
  assert.match(await text(visitorPage, "stocking-history-item"), /Died on/);
  await visitorPage.screenshot({ path: "/tmp/task-9.1-stocking-dark.png" });
  const refused = await fetch(`${API_URL}/api/tanks/${tank}/inhabitants?date=${today}`, { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ name: "Intruder" }) });
  assert.equal(refused.status, 401);
  assert.equal((await (await listStocking()).json()).current.length, 1);

  const events = sql(`SELECT event_type FROM outbox_event WHERE aggregate_type = 'inhabitant' AND payload::jsonb->>'tankId' = '${tank}' ORDER BY occurred_at, event_type`).split("\n");
  assert.deepEqual(events.slice().sort(), ["inhabitant.added", "inhabitant.added", "inhabitant.removed", "inhabitant.updated"]);
  assert.deepEqual(errors, []);
  console.log("TASK-9.1: group and named fish added with their days, the group changed, a future day refused, the removal as died into the collapsible history, visitors reading and not writing passed.");
} finally {
  await browser.close();
  sql(`DELETE FROM outbox_event WHERE aggregate_type = 'inhabitant' AND payload::jsonb->>'tankId' = '${tank}'`);
  sql(`DELETE FROM inhabitant WHERE tank_id = ${tank}`);
  sql(`DELETE FROM species WHERE id = ${speciesId}`);
  sql(`UPDATE tank SET deleted_at = now() WHERE id = ${tank}`);
}
