// Story: TASK-5.24
// Today shows the followed tanks and the milestones, as a member of the web app:
// - A member follows a public tank with a fresh story and a public tank whose care stream is private
//   and whose only update is a care entry. Today lists the first with its story and says nothing of
//   the second; the list of all followed tanks shows both, the second without an update.
// - A tank that completed a full tank year three days ago shows a congratulation on Today; dismissing
//   it removes it, and it stays away after a reload.
//
// Runs on the isolated write stack only (see e2e/write-stack.sh). The spec creates its own tanks,
// follows and dismissals and removes them when it ends, so the order of the specs does not matter.
import assert from "node:assert/strict";
import { chromium } from "playwright";
import { API_URL, WEB_URL, ZONE, byTestId, logInThroughKeycloak, passwordGrantToken, signOutThroughKeycloak, sql } from "./support.mjs";

assert.equal(process.env.E2E_DB, "cichlids_e2e", "Refusing to run outside the cichlids_e2e database");

const token = await passwordGrantToken();
const headers = { Authorization: `Bearer ${token}`, "Content-Type": "application/json" };
const profileId = Number((await (await fetch(`${API_URL}/api/me`, { headers })).json()).profile.id);
const suffix = Date.now();

// The owner of the followed tanks is a member without a login; the tanks are written to the database
// because a spec cannot sign in as a second member.
const ownerId = Number(sql(`insert into profile (username, display_name, kind, created_at) values ('e2e-followed-${suffix}', 'Followed Owner', 'member', now()) returning id`).split("\n")[0]);
const insertTank = title => Number(sql(`insert into tank (profile_id, title, state, created_at, published_at, profile_visibility, stream_visibility, care_reminders_enabled)
  values (${ownerId}, '${title}', 'published', now(), now(), 'public', '${title.includes("hidden") ? "private" : "public"}', true) returning id`).split("\n")[0]);
const storyTank = insertTank("E2E followed story tank");
const hiddenTank = insertTank("E2E followed hidden tank");
const storyId = Number(sql(`insert into story (author_profile_id, topic, title, state, view_count, rating_count, created_at, updated_at, published_at)
  values (${ownerId}, 'unknown', 'Fresh spawn', 'published', 0, 0, now() - interval '30 hours', now() - interval '30 hours', now() - interval '30 hours') returning id`).split("\n")[0]);
sql(`insert into story_subject (story_id, subject_type, tank_id) values (${storyId}, 'tank', ${storyTank})`);
sql(`insert into care_entry (tank_id, author_profile_id, kind, occurred_at, recorded_at, only_me) values (${storyTank}, ${ownerId}, 'measurement', now(), now(), true)`);
sql(`insert into care_entry (tank_id, author_profile_id, kind, occurred_at, recorded_at, only_me) values (${hiddenTank}, ${ownerId}, 'water_change', now() - interval '2 hours', now() - interval '2 hours', false)`);

for (const tank of [storyTank, hiddenTank]) {
  const response = await fetch(`${API_URL}/api/tanks/${tank}/follow`, { method: "PUT", headers });
  assert.ok([200, 201, 204].includes(response.status), `following tank ${tank} answered ${response.status}`);
}

// An own tank that is two years and three days old.
const created = await fetch(`${API_URL}/api/tanks`, { method: "POST", headers, body: JSON.stringify({ title: "E2E milestone tank", isPublic: false, volume: { value: 120, unit: "liter" } }) });
assert.equal(created.status, 201);
const ownTank = (await created.json()).id;
sql(`update tank set created_at = now() - interval '2 years 3 days' where id = ${ownTank}`);
const milestoneKey = `tank_anniversary:${ownTank}:2`;

const sizeOk = async (page, id) => {
  const box = await byTestId(page, id).boundingBox();
  assert.ok(box && box.height >= 48 && box.width >= 48, `${id} is at least 48 by 48`);
};
const expectText = async (page, id, expected) => {
  await page.waitForFunction(([testId, value]) => document.querySelector(`[data-testid="${testId}"]`)?.textContent?.includes(value), [id, expected], { timeout: 30000 })
    .catch(async error => { throw new Error(`${id}: expected "${expected}", got "${await byTestId(page, id).innerText().catch(() => "(missing)")}" (${error.message})`); });
};

