// Story: TASK-8.5
// A legacy member shares the archive retrospective as a portrait image, as a member of the web app:
// - The share button on the stats card and on the last card opens a sheet with the card.
// - The card shows member since, the photo and comment counts, the most viewed photo and the app name,
//   and no handle until the member switches it on.
// - Sharing the image downloads a PNG with 9:16 proportions (the headless browser has no share options
//   for files, so the image is saved as a download).
//
// Runs on the isolated write stack only (see e2e/write-stack.sh). The spec creates one account with its
// profile and legacy content, and removes all of it at the end.
import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { chromium } from "playwright";
import {
  API_URL, WEB_URL, ZONE, byTestId, createKeycloakAccount, deleteKeycloakAccount, passwordGrantToken, sql, submitKeycloakLogin,
} from "./support.mjs";

assert.equal(process.env.E2E_DB, "cichlids_e2e", "Refusing to run outside the cichlids_e2e database");

const stamp = Date.now();
const username = `retro-share-${stamp}`;
const password = "retro-password"; // gitleaks:allow
const legacyBase = 1_900_000_000 + (stamp % 90_000_000);
const FIXTURE = path.join(path.dirname(fileURLToPath(import.meta.url)), "..", "fixtures", "upload-photo.jpg");
let accountId;
let profileId;

/**
 * Seeds the legacy content of the member: two migrated photos without files and a migrated story whose
 * cover is a published photo with real variants, so the card can draw the photo.
 */
async function seedLegacyContent(token) {
  sql(`update profile set created_at = '2012-03-10T10:00:00Z' where id = ${profileId}`);
  [2, 3].forEach(n => sql(
    `insert into media_item (legacy_id, owner_profile_id, kind, storage_key, created_at, received_at) values (${legacyBase + n}, ${profileId}, 'photo', 'private/e2e/retro-share-${stamp}-${n}.jpg', now(), now())`));
  const form = new FormData();
  form.append("file", new Blob([fs.readFileSync(FIXTURE)], { type: "image/jpeg" }), "retro-share.jpg");
  const upload = await fetch(`${API_URL}/api/uploads`, { method: "POST", headers: { Authorization: `Bearer ${token}` }, body: form });
  assert.equal(upload.status, 201, await upload.clone().text());
  const draft = await upload.json();
  // Publishing places the photo in the public bucket, whose URLs carry no CORS headers; the retrospective
  // names the photo for the card by a signed path of the API as well.
  const publish = await fetch(`${API_URL}/api/stories/${draft.id}/publish`, {
    method: "POST", headers: { Authorization: `Bearer ${token}`, "Content-Type": "application/json" }, body: JSON.stringify({ title: `Share story ${stamp}` }),
  });
  assert.equal(publish.status, 200, await publish.clone().text());
  sql(`update story set legacy_id = ${legacyBase + 10}, view_count = 321 where id = ${draft.id}`);
  sql(`update media_item set legacy_id = ${legacyBase + 1} where id in (select media_item_id from media_link where story_id = ${draft.id})`);
}

function cleanUp() {
  if (!profileId) return;
  sql(`delete from outbox_event where aggregate_id = '${profileId}' and aggregate_type = 'profile'`);
  sql(`delete from slug_alias where story_id in (select id from story where author_profile_id = ${profileId})`);
  sql(`delete from story where author_profile_id = ${profileId}`);
  sql(`delete from media_item where owner_profile_id = ${profileId}`);
  sql(`delete from profile_identity where profile_id = ${profileId}`);
  sql(`delete from profile where id = ${profileId}`);
}

/** The width and height a PNG file declares in its header. */
function pngSize(bytes) {
  assert.deepEqual([...bytes.subarray(0, 8)], [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a], "the download is a PNG file");
  return { width: bytes.readUInt32BE(16), height: bytes.readUInt32BE(20) };
}

