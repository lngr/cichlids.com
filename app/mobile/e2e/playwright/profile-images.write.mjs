// Story: TASK-5.20.6
// A member picks an avatar from the media library and a header from a fresh photo. The profile, the
// comment of the member and the story show the avatar, another member cannot set a file they do not
// own, and removing an image keeps the photo in the library.
//
// Runs on the isolated write stack only (see e2e/write-stack.sh), since it writes to the database and
// the bucket.
import assert from "node:assert/strict";
import { execFileSync } from "node:child_process";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { chromium } from "playwright";
import { API_URL, WEB_URL, byTestId, passwordGrantToken, logInThroughKeycloak, signOutThroughKeycloak } from "./support.mjs";

assert.equal(process.env.E2E_DB, "cichlids_e2e");
const COMPOSE_FILE = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../../../stack/compose.yaml");

function sql(statement) {
  return execFileSync(
    "docker",
    ["compose", "-f", COMPOSE_FILE, "exec", "-T", "postgres", "psql", "-U", "cichlids", "-d", "cichlids_e2e", "-v", "ON_ERROR_STOP=1", "-t", "-A", "-c", statement],
    { stdio: ["ignore", "pipe", "pipe"] },
  ).toString().trim();
}

const token = await passwordGrantToken();
const json = { Authorization: `Bearer ${token}`, "Content-Type": "application/json" };
const auth = { Authorization: `Bearer ${token}` };
const call = async (method, url, body, headers = json) => fetch(`${API_URL}${url}`, { method, headers, body: body === undefined ? undefined : JSON.stringify(body) });