const browser = await chromium.launch({ channel: "chromium" });
const errors = [];
let page;
try {
  const context = await browser.newContext({ locale: "en-US", timezoneId: ZONE, colorScheme: "light", viewport: { width: 390, height: 844 } });
  page = await context.newPage();
  page.on("pageerror", error => errors.push(String(error)));
  await page.goto(`${WEB_URL}/me?lang=en`, { waitUntil: "networkidle" });
  await logInThroughKeycloak(page);

  const followed = page.waitForResponse(response => response.url().includes("/api/me/today/followed") && response.status() === 200, { timeout: 30000 });
  await page.goto(`${WEB_URL}/?lang=en`, { waitUntil: "networkidle" });
  await followed;

  // The followed tank with the story shows with it; the tank with only a private stream does not.
  const card = `today-followed-${storyTank}`;
  await byTestId(page, card).waitFor({ timeout: 30000 });
  await expectText(page, card, "E2E followed story tank");
  await expectText(page, card, "New story, 1 day ago");
  await sizeOk(page, card);
  assert.equal(await byTestId(page, `today-followed-${hiddenTank}`).count(), 0, "no hint of the tank whose update is private");
  await page.screenshot({ path: "/tmp/task-5.24-today-light.png" });

  // The congratulation on the full tank year.
  const milestone = `today-milestone-${milestoneKey}`;
  await byTestId(page, milestone).waitFor({ timeout: 30000 });
  await expectText(page, milestone, "E2E milestone tank is 2 years old.");
  await sizeOk(page, `today-milestone-dismiss-${milestoneKey}`);

  // All followed tanks: both, the hidden one without an update.
  await byTestId(page, "today-followed-all").click();
  await page.waitForURL(url => url.pathname === "/followed", { timeout: 30000 });
  await byTestId(page, `followed-tank-${storyTank}`).waitFor({ timeout: 30000 });
  await expectText(page, `followed-tank-${hiddenTank}`, "No update so far");
  await expectText(page, `followed-tank-${storyTank}`, "New story, 1 day ago");
  await page.screenshot({ path: "/tmp/task-5.24-followed-light.png" });

  // Dismissing the congratulation removes it for good.
  await page.goto(`${WEB_URL}/?lang=en`, { waitUntil: "networkidle" });
  await byTestId(page, milestone).waitFor({ timeout: 30000 });
  // The card leaves at once; the stored dismissal is checked after the answer of the request.
  const dismissed = page.waitForResponse(response => response.url().includes("/dismissal") && response.request().method() === "PUT", { timeout: 30000 });
  await byTestId(page, `today-milestone-dismiss-${milestoneKey}`).click();
  await byTestId(page, milestone).waitFor({ state: "detached", timeout: 30000 });
  assert.equal((await dismissed).status(), 204);
  assert.equal(sql(`select count(*) from milestone_dismissal where profile_id = ${profileId} and key = '${milestoneKey}'`), "1");
  const reloaded = page.waitForResponse(response => response.url().includes("/api/me/today/milestones") && response.status() === 200, { timeout: 30000 });
  await page.reload({ waitUntil: "networkidle" });
  await reloaded;
  assert.equal(await byTestId(page, milestone).count(), 0, "the dismissed congratulation stays away");

  await page.goto(`${WEB_URL}/me?lang=en`, { waitUntil: "networkidle" });
  await signOutThroughKeycloak(page);
  await context.close();

  assert.deepEqual(errors, []);
  console.log("TASK-5.24: Today lists the followed tank with its visible story, hides the private update, and the dismissed tank year congratulation stays away.");
} catch (error) {
  console.error(errors, await page?.locator("body").innerText().catch(() => ""));
  await page?.screenshot({ path: "/tmp/task-5.24-today-followed-failure.png" }).catch(() => {});
  throw error;
} finally {
  await browser.close();
  const tanks = `${storyTank}, ${hiddenTank}, ${ownTank}`;
  sql(`delete from milestone_dismissal where profile_id = ${profileId} and key = '${milestoneKey}'`);
  sql(`delete from follow where target_tank_id in (${tanks})`);
  sql(`delete from outbox_event where aggregate_type = 'tank' and aggregate_id in ('${storyTank}', '${hiddenTank}', '${ownTank}')`);
  sql(`delete from care_entry where tank_id in (${tanks})`);
  sql(`delete from story where id = ${storyId}`);
  sql(`delete from tank where id in (${tanks})`);
  sql(`delete from profile where id = ${ownerId}`);
}