/** The colour of one pixel of a PNG, read through a canvas of a second page. */
async function pixelOf(browser, bytes, x, y) {
  const reader = await browser.newPage();
  try {
    return await reader.evaluate(async ({ base64, x, y }) => {
      const image = new Image();
      image.src = `data:image/png;base64,${base64}`;
      await image.decode();
      const canvas = document.createElement("canvas");
      canvas.width = image.width;
      canvas.height = image.height;
      const context = canvas.getContext("2d");
      context.drawImage(image, 0, 0);
      return [...context.getImageData(x, y, 1, 1).data].slice(0, 3);
    }, { base64: bytes.toString("base64"), x, y });
  } finally {
    await reader.close();
  }
}

const waitForCard = (page, testId) => byTestId(page, testId).waitFor({ timeout: 30000 });
const cardLabel = async page => (await byTestId(page, "share-card").getAttribute("aria-label")) ?? "";

const browser = await chromium.launch({ channel: "chromium" });
const errors = [];
let page;
try {
  accountId = await createKeycloakAccount(username, password);
  const token = await passwordGrantToken(username, password);
  const headers = { Authorization: `Bearer ${token}`, "Content-Type": "application/json" };
  const me = await (await fetch(`${API_URL}/api/me`, { headers })).json();
  profileId = Number(me.profile.id);
  const handle = me.profile.username;
  assert.equal((await fetch(`${API_URL}/api/me/onboarding`, { method: "POST", headers, body: JSON.stringify({ skip: true }) })).status, 200);
  await seedLegacyContent(token);
  const retrospective = await (await fetch(`${API_URL}/api/me/retrospectives/archive`, { headers })).json();
  assert.match(retrospective.mostViewed.shareImageUrl, /^\/api\/media\/photos\//, "the share image is a signed path of the API");
  assert.ok(retrospective.mostViewed.image.medium.startsWith("http"), "the cover itself is a public bucket URL");

  const context = await browser.newContext({ locale: "en-US", timezoneId: ZONE, colorScheme: "dark", viewport: { width: 390, height: 844 }, acceptDownloads: true });
  page = await context.newPage();
  page.on("pageerror", error => errors.push(String(error)));
  // The deployed web app and API share one origin, so the browser may read a signed photo path for the
  // canvas; the test stack serves them from two origins, so the route adds the header the same origin makes
  // unnecessary (Playwright answers a missing header itself, so the refusal names another origin).
  const photoRoute = (corsHeader) => async route => {
    const response = await fetch(route.request().url());
    await route.fulfill({
      status: response.status,
      headers: { "content-type": response.headers.get("content-type") ?? "image/jpeg", "access-control-allow-origin": corsHeader ? "*" : "http://other-origin.invalid" },
      body: Buffer.from(await response.arrayBuffer()),
    });
  };
  const withCors = photoRoute(true);
  await context.route("**/api/media/photos/**", withCors);
  await page.goto(`${WEB_URL}/me?lang=en`, { waitUntil: "networkidle" });
  await submitKeycloakLogin(page, byTestId(page, "login-button"), username, password);
  await waitForCard(page, "retrospective-card-member-since");

  // The stats card offers the share button; the sheet shows the card without the handle.
  await byTestId(page, "retrospective-next").click();
  await waitForCard(page, "retrospective-card-stats");
  await byTestId(page, "retrospective-share-stats").click();
  await waitForCard(page, "share-card");
  const label = await cardLabel(page);
  assert.match(label, /cichlids/);
  assert.match(label, /March 2012/);
  assert.match(label, /Photos, 3\b/);
  assert.match(label, /321 views/);
  assert.equal(label.includes(`@${handle}`), false, "the card has no handle by default");
  const box = await byTestId(page, "share-card").boundingBox();
  assert.ok(box && Math.abs(box.width / box.height - 9 / 16) < 0.01, "the preview has 9:16 proportions");
  for (const id of ["share-card-share", "share-card-download", "share-card-close", "share-card-handle-toggle"]) {
    const target = await byTestId(page, id).boundingBox();
    assert.ok(target && target.height >= 48, `${id} is at least 48 high`);
  }
  await page.screenshot({ path: "/tmp/task-8.5-sheet-dark.png" });

  // The handle appears on the card once the member switches it on.
  await byTestId(page, "share-card-handle-toggle").click();
  assert.ok((await cardLabel(page)).includes(`@${handle}`), "the card shows the handle after the toggle");
  await byTestId(page, "share-card-handle-toggle").click();
  assert.equal((await cardLabel(page)).includes(`@${handle}`), false);

  // Sharing downloads a PNG with 9:16 proportions.
  const shared = page.waitForEvent("download", { timeout: 30000 });
  await byTestId(page, "share-card-share").click();
  const download = await shared;
  assert.equal(download.suggestedFilename(), "cichlids-retrospective.png");
  const size = pngSize(fs.readFileSync(await download.path()));
  assert.deepEqual(size, { width: 1080, height: 1920 });
  assert.ok(Math.abs(size.width / size.height - 9 / 16) < 0.001);
  await download.saveAs("/tmp/task-8.5-card.png");
  assert.match(await byTestId(page, "share-card-notice").innerText(), /saved as a download/i);

  // The photo is part of the image: its area is not the plain background of the card.
  const photoBox = await byTestId(page, "share-card-photo").boundingBox();
  const cardBox = await byTestId(page, "share-card").boundingBox();
  const photoCenter = Math.round(((photoBox.y + photoBox.height / 2 - cardBox.y) / cardBox.height) * 1920);
  const pixel = await pixelOf(browser, fs.readFileSync("/tmp/task-8.5-card.png"), 540, photoCenter);
  assert.ok(pixel.some((value, channel) => Math.abs(value - [241, 246, 248][channel]) > 12), `the photo is drawn on the card (pixel ${pixel})`);
  assert.equal(await byTestId(page, "share-card-photo-note").count(), 0, "the card has its photo");

  // A photo the server does not release to the page (a public bucket without CORS headers) leaves the
  // photo off the image, and the sheet says so.
  await context.unroute("**/api/media/photos/**", withCors);
  await context.route("**/api/media/photos/**", photoRoute(false));
  const withoutPhoto = page.waitForEvent("download", { timeout: 30000 });
  await byTestId(page, "share-card-share").click();
  assert.deepEqual(pngSize(fs.readFileSync(await (await withoutPhoto).path())), { width: 1080, height: 1920 });
  assert.match(await byTestId(page, "share-card-notice").innerText(), /no photo/i);

  // The last card offers the share button too.
  await byTestId(page, "share-card-close").click();
  await byTestId(page, "share-card").waitFor({ state: "detached", timeout: 10000 });
  for (let step = 0; step < 2; step++) await byTestId(page, "retrospective-next").click();
  await waitForCard(page, "retrospective-card-tank-today");
  await byTestId(page, "retrospective-share-last").click();
  await waitForCard(page, "share-card");
  const downloadAgain = page.waitForEvent("download", { timeout: 30000 });
  await byTestId(page, "share-card-download").click();
  assert.deepEqual(pngSize(fs.readFileSync(await (await downloadAgain).path())), { width: 1080, height: 1920 });
  await context.close();

  assert.deepEqual(errors, []);
  console.log("TASK-8.5: the archive retrospective is shared as a 1080 by 1920 PNG from the stats card and the last card; the handle is added only on request.");
} catch (error) {
  console.error(errors, await page?.locator("body").innerText().catch(() => ""));
  await page?.screenshot({ path: "/tmp/task-8.5-failure.png" }).catch(() => {});
  throw error;
} finally {
  await browser.close();
  cleanUp();
  if (accountId) await deleteKeycloakAccount(accountId).catch(() => {});
}
