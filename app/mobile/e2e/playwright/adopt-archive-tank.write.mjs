// Story: TASK-8.3
// A legacy member adopts archive tanks with suggested photos, as a member of the web app:
// - The offer on the archive tank card of the archive retrospective opens the adoption: the member
//   confirms the tank, sees the archive photos in the order of their dates, chooses two of them in an
//   order of their own and adopts the tank. The tank page opens, the photos are linked in the chosen
//   order and the tank leaves the list of archive tanks.
// - The page of an own archive tank that is not adopted offers the adoption; adopting without photos
//   works, and the offer is gone afterwards.
//
// Runs on the isolated write stack only (see e2e/write-stack.sh). The spec creates one account with its
// profile, its archive tanks and its archive photos, and removes all of it at the end.
import assert from "node:assert/strict";
import { chromium } from "playwright";
import {
  API_URL, WEB_URL, ZONE, byTestId, createKeycloakAccount, deleteKeycloakAccount, passwordGrantToken, signOutThroughKeycloak, sql, submitKeycloakLogin,
} from "./support.mjs";

assert.equal(process.env.E2E_DB, "cichlids_e2e", "Refusing to run outside the cichlids_e2e database");

const stamp = Date.now();
const username = `adopt-${stamp}`;
const password = "adopt-password"; // gitleaks:allow
const legacyBase = 1_800_000_000 + (stamp % 90_000_000);
let accountId;
let profileId;

function seedArchive() {
  sql(`update profile set created_at = '2010-01-10T10:00:00Z' where id = ${profileId}`);
  const photo = (n, takenAt, createdAt) => {
    const id = sql(
      `insert into media_item (legacy_id, owner_profile_id, kind, storage_key, created_at, received_at, taken_at_local, taken_at_offset_minutes) `
      + `values (${legacyBase + n}, ${profileId}, 'photo', 'private/e2e/adopt-${stamp}-${n}.jpg', '${createdAt}', now(), ${takenAt ? `'${takenAt}'` : "null"}, ${takenAt ? 0 : "null"}) returning id`).split("\n")[0];
    sql(`insert into media_variant (media_item_id, label, storage_key) values (${id}, 'small', 'variants/small/adopt-${stamp}-${n}.jpg')`);
    sql(`insert into media_variant (media_item_id, label, storage_key) values (${id}, 'medium', 'variants/medium/adopt-${stamp}-${n}.jpg')`);
    return Number(id);
  };
  // Sorted by date the photos are 2009 (taken), 2010 (no capture time, uploaded 2010-03-03), 2011 (taken).
  const photos = { late: photo(1, "2011-04-02 09:00:00", "2013-01-01T00:00:00Z"), early: photo(2, "2009-06-15 18:30:00", "2013-01-01T00:00:00Z"), undated: photo(3, null, "2010-03-03T12:00:00Z") };
  const tank = n => Number(sql(
    `insert into tank (legacy_id, profile_id, title, state, created_at, published_at) values (${legacyBase + 20 + n}, ${profileId}, 'Adopt tank ${n} ${stamp}', 'published', now(), now()) returning id`).split("\n")[0]);
  return { photos, first: tank(1), second: tank(2) };
}

function cleanUp() {
  if (!profileId) return;
  sql(`delete from outbox_event where aggregate_type = 'tank' and aggregate_id in (select id::text from tank where profile_id = ${profileId})`);
  sql(`delete from outbox_event where aggregate_id = '${profileId}' and aggregate_type = 'profile'`);
  sql(`delete from tank where profile_id = ${profileId}`);
  sql(`delete from media_item where owner_profile_id = ${profileId}`);
  sql(`delete from profile_identity where profile_id = ${profileId}`);
  sql(`delete from profile where id = ${profileId}`);
}

const box = async (page, testId) => {
  const rect = await byTestId(page, testId).boundingBox();
  assert.ok(rect && rect.width >= 48 && rect.height >= 48, `${testId} is at least 48 by 48`);
};

