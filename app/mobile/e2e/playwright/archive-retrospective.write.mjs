// Story: TASK-8.2
// A legacy member sees the archive retrospective once after the onboarding, as a member of the web app:
// - At the first sign-in the retrospective opens full screen with the cards that have data: member
//   since, the numbers, the most viewed photo, the archive tank and the question how the tank looks today.
// - Pause and the buttons to step through the cards work; closing returns to the Me tab and records that
//   the member has seen it.
// - A later sign-in does not open it, and the Me tab opens the same cards again.
// - A member without legacy data gets no retrospective and no entry in the Me tab.
//
// Runs on the isolated write stack only (see e2e/write-stack.sh). The spec creates two accounts with
// their profiles and the legacy content of the first one, and removes all of it at the end.
import assert from "node:assert/strict";
import { chromium } from "playwright";
import {
  API_URL, WEB_URL, ZONE, byTestId, createKeycloakAccount, deleteKeycloakAccount, passwordGrantToken, signOutThroughKeycloak, sql, submitKeycloakLogin,
} from "./support.mjs";

assert.equal(process.env.E2E_DB, "cichlids_e2e", "Refusing to run outside the cichlids_e2e database");

const stamp = Date.now();
const legacyUser = `retro-${stamp}`;
const plainUser = `retro-plain-${stamp}`;
const password = "retro-password"; // gitleaks:allow
const legacyBase = 1_900_000_000 + (stamp % 90_000_000);
const accountIds = [];
const profileIds = [];

async function registerMember(username) {
  accountIds.push(await createKeycloakAccount(username, password));
  const token = await passwordGrantToken(username, password);
  const headers = { Authorization: `Bearer ${token}`, "Content-Type": "application/json" };
  const me = await (await fetch(`${API_URL}/api/me`, { headers })).json();
  profileIds.push(Number(me.profile.id));
  const skipped = await fetch(`${API_URL}/api/me/onboarding`, { method: "POST", headers, body: JSON.stringify({ skip: true }) });
  assert.equal(skipped.status, 200);
  return { token, headers, profileId: Number(me.profile.id) };
}

function seedLegacyContent(profileId) {
  sql(`update profile set created_at = '2012-03-10T10:00:00Z' where id = ${profileId}`);
  const itemIds = [1, 2, 3].map(n => sql(
    `insert into media_item (legacy_id, owner_profile_id, kind, storage_key, created_at, received_at) values (${legacyBase + n}, ${profileId}, 'photo', 'private/e2e/retro-${stamp}-${n}.jpg', now(), now()) returning id`).split("\n")[0]);
  const storyId = sql(
    `insert into story (legacy_id, author_profile_id, topic, title, state, view_count, created_at, updated_at, published_at) values (${legacyBase + 10}, ${profileId}, 'cichlids', 'Retro story ${stamp}', 'published', 321, now(), now(), now()) returning id`).split("\n")[0];
  sql(`insert into slug_alias (story_id, value, is_canonical, created_at) values (${storyId}, 'retro-story-${stamp}', true, now())`);
  sql(`insert into media_link (media_item_id, context_type, story_id, role, position, created_at) values (${itemIds[0]}, 'story', ${storyId}, 'item', 0, now())`);
  sql(`insert into tank (legacy_id, profile_id, title, state, created_at, published_at) values (${legacyBase + 20}, ${profileId}, 'Retro tank ${stamp}', 'published', now(), now())`);
  return storyId;
}

function cleanUp() {
  for (const profileId of profileIds) {
    sql(`delete from outbox_event where aggregate_id = '${profileId}' and aggregate_type = 'profile'`);
    sql(`delete from tank where profile_id = ${profileId}`);
    sql(`delete from slug_alias where story_id in (select id from story where author_profile_id = ${profileId})`);
    sql(`delete from story where author_profile_id = ${profileId}`);
    sql(`delete from media_item where owner_profile_id = ${profileId}`);
    sql(`delete from profile_identity where profile_id = ${profileId}`);
    sql(`delete from profile where id = ${profileId}`);
  }
}

const text = async (page, testId) => (await byTestId(page, testId).innerText()).trim();
const waitForCard = (page, testId) => byTestId(page, testId).waitFor({ timeout: 30000 });
const signIn = async (page, username) => {
  await submitKeycloakLogin(page, byTestId(page, "login-button"), username, password);
  await byTestId(page, "me-name").waitFor({ timeout: 30000 });
};

