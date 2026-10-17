// Story: TASK-8.4
// A member who returns after an absence of 30 days sees the return retrospective, as a member of the web app:
// - At the first sign-in of the return the retrospective opens full screen with the overdue care of the own
//   tank, the news of a followed tank and the next steps; the news of a followed tank that turned private is
//   not part of it, and nothing hints at it.
// - The next steps lead to the care view of the tank (log missed care) and to the guided camera.
// - Opening it records it as seen: a later start does not open it again, the Me tab opens the same cards
//   for 30 days and shows no entry afterwards.
// - A second absence within 91 days of the last retrospective opens none and lapses; a new absence after 91 days opens a new one.
//
// Runs on the isolated write stack only (see e2e/write-stack.sh). The spec creates its own account, tanks and
// content and removes them when it ends, so the order of the specs does not matter.
import assert from "node:assert/strict";
import { chromium } from "playwright";
import {
  API_URL, WEB_URL, ZONE, byTestId, createKeycloakAccount, deleteKeycloakAccount, passwordGrantToken, signOutThroughKeycloak, sql, submitKeycloakLogin,
} from "./support.mjs";

assert.equal(process.env.E2E_DB, "cichlids_e2e", "Refusing to run outside the cichlids_e2e database");

const stamp = Date.now();
const username = `return-${stamp}`;
const password = "return-password"; // gitleaks:allow
const first = sql => Number(sql.split("\n")[0]);
let accountId;
let memberId;
let ownerId;
const tanks = [];

const insertTank = (profileId, title, visibility) => {
  const id = first(sql(`insert into tank (profile_id, title, state, created_at, published_at, profile_visibility, stream_visibility, care_reminders_enabled)
    values (${profileId}, '${title}', 'published', now(), now(), '${visibility}', '${visibility}', true) returning id`));
  tanks.push(id);
  return id;
};

function cleanUp() {
  const ids = tanks.length ? tanks.join(", ") : "0";
  sql(`delete from media_link where tank_id in (${ids})`);
  sql(`delete from media_item where owner_profile_id in (${memberId ?? 0}, ${ownerId ?? 0})`);
  sql(`delete from care_entry where tank_id in (${ids})`);
  sql(`delete from follow where target_tank_id in (${ids})`);
  sql(`delete from story where author_profile_id = ${ownerId ?? 0}`);
  sql(`delete from outbox_event where aggregate_type = 'tank' and aggregate_id in (${tanks.map(id => `'${id}'`).join(", ") || "''"})`);
  sql(`delete from tank where id in (${ids})`);
  if (memberId) {
    sql(`delete from outbox_event where aggregate_id = '${memberId}' and aggregate_type = 'profile'`);
    sql(`delete from profile_identity where profile_id = ${memberId}`);
    sql(`delete from profile where id = ${memberId}`);
  }
  if (ownerId) sql(`delete from profile where id = ${ownerId}`);
}

const text = async (page, testId) => (await byTestId(page, testId).innerText()).trim();
const waitForCard = (page, testId) => byTestId(page, testId).waitFor({ timeout: 30000 });
const sizeOk = async (page, id) => {
  const box = await byTestId(page, id).boundingBox();
  assert.ok(box && box.height >= 48 && box.width >= 48, `${id} is at least 48 by 48`);
};
const reloadMe = async page => {
  await page.goto(`${WEB_URL}/me?lang=en`, { waitUntil: "networkidle" });
  await byTestId(page, "me-name").waitFor({ timeout: 30000 });
  await page.waitForTimeout(2500);
};