const browser = await chromium.launch({ channel: "chromium" });
const errors = [];
let page;
try {
  accountId = await createKeycloakAccount(username, password);
  const token = await passwordGrantToken(username, password);
  const headers = { Authorization: `Bearer ${token}`, "Content-Type": "application/json" };
  const me = await (await fetch(`${API_URL}/api/me`, { headers })).json();
  profileId = Number(me.profile.id);
  assert.equal((await fetch(`${API_URL}/api/me/onboarding`, { method: "POST", headers, body: JSON.stringify({ skip: true }) })).status, 200);
  const { photos, first, second } = seedArchive();

  const listed = async () => (await (await fetch(`${API_URL}/api/me/legacy-tanks`, { headers })).json()).map(tank => Number(tank.id));
  assert.deepEqual(await listed(), [first, second]);

  const context = await browser.newContext({ locale: "en-US", timezoneId: ZONE, colorScheme: "light", viewport: { width: 390, height: 844 } });
  page = await context.newPage();
  page.on("pageerror", error => errors.push(String(error)));
  await page.goto(`${WEB_URL}/me?lang=en`, { waitUntil: "networkidle" });

  // The archive retrospective opens at the first sign-in; its archive tank card leads into the adoption.
  await submitKeycloakLogin(page, byTestId(page, "login-button"), username, password);
  await byTestId(page, "retrospective-player").waitFor({ timeout: 30000 });
  await byTestId(page, "retrospective-card-member-since").waitFor({ timeout: 30000 });
  await byTestId(page, "retrospective-next").click();
  await byTestId(page, "retrospective-card-stats").waitFor({ timeout: 30000 });
  await byTestId(page, "retrospective-next").click();
  await byTestId(page, `retrospective-adopt-${first}`).waitFor({ timeout: 30000 });
  await byTestId(page, `retrospective-adopt-${first}`).click();

  // Confirm the tank.
  await byTestId(page, "adoption-confirm").waitFor({ timeout: 30000 });
  assert.equal((await byTestId(page, "adoption-tank-title").innerText()).trim(), `Adopt tank 1 ${stamp}`);
  await box(page, "adoption-confirm-action");
  await page.screenshot({ path: "/tmp/task-8.3-confirm-light.png" });
  await byTestId(page, "adoption-confirm-action").click();

  // The archive photos come in the order of their dates, each with its date.
  await byTestId(page, "adoption-grid").waitFor({ timeout: 30000 });
  const cells = await page.locator('[data-testid^="adoption-photo-"]').evaluateAll(nodes => nodes.map(node => ({ id: node.getAttribute("data-testid"), text: node.textContent })));
  assert.deepEqual(cells.map(cell => cell.id), [photos.early, photos.undated, photos.late].map(id => `adoption-photo-${id}`));
  assert.match(cells[0].text, /Jun 15, 2009/);
  assert.match(cells[1].text, /Mar 3, 2010/);
  assert.match(cells[2].text, /Apr 2, 2011/);
  await box(page, `adoption-photo-${photos.early}`);

  // The choice keeps the order of the taps; another tap takes a photo out.
  await byTestId(page, `adoption-photo-${photos.late}`).click();
  await byTestId(page, `adoption-photo-${photos.early}`).click();
  await byTestId(page, `adoption-photo-${photos.undated}`).click();
  await byTestId(page, `adoption-photo-${photos.undated}`).click();
  assert.equal((await byTestId(page, `adoption-position-${photos.late}`).innerText()).trim(), "1");
  assert.equal((await byTestId(page, `adoption-position-${photos.early}`).innerText()).trim(), "2");
  assert.equal(await byTestId(page, `adoption-position-${photos.undated}`).count(), 0);
  assert.match(await byTestId(page, "adoption-chosen").innerText(), /Chosen: 2/);
  await box(page, "adoption-adopt");
  await page.screenshot({ path: "/tmp/task-8.3-grid-light.png" });
  await page.emulateMedia({ colorScheme: "dark" });
  await page.screenshot({ path: "/tmp/task-8.3-grid-dark.png" });
  await page.emulateMedia({ colorScheme: "light" });

  await byTestId(page, "adoption-adopt").click();
  await page.waitForURL(url => url.pathname === `/tanks/${first}`, { timeout: 30000 });
  await page.getByText(`Adopt tank 1 ${stamp}`).first().waitFor({ timeout: 30000 });
  assert.equal(await byTestId(page, "archive-tank-offer").count(), 0, "an adopted tank offers no adoption");

  // The tank is adopted, the photos are linked in the chosen order, and the tank left the list.
  assert.equal(sql(`select count(*) from tank where id = ${first} and adopted_at is not null`), "1");
  assert.equal(sql(`select string_agg(media_item_id::text, ',' order by position) from media_link where tank_id = ${first} and role = 'showcase'`), `${photos.late},${photos.early}`);
  assert.equal(sql(`select string_agg(position::text, ',' order by position) from media_link where tank_id = ${first} and role = 'showcase'`), "0,1");
  assert.equal(sql(`select count(*) from outbox_event where event_type = 'tank.adopted' and aggregate_id = '${first}'`), "1");
  assert.deepEqual(await listed(), [second]);
  const series = await (await fetch(`${API_URL}/api/tanks/${first}/time-lapse`, { headers })).json();
  assert.deepEqual(series.photos.map(photo => Number(photo.mediaItemId)), [photos.early, photos.late]);
  await page.screenshot({ path: "/tmp/task-8.3-tank-light.png" });

  // The page of an own archive tank that is not adopted offers the adoption.
  await page.goto(`${WEB_URL}/tanks/${second}?lang=en`, { waitUntil: "networkidle" });
  await byTestId(page, "archive-tank-offer").waitFor({ timeout: 30000 });
  await box(page, "archive-tank-offer-adopt");
  await byTestId(page, "archive-tank-offer-adopt").click();
  await byTestId(page, "adoption-confirm").waitFor({ timeout: 30000 });
  await byTestId(page, "adoption-confirm-action").click();
  await byTestId(page, "adoption-grid").waitFor({ timeout: 30000 });
  assert.equal(await page.locator('[data-testid^="adoption-photo-"]').count(), 3, "photos linked to another tank stay suggested for this tank");
  assert.match(await byTestId(page, "adoption-chosen").innerText(), /Chosen: 0/);
  await byTestId(page, "adoption-adopt").click();
  await page.waitForURL(url => url.pathname === `/tanks/${second}`, { timeout: 30000 });
  await page.getByText(`Adopt tank 2 ${stamp}`).first().waitFor({ timeout: 30000 });
  assert.equal(await byTestId(page, "archive-tank-offer").count(), 0);
  assert.equal(sql(`select count(*) from tank where id = ${second} and adopted_at is not null`), "1");
  assert.equal(sql(`select count(*) from media_link where tank_id = ${second}`), "0");
  assert.deepEqual(await listed(), []);

  // A second adoption of an adopted tank answers 409.
  assert.equal((await fetch(`${API_URL}/api/me/legacy-tanks/${second}/adopt`, { method: "POST", headers, body: JSON.stringify({ mediaItemIds: [] }) })).status, 409);
  assert.equal(sql(`select count(*) from outbox_event where event_type = 'tank.adopted' and aggregate_id = '${second}'`), "1");

  await page.goto(`${WEB_URL}/me?lang=en`, { waitUntil: "networkidle" });
  await signOutThroughKeycloak(page);
  await context.close();

  assert.deepEqual(errors, []);
  console.log("TASK-8.3: archive tanks are adopted from the retrospective and from the tank page, with the chosen photos linked in the chosen order.");
} catch (error) {
  console.error(errors, await page?.locator("body").innerText().catch(() => ""));
  await page?.screenshot({ path: "/tmp/task-8.3-failure.png" }).catch(() => {});
  throw error;
} finally {
  await browser.close();
  cleanUp();
  if (accountId) await deleteKeycloakAccount(accountId).catch(() => {});
}