const browser = await chromium.launch({ channel: "chromium" });
const errors = [];
let page;
try {
  const legacy = await registerMember(legacyUser);
  seedLegacyContent(legacy.profileId);
  const plain = await registerMember(plainUser);

  const apiStatus = async member => (await fetch(`${API_URL}/api/me/retrospectives/archive`, { headers: member.headers })).status;
  assert.equal(await apiStatus(legacy), 200);
  assert.equal(await apiStatus(plain), 204);
  const status = member => fetch(`${API_URL}/api/me`, { headers: member.headers }).then(response => response.json()).then(me => me.archiveRetrospective);
  assert.deepEqual(await status(legacy), { available: true, seenAt: null });
  assert.deepEqual(await status(plain), { available: false, seenAt: null });

  const context = await browser.newContext({ locale: "en-US", timezoneId: ZONE, colorScheme: "light", viewport: { width: 390, height: 844 } });
  page = await context.newPage();
  page.on("pageerror", error => errors.push(String(error)));
  await page.goto(`${WEB_URL}/me?lang=en`, { waitUntil: "networkidle" });

  // The first sign-in after the onboarding opens the retrospective with the cards that have data.
  await submitKeycloakLogin(page, byTestId(page, "login-button"), legacyUser, password);
  await byTestId(page, "retrospective-player").waitFor({ timeout: 30000 });
  await waitForCard(page, "retrospective-card-member-since");
  assert.equal(await text(page, "retrospective-member-since"), "March 2012");
  assert.equal(await byTestId(page, "retrospective-player").locator('[data-testid^="retrospective-progress-"]').count(), 5);
  for (const id of ["retrospective-toggle", "retrospective-close", "retrospective-previous", "retrospective-next"]) {
    const box = await byTestId(page, id).boundingBox();
    assert.ok(box && box.width >= 48 && box.height >= 48, `${id} is at least 48 by 48`);
  }
  await page.screenshot({ path: "/tmp/task-8.2-member-since-light.png" });

  // Pause keeps the card; the buttons step through the cards.
  await byTestId(page, "retrospective-toggle").click();
  assert.equal(await byTestId(page, "retrospective-toggle").getAttribute("aria-label"), "Play");
  await byTestId(page, "retrospective-next").click();
  await waitForCard(page, "retrospective-card-stats");
  assert.match(await text(page, "retrospective-stat-photos"), /photos uploaded\s*3/i);
  await byTestId(page, "retrospective-previous").click();
  await waitForCard(page, "retrospective-card-member-since");
  await byTestId(page, "retrospective-next").click();
  await byTestId(page, "retrospective-next").click();
  await waitForCard(page, "retrospective-card-most-viewed");
  assert.equal(await text(page, "retrospective-view-count"), "321");
  await page.screenshot({ path: "/tmp/task-8.2-most-viewed-light.png" });
  await byTestId(page, "retrospective-next").click();
  await waitForCard(page, "retrospective-card-archive-tank");
  assert.match(await text(page, "retrospective-card-archive-tank"), new RegExp(`Retro tank ${stamp}`));
  await byTestId(page, "retrospective-next").click();
  await waitForCard(page, "retrospective-card-tank-today");
  assert.equal(await byTestId(page, "retrospective-next").isDisabled(), true);
  await page.screenshot({ path: "/tmp/task-8.2-tank-today-light.png" });

  // Closing returns to the Me tab and records the retrospective as seen.
  await byTestId(page, "retrospective-close").click();
  await byTestId(page, "me-name").waitFor({ timeout: 30000 });
  assert.equal(await byTestId(page, "retrospective-player").count(), 0);
  assert.deepEqual({ available: (await status(legacy)).available, seen: (await status(legacy)).seenAt !== null }, { available: true, seen: true });
  assert.equal(sql(`select count(*) from profile where id = ${legacy.profileId} and archive_retrospective_seen_at is not null`), "1");

  // A later sign-in does not open it again.
  await signOutThroughKeycloak(page);
  await signIn(page, legacyUser);
  await page.waitForTimeout(2500);
  assert.equal(await byTestId(page, "retrospective-player").count(), 0, "the retrospective does not open at a later sign-in");

  // The Me tab opens the same cards again.
  await byTestId(page, "me-archive-retrospective").click();
  await waitForCard(page, "retrospective-card-member-since");
  assert.equal(await byTestId(page, "retrospective-player").locator('[data-testid^="retrospective-progress-"]').count(), 5);
  assert.equal(await text(page, "retrospective-member-since"), "March 2012");
  for (let step = 0; step < 4; step++) await byTestId(page, "retrospective-next").click();
  await waitForCard(page, "retrospective-card-tank-today");

  // The last card leads into the creation of a tank.
  await byTestId(page, "retrospective-set-up-tank").click();
  await byTestId(page, "onboarding-yes").waitFor({ timeout: 30000 });
  await page.goto(`${WEB_URL}/me?lang=en`, { waitUntil: "networkidle" });
  await signOutThroughKeycloak(page);

  // A member without legacy data gets no retrospective and no entry.
  await signIn(page, plainUser);
  await page.waitForTimeout(2500);
  assert.equal(await byTestId(page, "retrospective-player").count(), 0);
  assert.equal(await byTestId(page, "me-archive-retrospective").count(), 0);
  await signOutThroughKeycloak(page);
  await context.close();

  assert.deepEqual(errors, []);
  console.log("TASK-8.2: the archive retrospective opens once at the first sign-in with the cards that have data, opens again from Me, and a member without legacy data gets none.");
} catch (error) {
  console.error(errors, await page?.locator("body").innerText().catch(() => ""));
  await page?.screenshot({ path: "/tmp/task-8.2-failure.png" }).catch(() => {});
  throw error;
} finally {
  await browser.close();
  cleanUp();
  for (const id of accountIds) await deleteKeycloakAccount(id).catch(() => {});
}