const browser = await chromium.launch();
const errors = [];
let page;
try {
  const canvasPage = await browser.newPage();
  const jpegBytes = async hue => canvasPage.evaluate(async hueValue => {
    const canvas = document.createElement("canvas");
    canvas.width = 640;
    canvas.height = 480;
    const ctx = canvas.getContext("2d");
    ctx.fillStyle = `hsl(${hueValue} 60% 40%)`;
    ctx.fillRect(0, 0, 640, 480);
    ctx.fillStyle = `hsl(${(hueValue + 90) % 360} 70% 60%)`;
    ctx.fillRect(hueValue % 300, 40, 200, 120);
    const blob = await new Promise(resolve => canvas.toBlob(resolve, "image/jpeg"));
    return [...new Uint8Array(await blob.arrayBuffer())];
  }, hue);
  const photoForm = async hue => {
    const form = new FormData();
    form.append("file", new Blob([new Uint8Array(await jpegBytes(hue))], { type: "image/jpeg" }), `profile-${hue}.jpg`);
    return form;
  };
  const upload = async hue => {
    const response = await fetch(`${API_URL}/api/me/media/photos`, { method: "POST", headers: auth, body: await photoForm(hue) });
    assert.equal(response.status, 201, await response.clone().text());
    return Number((await response.json()).id);
  };
  const profileOf = async id => (await (await fetch(`${API_URL}/api/profiles/${id}`)).json());

  const me = await (await call("GET", "/api/me")).json();
  const profileId = Number(me.profile.id);
  // Start without images, whatever earlier specs left on the shared account.
  assert.equal((await call("DELETE", "/api/me/avatar")).status, 200);
  assert.equal((await call("DELETE", "/api/me/header")).status, 200);
  const libraryPhoto = await upload(30);

  // Another member cannot set a file they do not own, and a missing file answers the same way.
  const otherToken = await passwordGrantToken("dev-mod");
  const otherHeaders = { Authorization: `Bearer ${otherToken}`, "Content-Type": "application/json" };
  const refused = await call("PUT", "/api/me/avatar", { mediaItemId: libraryPhoto }, otherHeaders);
  assert.equal(refused.status, 404);
  assert.equal((await refused.json()).code, "media_not_found");
  assert.equal(sql(`SELECT count(*) FROM media_link WHERE media_item_id = ${libraryPhoto}`), "0");

  page = await browser.newPage({ locale: "en-US", colorScheme: "light", viewport: { width: 390, height: 844 } });
  page.on("pageerror", error => errors.push(String(error)));
  await page.goto(`${WEB_URL}/me?lang=en`, { waitUntil: "networkidle" });
  await logInThroughKeycloak(page);
  await byTestId(page, "me-profile-images").click({ timeout: 20000 });
  await page.waitForURL(/\/profile-images$/, { timeout: 10000 });
  await byTestId(page, "profile-images-avatar-none").waitFor({ timeout: 20000 });
  await byTestId(page, "profile-images-header-none").waitFor({ timeout: 20000 });
  for (const id of ["profile-images-avatar-library", "profile-images-avatar-remove", "profile-images-header-library"]) {
    const box = await byTestId(page, id).boundingBox().catch(() => null);
    if (box) assert.ok(box.height >= 48, `${id} meets the touch target size`);
  }
  assert.equal(await byTestId(page, "profile-images-avatar-remove").count(), 0, "nothing to remove without an avatar");

  // The avatar comes from the media library.
  await byTestId(page, "profile-images-avatar-library").click();
  await byTestId(page, `profile-images-pick-${libraryPhoto}`).click({ timeout: 30000 });
  await byTestId(page, "profile-images-avatar-image").waitFor({ timeout: 30000 });
  assert.equal(sql(`SELECT media_item_id FROM media_link WHERE profile_id = ${profileId} AND role = 'avatar'`), String(libraryPhoto));
  const avatarUrl = (await profileOf(profileId)).avatarUrl;
  assert.ok(avatarUrl, "the profile has an avatar URL");
  assert.equal((await fetch(avatarUrl)).status, 200, "everyone can load the avatar");

  // The header comes from a fresh photo and lies in the library as well.
  const before = Number((await (await fetch(`${API_URL}/api/me/media?limit=1`, { headers: auth })).json()).total);
  await byTestId(page, "profile-images-header-upload-input").setInputFiles({ name: "header.jpg", mimeType: "image/jpeg", buffer: Buffer.from(await jpegBytes(200)) });
  for (let attempt = 0; attempt < 60 && sql(`SELECT count(*) FROM media_link WHERE profile_id = ${profileId} AND role = 'header'`) === "0"; attempt++) {
    await new Promise(resolve => setTimeout(resolve, 500));
  }
  assert.equal(sql(`SELECT count(*) FROM media_link WHERE profile_id = ${profileId} AND role = 'header'`), "1", "the upload linked the header");
  await byTestId(page, "profile-images-header-image").waitFor({ timeout: 30000 });
  assert.equal(Number((await (await fetch(`${API_URL}/api/me/media?limit=1`, { headers: auth })).json()).total), before + 1);
  const headerFile = Number(sql(`SELECT media_item_id FROM media_link WHERE profile_id = ${profileId} AND role = 'header'`));
  assert.ok(headerFile > 0 && headerFile !== libraryPhoto);
  assert.ok((await profileOf(profileId)).profileImageUrl, "the profile has a header URL");

  // Dark mode shows the same editor with its own colors.
  const lightBackground = await byTestId(page, "profile-images-avatar").evaluate(element => getComputedStyle(element.parentElement.parentElement).backgroundColor);
  await page.emulateMedia({ colorScheme: "dark" });
  await page.waitForFunction(light => getComputedStyle(document.querySelector('[data-testid="profile-images-avatar"]').parentElement.parentElement).backgroundColor !== light, lightBackground, { timeout: 10000 });
  await page.emulateMedia({ colorScheme: "light" });

  // The profile shows the header and the avatar.
  await page.goBack();
  await byTestId(page, "profile-header-image").waitFor({ timeout: 30000 });
  await byTestId(page, "profile-avatar-image").waitFor({ timeout: 30000 });

  // The story and the comment of the member show the avatar.
  const draft = await (await fetch(`${API_URL}/api/uploads`, { method: "POST", headers: auth, body: await photoForm(120) })).json();
  const story = await (await call("POST", `/api/stories/${draft.id}/publish`, { title: `Avatar story ${Date.now()}`, topic: "cichlids" })).json();
  assert.equal((await call("POST", `/api/stories/${story.slug}/comments`, { body: "Looking good" })).status, 201);
  assert.equal((await (await fetch(`${API_URL}/api/stories/${story.slug}`)).json()).author.avatarUrl, avatarUrl);
  const comments = await (await fetch(`${API_URL}/api/stories/${story.slug}/comments`)).json();
  assert.equal(comments.items[0].author.avatarUrl, avatarUrl);

  // Removing the avatar keeps the photo in the library.
  await page.goto(`${WEB_URL}/profile-images?lang=en`, { waitUntil: "networkidle" });
  await byTestId(page, "profile-images-avatar-remove").click({ timeout: 30000 });
  await byTestId(page, "profile-images-avatar-none").waitFor({ timeout: 30000 });
  assert.equal(sql(`SELECT count(*) FROM media_link WHERE profile_id = ${profileId} AND role = 'avatar'`), "0");
  assert.equal(sql(`SELECT count(*) FROM media_item WHERE id = ${libraryPhoto}`), "1");
  assert.equal((await profileOf(profileId)).avatarUrl, null);
  assert.ok((await profileOf(profileId)).profileImageUrl, "the header stays");

  // The header goes too, and the session ends.
  await byTestId(page, "profile-images-header-remove").click({ timeout: 30000 });
  await byTestId(page, "profile-images-header-none").waitFor({ timeout: 30000 });
  assert.equal(sql(`SELECT count(*) FROM media_link WHERE profile_id = ${profileId}`), "0");
  assert.equal(sql(`SELECT count(*) FROM media_item WHERE id = ${headerFile}`), "1");
  assert.equal((await fetch(`${API_URL}/api/me/avatar`, { method: "PUT" })).status, 401);

  await page.goto(`${WEB_URL}/me`, { waitUntil: "networkidle" });
  await signOutThroughKeycloak(page);
  assert.deepEqual(errors, []);
  console.log("TASK-5.20.6: a member sets avatar and header from the library and a fresh photo, everyone sees them, removing keeps the photos");
} catch (error) {
  console.error(errors, await page?.locator("body").innerText());
  throw error;
} finally {
  await browser.close();
}