const browser = await chromium.launch({ channel: "chromium" });
const errors = [];
let page;
try {
  accountId = await createKeycloakAccount(username, password);
  const token = await passwordGrantToken(username, password);
  const headers = { Authorization: `Bearer ${token}`, "Content-Type": "application/json" };
  memberId = Number((await (await fetch(`${API_URL}/api/me`, { headers })).json()).profile.id);
  assert.equal((await fetch(`${API_URL}/api/me/onboarding`, { method: "POST", headers, body: JSON.stringify({ skip: true }) })).status, 200);

  // Content: an own tank with a water change 40 days ago and a time-lapse photo, a followed public tank with a
  // fresh story, and a followed tank that turned private after the follow.
  ownerId = first(sql(`insert into profile (username, display_name, kind, created_at) values ('return-owner-${stamp}', 'Return Owner', 'member', now()) returning id`));
  const ownTank = insertTank(memberId, `Return own tank ${stamp}`, "private");
  sql(`insert into care_entry (tank_id, author_profile_id, kind, occurred_at, recorded_at, only_me) values (${ownTank}, ${memberId}, 'water_change', now() - interval '40 days', now() - interval '40 days', false)`);
  sql(`with m as (insert into media_item (owner_profile_id, kind, storage_key, created_at, received_at) values (${memberId}, 'photo', 'private/e2e/return-${stamp}.jpg', now(), now()) returning id)
    insert into media_link (tank_id, media_item_id, context_type, role, position, created_at) select ${ownTank}, id, 'tank', 'time_lapse', 0, now() - interval '50 days' from m`);
  const newsTank = insertTank(ownerId, `Return news tank ${stamp}`, "public");
  const hiddenTank = insertTank(ownerId, `Return secret tank ${stamp}`, "public");
  for (const [tank, title] of [[newsTank, "Fresh spawn"], [hiddenTank, "Secret spawn"]]) {
    const storyId = first(sql(`insert into story (author_profile_id, topic, title, state, view_count, rating_count, created_at, updated_at, published_at)
      values (${ownerId}, 'unknown', '${title}', 'published', 0, 0, now() - interval '5 days', now() - interval '5 days', now() - interval '5 days') returning id`));
    sql(`insert into story_subject (story_id, subject_type, tank_id) values (${storyId}, 'tank', ${tank})`);
    assert.ok([200, 201, 204].includes((await fetch(`${API_URL}/api/tanks/${tank}/follow`, { method: "PUT", headers })).status));
  }

  sql(`update tank set profile_visibility = 'private', stream_visibility = 'private' where id = ${hiddenTank}`);
  sql(`update profile set last_seen_at = now(), away_since = now() - interval '30 days' where id = ${memberId}`);

  const apiStatus = async () => (await fetch(`${API_URL}/api/me/retrospectives/return?date=${new Date().toISOString().slice(0, 10)}`, { headers })).status;
  assert.equal(await apiStatus(), 200);

  const context = await browser.newContext({ locale: "en-US", timezoneId: ZONE, colorScheme: "light", viewport: { width: 390, height: 844 } });
  page = await context.newPage();
  page.on("pageerror", error => errors.push(String(error)));
  await page.goto(`${WEB_URL}/me?lang=en`, { waitUntil: "networkidle" });

  // The first sign-in of the return opens the retrospective: welcome, overdue care, followed news, next steps.
  await submitKeycloakLogin(page, byTestId(page, "login-button"), username, password);
  await byTestId(page, "retrospective-player").waitFor({ timeout: 30000 });
  await byTestId(page, "retrospective-toggle").click();
  await waitForCard(page, "retrospective-card-return-welcome");
  assert.equal(await byTestId(page, "retrospective-player").locator('[data-testid^="retrospective-progress-"]').count(), 4);
  await page.screenshot({ path: "/tmp/task-8.4-welcome-light.png" });
  await byTestId(page, "retrospective-next").click();
  await waitForCard(page, "retrospective-card-return-overdue");
  const overdue = await text(page, `return-overdue-${ownTank}-water_change`);
  assert.ok(overdue.includes(`Return own tank ${stamp}`) && overdue.includes("Water change") && /days overdue: 3\d/i.test(overdue), `overdue care: ${overdue}`);
  await page.screenshot({ path: "/tmp/task-8.4-overdue-light.png" });
  await byTestId(page, "retrospective-next").click();
  await waitForCard(page, "retrospective-card-return-followed");
  assert.match(await text(page, `return-followed-${newsTank}`), /New story/);
  assert.equal(await byTestId(page, `return-followed-${hiddenTank}`).count(), 0, "the news of the tank that turned private is not shown");
  assert.ok(!(await page.locator("body").innerText()).includes("Secret"), "nothing hints at the private tank");
  await sizeOk(page, `return-followed-open-${newsTank}`);
  await page.screenshot({ path: "/tmp/task-8.4-followed-light.png" });
  await byTestId(page, "retrospective-next").click();
  await waitForCard(page, "retrospective-card-return-next");
  await sizeOk(page, `return-log-care-${ownTank}`);
  await sizeOk(page, `return-photo-${ownTank}`);
  assert.equal(await byTestId(page, "retrospective-next").isDisabled(), true);
  await page.screenshot({ path: "/tmp/task-8.4-next-light.png" });
  await page.emulateMedia({ colorScheme: "dark" });
  await page.screenshot({ path: "/tmp/task-8.4-next-dark.png" });
  await page.emulateMedia({ colorScheme: "light" });

  // It is recorded as seen when it opens.
  assert.equal(sql(`select (return_retrospective_seen_at is not null and away_since is null and return_retrospective_since is not null)::text from profile where id = ${memberId}`), "true");

  // The next steps lead to the care view of the tank and to the guided camera.
  await byTestId(page, `return-log-care-${ownTank}`).click();
  await page.waitForURL(url => url.pathname === `/tanks/${ownTank}` && url.search.includes("view=care"), { timeout: 30000 });
  await byTestId(page, "care-measure").waitFor({ timeout: 30000 });

  // A later start does not open it; the Me tab opens the same cards.
  await reloadMe(page);
  assert.equal(await byTestId(page, "retrospective-player").count(), 0, "the retrospective does not open at a later start");
  await byTestId(page, "me-return-retrospective").click();
  await waitForCard(page, "retrospective-card-return-welcome");
  await byTestId(page, "retrospective-toggle").click();
  await byTestId(page, "retrospective-next").click();
  await byTestId(page, "retrospective-next").click();
  await byTestId(page, "retrospective-next").click();
  await waitForCard(page, "retrospective-card-return-next");
  await byTestId(page, `return-photo-${ownTank}`).click();
  await page.waitForURL(url => url.pathname === `/tanks/${ownTank}/camera`, { timeout: 30000 });

  // 31 days after seeing it the Me tab shows no entry.
  sql(`update profile set return_retrospective_seen_at = now() - interval '31 days' where id = ${memberId}`);
  await reloadMe(page);
  assert.equal(await byTestId(page, "me-return-retrospective").count(), 0);
  assert.equal(await byTestId(page, "retrospective-player").count(), 0);

  // A second absence 40 days after the last retrospective opens none.
  sql(`update profile set return_retrospective_seen_at = now() - interval '40 days', away_since = now() - interval '29 days' where id = ${memberId}`);
  assert.equal(await apiStatus(), 204);
  assert.equal(sql(`select (away_since is null)::text from profile where id = ${memberId}`), "true", "the absence that cannot be shown lapses");
  await reloadMe(page);
  assert.equal(await byTestId(page, "retrospective-player").count(), 0, "no retrospective within 91 days of the last one");
  assert.equal(await byTestId(page, "me-return-retrospective").count(), 0);

  // After 91 days a new absence opens a new one.
  sql(`update profile set return_retrospective_seen_at = now() - interval '92 days', away_since = now() - interval '30 days' where id = ${memberId}`);
  await page.goto(`${WEB_URL}/me?lang=en`, { waitUntil: "networkidle" });
  await byTestId(page, "retrospective-player").waitFor({ timeout: 30000 });
  await waitForCard(page, "retrospective-card-return-welcome");
  await byTestId(page, "retrospective-close").click();
  await byTestId(page, "me-name").waitFor({ timeout: 30000 });
  await signOutThroughKeycloak(page);
  await context.close();

  assert.deepEqual(errors, []);
  console.log("TASK-8.4: the return retrospective opens at the first start of a return with the overdue care, the news and the next steps, hides private news, opens again from Me for 30 days and waits 91 days.");
} catch (error) {
  console.error(errors, await page?.locator("body").innerText().catch(() => ""));
  await page?.screenshot({ path: "/tmp/task-8.4-failure.png" }).catch(() => {});
  throw error;
} finally {
  await browser.close();
  cleanUp();
  if (accountId) await deleteKeycloakAccount(accountId).catch(() => {});
}
